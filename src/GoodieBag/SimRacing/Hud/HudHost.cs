using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using GoodieBag.Config;
using GoodieBag.Services;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;
using WinForms = System.Windows.Forms;

namespace GoodieBag.SimRacing.Hud;

internal sealed record HudWidgetStats(string Id, string Name, bool OnScreen, double DrawMs, double PerSecond);

internal sealed class HudHost : IDisposable
{
    private const string Consumer = "iRacing Overlay";

    private readonly AppServices _app;
    private readonly WinForms.Timer _timer = new();
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private sealed record Places(HudLayout? Layout, List<WidgetPlace> Items);
    private readonly List<Places> _undo = new(), _redo = new();
    private readonly Dictionary<HudLayout, Places> _editStart = new(ReferenceEqualityComparer.Instance);
    private HudToolbar? _toolbar;
    private bool _toolbarDirty;
    private bool _active, _holding, _hidden, _editing, _editSimulated, _preview, _overrideRules;
    private string _layoutName = "";
    private (bool Connected, SessionKind Kind, string? Car, int? Track, int Num) _sessionKey;
    private IReadOnlyList<HudMonitor>? _monitors;

    private sealed class Slot
    {
        public required HudWidgetConfig Config;
        public required HudWidgetDef Def;
        public required HudWidget Widget;
        public required HudOptions Options;
        public HudWindow? Window;
        public HudPainter? Painter;
        public long LastSeq = -1;
        public long LastDraw;
        public bool Dirty = true;
        public double DrawMs;
        public int Draws;
        public long CountSince = Stopwatch.GetTimestamp();
        public double PerSecond;
        public bool Empty;
    }

    private readonly record struct WidgetPlace(string Id, string Monitor, HudAnchor Anchor, double X, double Y, double W, double H);

    public HudHost(AppServices app)
    {
        _app = app;
        Race = new RaceState(() => Settings);
        _timer.Tick += (_, _) => SafeTick();
        app.Telemetry.StatusChanged += OnStatusChanged;
        app.Telemetry.SimStarted += OnSimStarted;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        Theme.ThemeManager.Changed += OnThemeChanged;
    }

    private void OnStatusChanged(SimStatus status)
    {
        if (_holding && !_timer.Enabled) SafeTick();
        Changed?.Invoke();
    }

    private void OnSimStarted()
    {
        if (_app.Demo || !(_preview || _editing && _editSimulated)) return;
        _preview = false;
        _editSimulated = false;
        Refresh();
        _app.Notify("iRacing started", "Simulated data is off: the widgets show the live session.", NoticeKind.Info);
        Changed?.Invoke();
    }

    private void SafeTick()
    {
        try { Tick(); }
        catch (Exception ex) { LogOnce("tick", ex); }
    }

    public RaceHudSettings Settings => _app.Config.SimRacing.Hud;
    public RaceState Race { get; }
    public TelemetryService Telemetry => _app.Telemetry;

    public event Action? Changed;

    public event Action<string>? WidgetPicked;

    public string LayoutName => _layoutName;

    public HudLayoutRule? MatchedRule { get; private set; }

    public bool Editing => _editing;
    public bool Preview => _preview;
    public bool TemporarilyHidden => _hidden;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public bool OnScreen => _slots.Values.Any(s => s.Window?.Visible == true);

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (!active)
        {
            if (_editing) EndEdit(keep: true);
            _preview = false;
        }
        Refresh();
    }

    public void SettingsChanged(bool save = true)
    {
        if (save) Save();
        foreach (var s in _slots.Values) s.Dirty = true;
        Refresh();
    }

    public void SetEnabled(bool on)
    {
        Settings.Enabled = on;
        _hidden = false;
        SettingsChanged();
    }

    public void ToggleHidden()
    {
        if (!Settings.Enabled) { SetEnabled(true); return; }
        _hidden = !_hidden;
        Refresh();
    }

    public void SetPreview(bool on)
    {
        if (on && _app.Telemetry.Status == SimStatus.Connected && !_app.Telemetry.Simulated)
        {
            _app.Notify("iRacing is running", "The widgets already show the live session, so nothing is simulated.", NoticeKind.Info);
            return;
        }
        _preview = on;
        Refresh();
    }

    public void UseLayout(string name)
    {
        if (Settings.Layout(name) is null) return;
        Settings.ActiveLayout = Settings.Layout(name)!.Name;
        _overrideRules = true;
        SettingsChanged();
    }

    public void NextLayout() => StepLayout(1);

    public void PreviousLayout() => StepLayout(-1);

    private void StepLayout(int delta)
    {
        var list = Settings.Layouts;
        if (list.Count == 0) return;
        int i = list.FindIndex(l => string.Equals(l.Name, _layoutName, StringComparison.OrdinalIgnoreCase));
        if (i < 0) i = delta > 0 ? -1 : 0;
        UseLayout(list[((i + delta) % list.Count + list.Count) % list.Count].Name);
        _app.Notify("iRacing Overlay layout applied", _layoutName, NoticeKind.Success);
    }

    public void DismissAlerts()
    {
        Race.Alerts.DismissAll();
        foreach (var s in _slots.Values) s.Dirty = true;
    }

    private int _watchers;

    public void Watch(bool on)
    {
        _watchers = Math.Max(0, _watchers + (on ? 1 : -1));
        Refresh();
    }

    private bool WantsTelemetry => _active && (Settings.Enabled && !_app.Demo || _preview || _editing || _watchers > 0);

    private void Refresh()
    {
        bool want = WantsTelemetry;
        if (want != _holding)
        {
            _holding = want;
            if (want) _app.Telemetry.Acquire(Consumer);
            else _app.Telemetry.Release(Consumer);
        }
        _app.Telemetry.Simulated = _app.Demo || _preview || _editing && _editSimulated;
        if (!want)
        {
            _timer.Stop();
            CloseWindows();
            Changed?.Invoke();
            return;
        }
        ResolveLayout(force: true);
        SafeTick();
        Changed?.Invoke();
    }

    private void ResolveLayout(bool force)
    {
        var f = Race.Frame;
        var player = f.Session.Player;
        bool connected = f.Status == SimStatus.Connected;
        var key = (connected, connected ? f.SessionKind : SessionKind.Other, player?.CarPath, f.Session.TrackId, f.SessionNum);
        if (!force && key == _sessionKey) return;
        string session = connected ? f.SessionKind.ToString() : "";
        if (key != _sessionKey)
        {
            _overrideRules = false;
            if (session.Length > 0 && !f.Simulated && Settings.Remember(player?.ClassName, player?.CarName, f.Session.TrackDisplayName))
                Save();
        }
        _sessionKey = key;
        HudLayout? layout;
        MatchedRule = null;
        if (!_overrideRules && Settings.AutoSwitch && session.Length > 0)
        {
            layout = Settings.Resolve(session, player?.CarName ?? "", f.Session.TrackDisplayName, player?.ClassName ?? "");
            MatchedRule = Settings.Rules.Where(r => Settings.Layout(r.Layout) == layout && r.Matches(session, player?.CarName ?? "", f.Session.TrackDisplayName, player?.ClassName ?? ""))
                .OrderByDescending(r => r.Specificity).FirstOrDefault();
        }
        else layout = Settings.Layout(Settings.ActiveLayout) ?? Settings.Layouts.FirstOrDefault();
        string name = layout?.Name ?? "";
        if (!force && name == _layoutName) return;
        bool changed = name != _layoutName;
        _layoutName = name;
        NoteEditStart();
        Sync(layout);
        if (changed) Changed?.Invoke();
    }

    private void Sync(HudLayout? layout)
    {
        var widgets = layout?.Widgets ?? new List<HudWidgetConfig>();
        foreach (var id in _slots.Keys.ToList())
        {
            var s = _slots[id];
            if (!widgets.Any(w => ReferenceEquals(w, s.Config)))
            {
                Close(s);
                _slots.Remove(id);
            }
        }
        foreach (var w in widgets)
        {
            if (_slots.TryGetValue(w.Id, out var existing) && ReferenceEquals(existing.Config, w))
            {
                existing.Dirty = true;
                continue;
            }
            if (_slots.Remove(w.Id, out var stale)) Close(stale);
            if (HudCatalog.Find(w.Kind) is not { } def) continue;
            _slots[w.Id] = new Slot { Config = w, Def = def, Widget = def.Create(), Options = new HudOptions(def, w) };
        }
    }

    private void Tick()
    {
        if (!_holding) return;
        var frame = _app.Telemetry.Take();
        Race.Update(frame);
        ResolveLayout(force: false);
        bool show = !_app.Demo && (_editing || _preview || Settings.Enabled && !_hidden && ShowWhen(frame));
        int fastest = 0;
        long now = Stopwatch.GetTimestamp();
        foreach (var s in _slots.Values)
        {
            bool visible = show && s.Config.Enabled && (_editing || frame.Status != SimStatus.Connected
                || s.Config.ShowsIn(frame.SessionKind.ToString()) && !(s.Config.HideInReplay && frame.IsReplayPlaying) && !(s.Config.OnlyOnTrack && !frame.Driving));
            if (!visible)
            {
                if (s.Window?.Visible == true) { s.Window.Hide(); s.Window.ReleaseSurface(); }
                continue;
            }
            int hz = s.Config.UpdateHz > 0 ? s.Config.UpdateHz : s.Def.DefaultHz;
            fastest = Math.Max(fastest, hz);
            bool due = Stopwatch.GetElapsedTime(s.LastDraw, now).TotalMilliseconds >= 1000.0 / hz - 4;
            if (!_editing && frame.Status == SimStatus.Connected && (s.Config.OnlyWhenNeeded ?? s.Def.HideWhenEmpty) == true)
            {
                if (s.Dirty || due && s.LastSeq != frame.Sequence)
                {
                    try { s.Empty = !s.Widget.HasContent(new HudView { Frame = frame, Race = Race, Settings = Settings, Config = s.Config, Options = s.Options, Def = s.Def }); }
                    catch (Exception ex) { LogOnce(s.Def.Kind, ex); s.Empty = true; }
                    if (s.Empty) { s.Dirty = false; s.LastSeq = frame.Sequence; s.LastDraw = Stopwatch.GetTimestamp(); }
                }
                if (s.Empty)
                {
                    if (s.Window?.Visible == true) { s.Window.Hide(); s.Window.ReleaseSurface(); }
                    continue;
                }
            }
            if (s.Dirty || due && s.LastSeq != frame.Sequence) Draw(s, frame);
        }
        if (_toolbarDirty && _toolbar is not null) { _toolbarDirty = false; _toolbar.Draw(this); }
        if (fastest == 0 && !_editing && !_preview && frame.Status == SimStatus.NotRunning)
        {
            _timer.Stop();
            return;
        }
        int interval = fastest > 0 ? Math.Clamp(1000 / fastest, 16, 250) : 250;
        if (_timer.Interval != interval) _timer.Interval = interval;
        if (!_timer.Enabled) _timer.Start();
    }

    private bool ShowWhen(TelemetryFrame f) => Settings.ShowWhen switch
    {
        HudShowWhen.Always => true,
        HudShowWhen.Connected => f.Status == SimStatus.Connected,
        _ => f.Driving,
    };

    private HudPalette Palette => HudPalette.Of(Settings.Appearance);

    private void Draw(Slot s, TelemetryFrame frame)
    {
        long start = Stopwatch.GetTimestamp();
        var monitor = HudGeometry.MonitorFor(s.Config.Monitor, Monitors);
        var rect = s.Window is { } w0 && w0.Visible && _dragging == s ? w0.Rect : HudGeometry.Place(s.Config, monitor);
        if (s.Window is null)
        {
            var w = new HudWindow("Goodie Bag · " + s.Def.Name) { Tag = s.Config.Id };
            w.Dragging += OnDragging;
            w.Dropped += OnDropped;
            w.Picked += win => WidgetPicked?.Invoke((string)win.Tag!);
            w.Clicked += OnClicked;
            s.Window = w;
            s.Painter = new HudPainter();
        }
        var window = s.Window;
        window.Rect = rect;
        window.Editing = _editing;
        window.Locked = s.Config.Locked;
        window.Grip = 14 * monitor.Scale;
        window.ClickThrough = !_editing && !Settings.Interactive;
        var size = new Size(Math.Max(1, (int)Math.Round(rect.Width)), Math.Max(1, (int)Math.Round(rect.Height)));
        try
        {
            var g = window.Surface(size);
            var painter = s.Painter!;
            float scale = size.Width / (float)s.Config.W;
            painter.Simulated = frame.Simulated;
            painter.Begin(g, scale, new SizeF((float)s.Config.W, (float)s.Config.H), Palette, Settings.Appearance);
            painter.Background = s.Config.Background;
            var view = new HudView { Frame = frame, Race = Race, Settings = Settings, Config = s.Config, Options = s.Options, Def = s.Def };
            s.Widget.Draw(painter, view);
            if (_editing) EditFrame(painter, s, _picked == s.Config.Id);
            if (!window.Visible) window.Show();
            window.Present(new Point((int)Math.Round(rect.X), (int)Math.Round(rect.Y)), (byte)Math.Round(s.Config.Opacity * 2.55));
            if (++_presents % 20 == 0) window.KeepOnTop();
        }
        catch (Exception ex)
        {
            LogOnce(s.Def.Kind, ex);
            if (window.Visible) { window.Hide(); window.ReleaseSurface(); }
        }
        s.Dirty = false;
        s.LastSeq = frame.Sequence;
        s.LastDraw = Stopwatch.GetTimestamp();
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        s.DrawMs = s.DrawMs * 0.9 + ms * 0.1;
        s.Draws++;
        double since = Stopwatch.GetElapsedTime(s.CountSince).TotalSeconds;
        if (since >= 2) { s.PerSecond = s.Draws / since; s.Draws = 0; s.CountSince = Stopwatch.GetTimestamp(); }
    }

    private int _presents;
    private readonly HashSet<string> _logged = new();

    private void LogOnce(string kind, Exception ex)
    {
        if (_logged.Add(kind)) App.LogError(ex);
    }

    private static void EditFrame(HudPainter p, Slot s, bool picked)
    {
        var b = p.Bounds;
        p.Fill(b, HudPalette.Alpha(p.P.AccentSoft, 70));
        p.Outline(new RectangleF(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2), picked ? p.P.Accent : p.P.AccentText, picked ? 3 : 1.5f, p.Look.Corner);
        string label = s.Def.Name + (s.Config.Locked ? " · locked" : "");
        float w = Math.Min(b.Width - 8, p.Measure(label, p.SmallBold) + 14);
        var tag = new RectangleF(4, 4, w, p.LineHeight(p.SmallBold) + 6);
        p.Fill(tag, p.P.Accent, 4);
        p.Text(label, p.SmallBold, p.P.OnAccent, tag, HAlign.Center, VAlign.Center);
        if (!s.Config.Locked)
            for (int i = 0; i < 3; i++)
            {
                float o = 4 + i * 4;
                p.Line(b.Right - o, b.Bottom - 3, b.Right - 3, b.Bottom - o, p.P.AccentText, 1.5f);
            }
    }

    private void CloseWindows()
    {
        foreach (var s in _slots.Values) Close(s);
        _toolbar?.Close();
    }

    private static void Close(Slot s)
    {
        s.Window?.Hide();
        s.Window?.Dispose();
        s.Window = null;
        s.Painter?.Dispose();
        s.Painter = null;
        s.Dirty = true;
    }

    public IReadOnlyList<HudWidgetStats> Stats() =>
        _slots.Values.Select(s => new HudWidgetStats(s.Config.Id, s.Def.Name, s.Window?.Visible == true, s.DrawMs, s.PerSecond)).ToList();

    public IReadOnlyList<HudMonitor> Monitors => _monitors ??= ReadMonitors();

    public static IReadOnlyList<HudMonitor> ReadMonitors()
    {
        var list = new List<HudMonitor>();
        foreach (var screen in WinForms.Screen.AllScreens.OrderBy(s => s.Bounds.X))
        {
            var b = screen.Bounds;
            list.Add(new HudMonitor(screen.DeviceName, new RectangleF(b.X, b.Y, b.Width, b.Height), DpiScale(b), screen.Primary));
        }
        return list;
    }

    public static IEnumerable<(string Id, string Label)> MonitorChoices()
    {
        yield return ("primary", "Primary monitor");
        int n = 1;
        foreach (var m in ReadMonitors())
            yield return (m.Id, $"Monitor {n++} ({m.Bounds.Width:0}×{m.Bounds.Height:0}{(m.Primary ? ", primary" : "")})");
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _monitors = null;
            _toolbarDirty = true;
            foreach (var s in _slots.Values) s.Dirty = true;
            if (_holding) SafeTick();
        });
    }

    private void OnThemeChanged()
    {
        foreach (var s in _slots.Values) s.Dirty = true;
    }

    private static float DpiScale(Rectangle bounds)
    {
        try
        {
            var center = new POINT { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 };
            IntPtr monitor = MonitorFromPoint(center, 2);
            if (GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0)
                return dpi / 96f;
        }
        catch { }
        return 1f;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    private Slot? _dragging;
    private string? _picked;

    public void BeginEdit(bool toolbar = true)
    {
        if (_editing || _app.Demo) return;
        _editing = true;
        _editSimulated = _app.Telemetry.Status != SimStatus.Connected || _app.Telemetry.Simulated;
        _undo.Clear();
        _redo.Clear();
        _editStart.Clear();
        NoteEditStart();
        if (toolbar)
        {
            _toolbar = new HudToolbar();
            _toolbar.Command += OnToolbar;
            _toolbarDirty = true;
        }
        foreach (var s in _slots.Values) s.Dirty = true;
        Refresh();
    }

    public void EndEdit(bool keep)
    {
        if (!_editing) return;
        if (!keep)
            foreach (var start in _editStart.Values) Restore(start);
        _editing = false;
        _editStart.Clear();
        _dragging = null;
        _picked = null;
        _toolbar?.Close();
        _toolbar = null;
        _undo.Clear();
        _redo.Clear();
        Save();
        foreach (var s in _slots.Values) s.Dirty = true;
        Refresh();
    }

    public void Pick(string? id)
    {
        _picked = id;
        foreach (var s in _slots.Values) s.Dirty = true;
        if (_holding) SafeTick();
    }

    public void ToggleSnap() => SetSnap(!Settings.Snap);

    public void SetSnap(bool on)
    {
        if (Settings.Snap == on) return;
        Settings.Snap = on;
        if (on && (Settings.Layout(_layoutName) ?? Settings.Layout(Settings.ActiveLayout)) is { Widgets.Count: > 0 } layout)
        {
            if (_editing) RecordUndo();
            HudGeometry.SnapAll(layout.Widgets, Monitors, Settings.GridSize);
            foreach (var s in _slots.Values) s.Dirty = true;
            if (_holding) SafeTick();
        }
        Save();
        _toolbarDirty = true;
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(Snapshot());
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        _toolbarDirty = true;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(Snapshot());
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        _toolbarDirty = true;
        Changed?.Invoke();
    }

    public void RecordUndo()
    {
        _undo.Add(Snapshot());
        if (_undo.Count > 50) _undo.RemoveAt(0);
        _redo.Clear();
        _toolbarDirty = true;
    }

    private Places Snapshot()
    {
        var layout = Settings.Layout(_layoutName);
        return new(layout, (layout?.Widgets ?? new()).Select(w => new WidgetPlace(w.Id, w.Monitor, w.Anchor, w.X, w.Y, w.W, w.H)).ToList());
    }

    private void NoteEditStart()
    {
        if (_editing && Settings.Layout(_layoutName) is { } layout && !_editStart.ContainsKey(layout))
            _editStart[layout] = Snapshot();
    }

    private void Restore(Places places)
    {
        var widgets = places.Layout?.Widgets ?? new();
        foreach (var p in places.Items)
            if (widgets.FirstOrDefault(w => w.Id == p.Id) is { } w)
                (w.Monitor, w.Anchor, w.X, w.Y, w.W, w.H) = (p.Monitor, p.Anchor, p.X, p.Y, p.W, p.H);
        foreach (var s in _slots.Values) s.Dirty = true;
        Save();
        if (_holding) SafeTick();
    }

    private void OnDragging(HudWindow window, RectangleF proposed, bool resizing)
    {
        if (!_slots.TryGetValue((string)window.Tag!, out var s)) return;
        if (_dragging != s) { RecordUndo(); _dragging = s; }
        var monitor = HudGeometry.MonitorAt(new PointF(proposed.X + proposed.Width / 2, proposed.Y + proposed.Height / 2), Monitors);
        float minW = (float)s.Def.MinW * monitor.Scale, minH = (float)s.Def.MinH * monitor.Scale;
        if (proposed.Width < minW) proposed.Width = minW;
        if (proposed.Height < minH) proposed.Height = minH;
        if (Settings.Snap && !HudWindow.Free)
            proposed = HudGeometry.Snap(proposed, Others(s, monitor), monitor.Bounds, Settings.GridSize * monitor.Scale, 10 * monitor.Scale, resizing);
        proposed = HudGeometry.Clamp(proposed, monitor.Bounds);
        window.Rect = proposed;
        HudGeometry.Store(s.Config, proposed, monitor);
        s.Dirty = true;
        Draw(s, Race.Frame);
    }

    private void OnDropped(HudWindow window)
    {
        if (!_slots.TryGetValue((string)window.Tag!, out var s)) return;
        var monitor = HudGeometry.MonitorAt(new PointF(window.Rect.X + window.Rect.Width / 2, window.Rect.Y + window.Rect.Height / 2), Monitors);
        var rect = window.Rect;
        if (!HudWindow.Free) rect = HudGeometry.Resolve(rect, Others(s, monitor).ToList(), monitor.Bounds);
        HudGeometry.Store(s.Config, rect, monitor);
        _dragging = null;
        s.Dirty = true;
        Save();
        Draw(s, Race.Frame);
        Changed?.Invoke();
    }

    private IEnumerable<RectangleF> Others(Slot s, HudMonitor monitor) =>
        _slots.Values.Where(o => o != s && o.Window?.Visible == true && HudGeometry.MonitorFor(o.Config.Monitor, Monitors).Id == monitor.Id)
            .Select(o => o.Window!.Rect);

    private void OnClicked(HudWindow window, PointF at)
    {
        if (!_slots.TryGetValue((string)window.Tag!, out var s)) return;
        float scale = window.Rect.Width / (float)s.Config.W;
        var view = new HudView { Frame = Race.Frame, Race = Race, Settings = Settings, Config = s.Config, Options = s.Options, Def = s.Def };
        if (s.Widget.Click(new PointF(at.X / scale, at.Y / scale), view))
            foreach (var x in _slots.Values) x.Dirty = true;
    }

    private void Save()
    {
        if (HudPresets.Fork(Settings) is { } custom)
        {
            if (RaceHudSettings.IsDefaultName(_layoutName)) _layoutName = custom;
            Changed?.Invoke();
        }
        _app.Save();
    }

    public void ResetLayout(string name)
    {
        if (_editing) EndEdit(keep: true);
        Settings.ResetToDefault(name, HudPresets.DefaultLayouts()[0]);
        _app.Save();
        foreach (var s in _slots.Values) s.Dirty = true;
        Refresh();
    }

    private void OnToolbar(string command)
    {
        switch (command)
        {
            case "undo": Undo(); break;
            case "redo": Redo(); break;
            case "snap": ToggleSnap(); break;
            case "cancel": EndEdit(keep: false); break;
            case "done": EndEdit(keep: true); break;
        }
        _toolbarDirty = true;
    }

    private readonly List<HudWindow> _gridWindows = new();
    private System.Windows.Threading.DispatcherTimer? _gridTimer;

    public void ShowGrid()
    {
        if (_app.Demo || !_active) return;
        var monitors = ReadMonitors();
        while (_gridWindows.Count < monitors.Count) _gridWindows.Add(new HudWindow("Goodie Bag · grid"));
        var palette = Palette;
        var accent = System.Windows.Application.Current?.TryFindResource("Accent") is System.Windows.Media.SolidColorBrush b
            ? Color.FromArgb(b.Color.R, b.Color.G, b.Color.B) : palette.Accent;
        for (int i = 0; i < _gridWindows.Count; i++)
        {
            var w = _gridWindows[i];
            if (i >= monitors.Count) { w.Hide(); w.ReleaseSurface(); continue; }
            var m = monitors[i];
            var size = new Size((int)m.Bounds.Width, (int)m.Bounds.Height);
            try
            {
                var g = w.Surface(size);
                DrawGrid(g, size, Settings.GridSize * m.Scale, Settings.GridSize, palette, accent);
                if (!w.Visible) w.Show();
                w.Present(new Point((int)m.Bounds.X, (int)m.Bounds.Y));
                w.KeepOnTop();
            }
            catch (Exception ex) { LogOnce("grid", ex); }
        }
        if (_gridTimer is null)
        {
            _gridTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _gridTimer.Tick += (_, _) => HideGrid();
        }
        _gridTimer.Stop();
        _gridTimer.Start();
    }

    private static void DrawGrid(Graphics g, Size size, float step, int setting, HudPalette p, Color accent)
    {
        g.ResetTransform();
        g.Clear(Color.FromArgb(70, 0, 0, 0));
        step = Math.Max(2, step);
        using var minor = new Pen(Color.FromArgb(110, accent), 1);
        using var major = new Pen(Color.FromArgb(200, accent), 1);
        int n = 0;
        for (float x = 0; x < size.Width; x += step, n++) g.DrawLine(n % 4 == 0 ? major : minor, x, 0, x, size.Height);
        n = 0;
        for (float y = 0; y < size.Height; y += step, n++) g.DrawLine(n % 4 == 0 ? major : minor, 0, y, size.Width, y);
        string text = $"Snap grid · {setting} px";
        using var font = new Font("Segoe UI Semibold", 26, FontStyle.Regular, GraphicsUnit.Pixel);
        var box = g.MeasureString(text, font);
        var r = new RectangleF((size.Width - box.Width) / 2 - 18, (size.Height - box.Height) / 2 - 10, box.Width + 36, box.Height + 20);
        using (var bg = new SolidBrush(Color.FromArgb(235, p.Surface))) g.FillRectangle(bg, r);
        using (var edge = new Pen(accent, 2)) g.DrawRectangle(edge, r.X, r.Y, r.Width, r.Height);
        using var ink = new SolidBrush(p.Heading);
        g.DrawString(text, font, ink, r.X + 18, r.Y + 10);
    }

    private void HideGrid()
    {
        _gridTimer?.Stop();
        foreach (var w in _gridWindows) { w.Hide(); w.Dispose(); }
        _gridWindows.Clear();
    }

    public Bitmap RenderPreview(HudWidgetConfig config, RaceState race, HudPainter painter, float scale)
    {
        Bitmap? bitmap = null;
        RenderPreview(config, race, painter, scale, ref bitmap);
        return bitmap!;
    }

    public void RenderPreview(HudWidgetConfig config, RaceState race, HudPainter painter, float scale, ref Bitmap? bitmap)
    {
        var def = HudCatalog.Find(config.Kind) ?? throw new InvalidOperationException("Unknown widget " + config.Kind);
        int w = Math.Max(1, (int)Math.Round(config.W * scale)), h = Math.Max(1, (int)Math.Round(config.H * scale));
        if (bitmap is null || bitmap.Width != w || bitmap.Height != h)
        {
            bitmap?.Dispose();
            bitmap = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        }
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        painter.Simulated = race.Frame.Simulated;
        painter.MarkSimulated = false;
        painter.IsPreview = true;
        painter.Begin(g, scale, new SizeF((float)config.W, (float)config.H), Palette, Settings.Appearance);
        painter.Background = config.Background;
        var view = new HudView { Frame = race.Frame, Race = race, Settings = Settings, Config = config, Options = new HudOptions(def, config), Def = def };
        string key = config.Id + "|" + config.Kind;
        if (!_previewWidgets.TryGetValue(key, out var widget)) _previewWidgets[key] = widget = def.Create();
        widget.Draw(painter, view);
    }

    private readonly Dictionary<string, HudWidget> _previewWidgets = new();

    public void Dispose()
    {
        try { SetActive(false); }
        catch (Exception ex) { App.LogError(ex); }
        _app.Telemetry.Simulated = _app.Demo;
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        Theme.ThemeManager.Changed -= OnThemeChanged;
        _app.Telemetry.StatusChanged -= OnStatusChanged;
        _app.Telemetry.SimStarted -= OnSimStarted;
        _active = false;
        _editing = false;
        _preview = false;
        _timer.Stop();
        _timer.Dispose();
        HideGrid();
        CloseWindows();
        _toolbar = null;
        if (_holding) { _holding = false; _app.Telemetry.Release(Consumer); }
    }
}

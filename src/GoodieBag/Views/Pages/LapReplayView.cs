using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.SessionStats;

namespace GoodieBag.Views.Pages;

internal sealed class ReplayMemory
{
    public double Distance;
    public string? Follow;
}

internal sealed class ReplayCar
{
    public required AnalysisLine Line { get; init; }
    public required double[] X { get; init; }
    public required double[] Y { get; init; }
    public required double[] Headings { get; init; }
    public double Distance, Px, Py, Heading, Yaw;
    public LapTrace Trace => Line.Trace;
}

internal sealed class LapReplayView : Grid
{
    private const double MinStage = 420, MaxStage = 900;
    private const long ReadoutMs = 66;
    private readonly AnalysisState _s;
    private readonly Config.SessionStatsSettings _settings;
    private readonly ReplayMemory _memory;
    private readonly Action _save;
    private readonly List<ReplayCar> _cars = new();
    private readonly List<ReplayCard> _cards = new();
    private readonly double[] _sectorStarts;
    private readonly ReplayStage _stage = null!;
    private readonly ReplayMiniMap _mini = null!;
    private readonly ReplayScrubber _scrubber = null!;
    private readonly ReplayWind? _wind;
    private readonly Button _play = null!;
    private readonly TextBlock _clock = new(), _gapSeconds = new(), _gapMetres = new(), _sectorName = new(), _sectorGap = new();
    private readonly SectorTable? _table;
    private readonly Slider _zoom = null!;
    private readonly FrameworkElement _camera = null!;
    private readonly Button _turn = null!;
    private readonly CheckBox _refSwitch = null!;
    private readonly Tile _stageTile = null!;
    private readonly Tile _barTile = null!;
    private ScrollViewer? _page;
    private ReplayCar _follow = null!;
    private double _time;
    private bool _playing, _hooked;
    private TimeSpan? _lastFrame;
    private long _lastReadout;
    private int _gapSign = 2, _sectorSign = 2, _sector = -1;

    public LapReplayView(AnalysisData data, AnalysisState s, Config.SessionStatsSettings settings, ReplayMemory memory, Action save)
    {
        _s = s;
        _settings = settings;
        _memory = memory;
        _save = save;
        _sectorStarts = new[] { 0.0 }.Concat(s.SectorDistances).ToArray();
        foreach (var line in s.Lines)
        {
            if (line.Line is not { } xy) continue;
            _cars.Add(new ReplayCar { Line = line, X = xy.X, Y = xy.Y, Headings = LapReplay.Headings(xy.X, xy.Y, 6) });
        }
        if (_cars.Count == 0)
        {
            Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Child = Ui.Text("These laps have no position data, so they can't be replayed.", "Muted") });
            return;
        }
        _follow = _cars.FirstOrDefault(c => c.Line.Label == memory.Follow) ?? _cars.FirstOrDefault(c => !c.Line.Reference) ?? _cars[0];
        _time = _follow.Trace.TimeAt(memory.Distance);
        Focusable = true;
        FocusVisualStyle = null;

        for (int r = 0; r < 2; r++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _stage = new ReplayStage(_cars, s, settings, () => _follow, data.Session.Car);
        if (data.Map is { } map)
        {
            var lines = _cars.Select(c => (c.X, c.Y, c.Trace.StepM, c.Trace.LengthM)).ToList();
            _ = PlaceMapAsync(map, lines);
        }
        _stage.ZoomStep += delta => SetZoom(_settings.ReplayZoom + delta);
        _stage.WentFree += () => { _save(); SyncCamera(); };
        var overlay = new Grid { Margin = new Thickness(14) };

        var cards = new ReplayCardRow { VerticalAlignment = VerticalAlignment.Top };
        foreach (var car in _cars.OrderBy(c => c.Line.Reference ? 1 : 0))
        {
            var card = new ReplayCard(car, settings.Imperial, () => Follow(car));
            _cards.Add(card);
            cards.Children.Add(card);
        }
        var track = _cars.FirstOrDefault(c => c.Line.Reference) ?? _follow;
        _mini = new ReplayMiniMap((track.X, track.Y), track.Trace.StepM, _sectorStarts) { Width = 230, Height = 150 };
        _sectorName.Style = Ui.Res<Style>("Body");
        _sectorName.FontWeight = FontWeights.SemiBold;
        _sectorName.HorizontalAlignment = HorizontalAlignment.Center;
        _sectorGap.Style = Ui.Res<Style>("Body");
        _sectorGap.FontWeight = FontWeights.SemiBold;
        _sectorGap.HorizontalAlignment = HorizontalAlignment.Center;
        Typography.SetNumeralAlignment(_sectorGap, FontNumeralAlignment.Tabular);
        var miniFoot = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        miniFoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        miniFoot.ColumnDefinitions.Add(new ColumnDefinition());
        miniFoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sectorText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _sectorName, _sectorGap } };
        sectorText.ToolTip = "Time gained or lost on the reference since this sector began";
        Grid.SetColumn(sectorText, 1);
        miniFoot.Children.Add(sectorText);
        if (_sectorStarts.Length > 1)
        {
            var prev = Ui.IconButton("I.ChevronLeft", "Previous sector", () => StepSector(-1), "QuietIconButton");
            var next = Ui.IconButton("I.ChevronRight", "Next sector", () => StepSector(1), "QuietIconButton");
            Grid.SetColumn(next, 2);
            miniFoot.Children.Add(prev);
            miniFoot.Children.Add(next);
        }
        var miniCard = new Tile
        {
            Style = Ui.Res<Style>("Card"), Padding = new Thickness(12, 10, 12, 10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Child = new StackPanel { Children = { _mini, miniFoot } },
        };
        overlay.Children.Add(miniCard);

        var topLeft = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0) };
        var top = new Grid { VerticalAlignment = VerticalAlignment.Top };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(cards, 1);
        top.Children.Add(topLeft);
        top.Children.Add(cards);
        overlay.Children.Add(top);
        if (_cars.Any(c => c.Line.Wind is not null))
        {
            _wind = new ReplayWind(_cars, settings.Imperial) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            _wind.Visibility = settings.AnalysisWind ? Visibility.Visible : Visibility.Collapsed;
            topLeft.Children.Add(_wind);
        }

        if (data.SectorStarts.Count > 1)
        {
            _table = new SectorTable(_cars, data.SectorStarts);
            overlay.Children.Add(new Tile
            {
                Style = Ui.Res<Style>("Card"), Padding = new Thickness(14, 10, 14, 10), Child = _table,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            });
        }

        _zoom = new Slider { Minimum = 0, Maximum = 1, Value = settings.ReplayZoom, Width = 150, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        _zoom.ValueChanged += (_, e) => SetZoom(e.NewValue);
        _zoom.ToolTip = "Zoom (the wheel over the track too); all the way out = the whole lap";
        var turn = Ui.LabeledButton("I.Rotate", "Turn with car", () => { });
        Ui.SetToggled(turn, settings.ReplayTurn);
        turn.Click += (_, _) => { settings.ReplayTurn = !settings.ReplayTurn; Ui.SetToggled(turn, settings.ReplayTurn); _stage.Update(); _wind?.Turn(_stage.Rotation); _save(); };
        turn.ToolTip = "Keep the followed car pointing up (off = north up)";
        turn.Margin = new Thickness(10, 0, 0, 0);
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal };
        _camera = Ui.Segmented(new[] { "Follow car", "Free" }, settings.ReplayFree ? 1 : 0, i => SetFree(i == 1), "replayCamera",
            new[] { "The camera stays on the followed car (click a card to pick it)", "Drag to move around the track, the wheel zooms where the pointer is" });
        _camera.VerticalAlignment = VerticalAlignment.Center;
        _camera.Margin = new Thickness(0, 0, 10, 0);
        zoomRow.Children.Add(_camera);
        zoomRow.Children.Add(Ui.IconButton("I.Minus", "Zoom out", () => ZoomBy(-0.1), "QuietIconButton"));
        zoomRow.Children.Add(_zoom);
        zoomRow.Children.Add(Ui.IconButton("I.Plus", "Zoom in", () => ZoomBy(0.1), "QuietIconButton"));
        _turn = turn;
        turn.IsEnabled = !settings.ReplayFree;
        zoomRow.Children.Add(turn);
        var tips = Ui.LabeledButton("I.Flag", "Tips", () => { });
        Ui.SetToggled(tips, settings.ReplayTips);
        tips.ToolTip = "Notes on the track where the lap brakes, picks up the throttle or carries speed differently from the reference";
        tips.Margin = new Thickness(6, 0, 0, 0);
        tips.Click += (_, _) => { settings.ReplayTips = !settings.ReplayTips; Ui.SetToggled(tips, settings.ReplayTips); _stage.Update(); _save(); };
        zoomRow.Children.Add(tips);
        if (_wind is not null)
        {
            var windButton = Ui.LabeledButton("I.Wind", "Wind", () => { });
            Ui.SetToggled(windButton, settings.AnalysisWind);
            windButton.ToolTip = "Wind speed and direction on each lap";
            windButton.Margin = new Thickness(6, 0, 0, 0);
            windButton.Click += (_, _) =>
            {
                settings.AnalysisWind = !settings.AnalysisWind;
                Ui.SetToggled(windButton, settings.AnalysisWind);
                _wind.Visibility = settings.AnalysisWind ? Visibility.Visible : Visibility.Collapsed;
                _save();
            };
            zoomRow.Children.Add(windButton);
        }
        zoomRow.VerticalAlignment = VerticalAlignment.Center;
        zoomRow.Margin = new Thickness(18, 0, 0, 0);

        var legend = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        var colors = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-12, 0, 0, 0) };
        foreach (var (label, key) in new[] { ("Throttle", StatsUi.Hue("Sage").Deep), ("Coasting", "Heading"), ("Brake", "Danger") })
        {
            var sw = new Border { Width = 14, Height = 3, Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            sw.SetResourceReference(Border.BackgroundProperty, key);
            colors.Children.Add(sw);
            colors.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
        }
        colors.ToolTip = "The followed lap's line; the other laps are dotted in their colors";
        legend.Children.Add(colors);
        topLeft.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(12, 8, 12, 8), HorizontalAlignment = HorizontalAlignment.Left, Child = legend });

        var stageHost = new Grid { ClipToBounds = true, Children = { _stage, overlay } };
        IEnumerable<FrameworkElement> Parts() => overlay.Children.OfType<FrameworkElement>().Where(p => p != top)
            .Concat(topLeft.Children.OfType<FrameworkElement>()).Concat(cards.Children.OfType<FrameworkElement>());
        void Avoid()
        {
            _stage.Avoid.Clear();
            foreach (var part in Parts())
            {
                if (part.Visibility != Visibility.Visible || part.ActualWidth < 1 || !part.IsDescendantOf(stageHost)) continue;
                var at = part.TransformToAncestor(stageHost).TransformBounds(new Rect(part.RenderSize));
                at.Inflate(6, 6);
                _stage.Avoid.Add(at);
            }
            _stage.TopInset = cards.IsDescendantOf(stageHost) && cards.ActualHeight > 0 ? cards.TransformToAncestor(stageHost).Transform(new Point(0, cards.ActualHeight)).Y : 0;
            _stage.Update();
        }
        bool avoidQueued = false;
        void QueueAvoid()
        {
            if (avoidQueued) return;
            avoidQueued = true;
            Dispatcher.InvokeAsync(() => { avoidQueued = false; Avoid(); }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        foreach (var part in Parts().Append(top)) part.SizeChanged += (_, _) => QueueAvoid();
        stageHost.SizeChanged += (_, _) => QueueAvoid();
        _stageTile = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Height = 560, Child = stageHost };

        _stageTile.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(_stageTile);

        _scrubber = new ReplayScrubber(_follow.Trace.LapTime > 0 ? _sectorStarts.Skip(1).Select(d => _follow.Trace.TimeAt(d) / _follow.Trace.LapTime).ToArray() : Array.Empty<double>())
        {
            Height = 26, Margin = new Thickness(0, 0, 0, 10),
        };
        _scrubber.Seeking += share => { Pause(); Seek(share * _follow.Trace.LapTime); };

        _play = Ui.IconButton("I.Play", "Play / pause (Space)", Toggle, "PrimaryButton");
        _clock.Style = Ui.Res<Style>("H3");
        _clock.VerticalAlignment = VerticalAlignment.Center;
        _clock.Margin = new Thickness(14, 0, 0, 0);
        _clock.MinWidth = 150;
        Typography.SetNumeralAlignment(_clock, FontNumeralAlignment.Tabular);
        var speeds = LapReplay.Speeds.Select(v => new Choice(v.ToString(CultureInfo.InvariantCulture), v.ToString("0.##", CultureInfo.CurrentCulture) + "×")).ToList();
        var speed = Ui.Combo(speeds, settings.ReplaySpeed.ToString(CultureInfo.InvariantCulture), c =>
        {
            if (double.TryParse(c.Id, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) { _settings.ReplaySpeed = v; _save(); }
        }, 90);
        speed.ToolTip = "Playback speed";
        speed.Margin = new Thickness(10, 0, 0, 0);
        speed.VerticalAlignment = VerticalAlignment.Center;
        var left = new WrapPanel { Orientation = Orientation.Horizontal, Children = { _play, _clock, speed, zoomRow } };

        foreach (var t in new[] { _gapSeconds, _gapMetres })
        {
            t.Style = Ui.Res<Style>("Body");
            t.FontWeight = FontWeights.SemiBold;
            t.VerticalAlignment = VerticalAlignment.Center;
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        }
        _gapSeconds.MinWidth = 64;
        _gapMetres.MinWidth = 64;
        _gapMetres.Margin = new Thickness(10, 0, 0, 0);
        var gapLabel = new TextBlock { Text = "Delta", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var gap = new StackPanel { Orientation = Orientation.Horizontal, Children = { gapLabel, _gapSeconds, _gapMetres } };
        gap.ToolTip = "Behind (+) or ahead (−) of the reference: in time at this spot, and in distance after the same time";
        _refSwitch = Ui.Switch(settings.ReplayShowReference, ShowReference);
        var refSwitch = _refSwitch;
        var refRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        refRow.Children.Add(new TextBlock { Text = "Reference", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        refRow.Children.Add(refSwitch);
        refRow.ToolTip = "Show the reference lap's line and car";
        var sync = Ui.Segmented(new[] { "Distance", "Time" }, settings.ReplaySync == "time" ? 1 : 0, i =>
        {
            _settings.ReplaySync = i == 1 ? "time" : "distance";
            _save();
            Place(force: true);
        }, "replaySync", new[] { "Every car at the same spot: the gap shows in time", "Every car after the same time since the line: they race, the gap shows on track" });
        sync.Margin = new Thickness(18, 0, 0, 0);
        sync.VerticalAlignment = VerticalAlignment.Center;
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { gap, refRow, sync } };

        var bar = new StackPanel();
        bar.Children.Add(_scrubber);
        bar.Children.Add(Ui.SplitRow(left, right, 16));
        _barTile = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(20, 14, 20, 14), Child = bar };
        SetRow(_barTile, 1);
        Children.Add(_barTile);

        PreviewMouseDown += (_, e) => { if (e.OriginalSource is not TextBox && !IsKeyboardFocusWithin) Focus(); };
        PreviewKeyDown += OnKey;
        IsVisibleChanged += (_, _) => { if (!IsVisible) Pause(); };
        Unloaded += (_, _) =>
        {
            Pause();
            if (_page is not null) _page.SizeChanged -= FitStage;
            _page = null;
        };
        Loaded += (_, _) =>
        {
            _page = Ancestor<ScrollViewer>(this);
            if (_page is not null) _page.SizeChanged += FitStage;
            Dispatcher.InvokeAsync(() => FitStage(null, null), System.Windows.Threading.DispatcherPriority.Loaded);
            Place(force: true);
        };
        _stage.SizeChanged += (_, _) => { _stage.Update(); _wind?.Turn(_stage.Rotation); };
        Place(force: true);
    }

    public TrackOutline? Outline { get; private set; }

    private async Task PlaceMapAsync(SimRacing.TrackMaps.TrackMap map, List<(double[] X, double[] Y, double Step, double Length)> lines)
    {
        TrackOutline? outline;
        try { outline = await Task.Run(() => TrackFit.Fit(map, lines)); }
        catch (Exception ex) { App.LogError(ex); return; }
        if (outline is null) return;
        Outline = outline;
        _stage.SetOutline(outline);
    }

    private static T? Ancestor<T>(DependencyObject from) where T : DependencyObject
    {
        for (var at = VisualTreeHelper.GetParent(from); at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is T found) return found;
        return null;
    }

    private void FitStage(object? sender, SizeChangedEventArgs? e)
    {
        if (_page is null || !_stageTile.IsLoaded) return;
        var toPage = _stageTile.TransformToAncestor(_page);
        double unit = Math.Max(0.1, toPage.Transform(new Point(0, 100)).Y - toPage.Transform(new Point(0, 0)).Y) / 100;
        double room = _page.ViewportHeight / unit - _barTile.ActualHeight - _stageTile.Margin.Bottom - 28;
        _stageTile.Height = Math.Clamp(room, MinStage, MaxStage);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1;
        switch (e.Key)
        {
            case Key.Space: Toggle(); break;
            case Key.Left: Seek(_time - step); break;
            case Key.Right: Seek(_time + step); break;
            case Key.Home: Seek(0); break;
            case Key.End: Seek(_follow.Trace.LapTime); break;
            case Key.PageUp: StepSector(-1); break;
            case Key.PageDown: StepSector(1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void StepSector(int by)
    {
        if (_sectorStarts.Length < 2) return;
        int at = LapReplay.SectorAt(_sectorStarts, _follow.Distance);
        double into = _time - _follow.Trace.TimeAt(_sectorStarts[at]);
        int target = by < 0 && into > 1.5 ? at : Math.Clamp(at + by, 0, _sectorStarts.Length - 1);
        Pause();
        Seek(_follow.Trace.TimeAt(_sectorStarts[target]) + 0.001);
    }

    private void Toggle()
    {
        if (_playing) Pause();
        else Play();
    }

    public void Play()
    {
        if (_cars.Count == 0) return;
        if (_time >= _follow.Trace.LapTime - 0.01) _time = 0;
        _playing = true;
        ((Icon)_play.Content).Data = Ui.Res<Geometry>("I.Pause");
        if (!_hooked) { CompositionTarget.Rendering += OnFrame; _hooked = true; }
        _lastFrame = null;
    }

    public void Pause()
    {
        if (_hooked) { CompositionTarget.Rendering -= OnFrame; _hooked = false; }
        if (!_playing) return;
        _playing = false;
        ((Icon)_play.Content).Data = Ui.Res<Geometry>("I.Play");
        Place(force: true);
    }

    public void Seek(double time)
    {
        if (_cars.Count == 0) return;
        _time = Math.Clamp(time, 0, _follow.Trace.LapTime);
        Place(force: true);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs r) return;
        if (_lastFrame is not { } last) { _lastFrame = r.RenderingTime; return; }
        double dt = (r.RenderingTime - last).TotalSeconds;
        if (dt < 0.015) return;
        _lastFrame = r.RenderingTime;
        var (t, ended) = LapReplay.Advance(_time, Math.Min(dt, 0.1), _settings.ReplaySpeed, _follow.Trace.LapTime);
        _time = t;
        Place();
        if (ended) Pause();
    }

    private void ShowReference(bool on)
    {
        _settings.ReplayShowReference = on;
        _refSwitch.IsChecked = on;
        if (!on && _follow.Line.Reference && _cars.FirstOrDefault(c => !c.Line.Reference) is { } own) Follow(own);
        _stage.Update();
        Readouts();
        _save();
    }

    private void Follow(ReplayCar car)
    {
        if (_settings.ReplayFree) SetFree(false);
        if (car.Line.Reference && !_settings.ReplayShowReference) ShowReference(true);
        if (car == _follow) return;
        double d = _follow.Distance;
        _follow = car;
        _memory.Follow = car.Line.Label;
        _time = car.Trace.TimeAt(d);
        _scrubber.SetMarks(car.Trace.LapTime > 0 ? _sectorStarts.Skip(1).Select(x => car.Trace.TimeAt(x) / car.Trace.LapTime).ToArray() : Array.Empty<double>());
        Place(force: true);
    }

    private void SetFree(bool free)
    {
        if (free == _settings.ReplayFree) return;
        if (free) _stage.StartFree();
        else { _settings.ReplayFree = false; _stage.Update(); _wind?.Turn(_stage.Rotation); }
        _save();
        SyncCamera();
    }

    private void SyncCamera()
    {
        Ui.SetSegment(_camera, _settings.ReplayFree ? 1 : 0);
        _turn.IsEnabled = !_settings.ReplayFree;
        _wind?.Turn(_stage.Rotation);
    }

    private void ZoomBy(double step)
    {
        if (_settings.ReplayFree) _stage.ZoomFree(step > 0 ? 1.25 : 1 / 1.25);
        else SetZoom(_settings.ReplayZoom + step);
    }

    private void SetZoom(double z)
    {
        z = Math.Clamp(z, 0, 1);
        if (Math.Abs(_zoom.Value - z) > 1e-9) { _zoom.Value = z; return; }
        _settings.ReplayZoom = z;
        if (_settings.ReplayFree) { _settings.ReplayFree = false; SyncCamera(); }
        _stage.Update();
        _wind?.Turn(_stage.Rotation);
        _save();
    }

    private void Place(bool force = false)
    {
        if (_cars.Count == 0) return;
        var sync = _settings.ReplaySync == "time" ? ReplaySync.Time : ReplaySync.Distance;
        foreach (var car in _cars)
        {
            var t = car.Trace;
            car.Distance = car == _follow ? _follow.Trace.DistanceAtTime(_time) : LapReplay.DistanceOf(t, _follow.Trace, _time, sync);
            car.Px = LapReplay.At(car.X, t.StepM, car.Distance);
            car.Py = LapReplay.At(car.Y, t.StepM, car.Distance);
            car.Heading = LapReplay.HeadingAt(car.Headings, t.StepM, car.Distance);
            car.Yaw = t.At(TraceChannel.Yaw, car.Distance) ?? car.Heading;
        }
        _memory.Distance = _follow.Distance;
        _stage.Update();
        _mini.Move(_follow.Px, _follow.Py);
        _scrubber.Value = _follow.Trace.LapTime > 0 ? _time / _follow.Trace.LapTime : 0;
        long now = Environment.TickCount64;
        if (!force && now - _lastReadout < ReadoutMs) return;
        _lastReadout = now;
        Readouts();
    }

    private void Readouts()
    {
        _clock.Text = StatFormat.LapTime(_time) + " / " + StatFormat.LapTime(_follow.Trace.LapTime);
        foreach (var card in _cards) card.Update(card.Car == _follow, _settings.ReplayShowReference || !card.Car.Line.Reference);
        _wind?.Turn(_stage.Rotation);
        var compared = _follow.Line.Reference ? _cars.FirstOrDefault(c => !c.Line.Reference) : _follow;
        int sector = LapReplay.SectorAt(_sectorStarts, _follow.Distance);
        if (sector != _sector)
        {
            _sector = sector;
            _mini.SetSector(sector);
            _table?.SetCurrent(sector);
            _sectorName.Text = _sectorStarts.Length > 1 ? $"Sector {sector + 1}" : "Lap";
        }
        if (compared is null)
        {
            _gapSeconds.Text = _gapMetres.Text = _sectorGap.Text = RaceFormatMissing;
            Tone(_gapSeconds, 0, ref _gapSign);
            Tone(_sectorGap, 0, ref _sectorSign);
            return;
        }
        var reference = _s.Reference.Trace;
        var (seconds, metres) = LapReplay.Gap(compared.Trace, reference, compared.Distance);
        _gapSeconds.Text = Signed(seconds, "0.000") + " s";
        _gapMetres.Text = _settings.Imperial ? Signed(metres * 3.28084, "0") + " ft" : Signed(metres, "0") + " m";
        Tone(_gapSeconds, seconds, ref _gapSign);
        _gapMetres.Foreground = _gapSeconds.Foreground;
        double sectorGap = LapReplay.SectorGap(compared.Trace, reference, _sectorStarts, compared.Distance);
        _sectorGap.Text = Signed(sectorGap, "0.000");
        Tone(_sectorGap, sectorGap, ref _sectorSign);
    }

    private const string RaceFormatMissing = GoodieBag.SimRacing.Race.RaceFormat.Missing;

    internal static string Signed(double v, string format) =>
        (v < 0 ? "−" : "+") + Math.Abs(v).ToString(format, CultureInfo.CurrentCulture);

    private static void Tone(TextBlock text, double value, ref int last)
    {
        int sign = Math.Abs(value) < 0.0005 ? 0 : Math.Sign(value);
        if (sign == last) return;
        last = sign;
        text.SetResourceReference(TextBlock.ForegroundProperty, sign > 0 ? StatsUi.Hue("Coral").Deep : sign < 0 ? StatsUi.Hue("Sage").Deep : "Heading");
    }
}

internal sealed class ReplayWind : Tile
{
    private readonly List<(RotateTransform Turn, double To)> _arrows = new();
    private readonly RotateTransform _north = new();
    private double _rotation = double.NaN;

    public ReplayWind(List<ReplayCar> cars, bool imperial)
    {
        Style = Ui.Res<Style>("Card");
        Padding = new Thickness(12, 8, 12, 8);
        IsHitTestVisible = false;
        var rows = new Grid();
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            rows.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        rows.RowDefinitions.Add(new RowDefinition());
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        title.Children.Add(new TextBlock { Text = "Wind", Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var north = new Grid { Width = 18, Height = 18, Margin = new Thickness(10, 0, 0, 0), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _north, ToolTip = "North" };
        var needle = new Icon { Data = Geometry.Parse("M12 3l4 9h-8z"), Width = 18, Height = 18, StrokeThickness = 1.6 };
        needle.SetResourceReference(Icon.ForegroundProperty, "TextMuted");
        var n = new TextBlock { Text = "N", Style = Ui.Res<Style>("Caption"), FontSize = 8.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        north.Children.Add(needle);
        north.Children.Add(n);
        title.Children.Add(north);
        Cell(rows, title, 0, 0, 4);
        int row = 1;
        foreach (var car in cars)
        {
            var line = car.Line;
            if (line.Wind is not { } wind) continue;
            rows.RowDefinitions.Add(new RowDefinition());
            var swatch = new Border { Width = 4, Height = 16, Margin = new Thickness(0, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, line.BrushKey);
            if (line.Reference) swatch.Opacity = 0.6;
            double to = (wind.From + Math.PI) * 180 / Math.PI;
            var turn = new RotateTransform(to);
            _arrows.Add((turn, to));
            var arrow = new Icon
            {
                Data = Geometry.Parse("M12 20V4M6 10l6-6 6 6"), Width = 16, Height = 16, StrokeThickness = 2.2, Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = turn,
            };
            arrow.SetResourceReference(Icon.ForegroundProperty, line.BrushKey);
            double speed = imperial ? wind.Kph / 1.609344 : wind.Kph;
            var value = new TextBlock { Text = $"{line.Short}  {speed:0} {(imperial ? "mph" : "km/h")}", Style = Ui.Res<Style>("Body"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var point = new TextBlock { Text = SimRacing.Race.RaceFormat.Compass(wind.From + Math.PI), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
            point.ToolTip = "Where the wind blows to";
            Cell(rows, swatch, row, 0);
            Cell(rows, arrow, row, 1);
            Cell(rows, value, row, 2);
            Cell(rows, point, row, 3);
            row++;
        }
        Child = rows;
    }

    private static void Cell(Grid grid, FrameworkElement e, int row, int column, int span = 1)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        Grid.SetColumnSpan(e, span);
        grid.Children.Add(e);
    }

    public void Turn(double rotation)
    {
        double deg = rotation * 180 / Math.PI;
        if (Math.Abs(deg - _rotation) < 0.5) return;
        _rotation = deg;
        _north.Angle = deg;
        foreach (var (turn, to) in _arrows) turn.Angle = to + deg;
    }
}

internal sealed class ReplayStage : FrameworkElement, IKeepsWheel
{
    public const double TrackWidth = TrackFit.FallbackWidth;
    private const double MaxScale = 22;
    private const int Buckets = 16;
    private readonly List<ReplayCar> _cars;
    private readonly AnalysisState _s;
    private readonly Config.SessionStatsSettings _settings;
    private readonly Func<ReplayCar> _follow;
    private readonly (double Length, double Width) _carSize;
    private readonly bool _onlyReference;
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _world = new();
    private readonly MatrixTransform _camera = new();
    private readonly DrawingVisual[] _carVisuals, _labelVisuals;
    private readonly MatrixTransform[] _carMatrix, _labelMatrix;
    private readonly Point[] _labelAt;
    private readonly StreamGeometry[] _paths;
    private StreamGeometry _ticks;
    private (StreamGeometry Base, StreamGeometry Squares) _checker;
    private TrackOutline? _outline;
    private StreamGeometry? _surface, _pitFill, _pitLines;
    private readonly double _midX, _midY, _spanX, _spanY;
    private double _builtScale = -1;
    private ReplayCar? _builtFollow;
    private bool _builtRef;
    private readonly List<NoteMark> _notes = new();
    private readonly List<Rect> _taken = new();

    public List<Rect> Avoid { get; } = new();
    private ReplayCar? _notesFor;

    private sealed class NoteMark
    {
        public required DrawingVisual Marker, Callout;
        public required MatrixTransform MarkerAt, CalloutAt;
        public required Point World;
        public required double Distance;
        public required Size Box;
    }

    public event Action<double>? ZoomStep;

    public event Action? WentFree;

    public double TopInset { get; set; }

    private Point Anchor(double k)
    {
        double top = Math.Clamp(TopInset, 0, ActualHeight * 0.6), h = ActualHeight - top;
        return new Point(ActualWidth / 2, top + h * (0.5 + 0.08 * k));
    }

    private double _camX, _camY, _camScale = 1, _freeX, _freeY, _freeScale = 1;
    private Point? _dragFrom;

    public ReplayStage(List<ReplayCar> cars, AnalysisState s, Config.SessionStatsSettings settings, Func<ReplayCar> follow, string car)
    {
        _cars = cars;
        _carSize = CarShape.SizeOf(car);
        _onlyReference = cars.All(c => c.Line.Reference);
        _s = s;
        _settings = settings;
        _follow = follow;
        ClipToBounds = true;
        _visuals = new VisualCollection(this) { _world };
        _world.Transform = _camera;
        _paths = cars.Select(c => Path(c.X, c.Y)).ToArray();
        _carVisuals = cars.Select(_ => new DrawingVisual()).ToArray();
        _carMatrix = cars.Select(_ => new MatrixTransform()).ToArray();
        var track = Track;
        double step = _s.Reference.Trace.StepM;
        _labelAt = _s.SectorDistances.Select(d => LabelPoint(track, step, d, TrackWidth)).ToArray();
        _labelVisuals = _labelAt.Select(_ => new DrawingVisual()).ToArray();
        _labelMatrix = _labelAt.Select(_ => new MatrixTransform()).ToArray();
        for (int i = 0; i < _labelVisuals.Length; i++)
        {
            _labelVisuals[i].Transform = _labelMatrix[i];
            _visuals.Add(_labelVisuals[i]);
        }
        for (int i = 0; i < cars.Count; i++)
        {
            _carVisuals[i].Transform = _carMatrix[i];
            _visuals.Add(_carVisuals[i]);
        }
        double minX = track.X.Min(), maxX = track.X.Max(), minY = track.Y.Min(), maxY = track.Y.Max();
        (_midX, _midY, _spanX, _spanY) = ((minX + maxX) / 2, (minY + maxY) / 2, Math.Max(50, maxX - minX), Math.Max(50, maxY - minY));
        _ticks = Ticks(track, step, TrackWidth);
        _checker = Checker(track, step, TrackWidth);
    }

    public double Rotation { get; private set; }

    private static Point LabelPoint(ReplayCar track, double step, double d, double width)
    {
        double x = LapReplay.At(track.X, step, d), y = LapReplay.At(track.Y, step, d), h = LapReplay.HeadingAt(track.Headings, step, d);
        double off = width / 2 + 9;
        return new Point(x - Math.Sin(h) * off, y + Math.Cos(h) * off);
    }

    private static (StreamGeometry, StreamGeometry) Checker(ReplayCar track, double step, double width)
    {
        double x = LapReplay.At(track.X, step, 0), y = LapReplay.At(track.Y, step, 0), h = LapReplay.HeadingAt(track.Headings, step, 0);
        double fx = Math.Cos(h), fy = Math.Sin(h), nx = -Math.Sin(h), ny = Math.Cos(h);
        const int Across = 8, Along = 2;
        double cell = width / Across;
        Point At(double a, double b) => new(x + nx * (a - width / 2) + fx * (b - cell * Along / 2), y + ny * (a - width / 2) + fy * (b - cell * Along / 2));
        var all = new StreamGeometry();
        using (var g = all.Open())
        {
            g.BeginFigure(At(0, 0), true, true);
            g.LineTo(At(width, 0), false, false);
            g.LineTo(At(width, cell * Along), false, false);
            g.LineTo(At(0, cell * Along), false, false);
        }
        all.Freeze();
        var squares = new StreamGeometry();
        using (var g = squares.Open())
            for (int i = 0; i < Across; i++)
                for (int j = 0; j < Along; j++)
                {
                    if ((i + j) % 2 != 0) continue;
                    g.BeginFigure(At(i * cell, j * cell), true, true);
                    g.LineTo(At((i + 1) * cell, j * cell), false, false);
                    g.LineTo(At((i + 1) * cell, (j + 1) * cell), false, false);
                    g.LineTo(At(i * cell, (j + 1) * cell), false, false);
                }
        squares.Freeze();
        return (all, squares);
    }

    public void SetOutline(TrackOutline outline)
    {
        _outline = outline;
        _surface = Shapes(TrackFit.Wound(outline.Surface).Select(f => (f.X, f.Y, true)), FillRule.Nonzero);
        _pitFill = Shapes(outline.Pit.Where(p => p.Filled), FillRule.Nonzero);
        _pitLines = Shapes(outline.Pit.Where(p => !p.Filled), FillRule.Nonzero);
        _ticks = Ticks(Track, _s.Reference.Trace.StepM, outline.Width);
        _checker = Checker(Track, _s.Reference.Trace.StepM, outline.Width);
        for (int i = 0; i < _labelAt.Length; i++) _labelAt[i] = LabelPoint(Track, _s.Reference.Trace.StepM, _s.SectorDistances[i], outline.Width);
        _builtScale = -1;
        Update();
    }

    private static StreamGeometry Shapes(IEnumerable<(double[] X, double[] Y, bool Closed)> figures, FillRule rule)
    {
        var geo = new StreamGeometry { FillRule = rule };
        using (var g = geo.Open())
            foreach (var (x, y, closed) in figures)
            {
                if (x.Length < 2) continue;
                g.BeginFigure(new Point(x[0], y[0]), closed, closed);
                for (int i = 1; i < x.Length; i++) g.LineTo(new Point(x[i], y[i]), true, true);
            }
        geo.Freeze();
        return geo;
    }

    private ReplayCar Track => _cars.FirstOrDefault(c => c.Line.Reference) ?? _cars[0];

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override void OnRender(DrawingContext dc) => dc.DrawRectangle(Ui.Res<Brush>("Bg"), null, new Rect(RenderSize));

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_settings.ReplayFree) ZoomFree(e.Delta > 0 ? 1.2 : 1 / 1.2, e.GetPosition(this));
        else ZoomStep?.Invoke(e.Delta > 0 ? 0.06 : -0.06);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragFrom = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(this);
        if (!_settings.ReplayFree)
        {
            if ((at - from).Length < 5) return;
            StartFree();
            WentFree?.Invoke();
        }
        _freeX -= (at.X - from.X) / _freeScale;
        _freeY += (at.Y - from.Y) / _freeScale;
        _dragFrom = at;
        Update();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragFrom = null;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragFrom = null;
    }

    public void StartFree()
    {
        (_freeX, _freeY, _freeScale) = (_camX, _camY, _camScale);
        _settings.ReplayFree = true;
        Update();
    }

    public void ZoomFree(double by, Point? at = null)
    {
        var c = Anchor(0);
        var p = at ?? c;
        double wx = _freeX + (p.X - c.X) / _freeScale, wy = _freeY - (p.Y - c.Y) / _freeScale;
        _freeScale = Math.Clamp(_freeScale * by, Scale(0) * 0.5, MaxScale * 2);
        _freeX = wx - (p.X - c.X) / _freeScale;
        _freeY = wy + (p.Y - c.Y) / _freeScale;
        Update();
    }

    private double Scale(double zoom)
    {
        double fit = Math.Min((ActualWidth - 60) / _spanX, (ActualHeight - Math.Clamp(TopInset, 0, ActualHeight * 0.6) - 60) / _spanY);
        fit = Math.Max(0.01, fit);
        return fit * Math.Pow(Math.Max(1, MaxScale / fit), zoom);
    }

    public void Update()
    {
        if (ActualWidth < 20 || ActualHeight < 20) return;
        var follow = _follow();
        bool free = _settings.ReplayFree;
        double zoom = _settings.ReplayZoom, scale = free ? _freeScale : Scale(zoom);
        if (Math.Abs(scale / _builtScale - 1) > 0.004 || follow != _builtFollow || _settings.ReplayShowReference != _builtRef) Build(scale, follow);

        double k = free ? 0 : Math.Clamp(zoom / 0.15, 0, 1);
        double cx = free ? _freeX : _midX + (follow.Px - _midX) * k, cy = free ? _freeY : _midY + (follow.Py - _midY) * k;
        double rot = !free && _settings.ReplayTurn ? Math.IEEERemainder(follow.Heading - Math.PI / 2, Math.Tau) * k : 0;
        Rotation = rot;
        var m = Matrix.Identity;
        m.Translate(-cx, -cy);
        m.Scale(scale, -scale);
        m.Rotate(rot * 180 / Math.PI);
        var anchor = Anchor(k);
        m.Translate(anchor.X, anchor.Y);
        _camera.Matrix = m;
        if (!free && m.HasInverse)
        {
            var back = m;
            back.Invert();
            var centre = back.Transform(Anchor(0));
            (_camX, _camY, _camScale) = (centre.X, centre.Y, scale);
        }

        for (int i = 0; i < _cars.Count; i++)
        {
            var car = _cars[i];
            bool shown = _settings.ReplayShowReference || !car.Line.Reference || _onlyReference;
            _carVisuals[i].Opacity = shown ? 1 : 0;
            var p = m.Transform(new Point(car.Px, car.Py));
            var cm = Matrix.Identity;
            cm.Rotate((rot - car.Yaw + Math.PI / 2) * 180 / Math.PI);
            cm.Translate(p.X, p.Y);
            _carMatrix[i].Matrix = cm;
        }
        for (int i = 0; i < _labelAt.Length; i++)
        {
            var p = m.Transform(_labelAt[i]);
            _labelMatrix[i].Matrix = new Matrix(1, 0, 0, 1, p.X, p.Y);
        }
        PlaceNotes(m, follow);
    }

    private void PlaceNotes(Matrix m, ReplayCar follow)
    {
        _taken.Clear();
        _taken.AddRange(Avoid);
        int n = _notes.Count, first = 0;
        while (first < n && _notes[first].Distance < follow.Distance - 30) first++;
        var view = new Rect(4, 4, ActualWidth - 8, ActualHeight - 8);
        for (int k = 0; k < n; k++)
        {
            var note = _notes[(first + k) % n];
            var p = m.Transform(note.World);
            double x = Math.Round(p.X), y = Math.Round(p.Y);
            note.MarkerAt.Matrix = new Matrix(1, 0, 0, 1, x, y);
            note.Marker.Opacity = _settings.ReplayTips && view.Contains(p) ? 1 : 0;
            var box = new Rect(x + 12, y - 12, note.Box.Width, note.Box.Height);
            if (box.Right > view.Right) box.X = x - 12 - box.Width;
            if (box.Bottom > view.Bottom) box.Y = y + 12 - box.Height;
            bool show = _settings.ReplayTips && view.Contains(p) && view.Contains(box);
            foreach (var r in _taken) if (show && r.IntersectsWith(box)) show = false;
            if (show) _taken.Add(box);
            note.Callout.Opacity = show ? 1 : 0;
            note.CalloutAt.Matrix = new Matrix(1, 0, 0, 1, box.X, box.Y);
        }
    }

    private void Build(double scale, ReplayCar follow)
    {
        _builtScale = scale;
        _builtFollow = follow;
        _builtRef = _settings.ReplayShowReference;
        if (_notesFor != follow) MakeNotes(follow);
        double px = 1 / scale;
        using (var dc = _world.RenderOpen())
        {
            if (_outline is not null)
            {
                var rim = new Pen(Ui.Res<Brush>("LineStrong"), 2 * px) { LineJoin = PenLineJoin.Round };
                rim.Freeze();
                var pitBrush = Ui.Res<Brush>("SurfaceAlt").Clone();
                pitBrush.Opacity = 0.55;
                pitBrush.Freeze();
                if (_pitFill is not null) dc.DrawGeometry(pitBrush, null, _pitFill);
                if (_pitLines is not null && _outline.PitWidth > 0)
                {
                    var pit = new Pen(pitBrush, _outline.PitWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    pit.Freeze();
                    dc.DrawGeometry(null, pit, _pitLines);
                }
                dc.DrawGeometry(Ui.Res<Brush>("SurfaceAlt"), rim, _surface);
            }
            else
            {
                int track = _cars.IndexOf(Track);
                var edge = new Pen(Ui.Res<Brush>("LineStrong"), TrackWidth + 2 * px) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                var surface = new Pen(Ui.Res<Brush>("SurfaceAlt"), TrackWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                edge.Freeze();
                surface.Freeze();
                dc.DrawGeometry(null, edge, _paths[track]);
                dc.DrawGeometry(null, surface, _paths[track]);
            }
            var tick = new Pen(Ui.Res<Brush>("TextMuted"), 2 * px);
            tick.Freeze();
            dc.DrawGeometry(null, tick, _ticks);
            dc.DrawGeometry(Ui.Res<Brush>("Bg"), null, _checker.Base);
            dc.DrawGeometry(Ui.Res<Brush>("Heading"), null, _checker.Squares);
            for (int i = 0; i < _cars.Count; i++)
            {
                var car = _cars[i];
                if (car == follow || (car.Line.Reference && !_settings.ReplayShowReference)) continue;
                var pen = new Pen(Ui.Res<Brush>(car.Line.BrushKey), 2 * px) { DashStyle = new DashStyle(new[] { 1.0, 2.0 }, 0), DashCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                pen.Freeze();
                dc.DrawGeometry(null, pen, _paths[i]);
            }
            DrawPedals(dc, follow, 3.5 * px);
        }
        double carPx = Math.Max(_carSize.Length * scale, 22), carWide = carPx * _carSize.Width / _carSize.Length;
        for (int i = 0; i < _cars.Count; i++)
        {
            var brush = Ui.Res<Brush>(_cars[i].Line.BrushKey);
            using (var dc = _carVisuals[i].RenderOpen()) CarShape.Draw(dc, brush, carPx, carWide, _cars[i] == follow);
        }
        for (int i = 0; i < _labelVisuals.Length; i++)
        {
            using var dc = _labelVisuals[i].RenderOpen();
            var text = LapAnalysisView.Text(this, "S" + (i + 2), Ui.Res<Brush>("Heading"), 12, bold: true);
            var box = new Rect(-text.Width / 2 - 5, -text.Height / 2 - 2, text.Width + 10, text.Height + 4);
            dc.DrawRectangle(Ui.Res<Brush>("Surface"), new Pen(Ui.Res<Brush>("LineStrong"), 1), box);
            dc.DrawText(text, new Point(-text.Width / 2, -text.Height / 2));
        }
        int at = _cars.IndexOf(follow);
        _visuals.Remove(_carVisuals[at]);
        _visuals.Add(_carVisuals[at]);
        foreach (var note in _notes)
        {
            _visuals.Remove(note.Callout);
            _visuals.Add(note.Callout);
        }
    }

    private static string NoteBrush(NoteKind kind) => kind switch
    {
        NoteKind.Brake => "Danger", NoteKind.Throttle => StatsUi.Hue("Sage").Deep, _ => StatsUi.Hue("Orange").Deep,
    };

    private void MakeNotes(ReplayCar follow)
    {
        _notesFor = follow;
        foreach (var old in _notes) { _visuals.Remove(old.Marker); _visuals.Remove(old.Callout); }
        _notes.Clear();
        var track = Track;
        var you = follow == track ? _cars.FirstOrDefault(c => c != track) : follow;
        if (you is null) return;
        var mine = you.Trace;
        var reference = _s.Reference.Trace;
        var found = LapReplay.Notes(mine[TraceChannel.Brake], mine[TraceChannel.Throttle], mine.StepM, mine.LengthM,
            reference[TraceChannel.Brake], reference[TraceChannel.Throttle], reference.StepM, reference.LengthM,
            speed: mine[TraceChannel.Speed], refSpeed: reference[TraceChannel.Speed]);
        double step = track.Trace.StepM;
        int at = 1;
        foreach (var note in found)
        {
            double d = note.Distance;
            var world = new Point(LapReplay.At(track.X, step, d), LapReplay.At(track.Y, step, d));
            var brush = Ui.Res<Brush>(NoteBrush(note.Kind));
            var marker = new DrawingVisual();
            var markerAt = new MatrixTransform();
            marker.Transform = markerAt;
            using (var dc = marker.RenderOpen())
            {
                var edge = new Pen(Ui.Res<Brush>("Bg"), 2);
                if (note.Kind == NoteKind.MinSpeed)
                {
                    var diamond = new StreamGeometry();
                    using (var g = diamond.Open())
                    {
                        g.BeginFigure(new Point(0, -6.5), true, true);
                        g.LineTo(new Point(6.5, 0), true, true);
                        g.LineTo(new Point(0, 6.5), true, true);
                        g.LineTo(new Point(-6.5, 0), true, true);
                    }
                    diamond.Freeze();
                    dc.DrawGeometry(brush, edge, diamond);
                }
                else dc.DrawEllipse(brush, edge, new Point(0, 0), 5.5, 5.5);
            }
            var callout = new DrawingVisual();
            var calloutAt = new MatrixTransform();
            callout.Transform = calloutAt;
            Size box;
            using (var dc = callout.RenderOpen())
            {
                var title = LapAnalysisView.Text(this, note.Title, brush, 11, bold: true);
                var body = LapAnalysisView.Text(this, note.Text(_s.Imperial), Ui.Res<Brush>("Heading"), 13);
                box = new Size(Math.Ceiling(Math.Max(title.Width, body.Width) + 24), Math.Ceiling(title.Height + body.Height + 18));
                dc.DrawRectangle(Ui.Res<Brush>("Surface"), new Pen(Ui.Res<Brush>("LineStrong"), 1), new Rect(0.5, 0.5, box.Width - 1, box.Height - 1));
                dc.DrawRectangle(brush, null, new Rect(0, 0, 3, box.Height));
                dc.DrawText(title, new Point(13, 8));
                dc.DrawText(body, new Point(13, 8 + title.Height + 2));
            }
            _visuals.Insert(at++, marker);
            _notes.Add(new NoteMark { Marker = marker, Callout = callout, MarkerAt = markerAt, CalloutAt = calloutAt, World = world, Distance = d, Box = box });
        }
        foreach (var note in _notes) _visuals.Add(note.Callout);
    }

    private static void DrawPedals(DrawingContext dc, ReplayCar car, double width)
    {
        var t = car.Trace;
        if (t[TraceChannel.Throttle] is not { } throttle || t[TraceChannel.Brake] is not { } brake)
        {
            var plain = new Pen(Ui.Res<Brush>(car.Line.BrushKey), width) { LineJoin = PenLineJoin.Round };
            plain.Freeze();
            dc.DrawGeometry(null, plain, Path(car.X, car.Y));
            return;
        }
        Color brakeC = LapAnalysisView.Brush("Danger").Color, coast = LapAnalysisView.Brush("Heading").Color, gas = LapAnalysisView.Brush(StatsUi.Hue("Sage").Deep).Color;
        var geos = new StreamGeometry?[Buckets];
        var ctx = new StreamGeometryContext?[Buckets];
        int n = Math.Min(car.X.Length, throttle.Length);
        int run = -1;
        for (int i = 0; i + 1 < n; i++)
        {
            double u = (LapReplay.Pedal(throttle[i], brake[i]) + 1) / 2;
            int k = (int)Math.Round(u * (Buckets - 1));
            if (ctx[k] is null) { geos[k] = new StreamGeometry(); ctx[k] = geos[k]!.Open(); }
            if (k != run) ctx[k]!.BeginFigure(new Point(car.X[i], car.Y[i]), false, false);
            ctx[k]!.LineTo(new Point(car.X[i + 1], car.Y[i + 1]), true, true);
            run = k;
        }
        for (int k = 0; k < Buckets; k++)
        {
            if (ctx[k] is not { } c) continue;
            c.Close();
            geos[k]!.Freeze();
            double u = k / (Buckets - 1.0);
            var color = u < 0.5 ? LapAnalysisView.Mix(brakeC, coast, u * 2) : LapAnalysisView.Mix(coast, gas, u * 2 - 1);
            var pen = new Pen(new SolidColorBrush(color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geos[k]);
        }
    }

    private static StreamGeometry Path(double[] x, double[] y)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(x[0], y[0]), false, false);
            for (int i = 1; i < x.Length; i++) g.LineTo(new Point(x[i], y[i]), true, true);
        }
        geo.Freeze();
        return geo;
    }

    private StreamGeometry Ticks(ReplayCar track, double step, double width)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            foreach (double d in _s.SectorDistances.Prepend(0))
            {
                double x = LapReplay.At(track.X, step, d), y = LapReplay.At(track.Y, step, d), h = LapReplay.HeadingAt(track.Headings, step, d);
                double nx = -Math.Sin(h) * width / 2, ny = Math.Cos(h) * width / 2;
                g.BeginFigure(new Point(x - nx, y - ny), false, false);
                g.LineTo(new Point(x + nx, y + ny), true, false);
            }
        }
        geo.Freeze();
        return geo;
    }
}

internal static class CarShape
{
    public static readonly (double Length, double Width) Default = (4.619, 2.050);

    private static readonly (string[] Words, double Length, double Width)[] Sizes =
    {
        (new[] { "gt3 cup", "cup (992)" }, 4.585, 1.920),
        (new[] { "mx-5", "mx5", "miata" }, 3.915, 1.735),
        (new[] { "gtp", "lmdh", "963", "v-series.r", "m hybrid v8", "arx-06" }, 5.100, 2.000),
        (new[] { "ir18", "ir-18", "indycar" }, 5.000, 1.970),
        (new[] { "w12", "w13", "w14", "mercedes-amg f1" }, 5.600, 2.000),
        (new[] { "next gen", "nascar cup" }, 4.900, 2.030),
    };

    public static (double Length, double Width) SizeOf(string car)
    {
        foreach (var (words, length, width) in Sizes)
            foreach (var word in words)
                if (System.Text.RegularExpressions.Regex.IsMatch(car, $"(?<![a-z0-9]){System.Text.RegularExpressions.Regex.Escape(word)}(?![a-z0-9])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)) return (length, width);
        return Default;
    }

    private const double OuterWidth = 0.52;

    private static readonly Geometry Body = Geometry.Parse(
        "M-0.06,-0.5 L0.06,-0.5 L0.17,-0.42 L0.2,-0.28 L0.2,-0.12 L0.22,0.02 L0.22,0.3 L0.2,0.42 L0.1,0.47 L-0.1,0.47 L-0.2,0.42 L-0.22,0.3 L-0.22,0.02 L-0.2,-0.12 L-0.2,-0.28 L-0.17,-0.42 Z");
    private static readonly Geometry Cabin = Geometry.Parse("M-0.12,-0.12 L0.12,-0.12 L0.14,0.02 L0.13,0.16 L-0.13,0.16 L-0.14,0.02 Z M-0.1,0.16 L-0.08,0.26 L0.08,0.26 L0.1,0.16");
    private static readonly Geometry Wing = Geometry.Parse("M-0.24,0.43 L0.24,0.43 L0.24,0.5 L-0.24,0.5 Z M-0.06,0.43 L-0.06,0.36 M0.06,0.43 L0.06,0.36");
    private static readonly Geometry Wheels = Geometry.Parse(
        "M-0.25,-0.36 h0.05 v0.15 h-0.05 Z M0.2,-0.36 h0.05 v0.15 h-0.05 Z M-0.26,0.14 h0.05 v0.16 h-0.05 Z M0.21,0.14 h0.05 v0.16 h-0.05 Z");

    static CarShape()
    {
        Body.Freeze();
        Cabin.Freeze();
        Wing.Freeze();
        Wheels.Freeze();
    }

    public static void Draw(DrawingContext dc, Brush brush, double length, double width, bool strong)
    {
        dc.PushTransform(new ScaleTransform(width / OuterWidth, length));
        double px = 1 / Math.Min(length, width / OuterWidth);
        var fill = brush.Clone();
        fill.Opacity = strong ? 0.28 : 0.16;
        fill.Freeze();
        var pen = new Pen(brush, (strong ? 1.6 : 1.2) * px) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        var dark = new Pen(Ui.Res<Brush>("Bg"), 3.5 * px) { LineJoin = PenLineJoin.Round };
        dark.Freeze();
        dc.DrawGeometry(null, dark, Body);
        dc.DrawGeometry(fill, pen, Body);
        dc.DrawGeometry(null, pen, Cabin);
        dc.DrawGeometry(fill, pen, Wing);
        dc.DrawGeometry(brush, null, Wheels);
        dc.Pop();
    }
}

internal sealed class ReplayCardRow : Panel
{
    private const double MinCard = 270, MaxCard = 460, Gap = 10;

    private (int PerRow, double Width) Fit(double width)
    {
        int n = Math.Max(1, InternalChildren.Count);
        if (double.IsInfinity(width)) return (n, MaxCard);
        int perRow = Math.Clamp((int)((width + Gap) / (MinCard + Gap)), 1, n);
        return (perRow, Math.Min(MaxCard, (width - Gap * (perRow - 1)) / perRow));
    }

    protected override Size MeasureOverride(Size available)
    {
        var (perRow, w) = Fit(available.Width);
        double height = 0, row = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Measure(new Size(w, double.PositiveInfinity));
            row = Math.Max(row, InternalChildren[i].DesiredSize.Height);
            if (i % perRow == perRow - 1 || i == InternalChildren.Count - 1) { height += row + (height > 0 ? Gap : 0); row = 0; }
        }
        return new Size(double.IsInfinity(available.Width) ? perRow * w + Gap * (perRow - 1) : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (perRow, w) = Fit(final.Width);
        double y = 0;
        for (int start = 0; start < InternalChildren.Count; start += perRow)
        {
            int count = Math.Min(perRow, InternalChildren.Count - start);
            double rowHeight = 0;
            for (int k = 0; k < count; k++) rowHeight = Math.Max(rowHeight, InternalChildren[start + k].DesiredSize.Height);
            double x = (final.Width - (count * w + Gap * (count - 1))) / 2;
            for (int k = 0; k < count; k++) InternalChildren[start + k].Arrange(new Rect(x + k * (w + Gap), y, w, rowHeight));
            y += rowHeight + Gap;
        }
        return final;
    }
}

internal sealed class ReplayCard : Tile
{
    private const double ValueSize = 19;
    private readonly bool _imperial;
    private readonly ScaleTransform _throttle = new(0, 1), _brake = new(0, 1);
    private readonly RotateTransform _wheel = new();
    private readonly TextBlock _speed = new(), _gear = new(), _rpm = new(), _steer = new(), _throttleText = new(), _brakeText = new();
    private readonly FrameworkElement _boost;
    private int _lastSpeed = int.MinValue, _lastGear = int.MinValue, _lastRpm = int.MinValue, _lastSteer = int.MinValue, _lastThrottle = int.MinValue, _lastBrake = int.MinValue;
    private bool? _followed, _shown, _boosting;

    public ReplayCar Car { get; }

    public ReplayCard(ReplayCar car, bool imperial, Action follow)
    {
        Car = car;
        _imperial = imperial;
        Style = Ui.Res<Style>("Card");
        Padding = new Thickness(16, 12, 16, 14);
        Cursor = Cursors.Hand;
        ToolTip = "Follow this car";
        MouseLeftButtonUp += (_, e) => { follow(); e.Handled = true; };

        var swatch = new Border { Width = 4, Margin = new Thickness(0, 0, 10, 0) };
        swatch.SetResourceReference(Border.BackgroundProperty, car.Line.BrushKey);
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = car.Line.Driver.Length > 0 ? car.Line.Driver : car.Line.Short, Style = Ui.Res<Style>("Body"), FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
        var sub = TextFit.TipWhenTrimmed(new TextBlock { Text = car.Line.Driver.Length > 0 ? car.Line.Short : "", Style = Ui.Res<Style>("Caption"), FontSize = 12, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
        if (car.Line.Reference) sub.Text = sub.Text.Length > 0 ? sub.Text + " · reference" : "Reference";
        sub.ToolTip = sub.Text;
        var time = new TextBlock { Text = StatFormat.LapTime(car.Trace.LapTime), Style = Ui.Res<Style>("Body"), FontSize = 14.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top };
        Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        var boost = Ui.Chip("P2P", StatsUi.Hue("Coral").Soft, StatsUi.Hue("Coral").Deep);
        boost.Margin = new Thickness(0, 0, 8, 0);
        boost.VerticalAlignment = VerticalAlignment.Top;
        boost.Visibility = Visibility.Collapsed;
        boost.ToolTip = "Push-to-pass in use";
        _boost = boost;
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Children = { boost, time } };
        var head = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(swatch, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(swatch);
        head.Children.Add(right);
        head.Children.Add(new StackPanel { Children = { name, sub } });

        var pedals = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        pedals.ColumnDefinitions.Add(new ColumnDefinition());
        pedals.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        pedals.ColumnDefinitions.Add(new ColumnDefinition());
        pedals.Children.Add(Bar("Throttle", StatsUi.Hue("Sage").Deep, _throttle, _throttleText));
        var brakeBar = Bar("Brake", "Danger", _brake, _brakeText);
        Grid.SetColumn(brakeBar, 2);
        pedals.Children.Add(brakeBar);

        var values = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        for (int c = 0; c < 3; c++) values.ColumnDefinitions.Add(new ColumnDefinition());
        values.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        values.Children.Add(Value(_speed, imperial ? "mph" : "km/h", "speed"));
        values.Children.Add(Value(_gear, "Gear", "gear"));
        values.Children.Add(Value(_rpm, "RPM", "rpm"));
        var steer = Value(_steer, "Steer", "steering");
        _steer.MinWidth = 44;
        steer.ToolTip = "Steering wheel angle in degrees; the wheel shows which way";
        if (car.Trace[TraceChannel.Steering] is not null)
        {
            var wheel = new Icon { Data = Ui.Res<Geometry>("I.Wheel"), Width = 32, Height = 32, StrokeThickness = 2.4, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _wheel };
            wheel.SetResourceReference(Icon.ForegroundProperty, LapAnalysisView.LaneHue("steering"));
            steer = new DockPanel { Children = { wheel, steer }, ToolTip = steer.ToolTip };
            DockPanel.SetDock(wheel, Dock.Left);
        }
        values.Children.Add(steer);
        for (int c = 0; c < values.Children.Count; c++) Grid.SetColumn(values.Children[c], c);
        Child = new StackPanel { Children = { head, pedals, values } };
    }

    private static FrameworkElement Bar(string label, string brush, ScaleTransform scale, TextBlock percent)
    {
        var fill = new Border { Height = 6, RenderTransform = scale, HorizontalAlignment = HorizontalAlignment.Stretch };
        fill.SetResourceReference(Border.BackgroundProperty, brush);
        var track = new Border { Height = 6, Child = fill, Margin = new Thickness(0, 5, 0, 0) };
        track.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        percent.Style = Ui.Res<Style>("Body");
        percent.FontSize = 13;
        percent.FontWeight = FontWeights.SemiBold;
        percent.Text = "–";
        percent.MinWidth = 40;
        percent.TextAlignment = TextAlignment.Right;
        percent.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Typography.SetNumeralAlignment(percent, FontNumeralAlignment.Tabular);
        var top = new DockPanel();
        DockPanel.SetDock(percent, Dock.Right);
        top.Children.Add(percent);
        top.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        return new StackPanel { Children = { top, track } };
    }

    private static FrameworkElement Value(TextBlock value, string label, string lane)
    {
        value.Style = Ui.Res<Style>("Body");
        value.SetResourceReference(TextBlock.ForegroundProperty, LapAnalysisView.LaneHue(lane));
        value.FontSize = ValueSize;
        value.FontWeight = FontWeights.SemiBold;
        value.Text = "–";
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        return new StackPanel { Children = { value, new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), FontSize = 12 } } };
    }

    private static void Set(TextBlock text, ref int last, int value, Func<int, string> format)
    {
        if (value == last) return;
        last = value;
        text.Text = value == int.MinValue ? "–" : format(value);
    }

    public void Update(bool followed, bool shown)
    {
        if (_followed != followed)
        {
            _followed = followed;
            if (followed) SetResourceReference(BorderBrushProperty, "AccentText");
            else ClearValue(BorderBrushProperty);
        }
        if (_shown != shown) { _shown = shown; Opacity = shown ? 1 : 0.5; }
        var t = Car.Trace;
        double d = Car.Distance;
        double? throttle = t.At(TraceChannel.Throttle, d), brake = t.At(TraceChannel.Brake, d);
        _throttle.ScaleX = Math.Clamp(throttle ?? 0, 0, 1);
        _brake.ScaleX = Math.Clamp(brake ?? 0, 0, 1);
        var culture = CultureInfo.CurrentCulture;
        Set(_throttleText, ref _lastThrottle, throttle is { } th ? (int)Math.Round(Math.Clamp(th, 0, 1) * 100) : int.MinValue, v => v.ToString(culture) + "%");
        Set(_brakeText, ref _lastBrake, brake is { } br ? (int)Math.Round(Math.Clamp(br, 0, 1) * 100) : int.MinValue, v => v.ToString(culture) + "%");
        Set(_speed, ref _lastSpeed, t.At(TraceChannel.Speed, d) is { } v ? (int)Math.Round(v * (_imperial ? 2.236936 : 3.6)) : int.MinValue, x => x.ToString(culture));
        Set(_gear, ref _lastGear, t.At(TraceChannel.Gear, d) is { } g ? (int)Math.Round(g) : int.MinValue, x => LapAnalysisView.Format("gear", x));
        Set(_rpm, ref _lastRpm, t.At(TraceChannel.Rpm, d) is { } r ? (int)Math.Round(r / 10) * 10 : int.MinValue, x => x.ToString(culture));
        int steer = t.At(TraceChannel.Steering, d) is { } a ? (int)Math.Round(-a * 180 / Math.PI) : int.MinValue;
        if (steer != _lastSteer && steer != int.MinValue) _wheel.Angle = steer;
        Set(_steer, ref _lastSteer, steer, x => Math.Abs(x).ToString(culture) + "°");
        bool boosting = Car.Line.BoostAt(d);
        if (_boosting != boosting) { _boosting = boosting; _boost.Visibility = boosting ? Visibility.Visible : Visibility.Collapsed; }
    }
}

internal sealed class ReplayMiniMap : FrameworkElement
{
    private const double Pad = 12;
    private readonly double[] _x, _y, _starts;
    private readonly double _step, _minX, _maxY, _spanX, _spanY;
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _dot = new();
    private readonly TranslateTransform _dotAt = new();
    private int _sector;
    private double _scale, _ox, _oy, _lastX, _lastY;

    public ReplayMiniMap((double[] X, double[] Y) line, double step, double[] starts)
    {
        (_x, _y) = line;
        _step = step;
        _starts = starts;
        _minX = _x.Min();
        _maxY = _y.Max();
        _spanX = Math.Max(10, _x.Max() - _minX);
        _spanY = Math.Max(10, _maxY - _y.Min());
        _visuals = new VisualCollection(this) { _dot };
        _dot.Transform = _dotAt;
        using (var dc = _dot.RenderOpen()) dc.DrawEllipse(Ui.Res<Brush>("AccentText"), new Pen(Ui.Res<Brush>("Surface"), 2), new Point(0, 0), 5, 5);
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    private Point At(double x, double y) => new(_ox + (x - _minX) * _scale, _oy + (_maxY - y) * _scale);

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Fit();
        InvalidateVisual();
    }

    private void Fit()
    {
        double w = ActualWidth - 2 * Pad, h = ActualHeight - 2 * Pad;
        _scale = Math.Max(0.001, Math.Min(w / _spanX, h / _spanY));
        _ox = Pad + (w - _spanX * _scale) / 2;
        _oy = Pad + (h - _spanY * _scale) / 2;
        Move(_lastX, _lastY);
    }

    public void SetSector(int sector)
    {
        _sector = sector;
        InvalidateVisual();
    }

    public void Move(double x, double y)
    {
        _lastX = x;
        _lastY = y;
        var p = At(x, y);
        _dotAt.X = p.X;
        _dotAt.Y = p.Y;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 2 * Pad + 10) return;
        Fit();
        var line = new Pen(Ui.Res<Brush>("LineStrong"), 2) { LineJoin = PenLineJoin.Round };
        line.Freeze();
        dc.DrawGeometry(null, line, Stretch(0, _x.Length - 1));
        if (_starts.Length > 1)
        {
            int a = (int)(_starts[_sector] / _step), b = _sector + 1 < _starts.Length ? (int)(_starts[_sector + 1] / _step) : _x.Length - 1;
            var lit = new Pen(Ui.Res<Brush>("AccentText"), 3) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            lit.Freeze();
            dc.DrawGeometry(null, lit, Stretch(Math.Clamp(a, 0, _x.Length - 1), Math.Clamp(b, 0, _x.Length - 1)));
            var edge = new Pen(Ui.Res<Brush>("LineStrong"), 1.5);
            edge.Freeze();
            for (int k = 0; k < _starts.Length; k++)
            {
                int i = Math.Clamp((int)(_starts[k] / _step), 0, _x.Length - 1);
                var p = At(_x[i], _y[i]);
                bool on = k == _sector;
                dc.DrawEllipse(Ui.Res<Brush>(on ? "AccentText" : "Surface"), edge, p, 8, 8);
                var n = LapAnalysisView.Text(this, (k + 1).ToString(CultureInfo.CurrentCulture), Ui.Res<Brush>(on ? "OnAccent" : "Heading"), 10, bold: true);
                dc.DrawText(n, new Point(p.X - n.Width / 2, p.Y - n.Height / 2));
            }
        }
    }

    private StreamGeometry Stretch(int a, int b)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(At(_x[a], _y[a]), false, false);
            for (int i = a + 1; i <= b; i++) g.LineTo(At(_x[i], _y[i]), true, true);
        }
        geo.Freeze();
        return geo;
    }
}

internal sealed class ReplayScrubber : FrameworkElement
{
    private const double Thumb = 7;
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _fill = new(), _thumb = new();
    private readonly ScaleTransform _fillScale = new(0, 1);
    private readonly TranslateTransform _thumbAt = new();
    private double[] _marks;
    private double _value;

    public event Action<double>? Seeking;

    public ReplayScrubber(double[] marks)
    {
        _marks = marks;
        _visuals = new VisualCollection(this) { _fill, _thumb };
        _fill.Transform = _fillScale;
        _thumb.Transform = _thumbAt;
        Cursor = Cursors.Hand;
        ToolTip = "Drag to move through the lap (← → step a second, Shift for five)";
        using var dc = _thumb.RenderOpen();
        dc.DrawEllipse(Ui.Res<Brush>("AccentText"), new Pen(Ui.Res<Brush>("Surface"), 2), new Point(0, 0), Thumb, Thumb);
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 1);
            Place();
        }
    }

    public void SetMarks(double[] marks)
    {
        _marks = marks;
        InvalidateVisual();
    }

    private double Left => Thumb;

    private double Width0 => Math.Max(1, ActualWidth - 2 * Thumb);

    private void Place()
    {
        _fillScale.ScaleX = _value;
        _thumbAt.X = Left + _value * Width0;
        _thumbAt.Y = ActualHeight / 2;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        using (var dc = _fill.RenderOpen()) dc.DrawRectangle(Ui.Res<Brush>("AccentText"), null, new Rect(Left, ActualHeight / 2 - 2, Width0, 4));
        _fillScale.CenterX = Left;
        Place();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double mid = ActualHeight / 2;
        dc.DrawRectangle(Ui.Res<Brush>("Line"), null, new Rect(Left, mid - 2, Width0, 4));
        var tick = new Pen(Ui.Res<Brush>("TextMuted"), 1.5);
        tick.Freeze();
        foreach (double m in _marks)
        {
            double x = Math.Round(Left + m * Width0) + 0.5;
            dc.DrawLine(tick, new Point(x, mid - 7), new Point(x, mid + 7));
        }
    }

    private void Seek(MouseEventArgs e) => Seeking?.Invoke(Math.Clamp((e.GetPosition(this).X - Left) / Width0, 0, 1));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        Seek(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) Seek(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }
}

internal sealed class SectorTable : Grid
{
    private readonly List<Border> _rows = new();
    private int _current = -1;

    public SectorTable(List<ReplayCar> cars, IReadOnlyList<double> startsPct)
    {
        var times = cars.Select(c => LapTrace.SectorTimes(c.Trace, startsPct)).ToList();
        int sectors = times[0].Length;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in cars) ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r <= sectors + 1; r++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Add(new TextBlock { Text = "Sectors", Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 6) }, 0, 0);
        for (int c = 0; c < cars.Count; c++)
        {
            var swatch = new Border { Height = 3, Width = 34, Margin = new Thickness(12, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, cars[c].Line.BrushKey);
            swatch.ToolTip = cars[c].Line.Label;
            Add(swatch, 0, c + 1);
        }
        for (int k = 0; k <= sectors; k++)
        {
            int row = k + 1;
            bool total = k == sectors;
            var band = new Border { Margin = new Thickness(-6, 0, -6, 0) };
            SetRow(band, row);
            SetColumnSpan(band, cars.Count + 1);
            Children.Add(band);
            if (!total) _rows.Add(band);
            Add(new TextBlock { Text = total ? "Lap" : $"S{k + 1}", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 3, 14, 3) }, row, 0);
            double best = total ? cars.Min(c => c.Trace.LapTime) : times.Min(t => t[k]);
            for (int c = 0; c < cars.Count; c++)
            {
                double v = total ? cars[c].Trace.LapTime : times[c][k];
                var text = new TextBlock
                {
                    Text = total ? StatFormat.LapTime(v) : v.ToString("0.000", CultureInfo.CurrentCulture), Style = Ui.Res<Style>("Caption"),
                    HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 3, 0, 3),
                };
                Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
                if (cars.Count > 1 && Math.Abs(v - best) < 0.0005)
                {
                    text.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                    text.FontWeight = FontWeights.SemiBold;
                }
                Add(text, row, c + 1);
            }
        }
    }

    private void Add(UIElement e, int row, int col)
    {
        SetRow(e, row);
        SetColumn(e, col);
        Children.Add(e);
    }

    public void SetCurrent(int sector)
    {
        if (sector == _current) return;
        if (_current >= 0 && _current < _rows.Count) _rows[_current].Background = null;
        _current = sector;
        if (sector >= 0 && sector < _rows.Count) _rows[sector].SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
    }
}

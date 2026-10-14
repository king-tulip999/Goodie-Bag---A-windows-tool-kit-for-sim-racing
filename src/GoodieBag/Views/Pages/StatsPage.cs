using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.Plugins;
using GoodieBag.Plugins.BuiltIn;
using GoodieBag.Services;
using GoodieBag.SessionStats;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class StatsPage : UserControl, IPage, IScreenshotStates, IPageHistory
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private readonly TextBlock _driver = new() { Style = Ui.Res<Style>("H3") };
    private readonly ContentControl _progress = new() { Focusable = false }, _content = new() { Focusable = false };
    private readonly ScrollViewer _scroll;
    private readonly SessionFilter _filter = new();
    private readonly HashSet<int> _ticked = new();
    private long? _sessionId;
    private List<int>? _analysisLaps;
    private string _reference = "session";
    private ReplayMemory _replay = new();

    internal void StartReplayAt(double distance) => _replay.Distance = distance;
    private double _overviewOffset;
    private int _version;
    private bool _shown;

    public StatsPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        Content = _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body) };
        Ui.ChainWheel(_scroll);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _sessionId is null || Keyboard.FocusedElement is TextBox) return;
            e.Handled = true;
            Back();
        };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, Ui.HeaderGap) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(Ui.SplitRow(Ui.PluginTitle(PluginIds.SessionStats, "Session Stats"), tools, 16, stackLeft: true));
        _driver.Margin = new Thickness(0, 6, 0, 0);
        header.Children.Add(_driver);
        _body.Children.Add(header);
        _body.Children.Add(_progress);
        _body.Children.Add(_content);
        Ui.Anchor(_content, "stats.all");
        if (Stats is { } stats)
        {
            var number = Ui.IconButton("I.Pencil", "Your car number (blank = the one you used most, league races first)", () => _ = EditNumberAsync(), "QuietIconButton");
            number.Margin = new Thickness(0, 0, 8, 0);
            tools.Children.Add(number);
            tools.Children.Add(StatsUi.RangePicker(stats, "page", Changed));
            var units = Ui.Segmented(new[] { "Metric", "Imperial" }, stats.Settings.Imperial ? 1 : 0, i => { stats.Settings.Imperial = i == 1; Changed(); }, "statsPageUnits");
            units.Margin = new Thickness(12, 0, 0, 0);
            tools.Children.Add(units);
        }
    }

    private SessionStatsService? Stats => SessionStatsPlugin.Of(_app);

    private async Task EditNumberAsync()
    {
        if (Stats is not { } stats) return;
        var typed = await _window.Prompt("Car number", "The number shown next to your name. Leave it empty to use the one you raced with most (league races first).",
            stats.Settings.DriverNumber ?? "", text => text.Trim().TrimStart('#').Length > 4 ? "Up to 4 characters" : null);
        if (typed is null) return;
        stats.Settings.DriverNumber = typed;
        stats.Settings.Normalize();
        Changed();
    }

    public void OnShown()
    {
        _shown = true;
        if (Stats is { } stats) stats.Changed += OnStatsChanged;
        Refresh();
        Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _shown = false;
        if (Stats is { } stats) stats.Changed -= OnStatsChanged;
    }

    private void OnStatsChanged()
    {
        if (_analysisLaps is not null && _shown && Stats is { } stats)
        {
            _progress.Content = StatsUi.Progress(stats);
            return;
        }
        Refresh();
    }

    private void Changed()
    {
        _app.Save();
        Refresh();
    }

    private void Open(long session, List<int>? laps = null)
    {
        if (_sessionId is null) _overviewOffset = _scroll.VerticalOffset;
        if (_sessionId != session) { _ticked.Clear(); _reference = "session"; _replay = new(); }
        _sessionId = session;
        _analysisLaps = laps;
        Refresh();
        _scroll.ScrollToTop();
        _window.RecordHistory();
    }

    private void Back()
    {
        if (_analysisLaps is not null) _analysisLaps = null;
        else _sessionId = null;
        Refresh(restoreOffset: _sessionId is null);
        _window.RecordHistory();
    }

    private sealed record View(long? Session, string? Laps);

    public object? HistoryState => new View(_sessionId, _analysisLaps is null ? null : string.Join(",", _analysisLaps));

    public void RestoreHistory(object? state)
    {
        if (state is not View view) return;
        bool toOverview = view.Session is null && _sessionId is not null;
        if (_sessionId is null && view.Session is not null) _overviewOffset = _scroll.VerticalOffset;
        if (_sessionId != view.Session) { _ticked.Clear(); _reference = "session"; _replay = new(); }
        _sessionId = view.Session;
        _analysisLaps = view.Laps?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        Refresh(restoreOffset: toOverview);
        if (!toOverview) _scroll.ScrollToTop();
    }

    private FrameworkElement Drilled(FrameworkElement view)
    {
        var back = Ui.LabeledButton("I.ChevronLeft", _analysisLaps is null ? "Session Stats" : "Session", Back);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Margin = new Thickness(0, 0, 0, 16);
        back.ToolTip = "Back (Esc)";
        return new StackPanel { Children = { back, view } };
    }

    private async void RefreshDrill(SessionStatsService stats, StatsStore store, int version)
    {
        long id = _sessionId!.Value;
        var traces = stats.Traces;
        if (_analysisLaps is { } laps)
        {
            string reference = _reference;
            var data = await Task.Run(() => traces is null ? null : LoadAnalysis(store, traces, id, laps, reference));
            if (version != _version || !_shown) return;
            if (data is null || data.Laps.Count == 0) { _analysisLaps = null; Refresh(); return; }
            var actions = new AnalysisActions(
                pick => { _reference = pick; Refresh(); },
                lap => { _analysisLaps = _analysisLaps?.Where(l => l != lap).ToList(); _ticked.Remove(lap); Refresh(); },
                () => { _app.Save(); Refresh(); },
                lap => ExportLap(store, traces!, id, lap),
                () => _ = ImportLapAsync(traces!, data.Session),
                shared => _ = DeleteSharedAsync(traces!, shared),
                _app.Save);
            var view = LapAnalysisView.Build(data, stats.Settings, _reference, actions, _replay, out _);
            _content.Content = Drilled(view);
            return;
        }
        var detail = await Task.Run(() => store.Session(id) is { } s
            ? new SessionDetail(s, store.Laps(id), traces?.LapsWithTrace(id) ?? new HashSet<int>(), store.BestLap(s.Car, s.Track, s.Config), store.Conditions(id))
            : null);
        if (version != _version || !_shown) return;
        if (detail is null) { _sessionId = null; Refresh(); return; }
        _content.Content = Drilled(StatsSessionView.Build(detail, stats.Settings.Imperial, StatsSessions.NoTraceReason(detail.Session, stats.Settings, DateTime.UtcNow), _ticked, picked => Open(id, picked.ToList())));
    }

    internal static AnalysisData? LoadAnalysis(StatsStore store, TraceStore traces, long id, IReadOnlyList<int> picked, string reference = "session")
    {
        if (store.Session(id) is not { } session) return null;
        var traced = traces.LapsWithTrace(id);
        var all = store.Laps(id).Where(l => traced.Contains(l.Lap)).Select(l => (Row: l, Trace: traces.Load(id, l.Lap))).Where(x => x.Trace is not null).ToList();
        if (all.Count == 0) return null;
        var shown = all.Where(x => picked.Contains(x.Row.Lap)).Select(x => (x.Row.Lap, x.Row.Time, x.Trace!)).ToList();
        var pool = all.Where(x => x.Row.Clean).ToList();
        if (pool.Count == 0) pool = all;
        var best = pool.MinBy(x => x.Row.Time);
        var starts = new List<double>();
        if (best.Trace is { } bt && best.Row?.Sectors is { Count: > 1 } sectors)
        {
            starts.Add(0);
            double at = 0;
            for (int k = 0; k < sectors.Count - 1; k++) { at += sectors[k]; starts.Add(bt.DistanceAtTime(at) / bt.LengthM); }
        }
        var optimal = starts.Count > 1 && pool.Count >= 2 ? LapTrace.Optimal(pool.Select(x => x.Trace!).ToList(), starts) : null;
        (string, LapTrace)? trackBest = null;
        if (traces.BestOf(session.Car, session.Track, session.Config) is { } tb && !(tb.Session == id && tb.Lap == best.Row?.Lap) && traces.Load(tb.Session, tb.Lap) is { } tbTrace)
        {
            string when = store.Session(tb.Session) is { } other ? Dates.Date(other.StartUtc.ToLocalTime()) : "";
            trackBest = ($"{StatFormat.LapTime(tb.Time)}{(when.Length > 0 ? " · " + when : "")}", tbTrace);
        }
        var shared = traces.SharedAt(session.Track, session.Config);
        (SharedLapInfo, LapTrace)? sharedPicked = null;
        if (reference.StartsWith("shared:") && long.TryParse(reference[7..], out long sid) && shared.FirstOrDefault(x => x.Id == sid) is { } info && traces.LoadShared(sid) is { } lap)
            sharedPicked = (info, lap.Trace);
        return new AnalysisData(session, shown, best.Trace is { } t && best.Row is { } br ? (br.Lap, t) : null, trackBest, optimal, starts,
            store.DriverOf(id), shared, sharedPicked, MapOf(store, session));
    }

    private static SimRacing.TrackMaps.TrackMap? MapOf(StatsStore store, SessionSummary session)
    {
        var (trackId, trackName) = store.TrackIdentity(session.Id);
        try { return SimRacing.TrackMaps.TrackMapLibrary.ForSession(trackId, trackName, session.Track, session.Config).GetAwaiter().GetResult(); }
        catch (Exception ex) { App.LogError(ex); return null; }
    }

    private void ExportLap(StatsStore store, TraceStore traces, long session, int lap)
    {
        if (_app.Demo) { _app.Notify("Demo mode", "Demo laps aren't saved to files.", NoticeKind.Info); return; }
        var summary = store.Session(session);
        var trace = traces.Load(session, lap);
        if (summary is null || trace is null) { _app.Notify("Couldn't export the lap", "Its telemetry isn't stored any more.", NoticeKind.Warning); return; }
        var row = store.Laps(session).FirstOrDefault(l => l.Lap == lap);
        var shared = new SharedLap(store.DriverOf(session), summary.Car, summary.Track, summary.Config, row?.Sectors, summary.StartUtc, trace);
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export lap", Filter = LapFile.Filter, FileName = LapFile.FileName(shared), DefaultExt = LapFile.Extension, AddExtension = true };
        if (dialog.ShowDialog(_window) != true) return;
        try
        {
            using (var file = File.Create(dialog.FileName)) LapFile.Write(file, shared);
            _app.Notify("Lap exported", Path.GetFileName(dialog.FileName), NoticeKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _app.Notify("Couldn't export the lap", ex.Message, NoticeKind.Error, important: true);
        }
    }

    private async Task ImportLapAsync(TraceStore traces, SessionSummary session)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import lap", Filter = LapFile.Filter + "|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(_window) != true) return;
        string path = dialog.FileName;
        SharedLap lap;
        try
        {
            lap = await Task.Run(() =>
            {
                using var file = File.OpenRead(path);
                return LapFile.Read(file);
            });
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _app.Notify("Couldn't import the lap", ex.Message, NoticeKind.Error, important: true);
            return;
        }
        if (!string.Equals(lap.Track, session.Track, StringComparison.OrdinalIgnoreCase) || !string.Equals(lap.Config, session.Config, StringComparison.OrdinalIgnoreCase))
        {
            string where = lap.Config.Length > 0 ? $"{lap.Track} · {lap.Config}" : lap.Track;
            _app.Notify("Different track", $"That lap is from {where}. Open a session there to compare against it.", NoticeKind.Warning, important: true);
            return;
        }
        long sid = await Task.Run(() => traces.AddShared(lap, DateTime.UtcNow));
        _reference = "shared:" + sid;
        _app.Notify("Lap imported", LapAnalysisView.SharedLabel(new SharedLapInfo(sid, lap.Driver, lap.Car, lap.Track, lap.Config, lap.LapTime, lap.RecordedUtc), session.Car) + " is the reference now.", NoticeKind.Success);
        Refresh();
    }

    private async Task DeleteSharedAsync(TraceStore traces, long id)
    {
        if (!await _window.Confirm("Remove this imported lap?", "It's taken out of every comparison. The file you imported it from stays.", "Remove", danger: true)) return;
        await Task.Run(() => traces.DeleteShared(id));
        _reference = "session";
        Refresh();
    }

    private async void Refresh(bool restoreOffset = false)
    {
        if (!_shown || Stats is not { } stats) return;
        int version = ++_version;
        _progress.Content = StatsUi.Progress(stats);
        if (stats.Problem is { } problem) { _content.Content = Ui.DangerBanner("Stats unavailable", problem); return; }
        var (name, number) = stats.Driver();
        _driver.Text = name is null ? "" : number is null ? name : $"{name} · #{number}";
        if (stats.Store is not { } store || store.Empty)
        {
            _content.Content = new Tile { Style = Ui.Res<Style>("Card"), Child = Ui.Text(StatsSessionsNote(stats), "Muted") };
            return;
        }
        if (_sessionId is not null) { RefreshDrill(stats, store, version); return; }
        var q = stats.Query();
        var range = stats.Range;
        var results = await stats.EvaluateAsync(stats.Available, q);
        var charts = await Task.Run(() => StatsCharts.Read(store, q, range, stats.Settings.TimelineCombo, stats.Settings.TimelinePickedUtc));
        var traces = stats.Traces;
        var (sessions, traced) = await Task.Run(() =>
        {
            var list = store.Sessions(q.From, q.To);
            return (list, traces?.SessionsWithTraces(list.Select(s => s.Id)) ?? new HashSet<long>());
        });
        if (version != _version || !_shown || _sessionId is not null) return;

        var page = new StackPanel();
        if (stats.Leagues is { Count: 0 }) page.Children.Add(LeagueNote(store));
        if (charts.Sessions == 0)
        {
            page.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Child = Ui.Text("No sessions in this range yet: pick a longer one to see the rest.", "Muted") });
            _content.Content = page;
            return;
        }
        var sessionsCard = StatsSessions.Card(sessions, traced, _filter, stats.Settings, Refresh, s => Open(s.Id), TraceSettings.Button(_app, stats, _window));
        page.Children.Add(StatsSections.Page(results, charts, stats, q, Changed, _app.Save, sessionsCard));
        _content.Content = page;
        if (restoreOffset)
        {
            double offset = _overviewOffset;
            await Dispatcher.InvokeAsync(() => _scroll.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void Refresh() => Refresh(false);

    private IEnumerable<(string Name, Func<Task> Enter)> LapStates()
    {
        yield return ("sessions", async () =>
        {
            if (Stats is { } stats) stats.Settings.Range = "all";
            _sessionId = null;
            Refresh();
            await Task.Delay(500);
            if (Ui.FindAnchor(this, "stats.sessions") is { } card) card.BringIntoView();
            await Task.Delay(300);
        });
        long? Traced() => Stats?.Store?.Sessions(0, long.MaxValue).FirstOrDefault(s => Stats.Traces?.Has(s.Id) == true)?.Id;
        yield return ("session", async () =>
        {
            if (Traced() is { } id) Open(id);
            await Task.Delay(500);
        });
        yield return ("session-runs", async () =>
        {
            if (Stats?.Store?.Sessions(0, long.MaxValue).FirstOrDefault(s => s.Type == "Race" && s.Laps > 18) is { } race) Open(race.Id);
            await Task.Delay(500);
            _scroll.ScrollToVerticalOffset(_scroll.ScrollableHeight * 0.55);
            await Task.Delay(300);
        });
        yield return ("analysis", async () =>
        {
            if (Traced() is { } id && Stats?.Traces?.LapsWithTrace(id).OrderBy(l => l).Take(3).ToList() is { Count: > 0 } laps) Open(id, laps);
            await Task.Delay(700);
        });
        yield return ("analysis-zoomed", async () =>
        {
            if (FindState() is { } s) { s.Zoom(s.Length * 0.32, s.Length * 0.45); s.SetCursor(s.Length * 0.38); }
            await Task.Delay(400);
        });
        yield return ("analysis-speed", async () =>
        {
            if (FindState() is { } s) { s.MapColors = MapColors.Speed; s.ResetZoom(); s.RaiseCursor(); }
            await Task.Delay(400);
        });
        yield return ("analysis-replay", async () =>
        {
            if (Stats is { } stats)
            {
                stats.Settings.ReplayZoom = 0.8;
                if (stats.Traces?.SharedAt("Circuit de Spa-Francorchamps", "Grand Prix Pits").FirstOrDefault() is { } rival) _reference = "shared:" + rival.Id;
            }
            _replay = new ReplayMemory { Distance = 2350 };
            Refresh();
            await Task.Delay(800);
            FindChild<LapReplayView>(this)?.BringIntoView();
            await Task.Delay(300);
        });
        async Task Replay(string sync, double zoom, double distance)
        {
            if (Stats is { } stats) { stats.Settings.ReplaySync = sync; stats.Settings.ReplayZoom = zoom; }
            _replay.Distance = distance;
            Refresh();
            await Task.Delay(700);
            FindChild<LapReplayView>(this)?.BringIntoView();
            await Task.Delay(200);
        }
        yield return ("analysis-replay-time", () => Replay("time", 0.62, 4300));
        yield return ("analysis-replay-whole", () => Replay("distance", 0, 1200));
        yield return ("analysis-replay-playing", async () =>
        {
            await Replay("distance", 0.8, 2350);
            if (FindChild<LapReplayView>(this) is not { } view) return;
            view.Play();
            await Task.Delay(1500);
            view.Pause();
            await Task.Delay(200);
        });
        yield return ("analysis-replay-closed", () =>
        {
            if (Stats is { } stats) stats.Settings.ReplaySync = "distance";
            _reference = "session";
            Refresh();
            return Task.Delay(300);
        });
        yield return ("traces-settings", async () =>
        {
            _sessionId = null;
            _analysisLaps = null;
            Refresh();
            await Task.Delay(600);
            if (Ui.FindAnchor(this, "stats.traces") is Button gear) { gear.BringIntoView(); await Task.Delay(200); gear.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); }
            await Task.Delay(500);
        });
        yield return ("traces-closed", () =>
        {
            TraceSettings.CloseOpen();
            if (Stats is { } stats) stats.Settings.Range = "season";
            Refresh();
            return Task.Delay(300);
        });
    }

    private AnalysisState? FindState() => FindChild<TraceChart>(this) is { } chart ? chart.State : null;

    private static T? FindChild<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found && (match is null || match(found))) return found;
            if (FindChild(child, match) is { } deeper) return deeper;
        }
        return null;
    }

    private static string StatsSessionsNote(SessionStatsService stats) =>
        stats.Importing is not null ? "Your telemetry files are being read: sessions show up here as they're done."
            : "No sessions yet: drive in iRacing and they show up here.";

    private static FrameworkElement LeagueNote(StatsStore store)
    {
        var seen = store.LeaguesSeen();
        string detail = "League figures count sessions by iRacing league id: add yours in your league's settings."
            + (seen.Count > 0 ? " League ids in your sessions: " + string.Join(", ", seen.Take(5).Select(l => $"{l.Id} ({l.Sessions})")) + "." : "");
        var note = Ui.Notice("Accent", "I.Flag", "Which league is yours?", detail);
        note.Margin = new Thickness(0, 0, 0, Ui.PanelSpacing);
        return note;
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        foreach (var state in LapStates()) yield return state;
        foreach (var range in new[] { "week", "month", "all" })
        {
            var r = range;
            yield return ("range-" + r, () =>
            {
                if (Stats is { } stats) { stats.Settings.Range = r; Refresh(); }
                return Task.Delay(400);
            });
        }
        foreach (var state in new[] { "activity", "cars" })
        {
            string anchor = "stats." + state;
            yield return (state, async () =>
            {
                await Task.Delay(300);
                Ui.FindAnchor(this, anchor)?.BringIntoView();
                await Task.Delay(300);
            });
        }
        yield return ("imperial", () =>
        {
            if (Stats is { } stats) { stats.Settings.Range = "season"; stats.Settings.Imperial = true; Refresh(); }
            return Task.Delay(400);
        });
        yield return ("metric", () =>
        {
            if (Stats is { } stats) { stats.Settings.Imperial = false; Refresh(); }
            return Task.Delay(400);
        });
    }
}

internal static class StatsUi
{
    private static readonly string[] Ranges = { "Week", "Month", "Season", "All time" };

    public static FrameworkElement RangePicker(SessionStatsService stats, string group, Action changed) =>
        Ui.Segmented(Ranges, (int)stats.Range, i =>
        {
            stats.Settings.Range = SessionStatsService.NameOf((StatRange)i);
            changed();
        }, "statsRange" + group, new[] { "Since Monday", "This calendar month", "This iRacing season (12 weeks + week 13)", "Everything" });

    internal static string? TipOf(StatDef def, StatResult result, bool full)
    {
        var tip = def.Help;
        if (def.LiveOnly) tip = (tip is null ? "" : tip + ". ") + "Live sessions only";
        if (result.NoData) tip = (tip is null ? "" : tip + ". ") + "Nothing recorded for this in the range";
        if (full) tip = string.Join("\n", new[] { result.NoData ? null : result.Value, result.Detail, tip }.Where(x => !string.IsNullOrEmpty(x)));
        return string.IsNullOrEmpty(tip) ? null : tip;
    }

    internal static (string Soft, string Deep) Hue(string name) => name switch
    {
        "Gold" => ("Tool1Soft", "Tool1Deep"), "Slate" => ("Tool2Soft", "Tool2Deep"), "Plum" => ("Tool3Soft", "Tool3Deep"),
        "Coral" => ("Tool4Soft", "Tool4Deep"), "Sage" => ("Tool5Soft", "Tool5Deep"), "Orange" => ("Tool6Soft", "Tool6Deep"),
        "Cyan" => ("Tool7Soft", "Tool7Deep"), _ => Hues.Accent,
    };

    internal readonly record struct Tone(string Soft, string Deep, string? Icon, bool Fill);

    internal static Tone? ToneOf(StatDef def, StatResult result)
    {
        if (result.NoData) return null;
        var (goldSoft, goldDeep) = Hue("Gold");
        var (sageSoft, sageDeep) = Hue("Sage");
        var (coralSoft, coralDeep) = Hue("Coral");
        bool some = result.Value != "0";
        return def.Id switch
        {
            "race.wins" or "race.podiums" or "race.top5" when some => new Tone(goldSoft, goldDeep, "I.Trophy", true),
            "inc.zero" when some => new Tone(sageSoft, sageDeep, "I.Check", true),
            "race.gained" or "race.irating" or "race.sr" when result.Value.StartsWith('+') => new Tone(sageSoft, sageDeep, null, false),
            "race.gained" or "race.irating" or "race.sr" when result.Value.StartsWith('−') => new Tone(coralSoft, coralDeep, null, false),
            _ => null,
        };
    }

    public static FrameworkElement? Progress(SessionStatsService stats)
    {
        if (stats.Importing is not { } p || p.Total == 0) return null;
        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(Ui.Text($"Reading telemetry files · {p.Done} / {p.Total}", "Caption"));
        stack.Children.Add(new Meter { Value = (double)p.Done / p.Total, Fill = Ui.Res<Brush>("Accent"), Margin = new Thickness(0, 4, 0, 0) });
        return stack;
    }
}

internal static class StatsSections
{
    public static FrameworkElement Page(List<(StatDef Def, StatResult Result)> results, StatsCharts.Data charts, SessionStatsService service, StatQuery q, Action changed,
        Action save, FrameworkElement? sessions = null)
    {
        var byId = results.ToDictionary(r => r.Def.Id, r => r);
        (StatDef Def, StatResult Result)? Get(string id) => byId.TryGetValue(id, out var r) ? r : null;
        IEnumerable<(StatDef, StatResult)> Many(params string[] ids) => ids.Select(Get).OfType<(StatDef, StatResult)>();

        var grid = new PanelGrid(service.Settings.PageLayout, save) { LiveEdit = true, RowHeight = 1, Gap = 0, PanelSpacing = Ui.PanelSpacing };
        int order = 0;
        void Add(string id, string label, FrameworkElement? element)
        {
            if (element is null) return;
            element.Margin = new Thickness(0);
            grid.Add(new PanelDef(id, label, _ => element) { FullWidth = true, Order = order++ });
        }

        var groups = new StatGrid { MinTileWidth = 420, Gap = 40, RowGap = 28, Columns = 3 };
        void Group(string title, IEnumerable<(StatDef, StatResult)> stats)
        {
            var list = stats.ToList();
            if (list.Count > 0) groups.Children.Add(Chart(title, null, Metrics(list)!));
        }
        Group("Results", Many("race.starts", "race.finishes", "race.wins", "race.podiums", "race.top5", "race.league"));
        Group("Positions and ratings", Many("race.avgstart", "race.avgfinish", "race.gained", "race.irating", "race.sr"));
        Group("Incidents, pit lane and weather", Many("inc.total", "inc.zero", "style.pits", "style.fuel", "cond.rain", "cond.night"));
        var known = new HashSet<string> { "pace.laps", "time.total", "pace.distance", "pace.top",
            "time.week", "time.month", "time.weekly", "time.streak", "time.session", "time.stint", "time.garage", "time.practice", "time.qualify", "time.race",
            "car.unique", "track.unique", "combo.top", "car.time", "car.laps", "track.time", "track.laps", "class.time",
            "race.starts", "race.finishes", "race.wins", "race.podiums", "race.top5", "race.league", "race.avgstart", "race.avgfinish", "race.gained", "race.irating", "race.sr",
            "inc.total", "inc.zero", "style.fuel", "style.pits", "cond.rain", "cond.night" };
        Group("More", results.Where(r => !known.Contains(r.Def.Id)));
        Add("hero", "Overview", Hero(charts, q, Many("pace.laps", "time.total", "pace.distance", "pace.top").ToList(), groups));
        Add("sessions", "Recent sessions", sessions);

        Add("performance", "Performance", Section("Performance", "I.Stats", null, PickPair(charts, service, changed),
            StatsLayout.Pair(
                Chart("Personal best progression", "Your best clean lap in this car and track, session by session", charts.Timeline.Count == 0 ? Empty("No clean laps for this pair yet.") : StatsCharts.Progression(charts.Timeline)),
                Chart("Lap consistency", "Clean laps per session: the box holds the middle half, the line runs from the fastest lap to the 90th percentile, the tick is the median",
                    charts.Consistency.Count == 0 ? Empty("No session with 3 or more clean laps in this pair yet.") : StatsCharts.Consistency(charts.Consistency)), 1050, gap: 32)!));

        var types = charts.SessionTypes;
        var activity = Section("Driving activity", "I.Clock", $"{charts.Sessions:N0} session{(charts.Sessions == 1 ? "" : "s")}", null, Stack(
            Chart("Session mix", null, StatsCharts.SessionMix(types)),
            StatsLayout.Pair(
                Chart("When you drive", "Driving time by the hour each session started, local time", StatsCharts.Heatmap(charts.Heatmap)),
                Chart(charts.Trend.Title, "Driving time per period of this range", StatsCharts.Trend(charts.Trend)), 1050, gap: 32),
            Metrics(Many("time.week", "time.month", "time.weekly", "time.streak", "time.session", "time.stint", "time.garage"))));
        Ui.Anchor(activity, "stats.activity");
        Add("activity", "Driving activity", activity);

        string? variety = string.Join(" · ", new[] { Count(Get("car.unique"), "car"), Count(Get("track.unique"), "track") }.OfType<string>());
        var rankings = new StatGrid { MinTileWidth = 280, Gap = 32, Columns = 3 };
        rankings.Children.Add(Chart("Cars", "By driving time", StatsCharts.Ranked(charts.Cars, "Accent", "No cars in this range")));
        rankings.Children.Add(Chart("Tracks", "By driving time", StatsCharts.Ranked(charts.Tracks, "Slate", "No tracks in this range")));
        rankings.Children.Add(Chart("Classes", "By driving time", StatsCharts.Ranked(charts.Classes, "Gold", "No classes in this range")));
        var cars = Section("Cars & tracks", "I.Flag", variety.Length > 0 ? variety : null, null, Stack(rankings, PersonalBests(charts, q, Get("combo.top"))));
        Ui.Anchor(cars, "stats.cars");
        Add("cars", "Cars & tracks", cars);
        var tip = Ui.Text("Tip: drag a panel by an empty spot to move it in the order", "Caption");
        tip.TextWrapping = TextWrapping.Wrap;
        var tipLine = Ui.IconText("I.Info", tip, "TextMuted");
        tipLine.Margin = new Thickness(2, 0, 0, 8);
        tipLine.HorizontalAlignment = HorizontalAlignment.Center;
        return new StackPanel { Children = { grid, tipLine } };
    }

    private static string? Count((StatDef Def, StatResult Result)? stat, string noun) =>
        stat is { Result: { NoData: false } r } ? $"{r.Value} {noun}{(r.Value == "1" ? "" : "s")}" : null;

    internal static FrameworkElement Empty(string text) => new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 4, 0, 0) };

    internal static Tile Section(string title, string icon, string? note, FrameworkElement? tools, FrameworkElement body)
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        var mark = new Icon { Data = Ui.Res<Geometry>(icon), Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        mark.SetResourceReference(Control.ForegroundProperty, "AccentText");
        left.Children.Add(mark);
        var text = Ui.Text(title, "H2");
        text.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(text);
        if (note is not null)
            left.Children.Add(new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
        FrameworkElement head = left;
        if (tools is not null)
        {
            tools.Margin = new Thickness(0);
            head = Ui.SplitRow(left, tools, stackLeft: true);
        }
        body.Margin = new Thickness(0, 18, 0, 0);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(24, 20, 24, 24), Child = new StackPanel { Children = { head, body } } };
    }

    internal static StackPanel Stack(params FrameworkElement?[] parts)
    {
        var stack = new StackPanel();
        foreach (var part in parts.OfType<FrameworkElement>())
        {
            if (stack.Children.Count > 0)
            {
                var rule = new Border { Height = 1, Margin = new Thickness(0, 22, 0, 20) };
                rule.SetResourceReference(Border.BackgroundProperty, "Line");
                stack.Children.Add(rule);
            }
            stack.Children.Add(part);
        }
        return stack;
    }

    internal static StackPanel Chart(string title, string? caption, FrameworkElement content)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("H3") });
        if (caption is not null) stack.Children.Add(new TextBlock { Text = caption, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0) });
        content.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(content);
        return stack;
    }

    private static FrameworkElement? Metrics(IEnumerable<(StatDef Def, StatResult Result)> stats, double size = 22)
    {
        var grid = new StatGrid { MinTileWidth = 150, Gap = 0, RowGap = 16 };
        foreach (var (def, result) in stats)
        {
            var element = Metric(def, result, size);
            if (def.Kind == StatKind.Text && !result.NoData) StatGrid.SetSpan(element, 2);
            grid.Children.Add(element);
        }
        return grid.Children.Count == 0 ? null : grid;
    }

    private static FrameworkElement Metric(StatDef def, StatResult result, double size, string? hue = null)
    {
        var tone = StatsUi.ToneOf(def, result);
        bool name = def.Kind == StatKind.Text && !result.NoData;
        var value = new TextBlock { Text = result.NoData ? "No data" : result.Value, Style = Ui.Res<Style>("BigValue"), FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (result.NoData)
        {
            value.FontSize = 15;
            value.FontWeight = FontWeights.SemiBold;
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
        }
        else
        {
            if (name) { value.FontSize = 16; value.MaxHeight = 16 * 2.8; value.TextTrimming = TextTrimming.CharacterEllipsis; }
            if (tone is { } t) value.SetResourceReference(TextBlock.ForegroundProperty, t.Deep);
            else if (hue is not null) value.SetResourceReference(TextBlock.ForegroundProperty, hue);
        }
        var label = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
        if (tone is { Icon: { } icon } mark)
        {
            var glyph = new Icon { Data = Ui.Res<Geometry>(icon), Width = 13, Height = 13, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(Control.ForegroundProperty, mark.Deep);
            label.Children.Add(glyph);
        }
        label.Children.Add(new TextBlock { Text = def.Label, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var stack = new StackPanel { Children = { value, label } };
        if (result.Detail is { Length: > 0 } detail && !result.NoData)
        {
            var line = new TextBlock { Text = detail, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
            line.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
            stack.Children.Add(line);
        }
        var cell = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(14, 2, 12, 2), Child = stack, ToolTip = StatsUi.TipOf(def, result, full: name) };
        cell.SetResourceReference(Border.BorderBrushProperty, "Line");
        return cell;
    }

    private static Tile Hero(StatsCharts.Data charts, StatQuery q, List<(StatDef Def, StatResult Result)> headline, StatGrid racecraft)
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = "Personal best", Style = Ui.Res<Style>("Eyebrow") });
        if (charts.Bests.FirstOrDefault() is { } pb)
        {
            var lap = new TextBlock { Text = StatFormat.LapTime(pb.Best), Style = Ui.Res<Style>("BigValue"), FontSize = 48, Margin = new Thickness(0, 4, 0, 0) };
            lap.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            left.Children.Add(lap);
            left.Children.Add(new TextBlock { Text = pb.Car, Style = Ui.Res<Style>("H3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            left.Children.Add(new TextBlock { Text = pb.Config.Length > 0 ? $"{pb.Track} · {pb.Config}" : pb.Track, Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap });
            var facts = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            void Fact(FrameworkElement element) { element.Margin = new Thickness(0, 0, 10, 4); element.VerticalAlignment = VerticalAlignment.Center; facts.Children.Add(element); }
            if (pb.InRange && q.From > 0)
            {
                var (goldSoft, goldDeep) = StatsUi.Hue("Gold");
                Fact(Ui.Chip("New in this range", goldSoft, goldDeep));
            }
            Fact(new TextBlock { Text = $"Set {Dates.Date(pb.When.ToLocalTime())} · {pb.Laps:N0} clean lap{(pb.Laps == 1 ? "" : "s")} in this range", Style = Ui.Res<Style>("Caption") });
            if (charts.HeroFirst is { } first && first.Lap - pb.Best > 0.0005)
            {
                var gain = new TextBlock { Text = $"{StatFormat.Value(StatKind.LapDelta, pb.Best - first.Lap, false)} since your first session here ({Dates.Date(first.When.ToLocalTime())})", Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold };
                gain.SetResourceReference(TextBlock.ForegroundProperty, StatsUi.Hue("Sage").Deep);
                Fact(gain);
            }
            left.Children.Add(facts);
            left.ToolTip = "The all-time best clean lap in the car and track you drove last in this range";
        }
        else left.Children.Add(new TextBlock { Text = "No clean laps in this range yet", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 8, 0, 0) });

        var right = new StatGrid { MinTileWidth = 190, Gap = 0, RowGap = 18, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (def, result) in headline) right.Children.Add(Metric(def, result, 30));
        var hero = StatsLayout.Pair(left, right, 900, 4, 6, 40, center: true)!;
        var body = racecraft.Children.Count > 0 ? Stack(hero, racecraft) : hero;
        var tile = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(28, 24, 28, 26), Child = body };
        tile.SetResourceReference(Border.BorderBrushProperty, "AccentEdge");
        return tile;
    }

    private static FrameworkElement? PickPair(StatsCharts.Data charts, SessionStatsService service, Action changed)
    {
        if (charts.Bests.Count == 0) return null;
        var combos = charts.Bests.Select(b => (b.Key, Label: $"{b.Car} · {b.Track}{(b.Config.Length > 0 ? " · " + b.Config : "")}")).ToList();
        var pick = Ui.Combo(combos.Select(c => new Choice(c.Key, c.Label)), charts.TimelineKey ?? combos[0].Key, c => { service.Settings.TimelineCombo = c.Id; service.Settings.TimelinePickedUtc = StatsStore.Unix(DateTime.UtcNow); changed(); }, 260);
        pick.MaxWidth = 460;
        pick.VerticalAlignment = VerticalAlignment.Center;
        pick.ToolTip = "The car and track the charts below show";
        return pick;
    }

    private static FrameworkElement PersonalBests(StatsCharts.Data charts, StatQuery q, (StatDef Def, StatResult Result)? top)
    {
        string? caption = top is { Result: { NoData: false } t } ? $"Most driven pair in this range: {t.Value} ({t.Detail})" : null;
        if (charts.Bests.Count == 0) return Chart("Personal bests", caption, Empty("No clean laps in this range."));
        bool everything = q.From <= 0;
        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 90 });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star), MinWidth = 90 });
        for (int c = 0; c < 3; c++) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int row = 0;
        void Cell(FrameworkElement element, int col, bool right = false)
        {
            element.Margin = new Thickness(col == 0 ? 0 : 18, 8, 0, 8);
            element.VerticalAlignment = VerticalAlignment.Center;
            if (right) element.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(element, row);
            Grid.SetColumn(element, col);
            table.Children.Add(element);
        }
        TextBlock Text(string text, string style) => new() { Text = text, Style = Ui.Res<Style>(style), TextWrapping = TextWrapping.Wrap };
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Cell(Text("Car", "Eyebrow"), 0); Cell(Text("Track", "Eyebrow"), 1); Cell(Text("Clean laps", "Eyebrow"), 2, true); Cell(Text("Best lap", "Eyebrow"), 3, true); Cell(Text("Set", "Eyebrow"), 4, true);
        for (int c = 1; c < 5; c++) StatsSessions.ColumnBar(table, 0, c, 18);
        var (goldSoft, goldDeep) = StatsUi.Hue("Gold");
        foreach (var pb in charts.Bests.Take(12))
        {
            row++;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
            rule.SetResourceReference(Border.BackgroundProperty, "Line");
            Grid.SetRow(rule, row);
            Grid.SetColumnSpan(rule, 5);
            table.Children.Add(rule);
            Cell(Text(pb.Car, "Body"), 0);
            var track = Text(pb.Config.Length > 0 ? $"{pb.Track} · {pb.Config}" : pb.Track, "Body");
            track.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            Cell(track, 1);
            Cell(Text(pb.Laps.ToString("N0", CultureInfo.CurrentCulture), "Caption"), 2, true);
            var lap = new TextBlock { Text = StatFormat.LapTime(pb.Best), FontWeight = FontWeights.SemiBold, FontSize = 14 };
            lap.SetResourceReference(TextBlock.FontFamilyProperty, "MetricFont");
            lap.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            System.Windows.Documents.Typography.SetNumeralAlignment(lap, FontNumeralAlignment.Tabular);
            Cell(lap, 3, true);
            var when = new StackPanel { Orientation = Orientation.Horizontal };
            if (pb.InRange && !everything)
            {
                var chip = Ui.Chip("New", goldSoft, goldDeep);
                chip.Margin = new Thickness(0, 0, 6, 0);
                chip.ToolTip = "Set in this range";
                when.Children.Add(chip);
            }
            when.Children.Add(new TextBlock { Text = Dates.Date(pb.When.ToLocalTime()), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
            Cell(when, 4, true);
        }
        var content = new StackPanel { Children = { table } };
        if (charts.Bests.Count > 12)
            content.Children.Add(new TextBlock { Text = $"12 of {charts.Bests.Count} shown: the latest driven", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 8, 0, 0) });
        string what = everything ? "Best clean lap per car and track, the latest driven first" : "All-time best clean lap of each car and track driven in this range, the latest driven first";
        return Chart("Personal bests", caption is null ? what : what + ". " + caption, content);
    }
}

internal static class StatsLayout
{
    public static FrameworkElement? Pair(FrameworkElement? first, FrameworkElement? second, double sideBySide, double a = 1, double b = 1, double gap = 24, bool center = false)
    {
        if (first is null) return second;
        if (second is null) return first;
        return new PairPanel { Threshold = sideBySide, A = a, B = b, Gap = gap, Center = center, Children = { first, second } };
    }

    private sealed class PairPanel : Panel
    {
        public double Threshold, A = 1, B = 1, Gap = 24;
        public bool Center;

        private bool Side(double width) => double.IsInfinity(width) || width >= Threshold;

        private (double First, double Second) Widths(double width) => ((width - Gap) * A / (A + B), (width - Gap) * B / (A + B));

        protected override Size MeasureOverride(Size available)
        {
            var (first, second) = (InternalChildren[0], InternalChildren[1]);
            if (Side(available.Width))
            {
                var (w1, w2) = double.IsInfinity(available.Width) ? (double.PositiveInfinity, double.PositiveInfinity) : Widths(available.Width);
                first.Measure(new Size(w1, double.PositiveInfinity));
                second.Measure(new Size(w2, double.PositiveInfinity));
                return new Size(double.IsInfinity(available.Width) ? first.DesiredSize.Width + Gap + second.DesiredSize.Width : available.Width,
                    Math.Max(first.DesiredSize.Height, second.DesiredSize.Height));
            }
            first.Measure(new Size(available.Width, double.PositiveInfinity));
            second.Measure(new Size(available.Width, double.PositiveInfinity));
            return new Size(available.Width, first.DesiredSize.Height + Gap + second.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            var (first, second) = (InternalChildren[0], InternalChildren[1]);
            if (Side(final.Width))
            {
                var (w1, w2) = Widths(final.Width);
                double Top(UIElement e) => Center ? (final.Height - e.DesiredSize.Height) / 2 : 0;
                first.Arrange(new Rect(0, Top(first), w1, first.DesiredSize.Height));
                second.Arrange(new Rect(w1 + Gap, Top(second), w2, second.DesiredSize.Height));
            }
            else
            {
                first.Arrange(new Rect(0, 0, final.Width, first.DesiredSize.Height));
                second.Arrange(new Rect(0, first.DesiredSize.Height + Gap, final.Width, second.DesiredSize.Height));
            }
            return final;
        }
    }
}

internal sealed class StatGrid : Panel
{
    public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached("Span", typeof(int), typeof(StatGrid),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static void SetSpan(UIElement element, int span) => element.SetValue(SpanProperty, span);
    public static int GetSpan(UIElement element) => (int)element.GetValue(SpanProperty);

    public double MinTileWidth { get; init; } = 158;
    public double Gap { get; init; } = 10;
    public double? RowGap { get; init; }
    public int Columns { get; init; } = int.MaxValue;

    private readonly List<(UIElement Child, int Row, int Col, int Span)> _places = new();
    private readonly List<double> _rows = new();
    private double _measuredWidth = double.NaN;

    private double Between => RowGap ?? Gap;

    private int ColumnsFor(double width)
    {
        int most = Math.Clamp((int)((width + Gap) / (MinTileWidth + Gap)), 1, Columns);
        var spans = InternalChildren.OfType<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).Select(c => Math.Clamp(GetSpan(c), 1, most)).ToList();
        int units = spans.Sum();
        if (units == 0 || spans.Max() > 1 && spans.Min() == 1) return most;
        int rows = (int)Math.Ceiling(units / (double)most);
        int even = (int)Math.Ceiling(units / (double)rows);
        return even % spans[0] == 0 ? even : most;
    }

    private double CellWidth(double width, int columns) => Math.Max(0, (width - Gap * (columns - 1)) / columns);

    private void Place(double width, bool measure)
    {
        int columns = ColumnsFor(width);
        double cell = CellWidth(width, columns);
        _places.Clear();
        _rows.Clear();
        int row = 0, col = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            int span = Math.Clamp(GetSpan(child), 1, columns);
            if (col + span > columns) { row++; col = 0; }
            if (measure) child.Measure(new Size(cell * span + Gap * (span - 1), double.PositiveInfinity));
            if (_rows.Count <= row) _rows.Add(0);
            _rows[row] = Math.Max(_rows[row], child.DesiredSize.Height);
            _places.Add((child, row, col, span));
            col += span;
        }
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? Math.Max(1, InternalChildren.Count) * (MinTileWidth + Gap) - Gap : available.Width;
        _measuredWidth = width;
        Place(width, measure: true);
        return new Size(width, _rows.Sum() + Between * Math.Max(0, _rows.Count - 1));
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Math.Abs(final.Width - _measuredWidth) > 0.5) Place(final.Width, measure: false);
        double cell = CellWidth(final.Width, ColumnsFor(final.Width));
        var tops = new double[_rows.Count];
        for (int r = 1; r < tops.Length; r++) tops[r] = tops[r - 1] + _rows[r - 1] + Between;
        foreach (var (child, row, col, span) in _places)
            child.Arrange(new Rect(col * (cell + Gap), tops[row], cell * span + Gap * (span - 1), _rows[row]));
        return final;
    }
}

internal static class StatsCharts
{
    internal sealed record PersonalBest(string Key, string Car, string Track, string Config, double Best, DateTime When, long Laps, bool InRange)
    {
        public long Latest { get; init; }
    }

    internal sealed record Ranking(string Label, double Seconds, long Laps);

    internal sealed record SessionSpread(DateTime When, int Laps, double Best, double Q1, double Median, double Q3, double P90, double StdDev);

    internal sealed record TrendBin(string Label, string Tip, double Seconds, int Sessions);

    internal sealed record TrendData(string Title, List<TrendBin> Bins);

    internal sealed record Data(int Sessions, double[,] Heatmap, List<Ranking> Cars, List<Ranking> Tracks, List<Ranking> Classes,
        List<(string Type, double Seconds)> SessionTypes, TrendData Trend,
        List<PersonalBest> Bests, (DateTime When, double Lap)? HeroFirst, string? TimelineKey, List<(DateTime When, double Lap)> Timeline, List<SessionSpread> Consistency);

    public static Data Read(StatsStore store, StatQuery q, StatRange range, string? timelineKey, long? pickedAt)
    {
        var db = store.Db;
        var heat = new double[7, 24];
        var starts = new List<(DateTime Local, double Seconds)>();
        foreach (var r in db.Query(StatRegistry.Scope + "SELECT start_utc, drive_s FROM s", q.From, q.To))
        {
            var local = StatsStore.FromUnix(Convert.ToInt64(r[0])).ToLocalTime();
            double seconds = Convert.ToDouble(r[1] ?? 0d);
            heat[(int)local.DayOfWeek, local.Hour] += seconds;
            starts.Add((local, seconds));
        }
        List<Ranking> Top(string column) => db.Query(StatRegistry.Scope + $"SELECT {column}, SUM(drive_s) t, SUM(laps) FROM s GROUP BY {column} ORDER BY t DESC LIMIT 10", q.From, q.To)
            .Select(r => new Ranking((string)(r[0] ?? ""), Convert.ToDouble(r[1] ?? 0d), Convert.ToInt64(r[2] ?? 0L))).ToList();
        var types = db.Query(StatRegistry.Scope + "SELECT session_type, SUM(drive_s) t FROM s GROUP BY session_type ORDER BY t DESC", q.From, q.To)
            .Select(r => ((string)(r[0] ?? ""), Convert.ToDouble(r[1] ?? 0d))).ToList();
        var bests = db.Query(StatRegistry.Scope + """
            , pairs AS (SELECT s.car, s.track, s.track_config, COUNT(*) n, MAX(s.start_utc) latest FROM l JOIN s ON s.id = l.session_id WHERE l.clean = 1 GROUP BY s.car, s.track, s.track_config)
            SELECT p.car, p.track, p.track_config, MIN(laps.time), sessions.start_utc, p.n, p.latest FROM pairs p
            JOIN sessions ON sessions.car = p.car AND sessions.track = p.track AND sessions.track_config = p.track_config
            JOIN laps ON laps.session_id = sessions.id
            WHERE laps.clean = 1 GROUP BY p.car, p.track, p.track_config ORDER BY p.latest DESC, p.car
            """, q.From, q.To)
            .Select(r =>
            {
                long at = Convert.ToInt64(r[4]);
                return new PersonalBest($"{r[0]}|{r[1]}|{r[2]}", (string)r[0]!, (string)r[1]!, (string)r[2]!, Convert.ToDouble(r[3]), StatsStore.FromUnix(at), Convert.ToInt64(r[5]), at >= q.From && at < q.To)
                    { Latest = Convert.ToInt64(r[6]) };
            }).ToList();

        List<(DateTime When, double Lap)> SessionBests(PersonalBest pb) => db.Query("""
            SELECT s.start_utc, MIN(l.time) FROM laps l JOIN sessions s ON s.id = l.session_id
            WHERE l.clean = 1 AND s.car = ?1 AND s.track = ?2 AND s.track_config = ?3 GROUP BY s.id ORDER BY s.start_utc, s.id
            """, pb.Car, pb.Track, pb.Config).Select(r => (StatsStore.FromUnix((long)r[0]!), Convert.ToDouble(r[1]))).ToList();

        (DateTime, double)? heroFirst = bests.Count > 0 && SessionBests(bests[0]) is [var first, ..] ? first : null;
        var picked = (timelineKey is { } key && bests.FirstOrDefault(b => b.Key == key) is { } chosen && pickedAt is { } at && bests[0].Latest <= at ? chosen : null)
            ?? bests.FirstOrDefault();
        var timeline = new List<(DateTime, double)>();
        var spreads = new List<SessionSpread>();
        if (picked is not null)
        {
            double best = double.MaxValue;
            foreach (var (when, lap) in SessionBests(picked))
            {
                if (lap >= best) continue;
                best = lap;
                timeline.Add((when, lap));
            }
            var laps = db.Query("""
                SELECT s.id, s.start_utc, l.time FROM laps l JOIN sessions s ON s.id = l.session_id
                WHERE l.clean = 1 AND s.car = ?1 AND s.track = ?2 AND s.track_config = ?3 ORDER BY s.start_utc, s.id
                """, picked.Car, picked.Track, picked.Config);
            foreach (var session in laps.GroupBy(r => (long)r[0]!))
            {
                var times = session.Select(r => Convert.ToDouble(r[2])).OrderBy(t => t).ToList();
                if (times.Count < 3) continue;
                spreads.Add(new SessionSpread(StatsStore.FromUnix((long)session.First()[1]!), times.Count, times[0], Quantile(times, 0.25), Quantile(times, 0.5),
                    Quantile(times, 0.75), Quantile(times, 0.9), StatRegistry.StdDev(times)));
            }
            if (spreads.Count > 24) spreads = spreads.Skip(spreads.Count - 24).ToList();
        }
        return new Data(starts.Count, heat, Top("car"), Top("track"), Top("car_class"), types, TrendOf(starts, q, range), bests, heroFirst, picked?.Key, timeline, spreads);
    }

    internal static double Quantile(IReadOnlyList<double> sorted, double p)
    {
        double at = (sorted.Count - 1) * p;
        int lo = (int)Math.Floor(at), hi = (int)Math.Ceiling(at);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (at - lo);
    }

    internal static TrendData TrendOf(IReadOnlyList<(DateTime Local, double Seconds)> sessions, StatQuery q, StatRange range)
    {
        var now = q.NowUtc.ToLocalTime();
        var from = range == StatRange.All ? (sessions.Count > 0 ? sessions.Min(s => s.Local) : now) : StatsStore.FromUnix(q.From).ToLocalTime();
        var to = range == StatRange.All ? now : StatsStore.FromUnix(q.To).ToLocalTime().AddTicks(-1);
        if (to > now && range != StatRange.Week && range != StatRange.Month) to = now;
        string unit = range is StatRange.Week or StatRange.Month ? "day" : range == StatRange.Season || (to - from).TotalDays <= 26 * 7 ? "week" : "month";
        DateTime Start(DateTime t) => unit switch
        {
            "day" => t.Date,
            "week" => t.Date.AddDays(-(((int)t.DayOfWeek + 6) % 7)),
            _ => new DateTime(t.Year, t.Month, 1),
        };
        DateTime Next(DateTime t) => unit switch { "day" => t.AddDays(1), "week" => t.AddDays(7), _ => t.AddMonths(1) };
        var starts = new List<DateTime>();
        for (var t = Start(from); t <= to && starts.Count < 400; t = Next(t)) starts.Add(t);
        var seconds = new double[starts.Count];
        var counts = new int[starts.Count];
        foreach (var (local, s) in sessions)
        {
            int i = starts.FindLastIndex(t => t <= local);
            if (i < 0 || local >= Next(starts[i])) continue;
            seconds[i] += s;
            counts[i]++;
        }
        var c = CultureInfo.CurrentCulture;
        var bins = starts.Select((t, i) => new TrendBin(
            unit switch { "day" => range == StatRange.Week ? t.ToString("ddd", c) : t.Day.ToString(c), "week" => Dates.Short(t), _ => Dates.ShortMonthYear(t) },
            unit switch { "day" => t.ToString("ddd ", c) + Dates.Short(t), "week" => "Week of " + Dates.Date(t), _ => Dates.MonthYear(t) },
            seconds[i], counts[i])).ToList();
        return new TrendData(unit switch { "day" => "Driving per day", "week" => "Driving per week", _ => "Driving per month" }, bins);
    }

    private static readonly string[] Weekdays = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    internal static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static FrameworkElement Heatmap(double[,] seconds)
    {
        double max = 0;
        foreach (double v in seconds) max = Math.Max(max, v);
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        for (int h = 0; h < 24; h++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 11 });
        var accent = ((SolidColorBrush)Ui.Res<Brush>("Accent")).Color;
        Brush Shade(double share) => Frozen(Color.FromArgb((byte)(50 + 205 * Math.Sqrt(share)), accent.R, accent.G, accent.B));
        for (int row = 0; row < 7; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            int day = (row + 1) % 7;
            var label = new TextBlock { Text = Weekdays[day], Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            for (int h = 0; h < 24; h++)
            {
                double v = seconds[day, h];
                var cell = new Border { Margin = new Thickness(1.5), ToolTip = $"{Weekdays[day]} {h:00}:00–{(h + 1) % 24:00}:00 · {(v > 0 ? StatFormat.Duration(v) : "no driving")}" };
                if (v > 0) cell.Background = Shade(v / max);
                else cell.SetResourceReference(Border.BackgroundProperty, "MeterTrack");
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, h + 1);
                grid.Children.Add(cell);
            }
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int h = 0; h < 24; h += 3)
        {
            var hour = new TextBlock { Text = $"{h:00}", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(1.5, 4, 0, 0) };
            Grid.SetRow(hour, 7);
            Grid.SetColumn(hour, h + 1);
            Grid.SetColumnSpan(hour, 3);
            grid.Children.Add(hour);
        }
        var legend = new DockPanel { Margin = new Thickness(38, 12, 0, 0) };
        var less = new TextBlock { Text = "Less", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(less, Dock.Left);
        legend.Children.Add(less);
        var more = new TextBlock { Text = max > 0 ? $"More (up to {StatFormat.Duration(max)})" : "More", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(more, Dock.Right);
        legend.Children.Add(more);
        var steps = new UniformGrid { Rows = 1 };
        var none = new Border { Height = 8, Margin = new Thickness(1.5, 0, 1.5, 0) };
        none.SetResourceReference(Border.BackgroundProperty, "MeterTrack");
        steps.Children.Add(none);
        foreach (double share in new[] { 0.1, 0.35, 0.65, 1.0 })
            steps.Children.Add(new Border { Height = 8, Margin = new Thickness(1.5, 0, 1.5, 0), Background = Shade(share) });
        legend.Children.Add(steps);
        Grid.SetRow(legend, 8);
        Grid.SetColumnSpan(legend, 25);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(legend);
        return grid;
    }

    public static FrameworkElement Ranked(List<Ranking> items, string fill, string empty)
    {
        if (items.Count == 0) return Ui.Text(empty, "Caption");
        var stack = new StackPanel();
        double max = Math.Max(1, items.Max(i => i.Seconds));
        int place = 0;
        foreach (var item in items.Take(8))
        {
            place++;
            string name = item.Label.Length > 0 ? item.Label : "Unknown";
            string figures = $"{StatFormat.Duration(item.Seconds)} · {item.Laps:N0} lap{(item.Laps == 1 ? "" : "s")}";
            var line = new DockPanel { Margin = new Thickness(0, place == 1 ? 0 : 12, 0, 5), ToolTip = $"{name}\n{figures}" };
            var rank = new TextBlock { Text = place.ToString(CultureInfo.CurrentCulture), Style = Ui.Res<Style>("Caption"), Width = 22, FontWeight = FontWeights.SemiBold };
            if (place == 1) rank.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            DockPanel.SetDock(rank, Dock.Left);
            line.Children.Add(rank);
            var value = new TextBlock { Text = figures, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            System.Windows.Documents.Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(value, Dock.Right);
            line.Children.Add(value);
            line.Children.Add(new TextBlock { Text = name, Style = Ui.Res<Style>("Body"), FontSize = 13, FontWeight = place == 1 ? FontWeights.SemiBold : FontWeights.Normal });
            stack.Children.Add(line);
            var track = new Grid { Height = 6, Margin = new Thickness(22, 0, 0, 0), ToolTip = line.ToolTip };
            track.SetResourceReference(Panel.BackgroundProperty, "MeterTrack");
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(item.Seconds, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, max - item.Seconds), GridUnitType.Star) });
            var bar = new Border { MinWidth = 3 };
            bar.SetResourceReference(Border.BackgroundProperty, fill);
            track.Children.Add(bar);
            stack.Children.Add(track);
        }
        if (items.Count > 8) stack.Children.Add(new TextBlock { Text = $"Top 8 of {items.Count}{(items.Count == 10 ? "+" : "")}", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(22, 10, 0, 0) });
        return stack;
    }

    public static FrameworkElement SessionMix(List<(string Type, double Seconds)> types)
    {
        double total = types.Sum(t => t.Seconds);
        if (total <= 0) return Ui.Text("No driving time in this range.", "Caption");
        static (string Name, string Brush, int Order) Look(string type) => type switch
        {
            "Practice" => ("Practice", "Slate", 0), "Qualify" => ("Qualifying", "Gold", 1), "Race" => ("Race", "Accent", 2),
            _ => (type.Length > 0 ? type : "Other", "LineStrong", 3),
        };
        var parts = types.Where(t => t.Seconds > 0).Select(t => (Look: Look(t.Type), t.Seconds)).OrderBy(p => p.Look.Order).ToList();
        var bar = new Grid { Height = 16 };
        foreach (var (look, seconds) in parts)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(seconds, GridUnitType.Star) });
            var piece = new Border { Margin = new Thickness(0, 0, bar.ColumnDefinitions.Count < parts.Count ? 2 : 0, 0), ToolTip = $"{look.Name} · {StatFormat.Duration(seconds)} · {seconds / total:P0}" };
            piece.SetResourceReference(Border.BackgroundProperty, look.Brush);
            Grid.SetColumn(piece, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(piece);
        }
        var legend = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (look, seconds) in parts)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 28, 4) };
            var swatch = new Border { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, look.Brush);
            item.Children.Add(swatch);
            item.Children.Add(new TextBlock { Text = look.Name, Style = Ui.Res<Style>("Body"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            var figure = new TextBlock { Text = $"{StatFormat.Duration(seconds)} · {seconds / total:P0}", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(figure);
            legend.Children.Add(item);
        }
        return new StackPanel { Children = { bar, legend } };
    }

    public static FrameworkElement Trend(TrendData trend) => new TrendChart(trend.Bins) { Height = 200 };

    public static FrameworkElement Progression(List<(DateTime When, double Lap)> points) => new ProgressionChart(points) { Height = 230 };

    public static FrameworkElement Consistency(List<SessionSpread> sessions) => new ConsistencyChart(sessions) { Height = 230 };

    internal static (double Lo, double Hi, double Step) LapScale(double lo, double hi)
    {
        double span = Math.Max(0.2, hi - lo);
        double step = new[] { 0.05, 0.1, 0.2, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60 }.FirstOrDefault(s => span / s <= 4, 120);
        return (Math.Floor((lo - step * 0.25) / step) * step, Math.Ceiling((hi + step * 0.25) / step) * step, step);
    }

    internal abstract class HoverChart : FrameworkElement
    {
        protected const double Left = 70, Bottom = 24, Top = 10, Right = 14;
        private readonly ToolTip _tip;
        protected int Hover = -1;

        protected HoverChart() => _tip = new ToolTip { PlacementTarget = this, Placement = PlacementMode.Mouse };

        protected abstract int HitAt(Point p);
        protected abstract string TipOf(int index);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int near = HitAt(e.GetPosition(this));
            if (near == Hover) return;
            Hover = near;
            _tip.IsOpen = false;
            if (near >= 0)
            {
                _tip.Content = TipOf(near);
                _tip.IsOpen = true;
            }
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            Hover = -1;
            _tip.IsOpen = false;
            InvalidateVisual();
        }

        protected double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        protected FormattedText Text(string s, Brush? brush = null, bool bold = false, double size = 11.5) =>
            new(ThemeManager.Case(s), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeManager.UiFace(bold: bold), size, brush ?? Ui.Res<Brush>("TextMuted"), Dpi);

        protected Func<double, double> LapAxis(DrawingContext dc, double lo, double hi, double step)
        {
            double w = ActualWidth, plotH = ActualHeight - Top - Bottom;
            double Y(double lap) => Top + plotH * (hi - lap) / (hi - lo);
            var grid = new Pen(Ui.Res<Brush>("GridLine"), 1);
            for (double v = lo; v <= hi + step / 2; v += step)
            {
                double y = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(grid, new Point(Left, y), new Point(w - Right, y));
                var label = Text(StatFormat.LapTime(v));
                dc.DrawText(label, new Point(Left - 8 - label.Width, y - label.Height / 2));
            }
            dc.DrawLine(new Pen(Ui.Res<Brush>("LineStrong"), 1), new Point(Left, ActualHeight - Bottom + 0.5), new Point(w - Right, ActualHeight - Bottom + 0.5));
            return Y;
        }
    }

    private sealed class ProgressionChart : HoverChart
    {
        private readonly List<(DateTime When, double Lap)> _points;

        public ProgressionChart(List<(DateTime When, double Lap)> points) => _points = points;

        private (double Min, double Max) TimeScale()
        {
            double min = _points[0].When.Ticks, max = _points[^1].When.Ticks;
            if (max - min < TimeSpan.TicksPerDay) { min -= TimeSpan.TicksPerDay; max += TimeSpan.TicksPerDay; }
            return (min, max);
        }

        private Point At(int i)
        {
            var (lo, hi, _) = LapScale(_points.Min(p => p.Lap), _points.Max(p => p.Lap));
            var (min, max) = TimeScale();
            double w = Math.Max(1, ActualWidth - Left - Right), h = Math.Max(1, ActualHeight - Top - Bottom);
            return new Point(Left + w * (_points[i].When.Ticks - min) / (max - min), Top + h * (hi - _points[i].Lap) / (hi - lo));
        }

        protected override int HitAt(Point p)
        {
            int near = -1;
            double best = 14;
            for (int i = 0; i < _points.Count; i++)
                if ((At(i) - p).Length is var d && d < best) { best = d; near = i; }
            return near;
        }

        protected override string TipOf(int i)
        {
            var p = _points[i];
            string gain = i > 0 ? $" ({StatFormat.Value(StatKind.LapDelta, p.Lap - _points[i - 1].Lap, false)})" : " (first clean lap)";
            return $"{StatFormat.LapTime(p.Lap)}{gain}\n{Dates.DateTime(p.When.ToLocalTime())}";
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (_points.Count == 0 || w < Left + Right + 20) return;
            var (lo, hi, step) = LapScale(_points.Min(p => p.Lap), _points.Max(p => p.Lap));
            LapAxis(dc, lo, hi, step);

            var axis = new Pen(Ui.Res<Brush>("LineStrong"), 1);
            var (min, max) = TimeScale();
            string format = (max - min) / TimeSpan.TicksPerDay > 300 ? CultureInfo.CurrentCulture.DateTimeFormat.YearMonthPattern.Replace("MMMM", "MMM") : Dates.ShortPattern;
            double plotW = w - Left - Right;
            var sample = Text(new DateTime(2026, 12, 28).ToString(format, CultureInfo.CurrentCulture));
            int ticks = Math.Clamp((int)(plotW / (sample.Width + 28)), 1, 6);
            for (int i = 0; i <= ticks; i++)
            {
                double x = Left + plotW * i / ticks;
                var label = Text(new DateTime((long)(min + (max - min) * i / ticks), DateTimeKind.Utc).ToLocalTime().ToString(format, CultureInfo.CurrentCulture));
                dc.DrawLine(axis, new Point(Math.Round(x) + 0.5, h - Bottom), new Point(Math.Round(x) + 0.5, h - Bottom + 4));
                dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, Left - 20, w - label.Width), h - Bottom + 6));
            }

            var accent = Ui.Res<Brush>("Accent");
            var steps = new StreamGeometry();
            using (var g = steps.Open())
            {
                g.BeginFigure(At(0), false, false);
                for (int i = 1; i < _points.Count; i++)
                {
                    var p = At(i);
                    g.LineTo(new Point(p.X, At(i - 1).Y), true, true);
                    g.LineTo(p, true, true);
                }
            }
            steps.Freeze();
            dc.DrawGeometry(null, new Pen(accent, 2) { LineJoin = PenLineJoin.Round }, steps);
            var ring = new Pen(Ui.Res<Brush>("Surface"), 2);
            for (int i = 0; i < _points.Count; i++)
            {
                bool current = i == _points.Count - 1;
                double r = i == Hover ? 6 : current ? 5 : 3.5;
                dc.DrawEllipse(accent, current || i == Hover ? ring : null, At(i), r, r);
            }
            var last = At(_points.Count - 1);
            var best = Text(StatFormat.LapTime(_points[^1].Lap), Ui.Res<Brush>("Heading"), bold: true, size: 12.5);
            double bx = last.X + 10 + best.Width <= w ? last.X + 10 : last.X - 10 - best.Width;
            dc.DrawText(best, new Point(bx, Math.Max(0, last.Y - best.Height - 4)));
        }
    }

    private sealed class ConsistencyChart : HoverChart
    {
        private readonly List<SessionSpread> _sessions;

        public ConsistencyChart(List<SessionSpread> sessions) => _sessions = sessions;

        private double Slot => (ActualWidth - Left - Right) / Math.Max(1, _sessions.Count);

        protected override int HitAt(Point p)
        {
            if (p.X < Left || p.X > ActualWidth - Right || p.Y > ActualHeight - Bottom) return -1;
            return Math.Clamp((int)((p.X - Left) / Slot), 0, _sessions.Count - 1);
        }

        protected override string TipOf(int i)
        {
            var s = _sessions[i];
            return $"{Dates.DateTime(s.When.ToLocalTime())} · {s.Laps} clean laps\n"
                + $"Fastest {StatFormat.LapTime(s.Best)} · median {StatFormat.LapTime(s.Median)}\n"
                + $"Middle half {StatFormat.LapTime(s.Q1)}–{StatFormat.LapTime(s.Q3)} · spread {StatFormat.Value(StatKind.Seconds, s.StdDev, false)}";
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (_sessions.Count == 0 || w < Left + Right + 20) return;
            var (lo, hi, step) = LapScale(_sessions.Min(s => s.Best), _sessions.Max(s => s.P90));
            var y = LapAxis(dc, lo, hi, step);
            double slot = Slot, box = Math.Clamp(slot * 0.5, 4, 22);
            var accent = Ui.Res<Brush>("Accent");
            var soft = Ui.Res<Brush>("AccentSoft");
            var whisker = new Pen(Ui.Res<Brush>("TextMuted"), 1.5);
            var edge = new Pen(accent, 1.5);
            var median = new Pen(Ui.Res<Brush>("Heading"), 2);
            var hover = Ui.Res<Brush>("Hover");
            for (int i = 0; i < _sessions.Count; i++)
            {
                var s = _sessions[i];
                double x = Left + slot * (i + 0.5);
                if (i == Hover) dc.DrawRectangle(hover, null, new Rect(Left + slot * i, Top, slot, h - Top - Bottom));
                dc.DrawLine(whisker, new Point(x, y(s.Best)), new Point(x, y(s.P90)));
                dc.DrawLine(whisker, new Point(x - box / 4, y(s.Best)), new Point(x + box / 4, y(s.Best)));
                double top = y(s.Q3), bottom = y(s.Q1);
                dc.DrawRectangle(soft, edge, new Rect(x - box / 2, top, box, Math.Max(2, bottom - top)));
                dc.DrawLine(median, new Point(x - box / 2, y(s.Median)), new Point(x + box / 2, y(s.Median)));
            }
            var sample = Text("28 Dec");
            int every = Math.Max(1, (int)Math.Ceiling((sample.Width + 16) / slot));
            for (int i = 0; i < _sessions.Count; i += every)
            {
                var label = Text(Dates.Short(_sessions[i].When.ToLocalTime()));
                double x = Left + slot * (i + 0.5);
                dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, Left - 20, w - label.Width), h - Bottom + 6));
            }
        }
    }

    private sealed class TrendChart : HoverChart
    {
        private new const double Left = 46;
        private readonly List<TrendBin> _bins;

        public TrendChart(List<TrendBin> bins) => _bins = bins;

        private double Slot => (ActualWidth - Left - Right) / Math.Max(1, _bins.Count);

        protected override int HitAt(Point p)
        {
            if (_bins.Count == 0 || p.X < Left || p.X > ActualWidth - Right || p.Y > ActualHeight - Bottom) return -1;
            return Math.Clamp((int)((p.X - Left) / Slot), 0, _bins.Count - 1);
        }

        protected override string TipOf(int i)
        {
            var b = _bins[i];
            return $"{b.Tip}\n{(b.Seconds > 0 ? StatFormat.Duration(b.Seconds) : "No driving")}{(b.Sessions > 0 ? $" · {b.Sessions} session{(b.Sessions == 1 ? "" : "s")}" : "")}";
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (_bins.Count == 0 || w < Left + Right + 20) return;
            double maxHours = Math.Max(_bins.Max(b => b.Seconds) / 3600, 0.01);
            double step = new[] { 0.25, 0.5, 1, 2, 3, 5, 10, 20, 50 }.FirstOrDefault(s => maxHours / s <= 4, 100);
            double top = Math.Ceiling(maxHours / step) * step, plotH = h - Top - Bottom;
            double Y(double hours) => Top + plotH * (1 - hours / top);
            var grid = new Pen(Ui.Res<Brush>("GridLine"), 1);
            for (double v = 0; v <= top + step / 2; v += step)
            {
                double yy = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(grid, new Point(Left, yy), new Point(w - Right, yy));
                var label = Text(v == 0 ? "0" : v < 1 ? $"{v * 60:0}m" : $"{v:0.#}h");
                dc.DrawText(label, new Point(Left - 8 - label.Width, yy - label.Height / 2));
            }
            double slot = Slot, bar = Math.Max(2, slot * 0.66);
            var accent = Ui.Res<Brush>("Accent");
            var hover = Ui.Res<Brush>("AccentText");
            for (int i = 0; i < _bins.Count; i++)
            {
                if (_bins[i].Seconds <= 0) continue;
                double x = Left + slot * i + (slot - bar) / 2, yy = Y(_bins[i].Seconds / 3600);
                dc.DrawRectangle(i == Hover ? hover : accent, null, new Rect(x, yy, bar, Math.Max(1, h - Bottom - yy)));
            }
            dc.DrawLine(new Pen(Ui.Res<Brush>("LineStrong"), 1), new Point(Left, h - Bottom + 0.5), new Point(w - Right, h - Bottom + 0.5));
            var sample = Text(_bins.OrderByDescending(b => b.Label.Length).First().Label);
            int every = Math.Max(1, (int)Math.Ceiling((sample.Width + 12) / slot));
            for (int i = 0; i < _bins.Count; i += every)
            {
                var label = Text(_bins[i].Label);
                double x = Left + slot * (i + 0.5);
                dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, 0, w - label.Width), h - Bottom + 6));
            }
        }
    }
}

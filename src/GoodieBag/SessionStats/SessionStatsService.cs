using System.IO;
using System.Windows.Threading;
using GoodieBag.Services;
using GoodieBag.SimRacing.IracingFiles;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SessionStats;

internal sealed class SessionStatsService : IDisposable
{
    private const string Consumer = "Session stats";
    private readonly AppServices _app;
    private readonly SynchronizationContext _ui;
    private readonly SessionRecorder _live;
    private readonly object _feedGate = new(), _queueGate = new();
    private Task _writes = Task.CompletedTask;
    private CancellationTokenSource? _importCancel;
    private Thread? _import;
    private DispatcherTimer? _scanAfterExit;
    private volatile bool _rescan;
    private bool _changedPosted, _started;
    private readonly Action<TelemetryFrame> _observer;

    public SessionStatsService(AppServices app)
    {
        _app = app;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _observer = OnFrame;
        _live = new SessionRecorder("live", traces: true) { Tracing = () => Settings.TraceRecord };
        _live.LapDone += (row, laps) =>
        {
            var traces = _live.TakeTraces();
            var copy = row.Copy();
            if (Settings.TraceRecord) copy.Traces = traces;
            Queue(store => store.SaveLive(copy, laps));
        };
    }

    public StatsStore? Store { get; private set; }

    public TraceStore? Traces { get; private set; }

    public string? Problem { get; private set; }

    public (int Done, int Total)? Importing { get; private set; }

    public event Action? Changed;

    public Config.SessionStatsSettings Settings => _app.Config.SessionStats;

    public IReadOnlyCollection<int>? Leagues => Plugins.BuiltIn.SessionStatsPlugin.LeagueIds(_app);

    public IEnumerable<StatDef> Available => StatRegistry.All.Where(d => !d.League || Leagues is not null);

    public static StatRange RangeOf(string? name) => name switch { "week" => StatRange.Week, "month" => StatRange.Month, "all" => StatRange.All, _ => StatRange.Season };

    public static string NameOf(StatRange range) => range switch { StatRange.Week => "week", StatRange.Month => "month", StatRange.All => "all", _ => "season" };

    public StatRange Range => RangeOf(Settings.Range);

    public StatQuery Query()
    {
        var now = DateTime.UtcNow;
        var (from, to) = TimeRanges.Of(Range, now, Store?.LatestOfficialWeek());
        return new StatQuery(StatsStore.Unix(from), StatsStore.Unix(to), Leagues ?? Array.Empty<int>(), Settings.Imperial, now);
    }

    public (string? Name, string? Number) Driver()
    {
        if (Store is not { } store) return (null, null);
        return (store.LatestDriver(), Settings.DriverNumber ?? store.MostUsedNumber(Leagues ?? Array.Empty<int>()));
    }

    public Task<List<(StatDef Def, StatResult Result)>> EvaluateAsync(IEnumerable<StatDef> defs, StatQuery q)
    {
        var list = defs.ToList();
        var store = Store;
        return Task.Run(() => store is null ? list.Select(d => (d, StatResult.None)).ToList() : list.Select(d => (d, StatRegistry.Evaluate(store, d, q))).ToList());
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        try
        {
            Store = StatsStore.Open(_app.Demo ? ":memory:" : StatsStore.DefaultPath);
            try { Store.Traces = Traces = TraceStore.Open(_app.Demo ? ":memory:" : TraceStore.DefaultPath); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { App.LogError(ex); }
            if (_app.Demo) StatsDemo.Fill(Store);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Problem = "The stats database couldn't be opened: " + ex.Message;
            App.LogError(ex);
            return;
        }
        if (_app.Demo) return;
        _app.Telemetry.FrameObserver = _observer;
        _app.Telemetry.Acquire(Consumer);
        _app.Watcher.Stopped += OnProcessStopped;
        ImportNewFiles();
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _app.Watcher.Stopped -= OnProcessStopped;
        _scanAfterExit?.Stop();
        if (ReferenceEquals(_app.Telemetry.FrameObserver, _observer)) _app.Telemetry.FrameObserver = null;
        if (!_app.Demo) _app.Telemetry.Release(Consumer);
        lock (_feedGate)
            if (_live.Finish() is { } last) Queue(store => store.SaveLive(last.Row, last.Laps));
        _importCancel?.Cancel();
        bool stillImporting = _import is { } import && !import.Join(4000);
        if (stillImporting) _disposeAfterImport = true;
        else _import = null;
        try { _writes.Wait(5000); } catch (AggregateException) { }
        if (!stillImporting)
        {
            Store?.Dispose();
            Traces?.Dispose();
        }
        Store = null;
        Traces = null;
        Importing = null;
    }

    public void Dispose() => Stop();

    private void OnFrame(TelemetryFrame frame)
    {
        lock (_feedGate)
        {
            if (_live.Feed(frame, DateTime.UtcNow) is { } ended)
                Queue(store => store.SaveLive(ended.Row, ended.Laps));
        }
    }

    private void OnProcessStopped(string name)
    {
        if (!string.Equals(name, TelemetryService.SimProcess, StringComparison.OrdinalIgnoreCase)) return;
        _scanAfterExit ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _scanAfterExit.Tick -= OnScanDue;
        _scanAfterExit.Tick += OnScanDue;
        _scanAfterExit.Stop();
        _scanAfterExit.Start();
    }

    private void OnScanDue(object? sender, EventArgs e)
    {
        _scanAfterExit?.Stop();
        ImportNewFiles();
    }

    private void Queue(Action<StatsStore> work)
    {
        lock (_queueGate)
            _writes = _writes.ContinueWith(_ =>
            {
                try { if (Store is { } store) work(store); }
                catch (Exception ex) { App.LogError(ex); }
                PostChanged();
            }, TaskScheduler.Default);
    }

    private void PostChanged()
    {
        lock (_queueGate)
        {
            if (_changedPosted) return;
            _changedPosted = true;
        }
        _ui.Post(_ =>
        {
            lock (_queueGate) _changedPosted = false;
            Changed?.Invoke();
        }, null);
    }

    public static string? TelemetryFolder() =>
        IracingPaths.FindDocumentsFolder() is { } documents && Path.Combine(documents, "telemetry") is var folder && Directory.Exists(folder) ? folder : null;

    public TraceRetention Retention => new(Settings.TraceKeepDays, Settings.TraceLapsPerSession, Settings.TraceKeepBests);

    public void RetentionChanged()
    {
        var keep = Retention;
        Queue(_ => Traces?.Prune(keep, DateTime.UtcNow, null));
        ImportNewFiles();
    }

    public void DeleteAllTraces() => Queue(_ => Traces?.DeleteAll());

    public void ImportNewFiles()
    {
        if (_app.Demo || Store is null) return;
        if (_import is { IsAlive: true }) { _rescan = true; return; }
        if (TelemetryFolder() is not { } folder)
        {
            var keep = Retention;
            Queue(_ => Traces?.Prune(keep, DateTime.UtcNow, null));
            return;
        }
        var cancel = _importCancel = new CancellationTokenSource();
        bool traces = Settings.TraceRecord && Traces is not null;
        var retention = Retention;
        _import = new Thread(() => Import(folder, traces, retention, cancel.Token)) { IsBackground = true, Name = ImportThread, Priority = ThreadPriority.Lowest };
        _import.Start();
    }

    private void Import(string folder, bool traces, TraceRetention retention, CancellationToken cancel)
    {
        var store = Store;
        var traceStore = Traces;
        try { ImportCore(folder, traces, retention, cancel); }
        finally
        {
            Foreground();
            if (_disposeAfterImport)
            {
                _disposeAfterImport = false;
                store?.Dispose();
                traceStore?.Dispose();
            }
        }
    }

    private volatile bool _disposeAfterImport;

    private const int BackgroundBegin = 0x10000, BackgroundEnd = 0x20000;

    [ThreadStatic] private static bool _background;

    private static void Foreground()
    {
        if (_background) SetThreadPriority(GetCurrentThread(), BackgroundEnd);
        _background = false;
    }

    private static void Background()
    {
        if (!_background && Thread.CurrentThread.Name == ImportThread) _background = SetThreadPriority(GetCurrentThread(), BackgroundBegin);
    }

    private const string ImportThread = "Stats import";

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetThreadPriority(IntPtr thread, int priority);

    private void ImportCore(string folder, bool traces, TraceRetention retention, CancellationToken cancel)
    {
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Store is not { } store) return;
            ImportAll(store, folder, traces, done, cancel);
            if (!cancel.IsCancellationRequested) FillMissing(store, folder, done, cancel);
            if (traces && !cancel.IsCancellationRequested) Backfill(store, folder, retention, done, cancel);
        }
        finally
        {
            SetProgress(null);
            try { if (!cancel.IsCancellationRequested && Traces is { } prune) prune.Prune(retention, DateTime.UtcNow, null); }
            catch (Exception ex) { App.LogError(ex); }
            if (_rescan && _started)
            {
                _rescan = false;
                var self = Thread.CurrentThread;
                _ui.Post(_ => { self.Join(2000); ImportNewFiles(); }, null);
            }
        }
    }

    private static bool BeingWritten(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private void ImportAll(StatsStore store, string folder, bool traces, HashSet<string> done, CancellationToken cancel)
    {
        bool simRunning = _app.Telemetry.Status != SimStatus.NotRunning;
        var todo = new DirectoryInfo(folder).EnumerateFiles("*.ibt")
            .Where(f => !store.Imported(f.Name))
            .Where(f => !simRunning || DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromMinutes(2))
            .Where(f => !simRunning || !BeingWritten(f.FullName))
            .OrderBy(f => f.LastWriteTimeUtc).ToList();
        if (todo.Count == 0) return;
        SetProgress((0, todo.Count));
        for (int i = 0; i < todo.Count && !cancel.IsCancellationRequested; i++)
        {
            var file = todo[i];
            try
            {
                string hash = StatsStore.HashOf(file.FullName);
                if (store.HashImported(hash)) store.MarkImported(file.Name, hash);
                else if (!ImportFile(store, file.FullName, traces, cancel, (file.Name, hash))) return;
                done.Add(file.Name);
                if (!traces) NoteWithoutTraces(file.LastWriteTimeUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                App.LogError(new InvalidOperationException($"Couldn't import {file.Name}.", ex));
                try { store.MarkImported(file.Name, "unreadable"); } catch { }
            }
            SetProgress((i + 1, todo.Count));
        }
    }

    private void FillMissing(StatsStore store, string folder, HashSet<string> done, CancellationToken cancel)
    {
        if (store.Meta("conditions_backfill") is not null && store.Meta("tyres_backfill") is not null) return;
        var todo = new DirectoryInfo(folder).EnumerateFiles("*.ibt")
            .Where(f => !done.Contains(f.Name) && store.Imported(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        if (todo.Count > 0) SetProgress((0, todo.Count));
        for (int i = 0; i < todo.Count; i++)
        {
            try
            {
                if (Read(todo[i].FullName, traces: false, cancel) is not { } found) return;
                foreach (var (row, _) in found) store.FillMissing(row);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
            catch (Exception ex) { App.LogError(new InvalidOperationException($"Couldn't read the conditions of {todo[i].Name}.", ex)); }
            SetProgress((i + 1, todo.Count));
            PostChanged();
        }
        store.SetMeta("conditions_backfill", DateTime.UtcNow.ToString("O"));
        store.SetMeta("tyres_backfill", DateTime.UtcNow.ToString("O"));
    }

    private const string MissingFromKey = "traces_missing_from", ChannelsKey = "traces_channels", ChannelsVersion = "yaw-uneven-halves";

    private void NoteWithoutTraces(DateTime fileUtc)
    {
        if (Traces is not { } traceStore) return;
        if (MissingFrom(traceStore) is { } from && from <= fileUtc) return;
        traceStore.SetMeta(MissingFromKey, fileUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static DateTime? MissingFrom(TraceStore traceStore) =>
        DateTime.TryParse(traceStore.Meta(MissingFromKey), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;

    private void Backfill(StatsStore store, string folder, TraceRetention retention, HashSet<string> done, CancellationToken cancel)
    {
        if (Traces is not { } traceStore) return;
        var since = retention.KeepDays > 0 ? DateTime.UtcNow.AddDays(-retention.KeepDays) : DateTime.MinValue;
        bool channels = traceStore.Meta(ChannelsKey) == ChannelsVersion;
        var covered = channels ? CoveredFrom(traceStore.Meta("traces_backfill"), traceStore.Meta("traces_backfill_from")) : null;
        var missing = MissingFrom(traceStore);
        if (covered is { } c && c <= since && missing is null) return;
        var todo = new DirectoryInfo(folder).EnumerateFiles("*.ibt")
            .Where(f => !done.Contains(f.Name) && f.LastWriteTimeUtc >= since
                        && (covered is null || f.LastWriteTimeUtc < covered || missing is { } m && f.LastWriteTimeUtc >= m) && store.Imported(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        if (todo.Count > 0) SetProgress((0, todo.Count));
        for (int i = 0; i < todo.Count; i++)
        {
            try
            {
                if (Read(todo[i].FullName, traces: true, cancel) is not { } found) return;
                foreach (var (row, _) in found) store.AttachTracesOnly(row);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
            catch (Exception ex) { App.LogError(new InvalidOperationException($"Couldn't read the traces of {todo[i].Name}.", ex)); }
            SetProgress((i + 1, todo.Count));
            PostChanged();
        }
        traceStore.SetMeta("traces_backfill", DateTime.UtcNow.ToString("O"));
        traceStore.SetMeta("traces_backfill_from", since.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        traceStore.SetMeta(ChannelsKey, ChannelsVersion);
        if (missing is not null) traceStore.SetMeta(MissingFromKey, "");
    }

    internal static DateTime? CoveredFrom(string? doneAt, string? from)
    {
        var styles = System.Globalization.DateTimeStyles.RoundtripKind;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (from is not null && DateTime.TryParse(from, culture, styles, out var f)) return f;
        if (doneAt is not null && DateTime.TryParse(doneAt, culture, styles, out var d)) return d.AddDays(-365);
        return null;
    }

    internal static bool ImportFile(StatsStore store, string path, bool traces = false, CancellationToken cancel = default, (string File, string Hash)? imported = null)
    {
        if (Read(path, traces, cancel) is not { } found) return false;
        store.SaveFile(found, imported);
        return true;
    }

    private static List<(SessionRow Row, List<LapRow> Laps)>? Read(string path, bool traces, CancellationToken cancel)
    {
        Background();
        try { return ReadFile(path, traces, cancel); }
        finally { Foreground(); }
    }

    internal static List<(SessionRow Row, List<LapRow> Laps)>? ReadFile(string path, bool traces, CancellationToken cancel)
    {
        using var ibt = IbtFile.Open(path, out var error);
        if (ibt is null) throw new InvalidDataException(error ?? "not a telemetry file");
        var info = SessionInfo.Parse(ibt.SessionInfoYaml);
        var decoder = new FrameDecoder();
        decoder.Bind(ibt.Vars);
        var frame = new TelemetryFrame { Session = info };
        var recorder = new SessionRecorder("ibt", traces);
        var started = ibt.StartedUtc ?? File.GetCreationTimeUtc(path);
        var found = new List<(SessionRow Row, List<LapRow> Laps)>();
        int n = 0;
        foreach (var record in ibt.Records())
        {
            if ((++n & 4095) == 0 && cancel.IsCancellationRequested) return null;
            decoder.Decode(record, frame);
            frame.Status = SimStatus.Connected;
            frame.Session = info;
            var at = started.AddSeconds(Math.Max(0, (frame.SessionTime ?? ibt.StartSessionTime) - ibt.StartSessionTime));
            if (recorder.Feed(frame, at) is { } ended) found.Add(ended);
        }
        if (recorder.Finish() is { } last) found.Add(last);
        return found;
    }

    private void SetProgress((int Done, int Total)? progress)
    {
        _ui.Post(_ =>
        {
            Importing = progress;
            Changed?.Invoke();
        }, null);
    }
}

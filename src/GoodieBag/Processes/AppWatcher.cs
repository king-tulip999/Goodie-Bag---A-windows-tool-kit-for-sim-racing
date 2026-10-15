using System.IO;
using System.Diagnostics;
using System.Management;

namespace GoodieBag.Processes;

internal sealed class AppWatcher : IDisposable
{
    private const int DebounceMs = 400;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly SynchronizationContext _ui;
    private readonly Dictionary<string, HashSet<string>> _consumers = new();
    private readonly object _gate = new();
    private readonly object _scanGate = new();
    private HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _catchUp = new(StringComparer.OrdinalIgnoreCase);
    private ManagementEventWatcher? _startTrace, _stopTrace;
    private readonly object _traceGate = new();
    private int _generation;
    private System.Threading.Timer? _timer;

    public event Action<string>? Started;

    public event Action<string>? Stopped;

    public AppWatcher()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public static string Normalize(string nameOrPath)
    {
        string name = Path.GetFileName(nameOrPath.Trim().Trim('"'));
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name.ToLowerInvariant();
    }

    public void Acquire(string consumer, IEnumerable<string> names)
    {
        var wanted = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
        {
            Release(consumer);
            return;
        }
        bool start;
        HashSet<string> added;
        lock (_gate)
        {
            start = _consumers.Count == 0;
            _consumers[consumer] = wanted;
            var watched = _consumers.Values.SelectMany(n => n).ToHashSet(StringComparer.OrdinalIgnoreCase);
            added = watched.Where(w => !_watched.Contains(w)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _watched = watched;
            if (!start)
            {
                _catchUp.UnionWith(added);
                _running.ExceptWith(added);
            }
        }
        if (start)
            Start();
        else if (added.Count > 0)
            ThreadPool.QueueUserWorkItem(_ => Rescan());
    }

    public void Release(string consumer)
    {
        bool stop;
        lock (_gate)
        {
            if (!_consumers.Remove(consumer))
                return;
            _watched = _consumers.Values.SelectMany(n => n).ToHashSet(StringComparer.OrdinalIgnoreCase);
            stop = _consumers.Count == 0;
        }
        if (stop)
            Stop();
    }

    public bool IsRunning(string nameOrPath)
    {
        lock (_gate)
        {
            if (_consumers.Count > 0)
                return _running.Contains(Normalize(nameOrPath));
        }
        return IsRunningNow(nameOrPath);
    }

    public static bool IsRunningNow(string nameOrPath)
    {
        string name = Normalize(nameOrPath);
        if (name.Length == 0)
            return false;
        var found = Process.GetProcessesByName(name);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    public static HashSet<string> Snapshot()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { names.Add(p.ProcessName.ToLowerInvariant()); }
            catch { }
            finally { p.Dispose(); }
        }
        return names;
    }

    private void Start()
    {
        var running = Snapshot();
        lock (_gate)
            _running = running;
        var timer = _timer = new System.Threading.Timer(_ => Rescan(), null, Timeout.Infinite, Timeout.Infinite);
        int generation;
        lock (_traceGate) generation = ++_generation;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_traceGate)
            {
                if (generation != _generation) return;
                try
                {
                    _startTrace = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessName FROM Win32_ProcessStartTrace"));
                    _stopTrace = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessName FROM Win32_ProcessStopTrace"));
                    _startTrace.EventArrived += OnTrace;
                    _stopTrace.EventArrived += OnTrace;
                    _startTrace.Start();
                    _stopTrace.Start();
                }
                catch
                {
                    DisposeTraces(TakeTraces());
                    try { timer.Change(PollInterval, PollInterval); }
                    catch (ObjectDisposedException) { }
                    return;
                }
            }
            Rescan();
        });
    }

    private void Stop()
    {
        ManagementEventWatcher?[] traces;
        lock (_traceGate)
        {
            _generation++;
            traces = TakeTraces();
        }
        if (traces.Any(t => t is not null))
            ThreadPool.QueueUserWorkItem(_ => DisposeTraces(traces));
        _timer?.Dispose();
        _timer = null;
        lock (_gate)
        {
            _running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _catchUp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private ManagementEventWatcher?[] TakeTraces()
    {
        var traces = new[] { _startTrace, _stopTrace };
        _startTrace = _stopTrace = null;
        return traces;
    }

    private void DisposeTraces(ManagementEventWatcher?[] traces)
    {
        foreach (var w in traces)
        {
            if (w is null) continue;
            w.EventArrived -= OnTrace;
            try { w.Stop(); } catch { }
            w.Dispose();
        }
    }

    private void OnTrace(object sender, EventArrivedEventArgs e)
    {
        var ev = e.NewEvent;
        string raw;
        try { raw = (ev["ProcessName"] as string ?? "").Trim().ToLowerInvariant(); }
        finally { ev.Dispose(); }
        string traced = Normalize(raw);
        HashSet<string> watched;
        lock (_gate) watched = _watched;
        bool relevant = watched.Contains(traced)
                        || (raw.Length >= 15 && watched.Any(w => (w + ".exe").StartsWith(raw, StringComparison.OrdinalIgnoreCase)));
        if (!relevant)
            return;
        try { _timer?.Change(DebounceMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    public bool CatchingUp { get; private set; }

    private void Rescan()
    {
        List<string> started, stopped;
        HashSet<string> added;
        lock (_scanGate)
        {
            HashSet<string> now;
            try { now = Snapshot(); }
            catch { return; }

            lock (_gate)
            {
                if (_consumers.Count == 0)
                    return;
                started = _watched.Where(w => now.Contains(w) && !_running.Contains(w)).ToList();
                stopped = _watched.Where(w => !now.Contains(w) && _running.Contains(w)).ToList();
                _running = now;
                added = _catchUp;
                _catchUp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        if (started.Count == 0 && stopped.Count == 0)
            return;
        _ui.Post(_ =>
        {
            foreach (var name in stopped) Raise(Stopped, name);
            foreach (var name in started) Raise(Started, name);
        }, null);

        void Raise(Action<string>? handler, string name)
        {
            CatchingUp = added.Contains(name);
            try { handler?.Invoke(name); }
            finally { CatchingUp = false; }
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _consumers.Clear();
        Stop();
    }
}

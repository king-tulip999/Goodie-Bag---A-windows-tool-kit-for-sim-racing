using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace GoodieBag.Overlay;

internal sealed record FrameStats(double Fps, double AverageFps, double OnePercentLowFps, double FrameTimeMs);

internal sealed class FrameRateService : IDisposable
{
    private const string SessionName = "GoodieBag-FrameRate";
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int DxgiPresentStart = 42;
    private const int DxgiPresentMultiplaneOverlayStart = 55;
    private const int D3D9PresentStart = 1;

    private const double MaxCountedFrameMs = 1000;

    private const long ProcessIdleLimitMs = 60_000;
    private const long SwapChainIdleLimitMs = 10_000;
    private const long MostActiveCacheMs = 250;

    private readonly object _lock = new();
    private readonly Dictionary<int, ProcessFrames> _processes = new();
    private TraceEventSession? _session;
    private Thread? _thread;
    private long _nextPruneMs;

    private readonly object _mostActiveGate = new();
    private (int ProcessId, string Name, FrameStats Stats)? _mostActive;
    private long _mostActiveAtMs;
    private bool _mostActiveValid;

    private readonly HashSet<string> _consumers = new();

    public string? Error { get; private set; }

    public bool DemoMode { get; init; }

    public bool Running => _session is not null;

    public void Acquire(string consumer)
    {
        _consumers.Add(consumer);
        if (_session is null && !DemoMode)
            Start();
    }

    public void Release(string consumer)
    {
        if (_consumers.Remove(consumer) && _consumers.Count == 0)
            Stop();
    }

    private void Start()
    {
        if (_session is not null)
            return;
        Error = null;

        if (!Environment.IsPrivilegedProcess)
        {
            Error = "FPS counting needs Goodie Bag to run as administrator.";
            return;
        }

        try
        {
            try { TraceEventSession.GetActiveSession(SessionName)?.Stop(noThrow: true); } catch { }

            var session = _session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableProvider(DxgiProvider, TraceEventLevel.Verbose, ulong.MaxValue,
                new TraceEventProviderOptions { EventIDsToEnable = new List<int> { DxgiPresentStart, DxgiPresentMultiplaneOverlayStart } });
            session.EnableProvider(D3D9Provider, TraceEventLevel.Verbose, ulong.MaxValue,
                new TraceEventProviderOptions { EventIDsToEnable = new List<int> { D3D9PresentStart } });
            session.Source.AllEvents += OnEvent;

            var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _thread = new Thread(() =>
            {
                try { session.Source.Process(); }
                catch (Exception) when (_session != session) { }
                catch (Exception ex) { Error = "FPS tracing stopped: " + ex.Message; }
                if (_session == session) ui.BeginInvoke(() => Restart(session));
            })
            { IsBackground = true, Name = "Goodie Bag frame rate" };
            _thread.Start();
        }
        catch (Exception ex)
        {
            Error = "Couldn't start FPS tracing: " + ex.Message;
            _session?.Dispose();
            _session = null;
        }
    }

    public FrameStats? GetStats(int processId)
    {
        lock (_lock)
        {
            if (!_processes.TryGetValue(processId, out var process))
                return null;
            return process.GetStats();
        }
    }

    public (int ProcessId, string Name, FrameStats Stats)? GetMostActive()
    {
        long now = Environment.TickCount64;
        lock (_mostActiveGate)
        {
            if (_mostActiveValid && now - _mostActiveAtMs < MostActiveCacheMs)
                return _mostActive;
        }

        int self = Environment.ProcessId;
        var candidates = new List<(int Pid, FrameStats Stats)>();
        lock (_lock)
        {
            PruneStale(now);
            foreach (var (pid, process) in _processes)
            {
                if (pid == self || pid <= 4)
                    continue;
                if (process.GetStats() is { } stats)
                    candidates.Add((pid, stats));
            }
        }

        int bestPid = 0;
        FrameStats? best = null;
        foreach (var (pid, stats) in candidates)
        {
            if (NameOf(pid).Equals("dwm", StringComparison.OrdinalIgnoreCase))
                continue;
            if (best is null || stats.Fps > best.Fps)
            {
                best = stats;
                bestPid = pid;
            }
        }
        (int ProcessId, string Name, FrameStats Stats)? result = best is null ? null : (bestPid, NameOf(bestPid), best);

        lock (_mostActiveGate)
        {
            _mostActive = result;
            _mostActiveAtMs = now;
            _mostActiveValid = true;
        }
        return result;
    }

    private string NameOf(int pid) => _names.GetOrAdd(pid, id =>
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return process.ProcessName;
        }
        catch { return $"PID {id}"; }
    });

    private void InvalidateMostActive()
    {
        lock (_mostActiveGate)
        {
            _mostActiveValid = false;
            _mostActive = null;
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _names = new();

    public double[] GetFrameTimes(int processId, int count)
    {
        lock (_lock)
            return _processes.TryGetValue(processId, out var process) ? process.FrameTimes(count) : Array.Empty<double>();
    }

    public static double[] SampleFrameTimes(int count)
    {
        var result = new double[count];
        for (int i = 0; i < count; i++)
            result[i] = 6.9 + 0.5 * Math.Sin(i * 0.37) + 0.3 * Math.Sin(i * 1.7) + (i % 47 == 13 ? 6 : 0) + (i % 89 == 40 ? 3.5 : 0);
        return result;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _processes.Clear();
            _names.Clear();
        }
        InvalidateMostActive();
    }

    private void OnEvent(TraceEvent e)
    {
        int id = (int)e.ID;
        bool isPresent = e.ProviderGuid == DxgiProvider ? id is DxgiPresentStart or DxgiPresentMultiplaneOverlayStart
                       : e.ProviderGuid == D3D9Provider && id == D3D9PresentStart;
        if (!isPresent)
            return;

        ulong swapChain = 0;
        if (e.EventDataLength >= e.PointerSize)
            swapChain = e.PointerSize == 8 ? (ulong)Marshal.ReadInt64(e.DataStart) : (uint)Marshal.ReadInt32(e.DataStart);

        lock (_lock)
        {
            PruneStale(Environment.TickCount64);
            if (!_processes.TryGetValue(e.ProcessID, out var process))
                process = _processes[e.ProcessID] = new ProcessFrames();
            process.Add(swapChain, e.TimeStampRelativeMSec);
        }
    }

    private void PruneStale(long nowMs)
    {
        if (nowMs < _nextPruneMs)
            return;
        _nextPruneMs = nowMs + 1000;

        List<int>? gone = null;
        foreach (var (pid, process) in _processes)
        {
            if (process.IdleFor.TotalMilliseconds > ProcessIdleLimitMs)
                (gone ??= new()).Add(pid);
            else
                process.PruneSwapChains(nowMs, SwapChainIdleLimitMs);
        }
        if (gone is null)
            return;
        foreach (var pid in gone)
        {
            _processes.Remove(pid);
            _names.TryRemove(pid, out _);
        }
    }

    private DateTime _lastRestart;

    private async void Restart(TraceEventSession dead)
    {
        if (_session != dead) return;
        Stop();
        var wait = _lastRestart + TimeSpan.FromMinutes(1) - DateTime.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait);
        if (_session is not null || _consumers.Count == 0) return;
        _lastRestart = DateTime.UtcNow;
        Start();
    }

    private void Stop()
    {
        var session = _session;
        _session = null;
        try { session?.Dispose(); } catch { }
        _thread?.Join(1000);
        _thread = null;
        lock (_lock)
        {
            _processes.Clear();
            _names.Clear();
        }
        InvalidateMostActive();
    }

    public void Dispose()
    {
        _consumers.Clear();
        Stop();
    }

    private sealed class ProcessFrames
    {
        private readonly Dictionary<ulong, SwapChainFrames> _swapChains = new();
        private readonly Stopwatch _sinceLastFrame = Stopwatch.StartNew();

        public TimeSpan IdleFor => _sinceLastFrame.Elapsed;

        public void Add(ulong swapChain, double timeMs)
        {
            if (!_swapChains.TryGetValue(swapChain, out var frames))
                frames = _swapChains[swapChain] = new SwapChainFrames();
            frames.Add(timeMs);
            _sinceLastFrame.Restart();
        }

        public void PruneSwapChains(long nowMs, long idleMs)
        {
            if (_swapChains.Count < 2)
                return;
            ulong newest = 0;
            long newestTicks = long.MinValue;
            foreach (var (key, chain) in _swapChains)
            {
                if (chain.LastSeenTicks > newestTicks)
                {
                    newestTicks = chain.LastSeenTicks;
                    newest = key;
                }
            }
            List<ulong>? stale = null;
            foreach (var (key, chain) in _swapChains)
            {
                if (key != newest && nowMs - chain.LastSeenTicks > idleMs)
                    (stale ??= new()).Add(key);
            }
            if (stale is null)
                return;
            foreach (var key in stale)
                _swapChains.Remove(key);
        }

        public FrameStats? GetStats()
        {
            if (IdleFor > TimeSpan.FromSeconds(2.5))
                return null;

            return Main()?.GetStats();
        }

        public double[] FrameTimes(int count) => Main()?.Recent(count) ?? Array.Empty<double>();

        private SwapChainFrames? Main()
        {
            long now = Environment.TickCount64;
            SwapChainFrames? main = null;
            foreach (var chain in _swapChains.Values)
            {
                if (now - chain.LastSeenTicks < 2500 && (main is null || chain.RecentCount > main.RecentCount))
                    main = chain;
            }
            return main;
        }
    }

    private sealed class SwapChainFrames
    {
        private const double BucketMs = 0.1;
        private readonly long[] _histogram = new long[(int)(MaxCountedFrameMs / BucketMs) + 1];
        private readonly Queue<double> _recent = new();
        private readonly double[] _times = new double[TimesLength];
        private int _timesNext, _timesCount;
        private const int TimesLength = 300;
        private double _lastTime = double.NaN;
        private long _countedFrames;
        private double _countedMs;

        public int RecentCount => _recent.Count;
        public long LastSeenTicks { get; private set; }

        public void Add(double timeMs)
        {
            if (!double.IsNaN(_lastTime))
            {
                double dt = timeMs - _lastTime;
                if (dt > 0 && dt <= MaxCountedFrameMs)
                {
                    _times[_timesNext] = dt;
                    _timesNext = (_timesNext + 1) % TimesLength;
                    _timesCount = Math.Min(_timesCount + 1, TimesLength);
                    _histogram[Math.Min((int)(dt / BucketMs), _histogram.Length - 1)]++;
                    _countedFrames++;
                    _countedMs += dt;
                }
            }
            _lastTime = timeMs;
            LastSeenTicks = Environment.TickCount64;

            _recent.Enqueue(timeMs);
            while (_recent.Count > 0 && _recent.Peek() < timeMs - 1000)
                _recent.Dequeue();
        }

        public double[] Recent(int count)
        {
            count = Math.Min(count, _timesCount);
            var result = new double[count];
            for (int i = 0; i < count; i++)
                result[i] = _times[(_timesNext - count + i + TimesLength) % TimesLength];
            return result;
        }

        public FrameStats? GetStats()
        {
            if (_recent.Count < 2 || _countedFrames == 0)
                return null;

            double span = _lastTime - _recent.Peek();
            double fps = span > 0 ? (_recent.Count - 1) * 1000.0 / span : 0;
            double average = _countedFrames * 1000.0 / _countedMs;

            long threshold = (long)Math.Ceiling(_countedFrames * 0.99);
            long cumulative = 0;
            double p99Ms = MaxCountedFrameMs;
            for (int i = 0; i < _histogram.Length; i++)
            {
                cumulative += _histogram[i];
                if (cumulative >= threshold)
                {
                    p99Ms = (i + 1) * BucketMs;
                    break;
                }
            }

            return new FrameStats(fps, average, 1000.0 / p99Ms, fps > 0 ? 1000.0 / fps : 0);
        }
    }
}

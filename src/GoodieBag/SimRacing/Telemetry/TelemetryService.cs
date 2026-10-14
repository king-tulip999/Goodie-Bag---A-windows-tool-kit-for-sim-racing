using System.Diagnostics;
using GoodieBag.Processes;

namespace GoodieBag.SimRacing.Telemetry;

internal sealed record TelemetryDiagnostics(
    SimStatus Status, bool Simulated, double FramesPerSecond, int TickRate, int Variables, int SessionUpdates,
    int Connects, string? LastError, IReadOnlyList<string> MissingCore, DateTime? LastFrameUtc);

internal sealed class TelemetryService : IDisposable
{
    public const string SimProcess = "iracingsim64dx11";

    private static readonly string[] CoreVars =
    {
        "SessionTime", "SessionNum", "SessionState", "SessionFlags", "PlayerCarIdx", "Speed", "RPM", "Gear", "FuelLevel",
        "LapDistPct", "LapCurrentLapTime", "CarIdxLapDistPct", "CarIdxPosition", "CarIdxClassPosition", "CarIdxEstTime", "CarLeftRight",
    };

    private readonly AppWatcher? _watcher;
    private readonly SynchronizationContext _ui;
    private readonly HashSet<string> _consumers = new(StringComparer.Ordinal);
    private readonly TripleBuffer<TelemetryFrame> _frames = new(() => new TelemetryFrame());
    private readonly AutoResetEvent _wake = new(false);
    private readonly DemoTelemetry _demo = new();
    private Thread? _thread;
    private volatile bool _stop, _simRunning, _simulated;
    private volatile SimStatus _status = SimStatus.NotRunning;
    private SimStatus _raisedStatus = SimStatus.NotRunning;
    private long _sequence;

    private volatile int _tickRate, _varCount, _sessionUpdates, _connects;
    private volatile string? _lastError;
    private string[] _missing = Array.Empty<string>();
    private double _fps;
    private long _lastFrameTicks;

    public TelemetryService(AppWatcher? watcher)
    {
        _watcher = watcher;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public event Action<SimStatus>? StatusChanged;

    public Action<TelemetryFrame>? FrameObserver { get; set; }

    private void Observe(TelemetryFrame f)
    {
        if (FrameObserver is not { } observe) return;
        try { observe(f); }
        catch (Exception ex) { _lastError = ex.Message; FrameObserver = null; App.LogError(ex); }
    }

    public SimStatus Status => _status;

    public bool Simulated
    {
        get => _simulated;
        set
        {
            if (_simulated == value) return;
            _simulated = value;
            _demo.Restart();
            _wake.Set();
        }
    }

    public bool Running => _thread is not null;

    public void Acquire(string consumer)
    {
        if (!_consumers.Add(consumer) || _consumers.Count > 1) return;
        if (_watcher is not null)
        {
            _watcher.Started += OnProcessStarted;
            _watcher.Stopped += OnProcessStopped;
            _watcher.Acquire("iRacing Overlay telemetry", new[] { SimProcess });
            _simRunning = _watcher.IsRunning(SimProcess);
        }
        _stop = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "Telemetry", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Release(string consumer)
    {
        if (!_consumers.Remove(consumer) || _consumers.Count > 0) return;
        if (_watcher is not null)
        {
            _watcher.Started -= OnProcessStarted;
            _watcher.Stopped -= OnProcessStopped;
            _watcher.Release("iRacing Overlay telemetry");
        }
        _stop = true;
        _wake.Set();
        _thread?.Join(2000);
        _thread = null;
        var f = _frames.Back;
        f.Reset();
        f.Session = SessionInfo.Empty;
        f.Sequence = ++_sequence;
        _frames.Publish();
        SetStatus(SimStatus.NotRunning);
    }

    public TelemetryFrame Take()
    {
        _frames.TryTake();
        return _frames.Front;
    }

    public TelemetryDiagnostics Diagnostics() => new(_status, _simulated, Volatile.Read(ref _fps), _tickRate, _varCount, _sessionUpdates, _connects,
        _lastError, Volatile.Read(ref _missing), Interlocked.Read(ref _lastFrameTicks) is > 0 and var t ? new DateTime(t, DateTimeKind.Utc) : null);

    public event Action? SimStarted;

    private void OnProcessStarted(string name)
    {
        if (name != SimProcess) return;
        _simRunning = true;
        _wake.Set();
        SimStarted?.Invoke();
    }

    private void OnProcessStopped(string name)
    {
        if (name != SimProcess) return;
        _simRunning = false;
        _wake.Set();
    }

    private void Run()
    {
        IrsdkMemory? mem = null;
        var decoder = new FrameDecoder();
        var session = SessionInfo.Empty;
        int sessionVersion = 0, lastSessionUpdate = -1, lastTick = int.MinValue;
        long lastData = 0;
        int framesThisSecond = 0;
        long second = Stopwatch.GetTimestamp();
        try
        {
            while (!_stop)
            {
                if (_simulated)
                {
                    if (mem is not null) { mem.Dispose(); mem = null; lastSessionUpdate = -1; }
                    var f = _frames.Back;
                    _demo.Fill(f);
                    f.Sequence = ++_sequence;
                    f.ReadAt = Stopwatch.GetTimestamp();
                    _frames.Publish();
                    Count(ref framesThisSecond, ref second);
                    SetStatus(SimStatus.Connected);
                    _wake.WaitOne(1000 / 30);
                    continue;
                }

                if (mem is null)
                {
                    if (!_simRunning && _watcher is not null)
                    {
                        PublishStatus(SimStatus.NotRunning);
                        _wake.WaitOne();
                        continue;
                    }
                    mem = IrsdkMemory.TryOpen();
                    if (mem is null)
                    {
                        PublishStatus(_simRunning ? SimStatus.Waiting : SimStatus.NotRunning);
                        _wake.WaitOne(1000);
                        continue;
                    }
                    _connects++;
                    session = SessionInfo.Empty;
                    sessionVersion++;
                    lastSessionUpdate = -1;
                    lastTick = int.MinValue;
                    lastData = Stopwatch.GetTimestamp();
                }

                bool signalled = mem.WaitForData(250);
                if (_stop || _simulated) continue;
                if (!mem.Connected || !signalled && Stopwatch.GetElapsedTime(lastData).TotalSeconds > 2)
                {
                    PublishStatus(SimStatus.Waiting);
                    if (_watcher is not null && !_simRunning)
                    {
                        mem.Dispose();
                        mem = null;
                    }
                    continue;
                }
                if (!signalled) continue;

                if (mem.VarsIfChanged() is { } vars)
                {
                    decoder.Bind(vars);
                    _varCount = vars.Count;
                    Volatile.Write(ref _missing, CoreVars.Where(v => !vars.ContainsKey(v)).ToArray());
                }
                if (mem.SessionInfoUpdate is var update && update != lastSessionUpdate)
                {
                    try
                    {
                        if (mem.SessionInfo() is { } yaml)
                        {
                            lastSessionUpdate = update;
                            session = SessionInfo.Parse(yaml);
                            sessionVersion++;
                            _sessionUpdates = sessionVersion;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastSessionUpdate = update;
                        _lastError = "Session information couldn't be read: " + ex.Message;
                    }
                }
                int tick = mem.CopyLatest();
                if (tick < 0 || tick == lastTick) continue;
                lastTick = tick;
                lastData = Stopwatch.GetTimestamp();
                _tickRate = mem.TickRate;

                var frame = _frames.Back;
                frame.Session = session;
                frame.SessionVersion = sessionVersion;
                decoder.Decode(mem.Data, frame);
                frame.Status = SimStatus.Connected;
                frame.Tick = tick;
                frame.Sequence = ++_sequence;
                frame.ReadAt = lastData;
                Observe(frame);
                _frames.Publish();
                Count(ref framesThisSecond, ref second);
                SetStatus(SimStatus.Connected);
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            App.LogError(ex);
        }
        finally
        {
            mem?.Dispose();
        }
    }

    private void Count(ref int frames, ref long second)
    {
        frames++;
        Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);
        double elapsed = Stopwatch.GetElapsedTime(second).TotalSeconds;
        if (elapsed < 1) return;
        Volatile.Write(ref _fps, frames / elapsed);
        frames = 0;
        second = Stopwatch.GetTimestamp();
    }

    private void PublishStatus(SimStatus status)
    {
        if (_status == status) return;
        var f = _frames.Back;
        f.Reset();
        f.Session = SessionInfo.Empty;
        f.Status = status;
        f.Sequence = ++_sequence;
        f.ReadAt = Stopwatch.GetTimestamp();
        Observe(f);
        _frames.Publish();
        Volatile.Write(ref _fps, 0);
        SetStatus(status);
    }

    private void SetStatus(SimStatus status)
    {
        if (_status == status) return;
        _status = status;
        _ui.Post(_ =>
        {
            if (_raisedStatus == _status) return;
            _raisedStatus = _status;
            StatusChanged?.Invoke(_status);
        }, null);
    }

    public void Dispose()
    {
        foreach (var c in _consumers.ToList()) Release(c);
        _wake.Dispose();
    }
}

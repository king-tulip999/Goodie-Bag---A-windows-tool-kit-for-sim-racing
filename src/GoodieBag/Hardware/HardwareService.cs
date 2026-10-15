using System.Collections.Concurrent;
using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using GoodieBag.Config;
using GoodieBag.Fans;

namespace GoodieBag.Hardware;

internal sealed record HardwareSnapshot
{
    public float? CpuTemp { get; init; }
    public float? CpuLoad { get; init; }
    public float? CpuClockMhz { get; init; }
    public float? CpuPower { get; init; }

    public float? GpuTemp { get; init; }
    public float? GpuHotSpot { get; init; }
    public float? GpuLoad { get; init; }
    public float? GpuClockMhz { get; init; }
    public float? GpuPower { get; init; }
    public float? VramUsedMb { get; init; }
    public float? VramTotalMb { get; init; }
    public float? GpuFanRpm { get; init; }
    public float? GpuFanPercent { get; init; }

    public float? DiskReadBps { get; init; }
    public float? DiskWriteBps { get; init; }
    public float? DiskActivePercent { get; init; }

    public float? RamUsedGb { get; init; }
    public float? RamTotalGb { get; init; }
}

internal sealed record TempSensorInfo(string Id, string Name, string Hardware, float? Value)
{
    public string Label => $"{Hardware} · {Name}";
}

internal sealed record FanInfo(string ControlId, string DefaultName, string Hardware, float? Percent, float? Rpm,
    bool Controlled, string? Error, bool Controllable = true, bool HasRpm = true);

internal sealed record CurveLive(float? Temp, float? Output);

internal sealed record DeviceInfo(string Kind, string Name, int Sensors);

internal sealed record HardwareCatalog(
    IReadOnlyList<TempSensorInfo> Temperatures,
    IReadOnlyList<FanInfo> Fans,
    IReadOnlyDictionary<string, CurveLive> Curves,
    bool Emergency);

internal sealed class HardwareService : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, bool> _consumers = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly ConcurrentQueue<Action> _requests = new();
    private readonly SynchronizationContext _ui;
    private readonly ISensorSource[] _sources = { new Sources.MemoryModulesSource(), new Sources.VolumesSource() };
    private readonly ConcurrentDictionary<ISensorSource, (long NextMs, IReadOnlyList<DeviceNode> Nodes)> _sourceCache = new();
    private Thread? _thread;
    private volatile bool _stopping;
    private volatile bool _disposed;
    private volatile bool _fullTree;
    private volatile bool _resetMinMax;
    private bool _openErrorReported;
    private bool _restartWanted;
    private long _openFailedAtMs = -1;
    private const long OpenRetryMs = 5 * 60 * 1000;
    private SnapshotSensors? _snapshotSensors;
    private List<(ISensor Sensor, string Id, string Name, string Hardware)>? _tempSensors;
    private int _sensorSignature = -1;
    private Computer? _computer;
    private IHardware? _cpu;
    private IHardware? _gpu;
    private readonly List<IHardware> _memory = new();
    private readonly DiskActivity _disk = new();
    private FanController? _fans;
    private volatile FanSettings _pendingFanSettings;
    private int _reads;

    public volatile HardwareSnapshot? Latest;
    public volatile HardwareCatalog? Catalog;

    public volatile SensorTree? Tree;

    public volatile bool OverlayActive;
    public volatile int OverlayIntervalMs = 500;

    private volatile int _monitorMs = 1000;
    private volatile bool _reduced;
    private volatile bool _windowHidden;
    private volatile bool _publishNow;

    public volatile IReadOnlyList<DeviceInfo>? Devices;

    public event Action<string, string>? Problem;

    public volatile bool Ready;
    public volatile string? OpenError;

    public volatile bool CpuTempMissing;

    private PawnIoState _pawnIo;
    private long _pawnIoReadMs = -1;

    public PawnIoState PawnIoState
    {
        get
        {
            if (_demo is not null)
                return PawnIoState.Installed;
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref _pawnIoReadMs);
            if (last < 0 || now - last > 3000)
            {
                _pawnIo = PawnIo.Read();
                Interlocked.Exchange(ref _pawnIoReadMs, now);
            }
            return _pawnIo;
        }
    }

    public bool PawnIoLikelyMissing => PawnIoState != PawnIoState.Installed;

    private DemoHardware? _demo;
    private System.Threading.Timer? _demoTimer;
    private readonly object _demoGate = new();

    public bool Running
    {
        get { lock (_gate) return _consumers.Count > 0; }
    }

    public IReadOnlyList<string> Consumers
    {
        get { lock (_gate) return _consumers.Keys.ToList(); }
    }

    public HardwareService()
    {
        _ui = SynchronizationContext.Current ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();
        _pendingFanSettings = new FanSettings { Enabled = false };
    }

    public void UseDemo() => _demo = new DemoHardware();

    public void Acquire(string consumer, bool fullTree = false)
    {
        lock (_gate)
            _consumers[consumer] = fullTree;
        Reconcile();
    }

    public void Release(string consumer)
    {
        lock (_gate)
        {
            if (!_consumers.Remove(consumer))
                return;
        }
        Reconcile();
    }

    public void ResetMinMax()
    {
        _resetMinMax = true;
        _demo?.ResetMinMax();
        Nudge();
    }

    public void Configure(int intervalMs, BackgroundUsage background)
    {
        _monitorMs = Math.Clamp(intervalMs, PerformanceSettings.MinMs, PerformanceSettings.MaxMs);
        _reduced = background == BackgroundUsage.Reduced;
        Nudge();
        RestartDemoTimer();
    }

    public void SetWindowHidden(bool hidden)
    {
        if (_windowHidden == hidden) return;
        _windowHidden = hidden;
        if (!_reduced) return;
        Nudge();
        RestartDemoTimer();
    }

    public int MonitorIntervalMs
    {
        get
        {
            int ms = PerformanceSettings.Effective(_monitorMs, _reduced ? BackgroundUsage.Reduced : BackgroundUsage.Standard, _windowHidden);
            return OverlayActive ? Math.Min(ms, Math.Clamp(OverlayIntervalMs, 100, 5000)) : ms;
        }
    }

    private void RestartDemoTimer()
    {
        if (_demo is null || _demoTimer is null || !Running) return;
        _demoTimer.Change(0, MonitorIntervalMs);
    }

    public void RefreshNow()
    {
        Nudge();
        if (_demo is not null && Running) Task.Run(DemoStep);
    }

    public bool Rescan()
    {
        _reads = 0;
        CpuTempMissing = false;
        Interlocked.Exchange(ref _pawnIoReadMs, -1);
        _openErrorReported = false;
        if (_demo is not null)
        {
            RefreshNow();
            return Running;
        }
        lock (_gate)
        {
            _openFailedAtMs = -1;
            if (_consumers.Count == 0 || _disposed)
                return false;
            if (_thread is not null)
            {
                _restartWanted = true;
                _stopping = true;
                _wake.Set();
                return true;
            }
        }
        Reconcile();
        return true;
    }

    private void Reconcile()
    {
        bool want, full;
        lock (_gate)
        {
            want = _consumers.Count > 0 && !_disposed;
            full = _consumers.Values.Any(v => v);
        }
        if (full && !_fullTree) _publishNow = true;
        _fullTree = full;

        if (_demo is not null)
        {
            _demoTimer ??= new System.Threading.Timer(_ => DemoStep(), null, Timeout.Infinite, Timeout.Infinite);
            _demoTimer.Change(want ? 0 : Timeout.Infinite, MonitorIntervalMs);
            if (!want)
            {
                lock (_demoGate)
                    ClearPublished();
            }
            return;
        }

        lock (_gate)
        {
            if (want)
            {
                if (_thread is not null)
                {
                    if (_stopping)
                        _restartWanted = true;
                    else
                        _wake.Set();
                    return;
                }
                if (_openFailedAtMs >= 0 && Environment.TickCount64 - _openFailedAtMs < OpenRetryMs)
                    return;
                StartThread();
            }
            else if (_thread is not null)
            {
                _restartWanted = false;
                _stopping = true;
                _wake.Set();
            }
        }
    }

    private void StartThread()
    {
        _stopping = false;
        _restartWanted = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "Goodie Bag hardware" };
        _thread.Start();
    }

    private void DemoStep()
    {
        if (!System.Threading.Monitor.TryEnter(_demoGate))
            return;
        try
        {
            if (_disposed || !Running)
                return;
            var (snapshot, catalog) = _demo!.Step(_pendingFanSettings);
            Latest = snapshot;
            Catalog = catalog;
            if (_fullTree) Tree = _demo.Tree(snapshot, catalog);
            Devices ??= (Tree ?? _demo.Tree(snapshot, catalog)).Devices.Select(d => new DeviceInfo(d.Kind.ToString(), d.Name, d.AllSensors.Count())).ToList();
            Ready = true;
        }
        catch (Exception ex)
        {
            LogOnce("demo", ex);
        }
        finally
        {
            System.Threading.Monitor.Exit(_demoGate);
        }
    }

    private void ClearPublished()
    {
        Devices = null;
        Latest = null;
        Catalog = null;
        Tree = null;
        Ready = false;
    }

    public void SetFanSettings(FanSettings settings)
    {
        _pendingFanSettings = settings;
        if (_demo is not null)
            return;
        Post(() => _fans?.SetSettings(settings));
    }

    public void SpinUp(string controlId)
    {
        if (_demo is not null) { _demo.SpinUp(controlId); return; }
        Post(() => _fans?.SpinUp(controlId, TimeSpan.FromSeconds(8)));
    }

    public async Task<string?> ReadFansAsync(Func<FanController, string?> read)
    {
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_thread is not { IsAlive: true }) return null;
        Post(() => { try { done.TrySetResult(_fans is null ? null : read(_fans)); } catch (Exception ex) { done.TrySetResult(ex.Message); } });
        try { return await done.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (TimeoutException) { return "no answer from the hardware thread in 15 s"; }
    }

    public void SetOverride(string controlId, float percent)
    {
        if (_demo is not null) { _demo.SetOverride(controlId, percent); return; }
        _overrides[controlId] = 0;
        Post(() => _fans?.SetOverride(controlId, percent));
    }

    public void ClearOverride(string controlId)
    {
        if (_demo is not null) { _demo.ClearOverride(controlId); return; }
        _overrides.TryRemove(controlId, out _);
        Post(() => _fans?.ClearOverride(controlId));
    }

    private readonly ConcurrentDictionary<string, byte> _overrides = new();

    public void Nudge()
    {
        _publishNow = true;
        _wake.Set();
    }

    public void EmergencyRestore()
    {
        lock (_gate)
            _restartWanted = false;
        _stopping = true;
        _wake.Set();
        var thread = _thread;
        if (thread is { IsAlive: true } && Thread.CurrentThread != thread && thread.Join(2000))
            return;
        try { _fans?.RestoreAll(); } catch { }
    }

    private void Post(Action action)
    {
        if (_thread is not { IsAlive: true })
            return;
        _requests.Enqueue(action);
        _wake.Set();
    }

    private void Raise(string title, string message) => _ui.Post(_ => Problem?.Invoke(title, message), null);

    private void Run()
    {
        bool opened = false;
        try
        {
            opened = RunSensors();
        }
        catch (Exception ex)
        {
            LogOnce("run", ex);
            try { _fans?.RestoreAll(); } catch { }
            _fans?.Dispose();
            _fans = null;
            ClearPublished();
        }
        finally
        {
            lock (_gate)
            {
                if (opened)
                    _openFailedAtMs = -1;
                else
                    _openFailedAtMs = Environment.TickCount64;
                if (opened && _restartWanted && _consumers.Count > 0 && !_disposed)
                {
                    StartThread();
                }
                else
                {
                    _restartWanted = false;
                    _thread = null;
                }
            }
        }
    }

    private static void KeepNoHistory(IComputer computer)
    {
        static void Sensor(ISensor sensor) => sensor.ValuesTimeWindow = TimeSpan.Zero;
        static void Device(IHardware hw)
        {
            foreach (var sensor in hw.Sensors) Sensor(sensor);
            hw.SensorAdded += Sensor;
            foreach (var sub in hw.SubHardware) Device(sub);
        }
        foreach (var hw in computer.Hardware) Device(hw);
        computer.HardwareAdded += Device;
    }

    private bool RunSensors()
    {
        while (_requests.TryDequeue(out _)) { }
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
        };
        FanController fans;
        try
        {
            OpenLhm(computer);
            KeepNoHistory(computer);
            _computer = computer;
            PickHardware();
            fans = new FanController(computer, _pendingFanSettings);
            fans.Problem += Raise;
            _fans = fans;
            OpenError = null;
        }
        catch (Exception ex)
        {
            OpenError = ex.Message;
            Ready = true;
            if (!_openErrorReported)
            {
                _openErrorReported = true;
                Raise("Hardware sensors unavailable", ex.Message);
            }
            CloseLhm(computer);
            _computer = null;
            _cpu = null;
            _gpu = null;
            _memory.Clear();
            return false;
        }
        Ready = true;

        var clock = Stopwatch.StartNew();
        double lastSeconds = 0;
        long nextReadMs = 0, nextDeepMs = 0;

        while (!_stopping)
        {
            int monitorMs = MonitorIntervalMs;
            bool fanLoop = _pendingFanSettings.Enabled || !_overrides.IsEmpty;
            long nowMs = Environment.TickCount64;
            bool deep = _publishNow || nowMs >= nextDeepMs - 15;
            bool publish = deep || nowMs >= nextReadMs - 15;
            if (publish)
            {
                _publishNow = false;
                nextReadMs = nowMs + monitorMs;
            }
            if (deep)
                nextDeepMs = nowMs + Math.Max(monitorMs, UserIntervalMs);
            bool quick = !deep && !fanLoop;

            while (_requests.TryDequeue(out var request))
            {
                try { request(); } catch { }
            }

            HardwareSnapshot? snapshot = null;
            bool full = _fullTree;
            try
            {
                SyncExtra(full, monitorMs);
                if (quick) UpdateSnapshotDevices();
                else UpdateDevices(computer);

                int signature = quick ? _sensorSignature : SensorSignature(computer);
                if (signature != _sensorSignature)
                {
                    PickDevices(computer);
                    Devices = DescribeDevices(computer);
                    try { fans.Reindex(computer); }
                    catch (Exception ex) { LogOnce("fan reindex", ex); }
                    _sensorSignature = signature;
                }

                if (_resetMinMax)
                {
                    _resetMinMax = false;
                    foreach (var sensor in computer.Hardware.SelectMany(AllSensors))
                    {
                        sensor.ResetMin();
                        sensor.ResetMax();
                    }
                }

                snapshot = ReadSnapshot();
            }
            catch (Exception ex)
            {
                LogOnce("read", ex);
            }

            double now = clock.Elapsed.TotalSeconds;
            try { fans.Tick(Math.Clamp(now - lastSeconds, 0.05, 5), snapshot?.CpuTemp, snapshot?.GpuTemp); }
            catch (Exception ex) { LogOnce("fans", ex); }
            lastSeconds = now;

            if (snapshot is not null && (publish || fanLoop))
            {
                Latest = snapshot;
                if (deep || fanLoop)
                {
                    try { Catalog = BuildCatalog(); }
                    catch (Exception ex) { LogOnce("catalog", ex); }
                }
            }
            if (snapshot is not null && deep)
            {
                try { if (full) Tree = BuildTree(); }
                catch (Exception ex) { LogOnce("tree", ex); }
                try { CheckForMissingSensors(snapshot); }
                catch (Exception ex) { LogOnce("missing sensors", ex); }
            }

            int wait = PerformanceSettings.LoopWaitMs(nextReadMs - Environment.TickCount64, monitorMs, fanLoop, calibrating: !_overrides.IsEmpty);
            _wake.Wait(wait);
            _wake.Reset();
        }

        try { _slowRead?.Wait(2000); } catch { }
        try { fans.RestoreAll(); } catch { }
        try { SyncExtra(false, finishing: true); } catch (Exception ex) { LogOnce("close storage / network", ex); }
        CloseLhm(computer);
        fans.Dispose();
        _fans = null;
        _computer = null;
        _cpu = null;
        _gpu = null;
        _memory.Clear();
        InvalidateSensorCache();
        _sensorSignature = -1;
        _disk.Dispose();
        _sourceCache.Clear();
        ClearPublished();
        return true;
    }

    private const int SlowUpdateMs = 250;
    private const int SlowIntervalMs = 3000;
    private readonly HashSet<IHardware> _slow = new();
    private Task? _slowRead;
    private IHardware[] _slowReading = [];
    private long _slowNextMs;

    private int UserIntervalMs => PerformanceSettings.Effective(_monitorMs, _reduced ? BackgroundUsage.Reduced : BackgroundUsage.Standard, _windowHidden);

    private void UpdateSnapshotDevices()
    {
        void One(IHardware? hw)
        {
            if (hw is null || _slow.Contains(hw)) return;
            try { Update(hw); }
            catch (Exception ex) { LogOnce("update " + hw.HardwareType, ex); }
        }
        One(_cpu);
        One(_gpu);
        foreach (var m in _memory) One(m);
    }

    private void UpdateDevices(IComputer computer)
    {
        foreach (var hw in computer.Hardware)
        {
            if (_slow.Contains(hw) || (_slowRead is { IsCompleted: false } && _slowReading.Contains(hw))) continue;
            long start = Stopwatch.GetTimestamp();
            try { Update(hw); }
            catch (Exception ex) { LogOnce("update " + hw.HardwareType, ex); }
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (ms > SlowUpdateMs && !HasControls(hw))
            {
                _slow.Add(hw);
                LogOnce("slow " + hw.HardwareType, new TimeoutException($"{hw.Name} ({hw.HardwareType}) took {ms:0} ms to update; it's now read in the background every {SlowIntervalMs / 1000} s."));
            }
        }
        long now = Environment.TickCount64;
        if (_slow.Count == 0 || now < _slowNextMs || _slowRead is { IsCompleted: false }) return;
        _slowNextMs = now + SlowIntervalMs;
        var slow = _slowReading = _slow.ToArray();
        _slowRead = Task.Run(() =>
        {
            foreach (var hw in slow)
            {
                try { Update(hw); }
                catch (Exception ex) { LogOnce("update " + hw.HardwareType, ex); }
            }
        });
    }

    private volatile Computer? _extra;
    private volatile Computer? _extraToClose;
    private Task? _extraOpen, _extraRead;
    private long _extraNextMs;

    private void SyncExtra(bool want, int intervalMs = 1000, bool finishing = false)
    {
        if (want && _extra is null && _extraOpen is null && _extraToClose is null)
        {
            _extraOpen = Task.Run(() =>
            {
                var extra = new Computer { IsStorageEnabled = true, IsNetworkEnabled = true };
                try
                {
                    OpenLhm(extra);
                    KeepNoHistory(extra);
                    foreach (var hw in extra.Hardware)
                    {
                        try { Update(hw); } catch (Exception ex) { LogOnce("update " + hw.HardwareType, ex); }
                    }
                    if (_fullTree && !_stopping) _extra = extra;
                    else HandBack(extra);
                }
                catch (Exception ex)
                {
                    LogOnce("open storage / network", ex);
                    HandBack(extra);
                }
            });
            _extraOpen.ContinueWith(_ => _extraOpen = null);
        }
        else if (!want && _extra is { } open)
        {
            _extra = null;
            _extraToClose = open;
        }

        if (_extraToClose is { } closing)
        {
            if (finishing) { try { _extraRead?.Wait(5000); } catch { } }
            if (_extraRead is not { IsCompleted: false })
            {
                _extraToClose = null;
                CloseLhm(closing);
            }
        }

        long now = Environment.TickCount64;
        if (!finishing && _extra is { } current && now >= _extraNextMs && _extraRead is not { IsCompleted: false })
        {
            _extraNextMs = now + Math.Max(1000, intervalMs);
            _extraRead = Task.Run(() =>
            {
                foreach (var hw in current.Hardware)
                {
                    try { Update(hw); } catch (Exception ex) { LogOnce("update " + hw.HardwareType, ex); }
                }
            });
        }
    }

    private void HandBack(Computer extra)
    {
        if (_thread is { IsAlive: true }) _extraToClose = extra;
        else CloseLhm(extra);
    }

    private static readonly object LhmGate = new();
    private static int _lhmOpen;

    internal static readonly System.Reflection.MethodInfo? ReopenLocks = typeof(Computer).Assembly
        .GetType("LibreHardwareMonitor.Hardware.Mutexes")
        ?.GetMethod("Open", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

    private static void OpenLhm(Computer computer)
    {
        try { computer.Open(); }
        finally { lock (LhmGate) _lhmOpen++; }
    }

    private void CloseLhm(Computer computer)
    {
        lock (LhmGate)
        {
            try { computer.Close(); } catch { }
            _lhmOpen = Math.Max(0, _lhmOpen - 1);
            if (_lhmOpen == 0) return;
            try
            {
                if (ReopenLocks is null) throw new MissingMethodException("LibreHardwareMonitor.Hardware.Mutexes.Open");
                ReopenLocks.Invoke(null, null);
            }
            catch (Exception ex) { LogOnce("reopen chip locks", ex); }
        }
    }

    private static bool HasControls(IHardware hw)
    {
        foreach (var s in hw.Sensors)
            if (s.SensorType == SensorType.Control) return true;
        foreach (var sub in hw.SubHardware)
            if (HasControls(sub)) return true;
        return false;
    }

    private readonly HashSet<string> _logged = new();

    private void LogOnce(string stage, Exception ex)
    {
        lock (_logged)
        if (_logged.Count < 50 && _logged.Add(stage + "|" + ex.GetType().Name + "|" + ex.Message))
            App.LogError(new InvalidOperationException($"Hardware read ({stage}) failed; carrying on.", ex));
    }

    private void InvalidateSensorCache()
    {
        _slow.Clear();
        _snapshotSensors = null;
        _tempSensors = null;
    }

    private static int SensorSignature(IComputer computer)
    {
        int n = 0;
        foreach (var hw in computer.Hardware)
            n = n * 31 + CountSensors(hw) + 1;
        return n;
    }

    private static int CountSensors(IHardware hw)
    {
        int n = hw.Sensors.Count();
        foreach (var sub in hw.SubHardware)
            n += CountSensors(sub) + 1;
        return n;
    }

    private void PickHardware()
    {
        foreach (var hw in _computer!.Hardware)
            Update(hw);
        PickDevices(_computer);
        Devices = DescribeDevices(_computer);
    }

    private static IReadOnlyList<DeviceInfo> DescribeDevices(IComputer computer)
    {
        var list = new List<DeviceInfo>();
        foreach (var hw in computer.Hardware)
        {
            list.Add(new DeviceInfo(hw.HardwareType.ToString(), hw.Name, hw.Sensors.Count()));
            foreach (var sub in hw.SubHardware)
                list.Add(new DeviceInfo(sub.HardwareType.ToString(), sub.Name, AllSensors(sub).Count()));
        }
        return list;
    }

    private void PickDevices(IComputer computer)
    {
        _cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);

        _gpu = computer.Hardware
            .Where(h => h.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel)
            .OrderBy(h => h.HardwareType == HardwareType.GpuIntel ? 1 : 0)
            .ThenByDescending(h => First(Find(h, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total")) ?? 0)
            .FirstOrDefault();

        _memory.Clear();
        _memory.AddRange(computer.Hardware.Where(h => h.HardwareType == HardwareType.Memory));
        InvalidateSensorCache();
    }

    private sealed class SnapshotSensors
    {
        public ISensor[] CpuTemp = [], CpuTemps = [], CpuLoad = [], CpuCoreClocks = [], CpuPower = [];
        public ISensor[] GpuTemp = [], GpuHotSpot = [], GpuLoad = [], GpuClock = [], GpuPower = [];
        public ISensor[] VramUsed = [], VramTotal = [], GpuFanRpm = [], GpuFanPercent = [];
        public ISensor[] RamUsed = [], RamAvailable = [];
    }

    private SnapshotSensors ResolveSnapshotSensors()
    {
        var set = new SnapshotSensors();
        if (_cpu is { } cpu)
        {
            set.CpuTemp = Find(cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Core (Tdie)", "Core Max", "Core Average");
            set.CpuTemps = AllSensors(cpu).Where(s => s.SensorType == SensorType.Temperature).ToArray();
            set.CpuLoad = Find(cpu, SensorType.Load, "CPU Total");
            set.CpuCoreClocks = AllSensors(cpu)
                .Where(s => s.SensorType == SensorType.Clock && s.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            set.CpuPower = Find(cpu, SensorType.Power, "Package", "CPU Package");
        }
        if (_gpu is { } gpu)
        {
            set.GpuTemp = Find(gpu, SensorType.Temperature, "GPU Core");
            set.GpuHotSpot = Find(gpu, SensorType.Temperature, "GPU Hot Spot");
            set.GpuLoad = Find(gpu, SensorType.Load, "GPU Core", "D3D 3D");
            set.GpuClock = Find(gpu, SensorType.Clock, "GPU Core");
            set.GpuPower = Find(gpu, SensorType.Power, "GPU Package", "GPU Core", "GPU Power");
            set.VramUsed = Find(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
            set.VramTotal = Find(gpu, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");
            set.GpuFanRpm = Find(gpu, SensorType.Fan, "GPU Fan", "GPU Fan 1");
            set.GpuFanPercent = Find(gpu, SensorType.Control, "GPU Fan", "GPU Fan 1");
        }
        var used = new List<ISensor>();
        var available = new List<ISensor>();
        foreach (var m in _memory)
        {
            if (m.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                continue;
            used.AddRange(Find(m, SensorType.Data, "Memory Used"));
            available.AddRange(Find(m, SensorType.Data, "Memory Available"));
        }
        set.RamUsed = used.ToArray();
        set.RamAvailable = available.ToArray();
        return set;
    }

    private HardwareSnapshot ReadSnapshot()
    {
        var set = _snapshotSensors ??= ResolveSnapshotSensors();
        float? ramUsed = First(set.RamUsed), ramAvailable = First(set.RamAvailable);
        var (diskRead, diskWrite, diskActive) = _disk.Read();

        return new HardwareSnapshot
        {
            CpuTemp = First(set.CpuTemp) ?? MaxPositive(set.CpuTemps),
            CpuLoad = First(set.CpuLoad),
            CpuClockMhz = AveragePositive(set.CpuCoreClocks),
            CpuPower = First(set.CpuPower),

            GpuTemp = First(set.GpuTemp),
            GpuHotSpot = First(set.GpuHotSpot),
            GpuLoad = First(set.GpuLoad),
            GpuClockMhz = First(set.GpuClock),
            GpuPower = First(set.GpuPower),
            VramUsedMb = First(set.VramUsed),
            VramTotalMb = First(set.VramTotal),
            GpuFanRpm = First(set.GpuFanRpm),
            GpuFanPercent = First(set.GpuFanPercent),
            DiskReadBps = diskRead,
            DiskWriteBps = diskWrite,
            DiskActivePercent = diskActive,

            RamUsedGb = ramUsed,
            RamTotalGb = ramUsed + ramAvailable,
        };
    }

    private HardwareCatalog BuildCatalog()
    {
        if (_tempSensors is null)
        {
            _tempSensors = new();
            foreach (var hw in _computer!.Hardware)
            {
                foreach (var sensor in AllSensors(hw).Where(s => s.SensorType == SensorType.Temperature))
                    _tempSensors.Add((sensor, sensor.Identifier.ToString(), sensor.Name, sensor.Hardware.Name));
            }
        }

        var temps = new List<TempSensorInfo>(_tempSensors.Count);
        foreach (var (sensor, id, name, hardware) in _tempSensors)
            temps.Add(new TempSensorInfo(id, name, hardware, sensor.Value));

        return new HardwareCatalog(temps, _fans!.DescribeFans(), _fans.LiveCurves, _fans.Emergency);
    }

    private SensorTree BuildTree()
    {
        var devices = _computer!.Hardware.Select(ToNode).ToList();
        if (_extra is { } extra) devices.AddRange(extra.Hardware.Select(ToNode));
        foreach (var source in _sources)
        {
            if (!_sourceCache.TryGetValue(source, out var cached))
                cached = (0, Array.Empty<DeviceNode>());
            if (Environment.TickCount64 >= cached.NextMs)
            {
                _sourceCache[source] = cached = (long.MaxValue, cached.Nodes);
                var s = source;
                long interval = (long)source.Interval.TotalMilliseconds;
                Task.Run(() =>
                {
                    IReadOnlyList<DeviceNode> nodes;
                    try { nodes = s.Read(); }
                    catch (Exception ex) { LogOnce("source " + s.Name, ex); nodes = Array.Empty<DeviceNode>(); }
                    _sourceCache[s] = (Environment.TickCount64 + interval, nodes);
                });
            }
            devices.AddRange(cached.Nodes);
        }
        return new SensorTree(devices);
    }

    private static DeviceNode ToNode(IHardware hw) => new(
        hw.Identifier.ToString(),
        hw.Name,
        hw.HardwareType switch
        {
            HardwareType.Cpu => DeviceKind.Cpu,
            HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel => DeviceKind.Gpu,
            HardwareType.Memory => DeviceKind.Memory,
            HardwareType.Motherboard => DeviceKind.Motherboard,
            HardwareType.SuperIO or HardwareType.EmbeddedController => DeviceKind.SuperIO,
            HardwareType.Storage => DeviceKind.Storage,
            HardwareType.Network => DeviceKind.Network,
            HardwareType.Cooler => DeviceKind.Controller,
            HardwareType.Battery => DeviceKind.Battery,
            HardwareType.Psu => DeviceKind.Psu,
            _ => DeviceKind.Other,
        },
        hw.Sensors.Select(s => new SensorReading(s.Identifier.ToString(), s.Name, Kind(s.SensorType), s.Value, s.Min, s.Max)).ToList(),
        hw.SubHardware.Select(ToNode).ToList(),
        "LibreHardwareMonitor");

    private static SensorKind Kind(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Load => SensorKind.Load,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Power => SensorKind.Power,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Current => SensorKind.Current,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Control => SensorKind.Control,
        SensorType.Data => SensorKind.Data,
        SensorType.SmallData => SensorKind.SmallData,
        SensorType.Throughput => SensorKind.Throughput,
        SensorType.Frequency => SensorKind.Frequency,
        SensorType.Level => SensorKind.Level,
        SensorType.Factor => SensorKind.Factor,
        SensorType.Energy => SensorKind.Energy,
        SensorType.TimeSpan => SensorKind.TimeSpan,
        SensorType.Flow => SensorKind.Flow,
        SensorType.Noise => SensorKind.Noise,
        _ => SensorKind.Other,
    };

    private void CheckForMissingSensors(HardwareSnapshot snapshot)
    {
        if (++_reads != 5)
            return;

        if (_cpu is not null && snapshot.CpuTemp is null)
        {
            CpuTempMissing = true;
            Raise("CPU temperature isn't available", PawnIoLikelyMissing
                ? "Reading CPU temps (and motherboard fans) needs the free PawnIO driver. Install it from Settings → Hardware access, then restart Goodie Bag."
                : "PawnIO is installed, but the CPU's sensors didn't answer. Restart Goodie Bag; if it stays missing, this CPU may not be supported by the sensor library yet.");
        }
        if (_gpu is null)
            Raise("No GPU found", "LibreHardwareMonitor didn't detect a graphics card.");
    }

    internal static void Update(IHardware hw)
    {
        hw.Update();
        foreach (var sub in hw.SubHardware)
            Update(sub);
    }

    internal static IEnumerable<ISensor> AllSensors(IHardware hw) =>
        hw.Sensors.Concat(hw.SubHardware.SelectMany(AllSensors));

    private static ISensor[] Find(IHardware hw, SensorType type, params string[] names)
    {
        var sensors = AllSensors(hw).Where(s => s.SensorType == type).ToList();
        var found = new List<ISensor>();
        foreach (var name in names)
            found.AddRange(sensors.Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)));
        return found.ToArray();
    }

    private static float? First(ISensor[] candidates)
    {
        foreach (var s in candidates)
        {
            if (s.Value is { } v)
                return v;
        }
        return null;
    }

    private static float? MaxPositive(ISensor[] sensors)
    {
        float? max = null;
        foreach (var s in sensors)
        {
            if (s.Value is { } v && v > 0 && (max is null || v > max))
                max = v;
        }
        return max;
    }

    private static float? AveragePositive(ISensor[] sensors)
    {
        double sum = 0;
        int count = 0;
        foreach (var s in sensors)
        {
            if (s.Value is { } v && v > 0)
            {
                sum += v;
                count++;
            }
        }
        return count > 0 ? (float)(sum / count) : null;
    }

    public void Dispose()
    {
        _disposed = true;
        _demoTimer?.Dispose();
        lock (_gate)
            _consumers.Clear();
        _stopping = true;
        _wake.Set();
        var thread = _thread;
        if (thread is { IsAlive: true } && !thread.Join(7000))
        {
            try { _fans?.RestoreAll(); } catch { }
        }
    }
}

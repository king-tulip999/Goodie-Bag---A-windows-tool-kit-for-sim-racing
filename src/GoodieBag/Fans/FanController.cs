using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using GoodieBag.Config;
using GoodieBag.Hardware;

namespace GoodieBag.Fans;

internal sealed class FanController : IDisposable
{
    private const double ResendSeconds = 5;
    private const float EmergencyReleaseMarginC = 5;
    private const double StartSeconds = 3;
    private const float RestartMargin = 2;
    private const double VerifySeconds = 2;
    private const float VerifyTolerance = 4;
    private const int MissesBeforeError = 3;
    private const int HandBackTries = 10;
    private const int HandBackGiveUp = 60;

    private readonly Dictionary<string, ISensor> _controls = new();
    private readonly Dictionary<string, ISensor?> _rpmForControl = new();
    private readonly Dictionary<string, ISensor> _temps = new();
    private readonly Dictionary<string, WriteState> _written = new();
    private readonly Dictionary<string, string> _errors = new();
    private readonly Dictionary<string, int> _handBack = new();
    private readonly Dictionary<string, CurveState> _curveState = new();
    private readonly Dictionary<string, long> _spinUntil = new();
    private readonly Dictionary<string, float> _overrides = new();
    private readonly Dictionary<string, ISensor> _monitorOnly = new();
    private readonly Dictionary<string, (string Label, string Hardware)> _names = new();
    private readonly Dictionary<FanCurve, CurvePoint[]> _sortedPoints = new();
    private readonly Dictionary<string, float?> _results = new();
    private readonly HashSet<string> _visiting = new();
    private readonly Dictionary<string, float> _targets = new();
    private readonly Dictionary<string, FanDrive> _drives = new();
    private readonly List<string> _scratchIds = new();
    private readonly Dictionary<string, AmdFanCurve> _amd = new();
    private readonly Dictionary<int, AmdFanCurve?> _amdAdapters = new();
    public readonly Dictionary<int, string> AmdNotes = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private FanSettings _settings;
    private bool _emergencyReported;

    public bool Emergency { get; private set; }
    public IReadOnlyDictionary<string, CurveLive> LiveCurves { get; private set; } = new Dictionary<string, CurveLive>();

    public event Action<string, string>? Problem;

    private sealed class WriteState
    {
        public float Value;
        public double At;
        public int Misses;
    }

    private sealed class CurveState
    {
        public float? UsedTemp;
        public float? Output;
    }

    internal sealed class FanDrive
    {
        public float? Value;
        public bool Stopped;
        public double StartUntil;
    }

    internal struct MixAccumulator
    {
        private readonly MixFunction _function;
        private float _acc;
        private int _count;

        public MixAccumulator(MixFunction function) => _function = function;

        public void Add(float value)
        {
            _acc = _count == 0 ? value : _function switch
            {
                MixFunction.Min => Math.Min(_acc, value),
                MixFunction.Average or MixFunction.Sum => _acc + value,
                MixFunction.Subtract => _acc - value,
                _ => Math.Max(_acc, value),
            };
            _count++;
        }

        public readonly float? Result => _count == 0 ? null : Math.Clamp(_function == MixFunction.Average ? _acc / _count : _acc, 0, 100);
    }

    public FanController(IComputer computer, FanSettings settings)
    {
        _settings = settings;
        foreach (var hw in computer.Hardware)
            Index(hw);
    }

    public void Reindex(IComputer computer)
    {
        var oldControls = new Dictionary<string, ISensor>(_controls);
        var oldAmd = new Dictionary<string, AmdFanCurve>(_amd);
        _controls.Clear();
        _amd.Clear();
        _rpmForControl.Clear();
        _temps.Clear();
        _monitorOnly.Clear();
        _names.Clear();
        foreach (var hw in computer.Hardware)
            Index(hw);

        foreach (var id in _written.Keys.Concat(_handBack.Keys).Distinct().ToList())
        {
            if (_controls.ContainsKey(id)) continue;
            try
            {
                if (oldAmd.TryGetValue(id, out var gone)) gone.Restore();
                else if (oldControls.TryGetValue(id, out var lost)) HandBack(lost);
            }
            catch (Exception) { }
        }

        Forget(_written.Keys);
        Forget(_drives.Keys);
        Forget(_overrides.Keys);
        Forget(_spinUntil.Keys);
        Forget(_errors.Keys);
        Forget(_handBack.Keys);
        _scratchIds.Clear();

        void Forget(IEnumerable<string> ids)
        {
            _scratchIds.Clear();
            foreach (var id in ids)
            {
                if (!_controls.ContainsKey(id))
                    _scratchIds.Add(id);
            }
            foreach (var id in _scratchIds)
            {
                _written.Remove(id);
                _drives.Remove(id);
                _overrides.Remove(id);
                _spinUntil.Remove(id);
                _errors.Remove(id);
                _handBack.Remove(id);
            }
        }
    }

    private void Index(IHardware hw)
    {
        foreach (var sensor in hw.Sensors)
        {
            string id = sensor.Identifier.ToString();
            if (sensor.SensorType == SensorType.Temperature)
                _temps[id] = sensor;

            if (sensor.SensorType == SensorType.Control && sensor.Control is not null)
            {
                _controls[id] = sensor;
                if (hw.HardwareType == HardwareType.GpuAmd && AmdFor(hw) is { } amd)
                    _amd[id] = amd;
                _rpmForControl[id] = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan && s.Index == sensor.Index);
                _names[id] = ($"{hw.Name} · {sensor.Name}", hw.Name);
            }
        }

        foreach (var fan in hw.Sensors.Where(s => s.SensorType == SensorType.Fan))
        {
            bool hasControl = hw.Sensors.Any(s => s.SensorType == SensorType.Control && s.Control is not null && s.Index == fan.Index);
            if (!hasControl)
            {
                string id = fan.Identifier.ToString();
                _monitorOnly[id] = fan;
                _names[id] = ($"{hw.Name} · {fan.Name}", hw.Name);
            }
        }
        foreach (var sub in hw.SubHardware)
            Index(sub);
    }

    private AmdFanCurve? AmdFor(IHardware hw)
    {
        if (!int.TryParse(hw.Identifier.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(), out int adapter))
            return null;
        if (!_amdAdapters.TryGetValue(adapter, out var curve))
        {
            curve = AmdFanCurve.TryOpen(adapter, out string why);
            _amdAdapters[adapter] = curve;
            AmdNotes[adapter] = curve is null ? "LibreHardwareMonitor's own call: " + why : $"Overdrive 8 fan curve ({curve.MinPercent}–{curve.MaxPercent} %)";
        }
        return curve;
    }

    public string? MotherboardModes() => NuvotonRestore.ManualBits(_controls.Values);

    public string DescribeControl()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (adapter, note) in AmdNotes) sb.AppendLine($"AMD adapter {adapter}: {note}");
        foreach (var curve in _amdAdapters.Values) if (curve is not null) sb.Append(curve.Describe());
        if (sb.Length == 0) sb.AppendLine("no AMD graphics fan controls (NVIDIA / motherboard fans use LibreHardwareMonitor's calls)");
        return sb.ToString();
    }

    public void Dispose()
    {
        foreach (var curve in _amdAdapters.Values) curve?.Dispose();
        _amdAdapters.Clear();
        _amd.Clear();
    }

    public void SetSettings(FanSettings settings)
    {
        var previous = _settings;
        _settings = settings;
        _sortedPoints.Clear();
        foreach (var name in _curveState.Keys.ToList())
        {
            var now = settings.Curves.FirstOrDefault(c => c.Name == name);
            if (now is null)
            {
                int at = previous.Curves.FindIndex(c => c.Name == name);
                if (at >= 0 && settings.Curves.Count == previous.Curves.Count && settings.Curves[at].Name is var renamed
                    && previous.Curves.All(c => c.Name != renamed) && !_curveState.ContainsKey(renamed))
                    _curveState[renamed] = _curveState[name];
                _curveState.Remove(name);
            }
            else if (previous.Curves.FirstOrDefault(c => c.Name == name)?.SensorId != now.SensorId)
                _curveState[name].UsedTemp = null;
        }
    }

    public void SpinUp(string controlId, TimeSpan duration)
    {
        if (_controls.ContainsKey(controlId))
            _spinUntil[controlId] = _clock.ElapsedMilliseconds + (long)duration.TotalMilliseconds;
    }

    public void SetOverride(string controlId, float percent)
    {
        if (_controls.ContainsKey(controlId))
            _overrides[controlId] = Math.Clamp(percent, 0, 100);
    }

    public void ClearOverride(string controlId) => _overrides.Remove(controlId);

    private readonly object _restoreGate = new();
    private bool _restored;

    public void Tick(double dtSeconds, float? cpuTemp, float? gpuTemp)
    {
        lock (_restoreGate)
        {
            if (!_restored) TickCore(dtSeconds, cpuTemp, gpuTemp);
        }
    }

    private void TickCore(double dtSeconds, float? cpuTemp, float? gpuTemp)
    {
        var settings = _settings;

        var results = _results;
        results.Clear();
        var live = new Dictionary<string, CurveLive>();
        foreach (var curve in settings.Curves)
        {
            _visiting.Clear();
            Evaluate(curve, settings, dtSeconds, results, live, _visiting);
        }
        LiveCurves = live;

        UpdateEmergency(settings, cpuTemp, gpuTemp);

        var targets = _targets;
        targets.Clear();
        if (settings.Enabled && settings.Active is { } preset)
        {
            foreach (var fan in preset.Fans)
            {
                if (fan.Mode == FanMode.Auto || !_controls.ContainsKey(fan.ControlId))
                    continue;

                float? target = fan.Mode == FanMode.Manual
                    ? fan.ManualPercent
                    : fan.CurveName is not null && results.TryGetValue(fan.CurveName, out var r) ? r : null;

                var hw = HardwareFor(settings, fan.ControlId) ?? new FanHardware();
                var drive = _drives.TryGetValue(fan.ControlId, out var d) ? d : _drives[fan.ControlId] = new FanDrive();
                float value;
                if (Emergency || target is null)
                {
                    value = 100;
                    (drive.Value, drive.Stopped) = (100, false);
                }
                else
                {
                    value = Drive(target.Value, hw, drive, dtSeconds, _clock.Elapsed.TotalSeconds);
                }
                targets[fan.ControlId] = value;
            }
        }

        foreach (var (id, percent) in _overrides)
            targets[id] = Emergency ? 100 : percent;

        long now = _clock.ElapsedMilliseconds;
        _scratchIds.Clear();
        foreach (var (id, until) in _spinUntil)
        {
            if (now < until)
                targets[id] = 100;
            else
                _scratchIds.Add(id);
        }
        foreach (var id in _scratchIds)
            _spinUntil.Remove(id);

        foreach (var (id, value) in targets)
            Write(id, value);

        _scratchIds.Clear();
        foreach (var id in _written.Keys)
        {
            if (!targets.ContainsKey(id))
                _scratchIds.Add(id);
        }
        foreach (var id in _scratchIds)
            Restore(id);

        _scratchIds.Clear();
        foreach (var id in _handBack.Keys)
            _scratchIds.Add(id);
        foreach (var id in _scratchIds)
        {
            if (targets.ContainsKey(id)) _handBack.Remove(id);
            else RetryHandBack(id);
        }

        _scratchIds.Clear();
        foreach (var id in _drives.Keys)
        {
            if (!targets.ContainsKey(id) || _overrides.ContainsKey(id))
                _scratchIds.Add(id);
        }
        foreach (var id in _scratchIds)
            _drives.Remove(id);
    }

    private static FanHardware? HardwareFor(FanSettings settings, string controlId)
    {
        foreach (var h in settings.Hardware)
        {
            if (h.ControlId == controlId)
                return h;
        }
        return null;
    }

    internal static bool StopTested(FanHardware hw) => !hw.IsPump && hw.Calibration is { StopPercent: not null, StartPercent: not null };

    internal static bool CanStop(FanHardware hw) => hw.AllowStop && StopTested(hw);

    internal static int? StopAt(FanHardware hw) => CanStop(hw) ? Math.Clamp(hw.StopPercent ?? hw.Calibration!.StopPercent!.Value, 0, 100) : null;

    internal static int StartAt(FanHardware hw) => Math.Clamp(hw.StartPercent ?? hw.Calibration?.StartPercent ?? 0, 0, 100);

    internal static float Drive(float target, FanHardware hw, FanDrive state, double dt, double now)
    {
        target = float.IsNaN(target) ? 100 : Math.Clamp(target, 0, 100);
        int min = Math.Clamp(hw.IsPump ? Math.Max(60, hw.MinPercent) : hw.MinPercent, 0, 100);
        int max = Math.Clamp(Math.Max(min, hw.MaxPercent), min, 100);
        int floor = min;
        if (hw.Calibration is { MinStablePercent: > 0 } cal)
            floor = Math.Min(max, Math.Max(floor, cal.MinStablePercent));

        bool stop = StopAt(hw) is { } stopAt && (state.Stopped ? target < stopAt + RestartMargin : target <= stopAt);
        float wanted = stop ? floor : Math.Clamp(target, floor, max);
        float running = wanted;
        if (state.Value is { } prev)
        {
            if (state.Stopped) prev = floor;
            int rate = wanted > prev ? hw.StepUpPerSecond : hw.StepDownPerSecond;
            if (rate > 0)
            {
                float step = (float)(rate * Math.Max(0, dt));
                running = prev + Math.Clamp(wanted - prev, -step, step);
            }
        }

        if (stop && (state.Stopped || running <= floor + 0.01f))
        {
            (state.Value, state.Stopped) = (floor, true);
            return 0;
        }

        bool starting = state.Stopped || state.Value is null;
        (state.Value, state.Stopped) = (running, false);
        int start = StartAt(hw);
        if (starting && start > 0)
            state.StartUntil = now + StartSeconds;
        return now < state.StartUntil ? Math.Min(max, Math.Max(running, start)) : running;
    }

    public void RestoreAll()
    {
        if (!Monitor.TryEnter(_restoreGate, 6000)) return;
        try
        {
            if (!_restored) RestoreCore();
            _restored = true;
        }
        finally { Monitor.Exit(_restoreGate); }
    }

    private void RestoreCore()
    {
        foreach (var id in _written.Keys.ToList())
            Restore(id);
        for (int i = 0; i < 25 && _handBack.Count > 0; i++)
        {
            Thread.Sleep(100);
            foreach (var id in _handBack.Keys.ToList())
                RetryHandBack(id);
        }
    }

    public IReadOnlyList<FanInfo> DescribeFans()
    {
        var list = new List<FanInfo>();
        foreach (var (id, sensor) in _controls)
        {
            var rpm = _rpmForControl.GetValueOrDefault(id);
            var (label, hardware) = _names[id];
            list.Add(new FanInfo(id, label, hardware, sensor.Value, rpm?.Value,
                _written.ContainsKey(id), _errors.GetValueOrDefault(id), Controllable: true, HasRpm: rpm is not null));
        }
        foreach (var (id, sensor) in _monitorOnly)
        {
            var (label, hardware) = _names[id];
            list.Add(new FanInfo(id, label, hardware, null, sensor.Value,
                false, null, Controllable: false, HasRpm: true));
        }
        return list;
    }

    private float? Evaluate(FanCurve curve, FanSettings settings, double dt,
        Dictionary<string, float?> results, Dictionary<string, CurveLive> live, HashSet<string> visiting)
    {
        if (results.TryGetValue(curve.Name, out var done))
            return done;
        if (!visiting.Add(curve.Name))
            return null;

        float? output = null;
        float? temp = null;

        switch (curve.Type)
        {
            case FanCurveType.Flat:
                output = Math.Clamp(curve.FlatPercent, 0, 100);
                break;

            case FanCurveType.Graph:
                temp = curve.SensorId is not null && _temps.TryGetValue(curve.SensorId, out var sensor) ? sensor.Value : null;
                if (temp is { } t && float.IsNaN(t)) temp = null;
                if (temp is not null && curve.Points.Count > 0)
                {
                    var state = _curveState.TryGetValue(curve.Name, out var s) ? s : _curveState[curve.Name] = new CurveState();

                    if (state.UsedTemp is null || Math.Abs(temp.Value - state.UsedTemp.Value) >= Math.Max(0, curve.HysteresisC))
                        state.UsedTemp = temp;

                    if (!_sortedPoints.TryGetValue(curve, out var sorted))
                        _sortedPoints[curve] = sorted = curve.Points.OrderBy(p => p.Temp).ToArray();
                    float target = Interpolate(sorted, state.UsedTemp.Value);
                    if (state.Output is null)
                    {
                        state.Output = target;
                    }
                    else
                    {
                        float rate = target > state.Output ? curve.StepUpPerSecond : curve.StepDownPerSecond;
                        float maxStep = rate <= 0 ? float.MaxValue : (float)(rate * dt);
                        state.Output += Math.Clamp(target - state.Output.Value, -maxStep, maxStep);
                    }
                    output = state.Output;
                }
                break;

            case FanCurveType.Mix:
                var mix = new MixAccumulator(curve.Function);
                bool missing = false;
                foreach (var name in curve.MixOf)
                {
                    FanCurve? part = null;
                    foreach (var c in settings.Curves)
                        if (c.Name == name) { part = c; break; }
                    float? value = part is null ? null : Evaluate(part, settings, dt, results, live, visiting);
                    if (value is null) { missing = true; break; }
                    mix.Add(value.Value);
                }
                output = missing ? null : mix.Result;
                break;
        }

        visiting.Remove(curve.Name);
        results[curve.Name] = output;
        live[curve.Name] = new CurveLive(temp, output);
        return output;
    }

    internal static float Interpolate(List<CurvePoint> points, float temp) =>
        Interpolate(points.OrderBy(p => p.Temp).ToArray(), temp);

    private static float Interpolate(CurvePoint[] sorted, float temp)
    {
        if (temp <= sorted[0].Temp)
            return sorted[0].Percent;
        for (int i = 1; i < sorted.Length; i++)
        {
            var a = sorted[i - 1];
            var b = sorted[i];
            if (temp <= b.Temp)
            {
                float t = b.Temp > a.Temp ? (temp - a.Temp) / (b.Temp - a.Temp) : 1;
                return a.Percent + t * (b.Percent - a.Percent);
            }
        }
        return sorted[^1].Percent;
    }

    private void UpdateEmergency(FanSettings settings, float? cpuTemp, float? gpuTemp)
    {
        float? best = null;
        void Watch(float? t)
        {
            if (t is not { } v) return;
            if (best is not { } b || float.IsNaN(b) || v > b) best = v;
        }
        Watch(cpuTemp);
        Watch(gpuTemp);
        foreach (var curve in settings.Curves)
        {
            if (curve.Type == FanCurveType.Graph && curve.SensorId is not null)
                Watch(_temps.TryGetValue(curve.SensorId, out var s) ? s.Value : null);
        }
        float hottest = best ?? 0;
        float limit = Math.Clamp(settings.EmergencyTempC, 70, 105);

        if (!Emergency && hottest >= limit)
            Emergency = true;
        else if (Emergency && hottest < limit - EmergencyReleaseMarginC)
            Emergency = false;

        if (Emergency && settings.Enabled && !_emergencyReported)
        {
            _emergencyReported = true;
            Problem?.Invoke("Fans at 100%", $"A temperature reached {hottest:0} °C (emergency limit {limit} °C). All controlled fans are at full speed until it drops below {limit - EmergencyReleaseMarginC:0} °C.");
        }
        else if (!Emergency)
        {
            _emergencyReported = false;
        }
    }

    private void Write(string id, float percent)
    {
        if (!_controls.TryGetValue(id, out var sensor) || sensor.Control is not { } control)
            return;
        _amd.TryGetValue(id, out var amd);
        float value = amd is not null ? (percent <= 0 ? 0 : Between(percent, amd.MinPercent, amd.MaxPercent)) : Between(percent, control.MinSoftwareValue, control.MaxSoftwareValue);
        double now = _clock.Elapsed.TotalSeconds;

        bool same = _written.TryGetValue(id, out var last) && Math.Abs(last.Value - value) < 0.5f;
        bool gpu = amd is not null || sensor.Hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
        if (same && !Due(sensor, last!, now, slowDevice: gpu))
            return;

        try
        {
            if (amd is not null)
            {
                if (amd.Set((int)MathF.Round(value)) is { } refused) throw new InvalidOperationException(refused);
            }
            else
            {
                float send = value;
                if (control.ControlMode == ControlMode.Software && control.SoftwareValue == value)
                    send = value < control.MaxSoftwareValue ? MathF.BitIncrement(value) : MathF.BitDecrement(value);
                else if (control.ControlMode != ControlMode.Software)
                    Prime(control, send);
                control.SetSoftware(send);
            }
            var state = same ? last! : new WriteState();
            (state.Value, state.At) = (value, now);
            _written[id] = state;
            if (state.Misses < MissesBeforeError) _errors.Remove(id);
        }
        catch (Exception ex)
        {
            _errors[id] = ex.Message;
            _written.TryAdd(id, new WriteState { Value = float.NaN, At = now });
        }
    }

    internal static float Between(float value, float a, float b) => Math.Clamp(value, Math.Min(a, b), Math.Max(a, b));

    internal static readonly System.Reflection.FieldInfo? SoftwareValueField =
        typeof(IControl).Assembly.GetType("LibreHardwareMonitor.Hardware.Control")?.GetField("_softwareValue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private static void Prime(IControl control, float value)
    {
        try { if (SoftwareValueField?.DeclaringType?.IsInstanceOfType(control) == true) SoftwareValueField.SetValue(control, value); }
        catch { }
    }

    private bool Due(ISensor sensor, WriteState last, double now, bool slowDevice)
    {
        double age = now - last.At;
        if (age >= (slowDevice ? ResendSeconds * 2 : ResendSeconds)) return true;
        if (age < (slowDevice ? VerifySeconds * 3 : VerifySeconds) || sensor.Value is not { } read || float.IsNaN(read)) return false;
        if (last.Value <= 0) return false;
        if (Math.Abs(read - last.Value) <= VerifyTolerance)
        {
            if (last.Misses > 0) { last.Misses = 0; _errors.Remove(sensor.Identifier.ToString()); }
            return false;
        }
        if (++last.Misses >= MissesBeforeError)
            _errors[sensor.Identifier.ToString()] = $"it reads {read:0}% instead of {last.Value:0}%; the driver or another program may be setting it";
        return true;
    }

    private void Restore(string id)
    {
        try
        {
            if (_amd.TryGetValue(id, out var amd))
            {
                if (amd.Restore() is { } refused) { _errors[id] = refused; _handBack.TryAdd(id, 0); }
            }
            else if (_controls.TryGetValue(id, out var sensor))
            {
                HandBack(sensor);
                if (HandBackPending(sensor) == true) _handBack.TryAdd(id, 0);
            }
        }
        catch (Exception ex) { _errors[id] = ex.Message; }
        _written.Remove(id);
    }

    private static void HandBack(ISensor sensor)
    {
        var undo = NuvotonRestore.Prepare(sensor);
        try { sensor.Control?.SetDefault(); }
        finally { undo?.Invoke(); }
    }

    private void RetryHandBack(string id)
    {
        bool pending = false;
        try
        {
            if (_amd.TryGetValue(id, out var amd))
                pending = amd.Restore() is not null;
            else if (_controls.TryGetValue(id, out var sensor) && sensor.Control is { } control && HandBackPending(sensor) == true)
            {
                control.SetSoftware(control.SoftwareValue);
                HandBack(sensor);
                pending = HandBackPending(sensor) == true;
            }
        }
        catch (Exception ex) { _errors[id] = ex.Message; pending = true; }
        if (!pending)
        {
            _handBack.Remove(id);
            _errors.Remove(id);
            return;
        }
        int tries = ++_handBack[id];
        if (tries == HandBackTries)
            _errors[id] = "couldn't hand it back to the BIOS / driver (another monitoring program may hold the chip); still trying";
        else if (tries >= HandBackGiveUp)
        {
            _handBack.Remove(id);
            _errors[id] = "couldn't hand it back to the BIOS / driver; switch fan control off and on, or restart, to try again";
        }
    }

    internal static bool? HandBackPending(ISensor sensor)
    {
        try
        {
            var superIO = sensor.Hardware.GetType().GetField("_superIO", HiddenField)?.GetValue(sensor.Hardware);
            return superIO is not null && PendingField(superIO.GetType())?.GetValue(superIO) is bool[] pending
                   && sensor.Index >= 0 && sensor.Index < pending.Length ? pending[sensor.Index] : null;
        }
        catch { return null; }
    }

    private const System.Reflection.BindingFlags HiddenField = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

    internal static System.Reflection.FieldInfo? PendingField(Type superIO) =>
        superIO.GetFields(HiddenField).FirstOrDefault(f => f.FieldType == typeof(bool[]) && f.Name.StartsWith("_restoreDefaultFan"));
}

internal static class NuvotonRestore
{
    internal static readonly Version LastBroken = new(0, 9, 6);
    private static readonly System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

    public static Action? Prepare(ISensor sensor)
    {
        try
        {
            var lib = typeof(IComputer).Assembly.GetName().Version;
            if (lib is null || new Version(lib.Major, lib.Minor, Math.Max(0, lib.Build)) > LastBroken) return null;
            var hardware = sensor.Hardware;
            var superIO = hardware.GetType().GetField("_superIO", Private)?.GetValue(hardware);
            if (superIO is null || superIO.GetType().Name != "Nct677X") return null;
            string chip = superIO.GetType().GetProperty("Chip")?.GetValue(superIO)?.ToString() ?? "";
            if (chip is not ("NCT6683D" or "NCT6686D" or "NCT6687D")) return null;
            if (superIO.GetType().GetField("_initialFanControlMode", Private)?.GetValue(superIO) is not byte[] recorded
                || superIO.GetType().GetField("_restoreDefaultFanControlRequired", Private)?.GetValue(superIO) is not bool[] required) return null;
            int i = sensor.Index;
            if (i < 0 || i >= recorded.Length || i >= required.Length || !required[i] || i > 7) return null;
            byte bit = (byte)(1 << i);
            recorded[i] ^= bit;
            return () => recorded[i] ^= bit;
        }
        catch { return null; }
    }

    public static string? ManualBits(IEnumerable<ISensor> controls)
    {
        foreach (var sensor in controls)
        {
            try
            {
                var superIO = sensor.Hardware.GetType().GetField("_superIO", Private)?.GetValue(sensor.Hardware);
                if (superIO?.GetType().Name != "Nct677X") continue;
                var regs = superIO.GetType().GetField("FAN_CONTROL_MODE_REG", Private)?.GetValue(superIO) as ushort[];
                var read = superIO.GetType().GetMethod("ReadByte", Private, [typeof(ushort)]);
                if (regs is not { Length: > 0 } || read is null) continue;
                using var bus = System.Threading.Mutex.OpenExisting(@"Global\Access_ISABUS.HTP.Method");
                if (!bus.WaitOne(200)) return "chip lock busy";
                try { return "manual bits " + Convert.ToString((byte)read.Invoke(superIO, [regs[0]])!, 2).PadLeft(8, '0') + " (bit 0 = CPU Fan, 1 = Pump, 2… = System Fan #1…)"; }
                finally { bus.ReleaseMutex(); }
            }
            catch (Exception ex) { return "couldn't read: " + ex.Message; }
        }
        return null;
    }
}

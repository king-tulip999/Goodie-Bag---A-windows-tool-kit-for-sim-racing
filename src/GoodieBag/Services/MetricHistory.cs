namespace GoodieBag.Services;

internal sealed class MetricHistory
{
    public const int Capacity = 120;
    private readonly Dictionary<string, List<double?>> _series = new();

    public static readonly string[] Keys = { "cpuTemp", "cpuLoad", "gpuTemp", "gpuLoad", "ram", "vram", "fps", "cpuClock", "gpuClock", "power", "gpuFan", "diskBusy" };

    public MetricHistory()
    {
        foreach (var key in Keys)
            _series[key] = new List<double?>();
    }

    public IReadOnlyList<double?> this[string key] => _series[key];

    private readonly double?[] _reading = new double?[Keys.Length];

    public void Sample(AppServices app)
    {
        Read(app, _reading);
        for (int i = 0; i < Keys.Length; i++)
            Add(Keys[i], _reading[i]);
    }

    public static void Read(AppServices app, double?[] into)
    {
        var hw = app.Hardware.Latest;
        into[0] = hw?.CpuTemp;
        into[1] = hw?.CpuLoad;
        into[2] = hw?.GpuTemp;
        into[3] = hw?.GpuLoad;
        into[4] = hw?.RamUsedGb is { } u && hw.RamTotalGb is { } t && t > 0 ? u / t * 100 : null;
        into[5] = hw?.VramUsedMb is { } vu && hw.VramTotalMb is { } vt && vt > 0 ? vu / vt * 100 : null;
        into[6] = app.Frames.GetMostActive()?.Stats.Fps;
        into[7] = hw?.CpuClockMhz / 1000;
        into[8] = hw?.GpuClockMhz;
        into[9] = hw?.CpuPower is null && hw?.GpuPower is null ? null : (hw?.CpuPower ?? 0) + (hw?.GpuPower ?? 0);
        into[10] = hw?.GpuFanRpm;
        into[11] = hw?.DiskActivePercent;
    }

    public void Clear()
    {
        foreach (var list in _series.Values)
            list.Clear();
    }

    private void Add(string key, double? value)
    {
        var list = _series[key];
        list.Add(value);
        if (list.Count > Capacity)
            list.RemoveAt(0);
    }
}

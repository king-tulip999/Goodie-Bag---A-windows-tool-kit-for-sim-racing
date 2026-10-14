using GoodieBag.Config;
using GoodieBag.Fans;

namespace GoodieBag.Hardware;

internal sealed class DemoHardware
{
    private sealed record DemoFan(string Id, string Name, string Hardware, int MaxRpm, int StallPercent, bool Controllable = true);

    private static readonly DemoFan[] Fans =
    {
        new("/lpc/nct6798d/control/1", "Fan Control #2", "Nuvoton NCT6798D", 1650, 18),
        new("/lpc/nct6798d/control/0", "Fan Control #1", "Nuvoton NCT6798D", 2900, 0),
        new("/gpu-amd/0/control/0", "GPU Fan", "AMD Radeon RX 7900 XTX", 3200, 0),
        new("/lpc/nct6798d/control/2", "Fan Control #3", "Nuvoton NCT6798D", 1500, 16),
        new("/lpc/nct6798d/control/3", "Fan Control #4", "Nuvoton NCT6798D", 1400, 15),
        new("/lpc/nct6798d/control/4", "Fan Control #5", "Nuvoton NCT6798D", 1200, 20),
        new("/lpc/nct6798d/control/6", "Fan Control #7", "Nuvoton NCT6798D", 0, 0),
        new("/lpc/nct6798d/fan/5", "Fan #6", "Nuvoton NCT6798D", 900, 0, Controllable: false),
    };

    private readonly Dictionary<string, float> _overrides = new();
    private readonly Dictionary<string, long> _spin = new();
    private readonly Random _random = new(42);
    private double _t;

    public void SetOverride(string id, float percent) { lock (_overrides) _overrides[id] = percent; }
    public void ClearOverride(string id) { lock (_overrides) _overrides.Remove(id); }
    public void SpinUp(string id) { lock (_overrides) _spin[id] = Environment.TickCount64 + 8000; }

    public (HardwareSnapshot, HardwareCatalog) Step(FanSettings settings)
    {
        _t += 1;
        double Wave(double period, double phase = 0) => Math.Sin(_t / period * Math.PI * 2 + phase);

        float cpuTemp = (float)(58 + 7 * Wave(37) + 2 * Wave(9, 1));
        float gpuTemp = (float)(64 + 6 * Wave(44, 2) + 1.5 * Wave(7));
        var snapshot = new HardwareSnapshot
        {
            CpuTemp = cpuTemp,
            CpuLoad = (float)(34 + 14 * Wave(23) + 6 * Wave(5, 1)),
            CpuClockMhz = (float)(4870 + 60 * Wave(11)),
            CpuPower = (float)(88 + 14 * Wave(23)),
            GpuTemp = gpuTemp,
            GpuHotSpot = gpuTemp + 14,
            GpuLoad = (float)(91 + 6 * Wave(13)),
            GpuClockMhz = (float)(2480 + 40 * Wave(17)),
            GpuPower = (float)(318 + 22 * Wave(13)),
            VramUsedMb = 11_900,
            VramTotalMb = 24_560,
            GpuFanRpm = (float)(1650 + 120 * Wave(44, 2)),
            GpuFanPercent = (float)(48 + 4 * Wave(44, 2)),
            DiskReadBps = (float)Math.Max(0, 2.5e6 + 2e6 * Wave(7)),
            DiskWriteBps = (float)Math.Max(0, 6e5 + 5e5 * Wave(11, 1)),
            DiskActivePercent = (float)(6 + 4 * Wave(7)),
            RamUsedGb = (float)(18.4 + 0.4 * Wave(60)),
            RamTotalGb = 31.9f,
        };

        var temps = new List<TempSensorInfo>
        {
            new("/amdcpu/0/temperature/2", "Core (Tctl/Tdie)", "AMD Ryzen 7 7800X3D", cpuTemp),
            new("/amdcpu/0/temperature/3", "CCD1 (Tdie)", "AMD Ryzen 7 7800X3D", cpuTemp - 3),
            new("/gpu-amd/0/temperature/0", "GPU Core", "AMD Radeon RX 7900 XTX", gpuTemp),
            new("/gpu-amd/0/temperature/1", "GPU Hot Spot", "AMD Radeon RX 7900 XTX", gpuTemp + 14),
            new("/gpu-amd/0/temperature/2", "GPU Memory", "AMD Radeon RX 7900 XTX", gpuTemp + 8),
            new("/lpc/nct6798d/temperature/0", "System", "Nuvoton NCT6798D", 36),
            new("/lpc/nct6798d/temperature/1", "CPU Socket", "Nuvoton NCT6798D", 44),
        };

        var curveOutputs = new Dictionary<string, CurveLive>();
        CurveLive Live(FanCurve curve, int depth)
        {
            if (curveOutputs.TryGetValue(curve.Name, out var done)) return done;
            float? temp = curve.Type == FanCurveType.Graph ? temps.FirstOrDefault(t => t.Id == curve.SensorId)?.Value : null;
            float? output = curve.Type switch
            {
                FanCurveType.Flat => curve.FlatPercent,
                FanCurveType.Graph when temp is not null && curve.Points.Count > 0 => FanController.Interpolate(curve.Points, temp.Value),
                FanCurveType.Mix when depth < 8 => MixOf(curve, depth),
                _ => null,
            };
            return curveOutputs[curve.Name] = new CurveLive(temp, output);
        }
        float? MixOf(FanCurve mix, int depth)
        {
            var acc = new FanController.MixAccumulator(mix.Function);
            foreach (var name in mix.MixOf)
            {
                if (settings.Curves.FirstOrDefault(c => c.Name == name) is not { } part || Live(part, depth + 1).Output is not { } v) return null;
                acc.Add(v);
            }
            return acc.Result;
        }
        foreach (var curve in settings.Curves)
            Live(curve, 0);

        var fans = new List<FanInfo>();
        lock (_overrides)
        {
            foreach (var fan in Fans)
            {
                if (!fan.Controllable)
                {
                    fans.Add(new FanInfo(fan.Id, $"{fan.Hardware} · {fan.Name}", fan.Hardware, null, (float)(fan.MaxRpm + 15 * Wave(3)), false, null, Controllable: false));
                    continue;
                }

                float percent = 40;
                var entry = settings.Active?.Fans.FirstOrDefault(f => f.ControlId == fan.Id);
                if (settings.Enabled && entry is not null)
                {
                    percent = entry.Mode switch
                    {
                        FanMode.Manual => entry.ManualPercent,
                        FanMode.Curve => entry.CurveName is not null && curveOutputs.TryGetValue(entry.CurveName, out var live) && live.Output is { } o ? o : 100,
                        _ => 40,
                    };
                }
                if (_overrides.TryGetValue(fan.Id, out var ov)) percent = ov;
                if (_spin.TryGetValue(fan.Id, out var until) && Environment.TickCount64 < until) percent = 100;

                float? rpm = fan.MaxRpm == 0 ? 0
                    : percent < fan.StallPercent ? 0
                    : (float)(fan.MaxRpm * (0.22 + 0.78 * percent / 100) + _random.Next(-12, 12));
                bool controlled = settings.Enabled && entry is { Mode: not FanMode.Auto } || _overrides.ContainsKey(fan.Id);
                fans.Add(new FanInfo(fan.Id, $"{fan.Hardware} · {fan.Name}", fan.Hardware, percent, rpm, controlled, null));
            }
        }

        return (snapshot, new HardwareCatalog(temps, fans, curveOutputs, false));
    }

    private readonly Dictionary<string, (float Min, float Max)> _range = new();

    public void ResetMinMax()
    {
        lock (_range)
            _range.Clear();
    }

    public SensorTree Tree(HardwareSnapshot hw, HardwareCatalog catalog)
    {
        double Wave(double period, double phase = 0) => Math.Sin(_t / period * Math.PI * 2 + phase);
        SensorReading R(string id, string name, SensorKind kind, double value)
        {
            float v = (float)value;
            lock (_range)
            {
                var (min, max) = _range.TryGetValue(id, out var r) ? r : (v, v);
                _range[id] = (Math.Min(min, v), Math.Max(max, v));
                return new SensorReading(id, name, kind, v, _range[id].Min, _range[id].Max);
            }
        }

        var cpu = new List<SensorReading>();
        for (int i = 0; i < 8; i++)
            cpu.Add(R($"/amdcpu/0/load/{i + 1}", $"CPU Core #{i + 1}", SensorKind.Load, Math.Clamp((hw.CpuLoad ?? 30) + 18 * Wave(5 + i, i), 1, 100)));
        cpu.Add(R("/amdcpu/0/load/0", "CPU Total", SensorKind.Load, hw.CpuLoad ?? 0));
        for (int i = 0; i < 8; i++)
            cpu.Add(R($"/amdcpu/0/clock/{i + 1}", $"Core #{i + 1}", SensorKind.Clock, 4850 + 80 * Wave(7 + i, i)));
        cpu.Add(R("/amdcpu/0/clock/0", "Bus Speed", SensorKind.Clock, 100));
        cpu.Add(R("/amdcpu/0/temperature/2", "Core (Tctl/Tdie)", SensorKind.Temperature, hw.CpuTemp ?? 0));
        cpu.Add(R("/amdcpu/0/temperature/3", "CCD1 (Tdie)", SensorKind.Temperature, (hw.CpuTemp ?? 0) - 3));
        cpu.Add(R("/amdcpu/0/power/0", "Package", SensorKind.Power, hw.CpuPower ?? 0));
        for (int i = 0; i < 8; i++)
            cpu.Add(R($"/amdcpu/0/power/{i + 1}", $"Core #{i + 1} (SMU)", SensorKind.Power, (hw.CpuPower ?? 80) / 11 + 0.8 * Wave(6 + i, i)));
        cpu.Add(R("/amdcpu/0/voltage/0", "Core (SVI2 TFN)", SensorKind.Voltage, 1.18 + 0.03 * Wave(9)));
        cpu.Add(R("/amdcpu/0/voltage/1", "SoC (SVI2 TFN)", SensorKind.Voltage, 1.25));

        var gpu = new List<SensorReading>
        {
            R("/gpu-amd/0/load/0", "GPU Core", SensorKind.Load, hw.GpuLoad ?? 0),
            R("/gpu-amd/0/load/1", "GPU Memory Controller", SensorKind.Load, 48 + 9 * Wave(11)),
            R("/gpu-amd/0/temperature/0", "GPU Core", SensorKind.Temperature, hw.GpuTemp ?? 0),
            R("/gpu-amd/0/temperature/1", "GPU Hot Spot", SensorKind.Temperature, hw.GpuHotSpot ?? 0),
            R("/gpu-amd/0/temperature/2", "GPU Memory", SensorKind.Temperature, (hw.GpuTemp ?? 0) + 8),
            R("/gpu-amd/0/clock/0", "GPU Core", SensorKind.Clock, hw.GpuClockMhz ?? 0),
            R("/gpu-amd/0/clock/1", "GPU Memory", SensorKind.Clock, 2487),
            R("/gpu-amd/0/power/0", "GPU Package", SensorKind.Power, hw.GpuPower ?? 0),
            R("/gpu-amd/0/voltage/0", "GPU Core", SensorKind.Voltage, 1.05 + 0.02 * Wave(13)),
            R("/gpu-amd/0/smalldata/0", "GPU Memory Used", SensorKind.SmallData, hw.VramUsedMb ?? 0),
            R("/gpu-amd/0/smalldata/1", "GPU Memory Total", SensorKind.SmallData, hw.VramTotalMb ?? 0),
        };
        if (catalog.Fans.FirstOrDefault(f => f.ControlId.StartsWith("/gpu", StringComparison.Ordinal)) is { } gpuFan)
        {
            gpu.Add(R("/gpu-amd/0/fan/0", "GPU Fan", SensorKind.Fan, gpuFan.Rpm ?? 0));
            gpu.Add(R("/gpu-amd/0/control/0", "GPU Fan", SensorKind.Control, gpuFan.Percent ?? 0));
        }

        double used = hw.RamUsedGb ?? 0, total = hw.RamTotalGb ?? 32;
        var memory = new List<SensorReading>
        {
            R("/ram/load/0", "Memory", SensorKind.Load, used / total * 100),
            R("/ram/data/0", "Memory Used", SensorKind.Data, used),
            R("/ram/data/1", "Memory Available", SensorKind.Data, total - used),
            R("/vram/load/0", "Virtual Memory", SensorKind.Load, 41 + 2 * Wave(40)),
        };

        var superIo = new List<SensorReading>
        {
            R("/lpc/nct6798d/temperature/0", "System", SensorKind.Temperature, 36 + Wave(50)),
            R("/lpc/nct6798d/temperature/1", "CPU Socket", SensorKind.Temperature, 44 + Wave(30)),
            R("/lpc/nct6798d/temperature/2", "PCH", SensorKind.Temperature, 52 + Wave(70)),
            R("/lpc/nct6798d/temperature/3", "VRM MOS", SensorKind.Temperature, 49 + 2 * Wave(25)),
            R("/lpc/nct6798d/voltage/0", "Vcore", SensorKind.Voltage, 1.18 + 0.03 * Wave(9)),
            R("/lpc/nct6798d/voltage/1", "+5V", SensorKind.Voltage, 5.04),
            R("/lpc/nct6798d/voltage/2", "+12V", SensorKind.Voltage, 12.10),
            R("/lpc/nct6798d/voltage/3", "+3.3V", SensorKind.Voltage, 3.34),
            R("/lpc/nct6798d/voltage/4", "DRAM", SensorKind.Voltage, 1.35),
        };
        foreach (var fan in catalog.Fans.Where(f => f.ControlId.StartsWith("/lpc", StringComparison.Ordinal)))
        {
            string n = fan.DefaultName[(fan.DefaultName.IndexOf(" · ", StringComparison.Ordinal) + 3)..];
            superIo.Add(R(fan.ControlId.Replace("control", "fan"), n.Replace("Fan Control", "Fan"), SensorKind.Fan, fan.Rpm ?? 0));
            if (fan.Controllable)
                superIo.Add(R(fan.ControlId, n, SensorKind.Control, fan.Percent ?? 0));
        }

        DeviceNode Drive(int i, string name, double temp, double usedPct, double read, double write) => new($"/nvme/{i}", name, DeviceKind.Storage, new[]
        {
            R($"/nvme/{i}/temperature/0", "Composite Temperature", SensorKind.Temperature, temp),
            R($"/nvme/{i}/load/0", "Used Space", SensorKind.Load, usedPct),
            R($"/nvme/{i}/throughput/0", "Read Rate", SensorKind.Throughput, read),
            R($"/nvme/{i}/throughput/1", "Write Rate", SensorKind.Throughput, write),
            R($"/nvme/{i}/data/0", "Data Read", SensorKind.Data, 41_230 + i * 900),
            R($"/nvme/{i}/data/1", "Data Written", SensorKind.Data, 28_810 + i * 500),
            R($"/nvme/{i}/level/0", "Available Spare", SensorKind.Level, 100),
            R($"/nvme/{i}/level/1", "Percentage Used", SensorKind.Level, 3 + i),
        }, Array.Empty<DeviceNode>(), "Demo");

        double Mb(double x) => x * 1_048_576;
        DeviceNode Nic(int n, string name) => new($"/nic/{n}", name, DeviceKind.Network, new[]
        {
            R($"/nic/{n}/throughput/7", "Upload Speed", SensorKind.Throughput, 42_000 + 30_000 * Math.Abs(Wave(8))),
            R($"/nic/{n}/throughput/8", "Download Speed", SensorKind.Throughput, 2_400_000 * Math.Abs(Wave(12))),
            R($"/nic/{n}/data/2", "Data Uploaded", SensorKind.Data, 3.2),
            R($"/nic/{n}/data/3", "Data Downloaded", SensorKind.Data, 48.6),
            R($"/nic/{n}/load/1", "Network Utilization", SensorKind.Load, 1.9 * Math.Abs(Wave(12))),
        }, Array.Empty<DeviceNode>(), "Demo");

        var devices = new List<DeviceNode>
        {
            new("/amdcpu/0", "AMD Ryzen 7 7800X3D", DeviceKind.Cpu, cpu, Array.Empty<DeviceNode>(), "Demo"),
            new("/gpu-amd/0", "AMD Radeon RX 7900 XTX", DeviceKind.Gpu, gpu, Array.Empty<DeviceNode>(), "Demo"),
            new("/ram", "Generic Memory", DeviceKind.Memory, memory, Array.Empty<DeviceNode>(), "Demo"),
            new("/motherboard", "ASUS ROG STRIX X670E-F GAMING WIFI", DeviceKind.Motherboard, Array.Empty<SensorReading>(),
                new[] { new DeviceNode("/lpc/nct6798d", "Nuvoton NCT6798D", DeviceKind.SuperIO, superIo, Array.Empty<DeviceNode>(), "Demo") }, "Demo"),
            Drive(0, "Samsung SSD 990 PRO 2TB", 44 + Wave(40), 61, Mb(Math.Max(0, 85 * Wave(9))), Mb(Math.Max(0, 12 * Wave(13)))),
            Drive(1, "WD_BLACK SN850X 4TB", 39 + Wave(55), 48, Mb(Math.Max(0, 20 * Wave(17))), 0),
            Nic(0, "Ethernet"),
            Nic(1, "vEthernet (Default Switch)"),
            Nic(2, "vEthernet (WSL)"),
            new("/wmi/dimms", "Memory modules", DeviceKind.Memory, Array.Empty<SensorReading>(), new[]
            {
                new DeviceNode("/wmi/dimm/0", "DIMM_A2 G Skill F5-6000J3038F16G", DeviceKind.Memory, new[]
                {
                    new SensorReading("/wmi/dimm/0/capacity", "Capacity", SensorKind.Data, 16),
                    new SensorReading("/wmi/dimm/0/speed", "Configured speed", SensorKind.Other, 6000, Unit: "MT/s"),
                }, Array.Empty<DeviceNode>(), "Demo"),
                new DeviceNode("/wmi/dimm/1", "DIMM_B2 G Skill F5-6000J3038F16G", DeviceKind.Memory, new[]
                {
                    new SensorReading("/wmi/dimm/1/capacity", "Capacity", SensorKind.Data, 16),
                    new SensorReading("/wmi/dimm/1/speed", "Configured speed", SensorKind.Other, 6000, Unit: "MT/s"),
                }, Array.Empty<DeviceNode>(), "Demo"),
            }, "Demo"),
            new("/volumes", "Volumes", DeviceKind.Storage, Array.Empty<SensorReading>(), new[]
            {
                new DeviceNode("/volume/c", "C: Windows (NTFS)", DeviceKind.Storage, new[]
                {
                    new SensorReading("/volume/c/capacity", "Capacity", SensorKind.Data, 1862),
                    new SensorReading("/volume/c/free", "Free", SensorKind.Data, 726),
                    new SensorReading("/volume/c/usedpct", "Used space", SensorKind.Load, 61),
                }, Array.Empty<DeviceNode>(), "Demo"),
                new DeviceNode("/volume/d", "D: Games (NTFS)", DeviceKind.Storage, new[]
                {
                    new SensorReading("/volume/d/capacity", "Capacity", SensorKind.Data, 3726),
                    new SensorReading("/volume/d/free", "Free", SensorKind.Data, 1937),
                    new SensorReading("/volume/d/usedpct", "Used space", SensorKind.Load, 48),
                }, Array.Empty<DeviceNode>(), "Demo"),
            }, "Demo"),
        };
        return new SensorTree(devices);
    }
}

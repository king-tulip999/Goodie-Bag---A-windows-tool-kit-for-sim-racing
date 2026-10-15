namespace GoodieBag.Hardware;

internal enum SensorKind
{
    Temperature, Load, Clock, Power, Voltage, Current, Fan, Control, Data, SmallData, Throughput,
    Frequency, Level, Factor, Energy, TimeSpan, Flow, Noise, Other,
}

internal enum DeviceKind { Cpu, Gpu, Memory, Motherboard, SuperIO, Storage, Network, Controller, Battery, Psu, Other }

internal sealed record SensorReading(string Id, string Name, SensorKind Kind, float? Value, float? Min = null, float? Max = null, string? Unit = null)
{
    public string Format(float? value) => SensorFormat.Format(Kind, value, Unit);
}

internal sealed record DeviceNode(string Id, string Name, DeviceKind Kind, IReadOnlyList<SensorReading> Sensors, IReadOnlyList<DeviceNode> Children, string Source)
{
    public IEnumerable<SensorReading> AllSensors => Sensors.Concat(Children.SelectMany(c => c.AllSensors));

    public void CollectSensors(List<SensorReading> into)
    {
        for (int i = 0; i < Sensors.Count; i++)
            into.Add(Sensors[i]);
        for (int i = 0; i < Children.Count; i++)
            Children[i].CollectSensors(into);
    }
}

internal sealed record SensorTree(IReadOnlyList<DeviceNode> Devices)
{
    public IEnumerable<DeviceNode> OfKind(DeviceKind kind) => Devices.Where(d => d.Kind == kind);

    public DeviceNode? FirstOfKind(DeviceKind kind)
    {
        for (int i = 0; i < Devices.Count; i++)
        {
            if (Devices[i].Kind == kind)
                return Devices[i];
        }
        return null;
    }
}

internal interface ISensorSource
{
    string Name { get; }

    TimeSpan Interval { get; }

    IReadOnlyList<DeviceNode> Read();
}

internal static class SensorLookup
{
    public static SensorReading? Find(DeviceNode device, SensorKind kind, params string[] names)
    {
        foreach (var name in names)
            if (FindNamed(device, kind, name) is { } s)
                return s;
        return null;
    }

    private static SensorReading? FindNamed(DeviceNode device, SensorKind kind, string name)
    {
        var sensors = device.Sensors;
        for (int i = 0; i < sensors.Count; i++)
        {
            var s = sensors[i];
            if (s.Kind == kind && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                return s;
        }
        var children = device.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (FindNamed(children[i], kind, name) is { } s)
                return s;
        }
        return null;
    }

    public static SensorReading? FindAny(SensorTree tree, DeviceKind deviceKind, SensorKind kind, params string[] names)
    {
        foreach (var device in tree.Devices.SelectMany(Flatten).Where(d => d.Kind == deviceKind))
            if (Find(device, kind, names) is { } s)
                return s;
        return null;
    }

    public static IEnumerable<DeviceNode> Flatten(DeviceNode d) => new[] { d }.Concat(d.Children.SelectMany(Flatten));
}

internal static class SensorFormat
{
    public static string Format(SensorKind kind, float? value, string? unit = null)
    {
        if (value is not { } v || float.IsNaN(v))
            return GoodieBag.SimRacing.Race.RaceFormat.Missing;
        if (unit is not null)
            return $"{v:0.##} {unit}";
        return kind switch
        {
            SensorKind.Temperature => $"{v:0.0} °C",
            SensorKind.Load or SensorKind.Control or SensorKind.Level => $"{v:0.0} %",
            SensorKind.Clock => v >= 1000 ? $"{v / 1000:0.00} GHz" : $"{v:0} MHz",
            SensorKind.Power => v < 10 ? $"{v:0.00} W" : $"{v:0.0} W",
            SensorKind.Voltage => $"{v:0.000} V",
            SensorKind.Current => $"{v:0.00} A",
            SensorKind.Fan => $"{v:N0} RPM",
            SensorKind.Data => v >= 1024 ? $"{v / 1024:0.00} TB" : $"{v:0.0} GB",
            SensorKind.SmallData => v >= 1024 ? $"{v / 1024:0.00} GB" : $"{v:0} MB",
            SensorKind.Throughput => Rate(v),
            SensorKind.Frequency => $"{v:0} Hz",
            SensorKind.Factor => $"{v:0.000}",
            SensorKind.TimeSpan => System.TimeSpan.FromSeconds(v).ToString(@"d\.hh\:mm\:ss"),
            SensorKind.Energy => $"{v:0} mWh",
            SensorKind.Flow => $"{v:0.0} L/h",
            SensorKind.Noise => $"{v:0} dBA",
            _ => $"{v:0.##}",
        };
    }

    public static string Rate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1_073_741_824 => $"{bytesPerSecond / 1_073_741_824:0.00} GB/s",
        >= 1_048_576 => $"{bytesPerSecond / 1_048_576:0.0} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:0} KB/s",
        _ => $"{bytesPerSecond:0} B/s",
    };

    public static string GroupName(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => "Temperatures",
        SensorKind.Load => "Utilization",
        SensorKind.Clock => "Clocks",
        SensorKind.Power => "Power",
        SensorKind.Voltage => "Voltages",
        SensorKind.Current => "Currents",
        SensorKind.Fan => "Fans",
        SensorKind.Control => "Fan control",
        SensorKind.Data or SensorKind.SmallData => "Data",
        SensorKind.Throughput => "Throughput",
        SensorKind.Frequency => "Frequencies",
        SensorKind.Level => "Levels",
        SensorKind.Factor => "Factors",
        SensorKind.Energy => "Energy",
        SensorKind.TimeSpan => "Time",
        SensorKind.Flow => "Flow",
        SensorKind.Noise => "Noise",
        _ => "Other",
    };
}

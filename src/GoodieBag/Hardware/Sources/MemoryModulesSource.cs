using System.Management;

namespace GoodieBag.Hardware.Sources;

internal sealed class MemoryModulesSource : ISensorSource
{
    private IReadOnlyList<DeviceNode>? _cached;

    public string Name => "Windows (WMI)";
    public TimeSpan Interval => TimeSpan.FromMinutes(30);

    public IReadOnlyList<DeviceNode> Read()
    {
        if (_cached is not null)
            return _cached;

        var modules = new List<DeviceNode>();
        bool ok = false;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT BankLabel, DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
            using var results = searcher.Get();
            int n = 0;
            foreach (var mo in results.Cast<ManagementObject>())
            {
                using (mo)
                {
                    string slot = (mo["DeviceLocator"] as string)?.Trim() is { Length: > 0 } s ? s : $"Slot {n + 1}";
                    string part = (mo["PartNumber"] as string)?.Trim() ?? "";
                    string maker = (mo["Manufacturer"] as string)?.Trim() ?? "";
                    float? capacityGb = mo["Capacity"] is ulong cap ? cap / (1024f * 1024 * 1024) : null;
                    float? configured = mo["ConfiguredClockSpeed"] is uint c && c > 0 ? c : null;
                    float? rated = mo["Speed"] is uint r && r > 0 ? r : null;
                    string id = $"/wmi/dimm/{n++}";
                    string name = string.Join(" ", new[] { slot, maker, part }.Where(x => x.Length > 0 && !x.Equals("Unknown", StringComparison.OrdinalIgnoreCase)));
                    modules.Add(new DeviceNode(id, name, DeviceKind.Memory, new[]
                    {
                        new SensorReading(id + "/capacity", "Capacity", SensorKind.Data, capacityGb),
                        new SensorReading(id + "/speed", "Configured speed", SensorKind.Other, configured, Unit: "MT/s"),
                        new SensorReading(id + "/rated", "Rated speed", SensorKind.Other, rated, Unit: "MT/s"),
                    }, Array.Empty<DeviceNode>(), Name));
                }
            }
            ok = true;
        }
        catch
        {
        }

        IReadOnlyList<DeviceNode> result = modules.Count == 0
            ? Array.Empty<DeviceNode>()
            : new[] { new DeviceNode("/wmi/dimms", "Memory modules", DeviceKind.Memory, Array.Empty<SensorReading>(), modules, Name) };
        if (ok)
            _cached = result;
        return result;
    }
}

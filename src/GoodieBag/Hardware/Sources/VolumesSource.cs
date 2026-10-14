using System.IO;

namespace GoodieBag.Hardware.Sources;

internal sealed class VolumesSource : ISensorSource
{
    public string Name => "Windows volumes";
    public TimeSpan Interval => TimeSpan.FromSeconds(10);

    public IReadOnlyList<DeviceNode> Read()
    {
        var volumes = new List<DeviceNode>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                    continue;
                const float gb = 1024f * 1024 * 1024;
                float total = drive.TotalSize / gb, free = drive.AvailableFreeSpace / gb;
                string id = "/volume/" + drive.Name.TrimEnd('\\', ':').ToLowerInvariant();
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local disk" : drive.VolumeLabel;
                volumes.Add(new DeviceNode(id, $"{drive.Name.TrimEnd('\\')} {label} ({drive.DriveFormat})", DeviceKind.Storage, new[]
                {
                    new SensorReading(id + "/capacity", "Capacity", SensorKind.Data, total),
                    new SensorReading(id + "/used", "Used", SensorKind.Data, total - free),
                    new SensorReading(id + "/free", "Free", SensorKind.Data, free),
                    new SensorReading(id + "/usedpct", "Used space", SensorKind.Load, total > 0 ? (total - free) / total * 100 : null),
                }, Array.Empty<DeviceNode>(), Name));
            }
            catch
            {
            }
        }
        return volumes.Count == 0
            ? volumes
            : new[] { new DeviceNode("/volumes", "Volumes", DeviceKind.Storage, Array.Empty<SensorReading>(), volumes, Name) };
    }
}

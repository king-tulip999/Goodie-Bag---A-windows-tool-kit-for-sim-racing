using System.IO;
using GoodieBag.SimRacing.IracingFiles;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SessionStats;

internal sealed record TelemetryReport(string Folder, IReadOnlyList<FileInfo> Files, IReadOnlyList<FileInfo> Clearable, double? BytesPerHour, bool? Recording)
{
    public long TotalBytes => Files.Sum(f => f.Length);

    public long ClearableBytes => Clearable.Sum(f => f.Length);
}

internal static class TelemetryFiles
{
    public const string Section = "Misc", AutoLogKey = "irsdkAutoLogDisk";
    private const int RateSample = 20;

    public static TelemetryReport? Read(Func<string, bool> imported)
    {
        if (IracingPaths.FindDocumentsFolder() is not { } documents) return null;
        var folder = TelemetryFolder.Scan(documents);
        return new TelemetryReport(folder.Folder, folder.Files, Clearable(folder.Files, imported), BytesPerHour(folder.Files.Take(RateSample)), Recording(documents));
    }

    internal static List<FileInfo> Clearable(IReadOnlyList<FileInfo> newestFirst, Func<string, bool> imported) =>
        newestFirst.Skip(1).Where(f => imported(f.Name)).ToList();

    internal static double? Rate(IEnumerable<(long Bytes, double Seconds)> files)
    {
        double bytes = 0, seconds = 0;
        foreach (var (b, s) in files)
        {
            if (s < 60) continue;
            bytes += b;
            seconds += s;
        }
        return seconds > 0 ? bytes / seconds * 3600 : null;
    }

    private static double? BytesPerHour(IEnumerable<FileInfo> files) => Rate(files.Select(f =>
    {
        try
        {
            using var ibt = IbtFile.Open(f.FullName, out _);
            return ibt is { TickRate: > 0 } ? (f.Length, (double)ibt.RecordCount / ibt.TickRate) : (0L, 0.0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return (0L, 0.0); }
    }).ToList());

    internal static bool? Recording(string documents)
    {
        var path = Path.Combine(documents, IracingPaths.App);
        if (!File.Exists(path)) return null;
        try { return IsOn(IniDocument.Load(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static bool? IsOn(IniDocument doc) => doc.Find(Section, AutoLogKey)?.Value?.Trim() is { } v ? v == "1" : null;

    public static void SetRecording(bool on)
    {
        if (Processes.AppWatcher.IsRunningNow(IracingPaths.SimProcess))
            throw new InvalidOperationException("iRacing is running. It rewrites its settings when it closes, so close it first.");
        if (IracingPaths.FindDocumentsFolder() is not { } documents) throw new InvalidOperationException("iRacing's documents folder wasn't found.");
        var path = Path.Combine(documents, IracingPaths.App);
        var doc = IniDocument.Load(path);
        if (!doc.TrySet(Section, AutoLogKey, on ? "1" : "0")) throw new InvalidOperationException($"app.ini has no {AutoLogKey} line to change.");
        if (!doc.IsDirty) return;
        new BackupStore(IracingPaths.BackupFolder).Create($"Before Session Stats turned telemetry recording {(on ? "on" : "off")}", new[] { path }, documents);
        SafeFileWriter.WriteAtomic(path, doc.ToBytes());
    }
}

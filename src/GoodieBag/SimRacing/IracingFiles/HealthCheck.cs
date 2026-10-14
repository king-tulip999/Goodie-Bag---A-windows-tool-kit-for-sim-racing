using System.Globalization;
using System.IO;

namespace GoodieBag.SimRacing.IracingFiles;

internal enum Rating { Bad, Concerning, Good }

internal sealed record Finding(string Id, Rating Rating, string Title, string Detail, Setting? Setting = null);

internal sealed class HealthReport
{
    public List<Setting> Unusual { get; } = new();
    public List<Finding> Findings { get; } = new();
    public List<string> Passed { get; } = new();
    public TelemetryFolder? Telemetry { get; init; }
    public bool TelemetryLarge { get; set; }
}

internal static class HealthCheck
{
    public const long TelemetryWarnBytes = 5_000_000_000;
    private const long LowDiskBytes = 15_000_000_000;

    public static HealthReport Run(SettingsSession session, string? documentsFolder, string? installFolder, TelemetryFolder? telemetry, ISet<string> ignoredFindings)
    {
        var r = new HealthReport { Telemetry = telemetry };
        r.Unusual.AddRange(session.Settings.Where(s => s.IsUnusual)
            .OrderBy(s => s.Def.Default.HasValue ? 1 : 0)
            .ThenBy(s => s.Tier == "core" ? 0 : 1)
            .ThenBy(s => s.SectionInfo.AreaIndex).ThenBy(s => s.Line.LineNumber));

        void Add(Finding f) { if (!ignoredFindings.Contains(f.Id)) r.Findings.Add(f); }

        var mem = session.Find(IracingPaths.App, "Misc", "irsdkEnableMem");
        if (mem is not null && Setting.SameValue(mem.Draft, "0"))
            Add(new Finding("telemetry-off", Rating.Bad, "Telemetry apps can't read iRacing",
                "Live telemetry is off in app.ini, so SimHub, overlays and dashboards get nothing from iRacing.", mem));
        else if (mem is not null) r.Passed.Add("Telemetry apps can read iRacing");

        if (!session.InMemory)
        {
            var readOnly = session.FileNames.Where(f => { try { return new FileInfo(session.PathOf(f)).IsReadOnly; } catch { return false; } }).ToList();
            foreach (var f in readOnly)
                Add(new Finding("readonly:" + f.ToLowerInvariant(), Rating.Concerning, f + " is read-only",
                    "iRacing can't save its settings to it, and changes made here can't be written."));
            if (readOnly.Count == 0) r.Passed.Add("No settings files are read-only");
        }

        if (IracingPaths.IsInOneDrive(documentsFolder))
            Add(new Finding("onedrive", Rating.Concerning, "Your iRacing folder is inside OneDrive",
                "OneDrive can lock files while iRacing saves them, which loses settings. Pause syncing while you race."));
        else if (documentsFolder is not null) r.Passed.Add("Documents folder is not in OneDrive");

        var drives = new List<(string Root, string Role)>();
        void AddDrive(string? path, string role)
        {
            if (string.IsNullOrEmpty(path)) return;
            var root = (Path.GetPathRoot(Path.GetFullPath(path)) ?? "").ToUpperInvariant();
            if (root.Length == 0) return;
            int at = drives.FindIndex(d => d.Root == root);
            if (at >= 0) drives[at] = (root, drives[at].Role + " and " + role);
            else drives.Add((root, role));
        }
        AddDrive(installFolder, "the iRacing install drive");
        AddDrive(documentsFolder, "the drive with your iRacing settings");
        foreach (var (root, role) in drives)
        {
            long? free;
            try { free = new DriveInfo(root).AvailableFreeSpace; }
            catch { free = null; }
            if (free is not { } bytes) continue;
            var name = root.TrimEnd('\\');
            if (bytes < LowDiskBytes)
                Add(new Finding("disk:" + name, Rating.Concerning, $"{Gb(bytes)} free on {name}, {role}",
                    role.Contains("install") ? "Large iRacing updates can fail on a nearly full drive." : "Replays, telemetry and backups need room on this drive."));
            else r.Passed.Add($"{Gb(bytes)} free on {name}");
        }

        var autoLog = session.Find(IracingPaths.App, "Misc", "irsdkAutoLogDisk");
        if (telemetry is not null && autoLog is not null && Setting.SameValue(autoLog.Draft, "1"))
        {
            if (telemetry.TotalBytes >= TelemetryWarnBytes && !ignoredFindings.Contains("telemetry-large")) r.TelemetryLarge = true;
            else r.Passed.Add($"Telemetry folder is {Gb(telemetry.TotalBytes)}");
        }
        return r;
    }

    public static string Gb(long bytes) => bytes >= 1e12
        ? (bytes / 1e12).ToString("0.0", CultureInfo.InvariantCulture) + " TB"
        : (bytes / 1e9).ToString(bytes >= 10e9 ? "0" : "0.0", CultureInfo.InvariantCulture) + " GB";
}

internal static class LayoutGuard
{
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "MonitorSetup|NumMonitors", "MonitorSetup|MonitorType", "MonitorSetup|RadiusOfCurvature", "MonitorSetup|ScreenWidth", "MonitorSetup|MonitorWidth",
        "MonitorSetup|ViewingDist", "MonitorSetup|ScreenAngles", "MonitorSetup|EnableSMPSurround", "MonitorSetup|RenderViewPerMonitor", "MonitorSetup|BezelProtectionPct",
        "Display|windowedXPos", "Display|windowedYPos", "Display|windowedWidth", "Display|windowedHeight", "Display|border",
    };

    public sealed record Difference(Setting Setting, string Saved);

    public static IEnumerable<Setting> Settings(SettingsSession s) =>
        Keys.Select(k => k.Split('|')).Select(p => s.Find(IracingPaths.MonitorRenderer, p[0], p[1])).OfType<Setting>();

    public static Dictionary<string, string> Capture(SettingsSession s) =>
        Settings(s).ToDictionary(x => x.Section + "|" + x.Key, x => x.Applied, StringComparer.OrdinalIgnoreCase);

    public static List<Difference> Differences(SettingsSession s, IReadOnlyDictionary<string, string> saved) =>
        saved.Count == 0 ? new()
            : Settings(s).Where(x => saved.TryGetValue(x.Section + "|" + x.Key, out var want) && !Setting.SameValue(want, x.Applied))
                .Select(x => new Difference(x, saved[x.Section + "|" + x.Key])).ToList();

    public static int StageRestore(SettingsSession s, IReadOnlyDictionary<string, string> saved) =>
        Differences(s, saved).Count(d => s.SetDraft(d.Setting, d.Saved) is null);

    public static double Number(SettingsSession s, string key, double fallback) =>
        s.Find(IracingPaths.MonitorRenderer, "MonitorSetup", key) is { } x && CommentInference.TryNumber(x.Draft, out var n) ? n : fallback;

    public static double IdealAngle(double monitorWidthMm, double viewingDistMm) =>
        Math.Clamp(Math.Round(2 * Math.Atan2(monitorWidthMm / 2, Math.Max(1, viewingDistMm)) * 180 / Math.PI), 10, 65);
}

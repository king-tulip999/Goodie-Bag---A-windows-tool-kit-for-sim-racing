using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GoodieBag.Config;
using GoodieBag.Hardware;

namespace GoodieBag.Services;

internal static class DiagnosticReport
{
    private const string Consumer = "Diagnostics";

    public static async Task<string> BuildAsync(AppServices app, bool full)
    {
        var hw = app.Hardware;
        bool held = false;
        if (hw.Catalog is null)
        {
            hw.Acquire(Consumer);
            held = true;
            for (int i = 0; i < 50 && hw.Catalog is null && hw.OpenError is null; i++)
                await Task.Delay(100);
        }
        try
        {
            return full ? Full(app) : Short(app);
        }
        finally
        {
            if (held) hw.Release(Consumer);
        }
    }

    private static string Short(AppServices app)
    {
        var sb = new StringBuilder();
        var hw = app.Hardware;
        var devices = hw.Devices ?? Array.Empty<DeviceInfo>();
        sb.AppendLine($"Goodie Bag {UpdateService.Show(UpdateService.Current)}{(app.Demo ? " (demo data)" : "")}");
        sb.AppendLine(WindowsVersion());
        foreach (var d in devices.Where(d => d.Kind == "Cpu")) sb.AppendLine("CPU: " + d.Name);
        foreach (var d in devices.Where(d => d.Kind.StartsWith("Gpu", StringComparison.Ordinal))) sb.AppendLine("GPU: " + d.Name);
        foreach (var d in devices.Where(d => d.Kind == "Motherboard")) sb.AppendLine("Motherboard: " + d.Name);
        sb.AppendLine($"Administrator: {(app.IsAdmin ? "yes" : "no")} · PawnIO: {hw.PawnIoState}{(hw.CpuTempMissing ? " (CPU temperature missing)" : "")}");
        sb.AppendLine(SensorsLine(hw));
        sb.AppendLine($"Fan control: {(app.Config.Fans.Enabled && app.FanControlAvailable ? "on" : "off")} · sensor update rate {Rate(app.Config.Performance)}");
        return Scrub(sb.ToString(), Environment.UserName, Environment.MachineName, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    private static string Full(AppServices app)
    {
        var sb = new StringBuilder();
        var c = app.Config;
        var hw = app.Hardware;
        var devices = hw.Devices ?? Array.Empty<DeviceInfo>();
        var catalog = hw.Catalog;

        sb.AppendLine("# Goodie Bag diagnostic report");
        sb.AppendLine();
        sb.AppendLine($"Created {DateTime.Now:yyyy-MM-dd HH:mm}. Personal details (user and PC names, profile paths, IP addresses) are left out.");
        sb.AppendLine();
        sb.AppendLine("## App and Windows");
        sb.AppendLine($"- Goodie Bag {UpdateService.Show(UpdateService.Current)}{(app.Demo ? " (demo data)" : "")}");
        sb.AppendLine($"- {WindowsVersion()}");
        sb.AppendLine($"- .NET {Environment.Version}, {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
        sb.AppendLine($"- Running as administrator: {(app.IsAdmin ? "yes" : "no")}");
        sb.AppendLine($"- Drawing: {PcClass.Describe()}");
        sb.AppendLine();

        sb.AppendLine("## Sensors");
        sb.AppendLine($"- {SensorsLine(hw)}");
        sb.AppendLine($"- PawnIO driver: {hw.PawnIoState}{(hw.CpuTempMissing ? ", CPU temperature missing" : "")}");
        if (hw.OpenError is { } error) sb.AppendLine($"- Couldn't open the sensors: {error}");
        sb.AppendLine($"- In use by: {(hw.Consumers.Count > 0 ? string.Join(", ", hw.Consumers) : "nothing")}");
        if (catalog is not null)
            sb.AppendLine($"- {catalog.Temperatures.Count} temperature sensors ({catalog.Temperatures.Count(t => t.Value is not null)} reading), {catalog.Fans.Count} fans / fan controls{(catalog.Emergency ? ", EMERGENCY failsafe active" : "")}");
        if (hw.Tree is { } tree)
        {
            var kinds = tree.Devices.SelectMany(d => d.AllSensors).GroupBy(s => s.Kind).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {g.Key}");
            sb.AppendLine($"- Sensor list: {string.Join(", ", kinds)}");
        }
        sb.AppendLine();

        sb.AppendLine("## Detected devices");
        if (devices.Count == 0) sb.AppendLine("- None (sensors not open)");
        foreach (var d in devices)
            sb.AppendLine($"- {d.Kind}: {d.Name} ({d.Sensors} sensors)");
        if (hw.Tree is { } full)
            foreach (var d in full.Devices.Where(d => d.Kind is DeviceKind.Storage or DeviceKind.Network))
                sb.AppendLine($"- {d.Kind}: {d.Name} ({d.AllSensors.Count()} sensors)");
        sb.AppendLine();

        sb.AppendLine("## Fans");
        if (catalog is null || catalog.Fans.Count == 0) sb.AppendLine("- None detected");
        else
            foreach (var f in catalog.Fans)
            {
                var setup = c.Fans.Hardware.FirstOrDefault(h => h.ControlId == f.ControlId);
                var parts = new List<string>
                {
                    f.HasRpm ? (f.Rpm is { } rpm ? $"{rpm:0} RPM" : "RPM not available") : "no RPM sensor",
                    f.Percent is { } pct ? $"{pct:0} %" : "speed % not available",
                    f.Controllable ? (f.Controlled ? "controlled by Goodie Bag" : "can be controlled") : "can't be controlled (reading only)",
                };
                if (setup?.IsPump == true) parts.Add("pump");
                if (setup?.Hidden == true) parts.Add("hidden");
                if (f.Error is { } e) parts.Add("problem: " + e);
                sb.AppendLine($"- {f.DefaultName} ({f.Hardware}): {string.Join(", ", parts)}");
            }
        sb.AppendLine();

        sb.AppendLine("## Plugins");
        foreach (var p in app.Plugins.All)
            sb.AppendLine($"- {p.Name}: {(p.IsActive ? "on" : "off")}");
        sb.AppendLine();

        sb.AppendLine("## Settings (choices and counts only)");
        sb.AppendLine($"- Theme {c.Ui.Theme}, interface size {c.Ui.InterfaceScale}%, animations {(c.Ui.Animations ? "on" : "off")}");
        sb.AppendLine($"- Sensor update rate {Rate(c.Performance)}, background usage {c.Performance.Background}");
        sb.AppendLine($"- Fan control {(c.Fans.Enabled ? "on" : "off")}{(app.FanControlAvailable ? "" : " (plugin off)")}, failsafe {c.Fans.EmergencyTempC} °C, {c.Fans.Presets.Count} presets, {c.Fans.Curves.Count} curves, {c.Fans.Hardware.Count} fans set up");
        sb.AppendLine($"- Overlay {(c.Overlay.Visible ? "shown" : "hidden")}, {c.Overlay.Layouts.Count} layouts, updates every {c.Overlay.UpdateIntervalMs} ms");
        sb.AppendLine($"- {c.WindowProfiles.Profiles.Count} window profiles ({c.WindowProfiles.Profiles.Count(p => p.Auto)} automatic, applying automatically {(c.WindowProfiles.AutoApply ? "on" : "off")}), {c.WindowProfiles.Groups.Count} groups, {c.Displays.Profiles.Count} display profiles");
        sb.AppendLine($"- {ConfigTransfer.Keybinds(c)} keybinds");
        sb.AppendLine($"- Close in background {(c.Ui.CloseToTray ? "on" : "off")}, start in background {(c.Ui.StartMinimized ? "on" : "off")}, update checks {c.Updates.Check}");
        sb.AppendLine();

        sb.AppendLine("## Recent errors (end of error.log)");
        string tail = ErrorLogTail(60);
        sb.AppendLine(tail.Length == 0 ? "None logged." : "```\n" + tail.TrimEnd() + "\n```");

        return Scrub(sb.ToString(), Environment.UserName, Environment.MachineName, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    private static string Rate(PerformanceSettings p) => $"{PerformanceSettings.Name(p.Rate)} ({PerformanceSettings.Describe(p.IntervalMs)})";

    private static string SensorsLine(HardwareService hw) =>
        hw.OpenError is not null ? "Sensors: couldn't be opened"
        : !hw.Running ? "Sensors: idle (nothing is using them)"
        : $"Sensors: running, {hw.Devices?.Count ?? 0} devices, {hw.Catalog?.Temperatures.Count ?? 0} temperatures, {hw.Catalog?.Fans.Count ?? 0} fans";

    public static string LogPath => Path.Combine(ConfigStore.Folder, "error.log");

    private static string ErrorLogTail(int lines)
    {
        try
        {
            if (!File.Exists(LogPath)) return "";
            using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, stream.Length - 16 * 1024);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var all = reader.ReadToEnd().Replace("\r", "").Split('\n');
            if (start > 0) all = all.Skip(1).ToArray();
            return string.Join("\n", all.TakeLast(lines));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(error.log couldn't be read)";
        }
    }

    public static string WindowsVersion()
    {
        string name = "Windows", display = "", build = Environment.OSVersion.Version.Build.ToString();
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is not null)
            {
                name = key.GetValue("ProductName") as string ?? name;
                display = key.GetValue("DisplayVersion") as string ?? "";
                build = key.GetValue("CurrentBuild") as string ?? build;
                if (key.GetValue("UBR") is int ubr) build += "." + ubr;
            }
        }
        catch { }
        if (int.TryParse(build.Split('.')[0], out int b) && b >= 22000)
            name = name.Replace("Windows 10", "Windows 11");
        return $"{name}{(display.Length > 0 ? " " + display : "")} (build {build}), {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}";
    }

    private static readonly Regex UsersFolder = new(@"(?i)(\\Users\\)[^\\/\s""':]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Ipv4 = new(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled);

    public static string Scrub(string text, string? userName, string? machineName, string? profilePath)
    {
        if (!string.IsNullOrEmpty(profilePath) && profilePath.Length > 3)
            text = text.Replace(profilePath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        text = UsersFolder.Replace(text, "$1<user>");
        foreach (var (word, mask) in new[] { (userName, "<user>"), (machineName, "<pc>") })
            if (!string.IsNullOrWhiteSpace(word) && word.Length >= 3)
                text = Regex.Replace(text, @"(?<![\w-])" + Regex.Escape(word) + @"(?![\w-])", mask, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Ipv4.Replace(text, "<ip>");
    }
}

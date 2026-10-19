using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoodieBag.Config;

internal sealed record ImportPreview(AppConfig? Config, string? Error, IReadOnlyList<string> Lines, string? SavedBy);

internal static class ConfigTransfer
{
    public const string Header = "goodieBagExport";

    public static string Export(AppConfig config, string appVersion, DateTime now)
    {
        var copy = ConfigStore.Clone(config);
        copy.Updates.LastCheckUtc = null;
        copy.Updates.NotifiedVersion = null;
        copy.Ui.SetupPending = false;
        copy.Windowsmith = null;
        var body = JsonSerializer.SerializeToNode(copy, ConfigStore.JsonOptions)!.AsObject();
        var file = new JsonObject
        {
            [Header] = new JsonObject { ["app"] = "Goodie Bag", ["version"] = appVersion, ["exported"] = now.ToString("yyyy-MM-dd HH:mm") },
        };
        foreach (var name in body.Select(p => p.Key).ToList())
        {
            var value = body[name];
            body.Remove(name);
            file[name] = value;
        }
        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static readonly HashSet<string> KnownSections = BuildKnownSections();

    private static HashSet<string> BuildKnownSections()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Header, "windowsmith" };
        foreach (var property in JsonSerializer.SerializeToElement(new AppConfig(), ConfigStore.JsonOptions).EnumerateObject())
            names.Add(property.Name);
        return names;
    }

    public static ImportPreview Read(string? json, Version current)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Fail("The file is empty.");
        string? savedBy = null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail(NotSettings);
            int known = 0;
            foreach (var property in root.EnumerateObject())
                if (KnownSections.Contains(property.Name)) known++;
            if (known == 0)
                return Fail(NotSettings);
            if (root.TryGetProperty(Header, out var header) && header.ValueKind == JsonValueKind.Object
                && header.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String)
                savedBy = version.GetString();
        }
        catch (JsonException ex)
        {
            return Fail($"This file isn't a readable settings file ({ex.Message}).");
        }

        AppConfig config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(json, ConfigStore.JsonOptions) ?? new AppConfig();
            ConfigStore.Normalize(config);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or InvalidCastException)
        {
            return Fail($"Parts of this file don't match Goodie Bag's settings, so nothing was imported ({ex.Message}).");
        }
        config.Ui.SetupPending = false;

        var lines = Summary(config);
        if (savedBy is not null && TryVersion(savedBy) is { } v && v > current)
            lines.Insert(0, "Saved by a newer Goodie Bag: anything this one doesn't know is skipped.");
        return new ImportPreview(config, null, lines, savedBy);
    }

    private const string NotSettings = "This doesn't look like a Goodie Bag settings file.";

    private static ImportPreview Fail(string error) => new(null, error, Array.Empty<string>(), null);

    private static Version? TryVersion(string text) => Version.TryParse(text.TrimStart('v', 'V'), out var v) ? v : null;

    public static List<string> Summary(AppConfig c)
    {
        var lines = new List<string>
        {
            $"Theme {ThemeName(c.Ui.Theme)}, interface size {c.Ui.InterfaceScale}%, animations {(c.Ui.Animations ? "on" : "off")}",
            Count(c.WindowProfiles.Profiles.Count, "window profile", "window profiles") + $" ({c.WindowProfiles.Profiles.Count(p => p.Auto)} automatic), {Count(c.WindowProfiles.Groups.Count, "group", "groups")}",
            Count(c.Displays.Profiles.Count, "display profile", "display profiles"),
            $"Fan control {(c.Fans.Enabled ? "on" : "off")}: {Count(c.Fans.Presets.Count, "preset", "presets")}, {Count(c.Fans.Curves.Count, "curve", "curves")}, {Count(c.Fans.Hardware.Count, "fan", "fans")} set up",
            Count(c.Overlay.Layouts.Count, "overlay layout", "overlay layouts"),
            Count(Keybinds(c), "keybind", "keybinds"),
            $"Sensor update rate {PerformanceSettings.Name(c.Performance.Rate)} ({PerformanceSettings.Describe(c.Performance.IntervalMs)}), background usage {c.Performance.Background}",
        };
        if (c.Plugins.Count > 0)
            lines.Add($"Plugins: {c.Plugins.Count(p => p.Value)} on, {c.Plugins.Count(p => !p.Value)} off");
        lines.Add("Window, startup, home page order, page layouts, sim racing tools and update settings");
        return lines;
    }

    private static string ThemeName(AppTheme id) => Theme.Themes.For(id) is { } spec && spec.Id == id ? spec.Name : id.ToString();

    public static int Keybinds(AppConfig c)
    {
        var a = c.Hotkeys;
        return c.WindowProfiles.Profiles.Count(p => Set(p.Hotkey)) + c.WindowProfiles.Groups.Count(g => Set(g.Hotkey)) + c.Displays.Profiles.Count(p => Set(p.Hotkey))
            + c.Fans.Presets.Count(p => Set(p.Hotkey))
            + new[] { c.Overlay.Hotkey, c.Overlay.ResetFpsHotkey, c.Ui.SearchKey, a.ShowWindow,
                a.NextDisplayProfile, a.PreviousDisplayProfile, a.FanControl, a.NextFanPreset, a.PreviousFanPreset, a.NextOverlayLayout }.Count(Set);
    }

    private static bool Set(string? hotkey) => !string.IsNullOrWhiteSpace(hotkey);

    private static string Count(int n, string one, string many) => n == 0 ? "No " + many : n == 1 ? "1 " + one : $"{n} {many}";
}

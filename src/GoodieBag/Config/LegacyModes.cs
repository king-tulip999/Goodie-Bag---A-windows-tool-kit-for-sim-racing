namespace GoodieBag.Config;

public sealed class LegacyMode
{
    public string Name { get; set; } = "";

    public string? Hotkey { get; set; }

    public string? DisplayProfile { get; set; }

    public DisplaySnapshot? Display { get; set; }

    public HdrMode? Hdr { get; set; }

    public List<WindowRule> Windows { get; set; } = new();
}

public sealed class WindowRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int DelayMs { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool RemoveBorders { get; set; }
    public bool HideTitleBar { get; set; }

    public WindowProfile ToProfile() => new()
    {
        Id = Id.Length > 0 ? Id : Guid.NewGuid().ToString("N"), Name = Name, ProcessName = ProcessName, DelayMs = DelayMs,
        Width = Width, Height = Height, X = X, Y = Y, RemoveBorders = RemoveBorders, HideTitleBar = HideTitleBar,
    };
}

internal static class LegacyModes
{
    public const string OldPluginId = "modes";

    private static readonly string[] RemovedSections = { "modeSwitching", "apps", "automations", "launcher" };

    public static void Migrate(AppConfig config)
    {
        var modes = config.Modes;
        config.Modes = null;
        foreach (var key in RemovedSections) config.Extra?.Remove(key);
        config.Plugins.Remove(OldPluginId);
        config.Plugins.Remove("automations");
        config.Plugins.Remove("launcher");
        var ui = config.Ui;
        foreach (var slot in ui.LandingLayout.Where(s => s.Id == OldPluginId)) slot.Id = Plugins.PluginIds.WindowProfiles;
        for (int i = 0; i < ui.NavOrder.Count; i++)
            if (ui.NavOrder[i] is OldPluginId or "automations" or "launcher") ui.NavOrder[i] = Plugins.PluginIds.WindowProfilesPage;
        ui.NavOrder = ui.NavOrder.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ui.StartPage is OldPluginId or "automations" or "launcher") ui.StartPage = Plugins.PluginIds.WindowProfilesPage;
        if (modes is null) return;

        var settings = config.WindowProfiles;
        var known = settings.Profiles.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var added = new List<WindowProfile>();
        foreach (var mode in modes)
        {
            var fresh = mode.Windows.Where(r => !string.IsNullOrWhiteSpace(r.ProcessName) && (r.Id.Length == 0 || known.Add(r.Id)))
                .Select(r => r.ToProfile()).ToList();
            if (fresh.Count == 0) continue;
            string? hotkey = string.IsNullOrWhiteSpace(mode.Hotkey) || Taken(config, mode.Hotkey) ? null : mode.Hotkey;
            if (fresh.Count > 1)
            {
                var group = new WindowGroup { Name = settings.UniqueName(mode.Name.Length > 0 ? mode.Name : "Group"), Hotkey = hotkey };
                settings.Groups.Add(group);
                foreach (var p in fresh) p.GroupId = group.Id;
            }
            else
            {
                fresh[0].Hotkey = hotkey;
            }
            foreach (var p in fresh)
            {
                p.Name = settings.UniqueName(p.Name.Length > 0 ? p.Name : System.IO.Path.GetFileNameWithoutExtension(p.ProcessName));
                settings.Profiles.Add(p);
                added.Add(p);
            }
        }
        OneAutoPerProgram(settings.Profiles, added);
    }

    internal static void OneAutoPerProgram(List<WindowProfile> all, List<WindowProfile> added)
    {
        var automatic = all.Where(p => p.Auto && !added.Contains(p)).Select(p => Processes.AppWatcher.Normalize(p.ProcessName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in added)
            p.Auto = automatic.Add(Processes.AppWatcher.Normalize(p.ProcessName));
    }

    private static bool Taken(AppConfig config, string hotkey) =>
        config.Displays.Profiles.Any(p => Hotkeys.Hotkey.SameKey(p.Hotkey, hotkey))
        || config.WindowProfiles.Profiles.Any(p => Hotkeys.Hotkey.SameKey(p.Hotkey, hotkey))
        || config.WindowProfiles.Groups.Any(g => Hotkeys.Hotkey.SameKey(g.Hotkey, hotkey));
}

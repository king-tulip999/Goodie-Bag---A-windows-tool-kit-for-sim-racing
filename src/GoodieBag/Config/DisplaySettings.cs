namespace GoodieBag.Config;

public enum TaskbarMode { Unchanged, AllDisplays, MainOnly }

public sealed class DisplaySettings
{
    public List<DisplayProfile> Profiles { get; set; } = new();

    public Dictionary<string, string> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DisplayProfile
{
    public string Name { get; set; } = "";
    public string? Hotkey { get; set; }

    public DisplaySnapshot? Layout { get; set; }

    public HdrMode Hdr { get; set; } = HdrMode.Unchanged;

    public TaskbarMode Taskbar { get; set; } = TaskbarMode.Unchanged;

    public Dictionary<string, string> Wallpapers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AudioSnapshot? Audio { get; set; }

    public int? Volume { get; set; }

    public static void Migrate(AppConfig config)
    {
        var profiles = config.Displays.Profiles;
        foreach (var mode in config.Modes.Where(m => m.Display is not null || m.Hdr is HdrMode.On or HdrMode.Off))
        {
            if (mode.DisplayProfile is null)
            {
                string name = mode.Name;
                for (int i = 2; profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
                    name = $"{mode.Name} {i}";
                profiles.Add(new DisplayProfile { Name = name, Layout = mode.Display, Hdr = mode.Hdr ?? HdrMode.Unchanged });
                mode.DisplayProfile = name;
            }
            mode.Display = null;
            mode.Hdr = null;
        }
        string? ProfileOfMode(string? modeName) =>
            config.Modes.FirstOrDefault(m => string.Equals(m.Name, modeName, StringComparison.OrdinalIgnoreCase))?.DisplayProfile;
        foreach (var app in (config.Apps ?? new()).Where(a => a.Overrides?.DisplayFrom is not null))
        {
            app.Overrides.DisplayProfile ??= ProfileOfMode(app.Overrides.DisplayFrom);
            app.Overrides.DisplayFrom = null;
        }
        foreach (var action in (config.Automations?.Rules ?? new()).SelectMany(r => r.Actions ?? new()).Where(a => a.Type == ActionType.DisplayLayout))
        {
            if (profiles.All(p => !string.Equals(p.Name, action.Target, StringComparison.OrdinalIgnoreCase)) && ProfileOfMode(action.Target) is { } profile)
                action.Target = profile;
        }
    }
}

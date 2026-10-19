namespace GoodieBag.Config;

public enum TaskbarMode { Unchanged, AllDisplays, MainOnly }

public enum HdrMode { Unchanged, On, Off }

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
        foreach (var mode in (config.Modes ?? new()).Where(m => m.Display is not null || m.Hdr is HdrMode.On or HdrMode.Off))
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
    }
}

public sealed class DisplaySnapshot
{
    public List<string> Summary { get; set; } = new();

    public List<string> TargetDevicePaths { get; set; } = new();

    public string Paths { get; set; } = "";

    public string Modes { get; set; } = "";
}

public sealed class AudioSnapshot
{
    public string? PlaybackId { get; set; }
    public string? PlaybackName { get; set; }
    public string? RecordingId { get; set; }
    public string? RecordingName { get; set; }

    public bool SetCommunications { get; set; } = true;
}

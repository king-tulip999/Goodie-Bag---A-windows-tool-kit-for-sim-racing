namespace GoodieBag.Config;

public sealed class LegacyWindowsmith
{
    public List<LegacyWindowProfile> Profiles { get; set; } = new();
    public List<LegacyWindowGroup> Groups { get; set; } = new();

    public static void Migrate(AppConfig config)
    {
        var legacy = config.Windowsmith;
        config.Windowsmith = null;
        var used = new HashSet<LegacyWindowProfile>();
        List<LegacyWindowProfile> Named(string? name)
        {
            if (name is null || legacy is null) return new();
            if (legacy.Profiles.FirstOrDefault(p => Same(p.Name, name)) is { } profile) return new() { profile };
            return legacy.Groups.FirstOrDefault(g => Same(g.Name, name)) is { } group ? legacy.Profiles.Where(p => p.GroupId == group.Id).ToList() : new();
        }
        foreach (var mode in config.Modes)
        {
            foreach (var p in Named(mode.WindowProfile))
            {
                used.Add(p);
                mode.Windows.Add(p.ToRule());
            }
            mode.WindowProfile = null;
        }
        var rest = legacy?.Profiles.Where(p => !used.Contains(p) && !string.IsNullOrWhiteSpace(p.ProcessName)).ToList() ?? new();
        if (rest.Count == 0) return;
        if (config.Modes.Count == 0)
            config.Modes.Add(new Mode { Name = "Windows" });
        foreach (var mode in config.Modes)
            foreach (var p in rest)
                if (mode.Windows.All(w => w.Id != p.Id))
                    mode.Windows.Add(p.ToRule());
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public sealed class LegacyWindowProfile
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
    public string? GroupId { get; set; }

    public WindowRule ToRule() => new()
    {
        Id = Id.Length > 0 ? Id : Guid.NewGuid().ToString("N"), Name = Name, ProcessName = ProcessName, DelayMs = DelayMs,
        Width = Width, Height = Height, X = X, Y = Y, RemoveBorders = RemoveBorders, HideTitleBar = HideTitleBar,
    };
}

public sealed class LegacyWindowGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

namespace GoodieBag.Config;

public sealed class LegacyWindowsmith
{
    public List<LegacyWindowProfile> Profiles { get; set; } = new();
    public List<LegacyWindowGroup> Groups { get; set; } = new();

    public static void Migrate(AppConfig config)
    {
        var legacy = config.Windowsmith;
        config.Windowsmith = null;
        if (legacy is null) return;
        var settings = config.WindowProfiles;
        var groups = new Dictionary<string, WindowGroup>(StringComparer.Ordinal);
        foreach (var g in legacy.Groups.Where(g => g.Id.Length > 0))
        {
            var group = new WindowGroup { Name = settings.UniqueName(g.Name.Length > 0 ? g.Name : "Group") };
            groups[g.Id] = group;
            settings.Groups.Add(group);
        }
        var added = new List<WindowProfile>();
        foreach (var p in legacy.Profiles.Where(p => !string.IsNullOrWhiteSpace(p.ProcessName) && settings.Profiles.All(e => e.Id != p.Id)))
        {
            var profile = p.ToRule().ToProfile();
            profile.Name = settings.UniqueName(profile.Name.Length > 0 ? profile.Name : System.IO.Path.GetFileNameWithoutExtension(profile.ProcessName));
            profile.GroupId = p.GroupId is not null && groups.TryGetValue(p.GroupId, out var group) ? group.Id : null;
            settings.Profiles.Add(profile);
            added.Add(profile);
        }
        settings.Groups.RemoveAll(g => settings.Profiles.All(p => p.GroupId != g.Id));
        LegacyModes.OneAutoPerProgram(settings.Profiles, added);
    }

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

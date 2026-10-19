using System.IO;

namespace GoodieBag.Config;

public sealed class WindowProfileSettings
{
    public const int MaxDelayMs = 120_000;

    public bool AutoApply { get; set; } = true;

    public List<WindowProfile> Profiles { get; set; } = new();

    public List<WindowGroup> Groups { get; set; } = new();

    public List<PanelSlot> Layout { get; set; } = new();

    public static string KeyOf(WindowProfile p) => "profile:" + p.Id;

    public static string KeyOf(WindowGroup g) => "group:" + g.Id;

    public IEnumerable<WindowProfile> Members(WindowGroup group) => Profiles.Where(p => p.GroupId == group.Id);

    public IEnumerable<WindowProfile> Ungrouped => Profiles.Where(p => p.GroupId is null);

    public WindowGroup? GroupOf(WindowProfile p) => p.GroupId is null ? null : Groups.FirstOrDefault(g => g.Id == p.GroupId);

    public IEnumerable<string> Names => Profiles.Select(p => p.Name).Concat(Groups.Select(g => g.Name));

    public string? NameProblem(string name, object? except)
    {
        if (name.Length == 0) return "Give it a name.";
        bool taken = Profiles.Any(p => !ReferenceEquals(p, except) && Same(p.Name, name))
                     || Groups.Any(g => !ReferenceEquals(g, except) && Same(g.Name, name));
        return taken ? $"“{name}” is already the name of a profile or group." : null;
    }

    public string UniqueName(string wanted, object? except = null)
    {
        string baseName = wanted.Trim().Length > 0 ? wanted.Trim() : "Window";
        string name = baseName;
        for (int i = 2; NameProblem(name, except) is not null; i++) name = $"{baseName} {i}";
        return name;
    }

    public void MoveWithinGroup(WindowGroup group, int from, int to)
    {
        var members = Members(group).ToList();
        if (from < 0 || from >= members.Count || to < 0 || to >= members.Count || from == to) return;
        var moving = members[from];
        var after = members[to];
        Profiles.Remove(moving);
        int at = Profiles.IndexOf(after);
        Profiles.Insert(from < to ? at + 1 : at, moving);
    }

    public void Remove(WindowGroup group)
    {
        int slot = Layout.FindIndex(s => s.Id == KeyOf(group));
        var members = Members(group).ToList();
        foreach (var p in members) p.GroupId = null;
        Groups.Remove(group);
        if (slot >= 0)
        {
            Layout.RemoveAt(slot);
            Layout.InsertRange(slot, members.Select(p => new PanelSlot { Id = KeyOf(p) }));
        }
    }

    public void Normalize()
    {
        Profiles ??= new();
        Groups ??= new();
        Layout ??= new();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in Profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id)) { p.Id = Guid.NewGuid().ToString("N"); ids.Add(p.Id); }
            p.Name = (p.Name ?? "").Trim();
            p.ProcessName = (p.ProcessName ?? "").Trim();
            if (p.Name.Length == 0) p.Name = Path.GetFileNameWithoutExtension(p.ProcessName);
            p.DelayMs = Math.Clamp(p.DelayMs, 0, MaxDelayMs);
            if (p.Width is null || p.Height is null) p.Width = p.Height = null;
            else { p.Width = Math.Clamp(p.Width.Value, 100, 32000); p.Height = Math.Clamp(p.Height.Value, 100, 32000); }
            p.X = Math.Clamp(p.X, -32000, 32000);
            p.Y = Math.Clamp(p.Y, -32000, 32000);
            if (!p.RemoveBorders) p.HideTitleBar = false;
        }
        foreach (var g in Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Id) || !ids.Add(g.Id)) { g.Id = Guid.NewGuid().ToString("N"); ids.Add(g.Id); }
            g.Name = (g.Name ?? "").Trim();
            if (g.Name.Length == 0) g.Name = "Group";
        }
        var groupIds = Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var p in Profiles.Where(p => p.GroupId is not null && !groupIds.Contains(p.GroupId)))
            p.GroupId = null;
        var keys = Groups.Select(KeyOf).Concat(Ungrouped.Select(KeyOf)).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Layout.RemoveAll(s => s is null || !keys.Contains(s.Id) || !seen.Add(s.Id));
        foreach (var slot in Layout) { slot.Col = slot.Row = slot.Cols = slot.Rows = null; }
        foreach (var key in Groups.Select(KeyOf).Concat(Ungrouped.Select(KeyOf)).Where(k => !seen.Contains(k)))
            Layout.Add(new PanelSlot { Id = key });
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public sealed class WindowProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    public string ProcessName { get; set; } = "";

    public bool Auto { get; set; } = true;

    public int DelayMs { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }

    public int X { get; set; }
    public int Y { get; set; }

    public bool RemoveBorders { get; set; }

    public bool HideTitleBar { get; set; }

    public string? Hotkey { get; set; }

    public string? GroupId { get; set; }

    public string Label => Name.Length > 0 ? Name : ProcessName;

    public WindowProfile Copy() => (WindowProfile)MemberwiseClone();
}

public sealed class WindowGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    public string? Hotkey { get; set; }

}

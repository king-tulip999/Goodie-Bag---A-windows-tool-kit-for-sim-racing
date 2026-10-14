namespace GoodieBag.Config;

public sealed class LauncherSettings
{
    public List<LauncherEntry> Entries { get; set; } = new();
}

public sealed class LauncherEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Arguments { get; set; }
    public bool AsAdmin { get; set; }
    public string? IconPath { get; set; }
    public string? ModeName { get; set; }
    public List<LaunchItem> AlsoLaunch { get; set; } = new();
    public string? ProcessName { get; set; }
    public bool CloseHelpersOnExit { get; set; }
}

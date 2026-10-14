namespace GoodieBag.Config;

internal static class ConfigReferences
{
    public static void FanPreset(AppConfig config, string oldName, string? newName)
    {
        foreach (var m in config.Modes) m.FanPreset = Follow(m.FanPreset, oldName, newName);
    }

    public static void OverlayLayout(AppConfig config, string oldName, string? newName)
    {
        foreach (var m in config.Modes) m.OverlayLayout = Follow(m.OverlayLayout, oldName, newName);
    }

    public static void Mode(AppConfig config, string oldName, string? newName)
    {
        if (newName is null) return;
        foreach (var t in config.Modes.SelectMany(m => m.Triggers)) t.OnlyInMode = Follow(t.OnlyInMode, oldName, newName);
    }

    public static void ColorSet(AppConfig config, string oldName, string? newName)
    {
        foreach (var l in config.Overlay.Layouts) l.ColorSet = Follow(l.ColorSet, oldName, newName);
    }

    public static void DisplayProfile(AppConfig config, string oldName, string? newName)
    {
        foreach (var m in config.Modes) m.DisplayProfile = Follow(m.DisplayProfile, oldName, newName);
    }

    private static bool Same(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string? Follow(string? value, string oldName, string? newName) => Same(value, oldName) ? newName : value;
}

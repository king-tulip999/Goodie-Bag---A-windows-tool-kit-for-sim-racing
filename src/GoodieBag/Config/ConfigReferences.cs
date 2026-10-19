namespace GoodieBag.Config;

internal static class ConfigReferences
{
    public static void ColorSet(AppConfig config, string oldName, string? newName)
    {
        foreach (var l in config.Overlay.Layouts) l.ColorSet = Follow(l.ColorSet, oldName, newName);
    }

    public static void FanCurve(AppConfig config, string oldName, string? newName)
    {
        foreach (var e in config.Fans.Presets.SelectMany(p => p.Fans).Where(e => Same(e.CurveName, oldName)))
        {
            e.CurveName = newName;
            if (newName is null && e.Mode == FanMode.Curve) e.Mode = FanMode.Auto;
        }
        foreach (var mix in config.Fans.Curves)
        {
            for (int i = mix.MixOf.Count - 1; i >= 0; i--)
                if (Same(mix.MixOf[i], oldName))
                {
                    if (newName is null) mix.MixOf.RemoveAt(i);
                    else mix.MixOf[i] = newName;
                }
        }
    }

    public static List<string> Dangling(AppConfig config)
    {
        var found = new List<string>();
        void Check(string what, string? name, IEnumerable<string> names)
        {
            if (!string.IsNullOrEmpty(name) && !names.Any(n => Same(n, name))) found.Add($"{what} → “{name}”");
        }
        var presets = config.Fans.Presets.Select(p => p.Name).ToList();
        var curves = config.Fans.Curves.Select(c => c.Name).ToList();
        var layouts = config.Overlay.Layouts.Select(l => l.Name).ToList();
        var colorSets = config.Ui.OledCustom.Profiles.Select(p => p.Name).Append(Theme.Themes.OledDefaultSet).ToList();
        if (presets.Count > 0) Check("active fan preset", config.Fans.ActivePreset, presets);
        var groups = config.WindowProfiles.Groups.Select(g => g.Id).ToList();
        foreach (var w in config.WindowProfiles.Profiles)
            Check($"window profile “{w.Name}” group", w.GroupId, groups);
        foreach (var p in config.Fans.Presets)
            foreach (var e in p.Fans.Where(e => e.Mode == FanMode.Curve))
                Check($"fan preset “{p.Name}” fan {e.ControlId} curve", e.CurveName, curves);
        foreach (var c in config.Fans.Curves.Where(c => c.Type == FanCurveType.Mix))
            foreach (var part in c.MixOf)
                Check($"mix curve “{c.Name}” part", part, curves);
        if (layouts.Count > 0) Check("active overlay layout", config.Overlay.ActiveLayout, layouts);
        foreach (var l in config.Overlay.Layouts)
            Check($"overlay layout “{l.Name}” color set", l.ColorSet, colorSets);
        Check("color set being edited", config.Ui.OledCustom.EditingProfile, colorSets);
        if (config.SimRacing.Hud is { } hud)
        {
            var hudLayouts = hud.Layouts.Select(l => l.Name).ToList();
            if (hudLayouts.Count > 0) Check("active iRacing Overlay layout", hud.ActiveLayout, hudLayouts);
            foreach (var r in hud.Rules) Check("iRacing Overlay rule layout", r.Layout, hudLayouts);
        }
        return found;
    }

    private static bool Same(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string? Follow(string? value, string oldName, string? newName) => Same(value, oldName) ? newName : value;
}

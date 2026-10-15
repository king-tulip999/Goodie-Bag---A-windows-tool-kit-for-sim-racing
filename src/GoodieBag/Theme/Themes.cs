using System.Windows;
using GoodieBag.Config;

namespace GoodieBag.Theme;

internal sealed record ThemeSpec
{
    public required AppTheme Id { get; init; }
    public required string Name { get; init; }
    public required bool Dark { get; init; }
    public required IReadOnlyDictionary<string, System.Windows.Media.Color> Colors { get; init; }

    public string DisplayFont { get; init; } = "Segoe UI Variable Display, Segoe UI";
    public string UiFont { get; init; } = "Segoe UI Variable Text, Segoe UI";
    public string MonoFont { get; init; } = "Consolas, Segoe UI";
    public string? MetricFont { get; init; }
    public FontWeight HeadingWeight { get; init; } = FontWeights.Bold;
    public FontStyle HeadingStyle { get; init; } = FontStyles.Normal;
    public bool Caps { get; init; } = true;
    public bool MonoLabels { get; init; } = true;

    public Uri? WindowIcon { get; init; }
    public ThemeLink? Link { get; init; }

    public double Radius { get; init; }
    public double SwitchRadius { get; init; } = 11;
    public double KnobRadius { get; init; } = 8;
    public double ChipRadius { get; init; }
    public Thickness CardEdge { get; init; } = new(0, 1, 0, 2);
    public bool TileEdge => CardEdge.Bottom > CardEdge.Top;
    public Thickness ButtonEdge { get; init; } = new(1);
    public Thickness ButtonEdgePressed { get; init; } = new(1);
    public double PressDrop { get; init; } = 0;
    public string Outline { get; init; } = "LineStrong";
    public Thickness LogoEdge { get; init; }
    public double CardChamfer { get; init; }
    public double ControlChamfer { get; init; }

    public TrackShape Track { get; init; }

    public double TrackBoost { get; init; } = 1;

    public bool RacingDecor { get; init; }
    public double FocusOffset { get; init; } = 1;

    public bool Glow { get; init; }
    public double GlowStrength { get; init; } = 1;
    public bool CornerGlow { get; init; }
    public bool StepsClimb { get; init; } = true;

    public required AppTheme Partner { get; init; }
}

internal static class Themes
{
    public static ThemeSpec? Variant(ThemeSpec spec)
    {
        var partner = For(spec.Partner);
        return partner.Id != spec.Id && partner.Partner == spec.Id && partner.Dark != spec.Dark ? partner : null;
    }

    public static readonly ThemeSpec Light = new()
    {
        Id = AppTheme.Light, Name = "Light", Dark = false, Colors = Palettes.Light, Partner = AppTheme.Dark,
        Track = TrackShape.Hairpin, Outline = "ButtonEdge",
    };

    private static readonly Thickness Hairline = new(1);

    public static readonly ThemeSpec Dark = new()
    {
        Id = AppTheme.Dark, Name = "Dark", Dark = true, Colors = Palettes.Dark, Partner = AppTheme.Light,
        CardEdge = Hairline, LogoEdge = new(0),
        StepsClimb = false, Track = TrackShape.Esses, Outline = "ScrollThumbBrushDrag",
    };

    public static readonly ThemeSpec Custom = new()
    {
        Id = AppTheme.Custom, Name = DefaultCustomName, Dark = true, Colors = Palettes.Custom, Partner = AppTheme.Light,
        CardEdge = Hairline, LogoEdge = new(1.5),
        Glow = true, GlowStrength = 0.45, StepsClimb = false, Track = TrackShape.Chicane,
        TrackBoost = 1.5,
    };

    public static readonly ThemeSpec OledPastel = new()
    {
        Id = AppTheme.OledPastel, Name = "OLED Pastel", Dark = true, Colors = Palettes.OledPastel, Partner = AppTheme.Light,
        CardEdge = Hairline, LogoEdge = new(1.5),
        Glow = true, GlowStrength = 0.35, StepsClimb = false, Track = TrackShape.Chicane,
        TrackBoost = 1.5,
    };

    public static readonly ThemeSpec[] BuiltIn = { Light, Dark, Custom, OledPastel };

    private static ThemeSpec[] _all = BuiltIn;

    public static IReadOnlyList<ThemeSpec> All => _all;

    public static bool Register(ThemeSpec theme)
    {
        if (_all.Any(t => t.Id == theme.Id)) return false;
        _all = _all.Append(theme).ToArray();
        OverlayPresets.ClearCache();
        return true;
    }

    public static void Unregister(AppTheme id)
    {
        if (BuiltIn.Any(t => t.Id == id)) return;
        _all = _all.Where(t => t.Id != id).ToArray();
        OverlayPresets.ClearCache();
    }

    internal static void ResetRegistered() => _all = BuiltIn;

    public static void SetOledCustom(CustomThemeSettings custom)
    {
        string name = string.IsNullOrWhiteSpace(custom.Name) ? DefaultCustomName : custom.Name.Trim();
        var spec = custom.InUse
            ? Custom with { Name = name, Colors = CustomPalette.Build(custom.Colors), Dark = CustomPalette.IsDark(custom.Colors) }
            : Custom with { Name = name };
        _all = _all.Select(t => t.Id == AppTheme.Custom ? spec : t).ToArray();
        OverlayPresets.ClearCache();
    }

    public static void SetPastelPreset(string? name)
    {
        var preset = CustomPalette.Preset(name);
        var spec = preset is { } colors ? OledPastel with { Colors = CustomPalette.Build(colors), Dark = CustomPalette.IsDark(colors) } : OledPastel;
        _all = _all.Select(t => t.Id == AppTheme.OledPastel ? spec : t).ToArray();
        OverlayPresets.ClearCache();
    }

    public const string OledDefaultSet = "#default";

    public static ThemeSpec ForSet(AppTheme id, string? set)
    {
        if (id != AppTheme.Custom || string.IsNullOrEmpty(set)) return For(id);
        if (set == OledDefaultSet) return Custom;
        if (Services.AppServices.Current?.Config.Ui.OledCustom.Profile(set) is not { } profile) return For(id);
        return Custom with { Name = profile.Name, Colors = CustomPalette.Build(profile.Colors), Dark = CustomPalette.IsDark(profile.Colors) };
    }

    public static ThemeSpec? OledSibling(ThemeSpec spec) =>
        spec.Id == AppTheme.Custom ? For(AppTheme.OledPastel) : spec.Id == AppTheme.OledPastel ? For(AppTheme.Custom) : null;

    public const string DefaultCustomName = "Custom";

    public static ThemeSpec For(AppTheme id) => _all.FirstOrDefault(t => t.Id == id) ?? Dark;

    public static bool Exists(AppTheme id) => _all.Any(t => t.Id == id);
}

internal sealed record ThemeLink(Uri Logo, string Url, string Title, string Tip)
{
    public required string Name { get; init; }
}

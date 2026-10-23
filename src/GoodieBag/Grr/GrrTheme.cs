using System.Windows;
using System.Windows.Media;
using GoodieBag.Config;
using GoodieBag.Theme;
using static GoodieBag.Theme.Palettes;

namespace GoodieBag.Grr;

internal static class GrrResources
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        ThemeManager.ExtraTokens.Add((spec, colors, res) =>
        {
            foreach (var (key, color) in GrrTheme.Podium(spec.Id, colors))
                res[key] = Frozen(color);
            res[GrrPlugin.LogoIcon] = Logo;
        });
    }

    private static readonly Geometry Logo = Frozen(Geometry.Parse("F0 M1.24,8.90 L9.12,8.90 8.72,10.14 8.48,10.38 2.88,10.38 2.80,10.46 2.80,10.54 3.32,10.98 3.32,11.10 2.96,12.18 2.88,12.26 2.56,13.34 2.48,13.42 2.48,13.58 5.48,13.58 5.64,13.10 5.72,13.02 5.84,12.50 5.08,12.46 4.88,12.26 4.88,12.18 4.48,11.82 4.52,11.62 8.24,11.62 8.24,11.78 8.12,11.98 7.56,13.70 7.16,14.46 6.72,14.86 6.00,15.10 0.28,15.10 0.12,15.02 0.00,14.86 0.00,14.58 1.72,9.54 1.72,9.42 1.64,9.34 1.56,9.34 1.48,9.22 1.20,9.06 Z M9.28,8.90 L15.64,8.90 16.16,9.06 16.52,9.38 16.68,9.78 16.68,10.06 16.60,10.38 9.52,10.38 9.60,10.02 9.80,9.58 9.80,9.42 9.48,9.18 9.40,9.18 9.36,9.10 9.28,9.10 Z M16.64,8.90 L22.96,8.90 23.40,9.02 23.80,9.30 23.96,9.58 24.00,10.10 23.52,11.58 23.20,12.18 22.92,12.50 22.84,12.50 22.72,12.66 22.64,12.66 22.40,12.86 21.92,13.02 21.20,13.06 22.16,14.58 22.16,14.82 22.04,15.02 21.88,15.10 20.08,15.10 19.00,13.06 18.00,13.06 17.40,14.86 17.24,15.06 15.20,15.10 15.48,14.14 15.56,14.06 15.76,13.34 15.84,13.26 16.00,12.66 16.08,12.58 16.40,11.62 20.92,11.62 20.96,11.70 20.88,12.06 20.96,12.06 21.16,11.90 22.76,11.10 22.72,10.98 22.56,10.94 22.52,10.86 22.44,10.86 22.40,10.78 22.12,10.66 22.08,10.58 22.00,10.58 21.96,10.50 21.68,10.38 21.64,10.30 21.56,10.30 21.24,10.06 21.16,10.18 21.16,10.38 16.84,10.38 17.16,9.42 17.08,9.42 16.88,9.22 16.68,9.14 16.60,9.06 Z M9.08,11.62 L16.12,11.62 16.12,11.74 15.88,12.18 15.28,12.74 14.88,12.94 14.56,13.02 13.88,13.06 13.88,13.14 13.96,13.18 14.16,13.58 14.24,13.62 14.24,13.70 14.32,13.74 14.44,14.02 14.52,14.06 14.52,14.14 14.60,14.18 14.60,14.26 14.84,14.58 14.80,14.90 14.72,14.94 14.72,15.02 14.56,15.10 12.76,15.10 11.68,13.06 10.64,13.10 10.16,14.62 10.08,14.70 10.04,14.94 9.92,15.06 7.84,15.10 Z"));

    private static Geometry Frozen(Geometry g)
    {
        g.Freeze();
        return g;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

internal static class GrrTheme
{
    public static readonly AppTheme Id = new("Grr");

    private static ThemeSpec? _spec;

    public static ThemeSpec Spec => _spec ??= new()
    {
        Id = Id, Name = "GRR", Dark = true, Colors = Palette, Partner = AppTheme.Light,
        DisplayFont = "./Assets/Fonts/#Barlow Condensed, Bahnschrift SemiBold SemiCondensed, Bahnschrift SemiCondensed, Arial Narrow, Segoe UI",
        UiFont = "./Assets/Fonts/#Barlow, Segoe UI Variable Text, Segoe UI",
        MonoFont = "./Assets/Fonts/#Barlow Condensed, Bahnschrift SemiCondensed, Arial Narrow, Segoe UI",
        HeadingWeight = FontWeights.ExtraBold, HeadingStyle = FontStyles.Italic, MonoLabels = true,
        Radius = 0, ChipRadius = 0,
        CardEdge = new Thickness(1), LogoEdge = new Thickness(0),
        RacingDecor = true, FocusOffset = 2,
        CornerGlow = true, StepsClimb = false, Track = TrackShape.Corner,
        WindowIcon = new Uri("pack://application:,,,/Assets/Themes/GoodieBag-Grr.ico"),
        Link = new ThemeLink(new Uri("pack://application:,,,/Assets/GrrLogo.png"), GrrLeagues.Site, "GRR Website", "Take me to the GRR website") { Name = "GRR theme" },
    };

    public static readonly IReadOnlyDictionary<string, Color> Palette = new Dictionary<string, Color>
    {
        ["Bg"] = Hex("#0B100C"), ["Panel"] = Hex("#0E140F"), ["SurfaceAlt"] = Hex("#151D15"), ["Surface"] = Hex("#111811"),
        ["TileRaised"] = Hex("#1A251A"), ["Hover"] = Hex("#1F2B1F"), ["HoverRaised"] = Hex("#243224"),
        ["TileHighlight"] = Hex("#263329"), ["TileEdge"] = Hex("#263329"),
        ["Line"] = Hex("#1E2820"), ["LineStrong"] = Hex("#4A5A4D"),
        ["Heading"] = Hex("#F4F6F4"), ["Text"] = Hex("#E8ECE8"), ["TextMuted"] = Hex("#98A399"), ["TextFaint"] = Hex("#839085"),
        ["Accent"] = Hex("#75FF00"), ["AccentHover"] = Hex("#67DE0C"), ["AccentPressed"] = Hex("#44BD00"), ["AccentEdge"] = Hex("#75FF00"),
        ["OnAccent"] = Hex("#071000"), ["AccentSoft"] = Hex("#192A10"), ["AccentSoftHover"] = Hex("#1D340F"), ["AccentText"] = Hex("#8CFF33"),
        ["ButtonEdge"] = Hex("#263329"),
        ["Sage"] = Hex("#5ED600"), ["SageSoft"] = Hex("#192A10"), ["SageDeep"] = Hex("#8CFF33"),
        ["Warn"] = Hex("#E8B931"), ["WarnSoft"] = Hex("#2B2B15"), ["WarnDeep"] = Hex("#F2D06B"),
        ["Danger"] = Hex("#F0524F"), ["DangerSoft"] = Hex("#2C1F18"), ["DangerDeep"] = Hex("#F79A98"),
        ["Gold"] = Hex("#E8B931"), ["GoldSoft"] = Hex("#2B2B15"), ["GoldDeep"] = Hex("#F2D06B"), ["GoldEdge"] = Hex("#E8B931"), ["OnGold"] = Hex("#071000"),
        ["Apricot"] = Hex("#E8B931"), ["ApricotSoft"] = Hex("#2B2B15"), ["ApricotDeep"] = Hex("#F2D06B"),
        ["Coral"] = Hex("#F0524F"), ["CoralSoft"] = Hex("#2C1F18"), ["CoralDeep"] = Hex("#F79A98"),
        ["Slate"] = Hex("#4CC9E0"), ["SlateSoft"] = Hex("#102026"), ["SlateDeep"] = Hex("#8FDFF0"),
        ["TopoLine"] = Hex("#3FD66E"),
        ["Plum"] = Hex("#8CFF33"), ["PlumSoft"] = Hex("#192A10"), ["PlumDeep"] = Hex("#8CFF33"),
        ["Knob"] = Hex("#8E988F"), ["SwitchTrack"] = Hex("#0B100C"), ["SwitchTrackHover"] = Hex("#162016"),
        ["MeterTrack"] = Hex("#1B241D"), ["GridLine"] = Hex("#1B241D"),
        ["Meter1"] = Hex("#75FF00"), ["Meter2"] = Hex("#75FF00"), ["Meter3"] = Hex("#75FF00"), ["Meter4"] = Hex("#E8B931"), ["Meter5"] = Hex("#F0524F"),
        ["ScrollThumbBrush"] = Hex("#263329"), ["ScrollThumbBrushHover"] = Hex("#34443A"), ["ScrollThumbBrushDrag"] = Hex("#4A5C4E"),
        ["TooltipBg"] = Hex("#162016"), ["TooltipText"] = Hex("#F4F6F4"), ["CloseHover"] = Hex("#F0524F"),
        ["Dim"] = Hex("#B3000000"), ["Shadow"] = Hex("#000000"),
        ["BtnBg"] = Hex("#162016"), ["BtnFg"] = Hex("#F4F6F4"), ["BtnHover"] = Hex("#1B261B"),
        ["PrimaryEdge"] = Hex("#75FF00"),
        ["ActiveModeBg"] = Hex("#75FF00"), ["ActiveModeFg"] = Hex("#071000"), ["ActiveModeEdge"] = Hex("#75FF00"), ["ActiveModeHover"] = Hex("#67DE0C"),
        ["SwitchOnKnob"] = Hex("#071000"), ["ChipBorder"] = Hex("#00000000"),
        ["InputEdge"] = Hex("#5C6E60"), ["InputEdgeHover"] = Hex("#7D8F80"), ["SwitchBorder"] = Hex("#5C6E60"),
        ["FocusEdge"] = Hex("#8CFF33"), ["FocusGap"] = Hex("#0B100C"),
        ["LogoBg"] = Hex("#75FF00"), ["LogoFg"] = Hex("#071000"), ["LogoEdge"] = Hex("#75FF00"),
        ["SidebarEdge"] = Hex("#1B241D"),
        ["BgGlow"] = Hex("#101C0B"),
        ["OnDanger"] = Hex("#1A0606"),
        ["EyebrowText"] = Hex("#6FB8C8"), ["SegmentBg"] = Hex("#151D15"), ["SegmentEdge"] = Hex("#2A372C"), ["QuietEdge"] = Hex("#34443A"),
    };

    private static readonly Dictionary<string, Dictionary<string, Color>> PodiumByTheme = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Light"] = new Dictionary<string, Color> { ["Podium1Bg"] = Hex("#F8E7A8"), ["Podium1Edge"] = Hex("#C9A227"), ["Podium1Fg"] = Hex("#6B5000"), ["Podium2Bg"] = Hex("#E6E8EC"), ["Podium2Edge"] = Hex("#9AA1AB"), ["Podium2Fg"] = Hex("#3E444D"), ["Podium3Bg"] = Hex("#F2D2B6"), ["Podium3Edge"] = Hex("#B0682E"), ["Podium3Fg"] = Hex("#6E3A12") },
        ["Dark"] = new Dictionary<string, Color> { ["Podium1Bg"] = Hex("#3A3115"), ["Podium1Edge"] = Hex("#D9AE35"), ["Podium1Fg"] = Hex("#F7D873"), ["Podium2Bg"] = Hex("#2B2F35"), ["Podium2Edge"] = Hex("#A7AEB8"), ["Podium2Fg"] = Hex("#E6E9ED"), ["Podium3Bg"] = Hex("#3A2416"), ["Podium3Edge"] = Hex("#C47A45"), ["Podium3Fg"] = Hex("#F0B88E") },
        ["CustomTheme"] = new Dictionary<string, Color> { ["Podium1Bg"] = Hex("#241C00"), ["Podium1Edge"] = Hex("#E0B020"), ["Podium1Fg"] = Hex("#FFD45A"), ["Podium2Bg"] = Hex("#18191B"), ["Podium2Edge"] = Hex("#B8BEC6"), ["Podium2Fg"] = Hex("#E8ECF0"), ["Podium3Bg"] = Hex("#26140A"), ["Podium3Edge"] = Hex("#D0773A"), ["Podium3Fg"] = Hex("#F5AA74") },
        ["Grr"] = new Dictionary<string, Color> { ["Podium1Bg"] = Hex("#2E2A0E"), ["Podium1Edge"] = Hex("#E8B931"), ["Podium1Fg"] = Hex("#F2D06B"), ["Podium2Bg"] = Hex("#20251F"), ["Podium2Edge"] = Hex("#B7C0B8"), ["Podium2Fg"] = Hex("#E8ECE8"), ["Podium3Bg"] = Hex("#2E1E12"), ["Podium3Edge"] = Hex("#C98450"), ["Podium3Fg"] = Hex("#F0B88E") },
    };

    public static IReadOnlyDictionary<string, Color> Podium(AppTheme theme, IReadOnlyDictionary<string, Color> colors)
    {
        if (PodiumByTheme.TryGetValue(theme.Id, out var known)) return known;
        var made = new Dictionary<string, Color>();
        foreach (var (place, family) in new[] { (1, "Gold"), (2, "Slate"), (3, "Apricot") })
        {
            made[$"Podium{place}Bg"] = colors[family + "Soft"];
            made[$"Podium{place}Edge"] = colors[family];
            made[$"Podium{place}Fg"] = colors[family + "Deep"];
        }
        return made;
    }
}

using System.Windows.Media;

namespace GoodieBag.Theme;

internal static class Palettes
{
    public static readonly IReadOnlyDictionary<string, Color> Light = new Dictionary<string, Color>
    {
        ["Bg"] = Hex("#D6CEC2"), ["Panel"] = Hex("#E4DDD2"), ["SurfaceAlt"] = Hex("#E8E2D8"), ["Surface"] = Hex("#F3EFE8"),
        ["TileRaised"] = Hex("#FAF7F2"), ["Hover"] = Hex("#E6E0D6"), ["HoverRaised"] = Hex("#EAE4DA"),
        ["TileHighlight"] = Hex("#FCFAF6"), ["TileEdge"] = Hex("#C3B7A7"),
        ["Line"] = Hex("#CCC2B4"), ["LineStrong"] = Hex("#A89A87"),
        ["Heading"] = Hex("#221C18"), ["Text"] = Hex("#2B241F"), ["TextMuted"] = Hex("#4F443A"), ["TextFaint"] = Hex("#665A4E"),
        ["SidebarBg"] = Hex("#E4DDD2"), ["TopoLine"] = Hex("#A89B88"),
        ["Tile2"] = Hex("#E2D9CF"), ["Tile4"] = Hex("#E0D3BE"), ["Tile8"] = Hex("#E2AABE"), ["Tile16"] = Hex("#BE688C"),
        ["Tile32"] = Hex("#D6887A"), ["Tile64"] = Hex("#DDAA55"), ["Tile128"] = Hex("#DDBE52"),
        ["Accent"] = Hex("#A8366B"), ["AccentHover"] = Hex("#93295B"), ["AccentPressed"] = Hex("#7D204D"), ["AccentEdge"] = Hex("#A8366B"),
        ["OnAccent"] = Hex("#FFFFFF"), ["AccentSoft"] = Hex("#EBCDDA"), ["AccentSoftHover"] = Hex("#E4C0D0"), ["AccentText"] = Hex("#8A2250"),
        ["ButtonEdge"] = Hex("#9A876E"),
        ["Sage"] = Hex("#5A8538"), ["SageSoft"] = Hex("#DAE0CD"), ["SageDeep"] = Hex("#426526"),
        ["Warn"] = Hex("#B57D18"), ["WarnSoft"] = Hex("#EADCC0"), ["WarnDeep"] = Hex("#82580C"),
        ["Danger"] = Hex("#BC342B"), ["DangerSoft"] = Hex("#EAD7D0"), ["DangerDeep"] = Hex("#8C231C"),
        ["Gold"] = Hex("#DDBE52"), ["GoldSoft"] = Hex("#EAE0C2"), ["GoldDeep"] = Hex("#775B0D"), ["GoldEdge"] = Hex("#BB9B34"), ["OnGold"] = Hex("#2A1F0E"),
        ["Apricot"] = Hex("#E5A870"), ["ApricotSoft"] = Hex("#EBD9C7"), ["ApricotDeep"] = Hex("#995623"),
        ["Coral"] = Hex("#CC6845"), ["CoralSoft"] = Hex("#EAD3C9"), ["CoralDeep"] = Hex("#99452A"),
        ["Slate"] = Hex("#2F7F93"), ["SlateSoft"] = Hex("#CFDCDA"), ["SlateDeep"] = Hex("#1E5F70"),
        ["Plum"] = Hex("#B08A4F"), ["PlumSoft"] = Hex("#E0D3BE"), ["PlumDeep"] = Hex("#70521F"),
        ["Knob"] = Hex("#F5F1EB"), ["SwitchTrack"] = Hex("#C9BDAC"), ["SwitchTrackHover"] = Hex("#BBAD9A"),
        ["MeterTrack"] = Hex("#DAD0C3"), ["Meter1"] = Hex("#CDB48A"), ["GridLine"] = Hex("#D4CABC"),
        ["ScrollThumbBrush"] = Hex("#C6BAA9"), ["ScrollThumbBrushHover"] = Hex("#B6A894"), ["ScrollThumbBrushDrag"] = Hex("#A4957F"),
        ["TooltipBg"] = Hex("#3E352E"), ["TooltipText"] = Hex("#EDE7DF"), ["CloseHover"] = Hex("#BC342B"),
        ["Dim"] = Hex("#663E352E"), ["Shadow"] = Hex("#6B5236"),
        ["FocusEdge"] = Hex("#1E6F82"), ["OnDanger"] = Hex("#FFFFFF"),
        ["InputEdgeHover"] = Hex("#6E9AA5"), ["EyebrowText"] = Hex("#33606A"), ["SegmentBg"] = Hex("#E8E1D7"), ["SegmentEdge"] = Hex("#C6BAA9"), ["QuietEdge"] = Hex("#B3A796"),
    };

    public static readonly IReadOnlyDictionary<string, Color> Dark = new Dictionary<string, Color>
    {
        ["Bg"] = Hex("#000000"), ["Panel"] = Hex("#0E0E0E"), ["Surface"] = Hex("#151515"), ["SurfaceAlt"] = Hex("#1B1B1B"),
        ["TileRaised"] = Hex("#222222"), ["Hover"] = Hex("#2A2A2A"), ["HoverRaised"] = Hex("#303030"),
        ["TileHighlight"] = Hex("#2A2A2A"), ["TileEdge"] = Hex("#3A3A3A"),
        ["Line"] = Hex("#2E2E2E"), ["LineStrong"] = Hex("#3C3C3C"),
        ["Heading"] = Hex("#E6E6E6"), ["Text"] = Hex("#D2D2D2"), ["TextMuted"] = Hex("#A0A0A0"), ["TextFaint"] = Hex("#8A8A8A"),
        ["Accent"] = Hex("#DC1A78"), ["AccentHover"] = Hex("#C4146A"), ["AccentPressed"] = Hex("#96104F"), ["AccentEdge"] = Hex("#F02D91"),
        ["OnAccent"] = Hex("#FFFFFF"), ["AccentSoft"] = Hex("#3D1C2E"), ["AccentSoftHover"] = Hex("#3C2230"), ["AccentText"] = Hex("#FF6EB4"),
        ["ButtonEdge"] = Hex("#3A3A3A"),
        ["Sage"] = Hex("#7FCB4F"), ["SageSoft"] = Hex("#13241A"), ["SageDeep"] = Hex("#9BDE68"),
        ["Warn"] = Hex("#E8AE3C"), ["WarnSoft"] = Hex("#2A2216"), ["WarnDeep"] = Hex("#F2C76E"),
        ["Danger"] = Hex("#F25555"), ["DangerSoft"] = Hex("#33171D"), ["DangerDeep"] = Hex("#FF9A9A"),
        ["Gold"] = Hex("#FFC08A"), ["GoldSoft"] = Hex("#2B241F"), ["GoldDeep"] = Hex("#FFD2AD"), ["GoldEdge"] = Hex("#D99A63"), ["OnGold"] = Hex("#1A120A"),
        ["Apricot"] = Hex("#FFC08A"), ["ApricotSoft"] = Hex("#2B241F"), ["ApricotDeep"] = Hex("#FFD2AD"),
        ["Coral"] = Hex("#F07A35"), ["CoralSoft"] = Hex("#2D1D19"), ["CoralDeep"] = Hex("#FF9A5C"),
        ["Slate"] = Hex("#49C8F5"), ["SlateSoft"] = Hex("#14303C"), ["SlateDeep"] = Hex("#8ADCFA"),
        ["Plum"] = Hex("#7E7E7E"), ["PlumSoft"] = Hex("#202020"), ["PlumDeep"] = Hex("#B8B8B8"),
        ["Knob"] = Hex("#F0F0F0"), ["SwitchTrack"] = Hex("#2E2E2E"), ["SwitchTrackHover"] = Hex("#393939"),
        ["MeterTrack"] = Hex("#242424"), ["GridLine"] = Hex("#1E1E1E"),
        ["Meter1"] = Hex("#DC1A78"), ["Meter2"] = Hex("#DC1A78"), ["Meter3"] = Hex("#DC1A78"), ["Meter4"] = Hex("#E8AE3C"), ["Meter5"] = Hex("#F25555"),
        ["ScrollThumbBrush"] = Hex("#3A3A3A"), ["ScrollThumbBrushHover"] = Hex("#4A4A4A"), ["ScrollThumbBrushDrag"] = Hex("#5A5A5A"),
        ["TooltipBg"] = Hex("#2A2A2A"), ["TooltipText"] = Hex("#F0F0F0"), ["CloseHover"] = Hex("#D93636"),
        ["Dim"] = Hex("#B3030307"), ["Shadow"] = Hex("#000000"),
        ["BtnBg"] = Hex("#222222"), ["BtnFg"] = Hex("#F0F0F0"), ["BtnHover"] = Hex("#303030"),
        ["PrimaryEdge"] = Hex("#F02D91"),
        ["ActiveModeBg"] = Hex("#DC1A78"), ["ActiveModeFg"] = Hex("#FFFFFF"), ["ActiveModeEdge"] = Hex("#F02D91"), ["ActiveModeHover"] = Hex("#C4146A"),
        ["LogoBg"] = Hex("#DC1A78"), ["LogoFg"] = Hex("#FFFFFF"), ["LogoEdge"] = Hex("#DC1A78"),
        ["SidebarEdge"] = Hex("#242424"),
        ["InputEdgeHover"] = Hex("#5A5A5A"), ["FocusEdge"] = Hex("#49C8F5"), ["OnDanger"] = Hex("#1A0808"),
        ["SegmentBg"] = Hex("#1B1B1B"), ["SegmentEdge"] = Hex("#333333"), ["QuietEdge"] = Hex("#3A3A3A"), ["EyebrowText"] = Hex("#7FBFD6"),
    };

    public static readonly IReadOnlyDictionary<string, Color> Custom = new Dictionary<string, Color>
    {
        ["Bg"] = Hex("#000000"), ["Panel"] = Hex("#000000"), ["SurfaceAlt"] = Hex("#100B0E"), ["Surface"] = Hex("#070507"),
        ["TileRaised"] = Hex("#161015"), ["Hover"] = Hex("#1C0E15"), ["HoverRaised"] = Hex("#241119"),
        ["TileHighlight"] = Hex("#291E24"), ["TileEdge"] = Hex("#291E24"),
        ["Line"] = Hex("#291E24"), ["LineStrong"] = Hex("#5E4654"),
        ["Heading"] = Hex("#F5ECEE"), ["Text"] = Hex("#EBDCE1"), ["TextMuted"] = Hex("#A7999F"), ["TextFaint"] = Hex("#84777D"),
        ["Accent"] = Hex("#DB156B"), ["AccentHover"] = Hex("#F0388A"), ["AccentPressed"] = Hex("#A80F52"), ["AccentEdge"] = Hex("#DB156B"),
        ["OnAccent"] = Hex("#FFF5F6"), ["AccentSoft"] = Hex("#240815"), ["AccentSoftHover"] = Hex("#310B1C"), ["AccentText"] = Hex("#FF6AA6"),
        ["ButtonEdge"] = Hex("#775869"),
        ["Sage"] = Hex("#3DDC84"), ["SageSoft"] = Hex("#062114"), ["SageDeep"] = Hex("#3DDC84"),
        ["Warn"] = Hex("#FFB224"), ["WarnSoft"] = Hex("#241800"), ["WarnDeep"] = Hex("#FFB224"),
        ["Danger"] = Hex("#FF4545"), ["DangerSoft"] = Hex("#2E0A0A"), ["DangerDeep"] = Hex("#FF7A7A"),
        ["Gold"] = Hex("#FFB224"), ["GoldSoft"] = Hex("#241800"), ["GoldDeep"] = Hex("#FFC24F"), ["GoldEdge"] = Hex("#CC8E1D"), ["OnGold"] = Hex("#1A1000"),
        ["Apricot"] = Hex("#FFB224"), ["ApricotSoft"] = Hex("#241800"), ["ApricotDeep"] = Hex("#FFC24F"),
        ["Coral"] = Hex("#FF6A3D"), ["CoralSoft"] = Hex("#2A0F07"), ["CoralDeep"] = Hex("#FF8C66"),
        ["Slate"] = Hex("#2EE6C8"), ["SlateSoft"] = Hex("#04211D"), ["SlateDeep"] = Hex("#5CF2D6"),
        ["Plum"] = Hex("#FF6A3D"), ["PlumSoft"] = Hex("#2A0F07"), ["PlumDeep"] = Hex("#FF8C66"),
        ["Knob"] = Hex("#725863"), ["SwitchTrack"] = Hex("#070507"), ["SwitchTrackHover"] = Hex("#150A0F"),
        ["MeterTrack"] = Hex("#150A0F"), ["GridLine"] = Hex("#130C10"),
        ["Meter1"] = Hex("#DB156B"), ["Meter2"] = Hex("#DB156B"), ["Meter3"] = Hex("#DB156B"), ["Meter4"] = Hex("#FFB224"), ["Meter5"] = Hex("#FF4545"),
        ["ScrollThumbBrush"] = Hex("#291E24"), ["ScrollThumbBrushHover"] = Hex("#48333E"), ["ScrollThumbBrushDrag"] = Hex("#A80F52"),
        ["TooltipBg"] = Hex("#150A0F"), ["TooltipText"] = Hex("#EBDCE1"), ["CloseHover"] = Hex("#E02E2E"),
        ["Dim"] = Hex("#CC000000"), ["Shadow"] = Hex("#000000"),
        ["BtnBg"] = Hex("#00000000"), ["BtnFg"] = Hex("#EBDCE1"), ["BtnHover"] = Hex("#150A0F"), ["BtnHoverFg"] = Hex("#F5ECEE"),
        ["PrimaryBg"] = Hex("#12DB156B"), ["PrimaryFg"] = Hex("#FF6AA6"), ["PrimaryEdge"] = Hex("#DB156B"),
        ["PrimaryHover"] = Hex("#DB156B"), ["PrimaryHoverFg"] = Hex("#FFF5F6"), ["PrimaryPressed"] = Hex("#A80F52"),
        ["ActiveModeBg"] = Hex("#1A00FFD0"), ["ActiveModeFg"] = Hex("#00FFD0"), ["ActiveModeEdge"] = Hex("#00FFD0"), ["ActiveModeHover"] = Hex("#3300FFD0"),
        ["SwitchBorder"] = Hex("#775869"), ["SwitchOn"] = Hex("#19040E"), ["SwitchOnBorder"] = Hex("#DB156B"), ["SwitchOnKnob"] = Hex("#DB156B"),
        ["ChipBorder"] = Hex("#291E24"), ["MetricText"] = Hex("#FF6AA6"),
        ["InputEdge"] = Hex("#775869"), ["InputEdgeHover"] = Hex("#9D7E8E"), ["FocusEdge"] = Hex("#00FFD0"),
        ["LogoBg"] = Hex("#000000"), ["LogoFg"] = Hex("#DB156B"), ["LogoEdge"] = Hex("#DB156B"),
        ["SidebarEdge"] = Hex("#291E24"),
        ["OnDanger"] = Hex("#1A0303"),
        ["EyebrowText"] = Hex("#4FC9B4"), ["SegmentBg"] = Hex("#100B0E"), ["SegmentEdge"] = Hex("#3A2C34"), ["QuietEdge"] = Hex("#4F3B46"),
    };

    public static readonly IReadOnlyDictionary<string, string> Derived = new Dictionary<string, string>
    {
        ["BtnBg"] = "TileRaised", ["BtnFg"] = "Heading", ["BtnEdge"] = "BtnOutline", ["BtnHover"] = "HoverRaised", ["BtnHoverFg"] = "BtnFg",
        ["PrimaryBg"] = "Accent", ["PrimaryFg"] = "OnAccent", ["PrimaryEdge"] = "AccentEdge", ["PrimaryHover"] = "AccentHover",
        ["PrimaryHoverFg"] = "PrimaryFg", ["PrimaryPressed"] = "AccentPressed",
        ["QuietBg"] = "SurfaceAlt", ["QuietFg"] = "Text", ["QuietEdge"] = "BtnEdge", ["QuietHover"] = "BtnHover", ["QuietEdgeHover"] = "LineStrong",
        ["ActiveModeBg"] = "Gold", ["ActiveModeFg"] = "OnGold", ["ActiveModeEdge"] = "GoldEdge", ["ActiveModeHover"] = "Tile64",
        ["SwitchBorder"] = "Transparent", ["SwitchOn"] = "Accent", ["SwitchOnBorder"] = "Transparent", ["SwitchOnKnob"] = "Knob",
        ["ChipBorder"] = "Transparent", ["MetricText"] = "Heading",
        ["Meter2"] = "Tile8", ["Meter3"] = "Tile16", ["Meter4"] = "Tile32", ["Meter5"] = "Tile64",
        ["LogoBg"] = "Gold", ["LogoFg"] = "OnGold", ["LogoEdge"] = "GoldEdge",
        ["SidebarEdge"] = "Transparent", ["SidebarBg"] = "Surface", ["TopoLine"] = "Slate", ["BgGlow"] = "Bg",
        ["InputEdge"] = "Line", ["InputEdgeHover"] = "LineStrong", ["FocusEdge"] = "Accent", ["FocusGap"] = "Transparent",
        ["SegmentBg"] = "Transparent", ["SegmentEdge"] = "BtnOutline", ["EyebrowText"] = "TextFaint",
    };

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static string ToHex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}

internal static class OverlayPresets
{
    public static Config.OverlayColors For(Config.OverlayColorPreset preset, Config.OverlayColors custom, string? set = null)
    {
        if ((preset.IsApp ? ThemeManager.Current.Id : preset.Theme) is not { } theme)
            return custom;
        if (preset.IsApp) set = null;
        lock (Cache)
        {
            var key = (theme, set ?? "");
            if (!Cache.TryGetValue(key, out var colors))
                Cache[key] = colors = FromPalette(ThemeManager.Resolve(Themes.ForSet(theme, set)));
            return Copy(colors);
        }
    }

    private static readonly Dictionary<(Config.AppTheme, string), Config.OverlayColors> Cache = new();

    public static void ClearCache()
    {
        lock (Cache) Cache.Clear();
    }

    private static Config.OverlayColors Copy(Config.OverlayColors c) => new()
    {
        Text = c.Text, Secondary = c.Secondary, Accent = c.Accent, Background = c.Background,
        Graph = c.Graph, Warning = c.Warning, Critical = c.Critical, Fps = c.Fps,
    };

    private static Config.OverlayColors FromPalette(IReadOnlyDictionary<string, Color> p) => new()
    {
        Text = Palettes.ToHex(p["Text"]),
        Secondary = Palettes.ToHex(p["TextMuted"]),
        Accent = Palettes.ToHex(p["AccentText"]),
        Background = Palettes.ToHex(p["Panel"]),
        Graph = Palettes.ToHex(p["Accent"]),
        Warning = Palettes.ToHex(p["Warn"]),
        Critical = Palettes.ToHex(p["Danger"]),
        Fps = Palettes.ToHex(p["SageDeep"]),
    };

    public static IReadOnlyList<(Config.OverlayColorPreset Preset, string Label)> All =>
        Themes.All.Select(t => (Config.OverlayLayout.For(t.Id), t.Name)).Append((Config.OverlayColorPreset.Custom, "Custom")).ToList();
}

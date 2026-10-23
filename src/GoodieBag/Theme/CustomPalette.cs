using System.Windows.Media;
using GoodieBag.Config;

namespace GoodieBag.Theme;

internal static class CustomPalette
{
    public static readonly (string Name, ThemeColors Colors)[] Presets =
    {
        ("Pastel", Make("#000000", "#6E6380", "#E6DEEE", "#F2A7C6", "#9EEBD4", "#8FE3B4", "#F6D28B", "#FF7070")),
        ("Sunrise", Make("#000000", "#7A5A4E", "#FFF1E6", "#FF7A45", "#FFC857", "#7BD88F", "#FFB224", "#FF4D4D")),
        ("Peach", Make("#000000", "#7D6258", "#FFF3EE", "#FFAB91", "#FFD7BA", "#9BE3B0", "#FFCF70", "#FF6B6B")),
        ("Midnight", Make("#03050C", "#4A5578", "#E8ECFA", "#5B7CFA", "#A5B8FF", "#4ADE80", "#FBBF24", "#F87171")),
        ("Ocean", Make("#000000", "#4A6A78", "#E6F6FA", "#00B4D8", "#90E0EF", "#2DD4A0", "#FFC145", "#FF5A5F")),
        ("Forest", Make("#000000", "#4F6B55", "#EAF5EC", "#3FB950", "#C3E88D", "#7EE787", "#E3B341", "#F85149")),
        ("Lavender", Make("#000000", "#6B6080", "#F3EEFC", "#B39DDB", "#F8BBD0", "#9DE5C0", "#F5D08A", "#FF7A85")),
        ("Ember", Make("#000000", "#7A4E42", "#FBECE6", "#FF5722", "#FFAB40", "#66D17A", "#FFC107", "#FF1744")),
        ("Glacier", Make("#000000", "#56707F", "#EEF7FB", "#4FC3F7", "#E1F5FE", "#69F0AE", "#FFD54F", "#FF6E6E")),
        ("Cherry Blossom", Make("#000000", "#7A5F6B", "#FFF0F5", "#FF8FB1", "#B5EAD7", "#8EDBA6", "#FFD27F", "#FF5C5C")),
        ("Synthwave", Make("#05010A", "#6A4C8C", "#F5E9FF", "#FF2E97", "#2DE2E6", "#45F0A0", "#FFD319", "#FF3864")),
        ("Mint", Make("#000000", "#557A6E", "#ECFBF5", "#3DDC97", "#A0F0FF", "#7CF5B8", "#FFC857", "#FF6060")),
        ("Gold Rush", Make("#000000", "#7A6A45", "#FFF8E7", "#FFC107", "#FFE082", "#8BE38B", "#FF9F1C", "#FF4D4D")),
    };

    public static ThemeColors? Preset(string? name) =>
        name is null ? null : Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Colors;

    private static ThemeColors Make(string bg, string edges, string text, string accent, string highlight, string success, string warning, string error) => new()
    {
        Background = bg, Edges = edges, Text = text, Accent = accent, Highlight = highlight, Success = success, Warning = warning, Error = error,
    };

    public static Color? Parse(string? hex)
    {
        string t = (hex ?? "").Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        if (t.Length != 7 || !t.Skip(1).All(Uri.IsHexDigit)) return null;
        return Color.FromRgb(Convert.ToByte(t[1..3], 16), Convert.ToByte(t[3..5], 16), Convert.ToByte(t[5..7], 16));
    }

    public static bool IsDark(ThemeColors seeds) => ThemeManager.Contrast(Pick(seeds.Background, ThemeColors.Default.Background), Colors.Black) < 4.5;

    private static Color Pick(string hex, string fallback) => Parse(hex) ?? Parse(fallback)!.Value;

    public static IReadOnlyDictionary<string, Color> Build(ThemeColors seeds)
    {
        if (seeds.IsDefault) return Palettes.Custom;
        var d = ThemeColors.Default;
        Color bg = Pick(seeds.Background, d.Background), edge = Pick(seeds.Edges, d.Edges), text = Pick(seeds.Text, d.Text),
            accent = Pick(seeds.Accent, d.Accent), hi = Pick(seeds.Highlight, d.Highlight), ok = Pick(seeds.Success, d.Success),
            warn = Pick(seeds.Warning, d.Warning), bad = Pick(seeds.Error, d.Error);
        static Color M(Color a, Color b, double t) => ThemeManager.Mix(a, b, t);
        static Color Light(Color c, double t) => ThemeManager.Mix(c, Colors.White, t);
        static Color Dark(Color c, double t) => ThemeManager.Mix(c, Colors.Black, t);
        static Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
        bool darkBg = ThemeManager.Contrast(bg, Colors.Black) < 4.5;

        var p = new Dictionary<string, Color>(Palettes.Custom);

        var tint = M(edge, accent, 0.5);
        p["Bg"] = p["Panel"] = p["LogoBg"] = bg;
        p["Surface"] = M(bg, edge, 0.06);
        p["SurfaceAlt"] = p["SegmentBg"] = M(bg, edge, 0.12);
        p["TileRaised"] = M(bg, edge, 0.17);
        p["Hover"] = p["MeterTrack"] = p["TooltipBg"] = p["BtnHover"] = p["SwitchTrackHover"] = M(bg, tint, 0.11);
        p["HoverRaised"] = M(bg, tint, 0.16);
        p["SwitchTrack"] = p["Surface"];
        p["Line"] = p["TileHighlight"] = p["TileEdge"] = p["ChipBorder"] = p["SidebarEdge"] = p["ScrollThumbBrush"] = M(bg, edge, 0.35);
        p["ButtonEdge"] = p["InputEdge"] = p["SwitchBorder"] = edge;
        p["LineStrong"] = M(bg, edge, 0.78);
        p["InputEdgeHover"] = M(edge, text, 0.25);
        p["ScrollThumbBrushHover"] = M(bg, edge, 0.6);
        p["Knob"] = M(bg, edge, 0.95);
        p["GridLine"] = M(bg, edge, 0.16);
        p["SegmentEdge"] = M(bg, edge, 0.5);
        p["QuietEdge"] = M(bg, edge, 0.65);

        p["Heading"] = p["BtnHoverFg"] = text;
        p["Text"] = p["TooltipText"] = p["BtnFg"] = M(bg, text, 0.93);
        p["TextMuted"] = M(bg, text, 0.66);
        p["TextFaint"] = M(bg, text, 0.53);

        var onAccent = ThemeManager.Contrast(Colors.White, accent) >= 3 ? Light(accent, 0.96) : Dark(accent, 0.85);
        var accentText = ReadableOn(accent, bg);
        var pressed = Dark(accent, 0.23);
        p["Accent"] = p["AccentEdge"] = p["PrimaryEdge"] = p["PrimaryHover"] = p["SwitchOnBorder"] = p["SwitchOnKnob"] = p["LogoFg"] = p["LogoEdge"] = accent;
        p["Meter1"] = p["Meter2"] = p["Meter3"] = accent;
        p["AccentHover"] = Light(accent, 0.18);
        p["AccentPressed"] = p["PrimaryPressed"] = p["ScrollThumbBrushDrag"] = pressed;
        p["OnAccent"] = p["PrimaryHoverFg"] = onAccent;
        p["AccentSoft"] = M(bg, accent, 0.165);
        p["AccentSoftHover"] = M(bg, accent, 0.225);
        p["AccentText"] = p["PrimaryFg"] = p["MetricText"] = accentText;
        p["PrimaryBg"] = A(accent, 0x12);
        p["SwitchOn"] = M(bg, accent, 0.115);

        p["ActiveModeBg"] = A(hi, 0x1A);
        p["ActiveModeHover"] = A(hi, 0x33);
        p["ActiveModeFg"] = p["ActiveModeEdge"] = p["FocusEdge"] = hi;

        p["Slate"] = hi;
        p["SlateSoft"] = M(bg, hi, 0.12);
        p["SlateDeep"] = darkBg ? Light(hi, 0.35) : Dark(hi, 0.45);
        p["EyebrowText"] = M(bg, hi, 0.78);

        p["Sage"] = p["SageDeep"] = ok;
        p["SageSoft"] = M(bg, ok, 0.1);
        p["Warn"] = p["WarnDeep"] = p["Gold"] = p["Apricot"] = p["Meter4"] = warn;
        p["WarnSoft"] = p["GoldSoft"] = p["ApricotSoft"] = M(bg, warn, 0.14);
        p["GoldDeep"] = p["ApricotDeep"] = Light(warn, 0.2);
        p["GoldEdge"] = Dark(warn, 0.2);
        p["OnGold"] = M(Colors.Black, warn, 0.1);
        p["Danger"] = p["Meter5"] = bad;
        p["DangerSoft"] = M(bg, bad, 0.16);
        p["DangerDeep"] = Light(bad, 0.3);
        p["CloseHover"] = Dark(bad, 0.12);
        p["OnDanger"] = M(Colors.Black, bad, 0.1);

        if (!darkBg)
        {
            p["AccentText"] = p["PrimaryFg"] = p["MetricText"] = ReadableOn(accent, bg);
            p["DangerDeep"] = Dark(bad, 0.3);
            p["GoldDeep"] = p["ApricotDeep"] = Dark(warn, 0.35);
            p["Dim"] = Color.FromArgb(0x80, 0, 0, 0);
        }
        return p;
    }

    private static Color ReadableOn(Color accent, Color bg)
    {
        bool darkBg = ThemeManager.Contrast(bg, Colors.Black) < 4.5;
        var start = darkBg ? Light(accent, 0.4) : Dark(accent, 0.2);
        for (int step = 0; step <= 20; step++)
        {
            var c = ThemeManager.Mix(start, darkBg ? Colors.White : Colors.Black, step * 0.05);
            if (ThemeManager.Contrast(c, bg) >= 4.5) return c;
        }
        return darkBg ? Colors.White : Colors.Black;

        static Color Light(Color c, double t) => ThemeManager.Mix(c, Colors.White, t);
        static Color Dark(Color c, double t) => ThemeManager.Mix(c, Colors.Black, t);
    }
}

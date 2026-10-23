using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GoodieBag.Config;

namespace GoodieBag.Theme;

internal static class ThemeManager
{
    public static ThemeSpec Current { get; private set; } = Themes.Light;

    public static bool Dark => Current.Dark;

    public static event Action? Changed;

    public static readonly List<Action<ThemeSpec, IReadOnlyDictionary<string, Color>, ResourceDictionary>> ExtraTokens = new();

    public static Dictionary<string, Color> Resolve(ThemeSpec spec)
    {
        var all = new Dictionary<string, Color>(spec.Colors) { ["Transparent"] = Colors.Transparent };
        all["BtnOutline"] = all[spec.Outline];
        for (bool progress = true; progress;)
        {
            progress = false;
            foreach (var (key, from) in Palettes.Derived)
                if (!all.ContainsKey(key) && all.TryGetValue(from, out var c))
                {
                    all[key] = c;
                    progress = true;
                }
        }
        all.Remove("Transparent");
        return all;
    }

    private static ResourceDictionary? _applied;

    public static void Apply(AppTheme theme)
    {
        var spec = Themes.For(theme);
        Current = spec;
        var res = new ResourceDictionary();
        var p = Resolve(spec);
        foreach (var (key, color) in p)
            res[key] = Freeze(new SolidColorBrush(color));

        res["DisplayFont"] = Family(spec.DisplayFont);
        res["UiFont"] = Family(spec.UiFont);
        res["MonoFont"] = Family(spec.MonoFont);
        res["MetricFont"] = Family(spec.MetricFont ?? spec.DisplayFont);
        res["LabelFont"] = Family(spec.MonoLabels ? spec.MonoFont : spec.UiFont);
        res["HeadingWeight"] = spec.HeadingWeight;
        res["HeadingStyle"] = spec.HeadingStyle;
        res["Caps"] = spec.Caps;
        _ui = _uiBold = _display = null;

        foreach (var (key, r) in new (string, double)[]
                 { ("R.Card", CardRadius), ("R.Inset", 10), ("R.Popup", 10), ("R.Control", 9), ("R.Input", 8), ("R.Tooltip", 8), ("R.Small", 7), ("R.Tiny", 5) })
            res[key] = Round(r);
        res["R.Switch"] = new CornerRadius(spec.SwitchRadius);
        res["R.Knob"] = new CornerRadius(spec.KnobRadius);
        res["R.Chip"] = new CornerRadius(spec.ChipRadius);

        res["CardEdge"] = res["LineStrong"];

        var tints = ToolTints(p, spec.Dark);
        for (int i = 0; i < tints.Count; i++)
        {
            res[$"Tool{i + 1}Soft"] = Freeze(new SolidColorBrush(tints[i].Soft));
            res[$"Tool{i + 1}Deep"] = Freeze(new SolidColorBrush(tints[i].Deep));
        }
        res["MeterFill"] = Freeze(new SolidColorBrush(MeterFillColor));
        res["Changed"] = res["Accent"];
        res["CardEdgeThickness"] = new Thickness(1);
        res["BtnEdgeThickness"] = spec.ButtonEdge;
        res["BtnEdgePressed"] = spec.ButtonEdgePressed;
        res["PressDrop"] = new Thickness(0, spec.PressDrop, 0, 0);
        res["LogoEdgeThickness"] = spec.LogoEdge;

        res["WindowBg"] = spec.CornerGlow
            ? Freeze(new RadialGradientBrush(p["BgGlow"], p["Bg"]) { Center = new Point(0, 0), GradientOrigin = new Point(0, 0), RadiusX = GlowRadius, RadiusY = GlowRadius })
            : res["Bg"];

        res["NavSelText"] = Freeze(new SolidColorBrush(NavSelection(p).Text));

        double glow = spec.GlowStrength;
        res["AccentGlow"] = spec.Glow ? Shadow(p["Accent"], 14, 0, 0.75 * glow) : null;
        res["ActiveGlow"] = spec.Glow ? Shadow(p["ActiveModeFg"], 16, 0, 0.7 * glow) : null;

        double gap = Math.Max(0, spec.FocusOffset);
        res["FocusRingGap"] = gap;
        res["FocusRingGapThickness"] = new Thickness(gap);
        res["FocusRingGapMargin"] = new Thickness(-gap);
        res["FocusRingOutset"] = gap + 2;
        res["FocusRingMargin"] = new Thickness(-(gap + 2));

        var track = TrackDecor.Brush(spec.Track, p, spec.TrackBoost);
        res["TrackBrush"] = track;
        res["TrackVisibility"] = track is null ? Visibility.Collapsed : Visibility.Visible;

        res["RacingVisibility"] = spec.RacingDecor ? Visibility.Visible : Visibility.Collapsed;
        res["RacingFade"] = Fade;
        res["RacingChecker"] = Checker(p["Accent"], 4);
        MeterHead = spec.RacingDecor ? Freeze(new SolidColorBrush(p["Heading"]) { Opacity = 0.85 }) : null;

        foreach (var extend in ExtraTokens)
        {
            try { extend(spec, p, res); }
            catch (Exception ex) { App.LogError(ex); }
        }

        var merged = Application.Current.Resources.MergedDictionaries;
        int at = _applied is null ? -1 : merged.IndexOf(_applied);
        if (at >= 0) merged[at] = res;
        else merged.Add(res);
        _applied = res;
        Changed?.Invoke();
    }

    private const double GlowRadius = 0.9;

    internal static (Color Tint, Color Mid, Color Text) NavSelection(IReadOnlyDictionary<string, Color> p)
    {
        var tint = p["AccentSoft"];
        var mid = Mix(p["Bg"], p["Accent"], 0.11);
        bool Reads(Color c) => Contrast(c, tint) >= 4.5 && Contrast(c, mid) >= 4.5;
        for (int step = 0; step <= 20; step++)
        {
            var c = Mix(p["AccentText"], p["Heading"], step * 0.05);
            if (Reads(c)) return (tint, mid, c);
        }
        return (tint, mid, p["Heading"]);
    }

    private static readonly Color[] ToolHues =
    {
        Color.FromRgb(0xE8, 0xA3, 0x3C), Color.FromRgb(0x4F, 0x8E, 0xF7), Color.FromRgb(0x9B, 0x6C, 0xF0),
        Color.FromRgb(0xE5, 0x48, 0x4D), Color.FromRgb(0x30, 0xA4, 0x6C), Color.FromRgb(0xF0, 0x74, 0x3A),
        Color.FromRgb(0x14, 0xB8, 0xC9),
    };

    internal static readonly Color MeterFillColor = Color.FromRgb(0x6F, 0xD9, 0xA3);

    internal static List<(Color Soft, Color Deep)> ToolTints(IReadOnlyDictionary<string, Color> p, bool dark) =>
        ToolHues.Select(hue => (Mix(p["Surface"], hue, 0.2), dark ? Mix(hue, Colors.White, 0.3) : Mix(hue, Colors.Black, 0.52))).ToList();

    internal static Color Mix(Color under, Color over, double amount)
    {
        byte M(byte a, byte b) => (byte)Math.Round(a + (b - a) * Math.Clamp(amount, 0, 1));
        return Color.FromRgb(M(under.R, over.R), M(under.G, over.G), M(under.B, over.B));
    }

    public static Brush? MeterHead { get; private set; }

    private static readonly LinearGradientBrush Fade = Freeze(new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), new Point(0, 0.5), new Point(1, 0.5)));

    private static DrawingBrush Checker(Color color, double cell)
    {
        var squares = new GeometryGroup();
        squares.Children.Add(new RectangleGeometry(new Rect(0, 0, cell, cell)));
        squares.Children.Add(new RectangleGeometry(new Rect(cell, cell, cell, cell)));
        return Freeze(Tiled(new Rect(0, 0, 2 * cell, 2 * cell), (color, squares)));
    }

    private static DrawingBrush Tiled(Rect tile, params (Color Color, Geometry Shape)[] parts)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(tile)));
        foreach (var (color, shape) in parts)
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, shape));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile, Viewport = tile, ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = tile, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill,
        };
    }

    private static DropShadowEffect Shadow(Color color, double blur, double depth, double opacity) =>
        Freeze(new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity, Color = color, RenderingBias = RenderingBias.Performance });

    private static double CardRadius => Current.CardChamfer > 0 ? 0 : 12;

    public static CornerRadius CardCorner => Round(CardRadius);

    public static CornerRadius Round(double radius) => new(Scaled(radius));

    public static CornerRadius Round(double topLeft, double topRight, double bottomRight, double bottomLeft) =>
        new(Scaled(topLeft), Scaled(topRight), Scaled(bottomRight), Scaled(bottomLeft));

    private static double Scaled(double r) => r <= 0 || Current.Radius <= 0 ? 0 : Math.Max(1, Math.Round(r * Current.Radius * 2) / 2);

    public static Brush RampBrush(double fraction) =>
        Brush(MeterKeys[fraction switch { < 0.40 => 0, < 0.60 => 1, < 0.75 => 2, < 0.90 => 3, _ => 4 }], Brushes.Orange);

    private static readonly string[] MeterKeys = { "Meter1", "Meter2", "Meter3", "Meter4", "Meter5" };

    public static Brush StepBrush(double position) => Current.StepsClimb ? RampBrush(position) : Brush("Accent", Brushes.Orange);

    private static Typeface? _ui, _uiBold, _display;

    public static Typeface UiFace(bool bold = false) => bold
        ? _uiBold ??= new(Family(Current.UiFont), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal)
        : _ui ??= new(Family(Current.UiFont), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static Typeface DisplayFace => _display ??= new(Family(Current.DisplayFont), Current.HeadingStyle, FontWeights.SemiBold, FontStretches.Normal);

    private static Uri? _fontBase;

    public static FontFamily Family(string fonts) =>
        fonts.Contains('#') ? new FontFamily(_fontBase ??= new Uri("pack://application:,,,/"), fonts) : Even(fonts);

    private const double ReferenceLineSpacing = 1.3301;

    private static readonly Dictionary<string, FontFamily> EvenFamilies = new();

    private static FontFamily Even(string fonts)
    {
        lock (EvenFamilies)
        {
            if (EvenFamilies.TryGetValue(fonts, out var cached)) return cached;
            double caps = new Typeface(fonts).TryGetGlyphTypeface(out var face) ? face.CapsHeight : 0.7;
            var family = new FontFamily { LineSpacing = ReferenceLineSpacing, Baseline = ReferenceLineSpacing / 2 + caps / 2 };
            family.FamilyMaps.Add(new FontFamilyMap { Target = fonts, Unicode = "0000-10ffff" });
            return EvenFamilies[fonts] = family;
        }
    }

    public static string Case(string text) => Current.Caps ? TextCase.Upper(text) : text;

    private static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    public static Brush Brush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    public static Color ColorOf(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) is SolidColorBrush s ? s.Color : fallback;
}

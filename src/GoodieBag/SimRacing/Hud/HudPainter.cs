using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using GoodieBag.Config;
using GoodieBag.SimRacing.Telemetry;
using GoodieBag.Theme;

namespace GoodieBag.SimRacing.Hud;

internal enum HAlign { Left, Center, Right }

internal enum VAlign { Top, Center, Bottom }

internal sealed class HudPalette
{
    public Color Bg, Surface, SurfaceAlt, Text, Heading, Muted, Faint, Line, LineStrong;
    public Color Accent, AccentText, AccentSoft, OnAccent;
    public Color Good, GoodText, GoodSoft, Warn, WarnText, WarnSoft, Danger, DangerText, DangerSoft, Gold, Slate, Plum;
    public bool Dark;
    public Color Player, PlayerText;
    public Color Lapping = Color.FromArgb(0xF8, 0x71, 0x71), Lapped = Color.FromArgb(0x60, 0xA5, 0xFA);
    public Color Fastest = Color.FromArgb(0xB0, 0x6B, 0xFF);
    public Color? MapTrack, MapEdge, MapPit, MapCars;

    public static readonly Color FlagGreen = Color.FromArgb(0x1E, 0xA8, 0x4A), FlagYellow = Color.FromArgb(0xFF, 0xD0, 0x1A),
        FlagBlue = Color.FromArgb(0x1F, 0x6F, 0xEB), FlagRed = Color.FromArgb(0xE0, 0x24, 0x24), FlagWhite = Color.FromArgb(0xF2, 0xF2, 0xF2),
        FlagBlack = Color.FromArgb(0x10, 0x10, 0x10), FlagOrange = Color.FromArgb(0xF2, 0x7A, 0x1A);

    public static HudPalette Of(HudAppearance look) => look.Colors == HudColors.Custom ? Custom(look.Custom) : Default;

    private static readonly Dictionary<string, HudPalette> CustomCache = new();

    public static HudPalette Custom(HudCustomColors c)
    {
        string key = string.Join("|", c.All().Select(e => e.Get()));
        lock (CustomCache)
        {
            if (CustomCache.TryGetValue(key, out var cached)) return cached;
            var d = new HudCustomColors();
            Color Hex(string v, string fallback)
            {
                try { return ColorTranslator.FromHtml(v); }
                catch (Exception) { return ColorTranslator.FromHtml(fallback); }
            }
            Color bg = Hex(c.Background, d.Background), surface = Hex(c.Surface, d.Surface), text = Hex(c.Text, d.Text), muted = Hex(c.Muted, d.Muted),
                accent = Hex(c.Accent, d.Accent), good = Hex(c.Good, d.Good), warn = Hex(c.Warn, d.Warn), danger = Hex(c.Danger, d.Danger),
                heading = Hex(c.Heading, d.Heading), lines = Hex(c.Lines, d.Lines);
            bool dark = Luminance(bg) < 0.35;
            Color Readable(Color colour) => Mix(colour, text, Math.Abs(Luminance(colour) - Luminance(surface)) < 0.25 ? 0.45 : 0.15);
            var p = new HudPalette
            {
                Dark = dark,
                Bg = bg, Surface = surface, SurfaceAlt = Mix(surface, bg, 0.5), Text = text, Heading = heading,
                Muted = muted, Faint = Mix(muted, bg, 0.3), Line = Mix(surface, lines, 0.6), LineStrong = lines,
                Accent = accent, AccentText = Readable(accent), AccentSoft = Mix(surface, accent, 0.22), OnAccent = On(accent),
                Good = good, GoodText = Readable(good), GoodSoft = Mix(surface, good, 0.2), Warn = warn, WarnText = Readable(warn), WarnSoft = Mix(surface, warn, 0.2),
                Danger = danger, DangerText = Readable(danger), DangerSoft = Mix(surface, danger, 0.2), Gold = warn, Slate = muted, Plum = Mix(accent, muted, 0.5),
                Player = Hex(c.YourRow, d.YourRow), PlayerText = Hex(c.YourRowText, d.YourRowText),
                Lapping = Hex(c.Lapping, d.Lapping), Lapped = Hex(c.Lapped, d.Lapped), Fastest = Hex(c.FastestLap, d.FastestLap),
                MapTrack = Hex(c.MapTrack, d.MapTrack), MapEdge = Hex(c.MapEdge, d.MapEdge), MapPit = Hex(c.MapPit, d.MapPit), MapCars = Hex(c.MapCars, d.MapCars),
            };
            CustomCache[key] = p;
            return p;
        }
    }

    private static HudPalette? _default;

    public static HudPalette Default => _default ??= new HudPalette
    {
        Dark = true,
        Bg = Hex(0x0F172A), Surface = Hex(0x1E293B), SurfaceAlt = Hex(0x273449), Text = Hex(0xF1F5F9), Heading = Hex(0xFFFFFF),
        Muted = Hex(0x94A3B8), Faint = Hex(0x64748B), Line = Hex(0x334155), LineStrong = Hex(0x475569),
        Accent = Hex(0x3B82F6), AccentText = Hex(0x60A5FA), AccentSoft = Hex(0x1E3A5F), OnAccent = Color.White,
        Good = Hex(0x22C55E), GoodText = Hex(0x4ADE80), GoodSoft = Hex(0x14532D), Warn = Hex(0xF59E0B), WarnText = Hex(0xFBBF24), WarnSoft = Hex(0x713F12),
        Danger = Hex(0xEF4444), DangerText = Hex(0xF87171), DangerSoft = Hex(0x7F1D1D), Gold = Hex(0xFACC15), Slate = Hex(0x64748B), Plum = Hex(0xA855F7),
        Player = Hex(0xEAB308), PlayerText = Hex(0xFCD34D),
    };

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    private static double Luminance(Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (int)Math.Round(a.A + (b.A - a.A) * t), (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    public static Color Alpha(Color c, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);

    public static Color On(Color bg) => 0.2126 * Lin(bg.R) + 0.7152 * Lin(bg.G) + 0.0722 * Lin(bg.B) > 0.22 ? Color.FromArgb(0x12, 0x12, 0x12) : Color.White;

    private static double Lin(int v) { double c = v / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
}

internal sealed class HudPainter : IDisposable
{
    private const int MaxCached = 256;
    private readonly Dictionary<int, SolidBrush> _brushes = new();
    private readonly Dictionary<(int, float), Pen> _pens = new();
    private readonly Dictionary<(float, bool), Font> _fonts = new();
    private readonly Dictionary<(Font, string), float> _widths = new();
    private readonly StringFormat[] _formats = new StringFormat[18];
    private GraphicsPath? _panelPath;
    private (SizeF Size, float Radius) _panelKey;
    private readonly Bitmap _measureBitmap = new(1, 1);
    private readonly Graphics _measure;
    private float _textScale = 1;

    public HudPainter()
    {
        _measure = Graphics.FromImage(_measureBitmap);
        _measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    public Graphics G { get; private set; } = null!;
    public HudPalette P { get; private set; } = HudPalette.Default;
    public HudAppearance Look { get; private set; } = new();
    public RectangleF Bounds { get; private set; }
    public float Density { get; private set; } = 1;
    public bool Simulated { get; set; }
    public bool ReducedMotion => Look.ReducedMotion;

    public List<(string Key, string Id, RectangleF Box)>? Items { get; set; }

    public void Item(string key, string id, RectangleF box) => Items?.Add((key, id, new RectangleF(box.X + _inset.X, box.Y + _inset.Y, box.Width, box.Height)));

    private PointF _inset;
    private RectangleF _outer;

    public void Inset(float left, float top, float right, float bottom)
    {
        if (left == 0 && top == 0 && right == 0 && bottom == 0) return;
        G.TranslateTransform(left, top);
        _inset = new PointF(left, top);
        _outer = Bounds;
        Bounds = new RectangleF(0, 0, Bounds.Width - left - right, Bounds.Height - top - bottom);
    }

    public RectangleF EndInset()
    {
        if (_inset != PointF.Empty)
        {
            G.TranslateTransform(-_inset.X, -_inset.Y);
            Bounds = _outer;
        }
        _inset = PointF.Empty;
        return Bounds;
    }

    public bool IsPreview { get; set; }

    public List<(string Key, string Id)>? Overflow { get; set; }

    public Font Tiny => Font(10f, false);
    public Font Label => LabelFont(10.5f);
    public Font Small => Font(11.5f, false);
    public Font SmallBold => Font(11.5f, true);
    public Font Body => Font(13.5f, false);
    public Font Bold => Font(13.5f, true);
    public Font Big => Font(20f, true);
    public Font Huge => Font(34f, true);

    public void Begin(Graphics g, float scale, SizeF layoutSize, HudPalette palette, HudAppearance look)
    {
        G = g;
        P = palette;
        Look = look;
        float textScale = look.FontScale / 100f;
        if (Math.Abs(textScale - _textScale) > 0.001f) { ClearFonts(); _textScale = textScale; }
        Density = look.Density switch { HudDensity.Compact => 0.8f, HudDensity.Expanded => 1.25f, _ => 1f };
        _scale = scale > 0 ? scale : 1;
        Background = null;
        g.ResetTransform();
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.ScaleTransform(scale, scale);
        Bounds = new RectangleF(0, 0, layoutSize.Width, layoutSize.Height);
        _inset = PointF.Empty;
        Items?.Clear();
        Overflow?.Clear();
    }

    private float _scale = 1;

    public int? Background { get; set; }

    public float Snap(float v) => MathF.Round(v * _scale) / _scale;

    public RectangleF Snap(RectangleF r)
    {
        float l = Snap(r.Left), t = Snap(r.Top), rt = Snap(r.Right), b = Snap(r.Bottom);
        return RectangleF.FromLTRB(l, t, Math.Max(rt, l + 1 / _scale), Math.Max(b, t + 1 / _scale));
    }

    private float SnapLine(float v, float width)
    {
        float w = MathF.Max(1, MathF.Round(width * _scale));
        float d = v * _scale;
        return (w % 2 == 1 ? MathF.Floor(d) + 0.5f : MathF.Round(d)) / _scale;
    }

    public Font BoldOf(Font font)
    {
        foreach (var (key, value) in _fonts)
            if (ReferenceEquals(value, font)) return key.Item1 > 0 ? Font(key.Item1, true) : font;
        return font;
    }

    public Font Font(float px, bool bold)
    {
        px = MathF.Round(px * 2) / 2;
        var key = (px, bold);
        if (_fonts.TryGetValue(key, out var f)) return f;
        f = new Font(bold ? Semibold : Regular, px * _textScale, FontStyle.Regular, GraphicsUnit.Pixel);
        _fonts[key] = f;
        return f;
    }

    private readonly Dictionary<Font, float> _lineHeights = new(), _textHeights = new();

    public float LineHeight(Font font) => _lineHeights.TryGetValue(font, out var h) ? h : _lineHeights[font] = font.Height;

    private float TextHeight(Font font) => _textHeights.TryGetValue(font, out var h) ? h : _textHeights[font] = font.GetHeight();

    private static readonly FontFamily Regular = new("Segoe UI"), Semibold = new("Segoe UI Semibold");

    private Font LabelFont(float px)
    {
        var key = (-px, false);
        if (_fonts.TryGetValue(key, out var f)) return f;
        f = new Font(Semibold, px * _textScale, FontStyle.Regular, GraphicsUnit.Pixel);
        _fonts[key] = f;
        return f;
    }

    public string LabelText(string label) => label.ToUpperInvariant();

    private readonly Dictionary<string, string> _caps = new();

    private string Caps(string label)
    {
        if (_caps.TryGetValue(label, out var c)) return c;
        if (_caps.Count >= 512) _caps.Clear();
        return _caps[label] = label.ToUpperInvariant();
    }

    private readonly Dictionary<Font, float> _ascents = new();

    public float Ascent(Font font)
    {
        if (_ascents.TryGetValue(font, out var a)) return a;
        var family = font.FontFamily;
        return _ascents[font] = font.Size * family.GetCellAscent(font.Style) / family.GetEmHeight(font.Style);
    }

    public SolidBrush Brush(Color c)
    {
        int argb = c.ToArgb();
        if (_brushes.TryGetValue(argb, out var b)) return b;
        if (_brushes.Count >= MaxCached) { foreach (var x in _brushes.Values) x.Dispose(); _brushes.Clear(); }
        return _brushes[argb] = new SolidBrush(c);
    }

    public Pen Pen(Color c, float width = 1)
    {
        var key = (c.ToArgb(), width);
        if (_pens.TryGetValue(key, out var p)) return p;
        if (_pens.Count >= MaxCached) { foreach (var x in _pens.Values) x.Dispose(); _pens.Clear(); }
        return _pens[key] = new Pen(c, width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
    }

    private StringFormat Format(HAlign h, VAlign v, bool ellipsis)
    {
        int i = (int)h * 6 + (int)v * 2 + (ellipsis ? 1 : 0);
        if (_formats[i] is { } f) return f;
        f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        f.Alignment = h switch { HAlign.Center => StringAlignment.Center, HAlign.Right => StringAlignment.Far, _ => StringAlignment.Near };
        f.LineAlignment = v switch { VAlign.Center => StringAlignment.Center, VAlign.Bottom => StringAlignment.Far, _ => StringAlignment.Near };
        f.Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None;
        return _formats[i] = f;
    }

    public void Text(string text, Font font, Color color, RectangleF box, HAlign h = HAlign.Left, VAlign v = VAlign.Center, bool ellipsis = true)
    {
        if (text.Length == 0 || box.Width <= 0) return;
        if (ellipsis && Measure(text, font) > box.Width + 0.5f) text = Trimmed(text, font, box.Width);
        if (text.Length == 0) return;
        float lh = TextHeight(font);
        float top = v switch { VAlign.Center => box.Y + (box.Height - lh) / 2, VAlign.Bottom => box.Bottom - lh, _ => box.Y };
        G.DrawString(text, font, Brush(color), new RectangleF(box.X, Snap(top), box.Width, lh + 2), Format(h, VAlign.Top, false));
    }

    private readonly Dictionary<(Font, string, int), string> _trimmed = new();

    private string Trimmed(string text, Font font, float width)
    {
        var key = (font, text, (int)width);
        if (_trimmed.TryGetValue(key, out var done)) return done;
        if (_trimmed.Count >= 512) _trimmed.Clear();
        int lo = 0, hi = text.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Measure(text[..mid].TrimEnd() + "…", font) <= width + 0.5f) lo = mid; else hi = mid - 1;
        }
        string fitted = lo > 0 ? text[..lo].TrimEnd() + "…" : Measure("…", font) <= width + 0.5f ? "…" : "";
        return _trimmed[key] = fitted;
    }

    public float Measure(string text, Font font)
    {
        if (text.Length == 0) return 0;
        var key = (font, text);
        if (_widths.TryGetValue(key, out var w)) return w;
        if (_widths.Count >= 2048) _widths.Clear();
        w = _measure.MeasureString(text, font, PointF.Empty, Format(HAlign.Left, VAlign.Top, false)).Width;
        return _widths[key] = w;
    }

    public void Panel() => Panel(Bounds);

    public void Panel(RectangleF r)
    {
        int alpha = PanelAlpha;
        float radius = Math.Min(Look.Corner, 4);
        r = Snap(r);
        if (_panelPath is null || _panelKey != (r.Size, radius))
        {
            _panelPath?.Dispose();
            _panelPath = Rounded(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), radius);
            _panelKey = (r.Size, radius);
        }
        if (alpha > 0) G.FillPath(Brush(HudPalette.Alpha(PanelColor, alpha)), _panelPath);
        if (Look.Outline)
        {
            G.DrawPath(Pen(HudPalette.Alpha(P.LineStrong, Math.Max(60, alpha * 2 / 3)), 1), _panelPath);
        }
        if (Simulated && MarkSimulated)
        {
            using var dashed = new Pen(HudPalette.Alpha(P.Warn, 170), 1) { DashStyle = DashStyle.Dash };
            G.DrawPath(dashed, _panelPath);
            var tag = new RectangleF(r.Right - 30, r.Y + 1.5f, 26, 9);
            Fill(tag, HudPalette.Alpha(P.Warn, 200), 2);
            Text("SIM", Font(7.5f, true), HudPalette.On(P.Warn), tag, HAlign.Center, VAlign.Center, false);
        }
    }

    public bool MarkSimulated { get; set; } = true;

    public void Fill(RectangleF r, Color c, float radius = 0)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        r = Snap(r);
        if (radius <= 0.5f) { G.FillRectangle(Brush(c), r); return; }
        var path = Shape(r.Size, radius);
        G.TranslateTransform(r.X, r.Y);
        G.FillPath(Brush(c), path);
        G.TranslateTransform(-r.X, -r.Y);
    }

    public void Outline(RectangleF r, Color c, float width = 1, float radius = 0)
    {
        r = RectangleF.FromLTRB(SnapLine(r.Left, width), SnapLine(r.Top, width), SnapLine(r.Right, width), SnapLine(r.Bottom, width));
        if (radius <= 0.5f) { G.DrawRectangle(Pen(c, width), r.X, r.Y, r.Width, r.Height); return; }
        var path = Shape(r.Size, radius);
        G.TranslateTransform(r.X, r.Y);
        G.DrawPath(Pen(c, width), path);
        G.TranslateTransform(-r.X, -r.Y);
    }

    private readonly Dictionary<(float, float, float), GraphicsPath> _shapes = new();

    private GraphicsPath Shape(SizeF size, float radius)
    {
        var key = (MathF.Round(size.Width, 1), MathF.Round(size.Height, 1), MathF.Round(radius, 1));
        if (_shapes.TryGetValue(key, out var path)) return path;
        if (_shapes.Count >= MaxCached) { foreach (var s in _shapes.Values) s.Dispose(); _shapes.Clear(); }
        return _shapes[key] = Rounded(new RectangleF(0, 0, key.Item1, key.Item2), Math.Min(key.Item3, Math.Min(key.Item1, key.Item2) / 2));
    }

    public void Line(float x1, float y1, float x2, float y2, Color c, float width = 1)
    {
        if (y1 == y2) y1 = y2 = SnapLine(y1, width);
        else if (x1 == x2) x1 = x2 = SnapLine(x1, width);
        G.DrawLine(Pen(c, width), x1, y1, x2, y2);
    }

    public void Dot(float cx, float cy, float r, Color c) => G.FillEllipse(Brush(c), cx - r, cy - r, r * 2, r * 2);

    public void Bar(RectangleF r, double value, Color fill, Color? track = null)
    {
        Fill(r, track ?? HudPalette.Alpha(P.LineStrong, 110), Math.Min(3, r.Height / 2));
        float w = (float)(Math.Clamp(value, 0, 1) * r.Width);
        if (w > 0.5f) Fill(new RectangleF(r.X, r.Y, w, r.Height), fill, Math.Min(3, r.Height / 2));
    }

    public void Pill(RectangleF r, string text, Color bg, Font font, Color? fg = null)
    {
        Fill(r, bg, Math.Min(4, r.Height / 2));
        Text(text, font, fg ?? HudPalette.On(bg), r, HAlign.Center, VAlign.Center);
    }

    public Color LabelColor => P.Muted;

    public void Stat(RectangleF r, string label, string value, Color? valueColor = null, Font? valueFont = null, HAlign align = HAlign.Left)
    {
        float lh = LineHeight(Label) + 1;
        Text(LabelText(label), Label, LabelColor, new RectangleF(r.X, r.Y, r.Width, lh), align, VAlign.Top);
        Text(value, valueFont ?? Bold, valueColor ?? P.Heading, new RectangleF(r.X, r.Y + lh, r.Width, r.Height - lh), align, VAlign.Center);
    }

    public Color PanelColor => P.Surface;

    public int PanelAlpha => (int)Math.Round((Background ?? Look.Background) * 2.55);

    public float RowGap => 0;

    public Color Raised(double step) => HudPalette.Mix(PanelColor, P.Dark ? Color.White : Color.Black, step);

    public Color Sunken(double step) => HudPalette.Mix(PanelColor, P.Dark ? Color.Black : P.Text, P.Dark ? step : step * 0.35);

    public Color Solid(Color c, int floor = 70) => HudPalette.Alpha(c, Math.Max(floor, PanelAlpha));

    public void Tile(RectangleF r, Color? tint = null)
    {
        var fill = tint is { } t ? HudPalette.Mix(Raised(P.Dark ? 0.05 : 0.04), t, 0.18) : Raised(P.Dark ? 0.05 : 0.04);
        Fill(r, Solid(fill), CornerRadius(4));
    }

    public void Box(RectangleF r)
    {
        Tile(r);
        Outline(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), HudPalette.Alpha(P.LineStrong, 170), 1, CornerRadius(4));
    }

    public float BoxPad => 3 * Density;

    public void BoxedStat(RectangleF r, string label, string value, Color? valueColor = null, Font? valueFont = null)
    {
        Box(r);
        var font = valueFont ?? Bold;
        var inner = r.Inflated(-6 * Density, -BoxPad);
        float h = Math.Min(inner.Height, ValueStatHeight(font));
        ValueStat(new RectangleF(inner.X, inner.Y + (inner.Height - h) / 2, inner.Width, h), label, value, valueColor, font, HAlign.Center);
    }

    public float BoxedStatHeight(Font? valueFont = null) => ValueStatHeight(valueFont ?? Bold) + BoxPad * 2;

    public float CornerRadius(float r) => r <= 2 ? 0 : 2;

    public void ValueStat(RectangleF r, string label, string value, Color? valueColor = null, Font? valueFont = null, HAlign align = HAlign.Left)
    {
        var font = valueFont ?? Big;
        float lh = LineHeight(Label) + 1;
        if (r.Height - lh < LineHeight(font) * 0.85f) font = r.Height - lh >= LineHeight(Big) * 0.85f ? Big : Bold;
        if (r.Height - lh < LineHeight(font) * 0.7f)
        {
            Text(value, font, valueColor ?? P.Heading, r, align, VAlign.Center, false);
            return;
        }
        float vh = Math.Min(LineHeight(font), r.Height - lh);
        Number(value, font, valueColor ?? P.Heading, new RectangleF(r.X, r.Y, r.Width, vh), align, VAlign.Bottom);
        Text(LabelText(label), Label, LabelColor, new RectangleF(r.X, r.Y + vh, r.Width, lh), align, VAlign.Top);
    }

    public void Number(string value, Font font, Color color, RectangleF box, HAlign align = HAlign.Left, VAlign valign = VAlign.Center)
    {
        int cut = UnitStart(value);
        if (cut < 0) { Text(value, font, color, box, align, valign, false); return; }
        Number(value[..cut].TrimEnd(), value[cut..].Trim(), font, color, box, align, valign);
    }

    public void Number(string number, string unit, Font font, Color color, RectangleF box, HAlign align = HAlign.Left, VAlign valign = VAlign.Center)
    {
        if (unit.Length == 0) { Text(number, font, color, box, align, valign, false); return; }
        string value = number + " " + unit;
        var unitFont = font.Size > Bold.Size * 1.2f ? SmallBold : Small;
        float nw = Measure(number, font), uw = Measure(unit, unitFont) + 2, total = nw + uw;
        float x = align switch { HAlign.Right => box.Right - total, HAlign.Center => box.X + (box.Width - total) / 2, _ => box.X };
        if (total > box.Width) { Text(value, font, color, box, align, valign, true); return; }
        Text(number, font, color, new RectangleF(x, box.Y, nw + 1, box.Height), HAlign.Left, valign, false);
        float drop = LineHeight(font) - LineHeight(unitFont);
        var ub = valign switch
        {
            VAlign.Top => new RectangleF(x + nw + 2, box.Y + drop * 0.78f, uw, LineHeight(unitFont)),
            VAlign.Bottom => new RectangleF(x + nw + 2, box.Bottom - LineHeight(unitFont) - drop * 0.22f, uw, LineHeight(unitFont)),
            _ => new RectangleF(x + nw + 2, box.Y + box.Height / 2 + LineHeight(font) / 2 - LineHeight(unitFont) - drop * 0.22f, uw, LineHeight(unitFont)),
        };
        Text(unit, unitFont, HudPalette.Mix(color, P.Muted, 0.35), ub, HAlign.Left, VAlign.Top, false);
    }

    public Font Fitting(string text, float width, float height, params Font[] fonts)
    {
        foreach (var f in fonts)
            if (Measure(text, f) <= width && LineHeight(f) <= height) return f;
        return fonts[^1];
    }

    public string Shortened(string label, Font font, float width)
    {
        if (Measure(label, font) <= width) return label;
        int paren = label.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? label[..paren] : label;
    }

    private static readonly string[] Units = { " L", " gal", " km/h", " mph", " kPa", " psi", " s", "°C", "°F", "%", " laps", " lap" };

    internal static int UnitStart(string value)
    {
        foreach (var u in Units)
            if (value.EndsWith(u, StringComparison.Ordinal) && value.Length > u.Length && char.IsDigit(value[value.Length - u.Length - 1]))
                return value.Length - u.Length;
        return -1;
    }

    public void Divider(float x, float top, float bottom) => Line(x, top, x, bottom, HudPalette.Alpha(P.LineStrong, 110), 1);

    public float ValueStatHeight(Font? valueFont = null) => LineHeight((valueFont ?? Big)) + LineHeight(Label) + 3;

    public void Strip(RectangleF r, IReadOnlyList<(string Label, string Value, Color? Color)> items, string? key = null, IReadOnlyList<string>? ids = null, int? opacity = null)
    {
        if (items.Count == 0) return;
        Fill(r, BandColor(opacity), CornerRadius(3));
        var inner = r.Inflated(-6 * Density, 0);
        var valueFont = SmallBold;
        var labelFont = LabelFont(11.5f);
        float baseline = r.Y + (r.Height - TextHeight(valueFont)) / 2 + Ascent(valueFont);
        float valueTop = baseline - Ascent(valueFont), labelTop = baseline - Ascent(labelFont);
        float[] widths = new float[items.Count];
        for (int i = 0; i < items.Count; i++)
            widths[i] = (items[i].Label.Length > 0 ? Measure(Caps(items[i].Label), labelFont) + 4 : 0) + Measure(items[i].Value, valueFont);
        var (shown, gap) = FitStrip(widths, inner.Width, 14 * Density);
        float x = inner.X;
        for (int i = 0; i < shown; i++)
        {
            var (label, value, color) = items[i];
            float lw = label.Length > 0 ? Measure(Caps(label), labelFont) + 4 : 0;
            if (lw > 0) Text(Caps(label), labelFont, LabelColor, new RectangleF(x, labelTop, lw, TextHeight(labelFont)), HAlign.Left, VAlign.Top, false);
            float room = (i == shown - 1 ? inner.Right - x : widths[i] + gap) - lw;
            Text(value, valueFont, color ?? P.Heading, new RectangleF(x + lw, valueTop, Math.Max(0, room), TextHeight(valueFont)), HAlign.Left, VAlign.Top);
            if (key is not null && ids is not null && i < ids.Count) Item(key, ids[i], new RectangleF(x - 3, r.Y, widths[i] + 6, r.Height));
            x += widths[i] + gap;
        }
        if (key is not null && ids is not null)
            for (int i = shown; i < items.Count && i < ids.Count; i++) Overflow?.Add((key, ids[i]));
    }

    internal static (int Shown, float Gap) FitStrip(IReadOnlyList<float> widths, float room, float minGap)
    {
        float used = 0, sum = 0;
        int shown = 0;
        foreach (float w in widths)
        {
            float need = used + (shown > 0 ? minGap : 0) + w;
            if (need > room + 0.5f) break;
            used = need;
            sum += w;
            shown++;
        }
        return (shown, shown > 1 ? (room - sum) / (shown - 1) : 0);
    }

    public float StripHeight => LineHeight(SmallBold) + 7 * Density;

    public Color BandColor(int? opacity = null) => HudPalette.Alpha(P.Bg, (int)Math.Round((opacity ?? 70) * 2.55));

    public RectangleF Header(string title, string? right = null, Color? rightColor = null)
    {
        float pad = 8 * Density, h = LineHeight(SmallBold) + 6 * Density;
        var r = new RectangleF(pad, pad * 0.6f, Bounds.Width - pad * 2, h);
        var (font, color) = (LabelFont(11f), P.Muted);
        string text = title.ToUpperInvariant();
        float titleW = Math.Min(Measure(text, font) + 4, r.Width * (right is null ? 1 : 0.62f));
        Text(text, font, color, new RectangleF(r.X, r.Y, titleW, r.Height), HAlign.Left, VAlign.Center);
        if (right is not null) Text(right, Small, rightColor ?? P.Muted, new RectangleF(r.X + titleW + 6, r.Y, r.Width - titleW - 6, r.Height), HAlign.Right, VAlign.Center);
        return new RectangleF(pad, r.Bottom + 2, Bounds.Width - pad * 2, Bounds.Height - r.Bottom - 2 - pad * 0.6f);
    }

    public RectangleF HeaderBand(string title, string? right = null)
    {
        float pad = 6 * Density;
        var band = new RectangleF(pad, pad, Bounds.Width - pad * 2, StripHeight);
        Fill(band, BandColor(), CornerRadius(3));
        var inner = band.Inflated(-7 * Density, 0);
        var (font, color) = (LabelFont(11f), P.Heading);
        string text = title.ToUpperInvariant();
        float titleW = Math.Min(Measure(text, font) + 4, inner.Width * (right is null ? 1 : 0.62f));
        Text(text, font, color, new RectangleF(inner.X, inner.Y, titleW, inner.Height), HAlign.Left, VAlign.Center);
        if (right is not null) Text(right, Small, P.Muted, new RectangleF(inner.X + titleW + 6, inner.Y, inner.Width - titleW - 6, inner.Height), HAlign.Right, VAlign.Center);
        return new RectangleF(pad, band.Bottom + 4 * Density, Bounds.Width - pad * 2, Bounds.Height - band.Bottom - 4 * Density - pad);
    }

    public void AccentRule(float x1, float x2, float y) => Line(x1, y, x2, y, HudPalette.Alpha(P.Accent, P.Dark ? 70 : 90), 1);

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0.5f) { path.AddRectangle(r); return path; }
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private SessionInfo? _classSession;
    private HudPalette? _classPalette;
    private readonly Dictionary<int, Color> _classColors = new();

    public Color ClassColor(SessionInfo s, int classId)
    {
        if (!ReferenceEquals(s, _classSession) || !ReferenceEquals(P, _classPalette))
        {
            _classColors.Clear();
            _classSession = s;
            _classPalette = P;
            var classes = s.Drivers.Where(d => d.Races).GroupBy(d => d.ClassId)
                .OrderBy(g => g.First().ClassEstLapTime ?? double.MaxValue).ToList();
            for (int i = 0; i < classes.Count; i++)
            {
                int color = classes[i].First().ClassColor;
                _classColors[classes[i].Key] = classes.Count <= 1 ? P.Accent
                    : color == 0 ? P.Slate
                    : Color.FromArgb(255, (color >> 16) & 0xFF, (color >> 8) & 0xFF, color & 0xFF);
            }
        }
        return _classColors.TryGetValue(classId, out var c) ? c : P.Faint;
    }

    public Color Readable(Color c)
    {
        var bg = PanelColor;
        for (double t = 0; t <= 1.0001; t += 0.1)
        {
            var m = HudPalette.Mix(c, P.Text, t);
            if (Contrast(m, bg) >= 3) return m;
        }
        return P.Text;
    }

    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Lin(int v) { double x = v / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }
        double la = L(a), lb = L(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private void ClearFonts()
    {
        foreach (var f in _fonts.Values) f.Dispose();
        _fonts.Clear();
        _lineHeights.Clear();
        _textHeights.Clear();
        _ascents.Clear();
        _widths.Clear();
    }

    public void Dispose()
    {
        ClearFonts();
        foreach (var b in _brushes.Values) b.Dispose();
        foreach (var p in _pens.Values) p.Dispose();
        foreach (var f in _formats) f?.Dispose();
        _panelPath?.Dispose();
        foreach (var s in _shapes.Values) s.Dispose();
        _measure.Dispose();
        _measureBitmap.Dispose();
    }
}

internal static class HudRect
{
    public static RectangleF Inflated(this RectangleF r, float dx, float dy) => RectangleF.FromLTRB(r.Left - dx, r.Top - dy, r.Right + dx, r.Bottom + dy);

    public static RectangleF Top(this RectangleF r, float h, out RectangleF rest)
    {
        h = Math.Min(h, r.Height);
        rest = new RectangleF(r.X, r.Y + h, r.Width, r.Height - h);
        return new RectangleF(r.X, r.Y, r.Width, h);
    }

    public static RectangleF Left(this RectangleF r, float w, out RectangleF rest)
    {
        w = Math.Min(w, r.Width);
        rest = new RectangleF(r.X + w, r.Y, r.Width - w, r.Height);
        return new RectangleF(r.X, r.Y, w, r.Height);
    }

    public static RectangleF Right(this RectangleF r, float w, out RectangleF rest)
    {
        w = Math.Min(w, r.Width);
        rest = new RectangleF(r.X, r.Y, r.Width - w, r.Height);
        return new RectangleF(r.Right - w, r.Y, w, r.Height);
    }
}

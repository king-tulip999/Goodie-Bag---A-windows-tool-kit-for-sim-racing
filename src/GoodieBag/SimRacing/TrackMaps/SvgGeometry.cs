using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GoodieBag.SimRacing.TrackMaps;

internal sealed record SvgFigure(PointF[] Points, bool Closed);

internal sealed record SvgShape(IReadOnlyList<SvgFigure> Figures, float StrokeWidth, string? Id)
{
    public bool Filled => StrokeWidth <= 0;
}

internal sealed record SvgText(string Text, PointF At, float Size);

internal sealed class SvgGeometry
{
    public RectangleF ViewBox { get; private init; }
    public List<SvgShape> Shapes { get; } = new();
    public List<SvgText> Texts { get; } = new();
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, Dictionary<string, string>> _css = new(StringComparer.Ordinal);
    private Dictionary<string, XElement> _byId = new(StringComparer.Ordinal);

    public static SvgGeometry Parse(string xml)
    {
        var doc = XDocument.Parse(xml.TrimStart('﻿'), LoadOptions.None);
        var root = doc.Root ?? throw new FormatException("No SVG element");
        if (root.Name.LocalName != "svg") throw new FormatException("Not an SVG file");
        var g = new SvgGeometry { ViewBox = ParseViewBox(root) };
        foreach (var a in root.Attributes()) g.Attributes[a.Name.LocalName] = a.Value;
        foreach (var style in root.Descendants().Where(e => e.Name.LocalName == "style")) g.ReadCss(style.Value);
        foreach (var e in root.Descendants()) if (e.Attribute("id")?.Value is { } id) g._byId[id] = e;
        g.Walk(root, Matrix3x2.Identity, new Dictionary<string, string>(), 0);
        return g;
    }

    private static RectangleF ParseViewBox(XElement root)
    {
        var n = Numbers(root.Attribute("viewBox")?.Value ?? "");
        if (n.Count == 4) return new RectangleF((float)n[0], (float)n[1], (float)n[2], (float)n[3]);
        float w = (float)(Num(root.Attribute("width")?.Value) ?? 0), h = (float)(Num(root.Attribute("height")?.Value) ?? 0);
        return new RectangleF(0, 0, w, h);
    }

    private void ReadCss(string css)
    {
        foreach (Match rule in Regex.Matches(Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline), @"([^{}]+)\{([^}]*)\}"))
        {
            var props = Declarations(rule.Groups[2].Value);
            foreach (var selector in rule.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!selector.StartsWith('.')) continue;
                string cls = selector[1..];
                if (!_css.TryGetValue(cls, out var into)) _css[cls] = into = new(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in props) into[k] = v;
            }
        }
    }

    private static Dictionary<string, string> Declarations(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = part.IndexOf(':');
            if (colon > 0) d[part[..colon].Trim()] = part[(colon + 1)..].Trim();
        }
        return d;
    }

    private static readonly string[] Inherited = { "fill", "stroke", "stroke-width", "font-size", "display", "visibility" };

    private Dictionary<string, string> StyleOf(XElement e, Dictionary<string, string> parent)
    {
        var s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in Inherited) if (parent.TryGetValue(k, out var v)) s[k] = v;
        foreach (var k in Inherited) if (e.Attribute(k)?.Value is { } v) s[k] = v;
        foreach (var cls in (e.Attribute("class")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (_css.TryGetValue(cls, out var props)) foreach (var (k, v) in props) s[k] = v;
        if (e.Attribute("style")?.Value is { } style) foreach (var (k, v) in Declarations(style)) s[k] = v;
        return s;
    }

    private void Walk(XElement e, Matrix3x2 m, Dictionary<string, string> inherited, int depth)
    {
        if (depth > 32) return;
        foreach (var child in e.Elements())
        {
            string name = child.Name.LocalName;
            if (name is "defs" or "symbol" or "clipPath" or "mask" or "title" or "desc" or "style" or "metadata" or "linearGradient" or "radialGradient" or "pattern") continue;
            var style = StyleOf(child, inherited);
            if (style.TryGetValue("display", out var display) && display == "none") continue;
            var cm = Transform(child.Attribute("transform")?.Value) * m;
            switch (name)
            {
                case "g":
                case "a":
                    Walk(child, cm, style, depth + 1);
                    break;
                case "use":
                    Use(child, cm, style, depth);
                    break;
                case "text":
                    Text(child, cm, style);
                    break;
                default:
                    if (Figures(child) is { Count: > 0 } figures) Add(figures, cm, style, child.Attribute("id")?.Value);
                    break;
            }
        }
    }

    private void Use(XElement use, Matrix3x2 m, Dictionary<string, string> style, int depth)
    {
        string? href = (use.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value)?.TrimStart('#');
        if (href is null || !_byId.TryGetValue(href, out var target)) return;
        float x = (float)(Num(use.Attribute("x")?.Value) ?? 0), y = (float)(Num(use.Attribute("y")?.Value) ?? 0);
        var at = Matrix3x2.CreateTranslation(x, y) * m;
        if (target.Name.LocalName == "symbol")
        {
            var inner = at;
            var vb = Numbers(target.Attribute("viewBox")?.Value ?? "");
            if (vb.Count == 4 && vb[2] > 0 && vb[3] > 0)
            {
                double w = Num(use.Attribute("width")?.Value) ?? vb[2], h = Num(use.Attribute("height")?.Value) ?? vb[3];
                inner = Matrix3x2.CreateTranslation((float)-vb[0], (float)-vb[1]) * Matrix3x2.CreateScale((float)(w / vb[2]), (float)(h / vb[3])) * at;
            }
            Walk(target, inner, StyleOf(target, style), depth + 1);
            return;
        }
        var holder = new XElement("g", target);
        Walk(holder, at, style, depth + 1);
    }

    private void Text(XElement text, Matrix3x2 m, Dictionary<string, string> style)
    {
        string content = string.Concat(text.DescendantNodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (content.Length == 0) return;
        var first = text.Descendants().FirstOrDefault(d => d.Name.LocalName == "tspan" && d.Attribute("x") is not null);
        float x = (float)(Num(text.Attribute("x")?.Value) ?? Num(first?.Attribute("x")?.Value) ?? 0);
        float y = (float)(Num(text.Attribute("y")?.Value) ?? Num(first?.Attribute("y")?.Value) ?? 0);
        float size = (float)(Num(style.GetValueOrDefault("font-size")) ?? 16);
        var p = Vector2.Transform(new Vector2(x, y), m);
        Texts.Add(new SvgText(Regex.Replace(content, @"\s+", " "), new PointF(p.X, p.Y), size * Scale(m)));
    }

    private void Add(List<SvgFigure> figures, Matrix3x2 m, Dictionary<string, string> style, string? id)
    {
        string fill = style.GetValueOrDefault("fill") ?? "#000";
        string stroke = style.GetValueOrDefault("stroke") ?? "none";
        bool filled = fill != "none" && fill != "transparent";
        bool stroked = stroke != "none" && stroke != "transparent";
        if (!filled && !stroked) return;
        float width = filled ? 0 : (float)(Num(style.GetValueOrDefault("stroke-width")) ?? 1) * Scale(m);
        if (!filled && width <= 0) return;
        var moved = figures.Select(f => new SvgFigure(f.Points.Select(p => { var v = Vector2.Transform(new Vector2(p.X, p.Y), m); return new PointF(v.X, v.Y); }).ToArray(), f.Closed)).ToList();
        Shapes.Add(new SvgShape(moved, width, id));
    }

    private static float Scale(Matrix3x2 m) => MathF.Sqrt(MathF.Abs(m.GetDeterminant()));

    private static List<SvgFigure>? Figures(XElement e)
    {
        double A(string name) => Num(e.Attribute(name)?.Value) ?? 0;
        switch (e.Name.LocalName)
        {
            case "path":
                return PathFigures(e.Attribute("d")?.Value ?? "");
            case "rect":
                double x = A("x"), y = A("y"), w = A("width"), h = A("height");
                if (w <= 0 || h <= 0) return null;
                return new() { new SvgFigure(new[] { P(x, y), P(x + w, y), P(x + w, y + h), P(x, y + h) }, true) };
            case "polygon":
            case "polyline":
                var n = Numbers(e.Attribute("points")?.Value ?? "");
                var pts = new PointF[n.Count / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = P(n[i * 2], n[i * 2 + 1]);
                return pts.Length < 2 ? null : new() { new SvgFigure(pts, e.Name.LocalName == "polygon") };
            case "line":
                return new() { new SvgFigure(new[] { P(A("x1"), A("y1")), P(A("x2"), A("y2")) }, false) };
            case "circle":
                return Ellipse(A("cx"), A("cy"), A("r"), A("r"));
            case "ellipse":
                return Ellipse(A("cx"), A("cy"), A("rx"), A("ry"));
        }
        return null;
    }

    private static List<SvgFigure>? Ellipse(double cx, double cy, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0) return null;
        const int n = 32;
        var pts = new PointF[n];
        for (int i = 0; i < n; i++) pts[i] = P(cx + rx * Math.Cos(i * 2 * Math.PI / n), cy + ry * Math.Sin(i * 2 * Math.PI / n));
        return new() { new SvgFigure(pts, true) };
    }

    private static PointF P(double x, double y) => new((float)x, (float)y);

    public static Matrix3x2 Transform(string? text)
    {
        var m = Matrix3x2.Identity;
        if (string.IsNullOrWhiteSpace(text)) return m;
        foreach (Match t in Regex.Matches(text, @"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)"))
        {
            var n = Numbers(t.Groups[2].Value);
            float N(int i, float fallback = 0) => i < n.Count ? (float)n[i] : fallback;
            Matrix3x2 step = t.Groups[1].Value switch
            {
                "matrix" when n.Count >= 6 => new Matrix3x2(N(0), N(1), N(2), N(3), N(4), N(5)),
                "translate" => Matrix3x2.CreateTranslation(N(0), N(1)),
                "scale" => Matrix3x2.CreateScale(N(0, 1), n.Count > 1 ? N(1) : N(0, 1)),
                "rotate" => n.Count >= 3 ? Matrix3x2.CreateRotation(N(0) * MathF.PI / 180, new Vector2(N(1), N(2))) : Matrix3x2.CreateRotation(N(0) * MathF.PI / 180),
                "skewX" => Matrix3x2.CreateSkew(N(0) * MathF.PI / 180, 0),
                "skewY" => Matrix3x2.CreateSkew(0, N(0) * MathF.PI / 180),
                _ => Matrix3x2.Identity,
            };
            m = step * m;
        }
        return m;
    }

    private static double? Num(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s, @"^\s*([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)");
        return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static readonly Regex NumberToken = new(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

    public static List<double> Numbers(string s) =>
        NumberToken.Matches(s).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();

    public static List<SvgFigure> PathFigures(string d)
    {
        var figures = new List<SvgFigure>();
        var reader = new PathReader(d);
        var current = new List<PointF>();
        PointF pen = default, start = default, lastControl = default;
        char last = ' ';

        void Flush(bool closed)
        {
            if (current.Count >= 2) figures.Add(new SvgFigure(current.ToArray(), closed));
            current = new List<PointF>();
        }

        while (reader.Command() is { } cmd)
        {
            bool rel = char.IsLower(cmd);
            char up = char.ToUpperInvariant(cmd);
            if (up == 'Z')
            {
                Flush(true);
                pen = start;
                last = 'Z';
                continue;
            }
            bool first = true;
            do
            {
                PointF Read() { var x = reader.Number(); var y = reader.Number(); return rel ? new PointF(pen.X + x, pen.Y + y) : new PointF(x, y); }
                if (up != 'M' && current.Count == 0) current.Add(pen);
                switch (up)
                {
                    case 'M':
                        if (first)
                        {
                            Flush(false);
                            pen = start = Read();
                            current.Add(pen);
                        }
                        else { pen = Read(); current.Add(pen); }
                        break;
                    case 'L': pen = Read(); current.Add(pen); break;
                    case 'H': { float x = reader.Number(); pen = new PointF(rel ? pen.X + x : x, pen.Y); current.Add(pen); break; }
                    case 'V': { float y = reader.Number(); pen = new PointF(pen.X, rel ? pen.Y + y : y); current.Add(pen); break; }
                    case 'C':
                    {
                        var c1 = Read(); var c2 = Read(); var to = Read();
                        Cubic(current, pen, c1, c2, to);
                        lastControl = c2; pen = to; break;
                    }
                    case 'S':
                    {
                        var c1 = "CS".Contains(char.ToUpperInvariant(last)) ? new PointF(2 * pen.X - lastControl.X, 2 * pen.Y - lastControl.Y) : pen;
                        var c2 = Read(); var to = Read();
                        Cubic(current, pen, c1, c2, to);
                        lastControl = c2; pen = to; break;
                    }
                    case 'Q':
                    {
                        var c = Read(); var to = Read();
                        Quad(current, pen, c, to);
                        lastControl = c; pen = to; break;
                    }
                    case 'T':
                    {
                        var c = "QT".Contains(char.ToUpperInvariant(last)) ? new PointF(2 * pen.X - lastControl.X, 2 * pen.Y - lastControl.Y) : pen;
                        var to = Read();
                        Quad(current, pen, c, to);
                        lastControl = c; pen = to; break;
                    }
                    case 'A':
                    {
                        float rx = reader.Number(), ry = reader.Number(), rot = reader.Number();
                        bool large = reader.Flag(), sweep = reader.Flag();
                        var to = Read();
                        Arc(current, pen, rx, ry, rot, large, sweep, to);
                        pen = to; break;
                    }
                    default:
                        Flush(false);
                        return figures;
                }
                last = up == 'M' ? 'L' : cmd;
                first = false;
            } while (reader.HasNumber() && reader.Ok);
            if (!reader.Ok) break;
        }
        Flush(false);
        return figures;
    }

    private static int Steps(PointF a, PointF b, PointF c, PointF d)
    {
        double len = Dist(a, b) + Dist(b, c) + Dist(c, d);
        return Math.Clamp((int)Math.Ceiling(len / 3), 2, 32);
    }

    private static double Dist(PointF a, PointF b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static void Cubic(List<PointF> into, PointF p0, PointF p1, PointF p2, PointF p3)
    {
        int n = Steps(p0, p1, p2, p3);
        for (int i = 1; i <= n; i++)
        {
            float t = i / (float)n, u = 1 - t;
            into.Add(new PointF(u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y));
        }
    }

    private static void Quad(List<PointF> into, PointF p0, PointF p1, PointF p2)
    {
        int n = Steps(p0, p1, p1, p2);
        for (int i = 1; i <= n; i++)
        {
            float t = i / (float)n, u = 1 - t;
            into.Add(new PointF(u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X, u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y));
        }
    }

    private static void Arc(List<PointF> into, PointF from, double rx, double ry, double rotation, bool large, bool sweep, PointF to)
    {
        if (from == to) return;
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        if (rx < 1e-6 || ry < 1e-6) { into.Add(to); return; }
        double phi = rotation * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx = (from.X - to.X) / 2.0, dy = (from.Y - to.Y) / 2.0;
        double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
        double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1) { rx *= Math.Sqrt(lambda); ry *= Math.Sqrt(lambda); }
        double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1, den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        double k = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
        double cx1 = k * rx * y1 / ry, cy1 = -k * ry * x1 / rx;
        double cx = cos * cx1 - sin * cy1 + (from.X + to.X) / 2.0, cy = sin * cx1 + cos * cy1 + (from.Y + to.Y) / 2.0;
        double Angle(double ux, double uy, double vx, double vy) => Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        double t1 = Angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
        double dt = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
        if (!sweep && dt > 0) dt -= 2 * Math.PI;
        else if (sweep && dt < 0) dt += 2 * Math.PI;
        int n = Math.Clamp((int)Math.Ceiling(Math.Abs(dt) * Math.Max(rx, ry) / 3), 2, 48);
        for (int i = 1; i <= n; i++)
        {
            double t = t1 + dt * i / n;
            double ex = rx * Math.Cos(t), ey = ry * Math.Sin(t);
            into.Add(i == n ? to : new PointF((float)(cos * ex - sin * ey + cx), (float)(sin * ex + cos * ey + cy)));
        }
    }

    private sealed class PathReader
    {
        private readonly string _d;
        private int _i;
        public bool Ok { get; private set; } = true;

        public PathReader(string d) => _d = d;

        private void Skip()
        {
            while (_i < _d.Length && (char.IsWhiteSpace(_d[_i]) || _d[_i] == ',')) _i++;
        }

        public char? Command()
        {
            Skip();
            if (_i >= _d.Length || !Ok) return null;
            char c = _d[_i];
            if (!char.IsLetter(c)) { Ok = false; return null; }
            _i++;
            return c;
        }

        public bool HasNumber()
        {
            Skip();
            return _i < _d.Length && (char.IsDigit(_d[_i]) || _d[_i] is '-' or '+' or '.');
        }

        public float Number()
        {
            Skip();
            var m = NumberToken.Match(_d, _i);
            if (!m.Success || m.Index != _i) { Ok = false; return 0; }
            _i += m.Length;
            return float.Parse(m.Value, CultureInfo.InvariantCulture);
        }

        public bool Flag()
        {
            Skip();
            if (_i < _d.Length && _d[_i] is '0' or '1') return _d[_i++] == '1';
            Ok = false;
            return false;
        }
    }
}

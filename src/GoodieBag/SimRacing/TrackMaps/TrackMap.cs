using System.Drawing;
using System.Globalization;
using System.Text;

namespace GoodieBag.SimRacing.TrackMaps;

internal enum TrackMapSource { IracingSvg, Gpx, Learned }

internal sealed class TrackMap
{
    public required string Key { get; init; }
    public int? TrackId { get; init; }
    public string TrackName { get; init; } = "";
    public TrackMapSource Source { get; init; }
    public List<PointF[]> Surface { get; init; } = new();
    public required PointF[] Centerline { get; init; }
    public float Width { get; init; } = 10;
    public List<SvgShape> Pit { get; init; } = new();
    public (PointF A, PointF B)? StartLine { get; init; }
    public PointF[]? Arrow { get; init; }
    public List<SvgText> Turns { get; init; } = new();

    private double[]? _lengths;

    public RectangleF Bounds
    {
        get
        {
            var all = Surface.SelectMany(f => f).Concat(Centerline).Concat(Pit.SelectMany(s => s.Figures.SelectMany(f => f.Points))).Concat(Turns.Select(t => t.At)).ToList();
            if (all.Count == 0) return RectangleF.Empty;
            float pad = Width;
            return RectangleF.FromLTRB(all.Min(p => p.X) - pad, all.Min(p => p.Y) - pad, all.Max(p => p.X) + pad, all.Max(p => p.Y) + pad);
        }
    }

    public double Length => Lengths()[^1];

    private double[] Lengths()
    {
        if (_lengths is not null) return _lengths;
        var c = Centerline;
        var l = new double[c.Length + 1];
        for (int i = 1; i <= c.Length; i++) l[i] = l[i - 1] + Distance(c[i - 1], c[i % c.Length]);
        return _lengths = l;
    }

    public PointF At(double pct)
    {
        var c = Centerline;
        if (c.Length == 0) return PointF.Empty;
        var l = Lengths();
        pct -= Math.Floor(pct);
        double d = pct * l[^1];
        int lo = 0, hi = c.Length;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (l[mid] <= d) lo = mid; else hi = mid; }
        double seg = l[lo + 1] - l[lo];
        double t = seg <= 0 ? 0 : (d - l[lo]) / seg;
        var a = c[lo];
        var b = c[(lo + 1) % c.Length];
        return new PointF((float)(a.X + (b.X - a.X) * t), (float)(a.Y + (b.Y - a.Y) * t));
    }

    public PointF Heading(double pct)
    {
        double step = 2.0 / Math.Max(1, Length);
        var a = At(pct - step);
        var b = At(pct + step);
        double len = Distance(a, b);
        return len <= 0 ? new PointF(1, 0) : new PointF((float)((b.X - a.X) / len), (float)((b.Y - a.Y) / len));
    }

    public static double Distance(PointF a, PointF b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static string F(float v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Points(IEnumerable<PointF> pts) => string.Join(" ", pts.Select(p => F(p.X) + "," + F(p.Y)));

    private static string PathData(IEnumerable<SvgFigure> figures)
    {
        var sb = new StringBuilder();
        foreach (var f in figures)
        {
            if (f.Points.Length < 2) continue;
            sb.Append('M').Append(F(f.Points[0].X)).Append(',').Append(F(f.Points[0].Y));
            for (int i = 1; i < f.Points.Length; i++) sb.Append('L').Append(F(f.Points[i].X)).Append(',').Append(F(f.Points[i].Y));
            if (f.Closed) sb.Append('Z');
        }
        return sb.ToString();
    }

    private static string Escape(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    public string ToSvg()
    {
        var b = Bounds;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(b.X)} {F(b.Y)} {F(b.Width)} {F(b.Height)}\"");
        sb.Append($" data-key=\"{Escape(Key)}\" data-track-name=\"{Escape(TrackName)}\" data-source=\"{Source}\" data-width=\"{F(Width)}\"");
        if (TrackId is { } id) sb.Append($" data-track-id=\"{id}\"");
        sb.Append(">\n");
        if (Surface.Count > 0)
            sb.Append($"<path id=\"surface\" fill=\"#fff\" fill-rule=\"evenodd\" d=\"{PathData(Surface.Select(s => new SvgFigure(s, true)))}\"/>\n");
        foreach (var shape in Pit)
            sb.Append(shape.Filled
                ? $"<path id=\"pit\" fill=\"#888\" d=\"{PathData(shape.Figures)}\"/>\n"
                : $"<path id=\"pit\" fill=\"none\" stroke=\"#888\" stroke-width=\"{F(shape.StrokeWidth)}\" d=\"{PathData(shape.Figures)}\"/>\n");
        sb.Append($"<polyline id=\"centerline\" fill=\"none\" stroke=\"#f0f\" stroke-width=\"1\" points=\"{Points(Centerline)}\"/>\n");
        if (StartLine is { } sf)
            sb.Append($"<line id=\"start-finish\" stroke=\"#f00\" stroke-width=\"2\" x1=\"{F(sf.A.X)}\" y1=\"{F(sf.A.Y)}\" x2=\"{F(sf.B.X)}\" y2=\"{F(sf.B.Y)}\"/>\n");
        if (Arrow is { Length: >= 3 } arrow)
            sb.Append($"<polygon id=\"direction\" fill=\"#f00\" points=\"{Points(arrow)}\"/>\n");
        foreach (var t in Turns)
            sb.Append($"<text x=\"{F(t.At.X)}\" y=\"{F(t.At.Y)}\" font-size=\"{F(t.Size)}\" text-anchor=\"middle\" dominant-baseline=\"central\">{Escape(t.Text)}</text>\n");
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    public static TrackMap FromSvg(string svg)
    {
        var g = SvgGeometry.Parse(svg);
        string A(string name) => g.Attributes.GetValueOrDefault(name) ?? "";
        var centre = g.Shapes.FirstOrDefault(s => s.Id == "centerline")?.Figures.FirstOrDefault()?.Points ?? throw new FormatException("Track map without a centreline");
        var line = g.Shapes.FirstOrDefault(s => s.Id == "start-finish")?.Figures.FirstOrDefault()?.Points;
        return new TrackMap
        {
            Key = A("data-key"),
            TrackId = int.TryParse(A("data-track-id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null,
            TrackName = A("data-track-name"),
            Source = Enum.TryParse<TrackMapSource>(A("data-source"), out var src) ? src : TrackMapSource.IracingSvg,
            Width = float.TryParse(A("data-width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 10,
            Surface = g.Shapes.Where(s => s.Id == "surface").SelectMany(s => s.Figures).Select(f => f.Points).ToList(),
            Centerline = centre,
            Pit = g.Shapes.Where(s => s.Id == "pit").ToList(),
            StartLine = line is { Length: >= 2 } ? (line[0], line[1]) : null,
            Arrow = g.Shapes.FirstOrDefault(s => s.Id == "direction")?.Figures.FirstOrDefault()?.Points,
            Turns = g.Texts,
        };
    }

    public static double Perimeter(PointF[] pts, bool closed = true)
    {
        double sum = 0;
        for (int i = 1; i < pts.Length; i++) sum += Distance(pts[i - 1], pts[i]);
        if (closed && pts.Length > 2) sum += Distance(pts[^1], pts[0]);
        return sum;
    }

    public static PointF[] Resample(PointF[] pts, int n, bool closed = true)
    {
        if (pts.Length < 2) return pts;
        var l = new double[pts.Length + (closed ? 1 : 0)];
        for (int i = 1; i < l.Length; i++) l[i] = l[i - 1] + Distance(pts[i - 1], pts[i % pts.Length]);
        double total = l[^1];
        var result = new PointF[n];
        int seg = 0;
        for (int k = 0; k < n; k++)
        {
            double d = closed ? total * k / n : total * k / Math.Max(1, n - 1);
            while (seg < l.Length - 2 && l[seg + 1] < d) seg++;
            double len = l[seg + 1] - l[seg];
            double t = len <= 0 ? 0 : (d - l[seg]) / len;
            var a = pts[seg];
            var b = pts[(seg + 1) % pts.Length];
            result[k] = new PointF((float)(a.X + (b.X - a.X) * t), (float)(a.Y + (b.Y - a.Y) * t));
        }
        return result;
    }

    public static PointF[] Simplify(PointF[] pts, double tolerance)
    {
        if (pts.Length < 4) return pts;
        var keep = new bool[pts.Length];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Length - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double max = 0;
            int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                double d = SegmentDistance(pts[i], pts[a], pts[b]);
                if (d > max) { max = d; at = i; }
            }
            if (at >= 0 && max > tolerance)
            {
                keep[at] = true;
                stack.Push((a, at));
                stack.Push((at, b));
            }
        }
        return pts.Where((_, i) => keep[i]).ToArray();
    }

    public static double SegmentDistance(PointF p, PointF a, PointF b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
        double t = len <= 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len, 0, 1);
        double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
        return Math.Sqrt(x * x + y * y);
    }

    public static PointF[] StartAt(PointF[] loop, PointF start, PointF? direction)
    {
        int at = 0;
        double best = double.MaxValue;
        for (int i = 0; i < loop.Length; i++)
        {
            double d = Distance(loop[i], start);
            if (d < best) { best = d; at = i; }
        }
        var rotated = loop.Skip(at).Concat(loop.Take(at)).ToArray();
        if (direction is { } dir && rotated.Length > 6)
        {
            var ahead = rotated[3];
            var behind = rotated[^3];
            if ((ahead.X - behind.X) * dir.X + (ahead.Y - behind.Y) * dir.Y < 0)
                rotated = new[] { rotated[0] }.Concat(rotated.Skip(1).Reverse()).ToArray();
        }
        return rotated;
    }
}

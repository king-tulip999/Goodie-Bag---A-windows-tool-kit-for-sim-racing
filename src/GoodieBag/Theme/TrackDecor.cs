using System.Windows;
using System.Windows.Media;

namespace GoodieBag.Theme;

internal enum TrackShape { None, Corner, Hairpin, Esses, Chicane }

internal static class TrackDecor
{
    public const double Width = 560, Height = 360;

    private const double EdgeLevels = 14, KerbLevels = 18, LineLevels = 36, ApexLevels = 64;

    private const double Reach = 0.8;

    private const int Stations = 97;

    private const int Rounds = 6;

    private sealed record Spec(double HalfWidth, Point[] Waypoints, (double S, double D)[] Pins);

    private static Point[] P(params double[] xy)
    {
        var points = new Point[xy.Length / 2];
        for (int i = 0; i < points.Length; i++) points[i] = new Point(xy[2 * i], xy[2 * i + 1]);
        return points;
    }

    private static Spec? For(TrackShape shape) => shape switch
    {
        TrackShape.Corner => new(45,
            P(-120, 296, -88.4, 296, -56.8, 296, -25.2, 296, 6.4, 296, 38, 296, 69.6, 296, 101.1, 296, 132.7, 296, 164.3, 296,
              195.9, 296, 227.5, 296, 259.1, 296, 290.7, 295.8, 322, 292, 352.3, 283.2, 380.9, 269.8, 407, 252.1, 430, 230.5,
              449.3, 205.5, 464.5, 177.8, 475.1, 148.1, 480.9, 117.1, 482, 85.6, 482, 54, 482, 22.4, 482, -9.2, 482, -40.8, 482, -72.4,
              482, -104),
            new[] { (0.0, -1.0), (0.2, -1.0), (0.6, 1.0), (0.94, -1.0), (1.0, -1.0) }),
        TrackShape.Hairpin => new(38,
            P(470, 480, 470, 452.1, 470, 424.2, 470, 396.2, 470, 368.3, 470, 340.4, 470, 312.5, 470, 284.6, 470, 256.6, 470, 228.7,
              470, 200.8, 465.9, 173.3, 453.7, 148.3, 434.4, 128.3, 409.9, 115.1, 382.6, 110, 355, 113.5, 329.8, 125.3, 309.4, 144.2,
              295.7, 168.4, 287.8, 195.2, 280.1, 222, 272.4, 248.9, 264.7, 275.7, 257, 302.5, 249.3, 329.4, 241.6, 356.2, 233.9, 383.1,
              226.2, 409.9, 218.5, 436.7, 210.8, 463.6),
            new[] { (0.0, -1.0), (0.25, -1.0), (0.6, 1.0), (1.0, -1.0) }),
        TrackShape.Esses => new(32,
            P(-60, 330, -47.3, 306.7, -33.5, 286.2, -17.7, 270.9, 0.7, 262.2, 21.6, 260.4, 44.9, 264.5, 69.6, 272.5, 94.8, 281.8,
              119.3, 289.4, 142.2, 292.6, 162.8, 289.7, 180.7, 279.9, 196.1, 263.6, 209.6, 242.5, 222.3, 218.9, 235.1, 195.9,
              249.2, 176.1, 265.4, 161.8, 284.2, 154.3, 305.5, 153.6, 329.1, 158.5, 354, 166.9, 379.1, 176.1, 403.4, 183.1, 426, 185.5,
              446.1, 181.4, 463.6, 170.5, 478.6, 153.3, 492, 131.5, 504.6, 107.9, 517.5, 85.2, 531.9, 66.2, 548.5, 52.9, 567.7, 46.6,
              589.5, 46.9, 613.3, 52.6),
            new[] { (0.0, 0.0), (1.0, 0.0) }),
        TrackShape.Chicane => new(34,
            P(-120, 285, -91, 285, -62, 285, -33, 285, -4, 285, 25, 285, 54, 285, 83, 285, 112, 285, 141, 285, 170, 285,
              182.9, 283.3, 195, 278.3, 205.4, 270.4, 213.3, 260, 218.3, 247.9, 224, 226.7, 229, 214.6, 236.9, 204.3, 247.3, 196.3,
              259.3, 191.3, 272.3, 189.6, 294.3, 189.6, 316.3, 189.6, 329.2, 191.3, 341.3, 196.3, 351.6, 204.3, 359.6, 214.6,
              364.6, 226.7, 370.3, 247.9, 375.3, 260, 383.2, 270.4, 393.6, 278.3, 405.6, 283.3, 418.6, 285, 448.6, 285, 478.6, 285,
              508.6, 285, 538.6, 285, 568.6, 285, 598.6, 285, 628.6, 285, 658.6, 285, 688.6, 285),
            new[] { (0.0, -1.0), (1.0, -1.0) }),
        _ => null,
    };

    public static DrawingBrush? Brush(TrackShape shape, IReadOnlyDictionary<string, Color> palette, double boost = 1)
    {
        if (For(shape) is not { } spec) return null;
        var bg = palette["Bg"];
        boost = Math.Max(0, boost);
        var edge = Faint(palette["LineStrong"], bg, EdgeLevels * boost);
        var kerb = Faint(palette["LineStrong"], bg, KerbLevels * boost);
        var line = Faint(palette["Accent"], bg, LineLevels * boost);
        var apex = Faint(palette["AccentText"], bg, ApexLevels);

        Sample(spec.Waypoints, out var points, out var tangents, out var lengths);
        double total = lengths[^1];
        int n = points.Count;
        double hw = spec.HalfWidth, lim = Reach * hw;

        const int m = Stations;
        var centre = new Point[m];
        var heading = new Vector[m];
        var normal = new Vector[m];
        for (int j = 0, k = 1; j < m; j++)
        {
            double s = total * j / (m - 1);
            while (k < n - 1 && lengths[k] < s) k++;
            double u = lengths[k] > lengths[k - 1] ? Math.Clamp((s - lengths[k - 1]) / (lengths[k] - lengths[k - 1]), 0, 1) : 0;
            centre[j] = points[k - 1] + (points[k] - points[k - 1]) * u;
            var t = tangents[k - 1] + (tangents[k] - tangents[k - 1]) * u;
            if (t.Length > 0) t.Normalize();
            heading[j] = t;
            normal[j] = new Vector(t.Y, -t.X);
        }
        var pinned = new Dictionary<int, double>();
        foreach (var (s, d) in spec.Pins)
        {
            int j = (int)Math.Round(s * (m - 1));
            if (j == 0) { pinned[0] = d * lim; pinned[1] = d * lim; }
            else if (j == m - 1) { pinned[m - 2] = d * lim; pinned[m - 1] = d * lim; }
            else pinned[j] = d * lim;
        }
        var offsets = Solve(centre, normal, lim, pinned);

        var left = new Point[n];
        var right = new Point[n];
        var race = new Point[n];
        for (int i = 0; i < n; i++)
        {
            left[i] = Offset(points[i], tangents[i], hw);
            right[i] = Offset(points[i], tangents[i], -hw);
            race[i] = Offset(points[i], tangents[i], Spline(offsets, lengths[i] / total * (m - 1)));
        }

        double ds = total / (m - 1);
        var apexes = new List<(double At, double From, double To, double Side)>();
        for (int j = 0; j < m;)
        {
            double side = offsets[j] > 0 ? 1 : -1;
            var a = heading[Math.Max(j - 1, 0)];
            var b = heading[Math.Min(j + 1, m - 1)];
            double turn = -(a.X * b.Y - a.Y * b.X);
            if (Math.Abs(offsets[j]) >= lim - 0.01 && turn * side > 2 * ds / 400)
            {
                int k = j;
                while (k + 1 < m && Math.Abs(offsets[k + 1]) >= lim - 0.01 && offsets[k + 1] * side > 0) k++;
                apexes.Add(((j + k) / 2.0 * ds, j * ds, k * ds, side));
                j = k + 1;
            }
            else j++;
        }

        var group = new DrawingGroup();
        var box = new Rect(0, 0, Width, Height);
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, Freeze(new RectangleGeometry(box))));
        var edgePen = Freeze(new Pen(edge, 2));
        group.Children.Add(new GeometryDrawing(null, edgePen, Line(left)));
        group.Children.Add(new GeometryDrawing(null, edgePen, Line(right)));
        var kerbPen = Freeze(new Pen(kerb, 5) { DashStyle = Freeze(new DashStyle(new[] { 1.2, 1.2 }, 0)) });
        var linePen = Freeze(new Pen(line, 2) { DashStyle = Freeze(new DashStyle(new[] { 2.5, 2.0 }, 0)) });
        var apexPen = Freeze(new Pen(apex, 2));
        var rings = new List<Geometry>();
        foreach (var (at, start, end, side) in apexes)
        {
            int nearest = 0;
            for (int i = 1; i < n; i++)
                if (Math.Abs(lengths[i] - at) < Math.Abs(lengths[nearest] - at)) nearest = i;
            double lo = Math.Min(start, at - 36), hi = Math.Max(end, at + 36);
            var strip = new List<Point>();
            for (int i = 0; i < n; i++)
                if (lengths[i] >= lo && lengths[i] <= hi)
                    strip.Add(Offset(points[i], tangents[i], side * (hw - 3)));
            if (strip.Count > 1)
                group.Children.Add(new GeometryDrawing(null, kerbPen, Line(strip)));
            rings.Add(Freeze(new EllipseGeometry(race[nearest], 6, 6)));
        }
        group.Children.Add(new GeometryDrawing(null, linePen, Line(race)));
        foreach (var ring in rings)
            group.Children.Add(new GeometryDrawing(null, apexPen, ring));
        group.ClipGeometry = Freeze(new RectangleGeometry(box));
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.8, 0.9) };
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.15));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 1));
        group.OpacityMask = Freeze(fade);
        return Freeze(new DrawingBrush(Freeze(group))
        {
            Stretch = Stretch.Uniform, AlignmentX = AlignmentX.Right, AlignmentY = AlignmentY.Bottom,
            Viewbox = box, ViewboxUnits = BrushMappingMode.Absolute,
        });
    }

    private static double[] Solve(Point[] c, Vector[] nrm, double lim, Dictionary<int, double> pinned)
    {
        int m = c.Length;
        var d = new double[m];
        var state = new int[m];
        foreach (var (j, v) in pinned) { d[j] = v; state[j] = 2; }
        var nu = new Vector[m];
        var h = new double[m, 5];
        var g = new double[m];
        var free = new int[m];
        var a = new double[m, 5];
        var rhs = new double[m];
        var x = new double[m];
        for (int round = 0; round < Rounds; round++)
        {
            for (int k = 0; k < m; k++)
            {
                if (round == 0 || k == 0 || k == m - 1) { nu[k] = nrm[k]; continue; }
                var chord = (c[k + 1] + nrm[k + 1] * d[k + 1]) - (c[k - 1] + nrm[k - 1] * d[k - 1]);
                double len = chord.Length;
                if (len == 0) len = 1;
                nu[k] = new Vector(chord.Y / len, -chord.X / len);
            }
            Array.Clear(h);
            Array.Clear(g);
            for (int k = 1; k < m - 1; k++)
            {
                var second = (c[k - 1] - c[k]) + (c[k + 1] - c[k]);
                double r0 = nu[k] * second;
                for (int p = 0; p < 3; p++)
                {
                    int i = k - 1 + p;
                    double bi = (p == 1 ? -2.0 : 1.0) * (nu[k] * nrm[i]);
                    g[i] += bi * r0;
                    for (int q = 0; q < 3; q++)
                    {
                        int j = k - 1 + q;
                        double bj = (q == 1 ? -2.0 : 1.0) * (nu[k] * nrm[j]);
                        h[i, 2 + j - i] += bi * bj;
                    }
                }
            }
            for (int pass = 0; pass < 4 * m; pass++)
            {
                int f = 0;
                for (int i = 0; i < m; i++)
                    if (state[i] == 0) free[f++] = i;
                for (int r = 0; r < f; r++)
                {
                    int i = free[r];
                    for (int cc = 0; cc < 5; cc++) a[r, cc] = 0;
                    rhs[r] = -g[i];
                    for (int j = Math.Max(0, i - 2); j < Math.Min(m, i + 3); j++)
                        if (state[j] != 0) rhs[r] -= h[i, 2 + j - i] * d[j];
                    for (int cc = Math.Max(0, r - 2); cc < Math.Min(f, r + 3); cc++)
                        if (Math.Abs(free[cc] - i) <= 2) a[r, 2 + cc - r] = h[i, 2 + free[cc] - i];
                }
                for (int col = 0; col < f; col++)
                    for (int r = col + 1; r < Math.Min(f, col + 3); r++)
                    {
                        double factor = a[r, 2 + col - r] / a[col, 2];
                        if (factor == 0) continue;
                        for (int cc = col; cc < Math.Min(f, col + 3); cc++) a[r, 2 + cc - r] -= factor * a[col, 2 + cc - col];
                        rhs[r] -= factor * rhs[col];
                    }
                for (int col = f - 1; col >= 0; col--)
                {
                    double s = rhs[col];
                    for (int cc = col + 1; cc < Math.Min(f, col + 3); cc++) s -= a[col, 2 + cc - col] * x[cc];
                    x[col] = s / a[col, 2];
                }
                bool changed = false;
                for (int r = 0; r < f; r++)
                {
                    int i = free[r];
                    if (x[r] > lim) { d[i] = lim; state[i] = 1; changed = true; }
                    else if (x[r] < -lim) { d[i] = -lim; state[i] = -1; changed = true; }
                    else d[i] = x[r];
                }
                if (changed) continue;
                double worst = 1e-9;
                int release = -1;
                for (int i = 0; i < m; i++)
                {
                    if (state[i] != 1 && state[i] != -1) continue;
                    double grad = g[i];
                    for (int j = Math.Max(0, i - 2); j < Math.Min(m, i + 3); j++) grad += h[i, 2 + j - i] * d[j];
                    if (grad * state[i] > worst) { worst = grad * state[i]; release = i; }
                }
                if (release < 0) break;
                state[release] = 0;
            }
        }
        return d;
    }

    private static double Spline(double[] d, double u)
    {
        int m = d.Length;
        int i = Math.Max(0, Math.Min((int)u, m - 2));
        double t = u - i;
        double p0 = d[Math.Max(i - 1, 0)], p1 = d[i], p2 = d[i + 1], p3 = d[Math.Min(i + 2, m - 1)];
        return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
    }

    private static SolidColorBrush Faint(Color color, Color bg, double levels)
    {
        double delta = Math.Max(Math.Abs(color.R - bg.R), Math.Max(Math.Abs(color.G - bg.G), Math.Abs(color.B - bg.B)));
        double alpha = Math.Clamp(levels / Math.Max(1, delta), 0.04, 0.7);
        return Freeze(new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B)));
    }

    private static Point Offset(Point p, Vector t, double d) => new(p.X + t.Y * d, p.Y - t.X * d);

    private static void Sample(Point[] w, out List<Point> points, out List<Vector> tangents, out List<double> lengths)
    {
        const int steps = 48;
        points = new List<Point>();
        tangents = new List<Vector>();
        lengths = new List<double>();
        for (int s = 1; s < w.Length - 2; s++)
        {
            Point a = w[s], d = w[s + 1];
            var b = a + (w[s + 1] - w[s - 1]) / 6;
            var c = d - (w[s + 2] - w[s]) / 6;
            for (int k = s == 1 ? 0 : 1; k <= steps; k++)
            {
                double t = (double)k / steps, mt = 1 - t;
                var p = new Point(
                    mt * mt * mt * a.X + 3 * mt * mt * t * b.X + 3 * mt * t * t * c.X + t * t * t * d.X,
                    mt * mt * mt * a.Y + 3 * mt * mt * t * b.Y + 3 * mt * t * t * c.Y + t * t * t * d.Y);
                var v = 3 * mt * mt * (b - a) + 6 * mt * t * (c - b) + 3 * t * t * (d - c);
                if (v.Length > 0) v.Normalize();
                lengths.Add(points.Count == 0 ? 0 : lengths[^1] + (p - points[^1]).Length);
                points.Add(p);
                tangents.Add(v);
            }
        }
    }

    private static StreamGeometry Line(IReadOnlyList<Point> points)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        g.Freeze();
        return g;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

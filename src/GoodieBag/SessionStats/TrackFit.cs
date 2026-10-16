using System.Drawing;
using GoodieBag.SimRacing.TrackMaps;

namespace GoodieBag.SessionStats;

internal sealed record TrackOutline(List<(double[] X, double[] Y)> Surface, List<(double[] X, double[] Y, bool Filled)> Pit, double PitWidth,
    (double X1, double Y1, double X2, double Y2)? StartLine, double Width, double Rms, double MapLength = 0);

internal static class TrackFit
{
    internal readonly record struct Similarity(double A, double B, double Tx, double Ty)
    {
        public double Scale => Math.Sqrt(A * A + B * B);

        public (double X, double Y) Apply(double sx, double sy)
        {
            double u = sx, v = -sy;
            return (A * u - B * v + Tx, B * u + A * v + Ty);
        }
    }

    public const double MaxRms = 8;
    private const int Samples = 360, Dense = 2400, Rounds = 8, Window = 80;

    internal static Similarity Solve(IReadOnlyList<(double U, double V)> from, IReadOnlyList<(double X, double Y)> to)
    {
        int n = from.Count;
        double mu = 0, mv = 0, mx = 0, my = 0;
        for (int i = 0; i < n; i++) { mu += from[i].U; mv += from[i].V; mx += to[i].X; my += to[i].Y; }
        mu /= n; mv /= n; mx /= n; my /= n;
        double suu = 0, sa = 0, sb = 0;
        for (int i = 0; i < n; i++)
        {
            double u = from[i].U - mu, v = from[i].V - mv, x = to[i].X - mx, y = to[i].Y - my;
            suu += u * u + v * v;
            sa += u * x + v * y;
            sb += u * y - v * x;
        }
        double a = suu > 0 ? sa / suu : 1, b = suu > 0 ? sb / suu : 0;
        return new Similarity(a, b, mx - (a * mu - b * mv), my - (b * mu + a * mv));
    }

    public static TrackOutline? Fit(TrackMap map, double[] x, double[] y, double step, double lengthM) =>
        Fit(map, new[] { (x, y, step, lengthM) });

    public static TrackOutline? Fit(TrackMap map, IReadOnlyList<(double[] X, double[] Y, double Step, double Length)> lines)
    {
        lines = lines.Where(l => l.X.Length >= 50 && l.Length >= 200).ToList();
        if (map.Centerline.Length < 8 || map.Surface.Count == 0 || lines.Count == 0) return null;
        int count = Samples * lines.Count;
        var lap = new (double X, double Y)[count];
        var mapPts = new (double U, double V)[count];
        var hint = new int[count];
        for (int l = 0; l < lines.Count; l++)
        {
            var (x, y, step, length) = lines[l];
            for (int k = 0; k < Samples; k++)
            {
                double share = (k + 0.5) / Samples;
                int at = l * Samples + k;
                lap[at] = (LapReplay.At(x, step, share * length), LapReplay.At(y, step, share * length));
                var p = map.At(share);
                mapPts[at] = (p.X, -p.Y);
                hint[at] = (int)(share * Dense);
            }
        }
        double lengthM = lines.Average(l => l.Length);
        var sim = Solve(mapPts, lap);

        var dense = TrackMap.Resample(map.Centerline, Dense);
        int nd = dense.Length;
        var placed = new (double X, double Y)[nd];
        double rms = 0;
        Span<double> row = stackalloc double[4];
        for (int round = 0; round <= Rounds; round++)
        {
            for (int j = 0; j < nd; j++) placed[j] = sim.Apply(dense[j].X, dense[j].Y);
            var m = new double[4, 5];
            double sum = 0;
            for (int k = 0; k < count; k++)
            {
                var (qx, qy) = lap[k];
                int best = 0;
                double near = double.MaxValue, bestT = 0;
                for (int w = -Window; w <= Window; w++)
                {
                    int j = ((hint[k] + w) % nd + nd) % nd;
                    var (ax, ay) = placed[j];
                    var (bx, by) = placed[(j + 1) % nd];
                    double ex = bx - ax, ey = by - ay, len2 = ex * ex + ey * ey;
                    double t = len2 > 0 ? Math.Clamp(((qx - ax) * ex + (qy - ay) * ey) / len2, 0, 1) : 0;
                    double dx = ax + ex * t - qx, dy = ay + ey * t - qy, d = dx * dx + dy * dy;
                    if (d < near) { near = d; best = j; bestT = t; }
                }
                hint[k] = best;
                sum += near;
                if (round == Rounds) continue;
                var pa = dense[best];
                var pb = dense[(best + 1) % nd];
                double u = pa.X + (pb.X - pa.X) * bestT, v = -(pa.Y + (pb.Y - pa.Y) * bestT);
                var (wax, way) = placed[best];
                var (wbx, wby) = placed[(best + 1) % nd];
                double tx = wbx - wax, ty = wby - way, tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl <= 0) continue;
                double nx = -ty / tl, ny = tx / tl;
                double r0 = nx * u + ny * v, r1 = -nx * v + ny * u, target = nx * qx + ny * qy;
                (row[0], row[1], row[2], row[3]) = (r0, r1, nx, ny);
                for (int r = 0; r < 4; r++)
                {
                    for (int c = 0; c < 4; c++) m[r, c] += row[r] * row[c];
                    m[r, 4] += row[r] * target;
                }
            }
            rms = Math.Sqrt(sum / count);
            if (round < Rounds && SolveLinear(m) is { } x4) sim = new Similarity(x4[0], x4[1], x4[2], x4[3]);
        }
        double mapLength = map.Length * sim.Scale;
        if (rms > MaxRms || Math.Abs(mapLength - lengthM) / lengthM > 0.08) return null;

        (double[] X, double[] Y) Place(PointF[] pts)
        {
            var xs = new double[pts.Length];
            var ys = new double[pts.Length];
            for (int i = 0; i < pts.Length; i++) (xs[i], ys[i]) = sim.Apply(pts[i].X, pts[i].Y);
            return (xs, ys);
        }
        var surface = map.Surface.Where(f => f.Length >= 3).Select(f => { var (px, py) = Place(f); return Spline(px, py, closed: true, 1.5); }).ToList();
        var pit = new List<(double[], double[], bool)>();
        float pitWidth = 0;
        foreach (var shape in map.Pit)
            foreach (var fig in shape.Figures.Where(f => f.Points.Length >= 2 && !(shape.Filled && IsDash(f.Points, map.Width))))
            {
                var (px, py) = Place(fig.Points);
                var (sx, sy) = Spline(px, py, fig.Closed || shape.Filled, 1.5);
                pit.Add((sx, sy, shape.Filled));
                if (!shape.Filled) pitWidth = Math.Max(pitWidth, shape.StrokeWidth);
            }
        (double, double, double, double)? start = null;
        if (map.StartLine is { } line)
        {
            var (x1, y1) = sim.Apply(line.A.X, line.A.Y);
            var (x2, y2) = sim.Apply(line.B.X, line.B.Y);
            start = (x1, y1, x2, y2);
        }
        return new TrackOutline(surface, pit, pitWidth * sim.Scale, start, map.Width * sim.Scale, rms, mapLength);
    }

    private static double[]? SolveLinear(double[,] m)
    {
        int n = m.GetLength(0);
        for (int c = 0; c < n; c++)
        {
            int pivot = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[pivot, c])) pivot = r;
            if (Math.Abs(m[pivot, c]) < 1e-12) return null;
            if (pivot != c) for (int k = 0; k <= n; k++) (m[c, k], m[pivot, k]) = (m[pivot, k], m[c, k]);
            for (int r = 0; r < n; r++)
            {
                if (r == c) continue;
                double f = m[r, c] / m[c, c];
                for (int k = c; k <= n; k++) m[r, k] -= f * m[c, k];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = m[i, n] / m[i, i];
        return x;
    }

    internal static bool IsDash(PointF[] points, double width)
    {
        double w = points.Max(p => p.X) - points.Min(p => p.X), h = points.Max(p => p.Y) - points.Min(p => p.Y);
        return Math.Sqrt(w * w + h * h) < width * 3;
    }

    public static List<(double[] X, double[] Y)> Wound(List<(double[] X, double[] Y)> figures)
    {
        if (figures.Count < 2) return figures;
        var areas = figures.Select(f => SignedArea(f.X, f.Y)).ToList();
        int outer = 0;
        for (int i = 1; i < areas.Count; i++) if (Math.Abs(areas[i]) > Math.Abs(areas[outer])) outer = i;
        return figures.Select((f, i) => i != outer && Math.Sign(areas[i]) == Math.Sign(areas[outer])
            ? (f.X.Reverse().ToArray(), f.Y.Reverse().ToArray()) : f).ToList();
    }

    internal static double SignedArea(double[] x, double[] y)
    {
        double sum = 0;
        for (int i = 0, j = x.Length - 1; i < x.Length; j = i++) sum += x[j] * y[i] - x[i] * y[j];
        return sum / 2;
    }

    public static (double[] X, double[] Y) Spline(double[] x, double[] y, bool closed, double step)
    {
        var px = new List<double>(x.Length);
        var py = new List<double>(y.Length);
        for (int i = 0; i < x.Length; i++)
            if (px.Count == 0 || Math.Abs(x[i] - px[^1]) + Math.Abs(y[i] - py[^1]) > 1e-9) { px.Add(x[i]); py.Add(y[i]); }
        if (closed && px.Count > 1 && Math.Abs(px[0] - px[^1]) + Math.Abs(py[0] - py[^1]) <= 1e-9) { px.RemoveAt(px.Count - 1); py.RemoveAt(py.Count - 1); }
        int n = px.Count;
        if (n < 3) return (px.ToArray(), py.ToArray());
        (double X, double Y) P(int i) => closed ? (px[((i % n) + n) % n], py[((i % n) + n) % n])
            : i < 0 ? (2 * px[0] - px[1], 2 * py[0] - py[1]) : i >= n ? (2 * px[n - 1] - px[n - 2], 2 * py[n - 1] - py[n - 2]) : (px[i], py[i]);
        var ox = new List<double>(n * 4);
        var oy = new List<double>(n * 4);
        int segments = closed ? n : n - 1;
        for (int i = 0; i < segments; i++)
        {
            var p0 = P(i - 1); var p1 = P(i); var p2 = P(i + 1); var p3 = P(i + 2);
            static double Knot((double X, double Y) a, (double X, double Y) b) => Math.Max(1e-6, Math.Sqrt(Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y))));
            double t0 = 0, t1 = t0 + Knot(p0, p1), t2 = t1 + Knot(p1, p2), t3 = t2 + Knot(p2, p3);
            double len = Math.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y));
            int parts = Math.Max(1, (int)Math.Ceiling(len / step));
            for (int s = 0; s < parts; s++)
            {
                double t = t1 + (t2 - t1) * s / parts;
                (double, double) L((double X, double Y) a, (double X, double Y) b, double ta, double tb) =>
                    ((tb - t) / (tb - ta) * a.X + (t - ta) / (tb - ta) * b.X, (tb - t) / (tb - ta) * a.Y + (t - ta) / (tb - ta) * b.Y);
                var a1 = L(p0, p1, t0, t1); var a2 = L(p1, p2, t1, t2); var a3 = L(p2, p3, t2, t3);
                var b1 = L(a1, a2, t0, t2); var b2 = L(a2, a3, t1, t3);
                var c = L(b1, b2, t1, t2);
                ox.Add(c.Item1);
                oy.Add(c.Item2);
            }
        }
        if (!closed) { ox.Add(px[^1]); oy.Add(py[^1]); }
        return (ox.ToArray(), oy.ToArray());
    }
}

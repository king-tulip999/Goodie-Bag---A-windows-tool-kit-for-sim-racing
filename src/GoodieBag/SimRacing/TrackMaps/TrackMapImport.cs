using System.Drawing;
using System.Globalization;
using System.Xml.Linq;

namespace GoodieBag.SimRacing.TrackMaps;

internal sealed record ImportResult(TrackMap? Map, string? Problem, IReadOnlyList<string> Notes);

internal static class TrackMapImport
{
    public const int CenterlinePoints = 720;

    public static ImportResult FromIracingLayers(string key, int? trackId, string trackName, string? active, string? startFinish, string? pitroad, string? turns)
    {
        var notes = new List<string>();
        if (active is null) return new(null, "active layer missing", notes);
        SvgGeometry surface;
        try { surface = SvgGeometry.Parse(active); }
        catch (Exception ex) when (ex is FormatException or System.Xml.XmlException) { return new(null, "active layer unreadable: " + ex.Message, notes); }

        var closed = surface.Shapes.Where(s => s.Filled).SelectMany(s => s.Figures).Where(f => f.Points.Length >= 8)
            .Select(f => f.Points).OrderByDescending(p => TrackMap.Perimeter(p)).ToList();
        PointF[] centre;
        float width;
        List<PointF[]> ribbon;
        if (Ribbon(closed) is { } pair)
        {
            (centre, width) = Midline(pair.Outer, pair.Inner);
            ribbon = new() { pair.Outer, pair.Inner };
            if (closed.Count > 2) notes.Add($"{closed.Count - 2} extra shape(s) in the active layer left out");
        }
        else if (closed.Count > 0 && MidlineSingle(closed[0]) is { } single)
        {
            (centre, width) = single;
            ribbon = new() { closed[0] };
            notes.Add("surface drawn as one outline: centreline from its two sides");
        }
        else if (surface.Shapes.Where(s => !s.Filled).SelectMany(s => s.Figures.Select(f => (f, s.StrokeWidth))).OrderByDescending(x => TrackMap.Perimeter(x.f.Points, x.f.Closed)).FirstOrDefault() is { f: { } line } stroke)
        {
            centre = TrackMap.Resample(line.Points, CenterlinePoints, line.Closed);
            width = stroke.StrokeWidth;
            ribbon = new();
            notes.Add("drawn as a line (the active layer has no surface outline)");
        }
        else return new(null, "no racing surface outline (outer + inner edge) in the active layer", notes);

        if (ribbon.Count > 0 && Stray(centre, ribbon, width) is > 1.2 and var stray)
            return new(null, $"centreline leaves the drawn track (up to {stray:0.0} track widths off)", notes);
        double around = TrackMap.Perimeter(centre);
        if (width < 1 || width > around * 0.08) return new(null, $"centreline unreliable (track width {width:0.0} units on a {around:0} unit lap)", notes);

        (PointF A, PointF B)? startLine = null;
        PointF[]? arrow = null;
        if (startFinish is not null)
        {
            try { (startLine, arrow) = StartFinish(SvgGeometry.Parse(startFinish)); }
            catch (Exception ex) when (ex is FormatException or System.Xml.XmlException) { notes.Add("start-finish layer unreadable"); }
        }
        if (startLine is null) return new(null, "no start / finish line (the lap can't be placed on the map)", notes);
        var sf = startLine.Value;
        var mid = new PointF((sf.A.X + sf.B.X) / 2, (sf.A.Y + sf.B.Y) / 2);
        PointF? direction = arrow is { Length: >= 3 } a ? ArrowDirection(a) : null;
        if (direction is null) notes.Add("no direction arrow: driving direction not verified");
        centre = TrackMap.StartAt(centre, mid, direction);
        double offStart = centre.Min(p => TrackMap.Distance(p, mid));
        if (offStart > Math.Max(30, width * 3)) return new(null, $"start / finish line {offStart:0} units from the track", notes);

        var pit = new List<SvgShape>();
        if (pitroad is not null)
        {
            try { pit = SvgGeometry.Parse(pitroad).Shapes.Select(Simplified).ToList(); }
            catch (Exception ex) when (ex is FormatException or System.Xml.XmlException) { notes.Add("pit road layer unreadable"); }
        }
        else notes.Add("no pit road layer");
        var turnLabels = new List<SvgText>();
        if (turns is not null)
        {
            try { turnLabels = SvgGeometry.Parse(turns).Texts.Where(t => t.Text.Length <= 8).Select(Centred).ToList(); }
            catch (Exception ex) when (ex is FormatException or System.Xml.XmlException) { notes.Add("turns layer unreadable"); }
        }

        var map = new TrackMap
        {
            Key = key, TrackId = trackId, TrackName = trackName, Source = TrackMapSource.IracingSvg,
            Surface = ribbon.Select(r => TrackMap.Simplify(Close(r), 0.35)).ToList(),
            Centerline = centre, Width = width, Pit = pit, StartLine = startLine, Arrow = arrow, Turns = turnLabels,
        };
        return new(map, null, notes);
    }

    private static PointF[] Close(PointF[] p) => p.Length > 1 && p[0] != p[^1] ? p.Append(p[0]).ToArray() : p;

    private static SvgShape Simplified(SvgShape s) =>
        s with { Figures = s.Figures.Select(f => new SvgFigure(TrackMap.Simplify(f.Points, 0.35), f.Closed)).ToList() };

    private static SvgText Centred(SvgText t) => t with { At = new PointF(t.At.X + t.Text.Length * t.Size * 0.28f, t.At.Y - t.Size * 0.36f) };

    internal static (PointF[] Outer, PointF[] Inner)? Ribbon(List<PointF[]> closed)
    {
        if (closed.Count < 2) return null;
        var outer = closed[0];
        var ob = BoundsOf(outer);
        double op = TrackMap.Perimeter(outer);
        foreach (var candidate in closed.Skip(1))
        {
            var cb = BoundsOf(candidate);
            double ratio = TrackMap.Perimeter(candidate) / op;
            if (ratio < 0.5) break;
            if (ob.Contains(RectangleF.Inflate(cb, -1, -1))) return (outer, candidate);
        }
        return null;
    }

    private static RectangleF BoundsOf(PointF[] p) =>
        RectangleF.FromLTRB(p.Min(x => x.X), p.Min(x => x.Y), p.Max(x => x.X), p.Max(x => x.Y));

    internal static (PointF[] Centre, float Width) Midline(PointF[] outer, PointF[] inner)
    {
        var o = TrackMap.Resample(outer, CenterlinePoints);
        var dense = TrackMap.Resample(inner, CenterlinePoints * 3);
        int m = dense.Length;
        static double D2(PointF a, PointF b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
        int j = 0;
        double first = double.MaxValue;
        for (int k = 0; k < m; k++) { double d = D2(dense[k], o[0]); if (d < first) { first = d; j = k; } }
        var mid = new PointF[o.Length];
        var widths = new double[o.Length];
        const int window = 24;
        for (int i = 0; i < o.Length; i++)
        {
            double best = double.MaxValue;
            int at = j;
            for (int s = -window; s <= window; s++)
            {
                int k = ((j + s) % m + m) % m;
                double d = D2(dense[k], o[i]);
                if (d < best) { best = d; at = k; }
            }
            j = at;
            mid[i] = new PointF((o[i].X + dense[j].X) / 2, (o[i].Y + dense[j].Y) / 2);
            widths[i] = Math.Sqrt(best);
        }
        Array.Sort(widths);
        return (TrackMap.Resample(Smooth(mid), CenterlinePoints), (float)widths[widths.Length / 2]);
    }

    private static PointF[] Smooth(PointF[] mid, bool closed = true)
    {
        var smooth = new PointF[mid.Length];
        for (int i = 0; i < mid.Length; i++)
        {
            float x = 0, y = 0;
            int count = 0;
            for (int k = -2; k <= 2; k++)
            {
                int at = i + k;
                if (closed) at = (at + mid.Length) % mid.Length;
                else if (at < 0 || at >= mid.Length) continue;
                x += mid[at].X; y += mid[at].Y; count++;
            }
            smooth[i] = new PointF(x / count, y / count);
        }
        return smooth;
    }

    internal static (PointF[] Centre, float Width)? MidlineSingle(PointF[] loop)
    {
        const int n = 1440;
        var p = TrackMap.Resample(loop, n);
        int minGap = n / 12;
        int Circ(int a, int b) { int d = Math.Abs(a - b) % n; return Math.Min(d, n - d); }
        static double D2(PointF a, PointF b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
        var partner = new int[n];
        for (int i = 0; i < n; i++)
        {
            double best = double.MaxValue;
            for (int j = 0; j < n; j++)
            {
                if (Circ(i, j) < minGap) continue;
                double d = D2(p[i], p[j]);
                if (d < best) { best = d; partner[i] = j; }
            }
        }
        int e1 = Enumerable.Range(0, n).OrderBy(i => Circ(i, partner[i])).First();
        int e2 = Enumerable.Range(0, n).Where(i => Circ(i, e1) > n / 4).OrderBy(i => Circ(i, partner[i])).FirstOrDefault(-1);
        if (e2 < 0) return null;
        int span = (e2 - e1 + n) % n;
        if (span < n * 0.35 || span > n * 0.65) return null;
        var mid = new List<PointF>();
        var widths = new List<double>();
        int back = 0;
        const int window = 30;
        for (int k = 0; k <= span; k++)
        {
            var a = p[(e1 + k) % n];
            double best = double.MaxValue;
            int at = back;
            for (int s = 0; s <= window && back + s <= n - span; s++)
            {
                double d = D2(a, p[((e1 - back - s) % n + n) % n]);
                if (d < best) { best = d; at = back + s; }
            }
            back = at;
            var b = p[((e1 - back) % n + n) % n];
            mid.Add(new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2));
            widths.Add(Math.Sqrt(best));
        }
        widths.Sort();
        double width = widths[widths.Count / 2];
        if (width > TrackMap.Perimeter(loop) / 40) return null;
        return (TrackMap.Resample(Smooth(mid.ToArray(), closed: false), CenterlinePoints), (float)width);
    }

    internal static double Stray(PointF[] centre, IReadOnlyList<PointF[]> edges, float width)
    {
        if (edges.Count == 0 || width <= 0) return 0;
        var dense = edges.SelectMany(e => TrackMap.Resample(e, Math.Max(200, (int)(TrackMap.Perimeter(e) / Math.Max(1, width / 4))))).ToArray();
        double worst = 0;
        foreach (var c in centre)
        {
            double best = double.MaxValue;
            foreach (var q in dense)
            {
                double d = (q.X - c.X) * (q.X - c.X) + (q.Y - c.Y) * (q.Y - c.Y);
                if (d < best) best = d;
            }
            worst = Math.Max(worst, Math.Sqrt(best));
        }
        return worst / width;
    }

    internal static ((PointF A, PointF B)? Line, PointF[]? Arrow) StartFinish(SvgGeometry g)
    {
        var figures = g.Shapes.SelectMany(s => s.Figures).Where(f => f.Points.Length >= 2).Select(f => f.Points).ToList();
        if (figures.Count == 0) return (null, null);
        (PointF A, PointF B, double Ratio) Axis(PointF[] p)
        {
            var pts = p.Length > 1 && p[0] == p[^1] ? p[..^1] : p;
            double mx = pts.Average(x => x.X), my = pts.Average(x => x.Y);
            double sxx = pts.Average(x => (x.X - mx) * (x.X - mx)), syy = pts.Average(x => (x.Y - my) * (x.Y - my)), sxy = pts.Average(x => (x.X - mx) * (x.Y - my));
            double angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            double ux = Math.Cos(angle), uy = Math.Sin(angle);
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (var q in pts)
            {
                double u = (q.X - mx) * ux + (q.Y - my) * uy, v = -(q.X - mx) * uy + (q.Y - my) * ux;
                minU = Math.Min(minU, u); maxU = Math.Max(maxU, u); minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
            }
            double along = maxU - minU, across = maxV - minV, midV = (minV + maxV) / 2;
            PointF At(double u) => new((float)(mx + u * ux - midV * uy), (float)(my + u * uy + midV * ux));
            return (At(minU), At(maxU), along / Math.Max(0.5, across));
        }
        var ranked = figures.Select(f => (Fig: f, Axis: Axis(f))).OrderByDescending(x => x.Axis.Ratio).ToList();
        var line = ranked[0];
        if (line.Axis.Ratio < 2.5) return (null, null);
        var rest = ranked.Skip(1).Where(x => x.Fig.Length >= 3).Select(x => x.Fig).OrderByDescending(f => TrackMap.Perimeter(f)).FirstOrDefault();
        return ((line.Axis.A, line.Axis.B), rest);
    }

    internal static PointF ArrowDirection(PointF[] arrow)
    {
        var pts = arrow.Length > 1 && arrow[0] == arrow[^1] ? arrow[..^1] : arrow;
        var c = new PointF(pts.Average(p => p.X), pts.Average(p => p.Y));
        var tip = pts.OrderByDescending(p => TrackMap.Distance(p, c)).First();
        double len = TrackMap.Distance(tip, c);
        return len <= 0 ? new PointF(1, 0) : new PointF((float)((tip.X - c.X) / len), (float)((tip.Y - c.Y) / len));
    }

    public static ImportResult FromPath(string key, int? trackId, string trackName, TrackMapSource source, IReadOnlyList<PointF> track, IReadOnlyList<PointF>? pitLane)
    {
        var notes = new List<string>();
        if (track.Count < 20) return new(null, $"too few points on the track line ({track.Count})", notes);
        var all = track.Concat(pitLane ?? Array.Empty<PointF>()).ToList();
        float minX = all.Min(p => p.X), minY = all.Min(p => p.Y), maxX = all.Max(p => p.X), maxY = all.Max(p => p.Y);
        float extent = Math.Max(maxX - minX, maxY - minY);
        if (extent < 100) return new(null, $"track line only {extent:0} m across", notes);
        float s = 1700 / extent;
        PointF T(PointF p) => new((p.X - minX) * s + 110, (p.Y - minY) * s + 110);
        var line = track.Select(T).ToArray();
        if (TrackMap.Distance(line[0], line[^1]) > TrackMap.Perimeter(line, false) * 0.03)
            return new(null, "track line doesn't close into a lap", notes);
        var centre = TrackMap.Resample(line, CenterlinePoints);
        float width = Math.Clamp(12 * s, 6, 18);
        var heading = Unit(centre[0], centre[3]);
        var normal = new PointF(-heading.Y, heading.X);
        float half = width * 1.4f;
        var sf = (new PointF(centre[0].X - normal.X * half, centre[0].Y - normal.Y * half), new PointF(centre[0].X + normal.X * half, centre[0].Y + normal.Y * half));
        var pit = new List<SvgShape>();
        if (pitLane is { Count: >= 2 })
            pit.Add(new SvgShape(new[] { new SvgFigure(TrackMap.Simplify(pitLane.Select(T).ToArray(), 0.35), false) }, width * 0.55f, "pit"));
        else notes.Add("no pit lane");
        var map = new TrackMap
        {
            Key = key, TrackId = trackId, TrackName = trackName, Source = source, Centerline = centre, Width = width, Pit = pit, StartLine = sf,
        };
        return new(map, null, notes);
    }

    private static PointF Unit(PointF a, PointF b)
    {
        double len = TrackMap.Distance(a, b);
        return len <= 0 ? new PointF(1, 0) : new PointF((float)((b.X - a.X) / len), (float)((b.Y - a.Y) / len));
    }

    public static List<(double Lat, double Lon)> GpxPoints(string xml)
    {
        var doc = XDocument.Parse(xml.TrimStart('﻿'));
        return doc.Descendants().Where(e => e.Name.LocalName is "rtept" or "trkpt")
            .Select(e => (double.Parse(e.Attribute("lat")!.Value, CultureInfo.InvariantCulture), double.Parse(e.Attribute("lon")!.Value, CultureInfo.InvariantCulture)))
            .ToList();
    }

    public static List<PointF> Project(IReadOnlyList<(double Lat, double Lon)> points, double lat0, double lon0)
    {
        const double R = 6371000;
        double k = Math.Cos(lat0 * Math.PI / 180);
        return points.Select(p => new PointF((float)((p.Lon - lon0) * Math.PI / 180 * R * k), (float)(-(p.Lat - lat0) * Math.PI / 180 * R))).ToList();
    }
}

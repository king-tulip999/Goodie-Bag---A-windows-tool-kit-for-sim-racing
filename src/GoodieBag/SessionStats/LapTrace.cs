using System.IO;
using System.IO.Compression;

namespace GoodieBag.SessionStats;

internal enum TraceChannel { Time, Speed, Throttle, Brake, Clutch, Steering, Gear, Rpm, LatAccel, LongAccel, X, Y }

internal sealed class LapTrace
{
    public const double DefaultStep = 1;
    private const byte Format = 1;
    private const double EarthRadius = 6371008.8;
    private static readonly int Channels = Enum.GetValues<TraceChannel>().Length;
    private static readonly double[] Scale = { 1000, 100, 255, 255, 255, 1000, 1, 1, 100, 100, 100, 100 };

    private readonly double[]?[] _data;

    public LapTrace(double stepM, double lengthM, double lapTime, double originLat, double originLon, int points)
    {
        StepM = stepM;
        LengthM = lengthM;
        LapTime = lapTime;
        OriginLat = originLat;
        OriginLon = originLon;
        Points = points;
        _data = new double[]?[Channels];
    }

    public double StepM { get; }
    public double LengthM { get; }
    public double LapTime { get; }
    public double OriginLat { get; }
    public double OriginLon { get; }
    public int Points { get; }

    public double[]? this[TraceChannel c]
    {
        get => _data[(int)c];
        set => _data[(int)c] = value is null || value.Length == Points ? value : throw new ArgumentException("wrong length");
    }

    public double[] Time => _data[(int)TraceChannel.Time]!;

    public bool HasLine => this[TraceChannel.X] is not null && this[TraceChannel.Y] is not null;

    public bool Relative => OriginLat == 0 && OriginLon == 0;

    public double Distance(int i) => i * StepM;

    public double? At(TraceChannel c, double d)
    {
        if (this[c] is not { } v) return null;
        if (d >= LengthM && c == TraceChannel.Time) return LapTime;
        double at = Math.Clamp(d / StepM, 0, Points - 1);
        int i = (int)at;
        if (i >= Points - 1) return v[Points - 1];
        double f = at - i;
        return c == TraceChannel.Gear ? v[f < 0.5 ? i : i + 1] : v[i] + (v[i + 1] - v[i]) * f;
    }

    public double TimeAt(double d) => At(TraceChannel.Time, d) ?? 0;

    public double DistanceAtTime(double time)
    {
        var t = Time;
        if (time <= 0) return 0;
        if (time >= t[^1]) return Math.Min(LengthM, Distance(Points - 1) + (LapTime > t[^1] ? (time - t[^1]) / (LapTime - t[^1]) * (LengthM - Distance(Points - 1)) : 0));
        int lo = 0, hi = Points - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (t[mid] <= time) lo = mid; else hi = mid; }
        double f = t[hi] > t[lo] ? (time - t[lo]) / (t[hi] - t[lo]) : 0;
        return Distance(lo) + f * StepM;
    }

    public byte[] Encode()
    {
        using var raw = new MemoryStream();
        using (var w = new BinaryWriter(raw, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Format);
            w.Write(StepM);
            w.Write(LengthM);
            w.Write(LapTime);
            w.Write(OriginLat);
            w.Write(OriginLon);
            w.Write(Points);
            int mask = 0;
            for (int c = 0; c < Channels; c++) if (_data[c] is not null) mask |= 1 << c;
            w.Write(mask);
            for (int c = 0; c < Channels; c++)
            {
                if (_data[c] is not { } values) continue;
                long last = 0;
                foreach (double v in values)
                {
                    long q = (long)Math.Round(v * Scale[c]);
                    long d = q - last;
                    last = q;
                    ulong z = (ulong)((d << 1) ^ (d >> 63));
                    while (z >= 0x80) { w.Write((byte)(z | 0x80)); z >>= 7; }
                    w.Write((byte)z);
                }
            }
        }
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true)) raw.WriteTo(deflate);
        return packed.ToArray();
    }

    private const int MaxUnpacked = 64 << 20;

    public static LapTrace Decode(byte[] bytes)
    {
        using var input = new DeflateStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        for (int n; (n = input.Read(chunk, 0, chunk.Length)) > 0;)
        {
            if (buffer.Length + n > MaxUnpacked) throw new InvalidDataException("lap trace too large");
            buffer.Write(chunk, 0, n);
        }
        buffer.Position = 0;
        using var r = new BinaryReader(buffer);
        if (r.ReadByte() != Format) throw new InvalidDataException("unknown lap trace format");
        var trace = new LapTrace(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadInt32());
        if (trace.Points is < 2 or > 1_000_000) throw new InvalidDataException("lap trace out of range");
        if (!double.IsFinite(trace.OriginLat) || !double.IsFinite(trace.OriginLon) || !(trace.StepM is > 0 and <= 10)
            || !(trace.LapTime is > 0 and < 3600) || !(trace.LengthM > 0) || Math.Abs((trace.Points - 1) * trace.StepM - trace.LengthM) > trace.StepM)
            throw new InvalidDataException("lap trace out of range");
        int mask = r.ReadInt32();
        for (int c = 0; c < Channels; c++)
        {
            if ((mask & (1 << c)) == 0) continue;
            var values = new double[trace.Points];
            long last = 0;
            for (int i = 0; i < values.Length; i++)
            {
                ulong z = 0;
                for (int shift = 0; ; shift += 7)
                {
                    if (shift > 63) throw new InvalidDataException("lap trace damaged");
                    byte b = r.ReadByte();
                    z |= (ulong)(b & 0x7F) << shift;
                    if (b < 0x80) break;
                }
                last += (long)(z >> 1) ^ -(long)(z & 1);
                values[i] = last / Scale[c];
            }
            trace._data[c] = values;
        }
        return trace;
    }

    public static double[] Delta(LapTrace lap, LapTrace reference)
    {
        int n = lap.Points;
        while (n > 0 && lap.Distance(n - 1) > reference.LengthM) n--;
        var delta = new double[n];
        for (int i = 0; i < n; i++) delta[i] = lap.Time[i] - reference.TimeAt(lap.Distance(i));
        return delta;
    }

    public static double[] SectorTimes(LapTrace trace, IReadOnlyList<double> startsPct)
    {
        var bounds = Bounds(startsPct);
        var times = new double[bounds.Count];
        for (int k = 0; k < bounds.Count; k++)
        {
            double from = trace.TimeAt(bounds[k] * trace.LengthM);
            double to = k + 1 < bounds.Count ? trace.TimeAt(bounds[k + 1] * trace.LengthM) : trace.LapTime;
            times[k] = to - from;
        }
        return times;
    }

    private static List<double> Bounds(IReadOnlyList<double> startsPct)
    {
        var list = startsPct.Where(p => p > 0 && p < 1).Distinct().OrderBy(p => p).ToList();
        list.Insert(0, 0);
        return list;
    }

    public static LapTrace? Optimal(IReadOnlyList<LapTrace> laps, IReadOnlyList<double> startsPct)
    {
        if (laps.Count == 0) return null;
        var bounds = Bounds(startsPct);
        var sectors = laps.Select(l => SectorTimes(l, startsPct)).ToList();
        var bestOf = Enumerable.Range(0, bounds.Count).Select(k => Enumerable.Range(0, laps.Count).MinBy(i => sectors[i][k])).ToArray();
        var first = laps[0];
        double length = laps.Min(l => l.LengthM);
        int points = laps.Min(l => l.Points);
        double lapTime = Enumerable.Range(0, bounds.Count).Sum(k => sectors[bestOf[k]][k]);
        var result = new LapTrace(first.StepM, length, lapTime, first.OriginLat, first.OriginLon, points);
        var source = new LapTrace[points];
        var offset = new double[points];
        double carried = 0;
        for (int k = 0; k < bounds.Count; k++)
        {
            var lap = laps[bestOf[k]];
            double from = bounds[k] * length, to = k + 1 < bounds.Count ? bounds[k + 1] * length : double.MaxValue;
            double shift = carried - lap.TimeAt(from);
            for (int i = 0; i < points; i++)
            {
                double d = first.Distance(i);
                if (d < from || d >= to) continue;
                source[i] = lap;
                offset[i] = shift;
            }
            carried += sectors[bestOf[k]][k];
        }
        foreach (TraceChannel c in Enum.GetValues<TraceChannel>())
        {
            if (c is TraceChannel.Y || laps.Any(l => l[c] is null)) continue;
            if (c == TraceChannel.X)
            {
                var lines = new Dictionary<LapTrace, (double[] X, double[] Y)>();
                foreach (var l in laps) lines.TryAdd(l, l.LineIn(first)!.Value);
                var xs = new double[points];
                var ys = new double[points];
                for (int i = 0; i < points; i++)
                {
                    var (lx, ly) = lines[source[i]];
                    xs[i] = lx[i];
                    ys[i] = ly[i];
                }
                result[TraceChannel.X] = xs;
                result[TraceChannel.Y] = ys;
                continue;
            }
            var values = new double[points];
            for (int i = 0; i < points; i++)
                values[i] = c == TraceChannel.Time ? source[i].Time[i] + offset[i] : source[i][c]![i];
            result[c] = values;
        }
        return result;
    }

    public static (double X, double Y) Project(double lat, double lon, double originLat, double originLon)
    {
        double k = Math.PI / 180 * EarthRadius;
        return ((lon - originLon) * k * Math.Cos(originLat * Math.PI / 180), (lat - originLat) * k);
    }

    public static (double Lat, double Lon) Unproject(double x, double y, double originLat, double originLon)
    {
        double k = Math.PI / 180 * EarthRadius;
        return (originLat + y / k, originLon + x / (k * Math.Cos(originLat * Math.PI / 180)));
    }

    public (double[] X, double[] Y)? LineIn(LapTrace frame)
    {
        if (this[TraceChannel.X] is not { } x || this[TraceChannel.Y] is not { } y) return null;
        if (OriginLat == frame.OriginLat && OriginLon == frame.OriginLon) return (x, y);
        if (Relative || frame.Relative)
        {
            double dx = (frame[TraceChannel.X] is { Length: > 0 } fx ? fx[0] : 0) - x[0];
            double dy = (frame[TraceChannel.Y] is { Length: > 0 } fy ? fy[0] : 0) - y[0];
            return (x.Select(v => v + dx).ToArray(), y.Select(v => v + dy).ToArray());
        }
        var xs = new double[Points];
        var ys = new double[Points];
        for (int i = 0; i < Points; i++)
        {
            var (lat, lon) = Unproject(x[i], y[i], OriginLat, OriginLon);
            (xs[i], ys[i]) = Project(lat, lon, frame.OriginLat, frame.OriginLon);
        }
        return (xs, ys);
    }
}

internal sealed class TraceBuilder
{
    internal readonly record struct Sample(double Dist, double Pct, double Time, double Speed, double Throttle, double Brake, double? Clutch,
        double Steering, int Gear, double Rpm, double LatAccel, double LongAccel, double Lat, double Lon, double X = double.NaN, double Y = double.NaN);

    private readonly List<Sample> _samples = new(8192);
    private readonly double _step;

    public TraceBuilder(double step = LapTrace.DefaultStep) => _step = step;

    public int Count => _samples.Count;

    public void Add(in Sample s) => _samples.Add(s);

    public void Clear() => _samples.Clear();

    public LapTrace? Finish(double lapStart, double lapTime)
    {
        try { return Build(_samples, lapStart, lapTime, _step); }
        finally { _samples.Clear(); }
    }

    internal static LapTrace? Build(List<Sample> samples, double lapStart, double lapTime, double step)
    {
        if (samples.Count < 20 || lapTime <= 0) return null;
        var ratios = samples.Where(s => s.Pct is > 0.3 and < 0.95 && s.Dist > 0).Select(s => s.Dist / s.Pct).OrderBy(r => r).ToList();
        if (ratios.Count == 0) return null;
        double length = ratios[ratios.Count / 2];
        if (length < 200) return null;

        var use = new List<Sample>(samples.Count);
        foreach (var s in samples)
        {
            double t = s.Time - lapStart;
            if (t < -0.001 || t > lapTime + 0.001) continue;
            if (t < lapTime / 2 ? s.Dist > length / 2 : s.Dist < length / 2) continue;
            if (use.Count > 0 && s.Dist <= use[^1].Dist) continue;
            use.Add(s with { Time = t });
        }
        if (use.Count < 20 || use[0].Dist > 150 || length - use[^1].Dist > 150) return null;
        for (int i = 1; i < use.Count; i++) if (use[i].Dist - use[i - 1].Dist > 150) return null;
        bool globe = use.All(s => s.Lat != 0 || s.Lon != 0);
        if (!globe && !use.All(s => double.IsFinite(s.X) && double.IsFinite(s.Y))) return null;

        use.Insert(0, use[0] with { Dist = 0, Time = 0 });
        use.Add(use[^1] with { Dist = length, Time = lapTime });

        int points = (int)Math.Floor(length / step) + 1;
        double originLat = globe ? use[1].Lat : 0, originLon = globe ? use[1].Lon : 0;
        var trace = new LapTrace(step, length, lapTime, originLat, originLon, points);
        var ch = Enum.GetValues<TraceChannel>().ToDictionary(c => c, _ => new double[points]);
        bool clutch = use.Any(s => s.Clutch is not null);
        int j = 0;
        for (int i = 0; i < points; i++)
        {
            double d = i * step;
            while (j < use.Count - 2 && use[j + 1].Dist < d) j++;
            var a = use[j];
            var b = use[j + 1];
            double f = b.Dist > a.Dist ? Math.Clamp((d - a.Dist) / (b.Dist - a.Dist), 0, 1) : 0;
            double L(double x, double y) => x + (y - x) * f;
            ch[TraceChannel.Time][i] = L(a.Time, b.Time);
            ch[TraceChannel.Speed][i] = L(a.Speed, b.Speed);
            ch[TraceChannel.Throttle][i] = L(a.Throttle, b.Throttle);
            ch[TraceChannel.Brake][i] = L(a.Brake, b.Brake);
            ch[TraceChannel.Clutch][i] = L(a.Clutch ?? 0, b.Clutch ?? 0);
            ch[TraceChannel.Steering][i] = L(a.Steering, b.Steering);
            ch[TraceChannel.Gear][i] = f < 0.5 ? a.Gear : b.Gear;
            ch[TraceChannel.Rpm][i] = L(a.Rpm, b.Rpm);
            ch[TraceChannel.LatAccel][i] = L(a.LatAccel, b.LatAccel);
            ch[TraceChannel.LongAccel][i] = L(a.LongAccel, b.LongAccel);
            var (x, y) = globe ? LapTrace.Project(L(a.Lat, b.Lat), L(a.Lon, b.Lon), originLat, originLon) : (L(a.X, b.X), L(a.Y, b.Y));
            ch[TraceChannel.X][i] = x;
            ch[TraceChannel.Y][i] = y;
        }
        if (!globe) Close(ch[TraceChannel.X], ch[TraceChannel.Y], use[0].X, use[0].Y, use[^1].X - use[0].X, use[^1].Y - use[0].Y);
        foreach (var (c, values) in ch)
            if (c != TraceChannel.Clutch || clutch) trace[c] = values;
        return trace;
    }

    private static void Close(double[] xs, double[] ys, double x0, double y0, double driftX, double driftY)
    {
        int last = xs.Length - 1;
        for (int i = 0; i <= last; i++)
        {
            double k = last > 0 ? (double)i / last : 0;
            xs[i] -= x0 + driftX * k;
            ys[i] -= y0 + driftY * k;
        }
    }
}

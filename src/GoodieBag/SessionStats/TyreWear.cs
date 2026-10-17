using System.Globalization;
using System.Text;

namespace GoodieBag.SessionStats;

internal sealed record TyreReading(int Lap, int Run, int Laps, double[] Start, double[] Now)
{
    public const int Values = 12;
    public static readonly string[] Corners = { "Left front", "Right front", "Left rear", "Right rear" };

    public double Left(int corner, int zone) => Now[corner * 3 + zone];

    public double Lowest(int corner) => Math.Min(Now[corner * 3], Math.Min(Now[corner * 3 + 1], Now[corner * 3 + 2]));

    public double Used(int corner) => Math.Max(0, Math.Max(Start[corner * 3] - Now[corner * 3], Math.Max(Start[corner * 3 + 1] - Now[corner * 3 + 1], Start[corner * 3 + 2] - Now[corner * 3 + 2])));

    public double? UsedPerLap(int corner) => Laps > 0 ? Used(corner) / Laps : null;

    public double? LapsTo(int corner, double floor)
    {
        if (UsedPerLap(corner) is not { } rate || rate <= 1e-6) return null;
        return Math.Max(0, (Lowest(corner) - floor) / rate);
    }
}

internal sealed class TyreWearTracker
{
    internal const double Same = 0.0005;
    private readonly double[] _last = new double[TyreReading.Values], _start = new double[TyreReading.Values];
    private bool _seen;
    private int _startLaps, _lastLaps;

    public TyreReading? Feed(ReadOnlySpan<double> now, int laps, int run)
    {
        if (now.Length != TyreReading.Values) return null;
        foreach (double v in now) if (!double.IsFinite(v) || v < 0 || v > 1.001) return null;
        if (!_seen)
        {
            _seen = true;
            now.CopyTo(_last);
            now.CopyTo(_start);
            _startLaps = _lastLaps = laps;
            return null;
        }
        bool changed = false, fresh = true;
        for (int i = 0; i < now.Length; i++)
        {
            if (Math.Abs(now[i] - _last[i]) >= Same) changed = true;
            if (now[i] < 1 - Same) fresh = false;
        }
        if (!changed) return null;
        if (fresh)
        {
            now.CopyTo(_last);
            now.CopyTo(_start);
            _startLaps = _lastLaps = laps;
            return null;
        }
        Span<bool> fitted = stackalloc bool[4];
        bool newSet = false;
        for (int z = 0; z < TyreReading.Values; z++)
            if (now[z] > _last[z] + Same) newSet = fitted[z / 3] = true;
        if (newSet)
        {
            for (int z = 0; z < TyreReading.Values; z++) _start[z] = fitted[z / 3] ? 1 : _last[z];
            _startLaps = _lastLaps;
        }
        now.CopyTo(_last);
        _lastLaps = laps;
        return new TyreReading(laps, run, laps - _startLaps, _start.ToArray(), now.ToArray());
    }

    public void Reset() => _seen = false;
}

internal static class TyreWear
{
    public static List<TyreReading> NewSets(List<TyreReading> readings)
    {
        double[]? start = null;
        int startLap = 0;
        for (int i = 1; i < readings.Count; i++)
        {
            var prev = readings[i - 1];
            var r = readings[i];
            var fitted = new bool[4];
            bool any = false;
            for (int z = 0; z < TyreReading.Values; z++)
                if (r.Now[z] > prev.Now[z] + TyreWearTracker.Same) any = fitted[z / 3] = true;
            if (any && r.Laps > r.Lap - prev.Lap)
            {
                start = new double[TyreReading.Values];
                for (int z = 0; z < TyreReading.Values; z++) start[z] = fitted[z / 3] ? 1 : prev.Now[z];
                startLap = prev.Lap;
            }
            else if (any) start = null;
            if (start is not null && r.Laps > r.Lap - startLap)
                readings[i] = r with { Start = start.ToArray(), Laps = r.Lap - startLap };
        }
        return readings;
    }

    public static string? Text(IReadOnlyList<TyreReading>? readings, int lapOffset = 0)
    {
        if (readings is not { Count: > 0 }) return null;
        var sb = new StringBuilder();
        foreach (var r in readings)
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(r.Lap + lapOffset).Append('|').Append(r.Run).Append('|').Append(r.Laps).Append('|');
            sb.AppendJoin(',', r.Start.Select(v => v.ToString("0.####", CultureInfo.InvariantCulture)));
            sb.Append('|');
            sb.AppendJoin(',', r.Now.Select(v => v.ToString("0.####", CultureInfo.InvariantCulture)));
        }
        return sb.ToString();
    }

    public static List<TyreReading> Parse(string? text)
    {
        var list = new List<TyreReading>();
        if (string.IsNullOrEmpty(text)) return list;
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split('|');
            if (f.Length != 5 || !int.TryParse(f[0], CultureInfo.InvariantCulture, out int lap) || !int.TryParse(f[1], CultureInfo.InvariantCulture, out int run)
                || !int.TryParse(f[2], CultureInfo.InvariantCulture, out int laps) || Numbers(f[3]) is not { } start || Numbers(f[4]) is not { } now) continue;
            list.Add(new TyreReading(lap, run, laps, start, now));
        }
        return list;
    }

    private static double[]? Numbers(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != TyreReading.Values) return null;
        var values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return null;
        return values;
    }
}

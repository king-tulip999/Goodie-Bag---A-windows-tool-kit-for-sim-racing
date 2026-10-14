using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Race;

internal enum ClassScope { All, MyClass, Selected }

internal readonly record struct RelativeRow(int CarIdx, double? Gap, double TrackDistance, int LapDiff, bool IsPlayer);

internal readonly record struct StandingRow(int CarIdx, int Position, int ClassPosition, double? GapToLeader, double? GapToClassLeader,
    double? Interval, double? ClassInterval, int LapsDown, int ClassLapsDown);

internal static class FieldOrder
{
    public static double Wrap(double carPct, double refPct)
    {
        double d = carPct - refPct;
        d -= Math.Floor(d);
        return d > 0.5 ? d - 1 : d;
    }

    public static double? LapTimeOf(TelemetryFrame f, int car)
    {
        if (f.Session.ByCar.TryGetValue(car, out var d) && d.ClassEstLapTime is { } est) return est;
        if ((uint)car < TelemetryFrame.MaxCars && f.CarBestLap[car] > 0) return f.CarBestLap[car];
        return f.Session.PlayerEstLapTime ?? f.LapBest;
    }

    public static int ClassOf(TelemetryFrame f, int car) => f.Session.ByCar.TryGetValue(car, out var d) ? d.ClassId : 0;

    public static double? GapSeconds(TelemetryFrame f, int player, int car, EstProfile? profile = null)
    {
        double pp = f.CarLapDistPct[player], cp = f.CarLapDistPct[car];
        if (pp < 0 || cp < 0) return null;
        double dist = Wrap(cp, pp);
        var lapTime = LapTimeOf(f, player);
        if (f.HasCarEstTime && lapTime is { } lap && f.CarEstTime[player] > 0)
        {
            double? est = ClassOf(f, player) == ClassOf(f, car) && f.CarEstTime[car] > 0 ? f.CarEstTime[car] : profile?.At(cp, lap);
            if (est is { } e)
            {
                double gap = e - f.CarEstTime[player];
                if (dist > 0 && gap < 0) gap += lap;
                else if (dist < 0 && gap > 0) gap -= lap;
                if (Math.Abs(gap) <= lap / 2 + 1) return gap;
            }
        }
        return lapTime is { } l ? dist * l : null;
    }

    public static double? Distance(TelemetryFrame f, int car)
    {
        double pct = f.CarLapDistPct[car];
        if (pct < 0) return null;
        return Math.Max(0, f.CarLap[car] - 1) + pct;
    }

    public static List<RelativeRow> Relative(TelemetryFrame f, int ahead, int behind, bool hidePits = false, Func<int, bool>? include = null, EstProfile? profile = null)
    {
        var rows = new List<RelativeRow>();
        int player = f.PlayerCarIdx;
        if ((uint)player >= TelemetryFrame.MaxCars || f.CarLapDistPct[player] < 0) return rows;
        double pp = f.CarLapDistPct[player];
        double playerDist = Distance(f, player) ?? 0;
        var others = new List<(int Car, double Dist)>();
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            if (i == player || !f.CarActive(i) || f.CarLapDistPct[i] < 0) continue;
            if (hidePits && f.CarOnPitRoad[i]) continue;
            if (include is not null && !include(i)) continue;
            others.Add((i, Wrap(f.CarLapDistPct[i], pp)));
        }
        int LapDiff(int car, double dist) => Distance(f, car) is { } d ? (int)Math.Round(d - playerDist - dist) : 0;
        foreach (var (car, dist) in others.Where(o => o.Dist > 0).OrderBy(o => o.Dist).Take(Math.Max(0, ahead)).Reverse())
            rows.Add(new RelativeRow(car, GapSeconds(f, player, car, profile), dist, LapDiff(car, dist), false));
        rows.Add(new RelativeRow(player, 0, 0, 0, true));
        foreach (var (car, dist) in others.Where(o => o.Dist <= 0).OrderByDescending(o => o.Dist).Take(Math.Max(0, behind)))
            rows.Add(new RelativeRow(car, GapSeconds(f, player, car, profile), dist, LapDiff(car, dist), false));
        return rows;
    }

    public static List<StandingRow> Standings(TelemetryFrame f, bool race)
    {
        var cars = new List<int>();
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
            if (f.CarActive(i) || f.CarPosition[i] > 0 && f.Session.ByCar.TryGetValue(i, out var d) && d.Races) cars.Add(i);

        int Key(int car) => f.CarPosition[car] > 0 ? f.CarPosition[car] : int.MaxValue;
        var ordered = cars.OrderBy(Key)
            .ThenByDescending(c => race ? Distance(f, c) ?? -1 : 0)
            .ThenBy(c => !race && f.CarBestLap[c] > 0 ? f.CarBestLap[c] : double.MaxValue)
            .ThenBy(c => c)
            .ToList();

        var classCount = new Dictionary<int, int>();
        var classPos = new Dictionary<int, int>();
        foreach (var car in ordered)
        {
            int cls = ClassOf(f, car);
            classCount[cls] = classCount.GetValueOrDefault(cls) + 1;
            classPos[car] = f.CarClassPosition[car] > 0 ? f.CarClassPosition[car] : f.CarPosition[car] > 0 ? classCount[cls] : 0;
        }

        int? leader = ordered.Count > 0 ? ordered[0] : null;
        var classLeader = new Dictionary<int, int>();
        foreach (var car in ordered) classLeader.TryAdd(ClassOf(f, car), car);

        var rows = new List<StandingRow>(ordered.Count);
        var prevInClass = new Dictionary<int, int>();
        int? prev = null;
        foreach (var car in ordered)
        {
            int cls = ClassOf(f, car);
            var (gap, laps) = leader is { } l ? Behind(f, car, l, race) : (null, 0);
            var (classGap, classLaps) = Behind(f, car, classLeader[cls], race);
            double? interval = prev is { } p ? Behind(f, car, p, race).Gap : null;
            double? classInterval = prevInClass.TryGetValue(cls, out var pc) ? Behind(f, car, pc, race).Gap : null;
            rows.Add(new StandingRow(car, f.CarPosition[car], classPos[car], gap, classGap, interval, classInterval, laps, classLaps));
            prev = car;
            prevInClass[cls] = car;
        }
        return rows;
    }

    public static (double? Gap, int Laps) Behind(TelemetryFrame f, int car, int ahead, bool race)
    {
        if (car == ahead) return (0, 0);
        if (!race)
            return (f.CarBestLap[car] > 0 && f.CarBestLap[ahead] > 0 ? f.CarBestLap[car] - f.CarBestLap[ahead] : null, 0);
        int laps = 0;
        if (Distance(f, car) is { } dc && Distance(f, ahead) is { } da)
            laps = Math.Max(0, (int)Math.Floor(da - dc));
        if (f.HasCarF2Time && f.CarPosition[car] > 0 && f.CarPosition[ahead] > 0 && (f.CarF2Time[car] > 0 || f.CarF2Time[ahead] > 0))
            return (Math.Max(0, f.CarF2Time[car] - f.CarF2Time[ahead]), laps);
        if (Distance(f, car) is { } c && Distance(f, ahead) is { } a && LapTimeOf(f, car) is { } lap)
            return (Math.Max(0, (a - c - laps) * lap), laps);
        return (null, laps);
    }

    public static (int Car, double Time)? FastestLap(TelemetryFrame f, int? classId = null)
    {
        (int, double)? best = null;
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            double t = f.CarBestLap[i];
            if (t <= 0 || !f.Session.ByCar.TryGetValue(i, out var d) || !d.Races) continue;
            if (classId is { } c && d.ClassId != c) continue;
            if (best is null || t < best.Value.Item2) best = (i, t);
        }
        return best;
    }
}

internal sealed class EstProfile
{
    private const int Slices = 200;
    private readonly double[] _time = new double[Slices];
    private readonly double[] _pct = new double[Slices];
    private readonly bool[] _known = new bool[Slices];
    private int _class = int.MinValue;
    private int? _track;

    public void Update(TelemetryFrame f)
    {
        int player = f.PlayerCarIdx;
        if (!f.HasCarEstTime || (uint)player >= TelemetryFrame.MaxCars) return;
        int cls = FieldOrder.ClassOf(f, player);
        int? track = f.Session.TrackId;
        if (cls != _class || track != _track) Clear(cls, track);
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            double pct = f.CarLapDistPct[i], est = f.CarEstTime[i];
            if (pct < 0 || pct >= 1 || est <= 0 || !f.CarActive(i) || FieldOrder.ClassOf(f, i) != cls) continue;
            int s = (int)(pct * Slices);
            _time[s] = est;
            _pct[s] = pct;
            _known[s] = true;
        }
    }

    private void Clear(int cls, int? track)
    {
        _class = cls;
        _track = track;
        Array.Clear(_known);
    }

    public double? At(double pct, double lap)
    {
        if (pct < 0 || pct >= 1 || lap <= 0) return null;
        int s = (int)(pct * Slices);
        if (_known[s]) return _time[s] + (pct - _pct[s]) * lap;
        double loPct = 0, loTime = 0, hiPct = 1, hiTime = lap;
        bool any = false;
        for (int k = s - 1; k >= 0; k--) if (_known[k]) { loPct = _pct[k]; loTime = _time[k]; any = true; break; }
        for (int k = s + 1; k < Slices; k++) if (_known[k]) { hiPct = _pct[k]; hiTime = _time[k]; any = true; break; }
        if (!any) return null;
        return loTime + (hiTime - loTime) * (pct - loPct) / Math.Max(1e-9, hiPct - loPct);
    }
}

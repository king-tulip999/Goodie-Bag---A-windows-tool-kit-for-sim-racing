using System.Globalization;

namespace GoodieBag.SessionStats;

internal enum StatRange { Week, Month, Season, All }

internal enum StatKind { Duration, Count, Distance, Speed, Fuel, Temp, LapTime, LapDelta, Seconds, Percent, Position, Signed, Rating, PerHour, Decimal, Gs, Text }

internal sealed record StatResult(string Value, string? Detail = null)
{
    public static readonly StatResult None = new("—") { NoData = true };

    public bool NoData { get; init; }
}

internal sealed record StatQuery(long From, long To, IReadOnlyCollection<int> Leagues, bool Imperial, DateTime NowUtc)
{
    public string LeagueSql => Leagues.Count == 0 ? "0" : $"league_id IN ({string.Join(",", Leagues)})";
}

internal sealed record StatDef(string Id, string Category, string Label, StatKind Kind, string? Sql = null)
{
    public StatKind DetailKind { get; init; } = StatKind.Text;
    public string? DetailSuffix { get; init; }
    public bool Span { get; init; }
    public Func<StatsStore, StatQuery, StatResult>? Compute { get; init; }
    public bool League { get; init; }
    public bool LiveOnly { get; init; }
    public string? Help { get; init; }
    public bool ZeroWhenEmpty { get; init; }
}

internal static class StatRegistry
{
    public const string Time = "Time", CarsTracks = "Cars & tracks", Pace = "Pace", Racing = "Racing", Incidents = "Incidents", Style = "Driving style", Conditions = "Conditions";

    private const string Combo = "s.car || ' · ' || s.track";

    public static readonly IReadOnlyList<StatDef> All = new StatDef[]
    {
        new("time.total", Time, "Driving time", StatKind.Duration, "SELECT SUM(drive_s) FROM s") { ZeroWhenEmpty = true },
        new("time.practice", Time, "Practice", StatKind.Duration, "SELECT SUM(drive_s) FROM s WHERE session_type = 'Practice'") { ZeroWhenEmpty = true },
        new("time.qualify", Time, "Qualifying", StatKind.Duration, "SELECT SUM(drive_s) FROM s WHERE session_type = 'Qualify'") { ZeroWhenEmpty = true },
        new("time.race", Time, "Racing", StatKind.Duration, "SELECT SUM(drive_s) FROM s WHERE session_type = 'Race'") { ZeroWhenEmpty = true },
        new("time.week", Time, "This week", StatKind.Duration) { Compute = (store, q) => Fixed(store, q, StatRange.Week), Help = "Monday to now, whatever the range" },
        new("time.month", Time, "This month", StatKind.Duration) { Compute = (store, q) => Fixed(store, q, StatRange.Month), Help = "This calendar month, whatever the range" },
        new("time.weekly", Time, "Weekly average", StatKind.Duration,
            "SELECT SUM(drive_s) / MAX(1.0, (MIN(?2, CAST(strftime('%s', 'now') AS INTEGER)) - MIN(start_utc)) / 604800.0) FROM s"),
        new("time.session", Time, "Longest session", StatKind.Duration, $"SELECT MAX(drive_s), {Combo} FROM s"),
        new("time.stint", Time, "Longest stint", StatKind.Duration, $"SELECT MAX(longest_stint_s), {Combo} FROM s") { Help = "On track without stopping in the pits" },
        new("time.garage", Time, "Garage time", StatKind.Duration, "SELECT SUM(garage_s) FROM s") { LiveOnly = true },
        new("time.streak", Time, "Day streak", StatKind.Count) { Compute = Streak, Help = "Days in a row with a session, up to today (all time)" },

        new("car.time", CarsTracks, "Most driven car", StatKind.Text, "SELECT car, SUM(drive_s) t FROM s GROUP BY car ORDER BY t DESC LIMIT 1") { DetailKind = StatKind.Duration },
        new("car.laps", CarsTracks, "Most laps in", StatKind.Text, "SELECT car, SUM(laps) n FROM s GROUP BY car ORDER BY n DESC LIMIT 1") { DetailKind = StatKind.Count, DetailSuffix = "laps" },
        new("track.time", CarsTracks, "Most driven track", StatKind.Text, "SELECT track, SUM(drive_s) t FROM s GROUP BY track ORDER BY t DESC LIMIT 1") { DetailKind = StatKind.Duration },
        new("track.laps", CarsTracks, "Most laps at", StatKind.Text, "SELECT track, SUM(laps) n FROM s GROUP BY track ORDER BY n DESC LIMIT 1") { DetailKind = StatKind.Count, DetailSuffix = "laps" },
        new("car.unique", CarsTracks, "Cars driven", StatKind.Count, "SELECT COUNT(DISTINCT car) FROM s"),
        new("track.unique", CarsTracks, "Tracks driven", StatKind.Count, "SELECT COUNT(DISTINCT track) FROM s"),
        new("combo.top", CarsTracks, "Top car and track", StatKind.Text, $"SELECT {Combo}, SUM(drive_s) t FROM s GROUP BY s.car, s.track ORDER BY t DESC LIMIT 1") { DetailKind = StatKind.Duration },
        new("class.time", CarsTracks, "Most driven class", StatKind.Text, "SELECT car_class, SUM(drive_s) t FROM s GROUP BY car_class ORDER BY t DESC LIMIT 1") { DetailKind = StatKind.Duration },

        new("pace.laps", Pace, "Laps", StatKind.Count, "SELECT SUM(laps) FROM s") { ZeroWhenEmpty = true },
        new("pace.distance", Pace, "Distance", StatKind.Distance, "SELECT SUM(distance_km) FROM s") { ZeroWhenEmpty = true },
        new("pace.top", Pace, "Top speed", StatKind.Speed, $"SELECT MAX(l.top_speed), {Combo} FROM l JOIN s ON s.id = l.session_id"),

        new("race.starts", Racing, "Starts", StatKind.Count, "SELECT COUNT(*) FROM s WHERE session_type = 'Race' AND start_pos > 0"),
        new("race.finishes", Racing, "Finishes", StatKind.Count, "SELECT COUNT(*) FROM s WHERE session_type = 'Race' AND finished = 1"),
        new("race.wins", Racing, "Wins", StatKind.Count, "SELECT COUNT(*) FROM s WHERE session_type = 'Race' AND finished = 1 AND finish_pos = 1") { Help = "Class position in multiclass races" },
        new("race.podiums", Racing, "Podiums", StatKind.Count, "SELECT COUNT(*) FROM s WHERE session_type = 'Race' AND finished = 1 AND finish_pos BETWEEN 1 AND 3"),
        new("race.top5", Racing, "Top fives", StatKind.Count, "SELECT COUNT(*) FROM s WHERE session_type = 'Race' AND finished = 1 AND finish_pos BETWEEN 1 AND 5"),
        new("race.avgstart", Racing, "Average start", StatKind.Position, "SELECT AVG(start_pos) FROM s WHERE session_type = 'Race' AND start_pos > 0"),
        new("race.avgfinish", Racing, "Average finish", StatKind.Position, "SELECT AVG(finish_pos) FROM s WHERE session_type = 'Race' AND finished = 1 AND finish_pos > 0"),
        new("race.gained", Racing, "Positions gained", StatKind.Signed, "SELECT SUM(start_pos - finish_pos) FROM s WHERE session_type = 'Race' AND finished = 1 AND start_pos > 0 AND finish_pos > 0"),
        new("race.irating", Racing, "iRating change", StatKind.Signed, "SELECT SUM(ir_delta) FROM s WHERE session_type = 'Race'") { Help = "Known once the next session in the same category starts" },
        new("race.sr", Racing, "Safety rating change", StatKind.Rating, "SELECT SUM(sr_delta) FROM s WHERE session_type = 'Race'"),
        new("race.league", Racing, "League / public races", StatKind.Text,
            "SELECT SUM({league}) || ' / ' || SUM(NOT {league}) FROM s WHERE session_type = 'Race' AND start_pos > 0") { League = true },

        new("inc.total", Incidents, "Total incidents", StatKind.Count, "SELECT SUM(incidents) FROM s") { ZeroWhenEmpty = true },
        new("inc.zero", Incidents, "Clean races (0x)", StatKind.Count, "SELECT COUNT(*) FROM s WHERE session_type = 'Race' AND finished = 1 AND incidents = 0"),

        new("style.fuel", Style, "Fuel used", StatKind.Fuel, "SELECT SUM(fuel_used) FROM l") { Help = "Counted lap by lap" },
        new("style.pits", Style, "Pit stops", StatKind.Count, "SELECT SUM(pit_stops) FROM s") { ZeroWhenEmpty = true },

        new("cond.rain", Conditions, "In the rain", StatKind.Duration, "SELECT SUM(rain_s) FROM s"),
        new("cond.night", Conditions, "At night", StatKind.Duration, "SELECT SUM(night_s) FROM s"),
    };

    public static StatDef? Find(string id) => All.FirstOrDefault(d => d.Id == id);

    public static IEnumerable<string> Categories => All.Select(d => d.Category).Distinct();

    internal const string Scope = "WITH s AS (SELECT * FROM sessions WHERE start_utc >= ?1 AND start_utc < ?2), l AS (SELECT laps.* FROM laps JOIN s ON s.id = laps.session_id) ";

    public static StatResult Evaluate(StatsStore store, StatDef def, StatQuery q)
    {
        try
        {
            if (def.Compute is { } compute) return compute(store, q);
            var rows = store.Db.Query(Scope + def.Sql!.Replace("{league}", q.LeagueSql), q.From, q.To);
            if (rows.Count == 0 || rows[0][0] is null)
                return def.ZeroWhenEmpty ? new(StatFormat.Value(def.Kind, 0, q.Imperial)) : StatResult.None;
            object? main = rows[0][0], second = rows[0].Length > 1 ? rows[0][1] : null;
            if (def.Span)
                return second is null ? new(StatFormat.Value(def.Kind, Convert.ToDouble(main), q.Imperial))
                    : new($"{StatFormat.Number(def.Kind, Convert.ToDouble(main), q.Imperial)}–{StatFormat.Value(def.Kind, Convert.ToDouble(second), q.Imperial)}");
            string value = main is string text ? text : StatFormat.Value(def.Kind, Convert.ToDouble(main), q.Imperial);
            string? detail = second switch
            {
                null => null,
                string t => t,
                _ => StatFormat.Value(def.DetailKind, Convert.ToDouble(second), q.Imperial) + (def.DetailSuffix is { } suffix ? " " + suffix : ""),
            };
            return new(value, detail);
        }
        catch (Exception ex)
        {
            App.LogError(new InvalidOperationException($"Stat {def.Id} failed.", ex));
            return StatResult.None;
        }
    }

    private static StatResult Fixed(StatsStore store, StatQuery q, StatRange range)
    {
        var (from, to) = TimeRanges.Of(range, q.NowUtc, null);
        return store.Db.Scalar("SELECT SUM(drive_s) FROM sessions WHERE start_utc >= ?1 AND start_utc < ?2", StatsStore.Unix(from), StatsStore.Unix(to)) is { } v
            ? new(StatFormat.Value(StatKind.Duration, Convert.ToDouble(v), q.Imperial)) : new(StatFormat.Value(StatKind.Duration, 0, q.Imperial));
    }

    private static StatResult Streak(StatsStore store, StatQuery q)
    {
        var days = store.Db.Query("SELECT start_utc FROM sessions WHERE drive_s >= 60")
            .Select(r => DateOnly.FromDateTime(StatsStore.FromUnix(Convert.ToInt64(r[0])).ToLocalTime())).Distinct().ToList();
        var (current, best) = Streaks(days, DateOnly.FromDateTime(q.NowUtc.ToLocalTime()));
        return best == 0 ? StatResult.None : new(Days(current), "best " + Days(best));
    }

    private static string Days(int n) => n == 1 ? "1 day" : $"{n} days";

    internal static (int Current, int Best) Streaks(IEnumerable<DateOnly> days, DateOnly today)
    {
        var set = days.ToHashSet();
        int best = 0;
        foreach (var d in set)
        {
            if (set.Contains(d.AddDays(-1))) continue;
            int run = 1;
            while (set.Contains(d.AddDays(run))) run++;
            best = Math.Max(best, run);
        }
        var end = set.Contains(today) ? today : today.AddDays(-1);
        int current = 0;
        while (set.Contains(end.AddDays(-current))) current++;
        return (current, best);
    }

    internal static double StdDev(IReadOnlyCollection<double> xs)
    {
        double mean = xs.Average();
        return Math.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / xs.Count);
    }
}

internal static class TimeRanges
{
    internal static readonly DateTime CycleStart = new(2024, 12, 17, 0, 0, 0, DateTimeKind.Utc);
    private const int SeasonDays = 91;

    public static (DateTime From, DateTime To) Of(StatRange range, DateTime nowUtc, (DateTime Start, int Week)? official)
    {
        var local = nowUtc.ToLocalTime();
        switch (range)
        {
            case StatRange.Week:
                var monday = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
                return (monday.ToUniversalTime(), monday.AddDays(7).ToUniversalTime());
            case StatRange.Month:
                var first = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Local);
                return (first.ToUniversalTime(), first.AddMonths(1).ToUniversalTime());
            case StatRange.Season:
                var start = SeasonStart(nowUtc, official);
                return (start, start.AddDays(SeasonDays));
            default:
                return (DateTime.UnixEpoch, new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
    }

    internal static DateTime SeasonStart(DateTime nowUtc, (DateTime Start, int Week)? official)
    {
        int n = (int)Math.Floor((nowUtc - CycleStart).TotalDays / SeasonDays);
        var cycle = CycleStart.AddDays(n * SeasonDays);
        if (official is { } o && o.Week is >= 0 and <= 12)
        {
            var day = o.Start.Date;
            var tuesday = day.AddDays(-(((int)day.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7));
            var start = DateTime.SpecifyKind(tuesday.AddDays(-7 * o.Week), DateTimeKind.Utc);
            if (Math.Abs((start - cycle).TotalDays) <= 10 && nowUtc >= start && nowUtc < start.AddDays(SeasonDays)) return start;
        }
        return cycle;
    }
}

internal static class StatFormat
{
    private static readonly CultureInfo C = CultureInfo.CurrentCulture;

    public static string Value(StatKind kind, double v, bool imperial) => kind switch
    {
        StatKind.Duration => Duration(v),
        StatKind.Count => Math.Round(v).ToString("N0", C),
        StatKind.Distance => imperial ? $"{v * 0.621371:N0} mi" : $"{v:N0} km",
        StatKind.Speed => imperial ? $"{v * 0.621371:0} mph" : $"{v:0} km/h",
        StatKind.Fuel => imperial ? $"{v * 0.264172:N1} gal" : $"{v:N1} L",
        StatKind.Temp => imperial ? $"{v * 9 / 5 + 32:0} °F" : $"{v:0} °C",
        StatKind.LapTime => LapTime(v),
        StatKind.LapDelta => (v < 0 ? "−" : "+") + Math.Abs(v).ToString("0.000", C) + " s",
        StatKind.Seconds => v.ToString(v < 10 ? "0.000" : "0.0", C) + " s",
        StatKind.Percent => $"{v:0}%",
        StatKind.Position => "P" + v.ToString("0.0", C),
        StatKind.Signed => (v > 0 ? "+" : v < 0 ? "−" : "") + Math.Abs(Math.Round(v)).ToString("N0", C),
        StatKind.Rating => (v > 0 ? "+" : v < 0 ? "−" : "") + Math.Abs(v).ToString("0.00", C),
        StatKind.PerHour => v.ToString("0.0", C) + "x / h",
        StatKind.Decimal => v.ToString("0.0", C),
        StatKind.Gs => v.ToString("0.00", C) + " g",
        _ => v.ToString("0.##", C),
    };

    public static string Number(StatKind kind, double v, bool imperial) => kind == StatKind.Temp ? (imperial ? v * 9 / 5 + 32 : v).ToString("0", C) : Value(kind, v, imperial);

    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t == TimeSpan.Zero) return "0m";
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:00}s" : $"{t.Seconds}s";
    }

    public static string LapTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds:000}" : $"{t.Seconds}.{t.Milliseconds:000}";
    }
}

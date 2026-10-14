using System.Globalization;
using System.Text.Json;

namespace GoodieBag.Grr;

public sealed record GrrClass(string Key, string Label);

public sealed record GrrStanding(int Rank, string Driver, double Points)
{
    public string? Car { get; init; }
    public string? Team { get; init; }
    public int? Starts { get; init; }
    public int? Wins { get; init; }
    public int? Podiums { get; init; }
    public int? Top5 { get; init; }
    public int? Top10 { get; init; }
    public int? Poles { get; init; }
    public int? LapsLed { get; init; }
}

public sealed record GrrRound(int? Number, DateTime Start, string Track)
{
    public string? Key { get; init; }
    public string? Format { get; init; }
    public int? Laps { get; init; }
    public IReadOnlyList<(string Class, string Driver)> Winners { get; init; } = Array.Empty<(string, string)>();
    public string? Pole { get; init; }
    public bool Done { get; init; }

    public string Title => Number is { } n ? $"Round {n}" : Track;
}

public sealed record GrrResult(int Position, string Driver)
{
    public string? Class { get; init; }
    public string? Car { get; init; }
    public int? Start { get; init; }
    public string? Interval { get; init; }
    public int? Laps { get; init; }
    public int? Led { get; init; }
    public double? Points { get; init; }
    public int? Incidents { get; init; }
    public double? Penalty { get; init; }
    public string? Status { get; init; }
    public bool FastestLap { get; init; }
    public bool Pole { get; init; }
}

public sealed record GrrSession(string Label, IReadOnlyList<GrrResult> Rows);

public sealed record GrrEvent(string? Key, DateTime Date, string Track, IReadOnlyList<GrrSession> Sessions);

public sealed record GrrClassResult(string Class, string? Winner, string? FastestLap);

public sealed record GrrLeagueData(string League)
{
    public string? Season { get; init; }
    public IReadOnlyList<GrrClass> Classes { get; init; } = Array.Empty<GrrClass>();
    public IReadOnlyList<GrrRound>? Schedule { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<GrrStanding>>? Standings { get; init; }
    public IReadOnlyList<GrrEvent>? Events { get; init; }

    public IEnumerable<GrrRace> Races => (Schedule ?? Array.Empty<GrrRound>())
        .Select(r => new GrrRace(League, r.Start, r.Track, r.Number is { } n ? $"Round {n}" : null));

    public string ClassLabel(string key) =>
        key.Length == 0 ? "" : Classes.FirstOrDefault(c => c.Key == key)?.Label ?? key.ToUpperInvariant();

    public IEnumerable<string> StandingKeys => (Standings?.Keys ?? Enumerable.Empty<string>()).OrderBy(k =>
    {
        int i = Classes.ToList().FindIndex(c => c.Key == k);
        return i < 0 ? int.MaxValue : i;
    });

    public (string Class, GrrStanding Row)? StandingOf(string? driver)
    {
        if (driver is null || Standings is null) return null;
        foreach (var key in StandingKeys)
            if (Standings[key].FirstOrDefault(r => GrrFeed.SameDriver(r.Driver, driver)) is { } row)
                return (key, row);
        return null;
    }

    public GrrEvent? EventFor(GrrRound round) =>
        Events?.FirstOrDefault(e => e.Key is not null && e.Key == round.Key)
        ?? Events?.FirstOrDefault(e => e.Date.Date == round.Start.Date);

    public GrrRound? RoundFor(GrrEvent e) =>
        Schedule?.FirstOrDefault(r => e.Key is not null && r.Key == e.Key)
        ?? Schedule?.FirstOrDefault(r => r.Start.Date == e.Date.Date);

    public IReadOnlyList<GrrClassResult> ClassResults(GrrRound round)
    {
        var rows = new Dictionary<string, List<GrrResult>>(StringComparer.OrdinalIgnoreCase);
        void Add(string cls, GrrResult r) { if (!rows.TryGetValue(cls, out var l)) rows[cls] = l = new(); l.Add(r); }
        if (EventFor(round) is { } ev)
        {
            if (ev.Sessions.FirstOrDefault(s => s.Rows.Any(r => r.Class is not null)) is { } overall)
                foreach (var r in overall.Rows) Add(r.Class ?? "", r);
            else if (ev.Sessions.Any(s => ClassNamed(s.Label) is not null))
                foreach (var s in ev.Sessions)
                {
                    if (ClassNamed(s.Label) is { } c) foreach (var r in s.Rows) Add(c, r);
                }
            else if (ev.Sessions.FirstOrDefault(s => s.Rows.Count > 0) is { } table)
                foreach (var r in table.Rows) Add("", r);
        }
        var order = Classes.Select(c => c.Label).Concat(round.Winners.Select(w => w.Class)).Concat(rows.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var list = new List<GrrClassResult>();
        foreach (var cls in order)
        {
            rows.TryGetValue(cls, out var mine);
            string? winner = round.Winners.FirstOrDefault(w => string.Equals(w.Class, cls, StringComparison.OrdinalIgnoreCase)).Driver
                ?? mine?.MinBy(r => r.Position)?.Driver;
            string? fastest = mine?.FirstOrDefault(r => r.FastestLap)?.Driver;
            if (winner is not null || fastest is not null) list.Add(new GrrClassResult(cls, winner, fastest));
        }
        return list;
    }

    public string? ClassNamed(string label) => Classes.FirstOrDefault(c => string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase))?.Label;

    public GrrEvent? LastEvent => Events?.LastOrDefault(e => e.Sessions.Any(s => s.Rows.Count > 0));

    public static GrrResult? ResultOf(GrrEvent e, string? driver) =>
        driver is null ? null : e.Sessions.Select(s => s.Rows.FirstOrDefault(r => GrrFeed.SameDriver(r.Driver, driver))).FirstOrDefault(r => r is not null);
}

public static class GrrFeed
{
    private const string DefaultTime = "20:00";
    private const string DefaultZone = "America/New_York";

    public static GrrLeagueData Parse(string json, string league)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return new GrrLeagueData(league);

        var season = Obj(root, "season");
        var time = TimeSpan.TryParse(Str(season, "raceTime") ?? DefaultTime, CultureInfo.InvariantCulture, out var t) ? t : new TimeSpan(20, 0, 0);
        var zone = Zone(Str(season, "timezone") ?? DefaultZone);
        var classes = Try(() => Arr(root, "classes")?.Select(c => (Key: Str(c, "key"), Label: Str(c, "label"), Order: Int(c, "sortOrder") ?? 0))
            .Where(c => c.Key is not null).OrderBy(c => c.Order).Select(c => new GrrClass(c.Key!, c.Label ?? c.Key!)).ToList()) ?? new();

        return new GrrLeagueData(league)
        {
            Season = Str(season, "name"),
            Classes = classes,
            Schedule = Try(() => Schedule(root, time, zone, classes)),
            Standings = Try(() => Standings(root)),
            Events = Try(() => Events(root)),
        };
    }

    public static bool SameDriver(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Squash(a), Squash(b), StringComparison.OrdinalIgnoreCase);

    private static string Squash(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static List<GrrRound>? Schedule(JsonElement root, TimeSpan time, TimeZoneInfo zone, List<GrrClass> classes)
    {
        if (Arr(root, "schedule") is not { } items) return null;
        var rounds = new List<GrrRound>();
        foreach (var r in items)
        {
            if (!DateTime.TryParseExact(Str(r, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            var at = DateTime.SpecifyKind(day + time, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(at)) at = at.AddHours(1);
            var start = TimeZoneInfo.ConvertTime(at, zone, TimeZoneInfo.Local);
            var winners = new List<(string, string)>();
            if (Str(r, "winner") is { } w) winners.Add(("", w));
            foreach (var c in classes)
                if ((Str(r, c.Key) ?? Str(r, c.Key[(c.Key.LastIndexOf('-') + 1)..])) is { } cw) winners.Add((c.Label, cw));
            string? state = Str(r, "state"), status = Str(r, "status");
            rounds.Add(new GrrRound(Int(r, "round"), start, (Str(r, "track") ?? "").Trim())
            {
                Key = Str(r, "eventId") ?? Str(r, "id"),
                Format = Str(r, "format") is { } f && !f.Equals("standard", StringComparison.OrdinalIgnoreCase) ? f : null,
                Laps = Int(r, "laps"),
                Winners = winners,
                Pole = Str(r, "pole"),
                Done = state is "done" || status is "completed" || winners.Count > 0,
            });
        }
        return rounds.OrderBy(r => r.Start).ToList();
    }

    private static Dictionary<string, IReadOnlyList<GrrStanding>>? Standings(JsonElement root)
    {
        if (!root.TryGetProperty("standings", out var s)) return null;
        var tables = new Dictionary<string, IReadOnlyList<GrrStanding>>();
        if (s.ValueKind == JsonValueKind.Array) tables[""] = Rows(s);
        else if (s.ValueKind == JsonValueKind.Object)
            foreach (var p in s.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Array) tables[p.Name] = Rows(p.Value);
        return tables;

        static List<GrrStanding> Rows(JsonElement array) => array.EnumerateArray()
            .Where(r => Str(r, "driver") is not null)
            .Select((r, i) => new GrrStanding(Int(r, "rank") ?? i + 1, Str(r, "driver")!, Num(r, "points") ?? 0)
            {
                Car = Str(r, "car"), Team = Str(r, "team"), Starts = Int(r, "starts"), Wins = Int(r, "wins"), Podiums = Int(r, "podiums"),
                Top5 = Int(r, "top5"), Top10 = Int(r, "top10"), Poles = Int(r, "poles"), LapsLed = Int(r, "lapsLed"),
            })
            .OrderBy(r => r.Rank).ToList();
    }

    private static List<GrrEvent>? Events(JsonElement root)
    {
        if (Arr(root, "events") is not { } items) return null;
        var events = new List<GrrEvent>();
        foreach (var e in items)
        {
            var label = Str(e, "label") ?? "";
            int dash = label.LastIndexOf(" — ", StringComparison.Ordinal);
            string? dateText = Str(e, "date") ?? (dash > 0 ? label[(dash + 3)..].Trim() : null);
            if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            string track = Str(e, "track") ?? (dash > 0 ? label[..dash].Trim() : label);
            var sessions = new List<GrrSession>();
            foreach (var s in Arr(e, "sessions") ?? Enumerable.Empty<JsonElement>())
            {
                var rows = (Arr(s, "rows") ?? Enumerable.Empty<JsonElement>())
                    .Where(r => Str(r, "driver") is not null && Int(r, "position") is not null)
                    .Select(r => new GrrResult(Int(r, "position")!.Value, Str(r, "driver")!)
                    {
                        Class = Str(r, "class"), Car = Str(r, "car"), Start = Int(r, "start"), Interval = Str(r, "interval"), Laps = Int(r, "laps"),
                        Led = Int(r, "led"), Points = Num(r, "total") ?? Num(r, "racePoints"), Incidents = Int(r, "incidents"), Penalty = Num(r, "penalty"), Status = Str(r, "status"),
                        FastestLap = Int(r, "fastestLap") > 0, Pole = Int(r, "pole") > 0,
                    })
                    .OrderBy(r => r.Position).ToList();
                sessions.Add(new GrrSession(Str(s, "label") ?? "Race", rows));
            }
            events.Add(new GrrEvent(Str(e, "sourceEventId"), date, track, sessions));
        }
        return events.OrderBy(e => e.Date).ToList();
    }

    private static T? Try<T>(Func<T?> read) where T : class
    {
        try { return read(); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or KeyNotFoundException) { return null; }
    }

    private static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Local; }
    }

    private static JsonElement Obj(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    private static IEnumerable<JsonElement>? Arr(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : null;

    private static string? Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
        && v.GetString()?.Trim() is { Length: > 0 } s && s is not ("—" or "-") ? s : null;

    private static double? Num(JsonElement e, string key)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : null;
    }

    private static int? Int(JsonElement e, string key) => Num(e, key) is { } d ? (int)Math.Round(d) : null;
}

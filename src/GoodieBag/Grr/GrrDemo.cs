namespace GoodieBag.Grr;

internal static class GrrDemo
{
    private static readonly string[] Drivers =
    {
        "Alex Moreno", "Jamie Fox", "Riley Chen", "Morgan Hale", "Sam Driver", "Casey Brooks", "Drew Patel", "Taylor Nash",
        "Jordan Pike", "Quinn Avery", "Reese Long", "Parker Lane", "Skyler Webb", "Rowan Ellis", "Emerson Gray", "Hayden Cole",
    };

    public static List<GrrLeagueData> Leagues(DateTime now) => new()
    {
        League("cup", "GRR Cup Series 2026", now, 1, 20, 0, 16, 11, Array.Empty<GrrClass>(), new[]
        {
            "Phoenix Raceway", "Atlanta Motor Speedway", "Las Vegas Motor Speedway", "Circuit of the Americas", "Richmond Raceway",
            "Martinsville Speedway", "Talladega Superspeedway", "Dover Motor Speedway", "Kansas Speedway", "Darlington Raceway",
            "Bristol Motor Speedway", "Daytona International Speedway", "Charlotte Motor Speedway", "Watkins Glen International",
            "Michigan International Speedway", "Homestead-Miami Speedway",
        }),
        League("gt", "GT League Season 9", now, 4, 19, 30, 12, 5,
            new[] { new GrrClass("gt3-am", "GT3 AM"), new GrrClass("gt3-pro", "GT3 Pro"), new GrrClass("gtp", "GTP") }, new[]
            {
                "Indianapolis Motor Speedway", "Fuji International Speedway", "Red Bull Ring", "Suzuka International Racing Course",
                "Spa-Francorchamps", "Road Atlanta", "Long Beach Street Circuit", "Sebring International Raceway",
                "Circuit Gilles Villeneuve", "Watkins Glen International", "Monza", "Mount Panorama Circuit",
            }),
        League("indycar", "IndyCar Season 1", now, 6, 20, 15, 12, 2, Array.Empty<GrrClass>(), new[]
        {
            "Texas Motor Speedway", "Barber Motorsports Park", "Long Beach Street Circuit", "Laguna Seca", "Road America",
            "Iowa Speedway", "Mid-Ohio", "Portland International Raceway", "Gateway", "Nashville Superspeedway",
            "Indianapolis Road Course", "Milwaukee Mile",
        }),
    };

    private static GrrLeagueData League(string id, string season, DateTime now, int nextInDays, int hour, int minute, int rounds, int run, GrrClass[] classes, string[] tracks)
    {
        var next = now.Date.AddDays(nextInDays).AddHours(hour).AddMinutes(minute);
        bool classed = classes.Length > 0;
        int field = classed ? 15 : 14;
        var schedule = new List<GrrRound>();
        var events = new List<GrrEvent>();
        var points = new Dictionary<string, (double Pts, int Wins, int Podiums, int Top5, int Top10, int Poles, int Led)>();
        for (int i = 0; i < rounds; i++)
        {
            int number = i + 1;
            var start = next.AddDays(7 * (i - run));
            bool done = i < run;
            var finish = done ? Finish(id.Length * 7 + i, field) : null;
            schedule.Add(new GrrRound(number, start, tracks[i % tracks.Length])
            {
                Key = $"{id}-{number}",
                Format = classed && number % 4 == 0 ? "endurance" : null,
                Laps = id == "indycar" ? 30 + number * 3 % 25 : null,
                Done = done,
                Winners = finish is null ? Array.Empty<(string, string)>()
                    : classed ? classes.Select((c, k) => (c.Label, finish.First(d => ClassOf(d, classes) == k))).ToArray()
                    : new[] { ("", finish[0]) },
                Pole = finish?[1],
            });
            if (finish is null) continue;
            var rows = finish.Select((d, p) => new GrrResult(p + 1, d)
            {
                Class = classed ? classes[ClassOf(d, classes)].Label : null,
                Car = classed ? CarOf(d, classes) : null,
                Start = (p * 5 + i) % field + 1,
                Interval = p == 0 ? "-" : $"+{p * 1.7 + i % 3:0.000}",
                Laps = 40 - (p > 11 ? 1 : 0),
                Led = p == 0 ? 22 : p == 1 ? 12 : p < 4 ? 3 : 0,
                Points = Math.Max(5, 50 - p * 3),
                Incidents = (p + i) % 6,
                Penalty = p == 6 && i % 2 == 0 ? 5 : 0,
                Status = p == field - 1 ? "Disconnected" : "Running",
                FastestLap = p == (i % 3) + 1,
                Pole = p == 1,
            }).ToList();
            foreach (var r in rows)
            {
                var s = points.GetValueOrDefault(r.Driver);
                points[r.Driver] = (s.Pts + r.Points!.Value, s.Wins + (r.Position == 1 ? 1 : 0), s.Podiums + (r.Position <= 3 ? 1 : 0),
                    s.Top5 + (r.Position <= 5 ? 1 : 0), s.Top10 + (r.Position <= 10 ? 1 : 0), s.Poles + (r.Pole ? 1 : 0), s.Led + (r.Led ?? 0));
            }
            if (i < run - 3) continue;
            var sessions = new List<GrrSession> { new(classed ? "Overall" : "Overall Race Finish", rows) };
            if (classed)
                sessions.AddRange(classes.Select((c, k) => new GrrSession(c.Label, rows.Where(r => r.Class == c.Label).Select((r, n) => r with { Position = n + 1 }).ToList())));
            else if (id == "cup")
                sessions.Add(new GrrSession("Stage 1", rows.OrderBy(r => (r.Position * 7 + i) % field).Select((r, n) => r with { Position = n + 1, Points = Math.Max(0, 10 - n) }).ToList()));
            events.Add(new GrrEvent($"{id}-{number}", start, tracks[i % tracks.Length], sessions));
        }

        IReadOnlyList<GrrStanding> Table(IEnumerable<string> drivers) => drivers
            .Select(d => (Driver: d, S: points.GetValueOrDefault(d)))
            .OrderByDescending(x => x.S.Pts).ThenBy(x => x.Driver)
            .Select((x, n) => new GrrStanding(n + 1, x.Driver, x.S.Pts)
            {
                Car = classed ? CarOf(x.Driver, classes) : null,
                Team = classed ? new[] { "Red River Racing", "Apex Hunters", "Late Brakers", "Blue Flag Motorsport" }[Array.IndexOf(Drivers, x.Driver) % 4] : null,
                Starts = run, Wins = x.S.Wins,
                Podiums = classed ? x.S.Podiums : null, Top5 = classed ? null : x.S.Top5, Top10 = classed ? null : x.S.Top10,
                Poles = classed ? null : x.S.Poles, LapsLed = classed ? null : x.S.Led,
            }).ToList();

        var field0 = Drivers.Take(field).ToList();
        var standings = classed
            ? classes.Select((c, k) => (c.Key, Table(field0.Where(d => ClassOf(d, classes) == k)))).ToDictionary(x => x.Key, x => x.Item2)
            : new Dictionary<string, IReadOnlyList<GrrStanding>> { [""] = Table(field0) };
        return new GrrLeagueData(id) { Season = season, Classes = classes, Schedule = schedule, Standings = standings, Events = events };
    }

    private static List<string> Finish(int seed, int field)
    {
        return Drivers.Take(field).OrderBy(d => (Array.IndexOf(Drivers, d) * 7 + seed * 13) % field + Array.IndexOf(Drivers, d) / 3.0).ToList();
    }

    private static int ClassOf(string driver, GrrClass[] classes) => Array.IndexOf(Drivers, driver) % classes.Length;

    private static string CarOf(string driver, GrrClass[] classes) => ClassOf(driver, classes) switch
    {
        0 => "Porsche 911 GT3 R (992)",
        1 => new[] { "Ferrari 296 GT3", "BMW M4 GT3", "Mercedes-AMG GT3" }[Array.IndexOf(Drivers, driver) % 3],
        _ => "Acura ARX-06 GTP",
    };
}

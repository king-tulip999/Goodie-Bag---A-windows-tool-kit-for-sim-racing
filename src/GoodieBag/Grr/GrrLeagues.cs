namespace GoodieBag.Grr;

public sealed record GrrLeague(string Id, string Name, string Page, string SchedulePage, string ResultsPage, string StandingsPage)
{
    public string Url(string page) => GrrLeagues.Site + page;

    public string Feed => GrrLeagues.Site + "/api/" + Id;
}

public sealed record GrrRace(string League, DateTime Start, string Track, string? Round = null);

public static class GrrLeagues
{
    public const string Site = "https://www.grassrootsracing.org";
    public const string SchedulePage = "/schedule";
    public const string DriverHistoryPage = "/driver-history";

    public static readonly IReadOnlyList<GrrLeague> All = new[]
    {
        new GrrLeague("cup", "Cup Series", "/pages/grr-cup-series", "/pages/cup-series-schedule", "/pages/cup-latest-race-results", "/pages/cupstandings"),
        new GrrLeague("gt", "GT League", "/pages/gt-league", "/pages/gt-schedule", "/pages/gt-race-results", "/pages/gt-standings"),
        new GrrLeague("indycar", "IndyCar", "/pages/indycar", "/pages/indycar-schedule", "/pages/indycar-results", "/pages/indycar-standings"),
    };

    public static GrrLeague? Find(string? id) => All.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));

    public static string Name(string id) => Find(id)?.Name ?? id;

    public static string Countdown(TimeSpan left) =>
        left <= TimeSpan.Zero ? "Racing now"
        : left.TotalDays >= 1 ? $"{(int)left.TotalDays}d {left.Hours:00}h {left.Minutes:00}m"
        : left.TotalHours >= 1 ? $"{left.Hours}h {left.Minutes:00}m {left.Seconds:00}s"
        : $"{left.Minutes}m {left.Seconds:00}s";

    public static string ShortCountdown(TimeSpan left) =>
        left <= TimeSpan.Zero ? "Racing now"
        : left.TotalDays >= 1 ? $"{(int)left.TotalDays}d {left.Hours:00}h"
        : left.TotalHours >= 1 ? $"{left.Hours}h {left.Minutes:00}m"
        : $"{Math.Max(1, left.Minutes)}m";

    public static string DriverUrl => Site + DriverHistoryPage;
}

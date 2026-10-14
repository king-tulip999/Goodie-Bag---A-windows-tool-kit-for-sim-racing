namespace GoodieBag.Grr;

public sealed class GrrSettings
{
    public List<string> Leagues { get; set; } = new() { "cup" };

    public string? DriverName { get; set; }

    public List<int> IracingLeagueIds { get; set; } = new();

    public string? DashboardLeague { get; set; }

    public string? DashboardClass { get; set; }

    public string? FeaturedRace { get; set; }

    public static string KeyOf(GrrRace race) => $"{race.League}|{race.Start:yyyy-MM-ddTHH:mm}";

    public List<GrrRace> Lead(IEnumerable<GrrRace> upcoming)
    {
        var list = upcoming.ToList();
        int i = FeaturedRace is null ? -1 : list.FindIndex(r => KeyOf(r) == FeaturedRace);
        if (i > 0)
        {
            var featured = list[i];
            list.RemoveAt(i);
            list.Insert(0, featured);
        }
        return list;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Normalized { get; private set; }

    public void Normalize()
    {
        Normalized = true;
        Leagues = (Leagues ?? new()).Where(id => GrrLeagues.Find(id) is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        DriverName = string.IsNullOrWhiteSpace(DriverName) ? null : DriverName.Trim();
        IracingLeagueIds = (IracingLeagueIds ?? new()).Where(id => id > 0).Distinct().ToList();
        if (GrrLeagues.Find(DashboardLeague) is null) DashboardLeague = null;
    }

    public static List<int> ParseIds(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\d+").Select(m => int.TryParse(m.Value, out int id) ? id : 0).Where(id => id > 0).Distinct().ToList();

    public string? DashboardLeagueOrNull() =>
        DashboardLeague is { } picked && Leagues.Contains(picked, StringComparer.OrdinalIgnoreCase) ? picked : Leagues.FirstOrDefault();
}

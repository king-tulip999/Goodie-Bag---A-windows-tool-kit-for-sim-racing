using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using GoodieBag.Config;

namespace GoodieBag.Grr;

internal sealed class GrrService
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(6);
    public static readonly TimeSpan RaceLength = TimeSpan.FromHours(2);
    public static readonly TimeSpan ManualGap = TimeSpan.FromSeconds(30);

    private static readonly HttpClient Http = CreateClient();
    private static string CacheFolder => Path.Combine(ConfigStore.Folder, "grr");
    private static string OldCacheFile => Path.Combine(ConfigStore.Folder, "grr-schedule.json");

    private readonly bool _demo;
    private readonly HashSet<string> _consumers = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(5) };
    private readonly Dictionary<string, GrrLeagueData> _data = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _fetched = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _problems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime At, int Count)> _failed = new(StringComparer.OrdinalIgnoreCase);
    private Task? _load;
    private DateTime _lastAttempt;
    private bool _loading, _fetching;

    public GrrService(bool demo)
    {
        _demo = demo;
        _timer.Tick += (_, _) => _ = RefreshIfStaleAsync();
        if (!demo) return;
        foreach (var league in GrrDemo.Leagues(DateTime.Now)) _data[league.League] = league;
        foreach (var l in GrrLeagues.All) _fetched[l.Id] = DateTime.Now.AddMinutes(-4);
        Races = BuildRaces();
        _load = Task.CompletedTask;
    }

    public IReadOnlyList<GrrRace> Races { get; private set; } = new List<GrrRace>();

    public GrrLeagueData? Data(string league) => _data.GetValueOrDefault(league);

    public DateTime? FetchedAt(string league) => _fetched.TryGetValue(league, out var t) ? t : null;

    public string? ProblemOf(string league) => _problems.GetValueOrDefault(league);

    public string? Problem => _problems.Count == 0 ? null : string.Join("\n", _problems.Select(p => $"{GrrLeagues.Name(p.Key)}: {p.Value}"));

    public bool Busy => _loading || _fetching;

    public bool CanRefresh => !_demo && !Busy && DateTime.Now - _lastAttempt >= ManualGap;

    private bool Stale => GrrLeagues.All.Any(l => IsStale(l.Id, DateTime.Now));

    private bool IsStale(string league, DateTime now) =>
        IsStale(FetchedAt(league), _failed.TryGetValue(league, out var f) ? f : null, now);

    internal static bool IsStale(DateTime? fetched, (DateTime At, int Count)? failed, DateTime now) =>
        now - (fetched ?? DateTime.MinValue) >= RefreshEvery
        && (failed is not { } f || now - f.At >= RetryAfter(f.Count));

    public static TimeSpan RetryAfter(int failures) =>
        TimeSpan.FromMinutes(Math.Min(RefreshEvery.TotalMinutes, 15 * Math.Pow(2, Math.Clamp(failures - 1, 0, 10))));

    public event Action? Changed;

    public void Acquire(string consumer)
    {
        if (!_consumers.Add(consumer) || _consumers.Count > 1) return;
        _timer.Start();
        _ = RefreshIfStaleAsync();
    }

    private async Task RefreshIfStaleAsync()
    {
        await (_load ??= LoadCacheAsync());
        if (Stale && _consumers.Count > 0) await RefreshAsync(onlyStale: true);
    }

    public void Release(string consumer)
    {
        if (_consumers.Remove(consumer) && _consumers.Count == 0) _timer.Stop();
    }

    public GrrRace? Next(string league, DateTime now) =>
        Races.FirstOrDefault(r => r.League == league && r.Start + RaceLength > now);

    public GrrRace? Last(string league, DateTime now) =>
        Races.LastOrDefault(r => r.League == league && r.Start + RaceLength <= now);

    public IEnumerable<DateTime> Starts(IEnumerable<string> leagues)
    {
        var set = leagues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Races.Where(r => set.Contains(r.League)).Select(r => r.Start);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GoodieBag/" + Services.UpdateService.Current);
        return client;
    }

    private List<GrrRace> BuildRaces() => _data.Values.SelectMany(d => d.Races).OrderBy(r => r.Start).ToList();

    private void Raise() => Changed?.Invoke();

    public void SettingsChanged() => Raise();

    private async Task LoadCacheAsync()
    {
        _loading = true;
        Raise();
        try
        {
            var read = await Task.Run(() =>
            {
                var list = new List<(GrrLeagueData Data, DateTime When)>();
                foreach (var league in GrrLeagues.All)
                {
                    var file = Path.Combine(CacheFolder, league.Id + ".json");
                    try
                    {
                        if (File.Exists(file)) list.Add((GrrFeed.Parse(File.ReadAllText(file), league.Id), File.GetLastWriteTime(file)));
                    }
                    catch (Exception) { }
                }
                return list;
            });
            foreach (var (data, when) in read)
                if (!_data.ContainsKey(data.League))
                {
                    _data[data.League] = data;
                    _fetched[data.League] = when;
                }
            Races = BuildRaces();
        }
        finally
        {
            _loading = false;
            Raise();
        }
    }

    public Task RefreshNowAsync() => CanRefresh ? RefreshAsync() : Task.CompletedTask;

    public async Task RefreshAsync(bool onlyStale = false)
    {
        if (_demo || _fetching) return;
        _fetching = true;
        _lastAttempt = DateTime.Now;
        Raise();
        try
        {
            await (_load ??= LoadCacheAsync());
            var now = DateTime.Now;
            var reads = GrrLeagues.All.Where(l => !onlyStale || IsStale(l.Id, now)).Select(async league =>
            {
                try
                {
                    var text = await Http.GetStringAsync(league.Feed);
                    var data = await Task.Run(() => GrrFeed.Parse(text, league.Id));
                    return (league, data, text, problem: data.Schedule is null && data.Standings is null ? "the feed had no schedule or standings" : null);
                }
                catch (Exception ex)
                {
                    return (league, data: (GrrLeagueData?)null, text: (string?)null, problem: ex switch
                    {
                        TaskCanceledException => "the GRR website didn't answer in time",
                        HttpRequestException => ex.Message,
                        _ => "the feed couldn't be read",
                    });
                }
            }).ToList();
            foreach (var (league, data, text, problem) in await Task.WhenAll(reads))
            {
                if (problem is null) { _problems.Remove(league.Id); _failed.Remove(league.Id); }
                else
                {
                    _problems[league.Id] = problem;
                    _failed[league.Id] = (DateTime.Now, _failed.TryGetValue(league.Id, out var f) ? f.Count + 1 : 1);
                }
                if (data is null || text is null || problem is not null) continue;
                _data[league.Id] = data;
                _fetched[league.Id] = DateTime.Now;
                Save(league.Id, text);
            }
            Races = BuildRaces();
        }
        finally
        {
            _fetching = false;
            Raise();
        }
    }

    private static void Save(string league, string text)
    {
        if (ConfigStore.ReadOnly) return;
        try
        {
            Directory.CreateDirectory(CacheFolder);
            File.WriteAllText(Path.Combine(CacheFolder, league + ".json"), text);
            if (File.Exists(OldCacheFile)) File.Delete(OldCacheFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

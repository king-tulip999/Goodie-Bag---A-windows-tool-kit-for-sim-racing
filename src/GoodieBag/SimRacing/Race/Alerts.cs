namespace GoodieBag.SimRacing.Race;

internal enum AlertLevel { Info, Good, Warning, Critical }

internal sealed record AlertDef(string Id, string Title, string Description, AlertLevel Level, bool OnByDefault, double? Threshold = null,
    string ThresholdUnit = "", double DefaultCooldown = 20, bool Sticky = false);

internal sealed class HudAlert(string id, string key, string title, string? detail, AlertLevel level, double raisedAt, bool sticky)
{
    public string Id { get; } = id;
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string? Detail { get; set; } = detail;
    public AlertLevel Level { get; } = level;
    public double RaisedAt { get; } = raisedAt;
    public bool Sticky { get; } = sticky;
    public bool Active { get; set; } = true;
}
internal readonly record struct AlertChoice(bool Enabled, double Cooldown, double? Threshold, double Duration);

internal static class AlertCatalog
{
    public static readonly AlertDef[] All =
    {
        new("fuel.short", "Not enough fuel to finish", "The fuel on board won't reach the flag at the recent rate (an estimate).", AlertLevel.Warning, true, DefaultCooldown: 120),
        new("fuel.low", "Fuel low", "Fuel left for fewer laps than the threshold.", AlertLevel.Critical, true, 2, "laps", 60, Sticky: true),
        new("pit.window", "Pit window open", "One more stop is now enough to finish.", AlertLevel.Info, true, DefaultCooldown: 600),
        new("pit.speed", "Pit speed limit exceeded", "Faster than the pit lane limit (plus the tolerance) on pit road.", AlertLevel.Critical, true, 0.5, "km/h", 2, Sticky: true),
        new("pit.limiter", "Pit limiter off", "On pit road without the pit limiter (only cars that report it).", AlertLevel.Warning, true, DefaultCooldown: 10, Sticky: true),
        new("traffic.faster", "Faster class approaching", "A car of a faster class is closer behind than the threshold and closing.", AlertLevel.Info, true, 2.0, "s", 25),
        new("traffic.slower", "Slower class ahead", "Catching a car of a slower class (seconds until you reach it).", AlertLevel.Info, false, 3.0, "s", 25),
        new("side.car", "Car alongside", "A call for every car alongside, from iRacing's spotter signal.", AlertLevel.Warning, false, DefaultCooldown: 0),
        new("flag.yellow", "Yellow / caution", "Yellow or full-course caution.", AlertLevel.Warning, true, DefaultCooldown: 15, Sticky: true),
        new("flag.blue", "Blue flag", "A faster car wants to lap you.", AlertLevel.Info, true, DefaultCooldown: 30),
        new("flag.penalty", "Penalty / black flag", "Black flag, repair (meatball) or disqualification.", AlertLevel.Critical, true, DefaultCooldown: 5, Sticky: true),
        new("flag.white", "Last lap", "The white flag: one lap to go.", AlertLevel.Info, true, DefaultCooldown: 60),
        new("incidents.limit", "Incident limit close", "Incident points within the threshold of the session's limit.", AlertLevel.Warning, true, 4, "points", 120),
        new("incident.new", "Incident points", "Incident points gained (how many).", AlertLevel.Info, true, DefaultCooldown: 3),
        new("lap.pb", "Personal best", "A new personal best lap this session.", AlertLevel.Good, true, DefaultCooldown: 5),
        new("lap.unclean", "Lap with incidents", "The last lap gained incident points.", AlertLevel.Info, false, DefaultCooldown: 5),
        new("pace.drop", "Pace dropping", "The last clean lap was slower than your recent average by more than the threshold.", AlertLevel.Info, false, 1.5, "%", 60),
        new("position.change", "Place gained / lost", "Your position in your class changed during the race.", AlertLevel.Info, false, DefaultCooldown: 3),
        new("weather.rain", "Rain", "It's raining harder than the threshold.", AlertLevel.Warning, true, 5, "%", 300),
        new("weather.wet", "Track wetness changed", "The track got wetter or dried out (iRacing's wetness level).", AlertLevel.Info, true, DefaultCooldown: 120),
        new("engine.warning", "Engine warning", "Water / oil temperature or pressure, or fuel pressure warning from the car.", AlertLevel.Critical, true, DefaultCooldown: 30, Sticky: true),
        new("stint.limit", "Stint time", "Your stint (since leaving the pits) reaches Longest stint on the Fuel tab (threshold = minutes before).", AlertLevel.Warning, true, 5, "min", 300),
        new("telemetry.lost", "Telemetry lost", "Data stopped arriving while driving.", AlertLevel.Warning, true, DefaultCooldown: 30, Sticky: true),
    };

    public static AlertDef? Find(string id) => All.FirstOrDefault(a => a.Id == id);

    public static readonly (string Id, string Title, string Icon, string[] Alerts)[] Groups =
    {
        ("fuel", "Fuel & strategy", "I.Gauge", new[] { "fuel.low", "fuel.short" }),
        ("pit", "Pit & stint", "I.Clock", new[] { "pit.window", "pit.speed", "pit.limiter", "stint.limit" }),
        ("traffic", "Traffic & spotter", "I.Target", new[] { "traffic.faster", "traffic.slower", "side.car" }),
        ("flags", "Flags & penalties", "I.Flag", new[] { "flag.yellow", "flag.blue", "flag.white", "flag.penalty", "incidents.limit", "incident.new" }),
        ("laps", "Laps & position", "I.Trophy", new[] { "lap.pb", "lap.unclean", "pace.drop", "position.change" }),
        ("weather", "Weather", "I.Sun", new[] { "weather.rain", "weather.wet" }),
        ("car", "Car & telemetry", "I.Alert", new[] { "engine.warning", "telemetry.lost" }),
    };
}

internal sealed class AlertEngine
{
    public const int HistorySize = 100;
    private readonly Func<AlertDef, AlertChoice> _choice;
    private readonly Dictionary<string, double> _lastRaised = new();
    private readonly Dictionary<string, bool> _condition = new();
    private readonly List<HudAlert> _visible = new();
    private readonly List<HudAlert> _history = new();
    private readonly HashSet<string> _dismissed = new();

    public AlertEngine(Func<AlertDef, AlertChoice> choice) => _choice = choice;

    public event Action<HudAlert, AlertChoice>? Raised;

    public IReadOnlyList<HudAlert> History => _history;

    public AlertChoice ChoiceFor(string id) => AlertCatalog.Find(id) is { } def ? _choice(def) : default;

    public double Threshold(string id) => AlertCatalog.Find(id) is { } def ? _choice(def).Threshold ?? def.Threshold ?? 0 : 0;

    public void Set(string id, bool active, double now, string? title = null, string? detail = null, string? key = null)
    {
        string k = key ?? id;
        bool was = _condition.TryGetValue(k, out var c) && c;
        _condition[k] = active;
        if (active && !was) Raise(id, k, now, title, detail, null);
        else if (!active && was)
        {
            _dismissed.Remove(k);
            foreach (var a in _visible) if (a.Key == k) a.Active = false;
        }
        else if (active && detail is not null)
            foreach (var a in _visible) if (a.Key == k) a.Detail = detail;
    }

    public void Fire(string id, double now, string? title = null, string? detail = null, string? key = null) =>
        Raise(id, key ?? id, now, title, detail, sticky: false);

    private void Raise(string id, string key, double now, string? title, string? detail, bool? sticky)
    {
        if (AlertCatalog.Find(id) is not { } def) return;
        var choice = _choice(def);
        if (!choice.Enabled) return;
        if (_lastRaised.TryGetValue(key, out var last) && now - last < choice.Cooldown) return;
        _lastRaised[key] = now;
        var alert = new HudAlert(id, key, title ?? def.Title, detail, def.Level, now, sticky ?? def.Sticky);
        _visible.RemoveAll(a => a.Key == key);
        _visible.Add(alert);
        _history.Add(alert);
        if (_history.Count > HistorySize) _history.RemoveAt(0);
        Raised?.Invoke(alert, choice);
    }

    public IReadOnlyList<HudAlert> Visible(double now)
    {
        _visible.RemoveAll(a => a.Sticky ? !a.Active || _dismissed.Contains(a.Key) : now - a.RaisedAt > ChoiceFor(a.Id).Duration);
        _sorted.Clear();
        _sorted.AddRange(_visible);
        _sorted.Sort(static (a, b) => Rank(b.Level) != Rank(a.Level) ? Rank(b.Level).CompareTo(Rank(a.Level)) : b.RaisedAt.CompareTo(a.RaisedAt));
        return _sorted;
    }

    private readonly List<HudAlert> _sorted = new();

    private static int Rank(AlertLevel l) => l switch { AlertLevel.Critical => 3, AlertLevel.Warning => 2, _ => 1 };

    public void DismissAll()
    {
        foreach (var a in _visible.Where(a => a.Sticky)) _dismissed.Add(a.Key);
        _visible.Clear();
    }

    public void ResetConditions()
    {
        _condition.Clear();
        _dismissed.Clear();
        _visible.Clear();
    }
}
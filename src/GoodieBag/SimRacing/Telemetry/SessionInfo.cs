namespace GoodieBag.SimRacing.Telemetry;

internal enum SessionKind { Other, Practice, Qualify, Race, TimeTrial }

internal sealed record DriverEntry
{
    public int CarIdx { get; init; }
    public string Name { get; init; } = "";
    public string AbbrevName { get; init; } = "";
    public string Initials { get; init; } = "";
    public string TeamName { get; init; } = "";
    public long UserId { get; init; }
    public long TeamId { get; init; }
    public string CarNumber { get; init; } = "";
    public string CarName { get; init; } = "";
    public string CarShortName { get; init; } = "";
    public string CarPath { get; init; } = "";
    public int ClassId { get; init; }
    public string ClassName { get; init; } = "";
    public int ClassColor { get; init; }
    public double? ClassEstLapTime { get; init; }
    public int? IRating { get; init; }
    public string License { get; init; } = "";
    public bool IsSpectator { get; init; }
    public bool IsPaceCar { get; init; }
    public bool IsAi { get; init; }
    public int? Incidents { get; init; }
    public int? TeamIncidents { get; init; }

    public bool Races => !IsSpectator && !IsPaceCar;
}

internal sealed record SessionEntry(int Number, SessionKind Kind, string Type, string Name, int? Laps, double? TimeSeconds, string Rubber = "");

internal sealed record SectorEntry(int Number, double StartPct);

internal sealed class SessionInfo
{
    public static readonly SessionInfo Empty = new();

    public string TrackName { get; init; } = "";
    public string TrackDisplayName { get; init; } = "";
    public string TrackConfig { get; init; } = "";
    public int? TrackId { get; init; }
    public double? TrackLengthKm { get; init; }
    public double? PitSpeedLimitKph { get; init; }
    public int? IncidentLimit { get; init; }
    public string EventType { get; init; } = "";
    public string Category { get; init; } = "";
    public int? SeriesId { get; init; }
    public int? SeasonId { get; init; }
    public int? RaceWeek { get; init; }
    public int? LeagueId { get; init; }
    public long? SubSessionId { get; init; }
    public bool Official { get; init; }
    public string WeatherType { get; init; } = "";
    public string Skies { get; init; } = "";
    public string SimDate { get; init; } = "";
    public bool TeamRacing { get; init; }
    public int NumCarClasses { get; init; } = 1;

    public IReadOnlyList<SessionEntry> Sessions { get; init; } = Array.Empty<SessionEntry>();
    public IReadOnlyList<DriverEntry> Drivers { get; init; } = Array.Empty<DriverEntry>();
    public IReadOnlyList<SectorEntry> Sectors { get; init; } = Array.Empty<SectorEntry>();

    public IReadOnlyDictionary<int, DriverEntry> ByCar { get; init; } = new Dictionary<int, DriverEntry>();

    public IReadOnlyDictionary<int, int> GridPositions { get; init; } = new Dictionary<int, int>();

    public int PlayerCarIdx { get; init; } = -1;
    public double? FuelMaxLiters { get; init; }
    public double? MaxFuelPct { get; init; }

    public double? PlayerEstLapTime { get; init; }

    public double? ShiftLightFirstRpm { get; init; }
    public double? ShiftRpm { get; init; }
    public string PlayerSetupName { get; init; } = "";

    public DriverEntry? Player => ByCar.TryGetValue(PlayerCarIdx, out var d) ? d : null;

    public SessionEntry? Session(int number) => Sessions.FirstOrDefault(s => s.Number == number);

    public static SessionKind KindOf(string type)
    {
        string t = type.ToLowerInvariant();
        return t.Contains("race") ? SessionKind.Race
            : t.Contains("qualif") ? SessionKind.Qualify
            : t.Contains("time trial") ? SessionKind.TimeTrial
            : t.Contains("practice") || t.Contains("testing") || t.Contains("test drive") || t.Contains("offline") || t.Contains("warmup") || t.Contains("warm up") ? SessionKind.Practice
            : SessionKind.Other;
    }

    public static SessionInfo Parse(string yaml)
    {
        var root = SessionYaml.Parse(yaml);
        var weekend = SessionYaml.At(root, "WeekendInfo");
        var driverInfo = SessionYaml.At(root, "DriverInfo");

        var sessions = SessionYaml.Items(root, "SessionInfo", "Sessions").Select(s =>
        {
            string type = SessionYaml.Str(s, "SessionType") ?? "";
            return new SessionEntry(SessionYaml.Int(s, "SessionNum") ?? 0, KindOf(type), type, SessionYaml.Str(s, "SessionName") ?? type,
                SessionYaml.Int(s, "SessionLaps"), SessionYaml.Num(s, "SessionTime"), SessionYaml.Str(s, "SessionTrackRubberState") ?? "");
        }).ToList();

        var drivers = SessionYaml.Items(driverInfo, "Drivers").Select(d => new DriverEntry
        {
            CarIdx = SessionYaml.Int(d, "CarIdx") ?? -1,
            Name = SessionYaml.Str(d, "UserName") ?? "",
            AbbrevName = SessionYaml.Str(d, "AbbrevName") ?? "",
            Initials = SessionYaml.Str(d, "Initials") ?? "",
            TeamName = SessionYaml.Str(d, "TeamName") ?? "",
            UserId = (long)(SessionYaml.Num(d, "UserID") ?? 0),
            TeamId = (long)(SessionYaml.Num(d, "TeamID") ?? 0),
            CarNumber = SessionYaml.Str(d, "CarNumber") ?? "",
            CarName = SessionYaml.Str(d, "CarScreenName") ?? "",
            CarShortName = SessionYaml.Str(d, "CarScreenNameShort") ?? SessionYaml.Str(d, "CarScreenName") ?? "",
            CarPath = SessionYaml.Str(d, "CarPath") ?? "",
            ClassId = SessionYaml.Int(d, "CarClassID") ?? 0,
            ClassName = SessionYaml.Str(d, "CarClassShortName") ?? "",
            ClassColor = SessionYaml.Int(d, "CarClassColor") ?? 0,
            ClassEstLapTime = SessionYaml.Num(d, "CarClassEstLapTime") is > 0 and var est ? est : null,
            IRating = SessionYaml.Int(d, "IRating") is > 0 and var ir ? ir : null,
            License = SessionYaml.Str(d, "LicString") ?? "",
            IsSpectator = SessionYaml.Int(d, "IsSpectator") == 1,
            IsPaceCar = SessionYaml.Int(d, "CarIsPaceCar") == 1,
            IsAi = SessionYaml.Int(d, "CarIsAI") == 1,
            Incidents = SessionYaml.Int(d, "CurDriverIncidentCount"),
            TeamIncidents = SessionYaml.Int(d, "TeamIncidentCount"),
        }).Where(d => d.CarIdx >= 0).ToList();
        var byCar = new Dictionary<int, DriverEntry>();
        foreach (var d in drivers) byCar[d.CarIdx] = d;

        var sectors = SessionYaml.Items(root, "SplitTimeInfo", "Sectors")
            .Select(s => new SectorEntry(SessionYaml.Int(s, "SectorNum") ?? 0, Math.Clamp(SessionYaml.Num(s, "SectorStartPct") ?? 0, 0, 1)))
            .OrderBy(s => s.StartPct).ToList();

        var grid = new Dictionary<int, int>();
        foreach (var r in SessionYaml.Items(root, "QualifyResultsInfo", "Results"))
            if (SessionYaml.Int(r, "CarIdx") is { } car && SessionYaml.Int(r, "Position") is { } pos)
                grid[car] = pos + 1;

        return new SessionInfo
        {
            TrackName = SessionYaml.Str(weekend, "TrackName") ?? "",
            TrackDisplayName = SessionYaml.Str(weekend, "TrackDisplayName") ?? SessionYaml.Str(weekend, "TrackName") ?? "",
            TrackConfig = SessionYaml.Str(weekend, "TrackConfigName") ?? "",
            TrackId = SessionYaml.Int(weekend, "TrackID"),
            TrackLengthKm = SessionYaml.Num(weekend, "TrackLength") is > 0 and var km ? km : null,
            PitSpeedLimitKph = SessionYaml.Num(weekend, "TrackPitSpeedLimit") is > 0 and var pit ? pit : null,
            IncidentLimit = SessionYaml.Str(weekend, "WeekendOptions", "IncidentLimit") is { } lim && !lim.Equals("unlimited", StringComparison.OrdinalIgnoreCase)
                ? SessionYaml.Int(weekend, "WeekendOptions", "IncidentLimit") : null,
            EventType = SessionYaml.Str(weekend, "EventType") ?? "",
            Category = SessionYaml.Str(weekend, "Category") ?? "",
            SeriesId = SessionYaml.Int(weekend, "SeriesID"),
            SeasonId = SessionYaml.Int(weekend, "SeasonID") is > 0 and var season ? season : null,
            RaceWeek = SessionYaml.Int(weekend, "RaceWeek"),
            LeagueId = SessionYaml.Int(weekend, "LeagueID") is > 0 and var league ? league : null,
            SubSessionId = SessionYaml.Num(weekend, "SubSessionID") is > 0 and var sub ? (long)sub : null,
            Official = SessionYaml.Int(weekend, "Official") == 1,
            WeatherType = SessionYaml.Str(weekend, "TrackWeatherType") ?? "",
            Skies = SessionYaml.Str(weekend, "TrackSkies") ?? "",
            SimDate = SessionYaml.Str(weekend, "WeekendOptions", "Date") ?? "",
            TeamRacing = SessionYaml.Int(weekend, "TeamRacing") == 1,
            NumCarClasses = Math.Max(1, SessionYaml.Int(weekend, "NumCarClasses") ?? drivers.Where(d => d.Races).Select(d => d.ClassId).Distinct().Count()),
            Sessions = sessions,
            Drivers = drivers,
            ByCar = byCar,
            Sectors = sectors,
            GridPositions = grid,
            PlayerCarIdx = SessionYaml.Int(driverInfo, "DriverCarIdx") ?? -1,
            FuelMaxLiters = SessionYaml.Num(driverInfo, "DriverCarFuelMaxLtr") is > 0 and var fuel ? fuel : null,
            MaxFuelPct = SessionYaml.Num(driverInfo, "DriverCarMaxFuelPct") is > 0 and var maxPct ? maxPct : null,
            PlayerEstLapTime = SessionYaml.Num(driverInfo, "DriverCarEstLapTime") is > 0 and var lap ? lap : null,
            ShiftLightFirstRpm = SessionYaml.Num(driverInfo, "DriverCarSLFirstRPM") is > 0 and var slFirst ? slFirst : null,
            ShiftRpm = SessionYaml.Num(driverInfo, "DriverCarSLShiftRPM") is > 0 and var slShift ? slShift : null,
            PlayerSetupName = SessionYaml.Str(driverInfo, "DriverSetupName") ?? "",
        };
    }
}

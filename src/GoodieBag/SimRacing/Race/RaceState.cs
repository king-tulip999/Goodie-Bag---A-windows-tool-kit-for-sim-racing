using System.Diagnostics;
using GoodieBag.Config;
using GoodieBag.SimRacing.FuelEngine;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Race;

internal readonly record struct RaceEvent(double SessionTime, string Text, AlertLevel Level);

internal readonly record struct PitStop(int Lap, double LaneTime, double? StallTime);

internal sealed class RaceState
{
    public const int MaxEvents = 200;

    private readonly Func<RaceHudSettings> _settings;
    private readonly Spotter _spotter = new();

    public EstProfile Profile { get; } = new();
    private SessionInfo? _keysFrom;
    private int? _keysNum;
    private readonly List<RaceEvent> _events = new();
    private readonly Dictionary<int, int> _startPositions = new();
    private List<StandingRow> _standings = new();
    private long _standingsSeq = -1;
    private string _sessionKey = "", _fuelIdentity = "";
    private double _nextFuelPlanAt = double.NegativeInfinity;
    private int _fuelLapsPlanned = -1;
    private long _lastSeq = -1;
    private double _lastChecks = double.NegativeInfinity;
    private int _lastIncidents = -1, _lastClassPos, _lastWetness = -1;
    private RaceFlags _lastFlags;
    private bool _wasOnPitRoad, _wasStall, _wasConnected, _lapsPbKnown;
    private double _pitEnteredAt = -1, _stallAt = -1, _stallTime;
    private int _lastPosition;
    private double? _bestLap;
    private bool _windowWasOpen;

    public RaceState(Func<RaceHudSettings> settings)
    {
        _settings = settings;
        Alerts = new AlertEngine(def => ChoiceFor(settings(), def));
        Laps.LapCompleted += OnLap;
    }

    public TelemetryFrame Frame { get; private set; } = new();

    public double Now { get; private set; }

    public GapHistory Gaps { get; } = new();
    public FuelModel FuelData { get; } = new();
    public LapTracker Laps { get; } = new();
    public AlertEngine Alerts { get; }

    public (SpotterCall Call, double At) LastCall { get; private set; }

    public int SelectedCar { get; set; } = -1;

    public IReadOnlyList<RaceEvent> Events => _events;

    public IReadOnlyList<PitStop> PitStops => _pitStops;
    private readonly List<PitStop> _pitStops = new();

    private int? _fuelLap;
    private double? _lapStartFuel;

    public double? FuelThisLap => _lapStartFuel is { } start && Frame.Fuel is { } now ? Math.Max(0, start - now) : null;

    public double FuelThisLapFrom { get; private set; }

    public double? PitTimer => Frame.OnPitRoad && _pitEnteredAt >= 0 && Frame.SessionTime is { } t ? t - _pitEnteredAt : null;

    public double? StintStart { get; private set; }
    public int StintStartLap { get; private set; }

    public FuelPlan? Fuel { get; private set; }

    public double? FuelPerLap { get; private set; }

    public double? Tank => Frame.Session.FuelMaxLiters is { } max ? max * (Frame.Session.MaxFuelPct ?? 1) : null;

    public IReadOnlyList<StandingRow> Standings
    {
        get
        {
            if (_standingsSeq != Frame.Sequence)
            {
                _standings = FieldOrder.Standings(Frame, Frame.SessionKind == SessionKind.Race);
                _standingsSeq = Frame.Sequence;
            }
            return _standings;
        }
    }

    private readonly double?[] _irChange = new double?[TelemetryFrame.MaxCars];
    private double _irChangeAt = double.NegativeInfinity;
    private readonly List<(int Rating, int Position)> _irField = new();
    private readonly List<int> _irCars = new();

    public double? IRatingChangeOf(int car)
    {
        if ((uint)car >= TelemetryFrame.MaxCars || Frame.SessionKind != SessionKind.Race) return null;
        if (Now - _irChangeAt >= 1)
        {
            _irChangeAt = Now;
            Array.Clear(_irChange);
            var standings = Standings;
            foreach (int cls in standings.Select(r => FieldOrder.ClassOf(Frame, r.CarIdx)).Distinct().ToList())
            {
                _irField.Clear();
                _irCars.Clear();
                foreach (var r in standings)
                    if (FieldOrder.ClassOf(Frame, r.CarIdx) == cls && r.ClassPosition > 0 && Frame.Session.ByCar.TryGetValue(r.CarIdx, out var d) && d.IRating is > 0 and var ir && !d.IsPaceCar && !d.IsSpectator)
                    {
                        _irField.Add((ir, r.ClassPosition));
                        _irCars.Add(r.CarIdx);
                    }
                if (_irField.Count < 2) continue;
                var order = Enumerable.Range(0, _irField.Count).OrderBy(i => _irField[i].Position).ToList();
                for (int k = 0; k < order.Count; k++) _irField[order[k]] = (_irField[order[k]].Rating, k + 1);
                var changes = IRatingEstimate.Changes(_irField);
                for (int i = 0; i < _irCars.Count; i++) _irChange[_irCars[i]] = changes[i];
            }
        }
        return _irChange[car];
    }

    public StandingRow? StandingOf(int car)
    {
        foreach (var r in Standings) if (r.CarIdx == car) return r;
        return null;
    }

    public int? PositionsGained(int car) =>
        Frame.SessionKind == SessionKind.Race && _startPositions.TryGetValue(car, out var start) && Frame.CarPosition[car] > 0
            ? start - Frame.CarPosition[car] : null;

    public const int CarLapHistory = 10;
    private readonly int[] _carStops = new int[TelemetryFrame.MaxCars];
    private readonly bool[] _carWasPit = new bool[TelemetryFrame.MaxCars];
    private readonly double[,] _carLaps = new double[TelemetryFrame.MaxCars, CarLapHistory];
    private readonly int[] _carLapCount = new int[TelemetryFrame.MaxCars];
    private readonly double[] _carLastSeen = new double[TelemetryFrame.MaxCars];

    private readonly int[] _carPitExitLap = new int[TelemetryFrame.MaxCars];
    private readonly bool[] _carLeftPits = new bool[TelemetryFrame.MaxCars];

    private readonly double[] _carPitAt = new double[TelemetryFrame.MaxCars], _carPitTime = new double[TelemetryFrame.MaxCars];
    private readonly int[] _carPitLap = new int[TelemetryFrame.MaxCars];

    public double? PitLaneTime(int car)
    {
        if ((uint)car >= TelemetryFrame.MaxCars) return null;
        if (Frame.CarOnPitRoad[car]) return _carPitAt[car] > 0 && Frame.SessionTime is { } t ? Math.Max(0, t - _carPitAt[car]) : null;
        return _carPitTime[car] > 0 ? _carPitTime[car] : null;
    }

    public int? LastPitLap(int car) => (uint)car < TelemetryFrame.MaxCars && _carPitLap[car] > 0 ? _carPitLap[car] : null;

    public bool OnOutLap(int car) => (uint)car < TelemetryFrame.MaxCars && _carLeftPits[car] && !Frame.CarOnPitRoad[car] && Frame.CarLapCompleted[car] == _carPitExitLap[car];

    public int StintLapsOf(int car) => (uint)car < TelemetryFrame.MaxCars ? Math.Max(0, Frame.CarLapCompleted[car] - _carPitExitLap[car]) : 0;

    public int StopsOf(int car) => (uint)car < TelemetryFrame.MaxCars ? _carStops[car] : 0;

    public double? PaceOf(int car, int n = 5)
    {
        if ((uint)car >= TelemetryFrame.MaxCars || _carLapCount[car] == 0) return null;
        int count = Math.Min(n, Math.Min(_carLapCount[car], CarLapHistory));
        double sum = 0;
        for (int k = 0; k < count; k++) sum += _carLaps[car, (_carLapCount[car] - 1 - k) % CarLapHistory];
        return sum / count;
    }

    private void TrackCars(TelemetryFrame f)
    {
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            bool pit = f.CarOnPitRoad[i];
            if (pit && !_carWasPit[i] && f.Phase >= SessionPhase.Racing) _carStops[i]++;
            if (pit && !_carWasPit[i]) { _carPitAt[i] = f.SessionTime ?? 0; _carPitLap[i] = Math.Max(1, f.CarLap[i]); }
            if (!pit && _carWasPit[i])
            {
                _carPitExitLap[i] = f.CarLapCompleted[i];
                _carLeftPits[i] = true;
                if (_carPitAt[i] > 0 && f.SessionTime is { } now) _carPitTime[i] = Math.Max(0, now - _carPitAt[i]);
            }
            _carWasPit[i] = pit;
            double last = f.CarLastLap[i];
            if (last > 0 && Math.Abs(last - _carLastSeen[i]) > 1e-6)
            {
                _carLaps[i, _carLapCount[i] % CarLapHistory] = last;
                _carLapCount[i]++;
                _carLastSeen[i] = last;
            }
        }
    }

    public string SessionLabel => Frame.CurrentSession?.Name is { Length: > 0 } n ? n : Frame.SessionKind.ToString();

    private int _split = -1;
    private double _splitCheckedAt = double.MinValue;

    internal static int SectorSplit(RaceHudSettings s)
    {
        int most = 0;
        foreach (var layout in s.Layouts)
            foreach (var w in layout.Widgets)
                if (w.Enabled && string.Equals(w.Kind, "timing", StringComparison.OrdinalIgnoreCase)
                    && w.Options.TryGetValue("split", out var raw) && int.TryParse(raw, out int n) && n > most)
                    most = n;
        return most;
    }

    public void Update(TelemetryFrame f)
    {
        Now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (f.Sequence == _lastSeq) { Alerts.Visible(Now); return; }
        _lastSeq = f.Sequence;
        Frame = f;
        var s = _settings();

        bool connected = f.Status == SimStatus.Connected;
        Alerts.Set("telemetry.lost", _wasConnected && !connected && f.Status == SimStatus.Waiting, Now);
        if (!connected) { _wasConnected = false; return; }
        _wasConnected = true;

        if (!ReferenceEquals(f.Session, _keysFrom) || f.SessionNum != _keysNum)
        {
            _keysFrom = f.Session;
            _keysNum = f.SessionNum;
            string sessionKey = $"{f.Session.TrackId}|{f.Session.PlayerCarIdx}|{f.SessionNum}|{f.Session.Sessions.Count}|{f.Session.Player?.CarPath}|{f.Session.SubSessionId}";
            _fuelIdentity = $"{f.Session.TrackId}|{f.Session.TrackConfig}|{f.Session.Player?.CarPath}";
            if (sessionKey != _sessionKey) NewSession(f);
            _sessionKey = sessionKey;
            _split = -1;
        }
        if (Now - _splitCheckedAt >= 1 || _split < 0)
        {
            _splitCheckedAt = Now;
            int split = SectorSplit(s);
            if (split != _split)
            {
                _split = split;
                if (split > 0) Laps.SetSectors(Enumerable.Range(0, split).Select(i => i / (double)split).ToList());
                else if (f.Session.Sectors.Count > 0) Laps.SetSectors(f.Session.Sectors.Select(x => x.StartPct).ToList());
                else Laps.SetSectors(Array.Empty<double>());
            }
        }

        Profile.Update(f);
        bool caution = (f.Flags & (RaceFlags.Caution | RaceFlags.CautionWaving | RaceFlags.Yellow | RaceFlags.YellowWaving)) != 0;
        if (!f.IsReplayPlaying)
        {
            Laps.Update(Now, f.LapCompleted, f.LapDistPct, f.LapCurrent, f.LapLast, f.Incidents, f.Fuel, f.OnPitRoad, caution);
            FuelData.Update(f, _fuelIdentity, s.Fuel.Window);
            if (f.LapCompleted != _fuelLap || f.OnPitRoad) { _fuelLap = f.LapCompleted; _lapStartFuel = f.Fuel; FuelThisLapFrom = f.LapDistPct is { } at && at < 0.95 ? at : 0; }
        }
        if (!f.IsReplayPlaying) Gaps.Sample(f, Now);

        if (f.SessionKind == SessionKind.Race && _startPositions.Count == 0 && f.Phase >= SessionPhase.Racing)
        {
            foreach (var (car, pos) in f.Session.GridPositions) _startPositions[car] = pos;
            if (_startPositions.Count == 0)
                for (int i = 0; i < TelemetryFrame.MaxCars; i++) if (f.CarPosition[i] > 0) _startPositions[i] = f.CarPosition[i];
        }

        if (!f.IsReplayPlaying)
        {
            TrackPit(f);
            TrackCars(f);
            TrackFlags(f);
            TrackPositions(f);
        }
        var call = _spotter.Next(f.Sides);
        if (call != SpotterCall.None)
        {
            LastCall = (call, Now);
            Alerts.Fire("side.car", Now, Spotter.Text(call));
        }
        if (Now - _lastChecks >= 0.25)
        {
            _lastChecks = Now;
            UpdateFuel(f, s);
            CheckAlerts(f, s);
        }
        Alerts.Visible(Now);
    }

    private void NewSession(TelemetryFrame f)
    {
        Laps.Clear();
        Gaps.Clear();
        _spotter.Reset();
        _startPositions.Clear();
        _pitStops.Clear();
        Array.Clear(_carStops);
        Array.Clear(_carPitAt);
        Array.Clear(_carPitTime);
        Array.Clear(_carPitLap);
        Array.Clear(_carPitExitLap);
        Array.Clear(_carLeftPits);
        Array.Clear(_carLapCount);
        Array.Clear(_carLastSeen);
        Array.Copy(f.CarOnPitRoad, _carWasPit, TelemetryFrame.MaxCars);
        _standingsSeq = -1;
        _lastIncidents = -1;
        _lastClassPos = 0;
        _lastWetness = -1;
        _lastFlags = f.Flags;
        _wasOnPitRoad = f.OnPitRoad;
        _wasStall = f.InPitStall;
        _pitEnteredAt = -1;
        _bestLap = null;
        _lapsPbKnown = false;
        _windowWasOpen = false;
        StintStart = f.SessionTime;
        StintStartLap = f.LapCompleted ?? 0;
        _lastPosition = 0;
        SelectedCar = -1;
        Alerts.ResetConditions();
        AddEvent(f, $"{(f.CurrentSession?.Name is { Length: > 0 } n ? n : "Session")} · {f.Session.TrackDisplayName}", AlertLevel.Info);
    }

    private void TrackPit(TelemetryFrame f)
    {
        double t = f.SessionTime ?? 0;
        if (f.OnPitRoad && !_wasOnPitRoad)
        {
            _pitEnteredAt = t;
            _stallTime = 0;
            AddEvent(f, "Pit entry", AlertLevel.Info);
        }
        if (f.InPitStall && !_wasStall) _stallAt = t;
        if (!f.InPitStall && _wasStall && _stallAt >= 0) _stallTime += t - _stallAt;
        if (!f.OnPitRoad && _wasOnPitRoad)
        {
            if (_pitEnteredAt >= 0 && t > _pitEnteredAt)
            {
                var stop = new PitStop(f.LapCompleted ?? 0, t - _pitEnteredAt, _stallTime > 0 ? _stallTime : null);
                _pitStops.Add(stop);
                AddEvent(f, $"Pit exit · lane {stop.LaneTime:0.0} s" + (stop.StallTime is { } st ? $", stopped {st:0.0} s" : ""), AlertLevel.Info);
            }
            StintStart = t;
            StintStartLap = f.LapCompleted ?? 0;
            _pitEnteredAt = -1;
        }
        _wasOnPitRoad = f.OnPitRoad;
        _wasStall = f.InPitStall;
    }

    private void TrackFlags(TelemetryFrame f)
    {
        var added = f.Flags & ~_lastFlags;
        _lastFlags = f.Flags;
        if (added == RaceFlags.None) return;
        foreach (var kind in RaceControl.Active(added))
            if (kind is not (FlagKind.StartReady or FlagKind.StartSet))
                AddEvent(f, RaceControl.Label(kind), RaceControl.Level(kind));
    }

    private void TrackPositions(TelemetryFrame f)
    {
        int car = f.PlayerCarIdx;
        if ((uint)car >= TelemetryFrame.MaxCars || f.SessionKind != SessionKind.Race || f.Phase < SessionPhase.Racing) return;
        int pos = f.CarPosition[car], cls = f.CarClassPosition[car];
        if (pos <= 0) return;
        if (_lastPosition > 0 && pos != _lastPosition)
        {
            bool multi = f.Session.NumCarClasses > 1;
            string where = multi && cls > 0 ? $"P{cls} in class (P{pos} overall)" : $"P{pos}";
            AddEvent(f, pos < _lastPosition ? $"Gained {_lastPosition - pos} · {where}" : $"Lost {pos - _lastPosition} · {where}",
                pos < _lastPosition ? AlertLevel.Good : AlertLevel.Info);
        }
        _lastPosition = pos;
        if (cls > 0 && _lastClassPos > 0 && cls != _lastClassPos)
            Alerts.Fire("position.change", Now, cls < _lastClassPos ? $"Up to P{cls}" : $"Down to P{cls}",
                cls < _lastClassPos ? $"{_lastClassPos - cls} place{(_lastClassPos - cls == 1 ? "" : "s")} gained" : $"{cls - _lastClassPos} place{(cls - _lastClassPos == 1 ? "" : "s")} lost");
        if (cls > 0) _lastClassPos = cls;
    }

    private void OnLap(LapRecord lap)
    {
        var f = Frame;
        if (lap.Clean && (_bestLap is null || lap.Time < _bestLap))
        {
            bool first = _bestLap is null;
            _bestLap = lap.Time;
            if (!first || _lapsPbKnown) Alerts.Fire("lap.pb", Now, "Personal best", RaceFormat.LapTime(lap.Time));
            AddEvent(f, $"Best lap {RaceFormat.LapTime(lap.Time)}", AlertLevel.Good);
        }
        _lapsPbKnown = true;
        if (lap.IncidentsGained > 0) Alerts.Fire("lap.unclean", Now, "Lap with incidents", $"Lap {lap.Lap}: {lap.IncidentsGained}x");
        if (lap.Clean && !lap.Caution && LapMath.Stats(Laps.Laps.Take(Math.Max(0, Laps.Laps.Count - 1)).ToList(), 5) is { Count: >= 3 } stats)
        {
            double pct = (lap.Time / stats.Average - 1) * 100;
            if (pct > Alerts.Threshold("pace.drop")) Alerts.Fire("pace.drop", Now, "Pace dropping", $"{RaceFormat.LapTime(lap.Time)} · +{pct:0.0} % on your average");
        }
    }

    private void UpdateFuel(TelemetryFrame f, RaceHudSettings s)
    {
        var estimate = FuelData.Estimate;
        FuelPerLap = estimate.HasEstimate && !FuelData.EnergyUnits ? estimate.MeanLitersPerLap : null;
        if (f.Fuel is not { } fuel || FuelData.EnergyUnits) { Fuel = null; return; }
        if (Now < _nextFuelPlanAt && FuelData.Laps.Count == _fuelLapsPlanned && Fuel is not null) return;
        _nextFuelPlanAt = Now + 0.25;
        _fuelLapsPlanned = FuelData.Laps.Count;
        double pct = f.LapDistPct is { } p && double.IsFinite(p) ? Math.Clamp(p, 0, 1) : 0;
        var mode = FuelPlanner.Mode(s.Fuel.Safety);
        double extraLaps = s.Fuel.ExtraKm > 0 && f.Session.TrackLengthKm is > 0 and var km ? s.Fuel.ExtraKm / km : 0;
        Fuel = FuelPlanner.Make(fuel, estimate, LapsToGoEstimate(f), pct, FuelData.CurrentLap(pct, mode), Tank, s.Fuel.MarginLaps + extraLaps, mode,
            s.Fuel.ReserveLiters, s.Fuel.ExtraPercent / 100);
    }

    public double? LapsToGo(TelemetryFrame f) => LapsToGoEstimate(f)?.Laps;

    public (double Laps, bool Forecast)? LapsToGoEstimate(TelemetryFrame f)
    {
        int me = f.PlayerCarIdx;
        if ((uint)me >= TelemetryFrame.MaxCars || f.LapDistPct is not { } raw || !double.IsFinite(raw)) return null;
        double pct = Math.Clamp(raw, 0, 1);
        var session = f.CurrentSession;
        int leader = Standings.Count > 0 ? Standings[0].CarIdx : me;
        int lapsDown = StandingOf(me)?.LapsDown ?? 0;
        if (session?.Laps is { } total && total > 0)
            return (RaceDistanceEstimator.LapLimitedLapsRemaining(Math.Max(0, total - lapsDown), Math.Max(0, f.LapCompleted ?? 0), pct), false);
        if (f.SessionTimeRemain is { } remain && remain >= 0 && session?.TimeSeconds is not null && f.SessionKind == SessionKind.Race)
        {
            double? Pace(int car) => f.CarLastLap[car] > 0 ? f.CarLastLap[car] : f.CarBestLap[car] > 0 ? f.CarBestLap[car] : FieldOrder.LapTimeOf(f, car);
            double? myPace = Laps.Laps.Count > 0 ? LapMath.Stats(Laps.Laps, 5)?.Average ?? Pace(me) : Pace(me);
            if (Pace(leader) is not > 0 || myPace is not > 0) return null;
            double leaderPct = f.CarLapDistPct[leader] is var lp && lp >= 0 && lp <= 1 ? lp : pct;
            var e = RaceDistanceEstimator.EstimateTimedRace(new TimedRaceInput
            {
                SessionTimeRemainingSeconds = remain,
                LeaderLapProgress = leaderPct,
                LeaderLapTimeSeconds = Pace(leader)!.Value,
                PlayerLapProgress = pct,
                PlayerLapTimeSeconds = myPace.Value,
                WhiteFlagObserved = (f.Flags & RaceFlags.White) != 0,
                CheckeredFlagObserved = (f.Flags & RaceFlags.Checkered) != 0,
                LeaderFinished = f.Phase >= SessionPhase.Checkered,
            });
            return (e.LapsRemainingExpected, !e.IsAuthoritative);
        }
        return null;
    }

    private void CheckAlerts(TelemetryFrame f, RaceHudSettings s)
    {
        double now = Now;
        if (Fuel is { } plan)
        {
            Alerts.Set("fuel.short", plan.Stops is > 0 && f.SessionKind == SessionKind.Race, now,
                detail: plan.Surplus is { } sur ? $"Short by {RaceFormat.Fuel(-sur, s.Units)} · add {RaceFormat.Fuel(plan.AddAtNextStop ?? -sur, s.Units)}" : null);
            double lowLaps = Alerts.Threshold("fuel.low");
            bool low = plan.LapsOfFuel < lowLaps && f.Driving;
            Alerts.Set("fuel.low", low, now, detail: low ? $"{plan.LapsOfFuel:0.0} laps left" : null);
            bool open = plan.WindowOpensIn is { } o && o <= 0 && plan.Stops == 1;
            if (open && !_windowWasOpen) Alerts.Fire("pit.window", now, detail: $"Pit within {plan.LapsOfFuel:0.0} laps");
            _windowWasOpen = open;
        }
        if (f.Session.PitSpeedLimitKph is { } limit && f.Speed is { } speed)
        {
            double kph = speed * 3.6, tolerance = Alerts.Threshold("pit.speed");
            bool speeding = f.OnPitRoad && f.Driving && kph > limit + tolerance;
            Alerts.Set("pit.speed", speeding, now, detail: speeding ? $"{RaceFormat.Speed(speed, s.Units)} · limit {RaceFormat.SpeedKph(limit, s.Units)}" : null);
        }
        Alerts.Set("pit.limiter", f.OnPitRoad && f.Driving && !f.InPitStall && f.Speed > 3 && !f.Warnings.HasFlag(EngineWarnings.PitLimiter)
            && f.Vars.ContainsKey("EngineWarnings"), now);
        Alerts.Set("flag.yellow", (f.Flags & (RaceFlags.Yellow | RaceFlags.YellowWaving | RaceFlags.Caution | RaceFlags.CautionWaving)) != 0 && f.Driving, now,
            title: f.Flags.HasFlag(RaceFlags.Caution) || f.Flags.HasFlag(RaceFlags.CautionWaving) ? "Caution" : "Yellow flag");
        Alerts.Set("flag.blue", f.Flags.HasFlag(RaceFlags.Blue) && f.Driving, now);
        Alerts.Set("flag.white", f.Flags.HasFlag(RaceFlags.White) && f.Driving, now);
        if (f.Precipitation is { } rain)
            Alerts.Set("weather.rain", rain * 100 >= Math.Max(0.5, Alerts.Threshold("weather.rain")), now, detail: $"{rain * 100:0}% rain");
        if (f.TrackWetness is { } wet)
        {
            if (_lastWetness >= 0 && wet != _lastWetness)
                Alerts.Fire("weather.wet", now, wet > _lastWetness ? "Track getting wetter" : "Track drying", "Now " + RaceFormat.Wetness(wet).ToLowerInvariant());
            _lastWetness = wet;
        }
        var penalty = RaceControl.Active(f.Flags).FirstOrDefault(k => k is FlagKind.Black or FlagKind.Meatball or FlagKind.Disqualified);
        Alerts.Set("flag.penalty", penalty != FlagKind.None, now, RaceControl.Label(penalty), RaceControl.Meaning(penalty));
        if (f.Incidents is { } inc)
        {
            if (_lastIncidents >= 0 && inc > _lastIncidents)
            {
                Alerts.Fire("incident.new", now, $"+{inc - _lastIncidents}x incident", $"{inc}x total" + (f.Session.IncidentLimit is { } l ? $" / {l}" : ""));
                AddEvent(f, $"+{inc - _lastIncidents}x incident ({inc}x)", AlertLevel.Warning);
            }
            _lastIncidents = inc;
            if (f.Session.IncidentLimit is { } lim && lim > 0)
                Alerts.Set("incidents.limit", inc >= lim - Alerts.Threshold("incidents.limit"), now, detail: $"{inc}x of {lim}x");
        }
        var bad = f.Warnings & (EngineWarnings.WaterTemp | EngineWarnings.OilTemp | EngineWarnings.OilPressure | EngineWarnings.FuelPressure);
        Alerts.Set("engine.warning", bad != EngineWarnings.None && f.Driving, now, detail: RaceFormat.Warnings(bad));
        if (s.Fuel.MaxStintMinutes > 0 && StintStart is { } st && f.SessionTime is { } t)
        {
            double left = s.Fuel.MaxStintMinutes * 60 - (t - st);
            Alerts.Set("stint.limit", f.Driving && left <= Alerts.Threshold("stint.limit") * 60, now, detail: left > 0 ? $"{left / 60:0} min left in the stint" : "Stint time reached");
        }
        int me = f.PlayerCarIdx;
        bool traffic = (uint)me < TelemetryFrame.MaxCars && f.Driving && f.Session.NumCarClasses > 1;
        if (!traffic && _trafficRaised)
        {
            for (int i = 0; i < TelemetryFrame.MaxCars; i++)
            {
                Alerts.Set("traffic.faster", false, now, key: FasterKeys[i]);
                Alerts.Set("traffic.slower", false, now, key: SlowerKeys[i]);
            }
        }
        _trafficRaised = traffic;
        if (traffic)
        {
            double faster = Alerts.Threshold("traffic.faster"), slower = Alerts.Threshold("traffic.slower");
            int myClass = FieldOrder.ClassOf(f, me);
            double? myLap = FieldOrder.LapTimeOf(f, me);
            for (int i = 0; i < TelemetryFrame.MaxCars; i++)
            {
                if (i == me || !f.CarActive(i) || f.CarOnPitRoad[i] || FieldOrder.ClassOf(f, i) == myClass)
                {
                    Alerts.Set("traffic.faster", false, now, key: FasterKeys[i]);
                    Alerts.Set("traffic.slower", false, now, key: SlowerKeys[i]);
                    continue;
                }
                var gap = FieldOrder.GapSeconds(f, me, i, Profile);
                double? lap = FieldOrder.LapTimeOf(f, i);
                bool isFaster = lap is { } l && myLap is { } m && l < m * 0.98;
                bool closing = Gaps.Trend(i) is < -0.01;
                bool fasterBehind = isFaster && gap is < 0 and var g1 && -g1 < faster && closing;
                Alerts.Set("traffic.faster", fasterBehind, now,
                    fasterBehind ? $"Faster car behind · {f.Session.ByCar.GetValueOrDefault(i)?.ClassName}" : null,
                    fasterBehind ? $"{Name(f, i)} · {-(gap ?? 0):0.0} s" : null, FasterKeys[i]);
                bool slowerAhead = !isFaster && lap is { } l2 && myLap is { } m2 && l2 > m2 * 1.02 && gap is > 0 && gap < slower && closing;
                Alerts.Set("traffic.slower", slowerAhead, now,
                    slowerAhead ? $"Slower car ahead · {f.Session.ByCar.GetValueOrDefault(i)?.ClassName}" : null,
                    slowerAhead ? $"{Name(f, i)} · {gap:0.0} s" : null, SlowerKeys[i]);
            }
        }
    }

    private bool _trafficRaised;

    private static readonly string[] FasterKeys = Enumerable.Range(0, TelemetryFrame.MaxCars).Select(i => "traffic.faster:" + i).ToArray();
    private static readonly string[] SlowerKeys = Enumerable.Range(0, TelemetryFrame.MaxCars).Select(i => "traffic.slower:" + i).ToArray();

    public static string Name(TelemetryFrame f, int car) =>
        f.Session.ByCar.TryGetValue(car, out var d) ? (d.Name.Length > 0 ? d.Name : d.TeamName) : $"Car {car}";

    private void AddEvent(TelemetryFrame f, string text, AlertLevel level)
    {
        _events.Add(new RaceEvent(f.SessionTime ?? 0, text, level));
        if (_events.Count > MaxEvents) _events.RemoveAt(0);
    }

    public static AlertChoice ChoiceFor(RaceHudSettings s, AlertDef def)
    {
        s.Alerts.TryGetValue(def.Id, out var a);
        return new AlertChoice(a?.Enabled ?? def.OnByDefault, a?.Cooldown ?? def.DefaultCooldown,
            a?.Threshold ?? def.Threshold, a?.Duration ?? 6);
    }
}

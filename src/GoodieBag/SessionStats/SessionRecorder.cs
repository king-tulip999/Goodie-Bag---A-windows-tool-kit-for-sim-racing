using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SessionStats;

internal sealed class SessionRow
{
    public string Key = Guid.NewGuid().ToString("N");
    public long Id;
    public DateTime StartUtc, EndUtc;
    public string Sim = "iRacing";
    public string Driver = "", CarNumber = "", Car = "", CarClass = "", Track = "", TrackConfig = "", SessionType = "", Weather = "", Category = "";
    public long DriverId;
    public double? TrackKm;
    public bool DynamicWeather, Finished, Official;
    public int Laps;
    public int? Incidents, StartPos, FinishPos, IrStart, IrDelta, RaceWeek;
    public double? SrStart, SrDelta, NightS, TrackTempMin, TrackTempMax;
    public int LeagueId, SessionNum, SeasonId, PitStops;
    public long SubSession;
    public double DriveS, GarageS, LongestStintS, RainS, DistanceKm, PitS;
    public double? AirTemp, WindKph, WindDir, Humidity, Pressure, AirDensity;
    public int? Skies;
    public double? RainMax;
    public int? WetnessMax;
    public string Rubber = "", SimStart = "";
    public int? TrackId;
    public string TrackName = "";
    public string Source = "live";
    public Dictionary<int, LapTrace>? Traces;

    public SessionRow Copy() => (SessionRow)MemberwiseClone();
}

internal sealed record LapRow(int Lap, double Time, bool Clean, double? TopSpeed, double? FuelUsed, double? FullThrottle, double? Brake, int Shifts, double? PeakLatG,
    IReadOnlyList<double>? Sectors = null, LapFlags Flags = LapFlags.None, int? Incidents = null, int? Run = null, string? Setup = null);

[Flags]
internal enum LapFlags { None = 0, PitLane = 1, OffTrack = 2, Incident = 4, Refuel = 8 }

internal sealed class SessionRecorder
{
    private const double Gravity = 9.80665, FullThrottle = 0.98, Braking = 0.05, GlitchG = 6;
    private readonly string _source;
    private readonly TraceBuilder? _trace;
    private IReadOnlyList<double> _sectorStarts = Array.Empty<double>();
    private readonly List<double> _sectorCrossings = new();
    private SessionRow? _row;
    private readonly List<LapRow> _laps = new();
    private int _savedLaps;
    private string _identity = "";
    private double? _lastTime;

    private double? _lapStart, _fuelStart, _lastFuel;
    private double _prevPct = -1, _prevTime, _lapDt, _throttleDt, _brakeDt, _top, _peakG;
    private double _x, _y;
    private int? _incStart;
    private int _shifts, _lastGear;
    private bool _pitThisLap, _offThisLap, _refuelled;

    private double _stint, _pitStart;
    private bool _onPitRoad, _sawStall, _wasDriving;
    private int _run, _lapRun;
    private int? _lapsAtCheckered;

    public SessionRecorder(string source = "live", bool traces = false)
    {
        _source = source;
        if (traces) _trace = new TraceBuilder();
    }

    public event Action<SessionRow, IReadOnlyList<LapRow>>? LapDone;

    public SessionRow? Current => _row;

    public Dictionary<int, LapTrace>? TakeTraces()
    {
        var traces = _row?.Traces;
        if (_row is not null) _row.Traces = null;
        return traces;
    }

    public (SessionRow Row, List<LapRow> Laps)? Feed(TelemetryFrame f, DateTime utc)
    {
        var info = f.Session;
        if (f.Status != SimStatus.Connected || f.Simulated || ReferenceEquals(info, SessionInfo.Empty) || info.Player is not { } player)
            return f.Status == SimStatus.Connected && !f.Simulated && !ReferenceEquals(info, SessionInfo.Empty) ? null : Finish();

        (SessionRow, List<LapRow>)? ended = null;
        string identity = $"{info.SubSessionId}|{f.SessionNum}|{info.TrackId}|{player.CarPath}|{player.UserId}";
        if (_row is not null && identity != _identity) ended = Finish();
        if (_row is null)
        {
            _identity = identity;
            Begin(f, info, player, utc);
        }
        var row = _row!;
        row.EndUtc = utc;

        double dt = 0;
        if (f.SessionTime is { } now)
        {
            if (_lastTime is { } last && now > last && now - last < 1) dt = now - last;
            _lastTime = now;
        }
        bool driving = f.IsOnTrack && !f.IsReplayPlaying;
        if (driving && !_wasDriving) _run++;
        _wasDriving = driving;
        if (driving)
        {
            if (row.AirTemp is null && f.AirTemp is { } air) Conditions(f, row, air);
            if (f.Precipitation is { } p) row.RainMax = Math.Max(row.RainMax ?? 0, p);
            if (f.TrackWetness is { } w and > 0) row.WetnessMax = Math.Max(row.WetnessMax ?? 0, w);
            row.DriveS += dt;
            row.DistanceKm += (f.Speed ?? 0) * dt / 1000;
            if (f.Precipitation > 0 || f.DeclaredWet == true) row.RainS += dt;
            if (f.RawValue("SolarAltitude") is { } sun) row.NightS = (row.NightS ?? 0) + (sun < 0 ? dt : 0);
            if (f.TrackTemp is { } temp && temp > -50 && temp < 90)
            {
                row.TrackTempMin = Math.Min(row.TrackTempMin ?? temp, temp);
                row.TrackTempMax = Math.Max(row.TrackTempMax ?? temp, temp);
            }
        }
        else if (f.IsInGarage) row.GarageS += dt;

        if (driving && !f.InPitStall) row.LongestStintS = Math.Max(row.LongestStintS, _stint += dt);
        else _stint = 0;

        PitVisit(f, row, driving);
        if (f.Incidents is { } inc) row.Incidents = inc;
        if (row.SessionType == nameof(SessionKind.Race)) RacePositions(f, row);
        Lap(f, row, driving, dt);
        return ended;
    }

    public (SessionRow Row, List<LapRow> Laps)? Finish()
    {
        var row = _row;
        _row = null;
        _identity = "";
        _lastTime = null;
        ResetLap();
        _lastGear = 0;
        _stint = 0;
        _onPitRoad = _wasDriving = false;
        _run = 0;
        _lapsAtCheckered = null;
        if (row is null) return null;
        var laps = _laps.ToList();
        _laps.Clear();
        _savedLaps = 0;
        row.Laps = laps.Count;
        return row.DriveS >= 30 || row.GarageS >= 60 ? (row, laps) : null;
    }

    private void Begin(TelemetryFrame f, SessionInfo info, DriverEntry player, DateTime utc)
    {
        var kind = f.CurrentSession?.Kind ?? SessionKind.Other;
        _row = new SessionRow
        {
            StartUtc = utc, EndUtc = utc, Source = _source,
            Driver = player.Name, DriverId = player.UserId, CarNumber = player.CarNumber,
            Car = player.CarName.Length > 0 ? player.CarName : player.CarShortName,
            CarClass = player.ClassName.Length > 0 ? player.ClassName : player.CarShortName,
            Track = info.TrackDisplayName, TrackConfig = info.TrackConfig, TrackKm = info.TrackLengthKm, TrackId = info.TrackId, TrackName = info.TrackName,
            SessionType = kind.ToString(), Category = info.Category,
            Weather = string.Join(" · ", new[] { info.WeatherType, info.Skies }.Where(x => x.Length > 0)),
            DynamicWeather = info.WeatherType.Contains("dynamic", StringComparison.OrdinalIgnoreCase) || info.WeatherType.Contains("realistic", StringComparison.OrdinalIgnoreCase),
            IrStart = player.IRating, SrStart = SafetyRating(player.License),
            LeagueId = info.LeagueId ?? 0, SubSession = info.SubSessionId ?? 0, SessionNum = f.SessionNum,
            SeasonId = info.SeasonId ?? 0, RaceWeek = info.Official ? info.RaceWeek : null, Official = info.Official,
        };
        _laps.Clear();
        _savedLaps = 0;
        _sectorStarts = info.Sectors.Select(x => x.StartPct).Where(p => p > 0 && p < 1).OrderBy(p => p).ToList();
    }

    private static void Conditions(TelemetryFrame f, SessionRow row, double air)
    {
        row.AirTemp = air;
        row.WindKph = f.WindSpeed is { } wind ? wind * 3.6 : null;
        row.WindDir = f.WindDir;
        row.Humidity = f.Humidity;
        row.Pressure = f.RawValue("AirPressure") is > 0 and var pa ? pa : null;
        row.AirDensity = f.RawValue("AirDensity") is > 0 and var rho ? rho : null;
        row.Skies = f.RawValue("Skies") is { } sky and >= 0 and <= 3 ? (int)sky : null;
        row.Rubber = f.CurrentSession?.Rubber ?? "";
        if (f.Session.SimDate.Length > 0 && f.RawValue("SessionTimeOfDay") is { } tod and >= 0)
            row.SimStart = $"{f.Session.SimDate} {(int)(tod / 3600) % 24:00}:{(int)(tod / 60) % 60:00}";
    }

    internal static double? SafetyRating(string license)
    {
        int space = license.LastIndexOf(' ');
        return space > 0 && double.TryParse(license[(space + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sr) ? sr : null;
    }

    private void PitVisit(TelemetryFrame f, SessionRow row, bool driving)
    {
        if (!driving) { _onPitRoad = false; return; }
        double t = f.SessionTime ?? 0;
        if (f.OnPitRoad)
        {
            if (!_onPitRoad) { _onPitRoad = true; _pitStart = t; _sawStall = false; }
            if (f.InPitStall) _sawStall = true;
        }
        else if (_onPitRoad)
        {
            _onPitRoad = false;
            if (_sawStall && t > _pitStart) { row.PitStops++; row.PitS += t - _pitStart; _run++; }
        }
    }

    private void RacePositions(TelemetryFrame f, SessionRow row)
    {
        int me = f.PlayerCarIdx;
        if ((uint)me >= TelemetryFrame.MaxCars) return;
        int pos = f.CarClassPosition[me] > 0 ? f.CarClassPosition[me] : f.CarPosition[me];
        if (pos <= 0) return;
        if (row.StartPos is null && f.Phase == SessionPhase.Racing) row.StartPos = pos;
        if (f.Phase is SessionPhase.Racing or SessionPhase.Checkered or SessionPhase.CoolDown) row.FinishPos = pos;
        if (f.Phase == SessionPhase.Checkered) _lapsAtCheckered ??= f.LapCompleted;
        if (!row.Finished && f.CarSurface[me] != TrackSurface.NotInWorld
            && (f.Phase == SessionPhase.CoolDown || _lapsAtCheckered is { } at && f.LapCompleted > at))
            row.Finished = true;
    }

    private void Lap(TelemetryFrame f, SessionRow row, bool driving, double dt)
    {
        if (!driving || f.LapDistPct is not { } pct || f.SessionTime is not { } t || pct < 0)
        {
            ResetLap();
            return;
        }
        if (f.Gear is { } gear and >= 1)
        {
            if (_lastGear >= 1 && gear != _lastGear) _shifts++;
            _lastGear = gear;
        }
        if (_lapStart is not null)
        {
            _lapDt += dt;
            if (f.Throttle >= FullThrottle) _throttleDt += dt;
            if (f.Brake > Braking) _brakeDt += dt;
            if (f.Speed is { } v) _top = Math.Max(_top, v);
            if (f.LatAccel is { } lat && Math.Abs(lat) / Gravity < GlitchG) _peakG = Math.Max(_peakG, Math.Abs(lat) / Gravity);
            if (f.OnPitRoad) _pitThisLap = true;
            if (f.PlayerSurface == TrackSurface.OffTrack) _offThisLap = true;
            if (f.Fuel is { } fuel && _lastFuel is { } before && fuel > before + 0.3) _refuelled = true;
            if (_prevPct >= 0 && pct > _prevPct && t > _prevTime)
                foreach (double start in _sectorStarts)
                    if (_prevPct < start && pct >= start) _sectorCrossings.Add(_prevTime + (start - _prevPct) / (pct - _prevPct) * (t - _prevTime));
        }
        if (f.Fuel is { } fl) _lastFuel = fl;

        if (_prevPct > 0.75 && pct < 0.25 && t > _prevTime)
        {
            double cross = _prevTime + (1 - _prevPct) / (1 - _prevPct + pct) * (t - _prevTime);
            if (_lapStart is { } start && cross - start > 10)
            {
                double time = cross - start;
                int? lapIncidents = _incStart is { } i0 && f.Incidents is { } i1 && i1 >= i0 ? i1 - i0 : null;
                bool clean = !_pitThisLap && !_offThisLap && lapIncidents is null or 0;
                var flags = (_pitThisLap ? LapFlags.PitLane : 0) | (_offThisLap ? LapFlags.OffTrack : 0) | (lapIncidents > 0 ? LapFlags.Incident : 0) | (_refuelled ? LapFlags.Refuel : 0);
                string setup = f.Session.PlayerSetupName;
                double? fuelUsed = !_refuelled && _fuelStart is { } fs && f.Fuel is { } fe && fs - fe >= 0 ? fs - fe : null;
                int number = _laps.Count + 1;
                _laps.Add(new LapRow(number, time, clean, _top > 0 ? _top * 3.6 : null, fuelUsed,
                    _lapDt > 0 ? 100 * _throttleDt / _lapDt : null, _lapDt > 0 ? 100 * _brakeDt / _lapDt : null, _shifts, _peakG > 0 ? _peakG : null,
                    SectorTimes(start, cross), flags, lapIncidents, _lapRun > 0 ? _lapRun : null, setup.Length > 0 ? setup : null));
                if (_trace?.Finish(start, time) is { } trace) (row.Traces ??= new())[number] = trace;
                row.Laps = _laps.Count;
                LapDone?.Invoke(row, _laps.Skip(_savedLaps).ToList());
                _savedLaps = _laps.Count;
            }
            ResetLap();
            _lapStart = cross;
            _fuelStart = f.Fuel;
            _incStart = f.Incidents;
            _pitThisLap = f.OnPitRoad;
            _lapRun = _run;
        }
        if (_trace is not null)
        {
            if (f.YawNorth is { } h && f.VelocityX is { } vx && f.VelocityY is { } vy)
            {
                _x += (vx * Math.Sin(h) - vy * Math.Cos(h)) * dt;
                _y += (vx * Math.Cos(h) + vy * Math.Sin(h)) * dt;
            }
            else _x = _y = double.NaN;
            if (_lapStart is not null)
                _trace.Add(new TraceBuilder.Sample(f.LapDist ?? pct * (row.TrackKm ?? 0) * 1000, pct, t, f.Speed ?? 0, f.Throttle ?? 0, f.Brake ?? 0, f.Clutch,
                    f.Steering ?? 0, f.Gear ?? 0, f.Rpm ?? 0, f.LatAccel ?? 0, f.LongAccel ?? 0, f.RawValue("Lat") ?? 0, f.RawValue("Lon") ?? 0, _x, _y));
        }
        _prevPct = pct;
        _prevTime = t;
    }

    private List<double>? SectorTimes(double start, double end)
    {
        if (_sectorStarts.Count == 0 || _sectorCrossings.Count != _sectorStarts.Count) return null;
        var times = new List<double>(_sectorStarts.Count + 1);
        double from = start;
        foreach (double at in _sectorCrossings.Append(end))
        {
            if (at < from) return null;
            times.Add(at - from);
            from = at;
        }
        return times;
    }

    private void ResetLap()
    {
        _lapStart = _fuelStart = null;
        _incStart = null;
        _prevPct = -1;
        _lapDt = _throttleDt = _brakeDt = _top = _peakG = 0;
        _shifts = 0;
        _pitThisLap = _offThisLap = _refuelled = false;
        _sectorCrossings.Clear();
        _trace?.Clear();
        _x = _y = 0;
    }
}

namespace GoodieBag.SimRacing.Telemetry;

internal sealed class FrameDecoder
{
    private IReadOnlyDictionary<string, IrsdkVar> _vars = new Dictionary<string, IrsdkVar>();
    private IrsdkVar? _sessionNum, _sessionState, _sessionFlags, _sessionTime, _sessionTimeRemain, _sessionTimeTotal, _lapsRemain, _lapsRemainEx, _lapsTotal, _raceLaps;
    private IrsdkVar? _isOnTrack, _inGarage, _replay, _playerIdx, _camIdx, _onPitRoad, _surface;
    private IrsdkVar? _speed, _rpm, _gear, _throttle, _brake, _clutch, _steer, _steerMax;
    private IrsdkVar? _fuel, _fuelPct, _fuelPerHour, _lap, _lapCompleted, _lapPct, _lapDist, _lapCur, _lapLast, _lapBest;
    private IrsdkVar? _dBest, _dBestOk, _dSession, _dSessionOk, _dOptimal, _dOptimalOk;
    private IrsdkVar? _inc, _teamInc, _drvInc, _warnings, _oilTemp, _oilPress, _waterTemp, _fuelPress, _voltage;
    private IrsdkVar? _bias, _tc, _abs, _map, _lat, _long, _yawRate, _yaw, _yawNorth, _absActive, _velX, _velY, _leftRight;
    private IrsdkVar? _pitFlags, _pitFuel, _repair, _optRepair, _pitStatus, _pitActive, _inStall, _fastRepair, _compound;
    private IrsdkVar? _air, _track, _wind, _windDir, _humidity, _fog, _precip, _wetness, _declaredWet;
    private IrsdkVar? _battery, _mguK, _deploy, _p2pCount, _p2pOn, _drs;
    private IrsdkVar? _cPct, _cLap, _cLapDone, _cPos, _cClassPos, _cPit, _cSurface, _cEst, _cF2, _cLast, _cBest, _cCompound, _cFlags;
    private readonly IrsdkVar?[] _coldPress = new IrsdkVar?[4], _tempL = new IrsdkVar?[4], _tempM = new IrsdkVar?[4], _tempR = new IrsdkVar?[4];
    private readonly IrsdkVar?[] _wearL = new IrsdkVar?[4], _wearM = new IrsdkVar?[4], _wearR = new IrsdkVar?[4];
    private static readonly string[] Corners = { "LF", "RF", "LR", "RR" };

    public IReadOnlyDictionary<string, IrsdkVar> Vars => _vars;

    public void Bind(IReadOnlyDictionary<string, IrsdkVar> vars)
    {
        _vars = vars;
        IrsdkVar? V(string name) => vars.TryGetValue(name, out var v) ? v : null;
        _sessionNum = V("SessionNum"); _sessionState = V("SessionState"); _sessionFlags = V("SessionFlags");
        _sessionTime = V("SessionTime"); _sessionTimeRemain = V("SessionTimeRemain"); _sessionTimeTotal = V("SessionTimeTotal");
        _lapsRemain = V("SessionLapsRemain"); _lapsRemainEx = V("SessionLapsRemainEx"); _lapsTotal = V("SessionLapsTotal"); _raceLaps = V("RaceLaps");
        _isOnTrack = V("IsOnTrack"); _inGarage = V("IsInGarage"); _replay = V("IsReplayPlaying");
        _playerIdx = V("PlayerCarIdx"); _camIdx = V("CamCarIdx"); _onPitRoad = V("OnPitRoad"); _surface = V("PlayerTrackSurface");
        _speed = V("Speed"); _rpm = V("RPM"); _gear = V("Gear"); _throttle = V("Throttle"); _brake = V("Brake"); _clutch = V("Clutch");
        _steer = V("SteeringWheelAngle"); _steerMax = V("SteeringWheelAngleMax");
        _fuel = V("FuelLevel"); _fuelPct = V("FuelLevelPct"); _fuelPerHour = V("FuelUsePerHour");
        _lap = V("Lap"); _lapCompleted = V("LapCompleted"); _lapPct = V("LapDistPct"); _lapDist = V("LapDist");
        _lapCur = V("LapCurrentLapTime"); _lapLast = V("LapLastLapTime"); _lapBest = V("LapBestLapTime");
        _dBest = V("LapDeltaToBestLap"); _dBestOk = V("LapDeltaToBestLap_OK");
        _dSession = V("LapDeltaToSessionBestLap"); _dSessionOk = V("LapDeltaToSessionBestLap_OK");
        _dOptimal = V("LapDeltaToOptimalLap"); _dOptimalOk = V("LapDeltaToOptimalLap_OK");
        _inc = V("PlayerCarMyIncidentCount"); _teamInc = V("PlayerCarTeamIncidentCount"); _drvInc = V("PlayerCarDriverIncidentCount");
        _warnings = V("EngineWarnings"); _oilTemp = V("OilTemp"); _oilPress = V("OilPress"); _waterTemp = V("WaterTemp");
        _fuelPress = V("FuelPress"); _voltage = V("Voltage");
        _bias = V("dcBrakeBias"); _tc = V("dcTractionControl"); _abs = V("dcABS"); _map = V("dcEngineMap") ?? V("dcFuelMixture");
        _lat = V("LatAccel"); _long = V("LongAccel"); _yawRate = V("YawRate"); _yaw = V("Yaw"); _yawNorth = V("YawNorth"); _velX = V("VelocityX"); _velY = V("VelocityY");
        _absActive = V("BrakeABSactive");
        _leftRight = V("CarLeftRight");
        _pitFlags = V("PitSvFlags"); _pitFuel = V("PitSvFuel"); _repair = V("PitRepairLeft"); _optRepair = V("PitOptRepairLeft");
        _pitStatus = V("PlayerCarPitSvStatus"); _pitActive = V("PitstopActive"); _inStall = V("PlayerCarInPitStall");
        _fastRepair = V("FastRepairAvailable"); _compound = V("PlayerTireCompound") ?? V("PitSvTireCompound");
        _air = V("AirTemp"); _track = V("TrackTempCrew") ?? V("TrackTemp"); _wind = V("WindVel"); _windDir = V("WindDir");
        _humidity = V("RelativeHumidity"); _fog = V("FogLevel"); _precip = V("Precipitation"); _wetness = V("TrackWetness"); _declaredWet = V("WeatherDeclaredWet");
        _battery = V("EnergyERSBatteryPct"); _mguK = V("PowerMGU_K"); _deploy = V("dcMGUKDeployMode") ?? V("dcMGUKDeployFixed");
        _p2pCount = V("P2P_Count"); _p2pOn = V("P2P_Status"); _drs = V("DRS_Status");
        _cPct = V("CarIdxLapDistPct"); _cLap = V("CarIdxLap"); _cLapDone = V("CarIdxLapCompleted");
        _cPos = V("CarIdxPosition"); _cClassPos = V("CarIdxClassPosition"); _cPit = V("CarIdxOnPitRoad"); _cSurface = V("CarIdxTrackSurface");
        _cEst = V("CarIdxEstTime"); _cF2 = V("CarIdxF2Time"); _cLast = V("CarIdxLastLapTime"); _cBest = V("CarIdxBestLapTime");
        _cCompound = V("CarIdxTireCompound"); _cFlags = V("CarIdxSessionFlags");
        for (int i = 0; i < 4; i++)
        {
            string c = Corners[i];
            _coldPress[i] = V(c + "coldPressure");
            _tempL[i] = V(c + "tempCL"); _tempM[i] = V(c + "tempCM"); _tempR[i] = V(c + "tempCR");
            _wearL[i] = V(c + "wearL"); _wearM[i] = V(c + "wearM"); _wearR[i] = V(c + "wearR");
        }
    }

    public void Decode(ReadOnlySpan<byte> data, TelemetryFrame f)
    {
        f.Reset();
        if (f.Raw.Length < data.Length) f.Raw = new byte[data.Length];
        data.CopyTo(f.Raw);
        f.RawLength = data.Length;
        f.Vars = _vars;

        byte[] raw = f.Raw;
        int length = data.Length;
        double? D(IrsdkVar? v, int i = 0) => v is null ? null : IrsdkMemory.Read(raw.AsSpan(0, length), v, i);
        int? I(IrsdkVar? v, int i = 0) => D(v, i) is { } d ? (int)d : null;
        bool B(IrsdkVar? v) => D(v) is > 0;

        f.SessionNum = I(_sessionNum) ?? 0;
        f.Phase = I(_sessionState) is { } st && Enum.IsDefined((SessionPhase)st) ? (SessionPhase)st : SessionPhase.Invalid;
        f.Flags = D(_sessionFlags) is { } flags ? (RaceFlags)(uint)(int)flags : RaceFlags.None;
        f.SessionTime = D(_sessionTime);
        f.SessionTimeRemain = D(_sessionTimeRemain) is >= 0 and < 604800 and var remain ? remain : null;
        f.SessionTimeTotal = D(_sessionTimeTotal) is > 0 and < 604800 and var total ? total : null;
        var lapsLeft = I(_lapsRemainEx) ?? I(_lapsRemain);
        f.SessionLapsRemain = lapsLeft is >= 0 and < 32767 ? lapsLeft : null;
        f.SessionLapsTotal = I(_lapsTotal) is > 0 and < 32767 and var lt ? lt : null;
        f.RaceLaps = I(_raceLaps);
        f.IsOnTrack = B(_isOnTrack);
        f.IsInGarage = B(_inGarage);
        f.IsReplayPlaying = B(_replay);
        f.PlayerCarIdx = I(_playerIdx) ?? f.Session.PlayerCarIdx;
        f.CamCarIdx = I(_camIdx) ?? -1;
        f.OnPitRoad = B(_onPitRoad);
        f.PlayerSurface = I(_surface) is { } surf && Enum.IsDefined((TrackSurface)surf) ? (TrackSurface)surf : null;

        f.Speed = D(_speed); f.Rpm = D(_rpm); f.Gear = I(_gear);
        f.Throttle = D(_throttle); f.Brake = D(_brake); f.Clutch = D(_clutch);
        f.Steering = D(_steer); f.SteeringMax = D(_steerMax) is > 0 and var sm ? sm : null;
        f.Fuel = D(_fuel); f.FuelPct = D(_fuelPct); f.FuelUsePerHour = D(_fuelPerHour);
        f.Lap = I(_lap); f.LapCompleted = I(_lapCompleted);
        f.LapDistPct = D(_lapPct); f.LapDist = D(_lapDist);
        f.LapCurrent = D(_lapCur) is >= 0 and var cur ? cur : null;
        f.LapLast = D(_lapLast) is > 0 and var last ? last : null;
        f.LapBest = D(_lapBest) is > 0 and var best ? best : null;
        f.DeltaBest = D(_dBest); f.DeltaBestOk = B(_dBestOk);
        f.DeltaSessionBest = D(_dSession); f.DeltaSessionBestOk = B(_dSessionOk);
        f.DeltaOptimal = D(_dOptimal); f.DeltaOptimalOk = B(_dOptimalOk);
        f.Incidents = I(_inc); f.TeamIncidents = I(_teamInc); f.DriverIncidents = I(_drvInc);
        f.Warnings = (EngineWarnings)(I(_warnings) ?? 0);
        f.OilTemp = D(_oilTemp); f.OilPress = D(_oilPress); f.WaterTemp = D(_waterTemp); f.FuelPress = D(_fuelPress); f.Voltage = D(_voltage);
        f.BrakeBias = D(_bias); f.TractionControl = D(_tc); f.Abs = D(_abs); f.EngineMap = D(_map);
        f.LatAccel = D(_lat); f.LongAccel = D(_long); f.YawRate = D(_yawRate); f.Yaw = D(_yaw); f.YawNorth = D(_yawNorth); f.VelocityX = D(_velX); f.VelocityY = D(_velY);
        f.AbsActive = _absActive is null ? null : B(_absActive);
        f.Sides = I(_leftRight) is { } lr2 && Enum.IsDefined((CarSides)lr2) ? (CarSides)lr2 : CarSides.Off;

        f.PitFlags = (PitServices)(I(_pitFlags) ?? 0);
        f.PitFuel = D(_pitFuel);
        f.PitRepairLeft = D(_repair); f.PitOptRepairLeft = D(_optRepair);
        f.PitStatus = I(_pitStatus);
        f.PitstopActive = B(_pitActive); f.InPitStall = B(_inStall);
        f.FastRepairsAvailable = I(_fastRepair);
        f.TyreCompound = I(_compound);
        for (int i = 0; i < 4; i++)
        {
            f.TyreColdPressure[i] = D(_coldPress[i]);
            f.TyreTempInner[i] = D(_tempL[i]); f.TyreTempMiddle[i] = D(_tempM[i]); f.TyreTempOuter[i] = D(_tempR[i]);
            f.TyreWearInner[i] = D(_wearL[i]); f.TyreWearMiddle[i] = D(_wearM[i]); f.TyreWearOuter[i] = D(_wearR[i]);
        }
        f.AirTemp = D(_air); f.TrackTemp = D(_track); f.WindSpeed = D(_wind); f.WindDir = D(_windDir);
        f.Humidity = D(_humidity); f.Fog = D(_fog); f.Precipitation = D(_precip); f.TrackWetness = I(_wetness);
        f.DeclaredWet = _declaredWet is null ? null : B(_declaredWet);
        f.BatteryPct = D(_battery); f.MguKPower = D(_mguK); f.DeployMode = D(_deploy); f.PushToPassCount = D(_p2pCount);
        f.PushToPassActive = _p2pOn is null ? null : B(_p2pOn);
        f.DrsActive = _drs is null ? null : I(_drs) >= 2;

        int n = TelemetryFrame.MaxCars;
        f.HasCarPositions = _cPos is not null;
        f.HasCarEstTime = _cEst is not null;
        f.HasCarLapTimes = _cLast is not null;
        f.HasCarF2Time = _cF2 is not null;
        for (int i = 0; i < n; i++)
        {
            f.CarLapDistPct[i] = D(_cPct, i) ?? -1;
            f.CarLap[i] = I(_cLap, i) ?? 0;
            f.CarLapCompleted[i] = I(_cLapDone, i) ?? 0;
            f.CarPosition[i] = I(_cPos, i) ?? 0;
            f.CarClassPosition[i] = I(_cClassPos, i) ?? 0;
            f.CarOnPitRoad[i] = D(_cPit, i) is > 0;
            f.CarSurface[i] = I(_cSurface, i) is { } s && Enum.IsDefined((TrackSurface)s) ? (TrackSurface)s : TrackSurface.NotInWorld;
            f.CarEstTime[i] = D(_cEst, i) ?? 0;
            f.CarF2Time[i] = D(_cF2, i) ?? 0;
            f.CarLastLap[i] = D(_cLast, i) ?? -1;
            f.CarBestLap[i] = D(_cBest, i) ?? -1;
            f.CarTyreCompound[i] = I(_cCompound, i) ?? -1;
            f.CarFlags[i] = D(_cFlags, i) is { } cf ? (RaceFlags)(uint)(int)cf : RaceFlags.None;
        }
    }
}

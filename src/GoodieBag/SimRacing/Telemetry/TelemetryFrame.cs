namespace GoodieBag.SimRacing.Telemetry;

internal enum SimStatus
{
    NotRunning,
    Waiting,
    Connected,
}

internal enum SessionPhase { Invalid = 0, GetInCar = 1, Warmup = 2, ParadeLaps = 3, Racing = 4, Checkered = 5, CoolDown = 6 }

internal enum TrackSurface { NotInWorld = -1, OffTrack = 0, InPitStall = 1, ApproachingPits = 2, OnTrack = 3 }

internal enum CarSides { Off = 0, Clear = 1, Left = 2, Right = 3, LeftRight = 4, TwoLeft = 5, TwoRight = 6 }

[Flags]
internal enum RaceFlags : uint
{
    None = 0,
    Checkered = 0x1, White = 0x2, Green = 0x4, Yellow = 0x8, Red = 0x10, Blue = 0x20, Debris = 0x40, Crossed = 0x80,
    YellowWaving = 0x100, OneLapToGreen = 0x200, GreenHeld = 0x400, TenToGo = 0x800, FiveToGo = 0x1000, RandomWaving = 0x2000,
    Caution = 0x4000, CautionWaving = 0x8000,
    Black = 0x10000, Disqualify = 0x20000, Servicible = 0x40000, Furled = 0x80000, Repair = 0x100000,
    StartHidden = 0x10000000, StartReady = 0x20000000, StartSet = 0x40000000, StartGo = 0x80000000,
}

[Flags]
internal enum EngineWarnings
{
    None = 0, WaterTemp = 0x1, FuelPressure = 0x2, OilPressure = 0x4, Stalled = 0x8, PitLimiter = 0x10, RevLimiter = 0x20, OilTemp = 0x40,
}

[Flags]
internal enum PitServices
{
    None = 0, LeftFront = 0x1, RightFront = 0x2, LeftRear = 0x4, RightRear = 0x8, Fuel = 0x10, Tearoff = 0x20, FastRepair = 0x40,
}

internal sealed class TelemetryFrame
{
    public const int MaxCars = 64;

    public SimStatus Status;
    public long Sequence;
    public int Tick;
    public long ReadAt;
    public bool Simulated;
    public SessionInfo Session = SessionInfo.Empty;
    public int SessionVersion;
    public byte[] Raw = Array.Empty<byte>();
    public int RawLength;
    public IReadOnlyDictionary<string, IrsdkVar> Vars = new Dictionary<string, IrsdkVar>();

    public int SessionNum;
    public SessionPhase Phase;
    public RaceFlags Flags;
    public double? SessionTime, SessionTimeRemain, SessionTimeTotal;
    public int? SessionLapsRemain, SessionLapsTotal;
    public int? RaceLaps;
    public bool IsOnTrack, IsInGarage, IsReplayPlaying;

    public int PlayerCarIdx = -1;
    public int CamCarIdx = -1;
    public bool OnPitRoad;
    public TrackSurface? PlayerSurface;
    public int? PlayerPosition, PlayerClassPosition;
    public double? Speed, Rpm, Throttle, Brake, Clutch, Steering, SteeringMax;
    public int? Gear;
    public double? Fuel, FuelPct, FuelUsePerHour;
    public int? Lap, LapCompleted;
    public double? LapDistPct, LapDist, LapCurrent, LapLast, LapBest;
    public double? DeltaBest, DeltaSessionBest, DeltaOptimal;
    public bool DeltaBestOk, DeltaSessionBestOk, DeltaOptimalOk;
    public int? Incidents, TeamIncidents, DriverIncidents;
    public EngineWarnings Warnings;
    public double? OilTemp, OilPress, WaterTemp, FuelPress, Voltage;
    public double? BrakeBias, TractionControl, Abs, EngineMap;
    public double? LatAccel, LongAccel, YawRate, Yaw, YawNorth, VelocityX, VelocityY;

    public bool? AbsActive;
    public CarSides Sides;

    public PitServices PitFlags;
    public double? PitFuel;
    public double? PitRepairLeft, PitOptRepairLeft;
    public int? PitStatus;
    public bool PitstopActive, InPitStall;
    public int? FastRepairsAvailable;

    public readonly double?[] TyreColdPressure = new double?[4];
    public readonly double?[] TyreTempInner = new double?[4], TyreTempMiddle = new double?[4], TyreTempOuter = new double?[4];
    public readonly double?[] TyreWearInner = new double?[4], TyreWearMiddle = new double?[4], TyreWearOuter = new double?[4];
    public int? TyreCompound;

    public double? AirTemp, TrackTemp, WindSpeed, WindDir, Humidity, Fog, Precipitation;
    public int? TrackWetness;
    public bool? DeclaredWet;

    public double? BatteryPct, MguKPower, DeployMode, PushToPassCount;
    public bool? PushToPassActive, DrsActive;

    public readonly double[] CarLapDistPct = new double[MaxCars];
    public readonly int[] CarLap = new int[MaxCars], CarLapCompleted = new int[MaxCars];
    public readonly int[] CarPosition = new int[MaxCars], CarClassPosition = new int[MaxCars];
    public readonly bool[] CarOnPitRoad = new bool[MaxCars];
    public readonly TrackSurface[] CarSurface = new TrackSurface[MaxCars];
    public readonly double[] CarEstTime = new double[MaxCars], CarF2Time = new double[MaxCars];
    public readonly double[] CarLastLap = new double[MaxCars], CarBestLap = new double[MaxCars];
    public readonly int[] CarTyreCompound = new int[MaxCars];
    public readonly RaceFlags[] CarFlags = new RaceFlags[MaxCars];
    public bool HasCarPositions, HasCarEstTime, HasCarLapTimes, HasCarF2Time;

    public double? RawValue(string name, int index = 0) =>
        Vars.TryGetValue(name, out var v) ? IrsdkMemory.Read(Raw.AsSpan(0, Math.Min(RawLength, Raw.Length)), v, index) : null;

    public bool CarActive(int idx) =>
        (uint)idx < MaxCars && Session.ByCar.TryGetValue(idx, out var d) && d.Races && CarSurface[idx] != TrackSurface.NotInWorld;

    public SessionEntry? CurrentSession => Session.Session(SessionNum);

    public SessionKind SessionKind => CurrentSession?.Kind ?? SessionKind.Other;

    public bool Driving => Status == SimStatus.Connected && IsOnTrack && !IsReplayPlaying;

    public void Reset()
    {
        Status = SimStatus.NotRunning;
        Simulated = false;
        Phase = SessionPhase.Invalid;
        Flags = RaceFlags.None;
        SessionTime = SessionTimeRemain = SessionTimeTotal = null;
        SessionLapsRemain = SessionLapsTotal = RaceLaps = null;
        IsOnTrack = IsInGarage = IsReplayPlaying = OnPitRoad = false;
        PlayerSurface = null;
        PlayerPosition = PlayerClassPosition = null;
        Speed = Rpm = Throttle = Brake = Clutch = Steering = SteeringMax = null;
        Gear = null;
        Fuel = FuelPct = FuelUsePerHour = null;
        Lap = LapCompleted = null;
        LapDistPct = LapDist = LapCurrent = LapLast = LapBest = null;
        DeltaBest = DeltaSessionBest = DeltaOptimal = null;
        DeltaBestOk = DeltaSessionBestOk = DeltaOptimalOk = false;
        Incidents = TeamIncidents = DriverIncidents = null;
        Warnings = EngineWarnings.None;
        OilTemp = OilPress = WaterTemp = FuelPress = Voltage = null;
        BrakeBias = TractionControl = Abs = EngineMap = null;
        LatAccel = LongAccel = YawRate = Yaw = YawNorth = VelocityX = VelocityY = null;
        AbsActive = null;
        Sides = CarSides.Off;
        PitFlags = PitServices.None;
        PitFuel = PitRepairLeft = PitOptRepairLeft = null;
        PitStatus = null;
        PitstopActive = InPitStall = false;
        FastRepairsAvailable = null;
        for (int i = 0; i < 4; i++)
            TyreColdPressure[i] = TyreTempInner[i] = TyreTempMiddle[i] = TyreTempOuter[i] = TyreWearInner[i] = TyreWearMiddle[i] = TyreWearOuter[i] = null;
        TyreCompound = null;
        AirTemp = TrackTemp = WindSpeed = WindDir = Humidity = Fog = Precipitation = null;
        TrackWetness = null;
        DeclaredWet = null;
        BatteryPct = MguKPower = DeployMode = PushToPassCount = null;
        PushToPassActive = DrsActive = null;
        Array.Fill(CarLapDistPct, -1);
        Array.Clear(CarLap);
        Array.Clear(CarLapCompleted);
        Array.Clear(CarPosition);
        Array.Clear(CarClassPosition);
        Array.Clear(CarOnPitRoad);
        Array.Fill(CarSurface, TrackSurface.NotInWorld);
        Array.Clear(CarEstTime);
        Array.Clear(CarF2Time);
        Array.Fill(CarLastLap, -1);
        Array.Fill(CarBestLap, -1);
        Array.Fill(CarTyreCompound, -1);
        Array.Clear(CarFlags);
        HasCarPositions = HasCarEstTime = HasCarLapTimes = HasCarF2Time = false;
    }
}

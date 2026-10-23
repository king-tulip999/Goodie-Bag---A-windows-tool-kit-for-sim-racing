using System.Diagnostics;
using GoodieBag.SimRacing.Race;

namespace GoodieBag.SimRacing.Telemetry;

internal sealed class DemoTelemetry
{
    private const double TrackKm = 5.2, RaceSeconds = 45 * 60, StartOffset = 14 * 60;
    private const int Player = 12, Cars = 23;
    private static readonly string[] Names =
    {
        "Pace Car", "Max Verstappen", "Lewis Hamilton", "Ayrton Senna", "Michael Schumacher", "Dale Earnhardt", "Jeff Gordon", "Danica Patrick",
        "Fernando Alonso", "John Doe", "Jane Doe", "Joe Bloggs", "You", "Richard Petty", "Jimmie Johnson", "Kimi Räikkönen", "Niki Lauda",
        "Ricky Bobby", "Mario Andretti", "James Hunt", "Daniel Ricciardo", "Jenson Button", "Average Joe",
    };

    private long _start = Stopwatch.GetTimestamp();
    private readonly SessionInfo _session;
    private readonly double[] _dist = new double[Cars];

    public DemoTelemetry() => _session = BuildSession();

    public void Restart() => _start = Stopwatch.GetTimestamp();

    private static SessionInfo BuildSession()
    {
        var drivers = new List<DriverEntry>();
        for (int i = 0; i < Cars; i++)
        {
            bool proto = i is >= 1 and <= 8;
            drivers.Add(new DriverEntry
            {
                CarIdx = i,
                Name = Names[i],
                AbbrevName = Names[i],
                TeamName = i == 0 ? "" : $"Team {Names[i].Split(' ')[^1]}",
                UserId = 1000 + i,
                CarNumber = i == 0 ? "0" : (i * 7 % 97 + 2).ToString(),
                CarName = i == 0 ? "Safety Car" : proto ? "Prototype LMDh" : i % 3 == 0 ? "GT3 Coupé" : i % 3 == 1 ? "GT3 Evo" : "GT3 R",
                CarShortName = i == 0 ? "Safety" : proto ? "LMDh" : i % 3 == 0 ? "GT3 C" : i % 3 == 1 ? "GT3 E" : "GT3 R",
                CarPath = proto ? "demoproto" : "demogt3",
                ClassId = i == 0 ? 11 : proto ? 4029 : 2708,
                ClassName = i == 0 ? "" : proto ? "GTP" : "GT3",
                ClassColor = i == 0 ? 0xffffff : proto ? 0xffda59 : 0x33ceff,
                ClassEstLapTime = proto ? 98.0 : 108.5,
                IRating = i == 0 ? null : 1400 + i * 137 % 2600,
                License = i == 0 ? "" : (i % 4) switch { 0 => "A 3.12", 1 => "B 2.87", 2 => "A 4.01", _ => "C 3.55" },
                IsPaceCar = i == 0,
                Incidents = i % 5,
            });
        }
        var byCar = drivers.ToDictionary(d => d.CarIdx);
        return new SessionInfo
        {
            TrackName = "demo raceway",
            TrackDisplayName = "Demo Raceway",
            Skies = "Partly Cloudy",
            TrackConfig = "Grand Prix",
            TrackId = 0,
            TrackLengthKm = TrackKm,
            PitSpeedLimitKph = 60,
            IncidentLimit = 17,
            NumCarClasses = 2,
            Sessions = new[]
            {
                new SessionEntry(0, SessionKind.Practice, "Practice", "PRACTICE", null, 1800),
                new SessionEntry(1, SessionKind.Qualify, "Open Qualify", "QUALIFY", null, 600),
                new SessionEntry(2, SessionKind.Race, "Race", "RACE", null, RaceSeconds),
            },
            Drivers = drivers,
            ByCar = byCar,
            Sectors = new[] { new SectorEntry(0, 0), new SectorEntry(1, 0.33), new SectorEntry(2, 0.68) },
            PlayerCarIdx = Player,
            FuelMaxLiters = 120,
            MaxFuelPct = 1,

            PlayerEstLapTime = 108.5,
            ShiftLightFirstRpm = 6500,
            ShiftRpm = 8400,
        };
    }

    private static double Noise(int a, int b)
    {
        uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h % 2001) / 1000.0 - 1;
    }

    private static double LapTime(int car) => car is >= 1 and <= 8 ? 97.4 + car * 0.05 : 107.2 + (car - 9) * 0.045;

    private static double LapOf(int car, int lap) => LapTime(car) + 0.3 + 0.7 * Noise(car, lap) - 0.035 * Math.Min(lap, 40);

    private static double Dist(int car, double t)
    {
        double lap = LapTime(car);
        double grid = -(car - 1) * 0.006;
        double swing = 0.03 * Math.Sin(t / 40 + car * 1.7) + 0.008 * Math.Sin(t / 14 + car * 2.9);
        double cycle = t + car * 97;
        double stopped = Math.Floor(cycle / 1500) * 40 + Math.Clamp(cycle % 1500, 0, 40) - Math.Clamp(car * 97 % 1500, 0, 40);
        return Math.Max(0, (t - stopped) / lap + grid + swing);
    }

    private static bool InPits(int car, double t) => (t + car * 97) % 1500 < 40 && t > 60;

    private static double SpeedAt(double pct)
    {
        double v = 72;
        (double At, double Depth, double Width)[] corners = { (0.08, 42, 0.02), (0.21, 30, 0.025), (0.33, 22, 0.03), (0.47, 48, 0.018), (0.6, 26, 0.03), (0.74, 35, 0.022), (0.9, 30, 0.025) };
        foreach (var (at, depth, width) in corners)
        {
            double d = FieldOrder.Wrap(pct, at) / width;
            v -= depth * Math.Exp(-d * d);
        }
        return Math.Max(18, v);
    }

    public void Fill(TelemetryFrame f) => Fill(f, Stopwatch.GetElapsedTime(_start).TotalSeconds);

    public void Fill(TelemetryFrame f, double since)
    {
        double t = StartOffset + since;
        double raceT = t % RaceSeconds;
        f.Reset();
        f.Simulated = true;
        f.Status = SimStatus.Connected;
        f.Session = _session;
        f.SessionVersion = 1;
        f.SessionNum = 2;
        f.Phase = SessionPhase.Racing;
        f.SessionTime = raceT;
        f.SessionTimeTotal = RaceSeconds;
        f.SessionTimeRemain = RaceSeconds - raceT;
        f.IsOnTrack = true;
        f.PlayerCarIdx = Player;
        f.CamCarIdx = Player;

        var dist = _dist;
        for (int i = 1; i < Cars; i++) dist[i] = Dist(i, raceT);
        f.HasCarPositions = f.HasCarEstTime = f.HasCarLapTimes = f.HasCarF2Time = true;
        double leaderDist = 0;
        for (int i = 1; i < Cars; i++) leaderDist = Math.Max(leaderDist, dist[i]);
        for (int i = 1; i < Cars; i++)
        {
            double d = dist[i], pct = d - Math.Floor(d);
            f.CarLapDistPct[i] = pct;
            f.CarLap[i] = (int)Math.Floor(d) + 1;
            f.CarLapCompleted[i] = (int)Math.Floor(d);
            f.CarOnPitRoad[i] = InPits(i, raceT);
            f.CarSurface[i] = f.CarOnPitRoad[i] ? TrackSurface.InPitStall : TrackSurface.OnTrack;
            f.CarEstTime[i] = pct * _session.ByCar[i].ClassEstLapTime!.Value;
            f.CarF2Time[i] = (leaderDist - d) * 100;
            int laps = (int)Math.Floor(d);
            f.CarLastLap[i] = laps > 0 ? LapOf(i, laps) : -1;
            double best = double.MaxValue;
            for (int l = 1; l <= laps; l++) best = Math.Min(best, LapOf(i, l));
            f.CarBestLap[i] = laps > 0 ? best : -1;
            f.CarTyreCompound[i] = 0;
            f.CarFlags[i] = i == 5 ? RaceFlags.Repair : i == 9 ? RaceFlags.Black : RaceFlags.None;
        }
        f.CarLapDistPct[0] = -1;
        var order = Enumerable.Range(1, Cars - 1).OrderByDescending(i => dist[i]).ToArray();
        int gtp = 0, gt3 = 0;
        for (int p = 0; p < order.Length; p++)
        {
            int car = order[p];
            f.CarPosition[car] = p + 1;
            f.CarClassPosition[car] = car <= 8 ? ++gtp : ++gt3;
        }
        f.CarSurface[22] = TrackSurface.NotInWorld;
        f.CarLapDistPct[22] = -1;

        double pd = dist[Player], ppct = pd - Math.Floor(pd);
        double v = SpeedAt(ppct), ahead = SpeedAt(ppct + 0.004);
        f.Speed = f.CarOnPitRoad[Player] ? 16.4 : v;
        f.OnPitRoad = f.CarOnPitRoad[Player];
        f.InPitStall = f.OnPitRoad && (raceT + Player * 97) % 1500 is > 12 and < 32;
        f.PlayerSurface = f.OnPitRoad ? TrackSurface.InPitStall : TrackSurface.OnTrack;
        double accel = ahead - v;
        f.Throttle = f.OnPitRoad ? 0.3 : accel > -0.2 ? Math.Clamp(0.55 + accel / 3, 0, 1) : 0;
        f.Brake = f.OnPitRoad ? 0 : accel < -0.2 ? Math.Clamp(-accel / 6, 0, 1) : 0;
        f.Clutch = 1;
        int gear = Math.Clamp((int)(f.Speed.Value / 11.5) + 1, 1, 6);
        f.Gear = gear;
        double gearTop = gear * 11.5 + 2;
        f.Rpm = Math.Clamp(4200 + (f.Speed.Value - (gear - 1) * 11.5) / (gearTop - (gear - 1) * 11.5) * 4400, 3000, 8750);
        f.Steering = 1.2 * Math.Sin(ppct * 2 * Math.PI * 7) * (1 - v / 80);
        f.SteeringMax = 7.85;
        f.Lap = f.CarLap[Player];
        f.LapCompleted = f.CarLapCompleted[Player];
        f.LapDistPct = ppct;
        f.LapDist = ppct * TrackKm * 1000;
        f.LapCurrent = ppct * LapTime(Player);
        f.LapLast = f.CarLastLap[Player] > 0 ? f.CarLastLap[Player] : null;
        f.LapBest = f.CarBestLap[Player] > 0 ? f.CarBestLap[Player] : null;
        f.DeltaBest = 0.35 * Math.Sin(ppct * 9) + 0.12;
        f.DeltaBestOk = true;
        f.DeltaSessionBest = f.DeltaBest + 0.6;
        f.DeltaSessionBestOk = true;
        f.DeltaOptimal = f.DeltaBest + 0.25;
        f.DeltaOptimalOk = true;
        double stintLaps = pd % 30;
        f.Fuel = Math.Max(1.2, 92 - stintLaps * 3.05);
        f.FuelPct = f.Fuel / 120;
        f.FuelUsePerHour = 72;
        f.Incidents = Math.Min(8, (int)(raceT / 420) * 2);
        f.TeamIncidents = f.Incidents;
        f.DriverIncidents = f.Incidents;
        f.OilTemp = 104 + 3 * Math.Sin(t / 60);
        f.OilPress = 4.1;
        f.WaterTemp = 88 + 2 * Math.Sin(t / 80);
        f.FuelPress = 3.2;
        f.Voltage = 13.8;
        f.BrakeBias = 54.5;
        f.TractionControl = 4;
        f.Abs = 6;
        f.EngineMap = 1;
        f.LatAccel = 14 * Math.Sin(ppct * 2 * Math.PI * 7) * (1 - v / 90);
        f.LongAccel = accel * 4;
        f.YawRate = 0.3 * Math.Sin(ppct * 40);
        f.YawNorth = ppct * 2 * Math.PI;
        f.AbsActive = f.Brake > 0.85;
        f.PushToPassCount = 150;
        f.PushToPassActive = (int)(t / 15) % 4 == 0 && f.Throttle > 0.95;
        f.Warnings = f.OnPitRoad ? EngineWarnings.PitLimiter : EngineWarnings.None;
        f.PitFlags = PitServices.Fuel | PitServices.LeftFront | PitServices.RightFront | PitServices.LeftRear | PitServices.RightRear;
        f.PitFuel = 60;
        f.PitStatus = f.InPitStall ? 1 : 0;
        f.PitstopActive = f.InPitStall;
        f.FastRepairsAvailable = 1;
        f.TyreCompound = 0;
        for (int c = 0; c < 4; c++)
        {
            f.TyreColdPressure[c] = 172 + c;
            f.TyreTempInner[c] = 84 + c * 2; f.TyreTempMiddle[c] = 81 + c; f.TyreTempOuter[c] = 77 + c;
            f.TyreWearInner[c] = 0.94 - c * 0.01; f.TyreWearMiddle[c] = 0.95 - c * 0.01; f.TyreWearOuter[c] = 0.96 - c * 0.01;
        }
        f.AirTemp = 21.5 + Math.Sin(t / 900);
        f.TrackTemp = 31.2 + 2 * Math.Sin(t / 900);
        f.WindSpeed = 3.4;
        f.WindDir = 1.2;
        f.Humidity = 0.55;
        f.Fog = 0;
        f.Precipitation = 0;
        f.TrackWetness = 1;
        f.DeclaredWet = false;

        var flags = RaceFlags.Green;
        if (raceT % 300 is > 120 and < 135) flags |= RaceFlags.Yellow;
        double length = TrackKm * 1000;
        bool left = false, right = false;
        for (int i = 1; i < Cars; i++)
        {
            if (i == Player || f.CarSurface[i] == TrackSurface.NotInWorld || f.CarOnPitRoad[i] != f.OnPitRoad) continue;
            double m = FieldOrder.Wrap(f.CarLapDistPct[i], ppct) * length;
            if (Math.Abs(m) < 5.5) { if (i % 2 == 0) left = true; else right = true; }
            if (i <= 8 && m < 0 && m > -60) flags |= RaceFlags.Blue;
        }
        f.Flags = flags;
        f.Sides = left && right ? CarSides.LeftRight : left ? CarSides.Left : right ? CarSides.Right : CarSides.Clear;
    }
}

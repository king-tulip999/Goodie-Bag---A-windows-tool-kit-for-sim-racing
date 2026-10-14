using GoodieBag.SimRacing.Hud.Widgets;
using GoodieBag.SimRacing.TrackMaps;

namespace GoodieBag.SessionStats;

internal static class StatsDemo
{
    public const int LeagueId = 4242;

    private static readonly (string Car, string Class, double Pace)[] Cars =
    {
        ("Porsche 911 GT3 R (992)", "GT3", 1.0), ("Ferrari 296 GT3", "GT3", 1.003), ("Global Mazda MX-5 Cup", "MX-5", 1.22),
        ("Super Formula SF23", "SF23", 0.86), ("Dallara IR18", "IR18", 0.84),
    };

    private static readonly (string Track, string Config, double Km, double Lap)[] Tracks =
    {
        ("Circuit de Spa-Francorchamps", "Grand Prix Pits", 7.004, 138.2), ("Road America", "Full Course", 6.515, 128.4),
        ("Watkins Glen International", "Boot", 5.552, 110.6), ("Okayama International Circuit", "Full Course", 3.703, 86.9),
        ("WeatherTech Raceway at Laguna Seca", "Full Course", 3.602, 84.1), ("Suzuka International Racing Course", "Grand Prix", 5.807, 121.5),
    };

    public static void Fill(StatsStore store)
    {
        var rnd = new Random(42);
        var more = new Random(7);
        var now = DateTime.UtcNow;
        int ir = 2140;
        double sr = 3.12;
        var starts = Enumerable.Range(0, 90).Select(_ => now.AddDays(-rnd.Next(0, 150)).Date.AddHours(rnd.Next(16, 23)).AddMinutes(rnd.Next(0, 60))).OrderBy(t => t).ToList();
        foreach (var start in starts)
        {
            var car = Cars[rnd.Next(Cars.Length)];
            var track = Tracks[rnd.Next(Tracks.Length)];
            double roll = rnd.NextDouble();
            string kind = roll < 0.5 ? "Practice" : roll < 0.65 ? "Qualify" : "Race";
            bool league = kind == "Race" && rnd.NextDouble() < 0.35;
            int lapCount = kind switch { "Qualify" => rnd.Next(3, 6), "Race" => rnd.Next(12, 26), _ => rnd.Next(5, 31) };
            double baseLap = track.Lap * car.Pace;
            bool wet = rnd.NextDouble() < 0.12;
            var laps = new List<LapRow>();
            int incidents = 0, run = 1, pitLap = kind == "Race" && lapCount > 18 ? lapCount / 2 : kind == "Practice" && lapCount > 12 ? lapCount / 2 : 0;
            string setup = more.NextDouble() < 0.5 ? "baseline.sto" : "race_setup_v2.sto";
            for (int n = 1; n <= lapCount; n++)
            {
                bool clean = rnd.NextDouble() > 0.18;
                if (!clean) incidents += rnd.Next(0, 3) * 2;
                double time = baseLap * (1 + (wet ? 0.07 : 0) + Gauss(rnd) * 0.006 + (clean ? 0 : 0.02) - Math.Min(0.01, start.Subtract(now.AddDays(-150)).TotalDays / 15000));
                bool pit = n == 1 && kind != "Race" || n == pitLap || n == pitLap + 1;
                int lapInc = clean ? 0 : more.Next(0, 3) * 2;
                var flags = (pit ? LapFlags.PitLane : 0) | (!clean && lapInc == 0 ? LapFlags.OffTrack : 0) | (lapInc > 0 ? LapFlags.Incident : 0);
                if (pitLap > 0 && n == pitLap + 1) run++;
                laps.Add(new LapRow(n, time, clean && !pit, 240 / car.Pace + Gauss(rnd) * 4, 2.6 + rnd.NextDouble() * 0.8, 58 + rnd.NextDouble() * 12,
                    11 + rnd.NextDouble() * 7, rnd.Next(14, 26), 1.7 / car.Pace + rnd.NextDouble() * 0.3, Split(time, rnd), flags, lapInc, run, setup));
            }
            double drive = laps.Sum(l => l.Time) + rnd.Next(60, 400);
            int field = rnd.Next(14, 26), grid = rnd.Next(1, field + 1);
            bool finished = kind == "Race" && rnd.NextDouble() > 0.1;
            int finish = Math.Clamp(grid + rnd.Next(-5, 6), 1, field);
            var row = new SessionRow
            {
                StartUtc = start, EndUtc = start.AddSeconds(drive + rnd.Next(120, 900)), Source = rnd.NextDouble() < 0.3 ? "live" : "ibt",
                Driver = "Sam Driver", DriverId = 123456, CarNumber = league ? "42" : rnd.NextDouble() < 0.6 ? "42" : "7",
                Car = car.Car, CarClass = car.Class, Track = track.Track, TrackConfig = track.Config, TrackKm = track.Km,
                SessionType = kind, Category = "Road", Weather = wet ? "Realistic · Overcast" : "Realistic · Partly Cloudy", DynamicWeather = rnd.NextDouble() < 0.4,
                Laps = laps.Count, Incidents = incidents, StartPos = kind == "Race" ? grid : null, FinishPos = finished ? finish : null, Finished = finished,
                IrStart = ir, SrStart = Math.Round(sr, 2), LeagueId = league ? LeagueId : 0, SubSession = 70_000_000 + rnd.Next(1_000_000), SessionNum = kind == "Race" ? 2 : 0,
                Official = !league && kind == "Race", SeasonId = 5000, RaceWeek = !league && kind == "Race" ? (int)((start - TimeRanges.SeasonStart(start, null)).TotalDays / 7) : null,
                DriveS = drive, GarageS = rnd.Next(60, 900), LongestStintS = Math.Min(drive, laps.Count * baseLap * 0.7),
                RainS = wet ? drive * 0.8 : 0, NightS = rnd.NextDouble() < 0.15 ? drive * 0.5 : 0,
                TrackTempMin = 18 + rnd.Next(0, 10), TrackTempMax = 30 + rnd.Next(0, 14), DistanceKm = laps.Count * track.Km + 2,
                AirTemp = wet ? 14 + more.Next(0, 6) : 19 + more.Next(0, 10), WindKph = 4 + more.NextDouble() * 18, WindDir = more.NextDouble() * 2 * Math.PI,
                Humidity = wet ? 0.9 : 0.4 + more.NextDouble() * 0.4, Pressure = 100_400 + more.Next(-900, 900), AirDensity = 1.15 + more.NextDouble() * 0.06,
                Skies = wet ? 3 : more.Next(0, 3), RainMax = wet ? 0.35 : 0, WetnessMax = wet ? 4 : 1,
                Rubber = kind == "Race" ? "moderately high usage" : "moderate usage", SimStart = $"{start:yyyy-MM-dd} {13 + more.Next(0, 5):00}:{more.Next(0, 4) * 15:00}",
                PitStops = kind == "Race" && lapCount > 18 ? 1 : 0, PitS = kind == "Race" && lapCount > 18 ? 38 + rnd.NextDouble() * 12 : 0,
            };
            store.Save(row, laps);
            if (row.Official) { ir += rnd.Next(-45, 70); sr = Math.Clamp(sr + (incidents < 4 ? 0.12 : -0.2), 1, 4.99); }
        }
        AddTraces(store, rnd);
    }

    private static readonly double[] SectorShares = { 0.33, 0.35, 0.32 };

    private static List<double> Split(double time, Random rnd)
    {
        var parts = SectorShares.Select(s => s * (1 + Gauss(rnd) * 0.004)).ToList();
        double sum = parts.Sum();
        return parts.Select(p => p / sum * time).ToList();
    }

    internal static readonly double[] SpaSectors = { 0, 0.313, 0.523, 0.723 };

    private static void AddTraces(StatsStore store, Random rnd)
    {
        if (store.Traces is not { } traces) return;
        var map = TrackMapLibrary.ByKey(TrackMapWidget.SampleKey).GetAwaiter().GetResult();
        if (map is null) return;
        var spa = Tracks[0];
        var course = Course(map, spa.Km * 1000);
        var sessions = store.Sessions(0, long.MaxValue).Where(x => x.Track == spa.Track).Take(5).ToList();
        foreach (var session in sessions)
        {
            var row = new SessionRow { StartUtc = session.StartUtc, Car = session.Car, Track = session.Track, TrackConfig = session.Config, Traces = new() };
            var stored = new List<(int, double, bool)>();
            foreach (var lap in store.Laps(session.Id))
            {
                var trace = Drive(course, lap.Time, rnd, lap.Clean ? 0.6 : 2.5);
                row.Traces[lap.Lap] = trace;
                store.Db.Run("UPDATE laps SET sectors = ?3 WHERE session_id = ?1 AND lap = ?2", session.Id, lap.Lap, StatsStore.SectorText(LapTrace.SectorTimes(trace, SpaSectors)));
                stored.Add((lap.Lap, lap.Time, lap.Clean));
            }
            traces.Attach(session.Id, row, stored, row.Traces, 0);
        }
        if (sessions.FirstOrDefault() is { } latest && latest.Best is { } best)
        {
            var rival = Drive(course, best - 0.4, new Random(11), 0.4);
            traces.AddShared(new SharedLap("Alex Rivera", latest.Car, spa.Track, spa.Config, LapTrace.SectorTimes(rival, SpaSectors), latest.StartUtc.AddDays(-3), rival), latest.StartUtc);
        }
    }

    internal sealed record CourseShape(double[] X, double[] Y, double[] Curvature, double[] Limit);

    internal static CourseShape Course(TrackMap map, double length)
    {
        int n = (int)length + 1;
        double scale = length / map.Length;
        var x = new double[n];
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            var p = map.At(i / length);
            x[i] = p.X * scale;
            y[i] = -p.Y * scale;
        }
        x = Smoothed(x, 12);
        y = Smoothed(y, 12);
        var heading = new double[n];
        for (int i = 0; i < n; i++)
        {
            int a = (i - 6 + n) % n, b = (i + 6) % n;
            heading[i] = Math.Atan2(y[b] - y[a], x[b] - x[a]);
        }
        var curvature = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int k = -8; k <= 8; k++)
            {
                int a = (i + k - 1 + 2 * n) % n, b = (i + k + 1 + 2 * n) % n;
                sum += Math.IEEERemainder(heading[b] - heading[a], 2 * Math.PI) / 2;
            }
            curvature[i] = sum / 17;
        }
        var v = curvature.Select(k => Math.Min(78, Math.Sqrt(22 / Math.Max(1e-5, Math.Abs(k))))).ToArray();
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 1; i < n * 2; i++)
            {
                int at = i % n, prev = (i - 1) % n;
                double acc = 7 * Math.Max(0.15, 1 - v[prev] / 85);
                v[at] = Math.Min(v[at], Math.Sqrt(v[prev] * v[prev] + 2 * acc));
            }
            for (int i = n * 2 - 2; i >= 0; i--)
            {
                int at = i % n, next = (i + 1) % n;
                v[at] = Math.Min(v[at], Math.Sqrt(v[next] * v[next] + 2 * 14));
            }
        }
        return new CourseShape(x, y, curvature, v);
    }

    internal static LapTrace Drive(CourseShape course, double lapTime, Random rnd, double wander)
    {
        int n = course.X.Length;
        double phaseA = rnd.NextDouble() * Math.Tau, phaseB = rnd.NextDouble() * Math.Tau, lineA = rnd.NextDouble() * Math.Tau;
        var speed = new double[n];
        for (int i = 0; i < n; i++)
        {
            double u = (double)i / n;
            speed[i] = course.Limit[i] * (1 + 0.012 * Math.Sin(Math.Tau * 7 * u + phaseA) + 0.008 * Math.Sin(Math.Tau * 13 * u + phaseB));
        }
        double natural = 0;
        for (int i = 1; i < n; i++) natural += 2 / (speed[i] + speed[i - 1]);
        for (int i = 0; i < n; i++) speed[i] *= natural / lapTime;
        var time = new double[n];
        for (int i = 1; i < n; i++) time[i] = time[i - 1] + 2 / (speed[i] + speed[i - 1]);
        var throttle = new double[n];
        var brake = new double[n];
        var steering = new double[n];
        var gear = new double[n];
        var rpm = new double[n];
        var lat = new double[n];
        var lon = new double[n];
        var xs = new double[n];
        var ys = new double[n];
        double[] shifts = { 0, 17, 27, 37, 47, 59, 75 };
        for (int i = 0; i < n; i++)
        {
            int next = Math.Min(n - 1, i + 12), prev = Math.Max(0, i - 12);
            double accel = speed[i] * (speed[next] - speed[prev]) / Math.Max(1, next - prev);
            lon[i] = accel;
            lat[i] = speed[i] * speed[i] * course.Curvature[i];
            brake[i] = accel < -1.5 ? Math.Clamp(-accel / 14, 0, 1) : 0;
            throttle[i] = brake[i] > 0 ? 0 : accel > 0.2 ? Math.Clamp(0.6 + accel / 5, 0, 1) : 0.5;
            steering[i] = Math.Atan(2.7 * course.Curvature[i]) * 14;
            int g = 1;
            while (g < 6 && speed[i] > shifts[g]) g++;
            gear[i] = g;
            rpm[i] = 3800 + 3600 * Math.Clamp((speed[i] - shifts[g - 1]) / (shifts[g] - shifts[g - 1]), 0, 1);
            int a = (i - 3 + n) % n, b = (i + 3) % n;
            double hx = course.X[b] - course.X[a], hy = course.Y[b] - course.Y[a], len = Math.Max(1e-6, Math.Sqrt(hx * hx + hy * hy));
            double offset = wander * Math.Sin(Math.Tau * 9 * i / n + lineA) - Math.Clamp(course.Curvature[i] * 400, -3, 3);
            xs[i] = course.X[i] - hy / len * offset;
            ys[i] = course.Y[i] + hx / len * offset;
        }
        var trace = new LapTrace(1, n - 1, time[^1], 50.4372, 5.9714, n);
        trace[TraceChannel.Time] = time;
        trace[TraceChannel.Speed] = speed;
        trace[TraceChannel.Throttle] = throttle;
        trace[TraceChannel.Brake] = brake;
        trace[TraceChannel.Steering] = steering;
        trace[TraceChannel.Gear] = gear;
        trace[TraceChannel.Rpm] = rpm;
        trace[TraceChannel.LatAccel] = lat;
        trace[TraceChannel.LongAccel] = lon;
        trace[TraceChannel.X] = xs;
        trace[TraceChannel.Y] = ys;
        return trace;
    }

    private static double[] Smoothed(double[] v, int radius)
    {
        int n = v.Length;
        for (int pass = 0; pass < 2; pass++)
        {
            var o = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++) sum += v[((i + k) % n + n) % n];
                o[i] = sum / (2 * radius + 1);
            }
            v = o;
        }
        return v;
    }

    private static double Gauss(Random rnd) => Math.Sqrt(-2 * Math.Log(1 - rnd.NextDouble())) * Math.Cos(2 * Math.PI * rnd.NextDouble());
}

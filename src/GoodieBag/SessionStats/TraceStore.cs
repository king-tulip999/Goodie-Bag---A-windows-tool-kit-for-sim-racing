using System.IO;

namespace GoodieBag.SessionStats;

internal sealed record TraceInfo(long Session, int Lap, double LapTime, string Car, string Track, string Config, long StartUtc, bool Clean);

internal sealed record TraceRetention(int KeepDays, int LapsPerSession, bool KeepBests);

internal sealed class TraceStore : IDisposable
{
    private readonly SqliteDb _db;
    private readonly string _path;

    public static string DefaultPath => Path.Combine(Config.ConfigStore.Folder, "traces.db");

    private TraceStore(SqliteDb db, string path)
    {
        _db = db;
        _path = path;
        if (_db.Scalar("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'traces'") is null) _db.Exec("PRAGMA auto_vacuum = INCREMENTAL;");
        _db.Exec("""
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS traces(
                session_id INTEGER NOT NULL, lap INTEGER NOT NULL, lap_time REAL NOT NULL,
                car TEXT NOT NULL, track TEXT NOT NULL, track_config TEXT NOT NULL, start_utc INTEGER NOT NULL, clean INTEGER NOT NULL,
                step_m REAL NOT NULL, origin_lat REAL NOT NULL, origin_lon REAL NOT NULL, points INTEGER NOT NULL, data BLOB NOT NULL,
                PRIMARY KEY(session_id, lap));
            CREATE INDEX IF NOT EXISTS traces_pair ON traces(car, track, track_config);
            CREATE TABLE IF NOT EXISTS shared_laps(
                id INTEGER PRIMARY KEY, driver TEXT NOT NULL, car TEXT NOT NULL, track TEXT NOT NULL, track_config TEXT NOT NULL,
                lap_time REAL NOT NULL, sectors TEXT, recorded_utc INTEGER NOT NULL, imported_utc INTEGER NOT NULL, data BLOB NOT NULL);
            """);
    }

    public static TraceStore Open(string path)
    {
        if (path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new TraceStore(SqliteDb.Open(path), path);
    }

    public string? Meta(string key) => _db.Scalar("SELECT value FROM meta WHERE key = ?1", key) as string;

    public void SetMeta(string key, string value) => _db.Run("INSERT OR REPLACE INTO meta(key, value) VALUES(?1, ?2)", key, value);

    public void Attach(long session, SessionRow row, IReadOnlyList<(int Lap, double Time, bool Clean)> stored, IReadOnlyDictionary<int, LapTrace> traces, int? offset)
    {
        var pairs = Match(stored, traces, offset);
        if (pairs.Count == 0) return;
        long start = StatsStore.Unix(row.StartUtc);
        _db.Transaction(() =>
        {
            foreach (var (lap, trace) in pairs)
                _db.Run("""
                    INSERT OR REPLACE INTO traces(session_id, lap, lap_time, car, track, track_config, start_utc, clean, step_m, origin_lat, origin_lon, points, data)
                    VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13)
                    """, session, lap.Lap, lap.Time, row.Car, row.Track, row.TrackConfig, start, lap.Clean, trace.StepM, trace.OriginLat, trace.OriginLon, trace.Points, trace.Encode());
        });
    }

    internal static List<((int Lap, double Time, bool Clean) Lap, LapTrace Trace)> Match(IReadOnlyList<(int Lap, double Time, bool Clean)> stored,
        IReadOnlyDictionary<int, LapTrace> traces, int? offset)
    {
        var result = new List<((int, double, bool), LapTrace)>();
        var taken = new HashSet<int>();
        foreach (var (number, trace) in traces.OrderBy(t => t.Key))
        {
            int at = -1;
            double nearest = 0.05;
            for (int i = 0; i < stored.Count; i++)
            {
                double off = Math.Abs(stored[i].Time - trace.LapTime);
                if (off <= nearest && !taken.Contains(i)) { nearest = off; at = i; }
            }
            if (at < 0 && offset is { } o)
                for (int i = 0; i < stored.Count; i++)
                    if (stored[i].Lap == number + o && !taken.Contains(i) && Math.Abs(stored[i].Time - trace.LapTime) < 1) { at = i; break; }
            if (at < 0) continue;
            taken.Add(at);
            result.Add((stored[at], trace));
        }
        return result;
    }

    public void DeleteSession(long session) => _db.Run("DELETE FROM traces WHERE session_id = ?1", session);

    public bool Has(long session) => _db.Scalar("SELECT 1 FROM traces WHERE session_id = ?1 LIMIT 1", session) is not null;

    public HashSet<int> LapsWithTrace(long session) =>
        _db.Query("SELECT lap FROM traces WHERE session_id = ?1", session).Select(r => Convert.ToInt32(r[0])).ToHashSet();

    public HashSet<long> SessionsWithTraces(IEnumerable<long> sessions)
    {
        var ids = sessions.ToList();
        if (ids.Count == 0) return new HashSet<long>();
        return _db.Query($"SELECT DISTINCT session_id FROM traces WHERE session_id IN ({string.Join(",", ids)})").Select(r => (long)r[0]!).ToHashSet();
    }

    public LapTrace? Load(long session, int lap)
    {
        if (_db.Scalar("SELECT data FROM traces WHERE session_id = ?1 AND lap = ?2", session, lap) is not byte[] data) return null;
        try { return LapTrace.Decode(data); }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { App.LogError(ex); return null; }
    }

    public (long Session, int Lap, double Time)? BestOf(string car, string track, string config) =>
        _db.Query("SELECT session_id, lap, lap_time FROM traces WHERE car = ?1 AND track = ?2 AND track_config = ?3 AND clean = 1 ORDER BY lap_time LIMIT 1", car, track, config)
            is { Count: > 0 } r ? ((long)r[0][0]!, Convert.ToInt32(r[0][1]), Convert.ToDouble(r[0][2])) : null;

    public List<TraceInfo> All() =>
        _db.Query("SELECT session_id, lap, lap_time, car, track, track_config, start_utc, clean FROM traces")
            .Select(r => new TraceInfo((long)r[0]!, Convert.ToInt32(r[1]), Convert.ToDouble(r[2]), (string)r[3]!, (string)r[4]!, (string)r[5]!, (long)r[6]!, Convert.ToInt64(r[7]) != 0)).ToList();

    public (long Bytes, long Laps, double AverageBytes) Usage()
    {
        long pages = Convert.ToInt64(_db.Scalar("PRAGMA page_count") ?? 0L), size = Convert.ToInt64(_db.Scalar("PRAGMA page_size") ?? 0L);
        var r = _db.Query("SELECT COUNT(*), AVG(LENGTH(data)) FROM traces")[0];
        return (pages * size, Convert.ToInt64(r[0] ?? 0L), Convert.ToDouble(r[1] ?? 0d));
    }

    public int Prune(TraceRetention keep, DateTime nowUtc, long? protectedSession)
    {
        var drop = Pruned(All(), keep, nowUtc, protectedSession);
        if (drop.Count == 0) return 0;
        _db.Transaction(() =>
        {
            foreach (var t in drop) _db.Run("DELETE FROM traces WHERE session_id = ?1 AND lap = ?2", t.Session, t.Lap);
        });
        _db.Exec("PRAGMA incremental_vacuum;");
        return drop.Count;
    }

    internal static List<TraceInfo> Pruned(IReadOnlyList<TraceInfo> all, TraceRetention keep, DateTime nowUtc, long? protectedSession)
    {
        var bests = keep.KeepBests
            ? all.Where(t => t.Clean).GroupBy(t => (t.Car, t.Track, t.Config)).Select(g => g.MinBy(t => t.LapTime)!).ToHashSet()
            : new HashSet<TraceInfo>();
        var drop = new HashSet<TraceInfo>();
        if (keep.KeepDays > 0)
        {
            long cutoff = StatsStore.Unix(nowUtc.AddDays(-keep.KeepDays));
            foreach (var t in all) if (t.StartUtc < cutoff) drop.Add(t);
        }
        if (keep.LapsPerSession > 0)
            foreach (var session in all.GroupBy(t => t.Session))
                foreach (var t in session.OrderByDescending(t => t.Clean).ThenBy(t => t.LapTime).Skip(keep.LapsPerSession)) drop.Add(t);
        return drop.Where(t => t.Session != protectedSession && !bests.Contains(t)).OrderBy(t => t.Session).ThenBy(t => t.Lap).ToList();
    }

    public long AddShared(SharedLap lap, DateTime nowUtc)
    {
        long recorded = StatsStore.Unix(lap.RecordedUtc);
        long id = 0;
        _db.Transaction(() =>
        {
            _db.Run("DELETE FROM shared_laps WHERE driver = ?1 AND car = ?2 AND track = ?3 AND track_config = ?4 AND abs(lap_time - ?5) < 0.0005 AND recorded_utc = ?6",
                lap.Driver, lap.Car, lap.Track, lap.Config, lap.LapTime, recorded);
            id = _db.Insert("INSERT INTO shared_laps(driver, car, track, track_config, lap_time, sectors, recorded_utc, imported_utc, data) VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)",
                lap.Driver, lap.Car, lap.Track, lap.Config, lap.LapTime, StatsStore.SectorText(lap.Sectors), recorded, StatsStore.Unix(nowUtc), lap.Trace.Encode());
        });
        return id;
    }

    public List<SharedLapInfo> SharedAt(string track, string config) =>
        _db.Query("SELECT id, driver, car, track, track_config, lap_time, recorded_utc FROM shared_laps WHERE track = ?1 AND track_config = ?2 ORDER BY lap_time", track, config)
            .Select(r => new SharedLapInfo((long)r[0]!, (string)r[1]!, (string)r[2]!, (string)r[3]!, (string)r[4]!, Convert.ToDouble(r[5]),
                DateTimeOffset.FromUnixTimeSeconds((long)r[6]!).UtcDateTime)).ToList();

    public SharedLap? LoadShared(long id)
    {
        var rows = _db.Query("SELECT driver, car, track, track_config, sectors, recorded_utc, data FROM shared_laps WHERE id = ?1", id);
        if (rows.Count == 0 || rows[0][6] is not byte[] data) return null;
        var r = rows[0];
        try
        {
            return new SharedLap((string)r[0]!, (string)r[1]!, (string)r[2]!, (string)r[3]!, StatsStore.ParseSectors(r[4] as string),
                DateTimeOffset.FromUnixTimeSeconds((long)r[5]!).UtcDateTime, LapTrace.Decode(data));
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { App.LogError(ex); return null; }
    }

    public void DeleteShared(long id)
    {
        _db.Run("DELETE FROM shared_laps WHERE id = ?1", id);
        if (_path != ":memory:") _db.Exec("PRAGMA incremental_vacuum;");
    }

    public void DeleteAll()
    {
        _db.Run("DELETE FROM traces");
        if (_path != ":memory:") _db.Exec("PRAGMA incremental_vacuum;");
    }

    public void Dispose() => _db.Dispose();
}

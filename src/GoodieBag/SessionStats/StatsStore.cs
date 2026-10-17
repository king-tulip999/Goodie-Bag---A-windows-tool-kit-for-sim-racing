using System.IO;
using System.Security.Cryptography;

namespace GoodieBag.SessionStats;

internal sealed class StatsStore : IDisposable
{
    private const int Version = 1;
    private readonly SqliteDb _db;
    private readonly Dictionary<string, LiveTarget> _liveIds = new();

    private sealed class LiveTarget
    {
        public long Id;
        public bool Continues;
        public int LapOffset, RunOffset;
        public SessionRow? Last;
    }

    public static string DefaultPath => Path.Combine(Config.ConfigStore.Folder, "stats.db");

    private StatsStore(SqliteDb db)
    {
        _db = db;
        _db.Exec("""
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS sessions(
                id INTEGER PRIMARY KEY,
                start_utc INTEGER NOT NULL, end_utc INTEGER NOT NULL,
                sim TEXT NOT NULL, driver TEXT NOT NULL, driver_id INTEGER NOT NULL, car_number TEXT NOT NULL,
                car TEXT NOT NULL, car_class TEXT NOT NULL, track TEXT NOT NULL, track_config TEXT NOT NULL, track_km REAL,
                session_type TEXT NOT NULL, weather TEXT NOT NULL, dynamic_weather INTEGER NOT NULL, category TEXT NOT NULL,
                laps INTEGER NOT NULL, incidents INTEGER, start_pos INTEGER, finish_pos INTEGER, finished INTEGER NOT NULL,
                ir_start INTEGER, sr_start REAL, ir_delta INTEGER, sr_delta REAL,
                league_id INTEGER NOT NULL, subsession INTEGER NOT NULL, session_num INTEGER NOT NULL,
                season_id INTEGER NOT NULL, race_week INTEGER, official INTEGER NOT NULL,
                drive_s REAL NOT NULL, garage_s REAL NOT NULL, longest_stint_s REAL NOT NULL, rain_s REAL NOT NULL, night_s REAL,
                track_temp_min REAL, track_temp_max REAL, distance_km REAL NOT NULL, pit_stops INTEGER NOT NULL, pit_s REAL NOT NULL,
                source TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS sessions_start ON sessions(start_utc);
            CREATE TABLE IF NOT EXISTS laps(
                session_id INTEGER NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
                lap INTEGER NOT NULL, time REAL NOT NULL, clean INTEGER NOT NULL, top_speed REAL, fuel_used REAL,
                full_throttle REAL, brake REAL, shifts INTEGER NOT NULL, peak_lat_g REAL,
                PRIMARY KEY(session_id, lap));
            CREATE TABLE IF NOT EXISTS imported(file TEXT PRIMARY KEY COLLATE NOCASE, hash TEXT NOT NULL, at INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS imported_hash ON imported(hash);
            """);
        _db.Run("INSERT OR IGNORE INTO meta(key, value) VALUES('version', ?1)", Version.ToString());
        AddColumns("laps", "sectors TEXT", "flags INTEGER", "lap_incidents INTEGER", "run INTEGER", "setup TEXT");
        AddColumns("sessions", "air_temp REAL", "wind_kph REAL", "wind_dir REAL", "humidity REAL", "pressure REAL", "air_density REAL", "skies INTEGER",
            "rain_max REAL", "wetness_max INTEGER", "rubber TEXT NOT NULL DEFAULT ''", "sim_start TEXT NOT NULL DEFAULT ''",
            "track_id INTEGER", "track_name TEXT NOT NULL DEFAULT ''", "tank_l REAL", "tyre_wear TEXT");
    }

    private void AddColumns(string table, params string[] columns)
    {
        var have = _db.Query($"PRAGMA table_info({table})").Select(r => (string?)r[1]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
            if (!have.Contains(column.Split(' ')[0])) _db.Exec($"ALTER TABLE {table} ADD COLUMN {column};");
    }

    public TraceStore? Traces { get; set; }

    public static StatsStore Open(string path)
    {
        if (path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new StatsStore(SqliteDb.Open(path));
    }

    public SqliteDb Db => _db;

    public string? Meta(string key) => _db.Scalar("SELECT value FROM meta WHERE key = ?1", key) as string;

    public void SetMeta(string key, string value) => _db.Run("INSERT OR REPLACE INTO meta(key, value) VALUES(?1, ?2)", key, value);

    internal static long Unix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    internal static DateTime FromUnix(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

    public void Save(SessionRow row, IReadOnlyList<LapRow> laps) => SaveFile(new[] { (row, laps) }, null);

    public void SaveFile(IReadOnlyList<(SessionRow Row, List<LapRow> Laps)> sessions, (string File, string Hash)? imported) =>
        SaveFile(sessions.Select(x => (x.Row, (IReadOnlyList<LapRow>)x.Laps)).ToList(), imported);

    private void SaveFile(IReadOnlyList<(SessionRow Row, IReadOnlyList<LapRow> Laps)> sessions, (string File, string Hash)? imported)
    {
        var replaced = new List<long>();
        var attach = new List<(long Id, SessionRow Row, int? Offset)>();
        _db.Transaction(() =>
        {
            foreach (var (row, laps) in sessions)
            {
                if (Match(row) is { } existing)
                {
                    if (existing.Source == "live" && row.Source != "live") { attach.Add((existing.Id, row, null)); continue; }
                    if (existing.Source == row.Source) { attach.Add((existing.Id, row, Merge(existing.Id, row, laps))); continue; }
                    _db.Run("DELETE FROM sessions WHERE id = ?1", existing.Id);
                    replaced.Add(existing.Id);
                }
                row.Id = InsertRow(row);
                InsertLaps(row.Id, laps, 0);
                LinkRatings(row);
                attach.Add((row.Id, row, 0));
            }
            if (imported is { } f) MarkImported(f.File, f.Hash);
        });
        foreach (var old in replaced) Traces?.DeleteSession(old);
        foreach (var a in attach) AttachTraces(a.Id, a.Row, a.Offset);
    }

    public bool FillMissing(SessionRow r)
    {
        if (Match(r) is not { } existing) return false;
        _db.Run("""
            UPDATE sessions SET
            air_temp = COALESCE(air_temp, ?2), wind_kph = COALESCE(wind_kph, ?3), wind_dir = COALESCE(wind_dir, ?4), humidity = COALESCE(humidity, ?5),
            pressure = COALESCE(pressure, ?6), air_density = COALESCE(air_density, ?7), skies = COALESCE(skies, ?8),
            rain_max = COALESCE(rain_max, ?9), wetness_max = COALESCE(wetness_max, ?10),
            rubber = CASE WHEN rubber = '' THEN ?11 ELSE rubber END, sim_start = CASE WHEN sim_start = '' THEN ?12 ELSE sim_start END,
            track_id = COALESCE(track_id, ?13), track_name = CASE WHEN track_name = '' THEN ?14 ELSE track_name END,
            tank_l = COALESCE(tank_l, ?15), tyre_wear = COALESCE(tyre_wear, ?16)
            WHERE id = ?1
            """, existing.Id, r.AirTemp, r.WindKph, r.WindDir, r.Humidity, r.Pressure, r.AirDensity, r.Skies, r.RainMax, r.WetnessMax,
            r.Rubber, r.SimStart, r.TrackId, r.TrackName, r.TankL, TyreWear.Text(r.Tyres));
        return true;
    }

    public (int? Id, string Name) TrackIdentity(long session) =>
        _db.Query("SELECT track_id, track_name FROM sessions WHERE id = ?1", session) is { Count: > 0 } r
            ? (r[0][0] is null ? null : Convert.ToInt32(r[0][0]), r[0][1] as string ?? "") : (null, "");

    public void AttachTracesOnly(SessionRow row)
    {
        if (row.Traces is { Count: > 0 } && Match(row) is { } existing) AttachTraces(existing.Id, row, null);
    }

    private void AttachTraces(long session, SessionRow row, int? offset)
    {
        if (Traces is not { } traces || row.Traces is not { Count: > 0 } found) return;
        var stored = _db.Query("SELECT lap, time, clean FROM laps WHERE session_id = ?1", session)
            .Select(r => (Convert.ToInt32(r[0]), Convert.ToDouble(r[1]), Convert.ToInt64(r[2]) != 0)).ToList();
        try { traces.Attach(session, row, stored, found, offset); }
        catch (Exception ex) { App.LogError(ex); }
    }

    public void SaveLive(SessionRow row, IReadOnlyList<LapRow> newLaps)
    {
        long? replaced = null;
        int lapOffset = 0;
        _db.Transaction(() =>
        {
            if (!_liveIds.TryGetValue(row.Key, out var target))
            {
                var existing = Match(row);
                if (existing is { Source: not "live" } file && SameTime(file.Id, row))
                {
                    _db.Run("DELETE FROM sessions WHERE id = ?1", file.Id);
                    replaced = file.Id;
                    existing = null;
                }
                if (existing is { } earlier)
                {
                    target = new LiveTarget
                    {
                        Id = earlier.Id, Continues = true,
                        LapOffset = Convert.ToInt32(_db.Scalar("SELECT COALESCE(MAX(lap), 0) FROM laps WHERE session_id = ?1", earlier.Id) ?? 0L),
                        RunOffset = Convert.ToInt32(_db.Scalar("SELECT COALESCE(MAX(run), 0) FROM laps WHERE session_id = ?1", earlier.Id) ?? 0L),
                    };
                    _db.Run("UPDATE sessions SET source = 'live' WHERE id = ?1", earlier.Id);
                }
                else
                {
                    row.Id = InsertRow(row);
                    target = new LiveTarget { Id = row.Id };
                    _liveIds[row.Key] = target;
                    InsertLaps(row.Id, newLaps, null);
                    LinkRatings(row);
                    return;
                }
                _liveIds[row.Key] = target;
            }
            row.Id = target.Id;
            lapOffset = target.LapOffset;
            if (!target.Continues)
            {
                UpdateRow(row);
                InsertLaps(target.Id, newLaps, null);
                return;
            }
            MergeTotals(target.Id, Since(row, target.Last), row.Laps - (target.Last?.Laps ?? 0), target.LapOffset);
            target.Last = row.Copy();
            InsertLaps(target.Id, newLaps, target.LapOffset, target.RunOffset);
        });
        if (replaced is { } old) Traces?.DeleteSession(old);
        if (row.Id > 0) AttachTraces(row.Id, row, lapOffset);
    }

    private bool SameTime(long id, SessionRow row) =>
        _db.Scalar("SELECT end_utc FROM sessions WHERE id = ?1", id) is long end && Unix(row.StartUtc) < end - 60;

    internal static SessionRow Since(SessionRow now, SessionRow? last)
    {
        var d = now.Copy();
        if (last is null) return d;
        d.DriveS -= last.DriveS;
        d.GarageS -= last.GarageS;
        d.RainS -= last.RainS;
        d.NightS = now.NightS is { } n ? n - (last.NightS ?? 0) : null;
        d.DistanceKm -= last.DistanceKm;
        d.PitStops -= last.PitStops;
        d.PitS -= last.PitS;
        d.Tyres = now.Tyres?.Skip(last.Tyres?.Count ?? 0).ToList();
        return d;
    }

    private (long Id, string Source)? Match(SessionRow row)
    {
        var found = row.SubSession > 0
            ? _db.Query("SELECT id, source FROM sessions WHERE subsession = ?1 AND session_num = ?2 AND driver_id = ?3 LIMIT 1", row.SubSession, row.SessionNum, row.DriverId)
            : _db.Query("""
                SELECT id, source FROM sessions WHERE subsession = 0 AND driver_id = ?1 AND car = ?2 AND track = ?3 AND track_config = ?4
                AND session_type = ?5 AND start_utc <= ?7 + 600 AND end_utc >= ?6 - 600 ORDER BY start_utc LIMIT 1
                """, row.DriverId, row.Car, row.Track, row.TrackConfig, row.SessionType, Unix(row.StartUtc), Unix(row.EndUtc));
        return found.Count > 0 ? ((long)found[0][0]!, (string)found[0][1]!) : null;
    }

    private const string Columns = """
        start_utc, end_utc, sim, driver, driver_id, car_number, car, car_class, track, track_config, track_km, session_type, weather,
        dynamic_weather, category, laps, incidents, start_pos, finish_pos, finished, ir_start, sr_start, ir_delta, sr_delta,
        league_id, subsession, session_num, season_id, race_week, official, drive_s, garage_s, longest_stint_s, rain_s, night_s,
        track_temp_min, track_temp_max, distance_km, pit_stops, pit_s, source,
        air_temp, wind_kph, wind_dir, humidity, pressure, air_density, skies, rain_max, wetness_max, rubber, sim_start, track_id, track_name,
        tank_l, tyre_wear
        """;

    private static object?[] Values(SessionRow r) => new object?[]
    {
        Unix(r.StartUtc), Unix(r.EndUtc), r.Sim, r.Driver, r.DriverId, r.CarNumber, r.Car, r.CarClass, r.Track, r.TrackConfig, r.TrackKm, r.SessionType, r.Weather,
        r.DynamicWeather, r.Category, r.Laps, r.Incidents, r.StartPos, r.FinishPos, r.Finished, r.IrStart, r.SrStart, r.IrDelta, r.SrDelta,
        r.LeagueId, r.SubSession, r.SessionNum, r.SeasonId, r.RaceWeek, r.Official, r.DriveS, r.GarageS, r.LongestStintS, r.RainS, r.NightS,
        r.TrackTempMin, r.TrackTempMax, r.DistanceKm, r.PitStops, r.PitS, r.Source,
        r.AirTemp, r.WindKph, r.WindDir, r.Humidity, r.Pressure, r.AirDensity, r.Skies, r.RainMax, r.WetnessMax, r.Rubber, r.SimStart, r.TrackId, r.TrackName,
        r.TankL, TyreWear.Text(r.Tyres),
    };

    private long InsertRow(SessionRow r)
    {
        var values = Values(r);
        string marks = string.Join(", ", Enumerable.Range(1, values.Length).Select(i => "?" + i));
        return _db.Insert($"INSERT INTO sessions({Columns}) VALUES({marks})", values);
    }

    private void UpdateRow(SessionRow r)
    {
        var values = Values(r);
        var names = Columns.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string set = string.Join(", ", names.Select((n, i) => $"{n} = ?{i + 1}"));
        _db.Run($"UPDATE sessions SET {set} WHERE id = ?{values.Length + 1}", values.Append(r.Id).ToArray());
    }

    private void InsertLaps(long session, IReadOnlyList<LapRow> laps, int? offset, int? runOffset = null)
    {
        foreach (var l in laps)
            _db.Run("INSERT OR REPLACE INTO laps(session_id, lap, time, clean, top_speed, fuel_used, full_throttle, brake, shifts, peak_lat_g, sectors, flags, lap_incidents, run, setup) VALUES(?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15)",
                session, l.Lap + (offset ?? 0), l.Time, l.Clean, l.TopSpeed, l.FuelUsed, l.FullThrottle, l.Brake, l.Shifts, l.PeakLatG, SectorText(l.Sectors),
                (int)l.Flags, l.Incidents, l.Run is { } run ? run + (runOffset ?? 0) : null, l.Setup);
    }

    internal static string? SectorText(IReadOnlyList<double>? sectors) =>
        sectors is { Count: > 0 } ? string.Join(",", sectors.Select(s => s.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))) : null;

    internal static List<double>? ParseSectors(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var list = new List<double>();
        foreach (var part in text.Split(','))
        {
            if (!double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) return null;
            list.Add(v);
        }
        return list;
    }

    private int Merge(long id, SessionRow r, IReadOnlyList<LapRow> laps)
    {
        int offset = Convert.ToInt32(_db.Scalar("SELECT COALESCE(MAX(lap), 0) FROM laps WHERE session_id = ?1", id) ?? 0L);
        int runs = Convert.ToInt32(_db.Scalar("SELECT COALESCE(MAX(run), 0) FROM laps WHERE session_id = ?1", id) ?? 0L);
        MergeTotals(id, r, laps.Count, offset);
        InsertLaps(id, laps, offset, runs);
        return offset;
    }

    private void MergeTotals(long id, SessionRow r, int lapCount, int lapOffset = 0)
    {
        _db.Run("""
            UPDATE sessions SET start_utc = MIN(start_utc, ?2), end_utc = MAX(end_utc, ?3), laps = laps + ?4, incidents = MAX(COALESCE(incidents, 0), COALESCE(?5, 0)),
            start_pos = COALESCE(start_pos, ?6), finish_pos = COALESCE(?7, finish_pos), finished = MAX(finished, ?8),
            drive_s = drive_s + ?9, garage_s = garage_s + ?10, longest_stint_s = MAX(longest_stint_s, ?11), rain_s = rain_s + ?12,
            night_s = CASE WHEN night_s IS NULL AND ?13 IS NULL THEN NULL ELSE COALESCE(night_s, 0) + COALESCE(?13, 0) END,
            track_temp_min = MIN(COALESCE(track_temp_min, ?14), COALESCE(?14, track_temp_min)),
            track_temp_max = MAX(COALESCE(track_temp_max, ?15), COALESCE(?15, track_temp_max)),
            distance_km = distance_km + ?16, pit_stops = pit_stops + ?17, pit_s = pit_s + ?18,
            air_temp = COALESCE(air_temp, ?19), wind_kph = COALESCE(wind_kph, ?20), wind_dir = COALESCE(wind_dir, ?21), humidity = COALESCE(humidity, ?22),
            pressure = COALESCE(pressure, ?23), air_density = COALESCE(air_density, ?24), skies = COALESCE(skies, ?25),
            rain_max = MAX(COALESCE(rain_max, ?26), COALESCE(?26, rain_max)), wetness_max = MAX(COALESCE(wetness_max, ?27), COALESCE(?27, wetness_max)),
            rubber = CASE WHEN rubber = '' THEN ?28 ELSE rubber END, sim_start = CASE WHEN sim_start = '' THEN ?29 ELSE sim_start END,
            track_id = COALESCE(track_id, ?30), track_name = CASE WHEN track_name = '' THEN ?31 ELSE track_name END,
            tank_l = COALESCE(tank_l, ?32),
            tyre_wear = CASE WHEN ?33 IS NULL THEN tyre_wear WHEN tyre_wear IS NULL THEN ?33 ELSE tyre_wear || ';' || ?33 END
            WHERE id = ?1
            """, id, Unix(r.StartUtc), Unix(r.EndUtc), lapCount, r.Incidents, r.StartPos, r.FinishPos, r.Finished,
            r.DriveS, r.GarageS, r.LongestStintS, r.RainS, r.NightS, r.TrackTempMin, r.TrackTempMax, r.DistanceKm, r.PitStops, r.PitS,
            r.AirTemp, r.WindKph, r.WindDir, r.Humidity, r.Pressure, r.AirDensity, r.Skies, r.RainMax, r.WetnessMax, r.Rubber, r.SimStart,
            r.TrackId, r.TrackName, r.TankL, TyreWear.Text(r.Tyres, lapOffset));
    }

    private void LinkRatings(SessionRow r)
    {
        if (r.IrStart is null && r.SrStart is null) return;
        const string Pick = "SELECT id, session_type, ir_start, sr_start FROM sessions WHERE driver_id = ?1 AND category = ?2 AND id <> ?3 AND (ir_start IS NOT NULL OR sr_start IS NOT NULL)";
        var before = _db.Query(Pick + " AND start_utc < ?4 ORDER BY start_utc DESC LIMIT 1", r.DriverId, r.Category, r.Id, Unix(r.StartUtc));
        if (before.Count > 0 && (string?)before[0][1] == "Race")
            SetDelta((long)before[0][0]!, before[0][2], before[0][3], r.IrStart, r.SrStart);
        if (r.SessionType != "Race") return;
        var after = _db.Query(Pick + " AND start_utc > ?4 ORDER BY start_utc LIMIT 1", r.DriverId, r.Category, r.Id, Unix(r.StartUtc));
        if (after.Count > 0) SetDelta(r.Id, r.IrStart, r.SrStart, after[0][2], after[0][3]);
    }

    private void SetDelta(long id, object? irFrom, object? srFrom, object? irTo, object? srTo)
    {
        long? ir = irFrom is null || irTo is null ? null : Convert.ToInt64(irTo) - Convert.ToInt64(irFrom);
        double? sr = srFrom is null || srTo is null ? null : Math.Round(Convert.ToDouble(srTo) - Convert.ToDouble(srFrom), 2);
        _db.Run("UPDATE sessions SET ir_delta = COALESCE(?2, ir_delta), sr_delta = COALESCE(?3, sr_delta) WHERE id = ?1", id, ir, sr);
    }

    public static string HashOf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var head = new byte[Math.Min(65536, stream.Length)];
        int read = 0;
        while (read < head.Length && stream.Read(head, read, head.Length - read) is var n and > 0) read += n;
        using var sha = SHA256.Create();
        sha.TransformBlock(BitConverter.GetBytes(stream.Length), 0, 8, null, 0);
        sha.TransformFinalBlock(head, 0, read);
        return Convert.ToHexString(sha.Hash!);
    }

    public bool Imported(string file) => _db.Scalar("SELECT 1 FROM imported WHERE file = ?1", file) is not null;

    public bool HashImported(string hash) => _db.Scalar("SELECT 1 FROM imported WHERE hash = ?1", hash) is not null;

    public void MarkImported(string file, string hash) =>
        _db.Run("INSERT OR REPLACE INTO imported(file, hash, at) VALUES(?1, ?2, ?3)", file, hash, Unix(DateTime.UtcNow));

    public bool Empty => _db.Scalar("SELECT 1 FROM sessions LIMIT 1") is null;

    public string DriverOf(long session) => _db.Scalar("SELECT driver FROM sessions WHERE id = ?1", session) as string ?? "";

    public string? LatestDriver() => _db.Scalar("SELECT driver FROM sessions WHERE driver <> '' ORDER BY start_utc DESC LIMIT 1") as string;

    public string? MostUsedNumber(IReadOnlyCollection<int> leagueIds)
    {
        if (leagueIds.Count > 0 && _db.Scalar($"SELECT car_number FROM sessions WHERE car_number <> '' AND league_id IN ({string.Join(",", leagueIds)}) GROUP BY car_number ORDER BY COUNT(*) DESC, MAX(start_utc) DESC LIMIT 1") is string league)
            return league;
        return _db.Scalar("SELECT car_number FROM sessions WHERE car_number <> '' GROUP BY car_number ORDER BY COUNT(*) DESC, MAX(start_utc) DESC LIMIT 1") as string;
    }

    public (DateTime Start, int Week)? LatestOfficialWeek() =>
        _db.Query("SELECT start_utc, race_week FROM sessions WHERE official = 1 AND race_week IS NOT NULL ORDER BY start_utc DESC LIMIT 1") is { Count: > 0 } r
            ? (FromUnix((long)r[0][0]!), Convert.ToInt32(r[0][1])) : null;

    public List<(int Id, int Sessions)> LeaguesSeen() =>
        _db.Query("SELECT league_id, COUNT(*) FROM sessions WHERE league_id > 0 GROUP BY league_id ORDER BY 2 DESC")
            .Select(r => (Convert.ToInt32(r[0]), Convert.ToInt32(r[1]))).ToList();

    public List<SessionSummary> Sessions(long from, long to) =>
        _db.Query(SummarySql + " WHERE s.start_utc >= ?1 AND s.start_utc < ?2 ORDER BY s.start_utc DESC", from, to).Select(Summary).ToList();

    public SessionSummary? Session(long id) => _db.Query(SummarySql + " WHERE s.id = ?1", id) is { Count: > 0 } r ? Summary(r[0]) : null;

    public List<LapRow> Laps(long session) =>
        _db.Query("SELECT lap, time, clean, top_speed, fuel_used, full_throttle, brake, shifts, peak_lat_g, sectors, flags, lap_incidents, run, setup FROM laps WHERE session_id = ?1 ORDER BY lap", session)
            .Select(r => new LapRow(Convert.ToInt32(r[0]), Convert.ToDouble(r[1]), Convert.ToInt64(r[2]) != 0, D(r[3]), D(r[4]), D(r[5]), D(r[6]), Convert.ToInt32(r[7]), D(r[8]),
                ParseSectors(r[9] as string), (LapFlags)(I(r[10]) ?? 0), I(r[11]), I(r[12]), r[13] as string)).ToList();

    public SessionConditions? Conditions(long session)
    {
        var q = _db.Query("""
            SELECT air_temp, track_temp_min, track_temp_max, wind_kph, wind_dir, humidity, pressure, air_density, skies, rain_max, wetness_max,
            rubber, sim_start, dynamic_weather FROM sessions WHERE id = ?1
            """, session);
        if (q.Count == 0) return null;
        var r = q[0];
        return new SessionConditions(D(r[0]), D(r[1]), D(r[2]), D(r[3]), D(r[4]), D(r[5]), D(r[6]), D(r[7]), I(r[8]), D(r[9]), I(r[10]),
            r[11] as string ?? "", r[12] as string ?? "", Convert.ToInt64(r[13]) != 0);
    }

    public (double? Tank, List<TyreReading> Tyres) CarExtras(long session) =>
        _db.Query("SELECT tank_l, tyre_wear FROM sessions WHERE id = ?1", session) is { Count: > 0 } r
            ? (D(r[0][0]), TyreWear.NewSets(TyreWear.Parse(r[0][1] as string))) : (null, new List<TyreReading>());

    public double? BestLap(string car, string track, string config) =>
        D(_db.Scalar("SELECT MIN(l.time) FROM laps l JOIN sessions s ON s.id = l.session_id WHERE l.clean = 1 AND s.car = ?1 AND s.track = ?2 AND s.track_config = ?3", car, track, config));

    private static double? D(object? v) => v is null ? null : Convert.ToDouble(v);
    private static int? I(object? v) => v is null ? null : Convert.ToInt32(v);

    private const string SummarySql = """
        SELECT s.id, s.start_utc, s.session_type, s.car, s.car_class, s.track, s.track_config, s.laps,
        (SELECT MIN(time) FROM laps WHERE session_id = s.id AND clean = 1), s.incidents, s.start_pos, s.finish_pos, s.finished,
        s.weather, s.track_km, s.ir_delta, s.sr_delta, s.drive_s, s.official, s.league_id, s.source
        FROM sessions s
        """;

    private static SessionSummary Summary(object?[] r) => new(
        (long)r[0]!, FromUnix((long)r[1]!), (string)r[2]!, (string)r[3]!, (string)r[4]!, (string)r[5]!, (string)r[6]!, Convert.ToInt32(r[7]), D(r[8]),
        I(r[9]), I(r[10]), I(r[11]), Convert.ToInt64(r[12]) != 0, (string)r[13]!, D(r[14]), I(r[15]), D(r[16]), Convert.ToDouble(r[17]), Convert.ToInt64(r[18]) != 0,
        Convert.ToInt32(r[19]), (string)r[20]!);

    public void Dispose() => _db.Dispose();
}

internal sealed record SessionConditions(double? AirTemp, double? TrackTempMin, double? TrackTempMax, double? WindKph, double? WindDir, double? Humidity,
    double? Pressure, double? AirDensity, int? Skies, double? RainMax, int? WetnessMax, string Rubber, string SimStart, bool Dynamic);

internal sealed record SessionSummary(long Id, DateTime StartUtc, string Type, string Car, string CarClass, string Track, string Config, int Laps, double? Best,
    int? Incidents, int? StartPos, int? FinishPos, bool Finished, string Weather, double? TrackKm, int? IrDelta, double? SrDelta, double DriveS, bool Official,
    int LeagueId, string Source)
{
    public string Place => Config.Length > 0 ? $"{Track} · {Config}" : Track;
}

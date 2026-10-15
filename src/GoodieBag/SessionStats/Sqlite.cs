using System.Runtime.InteropServices;

namespace GoodieBag.SessionStats;

internal sealed class SqliteDb : IDisposable
{
    private const int Ok = 0, Row = 100, Done = 101;
    private const int OpenReadWrite = 0x2, OpenCreate = 0x4, OpenFullMutex = 0x10000;
    private static readonly IntPtr Transient = new(-1);
    private IntPtr _db;

    public object Gate { get; } = new();

    private SqliteDb(IntPtr db) => _db = db;

    public static SqliteDb Open(string path)
    {
        int rc = sqlite3_open_v2(path, out var db, OpenReadWrite | OpenCreate | OpenFullMutex, null);
        if (rc != Ok)
        {
            string message = db != IntPtr.Zero ? Marshal.PtrToStringUni(sqlite3_errmsg16(db)) ?? "" : "";
            if (db != IntPtr.Zero) sqlite3_close_v2(db);
            throw new InvalidOperationException($"Couldn't open the stats database ({rc}): {message}");
        }
        sqlite3_busy_timeout(db, 3000);
        var conn = new SqliteDb(db);
        conn.Exec("PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;");
        return conn;
    }

    public void Exec(string sql)
    {
        lock (Gate)
        {
            if (sqlite3_exec(_db, sql, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != Ok) throw Error(sql);
        }
    }

    public int Run(string sql, params object?[] args)
    {
        lock (Gate)
        {
            var stmt = Prepare(sql, args);
            try
            {
                if (sqlite3_step(stmt) is not (Done or Row)) throw Error(sql);
                return sqlite3_changes(_db);
            }
            finally { sqlite3_finalize(stmt); }
        }
    }

    public long Insert(string sql, params object?[] args)
    {
        lock (Gate)
        {
            Run(sql, args);
            return sqlite3_last_insert_rowid(_db);
        }
    }

    public List<object?[]> Query(string sql, params object?[] args)
    {
        lock (Gate)
        {
            var rows = new List<object?[]>();
            var stmt = Prepare(sql, args);
            try
            {
                int columns = sqlite3_column_count(stmt);
                while (true)
                {
                    int rc = sqlite3_step(stmt);
                    if (rc == Done) break;
                    if (rc != Row) throw Error(sql);
                    var row = new object?[columns];
                    for (int i = 0; i < columns; i++)
                        row[i] = sqlite3_column_type(stmt, i) switch
                        {
                            1 => sqlite3_column_int64(stmt, i),
                            2 => sqlite3_column_double(stmt, i),
                            3 => Marshal.PtrToStringUni(sqlite3_column_text16(stmt, i)),
                            4 => Blob(stmt, i),
                            _ => null,
                        };
                    rows.Add(row);
                }
                return rows;
            }
            finally { sqlite3_finalize(stmt); }
        }
    }

    private static byte[] Blob(IntPtr stmt, int i)
    {
        var at = sqlite3_column_blob(stmt, i);
        var bytes = new byte[sqlite3_column_bytes(stmt, i)];
        if (bytes.Length > 0) Marshal.Copy(at, bytes, 0, bytes.Length);
        return bytes;
    }

    public object? Scalar(string sql, params object?[] args) => Query(sql, args) is { Count: > 0 } rows && rows[0].Length > 0 ? rows[0][0] : null;

    public void Transaction(Action work)
    {
        lock (Gate)
        {
            Exec("BEGIN");
            try
            {
                work();
                Exec("COMMIT");
            }
            catch
            {
                try { Exec("ROLLBACK"); } catch { }
                throw;
            }
        }
    }

    private IntPtr Prepare(string sql, object?[] args)
    {
        if (_db == IntPtr.Zero) throw new ObjectDisposedException(nameof(SqliteDb));
        if (sqlite3_prepare16_v2(_db, sql, -1, out var stmt, IntPtr.Zero) != Ok) throw Error(sql);
        for (int i = 0; i < args.Length; i++)
        {
            int at = i + 1, rc = args[i] switch
            {
                null => sqlite3_bind_null(stmt, at),
                bool b => sqlite3_bind_int64(stmt, at, b ? 1 : 0),
                int n => sqlite3_bind_int64(stmt, at, n),
                long n => sqlite3_bind_int64(stmt, at, n),
                double d => double.IsFinite(d) ? sqlite3_bind_double(stmt, at, d) : sqlite3_bind_null(stmt, at),
                float f => float.IsFinite(f) ? sqlite3_bind_double(stmt, at, f) : sqlite3_bind_null(stmt, at),
                string s => sqlite3_bind_text16(stmt, at, s, -1, Transient),
                byte[] b => sqlite3_bind_blob(stmt, at, b, b.Length, Transient),
                _ => sqlite3_bind_text16(stmt, at, Convert.ToString(args[i], System.Globalization.CultureInfo.InvariantCulture) ?? "", -1, Transient),
            };
            if (rc != Ok) { sqlite3_finalize(stmt); throw Error(sql); }
        }
        return stmt;
    }

    private InvalidOperationException Error(string sql) =>
        new($"Stats database: {Marshal.PtrToStringUni(sqlite3_errmsg16(_db))} ({sql.Split('\n')[0].Trim()})");

    public void Dispose()
    {
        lock (Gate)
        {
            if (_db == IntPtr.Zero) return;
            sqlite3_close_v2(_db);
            _db = IntPtr.Zero;
        }
    }

    private const string Lib = "winsqlite3.dll";

    [DllImport(Lib, ExactSpelling = true)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, [MarshalAs(UnmanagedType.LPUTF8Str)] string? vfs);

    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);

    [DllImport(Lib, ExactSpelling = true)]
    private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, IntPtr errmsg);

    [DllImport(Lib, ExactSpelling = true)]
    private static extern int sqlite3_prepare16_v2(IntPtr db, [MarshalAs(UnmanagedType.LPWStr)] string sql, int bytes, out IntPtr stmt, IntPtr tail);

    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_step(IntPtr stmt);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_changes(IntPtr db);
    [DllImport(Lib, ExactSpelling = true)] private static extern long sqlite3_last_insert_rowid(IntPtr db);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_bind_null(IntPtr stmt, int index);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);

    [DllImport(Lib, ExactSpelling = true)]
    private static extern int sqlite3_bind_text16(IntPtr stmt, int index, [MarshalAs(UnmanagedType.LPWStr)] string value, int bytes, IntPtr destructor);

    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_bind_blob(IntPtr stmt, int index, byte[] value, int bytes, IntPtr destructor);
    [DllImport(Lib, ExactSpelling = true)] private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int index);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_column_bytes(IntPtr stmt, int index);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport(Lib, ExactSpelling = true)] private static extern int sqlite3_column_type(IntPtr stmt, int index);
    [DllImport(Lib, ExactSpelling = true)] private static extern long sqlite3_column_int64(IntPtr stmt, int index);
    [DllImport(Lib, ExactSpelling = true)] private static extern double sqlite3_column_double(IntPtr stmt, int index);
    [DllImport(Lib, ExactSpelling = true)] private static extern IntPtr sqlite3_column_text16(IntPtr stmt, int index);
    [DllImport(Lib, ExactSpelling = true)] private static extern IntPtr sqlite3_errmsg16(IntPtr db);
}

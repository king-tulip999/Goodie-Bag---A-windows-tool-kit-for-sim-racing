using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using GoodieBag.Config;

namespace GoodieBag.SimRacing.IracingFiles;

internal static class IracingPaths
{
    public const string App = "app.ini";
    public const string Core = "core.ini";
    public const string RendererTemplate = "rendererDX11.ini";
    public const string MonitorRenderer = "rendererDX11Monitor.ini";
    public const string SimProcess = "iRacingSim64DX11";

    private static readonly Lazy<string> Backups = new(FindBackupFolder);

    public static string BackupFolder => Backups.Value;

    private static string FindBackupFolder()
    {
        string home = Path.Combine(ConfigStore.Folder, "iRacing");
        string folder = Path.Combine(home, "Backups");
        if (Directory.Exists(folder)) return folder;
        try
        {
            var earlier = Directory.Exists(ConfigStore.Folder)
                ? Directory.GetDirectories(ConfigStore.Folder).FirstOrDefault(d =>
                    !string.Equals(Path.GetFileName(d), "iRacing", StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(Path.Combine(d, "Backups"))
                    && Directory.GetDirectories(Path.Combine(d, "Backups")).Any(b => File.Exists(Path.Combine(b, "backup.txt"))))
                : null;
            if (earlier is not null && !Directory.Exists(home)) Directory.Move(earlier, home);
        }
        catch { }
        return folder;
    }

    public static readonly IReadOnlyList<(string File, string Label)> RendererModes = new[]
    {
        (MonitorRenderer, "Monitor"), ("rendererDX11OpenXR.ini", "OpenXR"), ("rendererDX11Oculus.ini", "Oculus"), ("rendererDX11OpenVR.ini", "OpenVR"),
    };

    private static readonly string[] KeptFiles = { "controls.cfg", "joyCalib.yaml", "camera.ini" };

    public static readonly IReadOnlyList<string> CopyableFiles =
        RendererModes.Select(m => m.File).Concat(new[] { RendererTemplate, App, Core }).Concat(KeptFiles).ToList();

    internal static string? DocumentsForTests { get; set; }

    public static string? FindDocumentsFolder()
    {
        var documents = DocumentsForTests ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents)) return null;
        var folder = Path.Combine(documents, "iRacing");
        return Directory.Exists(folder) ? folder : null;
    }

    public static string? FindInstallFolder()
    {
        foreach (var root in new[] { @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root);
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var app = key.OpenSubKey(name);
                    if (app is not null && app.GetValue("DisplayName") is string display && display.StartsWith("iRacing.com Race Simulation", StringComparison.OrdinalIgnoreCase)
                        && app.GetValue("InstallLocation") is string location && Directory.Exists(location))
                        return location;
                }
            }
            catch { }
        }
        return null;
    }

    public static IReadOnlyList<string> BackupFiles(string folder)
    {
        var files = RendererModes.Select(m => m.File).Concat(new[] { App, Core, RendererTemplate }).Concat(KeptFiles)
            .Select(n => Path.Combine(folder, n)).Where(File.Exists).ToList();
        var profiles = Path.Combine(folder, "profiles");
        if (Directory.Exists(profiles))
            files.AddRange(Directory.GetFiles(profiles, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        return files;
    }

    public static bool IsInOneDrive(string? path) =>
        path is not null && path.Contains(Path.DirectorySeparatorChar + "OneDrive", StringComparison.OrdinalIgnoreCase);
}

internal enum BackupKind { Auto, Manual, Imported }

internal sealed class Backup
{
    public string Folder { get; init; } = "";
    public int Number { get; set; }
    public DateTime Created { get; set; }
    public string Reason { get; set; } = "";
    public string? Name { get; set; }
    public BackupKind Kind { get; set; }
    public string? Base { get; set; }
    public string? Source { get; set; }
    public List<string> Files { get; } = new();

    public string Title => Name ?? Reason;
    public string CopyOf(string original) => Path.Combine(Folder, ShortName(original));
    public string ShortName(string original) => BackupStore.Relative(original, Base) ?? Path.GetFileName(original);
}

internal enum FileState { Same, Changed, MissingNow }

internal sealed record KeyDiff(string Section, string Key, string? Then, string? Now);

internal sealed record FileDiff(string Original, string Name, FileState State, IReadOnlyList<KeyDiff> Keys);

internal sealed class BackupStore
{
    private const string Manifest = "backup.txt";
    private const int KeepAuto = 30;

    public BackupStore(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public Backup Create(string reason, IEnumerable<string> files, string? baseFolder, BackupKind kind = BackupKind.Auto, string? name = null)
    {
        var b = CreateCore(reason, files.Select(f => (f, f)), DateTime.Now, baseFolder, kind, name, null);
        if (kind == BackupKind.Auto) Prune();
        return b;
    }

    public Backup Import(StrayCopy copy, string baseFolder) =>
        CreateCore("Imported from " + copy.FileName, new[] { (copy.Path, Path.Combine(baseFolder, copy.OriginalName)) }, copy.Modified,
            baseFolder, BackupKind.Imported, null, Signature(copy.Path, copy.Modified));

    public HashSet<string> ImportedSources() =>
        List().Where(b => b.Source is not null).Select(b => b.Source!).ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static string Signature(string path, DateTime modified) =>
        path + "|" + modified.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);

    private Backup CreateCore(string reason, IEnumerable<(string From, string Original)> files, DateTime when, string? baseFolder, BackupKind kind, string? name, string? source)
    {
        int number = List().Select(b => b.Number).DefaultIfEmpty(0).Max() + 1;
        Directory.CreateDirectory(Root);
        var id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var folder = Path.Combine(Root, id);
        for (int n = 2; Directory.Exists(folder); n++) folder = Path.Combine(Root, id + "-" + n);
        Directory.CreateDirectory(folder);

        try
        {
            var copied = new List<string>();
            foreach (var (from, original) in files.GroupBy(f => f.Original, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                if (!File.Exists(from)) continue;
                var target = Path.Combine(folder, Relative(original, baseFolder) ?? Path.GetFileName(original));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(from, target, overwrite: false);
                copied.Add(original);
            }

            var sb = new StringBuilder();
            void Line(string k, string? v) { if (v is not null) sb.Append(k).Append('=').Append(v.Replace('\r', ' ').Replace('\n', ' ')).Append("\r\n"); }
            Line("number", number.ToString(CultureInfo.InvariantCulture));
            Line("created", when.ToString("o", CultureInfo.InvariantCulture));
            Line("kind", kind.ToString().ToLowerInvariant());
            Line("reason", reason);
            Line("name", string.IsNullOrWhiteSpace(name) ? null : name.Trim());
            Line("base", baseFolder);
            Line("source", source);
            foreach (var f in copied) Line("file", f);
            string manifest = Path.Combine(folder, Manifest);
            File.WriteAllText(manifest + ".tmp", sb.ToString(), new UTF8Encoding(false));
            File.Move(manifest + ".tmp", manifest);
        }
        catch
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
            throw;
        }
        return Read(folder);
    }

    public IReadOnlyList<Backup> List()
    {
        if (!Directory.Exists(Root)) return Array.Empty<Backup>();
        return Directory.GetDirectories(Root).Where(d => File.Exists(Path.Combine(d, Manifest))).Select(TryRead).OfType<Backup>()
            .OrderByDescending(b => b.Created).ThenByDescending(b => b.Number).ToList();
    }

    public Backup Restore(Backup backup, string? folder = null)
    {
        var files = backup.Files.Select(f => (Copy: backup.CopyOf(f), Target: Moved(f, backup.Base, folder))).Where(f => File.Exists(f.Copy)).ToList();
        if (files.Count == 0) throw new FileNotFoundException("This backup's files are missing, so nothing was restored.");
        if (files.FirstOrDefault(f => File.Exists(f.Target) && new FileInfo(f.Target).IsReadOnly) is { Target: { } locked })
            throw new UnauthorizedAccessException($"{Path.GetFileName(locked)} is read-only, so nothing was restored.");
        var restore = files.Select(f => (f.Target, File.ReadAllBytes(f.Copy))).ToList();
        var before = CreateCore("Before restoring backup #" + backup.Number, files.Select(f => (f.Target, f.Target)).ToList(), DateTime.Now,
            folder ?? backup.Base, BackupKind.Auto, null, null);
        foreach (var (path, _) in restore) Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SafeFileWriter.WriteAllAtomic(restore, "restored");
        Prune(keep: backup.Folder);
        return before;
    }

    internal static string Moved(string file, string? then, string? now)
    {
        if (string.IsNullOrEmpty(now) || string.IsNullOrEmpty(then)
            || string.Equals(then.TrimEnd('\\', '/'), now.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return file;
        return Relative(file, then) is { } rel ? Path.Combine(now, rel) : file;
    }

    public static void Delete(Backup backup) => Directory.Delete(backup.Folder, recursive: true);

    private void Prune(string? keep = null)
    {
        foreach (var old in List().Where(b => b.Kind == BackupKind.Auto).Skip(KeepAuto))
        {
            if (string.Equals(old.Folder, keep, StringComparison.OrdinalIgnoreCase)) continue;
            try { Delete(old); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static IReadOnlyList<FileDiff> Compare(Backup backup, string? folder = null)
    {
        var result = new List<FileDiff>();
        foreach (var file in backup.Files)
        {
            var copy = backup.CopyOf(file);
            var original = Moved(file, backup.Base, folder);
            if (!File.Exists(copy)) continue;
            string name = backup.ShortName(file);
            if (!File.Exists(original))
            {
                result.Add(new FileDiff(original, name, FileState.MissingNow, Array.Empty<KeyDiff>()));
                continue;
            }
            var then = File.ReadAllBytes(copy);
            var now = File.ReadAllBytes(original);
            if (then.AsSpan().SequenceEqual(now))
                result.Add(new FileDiff(original, name, FileState.Same, Array.Empty<KeyDiff>()));
            else if (original.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            {
                var keys = DiffIni(IniDocument.Parse(then), IniDocument.Parse(now));
                result.Add(new FileDiff(original, name, keys.Count == 0 ? FileState.Same : FileState.Changed, keys));
            }
            else
                result.Add(new FileDiff(original, name, FileState.Changed, Array.Empty<KeyDiff>()));
        }
        return result;
    }

    private static IReadOnlyList<KeyDiff> DiffIni(IniDocument then, IniDocument now)
    {
        static Dictionary<string, IniLine> Map(IniDocument doc) =>
            doc.Entries.GroupBy(e => e.Section + "|" + e.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var a = Map(then);
        var b = Map(now);
        var diffs = new List<KeyDiff>();
        foreach (var (id, line) in a)
        {
            b.TryGetValue(id, out var cur);
            if (cur is not null && Setting.SameValue(line.Value, cur.Value)) continue;
            diffs.Add(new KeyDiff(line.Section ?? "", line.Key ?? "", line.DisplayValue, cur?.DisplayValue));
        }
        foreach (var (_, line) in b.Where(kv => !a.ContainsKey(kv.Key)))
            diffs.Add(new KeyDiff(line.Section ?? "", line.Key ?? "", null, line.DisplayValue));
        return diffs;
    }

    internal static string? Relative(string path, string? folder)
    {
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(path)) return null;
        var f = folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return path.StartsWith(f, StringComparison.OrdinalIgnoreCase) ? path[f.Length..] : null;
    }

    private static Backup? TryRead(string folder)
    {
        try { return Read(folder); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static Backup Read(string folder)
    {
        var b = new Backup { Folder = folder, Created = Directory.GetCreationTime(folder) };
        foreach (var line in File.ReadAllLines(Path.Combine(folder, Manifest), Encoding.UTF8))
        {
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            var v = line[(eq + 1)..];
            switch (line[..eq])
            {
                case "created" when DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var c): b.Created = c; break;
                case "number" when int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n): b.Number = n; break;
                case "kind": b.Kind = v == "manual" ? BackupKind.Manual : v == "imported" ? BackupKind.Imported : BackupKind.Auto; break;
                case "reason": b.Reason = v; break;
                case "name": b.Name = v; break;
                case "base": b.Base = v; break;
                case "source": b.Source = v; break;
                case "file": b.Files.Add(v); break;
            }
        }
        return b;
    }
}

internal sealed record StrayCopy(string Path, string OriginalName, DateTime Modified, long Bytes)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public static IReadOnlyList<StrayCopy> Find(string folder, ISet<string> imported)
    {
        if (!Directory.Exists(folder)) return Array.Empty<StrayCopy>();
        return new DirectoryInfo(folder).GetFiles()
            .Select(f => (File: f, Original: OriginalOf(f.Name)))
            .Where(x => x.Original is not null && !imported.Contains(BackupStore.Signature(x.File.FullName, x.File.LastWriteTime)))
            .Select(x => new StrayCopy(x.File.FullName, x.Original!, x.File.LastWriteTime, x.File.Length))
            .OrderBy(c => c.Modified).ToList();
    }

    private static string? OriginalOf(string name)
    {
        foreach (var original in IracingPaths.CopyableFiles)
        {
            var stem = Regex.Escape(System.IO.Path.GetFileNameWithoutExtension(original));
            var ext = Regex.Escape(System.IO.Path.GetExtension(original));
            var full = Regex.Escape(original);
            var pattern =
                $"^(?:{stem}(?: - Copy(?: \\(\\d+\\))?| \\(\\d+\\)|_backup|_old|_bak){ext}" +
                $"|{full}(?:\\d+|\\.bak|\\.old|\\.backup)" +
                $"|{stem}\\.(?:bak|old))$";
            if (Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return original;
        }
        return null;
    }
}

internal sealed record TelemetryFolder(string Folder, IReadOnlyList<FileInfo> Files)
{
    public long TotalBytes => Files.Sum(f => f.Length);

    public static TelemetryFolder Scan(string iracingFolder)
    {
        var folder = System.IO.Path.Combine(iracingFolder, "telemetry");
        if (!Directory.Exists(folder)) return new TelemetryFolder(folder, Array.Empty<FileInfo>());
        return new TelemetryFolder(folder, new DirectoryInfo(folder).EnumerateFiles("*.ibt").OrderByDescending(f => f.LastWriteTime).ToList());
    }

    public List<FileInfo> OlderThan(int days) => Files.Skip(1).Where(f => (DateTime.Now - f.LastWriteTime).TotalDays > days).ToList();
}

internal static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

    public static int Send(IReadOnlyList<string> paths)
    {
        static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);
        var existing = paths.Where(Exists).ToList();
        if (existing.Count == 0) return 0;
        var op = new SHFILEOPSTRUCT
        {
            hwnd = GetActiveWindow(),
            wFunc = FO_DELETE,
            pFrom = string.Join("\0", existing) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
        };
        SHFileOperation(ref op);
        return existing.Count(p => !Exists(p));
    }
}

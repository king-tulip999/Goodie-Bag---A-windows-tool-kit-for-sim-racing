using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using GoodieBag.Config;
using GoodieBag.SimRacing.IracingFiles;

namespace GoodieBag.SimRacing.Setups;

internal sealed class DownloadItem
{
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileName(Path);
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public List<(string Entry, long Size)> Entries { get; init; } = new();
    public bool IsPack => Entries.Count > 0;
    public List<string> PackCars { get; init; } = new();
    public string? Car { get; set; }
    public string IgnoreKey => Path + "|" + Modified.Ticks;
}

internal sealed class SetupStore : IDisposable
{
    public static readonly TimeSpan NewFor = TimeSpan.FromDays(3);

    public const int InboxDays = 30;
    private static readonly TimeSpan InboxFor = TimeSpan.FromDays(InboxDays);

    private const long MaxPackBytes = 50L * 1024 * 1024;

    private readonly SetupManagerSettings _settings;
    private readonly bool _demo;
    private readonly DispatcherTimer _settle;
    private FileSystemWatcher? _setupsWatch, _downloadsWatch;
    private DateTime _quietUntil;

    public SetupStore(SetupManagerSettings settings, bool demo)
    {
        _settings = settings;
        _demo = demo;
        _settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _settle.Tick += async (_, _) =>
        {
            _settle.Stop();
            if (await LoadAsync()) Changed?.Invoke();
        };
    }

    public event Action? Changed;

    public bool Demo => _demo;
    public string? Root { get; private set; }
    public string? Downloads { get; private set; }
    public List<SetupCar> Cars { get; } = new();
    public List<DownloadItem> Inbox { get; } = new();

    public DateTime NewSince => DateTime.Now - NewFor;

    public SetupCar? Car(string? dir) => dir is null ? null : Cars.FirstOrDefault(c => string.Equals(c.Dir, dir, StringComparison.OrdinalIgnoreCase));

    public SetupNode? Find(string key)
    {
        string dir = key.Split('/')[0];
        return Car(dir)?.Root.Find(key);
    }

    public string PathOf(SetupNode node) =>
        node.Segments().Aggregate(System.IO.Path.Combine(Root ?? @"C:\Users\You\Documents\iRacing\setups", node.Car.Dir), System.IO.Path.Combine);

    private sealed record Snapshot(string? Root, string? Downloads, List<SetupCar> Cars, List<DownloadItem> Inbox, long Signature);

    private long? _signature;
    private int _reads;
    private bool _demoLoaded;

    public async Task<bool> LoadAsync()
    {
        if (_demo)
        {
            if (_demoLoaded) return false;
            LoadDemo();
            return true;
        }
        int read = ++_reads;
        string? downloads = Downloads;
        var snapshot = await Task.Run(() => Read(downloads));
        return read == _reads && Apply(snapshot);
    }

    private void LoadDemo()
    {
        _demoLoaded = true;
        Cars.Clear();
        Root = @"C:\Users\You\Documents\iRacing\setups";
        Downloads = @"C:\Users\You\Downloads";
        Cars.AddRange(SetupDemo.Cars());
        foreach (var car in Cars) car.Describe(_settings);
        if (Inbox.Count == 0) Inbox.AddRange(SetupDemo.Inbox(Cars));
    }

    private static Snapshot Read(string? downloads)
    {
        var hash = new Signature();
        string? root = IracingPaths.FindDocumentsFolder() is { } documents ? System.IO.Path.Combine(documents, "setups") : null;
        downloads ??= DownloadsFolder();
        hash.Add(root);
        hash.Add(downloads);
        var cars = new List<SetupCar>();
        if (root is not null && Directory.Exists(root))
        {
            foreach (var dir in SafeDirs(root))
            {
                var car = new SetupCar(System.IO.Path.GetFileName(dir));
                Fill(car.Root, dir);
                cars.Add(car);
                hash.Add(car.Root);
            }
        }
        var inbox = ReadInbox(downloads);
        foreach (var item in inbox) { hash.Add(item.Path); hash.Add(item.Size); hash.Add(item.Modified.Ticks); }
        return new Snapshot(root, downloads, cars, inbox, hash.Value);
    }

    private bool Apply(Snapshot read)
    {
        Root = read.Root;
        Downloads = read.Downloads;
        if (_signature == read.Signature) return false;
        _signature = read.Signature;
        Cars.Clear();
        Cars.AddRange(read.Cars);
        foreach (var car in Cars) car.Describe(_settings);
        SetInbox(read.Inbox);
        return true;
    }

    private sealed class Signature
    {
        public long Value { get; private set; } = unchecked((long)14695981039346656037);

        public void Add(long v) { unchecked { Value = (Value ^ v) * 1099511628211; } }

        public void Add(string? text)
        {
            Add(text?.Length ?? -1);
            if (text is not null) foreach (char c in text) Add(char.ToUpperInvariant(c));
        }

        public void Add(SetupNode node)
        {
            Add(node.Name);
            Add(node.IsFolder ? -2 : node.Size);
            Add(node.Modified.Ticks);
            Add(node.Children.Count);
            foreach (var child in node.Children) Add(child);
        }
    }

    public void Describe()
    {
        foreach (var car in Cars) car.Describe(_settings);
    }

    private static void Fill(SetupNode folder, string path)
    {
        foreach (var dir in SafeDirs(path))
        {
            var child = new SetupNode(folder.Car, System.IO.Path.GetFileName(dir), true, folder);
            Fill(child, dir);
            folder.Children.Add(child);
        }
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*.sto"))
                folder.Children.Add(new SetupNode(folder.Car, file.Name, false, folder) { Size = file.Length, Modified = file.LastWriteTime });
        }
        catch { }
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return Array.Empty<string>(); }
    }

    public void Watch(bool on)
    {
        _setupsWatch?.Dispose();
        _downloadsWatch?.Dispose();
        _setupsWatch = _downloadsWatch = null;
        _settle.Stop();
        _afterQuiet?.Stop();
        if (!on || _demo) return;
        _setupsWatch = Watcher(Root, "*", true);
        _downloadsWatch = Watcher(Downloads, "*", false);
    }

    private FileSystemWatcher? Watcher(string? folder, string filter, bool subfolders)
    {
        if (folder is null || !Directory.Exists(folder)) return null;
        try
        {
            var w = new FileSystemWatcher(folder, filter)
            {
                IncludeSubdirectories = subfolders,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            FileSystemEventHandler changed = (_, e) => OnDiskEvent(e.FullPath, subfolders);
            w.Created += changed;
            w.Changed += changed;
            w.Deleted += changed;
            w.Renamed += (_, e) => OnDiskEvent(e.FullPath, subfolders);
            w.Error += (_, _) => OnDiskEvent(folder, setups: true);
            w.EnableRaisingEvents = true;
            return w;
        }
        catch { return null; }
    }

    private void OnDiskEvent(string path, bool setups)
    {
        if (!setups && !path.EndsWith(".sto", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;
        _settle.Dispatcher.InvokeAsync(() =>
        {
            if (DateTime.Now < _quietUntil) return;
            _settle.Stop();
            _settle.Start();
        });
    }

    private void Quiet()
    {
        _quietUntil = DateTime.Now.AddSeconds(1.5);
        _signature = null;
        _reads++;
        if (_setupsWatch is not null)
        {
            _afterQuiet ??= new DispatcherTimer(TimeSpan.FromSeconds(1.6), DispatcherPriority.Background, (_, _) =>
            {
                _afterQuiet!.Stop();
                if (_setupsWatch is not null) _settle.Start();
            }, _settle.Dispatcher);
            _afterQuiet.Stop();
            _afterQuiet.Start();
        }
    }

    private DispatcherTimer? _afterQuiet;

    public void Dispose() => Watch(false);

    public string? Rename(SetupNode node, string name)
    {
        var parent = node.Parent;
        if (parent is null) return "The car's own folder can't be renamed (iRacing finds it by that name).";
        var (clean, error) = SetupNames.Check(name, !node.IsFolder, parent, node);
        if (error is not null) return error;
        if (clean == node.Name) return null;
        string from = node.Name;
        if (!_demo)
        {
            try
            {
                Quiet();
                string src = PathOf(node), dst = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(src)!, clean!);
                if (node.IsFolder) Directory.Move(src, dst); else File.Move(src, dst);
            }
            catch (Exception ex) { return Explain(ex); }
        }
        string oldKey = node.Key;
        node.Name = clean!;
        SetupOrder.Renamed(parent, from, clean!, _settings.Orders);
        if (node.IsFolder) SetupOrder.FolderMoved(oldKey, node.Key, _settings.Orders);
        return null;
    }

    public (SetupNode? Folder, string? Error) CreateFolder(SetupNode parent, string name)
    {
        var (clean, error) = SetupNames.Check(name, false, parent);
        if (error is not null) return (null, error);
        if (!_demo)
        {
            try { Quiet(); Directory.CreateDirectory(System.IO.Path.Combine(PathOf(parent), clean!)); }
            catch (Exception ex) { return (null, Explain(ex)); }
        }
        var folder = new SetupNode(parent.Car, clean!, true, parent);
        parent.Children.Add(folder);
        return (folder, null);
    }

    public static List<SetupNode> Movable(IEnumerable<SetupNode> nodes, SetupNode dest)
    {
        var list = nodes.Distinct().ToList();
        return list.Where(n => !n.IsRoot && n.Car == dest.Car && n != dest && !n.IsAncestorOf(dest) && n.Parent != dest
            && !list.Any(o => o != n && o.IsAncestorOf(n))).ToList();
    }

    public (int Moved, string? Error) Move(IEnumerable<SetupNode> nodes, SetupNode dest)
    {
        int moved = 0;
        foreach (var node in Movable(nodes, dest))
        {
            string name = SetupNames.Unique(dest.Children.Select(c => c.Name), node.Name, !node.IsFolder);
            if (!_demo)
            {
                try
                {
                    Quiet();
                    string src = PathOf(node), dst = System.IO.Path.Combine(PathOf(dest), name);
                    if (node.IsFolder) Directory.Move(src, dst); else File.Move(src, dst);
                }
                catch (Exception ex) { return (moved, Explain(ex)); }
            }
            string oldKey = node.Key;
            node.Parent!.Children.Remove(node);
            node.Name = name;
            node.Parent = dest;
            dest.Children.Add(node);
            if (node.IsFolder) SetupOrder.FolderMoved(oldKey, node.Key, _settings.Orders);
            moved++;
        }
        return (moved, null);
    }

    public (int Deleted, string? Error) Delete(IEnumerable<SetupNode> nodes)
    {
        var list = nodes.Where(n => !n.IsRoot).Distinct().ToList();
        list = list.Where(n => !list.Any(o => o != n && o.IsAncestorOf(n))).ToList();
        if (!_demo)
        {
            Quiet();
            var paths = list.Select(PathOf).ToList();
            RecycleBin.Send(paths);
            bool left = paths.Any(p => File.Exists(p) || Directory.Exists(p));
            list = list.Where((n, i) => !File.Exists(paths[i]) && !Directory.Exists(paths[i])).ToList();
            if (left)
            {
                foreach (var n in list) n.Parent!.Children.Remove(n);
                return (list.Count, "Some items are still there (in use, no permission, or kept when asked).");
            }
        }
        foreach (var n in list) n.Parent!.Children.Remove(n);
        return (list.Count, null);
    }

    private const long MaxSetupBytes = 5 * 1024 * 1024;

    public (List<SetupNode> Added, List<string> Skipped) Import(IEnumerable<string> files, SetupNode folder)
    {
        var added = new List<SetupNode>();
        var skipped = new List<string>();
        Quiet();
        foreach (var file in files)
        {
            try
            {
                if (file.EndsWith(".sto", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                    added.Add(Add(folder, new[] { System.IO.Path.GetFileName(file) }, new FileInfo(file).Length, dst => File.Copy(file, dst)));
                else if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                {
                    int before = added.Count, failed = 0;
                    using var zip = ZipFile.OpenRead(file);
                    foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".sto", StringComparison.OrdinalIgnoreCase)))
                    {
                        var (car, segments) = SetupNames.Route(entry.FullName, Cars);
                        if (segments.Count == 0 || entry.Length > MaxSetupBytes) continue;
                        try { added.Add(Add(car?.Root ?? folder, segments, entry.Length, dst => ExtractWhole(entry, dst))); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException) { failed++; }
                    }
                    if (added.Count == before) skipped.Add(System.IO.Path.GetFileName(file));
                    else if (failed > 0) skipped.Add($"{System.IO.Path.GetFileName(file)} ({failed} of its setups couldn't be read)");
                }
                else skipped.Add(System.IO.Path.GetFileName(file));
            }
            catch { skipped.Add(System.IO.Path.GetFileName(file)); }
        }
        return (added, skipped);
    }

    private static void ExtractWhole(ZipArchiveEntry entry, string dst)
    {
        string part = dst + ".part";
        try
        {
            entry.ExtractToFile(part, overwrite: true);
            File.Move(part, dst);
        }
        catch
        {
            try { File.Delete(part); } catch { }
            throw;
        }
    }

    private SetupNode Add(SetupNode folder, IReadOnlyList<string> segments, long size, Action<string> write)
    {
        foreach (var seg in segments.Take(segments.Count - 1))
        {
            var next = folder.Children.FirstOrDefault(c => c.IsFolder && string.Equals(c.Name, seg, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                if (!_demo) Directory.CreateDirectory(System.IO.Path.Combine(PathOf(folder), seg));
                next = new SetupNode(folder.Car, seg, true, folder);
                folder.Children.Add(next);
            }
            folder = next;
        }
        string name = SetupNames.Unique(folder.Children.Select(c => c.Name), segments[^1]);
        if (!_demo)
        {
            Directory.CreateDirectory(PathOf(folder));
            write(System.IO.Path.Combine(PathOf(folder), name));
        }
        var node = new SetupNode(folder.Car, name, false, folder) { Size = size, Modified = DateTime.Now };
        folder.Children.Add(node);
        return node;
    }

    public (string? Path, string? Error) Export(IReadOnlyList<SetupNode> nodes, string destination, bool pack)
    {
        if (_demo) return (null, "Not available with demo data: nothing is written.");
        var files = nodes.SelectMany(n => n.FilesBelow()).Distinct().ToList();
        if (files.Count == 0) return (null, "There are no setups in that selection.");
        var car = files[0].Car;
        string? created = null;
        try
        {
            Directory.CreateDirectory(destination);
            if (pack)
            {
                string zipPath = System.IO.Path.Combine(destination, SetupNames.Unique(Directory.GetFiles(destination).Select(f => System.IO.Path.GetFileName(f)), PackName(car, nodes)));
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    created = zipPath;
                    foreach (var file in files)
                        zip.CreateEntryFromFile(PathOf(file), string.Join("/", file.Segments().Prepend(car.Dir)));
                }
                return (zipPath, null);
            }
            string top = System.IO.Path.Combine(destination, SetupNames.Unique(Directory.GetFileSystemEntries(destination).Select(d => System.IO.Path.GetFileName(d)), SetupNames.Clean(car.Name), isFile: false));
            if (!Directory.Exists(top)) created = top;
            foreach (var file in files)
            {
                string dst = file.Segments().Aggregate(top, System.IO.Path.Combine);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst)!);
                File.Copy(PathOf(file), dst, overwrite: false);
            }
            return (top, null);
        }
        catch (Exception ex)
        {
            try
            {
                if (created is not null && pack && File.Exists(created)) File.Delete(created);
                else if (created is not null && Directory.Exists(created)) Directory.Delete(created, recursive: true);
            }
            catch (Exception) { }
            return (null, Explain(ex));
        }
    }

    public static string PackName(SetupCar car, IReadOnlyList<SetupNode> nodes)
    {
        string what = nodes.Count == 1 ? (nodes[0].IsRoot ? "all setups" : System.IO.Path.GetFileNameWithoutExtension(nodes[0].Name)) : GoodieBag.SimRacing.Race.RaceFormat.Count(nodes.Sum(n => n.Files), "setup");
        return SetupNames.Clean($"{car.Name} - {what}") + ".zip";
    }

    public void ScanInbox()
    {
        if (_demo) return;
        SetInbox(ReadInbox(Downloads));
    }

    private static List<DownloadItem> ReadInbox(string? downloads)
    {
        var found = new List<DownloadItem>();
        if (downloads is null || !Directory.Exists(downloads)) return found;
        try
        {
            foreach (var info in new DirectoryInfo(downloads).EnumerateFiles())
            {
                if (DateTime.Now - info.LastWriteTime > InboxFor) continue;
                bool sto = info.Extension.Equals(".sto", StringComparison.OrdinalIgnoreCase), zip = info.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
                if (!sto && !(zip && info.Length < MaxPackBytes)) continue;
                var entries = new List<(string, long)>();
                if (zip)
                {
                    try
                    {
                        using var archive = ZipFile.OpenRead(info.FullName);
                        entries.AddRange(archive.Entries.Where(e => e.FullName.EndsWith(".sto", StringComparison.OrdinalIgnoreCase)).Select(e => (e.FullName, e.Length)));
                    }
                    catch { continue; }
                    if (entries.Count == 0) continue;
                }
                found.Add(new DownloadItem { Path = info.FullName, Size = info.Length, Modified = info.LastWriteTime, Entries = entries });
            }
        }
        catch { }
        found.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        return found;
    }

    private void SetInbox(List<DownloadItem> found)
    {
        Inbox.Clear();
        foreach (var item in found)
        {
            if (SetupManagerSettings.Has(_settings.IgnoredDownloads, item.IgnoreKey)) continue;
            Guess(item);
            Inbox.Add(item);
        }
    }

    private void Guess(DownloadItem item)
    {
        item.PackCars.AddRange(item.Entries.Select(e => SetupNames.Route(e.Entry, Cars).Car?.Dir).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase));
        item.Car = item.PackCars.FirstOrDefault() ?? SetupCars.GuessFromFileName(item.Name, Cars)?.Dir;
    }

    public (List<SetupNode> Added, string? Error) FileDownload(DownloadItem item)
    {
        var car = Car(item.Car);
        if (car is null) return (new(), "Choose the car first.");
        var added = new List<SetupNode>();
        bool partly = false;
        try
        {
            Quiet();
            if (item.IsPack)
            {
                if (_demo)
                    foreach (var (entry, size) in item.Entries)
                    {
                        var (target, segments) = SetupNames.Route(entry, Cars);
                        if (segments.Count > 0) added.Add(Add((target ?? car).Root, segments, size, _ => { }));
                    }
                else
                {
                    var result = Import(new[] { item.Path }, car.Root);
                    added = result.Added;
                    partly = result.Skipped.Count > 0;
                }
                if (added.Count == 0) return (added, "None of the setups in this pack could be filed.");
            }
            else added.Add(Add(car.Root, new[] { item.Name }, item.Size, dst => System.IO.File.Move(item.Path, dst)));
        }
        catch (Exception ex) { return (added, Explain(ex)); }
        Inbox.Remove(item);
        if (item.IsPack) IgnoreForGood(item);
        return (added, partly ? "Some setups in this pack couldn't be read (encrypted or damaged), so they weren't filed." : null);
    }

    public void Ignore(DownloadItem item)
    {
        Inbox.Remove(item);
        IgnoreForGood(item);
    }

    private void IgnoreForGood(DownloadItem item)
    {
        SetupManagerSettings.Set(_settings.IgnoredDownloads, item.IgnoreKey, true);
        if (!_demo) _settings.IgnoredDownloads.RemoveAll(k => !System.IO.File.Exists(k.Split('|')[0]));
    }

    private static string Explain(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Windows refused (no permission, or the file is read-only).",
        IOException io when io.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021) => "The file is in use (iRacing may have it open). Try again in a moment.",
        IOException io => io.Message,
        _ => ex.Message,
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    internal static string? DownloadsForTests { get; set; }

    private static string? DownloadsFolder()
    {
        if (DownloadsForTests is { } sandbox) return sandbox;
        try
        {
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0)
            {
                string? path = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                if (!string.IsNullOrEmpty(path)) return path;
            }
        }
        catch { }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? null : System.IO.Path.Combine(home, "Downloads");
    }
}

internal static class SetupDemo
{
    private static readonly (string Dir, string[] Paths)[] Sample =
    {
        ("porsche963gtp", new[] { "IMSA/Sebring/Race.sto", "IMSA/Sebring/Qualifying.sto", "IMSA/Daytona/Race.sto", "IMSA/Daytona/Race - Night.sto", "IMSA/Road Atlanta/Race.sto", "Endurance/Daytona 24.sto", "League S12/R5 Watkins Glen.sto", "League S12/R6 Spa.sto", "Default.sto", "baseline_safe.sto" }),
        ("bmwlmdh", new[] { "IMSA/Sebring Race.sto", "IMSA/Sebring Qual.sto", "Road Atlanta.sto" }),
        ("cadillacvseriesrgtp", new[] { "Teammate shared/Daytona_Race_v2.sto", "Teammate shared/Daytona_Race_v3.sto", "Monza Low DF.sto" }),
        ("acuraarx06gtp", new[] { "IMSA/Daytona/Race.sto", "IMSA/Daytona/Qualifying.sto", "IMSA/Long Beach.sto", "Baseline.sto" }),
        ("dallarap217", new[] { "ELMS/Spa Race.sto", "ELMS/Paul Ricard.sto", "Le Mans.sto" }),
        ("ferrari296gt3", new[] { "IMSA Endurance/Sebring/Race.sto", "Sprint/Monza.sto", "Sprint/Silverstone.sto", "Default.sto" }),
        ("porsche992rgt3", new[] { "League S12/R1 Laguna Seca.sto", "League S12/R2 Road America.sto", "League S12/R10 Imola.sto", "Archive/2025 S4/Spa_old.sto", "Default.sto" }),
        ("audir8lmsevo2gt3", new[] { "IMSA/Sebring Race.sto", "Sprint/Spa.sto", "Sprint/Monza.sto" }),
        ("mercedesamgevogt3", new[] { "Spa Race.sto", "Watkins Glen.sto" }),
        ("bmwm4evogt4", new[] { "Road America.sto" }),
        ("porsche991rsr", new[] { "Le Mans.sto", "Sebring Race.sto" }),
        ("hondacivictyper", Array.Empty<string>()),
        ("superformulasf23 toyota", new[] { "League S12/Suzuka Race.sto", "League S12/Suzuka Qual.sto", "Hotlap/Suzuka TT.sto", "Default.sto" }),
        ("dallarair18", new[] { "Ovals/Indianapolis Race.sto", "Road/Long Beach.sto" }),
        ("stockcars chevycamarozl12022", new[] { "Superspeedway/Daytona 500 - Draft.sto", "Intermediate/Charlotte Race.sto", "Intermediate/Kansas Race.sto", "Short track/Martinsville.sto" }),
        ("legends ford34c", new[] { "Charlotte Legends Oval.sto" }),
        ("dirtlatemodel 438", new[] { "Eldora/Heat.sto", "Eldora/Feature.sto" }),
        ("mx5 mx52016", new[] { "Rookies/Okayama.sto", "Rookies/Lime Rock.sto", "Default.sto" }),
        ("car_ae91c2", new[] { "setup1.sto" }),
    };

    public static IEnumerable<SetupCar> Cars()
    {
        var random = new Random(20261007);
        foreach (var (dir, paths) in Sample)
        {
            var car = new SetupCar(dir);
            foreach (var path in paths)
            {
                var folder = car.Root;
                var parts = path.Split('/');
                foreach (var part in parts[..^1])
                {
                    var next = folder.Children.FirstOrDefault(c => c.IsFolder && c.Name == part);
                    if (next is null) { next = new SetupNode(car, part, true, folder); folder.Children.Add(next); }
                    folder = next;
                }
                double days = Math.Pow(random.NextDouble(), 2) * 400;
                folder.Children.Add(new SetupNode(car, parts[^1], false, folder) { Size = 6000 + random.Next(26000), Modified = DateTime.Now.AddDays(-days) });
            }
            yield return car;
        }
    }

    public static IEnumerable<DownloadItem> Inbox(IReadOnlyList<SetupCar> cars)
    {
        var items = new[]
        {
            new DownloadItem { Path = @"C:\Users\You\Downloads\Porsche963_Sebring_Race_v4.sto", Size = 18400, Modified = DateTime.Now.AddHours(-2) },
            new DownloadItem { Path = @"C:\Users\You\Downloads\League S12 R7 - Audi R8 Spa.zip", Size = 41800, Modified = DateTime.Now.AddHours(-20),
                Entries = { ("audir8lmsevo2gt3/League S12/R7 Spa Race.sto", 19800), ("audir8lmsevo2gt3/League S12/R7 Spa Qual.sto", 19200) } },
            new DownloadItem { Path = @"C:\Users\You\Downloads\baseline_fixed.sto", Size = 12100, Modified = DateTime.Now.AddHours(-30) },
        };
        foreach (var item in items)
        {
            item.PackCars.AddRange(item.Entries.Select(e => SetupNames.Route(e.Entry, cars).Car?.Dir).OfType<string>().Distinct());
            item.Car = item.PackCars.FirstOrDefault() ?? SetupCars.GuessFromFileName(item.Name, cars)?.Dir;
        }
        return items;
    }
}

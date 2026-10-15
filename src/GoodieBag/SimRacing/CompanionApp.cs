using System.Diagnostics;
using System.IO;
using GoodieBag.Config;
using GoodieBag.Processes;
using GoodieBag.Services;
using Microsoft.Win32;

namespace GoodieBag.SimRacing;

internal enum CompanionStatus { NotInstalled, NotRunning, Running, Unknown }

internal sealed record CompanionSpec(string Id, string Name, string Program, string Website, string Site, string[] Names, string[] ExeNames, string[] Processes, string DemoPath)
{
    public string Host => new Uri(Site).Host.Replace("www.", "");
}

internal sealed class CompanionApp
{
    public static readonly CompanionSpec TradingPaints = new("tradingpaints", "Trading Paints", "the Trading Paints Downloader", "https://www.tradingpaints.com/install", "https://www.tradingpaints.com",
        new[] { "Trading Paints" }, new[] { "Trading" }, Array.Empty<string>(), @"C:\Program Files (x86)\Rhinode LLC\Trading Paints\Trading Paints.exe");

    public static readonly CompanionSpec Garage61 = new("garage61", "Garage 61", "the Garage 61 Telemetry Agent", "https://garage61.net/app", "https://garage61.net/app",
        new[] { "Garage 61", "Garage61" }, new[] { "garage61-launcher", "garage61" }, new[] { "garage61" }, @"C:\Users\You\AppData\Local\Programs\Garage 61\garage61-launcher.exe");

    private static readonly string[] IracingProcesses = { "iracingui", "iracingsim64dx11" };

    private readonly AppServices _app;
    private readonly Func<CompanionAppSettings> _settings;
    private bool _watching;
    private string? _exe;
    private bool _searched;

    public CompanionApp(AppServices app, CompanionSpec spec, Func<CompanionAppSettings> settings)
    {
        _app = app;
        Spec = spec;
        _settings = settings;
    }

    public CompanionSpec Spec { get; }

    public CompanionAppSettings Settings => _settings();

    private string Consumer => Spec.Name;

    public event Action? Changed;

    private readonly object _findGate = new();

    public string? ExePath
    {
        get
        {
            if (_app.Demo) return Spec.DemoPath;
            lock (_findGate)
            {
                if (!_searched || (_exe is not null && !File.Exists(_exe)))
                {
                    _exe = Find();
                    _searched = true;
                }
                return _exe;
            }
        }
    }

    public void Refresh()
    {
        _searched = false;
        ArmStatusWatch();
        Changed?.Invoke();
    }

    public CompanionStatus Status()
    {
        if (ExePath is not { } exe) return CompanionStatus.NotInstalled;
        if (_app.Demo) return CompanionStatus.NotRunning;
        try
        {
            var processes = Process.GetProcesses();
            try { return processes.Any(p => IsOurs(p, exe)) ? CompanionStatus.Running : CompanionStatus.NotRunning; }
            finally { foreach (var p in processes) p.Dispose(); }
        }
        catch
        {
            return CompanionStatus.Unknown;
        }
    }

    private string StatusConsumer => Spec.Name + " (status)";
    private string? _statusName;
    private bool _statusWatch, _statusWanted;

    public void WatchStatus(bool on)
    {
        _statusWanted = on;
        ArmStatusWatch();
    }

    private void ArmStatusWatch()
    {
        string? exe = _statusWanted && !_app.Demo ? ExePath : null;
        string? name = exe is null ? null : AppWatcher.Normalize(exe);
        if (_statusWatch && name == _statusName) return;
        if (_statusWatch)
        {
            _app.Watcher.Started -= OnStatusEvent;
            _app.Watcher.Stopped -= OnStatusEvent;
            _app.Watcher.Release(StatusConsumer);
            _statusWatch = false;
        }
        _statusName = name;
        if (exe is null) return;
        _app.Watcher.Started += OnStatusEvent;
        _app.Watcher.Stopped += OnStatusEvent;
        _statusNames = Spec.Processes.Select(AppWatcher.Normalize).Append(name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _app.Watcher.Acquire(StatusConsumer, _statusNames);
        _statusWatch = true;
    }

    private HashSet<string> _statusNames = new(StringComparer.OrdinalIgnoreCase);

    private void OnStatusEvent(string name)
    {
        if (_statusNames.Contains(name)) Changed?.Invoke();
    }

    private bool IsOurs(Process p, string exe) =>
        (p.ProcessName.Equals(Path.GetFileNameWithoutExtension(exe), StringComparison.OrdinalIgnoreCase)
         || Spec.Processes.Any(prefix => p.ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        && InThisSession(p);

    private static readonly int Session = Process.GetCurrentProcess().SessionId;

    private static bool InThisSession(Process p)
    {
        try { return p.SessionId == Session; }
        catch { return false; }
    }

    public async Task CloseAsync()
    {
        if (_app.Demo || ExePath is not { } exe)
        {
            if (_app.Demo) _app.Notify($"{Spec.Name} stopped", "Demo: nothing was closed.", NoticeKind.Info);
            return;
        }
        string? problem = await Task.Run(async () =>
        {
            try
            {
                var mine = Process.GetProcesses().Where(p => { bool ours = IsOurs(p, exe); if (!ours) p.Dispose(); return ours; }).ToList();
                try
                {
                    foreach (var p in mine) { try { p.CloseMainWindow(); } catch { } }
                    var deadline = DateTime.UtcNow.AddSeconds(4);
                    while (DateTime.UtcNow < deadline && mine.Any(p => { try { return !p.HasExited; } catch { return false; } }))
                        await Task.Delay(250);
                    foreach (var p in mine)
                        try { if (!p.HasExited) { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } } catch { }
                    if (mine.Any(p => { try { return !p.HasExited; } catch { return false; } }))
                        return "It's still running: Windows didn't let Goodie Bag end it.";
                }
                finally { foreach (var p in mine) p.Dispose(); }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        });
        if (problem is null) _app.Notify($"{Spec.Name} stopped", "", NoticeKind.Success);
        else _app.Notify($"Couldn't stop {Spec.Name}", problem, NoticeKind.Warning);
        Changed?.Invoke();
    }

    public string? Launch(bool quiet = false)
    {
        switch (Status())
        {
            case CompanionStatus.NotInstalled:
                return $"{Upper(Spec.Program)} isn't installed.";
            case CompanionStatus.Running:
                if (!quiet) _app.Notify($"{Spec.Name} is already running", "", NoticeKind.Info);
                return null;
        }
        if (_app.Demo)
            return null;
        var error = ProcessLauncher.Launch(ExePath!);
        if (error is null && !quiet)
            _app.Notify($"Starting {Spec.Name}", "", NoticeKind.Success);
        else if (error is not null)
            _app.Notify($"{Spec.Name} didn't start", error, NoticeKind.Warning, important: !quiet);
        Changed?.Invoke();
        return error;
    }

    public static string Upper(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    public void SetCustomPath(string? path)
    {
        Settings.CustomPath = string.IsNullOrWhiteSpace(path) ? null : path;
        _app.Save();
        Refresh();
    }

    public void Start()
    {
        if (Settings.LaunchWithGoodieBag)
            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(() => { if (!_off && Settings.LaunchWithGoodieBag) Launch(quiet: true); },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        UpdateWatch();
    }

    public void Stop()
    {
        WatchStatus(false);
        StopIracingWatch();
    }

    public void SetActive(bool on)
    {
        _off = !on;
        if (on) UpdateWatch();
        else Stop();
    }

    private bool _off;

    private void StopIracingWatch()
    {
        if (_watching)
        {
            _app.Watcher.Started -= OnStarted;
            _app.Watcher.Release(Consumer);
            _watching = false;
        }
    }

    public void UpdateWatch()
    {
        bool want = Settings.LaunchWithIracing && !_app.Demo && !_off;
        if (want == _watching) return;
        if (!want)
        {
            StopIracingWatch();
            return;
        }
        _rewatched = _watchedBefore;
        _app.Watcher.Started += OnStarted;
        _app.Watcher.Acquire(Consumer, IracingProcesses);
        _watching = true;
        _watchedBefore = true;
    }

    private bool _watchedBefore, _rewatched;

    private void OnStarted(string name)
    {
        if (_app.Watcher.CatchingUp && _rewatched) return;
        if (IracingProcesses.Contains(name))
            Launch(quiet: true);
    }

    private string? Find()
    {
        if (Settings.CustomPath is { } custom && Try(() => File.Exists(custom) ? custom : null) is { } picked)
            return picked;
        return Try(FromUninstallEntry) ?? Try(FromStartMenu) ?? Try(FromFolders);
    }

    private static string? Try(Func<string?> find)
    {
        try { return find(); }
        catch { return null; }
    }

    private bool Named(string text) => Spec.Names.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private string? FromUninstallEntry()
    {
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (root is null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    using var entry = root.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is not string display || !Named(display))
                        continue;
                    if (entry.GetValue("DisplayIcon") is string icon && ExeFrom(icon) is { } fromIcon)
                        return fromIcon;
                    if (entry.GetValue("InstallLocation") is string location && InFolder(location) is { } fromFolder)
                        return fromFolder;
                }
            }
            catch { }
        }
        return null;
    }

    private string? FromStartMenu()
    {
        foreach (var menu in new[] { Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.StartMenu })
        {
            var folder = Environment.GetFolderPath(menu);
            if (!Directory.Exists(folder)) continue;
            string[] links;
            try { links = Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var link in links.Where(l => Named(Path.GetFileName(l))))
                if (ShortcutTarget(link) is { } target && ExeFrom(target) is { } exe)
                    return exe;
        }
        return null;
    }

    internal static string? ShortcutTarget(string link)
    {
        object? shortcut = null;
        try
        {
            shortcut = new ShellLink();
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)shortcut).Load(link, 0);
            var path = new System.Text.StringBuilder(1024);
            ((IShellLinkW)shortcut).GetPath(path, path.Capacity, IntPtr.Zero, 0);
            return path.Length > 0 ? path.ToString() : null;
        }
        catch { return null; }
        finally
        {
            if (shortcut is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
        }
    }

    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("000214F9-0000-0000-C000-000000000046"),
     System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] System.Text.StringBuilder file,
            int size, IntPtr findData, uint flags);
    }

    private string? FromFolders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Path.Combine(local, "Programs"), local,
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            var folders = SafeDirectories(root).Where(d => Named(Path.GetFileName(d)))
                .Concat(root == local ? Enumerable.Empty<string>() : SafeDirectories(root).SelectMany(SafeDirectories).Where(d => Named(Path.GetFileName(d))));
            foreach (var folder in folders)
                if (InFolder(folder) is { } exe)
                    return exe;
        }
        return null;
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try { return Directory.GetDirectories(folder); }
        catch { return Array.Empty<string>(); }
    }

    private static string? ExeFrom(string value)
    {
        var path = value.Split(',')[0].Trim().Trim('"');
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) && !IsHelperExe(path) ? path : null;
    }

    private string? InFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        var exes = Directory.GetFiles(folder, "*.exe").Where(e => !IsHelperExe(e)).ToList();
        foreach (var name in Spec.ExeNames)
            if (exes.FirstOrDefault(e => Path.GetFileName(e).Contains(name, StringComparison.OrdinalIgnoreCase)) is { } named)
                return named;
        return exes.Count == 1 ? exes[0] : null;
    }

    private static bool IsHelperExe(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) || name.Contains("update", StringComparison.OrdinalIgnoreCase)
            || name.Contains("setup", StringComparison.OrdinalIgnoreCase) || name.Contains("crash", StringComparison.OrdinalIgnoreCase);
    }
}

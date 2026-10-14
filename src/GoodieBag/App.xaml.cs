using System.IO;
using System.Windows;
using System.Windows.Threading;
using GoodieBag.Config;
using GoodieBag.Services;
using GoodieBag.Theme;
using GoodieBag.Views;

namespace GoodieBag;

public partial class App : Application
{
    private AppServices? _services;

    internal static bool ExitRequested { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains(FanService.Arg, StringComparer.OrdinalIgnoreCase))
        {
            FanService.Run(this);
            return;
        }
        if (ArgValue(e.Args, HudSheet.Arg) is { } sheetFolder)
        {
            HudSheet.Run(this, sheetFolder);
            return;
        }
        if (ArgValue(e.Args, FuelReplay.Arg) is { } replayFolder)
        {
            int at = Array.FindIndex(e.Args, a => string.Equals(a, FuelReplay.Arg, StringComparison.OrdinalIgnoreCase));
            string reportFile = at + 2 < e.Args.Length ? e.Args[at + 2] : Path.Combine(replayFolder, "fuel-replay.txt");
            FuelReplay.Run(this, replayFolder, reportFile);
            return;
        }
        if (Array.FindIndex(e.Args, a => string.Equals(a, TrackMapBuild.Arg, StringComparison.OrdinalIgnoreCase)) is >= 0 and var mapsAt && mapsAt + 3 < e.Args.Length)
        {
            TrackMapBuild.Run(this, e.Args.Skip(mapsAt + 1).ToList());
            return;
        }
        if (ArgValue(e.Args, FanProbe.Arg) is { } probeFile)
        {
            FanProbe.Run(this, probeFile);
            return;
        }
        PerfProbe.Mark("onstartup");
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline), new FrameworkPropertyMetadata { DefaultValue = 60 });
        Controls.ClickGuard.Install();
        Views.Motion.InstallPress();
        Views.Ui.InstallDropDownWheel();
        Views.Motion.InstallSwitches();

        string? screenshotDir = ArgValue(e.Args, "--screenshots");
        bool fromStartup = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        string? showcaseDir = ArgValue(e.Args, "--showcase");
        string? perfFile = ArgValue(e.Args, "--perf");
        PerfProbe.File = perfFile;
        if (perfFile is not null) try { File.Delete(perfFile); } catch { }
        Motion.FollowFocus = screenshotDir is null && showcaseDir is null && perfFile is null;
        bool demo = screenshotDir is not null || showcaseDir is not null || perfFile is not null || e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase);
        bool fresh = e.Args.Contains("--fresh", StringComparer.OrdinalIgnoreCase);

        if (!demo && !SingleInstance.TryAcquire())
        {
            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }
        bool tookOverFans = !demo && FanService.StopRunning(TimeSpan.FromSeconds(6));

        DispatcherUnhandledException += OnUiException;
        Dispatcher.BeginInvoke(new Action(() => _starting = false));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError(args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) LogError(ex);
            AppServices.Current?.Hardware.EmergencyRestore();
        };
        System.Windows.Forms.Application.ThreadException += (_, args) => ReportError(args.Exception);

        string? warning = null;
        AppConfig config;
        if (demo)
        {
            ConfigStore.ReadOnly = true;
            config = fresh ? ConfigStore.Normalize(ConfigStore.Fresh()) : DemoData.Config();
        }
        else
        {
            config = ConfigStore.Load(out warning);
        }

        int themeAt = Array.FindIndex(e.Args, a => a.Equals("--theme", StringComparison.OrdinalIgnoreCase));
        bool themed = themeAt >= 0 && themeAt + 1 < e.Args.Length;
        var asked = themed ? AppTheme.Named(e.Args[themeAt + 1]) : AppTheme.Light;
        if (demo && screenshotDir is null && themed) config.Ui.Theme = asked;
        if (showcaseDir is not null)
        {
            config.Ui.Animations = true;
            if (!themed) config.Ui.Theme = AppTheme.Dark;
            config.Overlay.Visible = false;
        }
        if (screenshotDir is not null)
        {
            config.Ui.Theme = asked;
            if (ScreenshotRunner.Quick)
                config.Ui.Animations = false;
        }
        PerfProbe.Mark("config");
        Grr.GrrResources.Install();
        if (Plugins.PluginStates.IsEnabled(config.Plugins, Grr.GrrPlugin.PluginId, Grr.GrrPlugin.OnByDefault))
            Theme.Themes.Register(Grr.GrrTheme.Spec);
        if (!Theme.Themes.Exists(config.Ui.Theme))
            config.Ui.Theme = AppTheme.Dark;
        Theme.Themes.SetOledCustom(config.Ui.OledCustom);
        Theme.Themes.SetPastelPreset(config.Ui.PastelPreset);
        ThemeManager.Apply(config.Ui.Theme);
        PerfProbe.Mark("theme");

        Quotes.Warm();
        _services = new AppServices(demo, config);
        PerfProbe.Mark("services");
        _services.Start(withTray: screenshotDir is null && showcaseDir is null && perfFile is null, fromStartup: fromStartup && !demo);
        PerfProbe.Mark("started");

        var window = new MainWindow(_services);
        MainWindow = window;
        PerfProbe.Mark("window");

        _services.ToggleWindowRequested += window.ToggleFromHotkey;
        if (_services.Tray is { } tray)
        {
            tray.OpenRequested += page => window.ShowFromTray(page);
            tray.ExitRequested += () => _ = window.RequestQuitAsync();
            _trayWired = true;
        }

        if (screenshotDir is not null)
        {
            ScreenshotRunner.Run(window, screenshotDir);
            return;
        }
        if (showcaseDir is not null)
        {
            Showcase.ShowcaseRunner.Run(window, showcaseDir, ArgValue(e.Args, "--ffmpeg"));
            return;
        }
        if (perfFile is not null)
        {
            PerfProbe.Run(window, perfFile);
            return;
        }

        if (config.Ui.StartMinimized && _services.Tray is { } trayIcon)
        {
            if (!fromStartup || !config.Ui.TrayTipShown)
            {
                config.Ui.TrayTipShown = true;
                _services.Save();
                trayIcon.ShowBalloon("Goodie Bag is running in the background",
                    "Click the bag icon in the system tray to open it, or right-click it to quit. (Settings → Start in background)", NoticeKind.Info);
            }
        }
        else
        {
            window.Show();
        }

        if (!demo)
            SingleInstance.Listen(() => Dispatcher.BeginInvoke(new Action(() => window.ShowFromTray(null))));

        if (warning is not null)
            _services.Notify("Settings reset", warning, NoticeKind.Warning, important: true);
        if (tookOverFans)
            _services.Notify("Fan control is back in Goodie Bag", "The background fan control you left running has stopped; Goodie Bag drives the fans again.", NoticeKind.Info);
    }

    internal static void RelaunchAfterExit(string args, IEnumerable<string>? steps = null, string? exe = null)
    {
        exe ??= Environment.ProcessPath!;
        int pid = Environment.ProcessId;
        var script = Path.Combine(ScratchFolder(exe), $"GoodieBag-relaunch-{pid}.cmd");
        var lines = new List<string>
        {
            "@echo off",
            ":wait",
            $"{SystemTool("tasklist")} /fi \"PID eq {pid}\" | {SystemTool("find")} \"{pid}\" >nul && ({WaitASecond} & goto wait)",
        };
        if (steps is not null) lines.AddRange(steps);
        lines.Add($"start \"\" {BatchPath(exe)} {args}");
        lines.Add("del \"%~f0\"");
        File.WriteAllText(script, string.Join("\r\n", lines));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \".\\{Path.GetFileName(script)}\"")
        {
            WorkingDirectory = Path.GetDirectoryName(script)!,
            CreateNoWindow = true, UseShellExecute = false, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        })?.Dispose();
        Quit();
    }

    internal static string ScratchFolder(string exe)
    {
        if (Path.GetDirectoryName(exe) is { Length: > 0 } folder)
        {
            try
            {
                var probe = Path.Combine(folder, $"GoodieBag-write-test-{Environment.ProcessId}.tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return folder;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return Path.GetTempPath();
    }

    internal static string BatchPath(string path) => "\"" + path.Replace("%", "%%") + "\"";

    private static string SystemTool(string name) => $"\"%SystemRoot%\\System32\\{name}.exe\"";
    internal static readonly string WaitASecond = SystemTool("ping") + " -n 2 127.0.0.1 >nul";

    public static void Quit()
    {
        ExitRequested = true;
        Current.Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        _services?.FlushPendingSave();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        SingleInstance.Release();
        base.OnExit(e);
    }

    private bool _starting = true, _trayWired;

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_starting && !ScreenshotRunner.Active && PerfProbe.File is null && !_trayWired && MainWindow is not { IsVisible: true })
        {
            LogError(e.Exception);
            try { AppServices.Current?.Hardware.EmergencyRestore(); } catch { }
            MessageBox.Show(
                "Goodie Bag couldn't start.\n\n" + e.Exception.Message +
                $"\n\nDetails were written to {Path.Combine(ConfigStore.Folder, "error.log")}.",
                "Goodie Bag", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        ReportError(e.Exception);
    }

    private static void ReportError(Exception ex)
    {
        LogError(ex);
        if (ScreenshotRunner.Active)
        {
            ScreenshotRunner.RecordError(ex);
            return;
        }
        if (PerfProbe.File is { } perf)
        {
            try { File.AppendAllText(perf, "error " + ex.ToString().Replace(Environment.NewLine, " | ") + Environment.NewLine); } catch { }
            Environment.Exit(1);
        }

        MessageBox.Show(
            "Goodie Bag hit an unexpected error but is still running.\n\n" + ex.Message +
            $"\n\nDetails were written to {Path.Combine(ConfigStore.Folder, "error.log")}.",
            "Goodie Bag", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    internal static void LogError(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.Folder);
            File.AppendAllText(Path.Combine(ConfigStore.Folder, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static IEnumerable<string> ArgValues(string[] args, string name)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                yield return args[i + 1];
    }
}

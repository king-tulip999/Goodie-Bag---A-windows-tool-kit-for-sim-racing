using System.Diagnostics;
using System.IO;
using System.Windows;
using GoodieBag.Config;
using GoodieBag.Hardware;
using WinForms = System.Windows.Forms;

namespace GoodieBag.Services;

internal static class FanService
{
    public const string Arg = "--fan-service";
    private const string MutexName = @"Local\GoodieBag.FanService";
    private const string StopEventName = @"Local\GoodieBag.FanService.Stop";
    private const string AppMutexName = @"Local\GoodieBag.SingleInstance";
    private static string PidFile => Path.Combine(ConfigStore.Folder, "fan-service.pid");

    private static Mutex? _mutex;
    private static HardwareService? _hardware;
    private static WinForms.NotifyIcon? _tray;

    public static bool IsRunning
    {
        get
        {
            if (!Mutex.TryOpenExisting(MutexName, out var m)) return false;
            m.Dispose();
            return true;
        }
    }

    public static bool Start()
    {
        if (!External.Allowed("start the fan service")) return false;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Arg) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return false;
        }
    }

    public static bool StopRunning(TimeSpan wait)
    {
        if (!IsRunning) return false;
        try
        {
            using var stop = EventWaitHandle.OpenExisting(StopEventName);
            stop.Set();
        }
        catch { }
        var until = DateTime.UtcNow + wait;
        while (IsRunning && DateTime.UtcNow < until) Thread.Sleep(100);
        if (IsRunning && ReadPid() is { } pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                using var self = Process.GetCurrentProcess();
                if (pid != self.Id && p.ProcessName.Equals(self.ProcessName, StringComparison.OrdinalIgnoreCase)) p.Kill();
            }
            catch { }
        }
        return true;
    }

    private static int? ReadPid()
    {
        try { return int.TryParse(File.ReadAllText(PidFile).Trim(), out var pid) ? pid : null; }
        catch { return null; }
    }

    public static void Run(Application app)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _mutex = new Mutex(true, MutexName, out bool created);
        if (!created) { app.Shutdown(); return; }

        var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName);
        try { File.WriteAllText(PidFile, Environment.ProcessId.ToString()); } catch { }

        var until = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (AppRunning() && DateTime.UtcNow < until && !stop.WaitOne(200)) { }
        if (stop.WaitOne(0) || AppRunning()) { Leave(app); return; }

        ConfigStore.ReadOnly = true;
        var config = ConfigStore.Load(out _);
        if (!config.Fans.Enabled || config.Plugins.TryGetValue(Plugins.PluginIds.FanControl, out var on) && !on) { Leave(app); return; }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) App.LogError(ex);
            _hardware?.EmergencyRestore();
        };
        app.Exit += (_, _) => { _hardware?.Dispose(); _hardware = null; };
        _hardware = new HardwareService();
        var fans = ConfigStore.Clone(config.Fans);
        fans.Enabled = true;
        _hardware.SetFanSettings(fans);
        _hardware.Acquire("Fan service");
        if (stop.WaitOne(0) || AppRunning()) { Leave(app); return; }

        new Thread(() => { stop.WaitOne(); app.Dispatcher.BeginInvoke(new Action(() => Leave(app))); }) { IsBackground = true, Name = "Fan service stop" }.Start();
        ShowTray(app, config.Fans.ActivePreset ?? "your fan preset", config.Ui.Theme);
    }

    private static bool AppRunning()
    {
        if (!Mutex.TryOpenExisting(AppMutexName, out var m)) return false;
        m.Dispose();
        return true;
    }

    private static void ShowTray(Application app, string preset, Config.AppTheme theme)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(new WinForms.ToolStripMenuItem($"Fan control running · {preset}") { Enabled = false });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Open Goodie Bag", null, (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false })?.Dispose(); }
            catch (Exception ex) { App.LogError(ex); }
        });
        menu.Items.Add("Stop fan control", null, (_, _) => Leave(app));
        _tray = new WinForms.NotifyIcon
        {
            Icon = TrayService.FanIcon(theme) ?? System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), ContextMenuStrip = menu, Visible = true,
            Text = "Goodie Bag fan control (running without the app)",
        };
    }

    private static void Leave(Application app)
    {
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        _hardware?.Dispose();
        _hardware = null;
        try { File.Delete(PidFile); } catch { }
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
        app.Shutdown();
    }
}

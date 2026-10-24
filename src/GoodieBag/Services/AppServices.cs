using System.Windows;
using System.Windows.Threading;
using GoodieBag.Config;
using GoodieBag.Display;
using GoodieBag.Hardware;
using GoodieBag.Network;
using GoodieBag.Overlay;
using GoodieBag.Plugins;
using GoodieBag.Processes;
using GoodieBag.WindowProfiles;

namespace GoodieBag.Services;

internal enum NoticeKind { Info, Success, Warning, Error }

internal sealed class AppServices : IDisposable
{
    public static AppServices Current { get; private set; } = null!;

    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _saveDebounce;

    public AppConfig Config { get; }
    public bool Demo { get; }
    public bool IsAdmin { get; } = Environment.IsPrivilegedProcess;

    public HardwareService Hardware { get; }
    public FrameRateService Frames { get; }
    public NetworkMonitor Network { get; }
    public OverlayController Overlay { get; }
    public DisplayService Displays { get; }
    public DisplayProfileService DisplayProfiles { get; }
    public WindowProfileService WindowProfiles { get; }
    public AppWatcher Watcher { get; }

    public SimRacing.Telemetry.TelemetryService Telemetry { get; }

    public HotkeyService Hotkeys { get; }

    private SimRacing.IracingKeybinds? _iracingKeys;
    public SimRacing.IracingKeybinds IracingKeys => _iracingKeys ??= new SimRacing.IracingKeybinds(Demo, Config.SimRacing.IracingController);

    public event Action? ToggleWindowRequested;
    public void RequestToggleWindow() => ToggleWindowRequested?.Invoke();
    public TrayService? Tray { get; private set; }
    public MetricHistory History { get; } = new();
    public PluginManager Plugins { get; }
    public UpdateService Updates { get; }

    public event Action? Tick;

    public event Action<string, string, NoticeKind>? Notice;

    public event Action? OverlayChanged;
    public event Action? FansChanged;

    public event Action? PerformanceChanged;

    public Func<IReadOnlyList<(string Key, string Title)>> NavPages { get; set; } = () => Array.Empty<(string, string)>();

    public Func<bool> IsWindowVisible { get; set; } = () => false;

    public AppServices(bool demo, AppConfig config)
    {
        Current = this;
        Demo = demo;
        Config = config;

        Hardware = new HardwareService();
        if (demo)
            Hardware.UseDemo();
        Hardware.Problem += (title, message) => Notify(title, message, NoticeKind.Warning, important: true);
        Hardware.Configure(Config.Performance.IntervalMs, Config.Performance.Background);
        Frames = new FrameRateService { DemoMode = demo };
        Network = new NetworkMonitor(Config.Network);
        Overlay = new OverlayController(Config.Overlay, Hardware, Frames, Network, VisibleFanSpeeds) { DemoMode = demo };
        Overlay.Problem += (title, message) => Notify(title, message, NoticeKind.Warning, important: true);
        Displays = new DisplayService(demo);
        MonitorInfo.CustomName = key => Config.Displays.Names.GetValueOrDefault(key);
        DisplayProfiles = new DisplayProfileService(this);
        Watcher = new AppWatcher();
        Telemetry = new SimRacing.Telemetry.TelemetryService(Watcher);
        WindowProfiles = new WindowProfileService(this);
        Hotkeys = new HotkeyService(this);
        Plugins = new PluginManager(this, Config);
        Updates = new UpdateService(this);

        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) =>
        {
            Hardware.SetWindowHidden(!IsWindowVisible());
            Tick?.Invoke();
        };
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); SaveInBackground(); };
    }

    public void Start(bool withTray)
    {
        Plugins.StartEnabled();
        Hotkeys.RegisterAll();
        if (withTray)
            Tray = new TrayService(this);
        _tick.Start();
        Updates.Start();
        WatchSim();
    }

    public bool Animations => Config.Ui.Animations && SystemParameters.ClientAreaAnimation && !Light;

    private const string SimWatch = "Lighter drawing";

    public bool SimRunning { get; private set; }

    public bool Light => SimRunning && Config.Performance.LightWhileRacing;

    public event Action? LightChanged;

    public void WatchSim()
    {
        bool want = Config.Performance.LightWhileRacing && !Demo;
        Watcher.Started -= OnSimStarted;
        Watcher.Stopped -= OnSimStopped;
        if (want)
        {
            Watcher.Started += OnSimStarted;
            Watcher.Stopped += OnSimStopped;
            Watcher.Acquire(SimWatch, new[] { SimRacing.Telemetry.TelemetryService.SimProcess });
        }
        else Watcher.Release(SimWatch);
        SetSimRunning(want && Watcher.IsRunning(SimRacing.Telemetry.TelemetryService.SimProcess));
    }

    private void OnSimStarted(string name)
    {
        if (string.Equals(name, SimRacing.Telemetry.TelemetryService.SimProcess, StringComparison.OrdinalIgnoreCase)) SetSimRunning(true);
    }

    private void OnSimStopped(string name)
    {
        if (string.Equals(name, SimRacing.Telemetry.TelemetryService.SimProcess, StringComparison.OrdinalIgnoreCase)) SetSimRunning(false);
    }

    private void SetSimRunning(bool running)
    {
        bool was = Light;
        SimRunning = running;
        if (Light != was) LightChanged?.Invoke();
    }

    public void Save()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    public void SaveNow()
    {
        try
        {
            ConfigStore.Save(Config);
        }
        catch (Exception ex)
        {
            Notify("Couldn't save settings", ex.Message, NoticeKind.Error, important: true);
        }
    }

    private async void SaveInBackground()
    {
        try
        {
            if (ConfigStore.SaveInBackground(Config) is { } write) await write;
        }
        catch (Exception ex)
        {
            Notify("Couldn't save settings", ex.Message, NoticeKind.Error, important: true);
        }
    }

    public void Notify(string title, string message, NoticeKind kind = NoticeKind.Info, bool important = false)
    {
        if (!important && !Config.ShowNotifications && kind is NoticeKind.Info or NoticeKind.Success)
            return;
        if (IsWindowVisible())
            Notice?.Invoke(title, message, kind);
        else if (Tray is not null && (important || Config.ShowNotifications))
            Tray.ShowBalloon(title, message, kind);
    }

    public void ResetFpsStats()
    {
        Frames.Reset();
        Overlay.ResetAverages();
        Notify("FPS statistics reset", "Average FPS, 1% low and average temperatures start over from now.", NoticeKind.Success);
    }

    public void SetPerformance(SensorRate rate, int? customMs = null, BackgroundUsage? background = null)
    {
        var p = Config.Performance;
        p.Rate = rate;
        if (customMs is { } ms) p.CustomMs = Math.Clamp(ms, PerformanceSettings.MinMs, PerformanceSettings.MaxMs);
        if (background is { } b) p.Background = b;
        Save();
        Hardware.Configure(p.IntervalMs, p.Background);
        PerformanceChanged?.Invoke();
    }

    public void SetTheme(AppTheme theme)
    {
        if (theme == Theme.ThemeManager.Current.Id && Config.Ui.Theme == theme) return;
        Config.Ui.Theme = theme;
        Save();
        Theme.ThemeManager.Apply(theme);
        Overlay.InvalidateColors();
    }

    internal void SwitchThemeUnsaved(AppTheme theme) => Theme.ThemeManager.Apply(theme);

    public void UseOledColors(string? set)
    {
        var c = Config.Ui.OledCustom;
        if (set is null) c.Enabled = false;
        else
        {
            if (set.Length > 0 && c.Profile(set) is { } profile) { c.Colors = profile.Colors.Copy(); c.Name = profile.Name; }
            c.Enabled = true;
        }
        c.EditingProfile = null;
        Theme.Themes.SetOledCustom(c);
        if (Config.Ui.Theme != AppTheme.Custom) { SetTheme(AppTheme.Custom); return; }
        Save();
        Theme.ThemeManager.Apply(Config.Ui.Theme);
        Overlay.InvalidateColors();
    }

    public void OledCustomChanged()
    {
        Theme.Themes.SetOledCustom(Config.Ui.OledCustom);
        Save();
        if (Config.Ui.Theme != AppTheme.Custom) return;
        Theme.ThemeManager.Apply(Config.Ui.Theme);
        Overlay.InvalidateColors();
    }

    public void SetOverlayVisible(bool visible)
    {
        Overlay.SetVisible(visible);
        Config.Overlay.Visible = visible;
        Save();
        OverlayChanged?.Invoke();
    }

    public void OverlaySettingsChanged()
    {
        Overlay.ApplySettings();
        Save();
        OverlayChanged?.Invoke();
    }

    public bool UseOverlayLayout(string name)
    {
        var layout = Config.Overlay.Layouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (layout is null)
            return false;
        Config.Overlay.ActiveLayout = layout.Name;
        OverlaySettingsChanged();
        return true;
    }

    private const string FanConsumer = "Fan Control";

    public bool FanControlAvailable { get; private set; }

    public void SetFanControlAvailable(bool available)
    {
        FanControlAvailable = available;
        PushFanSettings();
        FansChanged?.Invoke();
    }

    private void PushFanSettings()
    {
        bool control = FanControlAvailable && Config.Fans.Enabled;
        var effective = ConfigStore.Clone(Config.Fans);
        effective.Enabled = control;
        Hardware.SetFanSettings(effective);
        if (control)
            Hardware.Acquire(FanConsumer);
        else
            Hardware.Release(FanConsumer);
    }

    public void SetFanControlEnabled(bool enabled)
    {
        Config.Fans.Enabled = enabled;
        FanSettingsChanged();
        Notify(enabled ? "Fan control on" : "Fan control off",
            enabled ? "Fans are following your curves." : "Fans are back under BIOS / driver control.", NoticeKind.Info);
    }

    public void SetFanPreset(string name)
    {
        if (Config.Fans.Presets.All(p => p.Name != name))
            return;
        Config.Fans.ActivePreset = name;
        FanSettingsChanged();
    }

    public void FanSettingsChanged()
    {
        PushFanSettings();
        Save();
        FansChanged?.Invoke();
    }

    public string FanName(FanInfo fan)
    {
        var hw = Config.Fans.Hardware.FirstOrDefault(h => h.ControlId == fan.ControlId);
        if (!string.IsNullOrWhiteSpace(hw?.CustomName))
            return hw!.CustomName!;
        string raw = fan.DefaultName.Contains(" · ") ? fan.DefaultName[(fan.DefaultName.IndexOf(" · ", StringComparison.Ordinal) + 3)..] : fan.DefaultName;
        if (fan.ControlId.Contains("gpu", StringComparison.OrdinalIgnoreCase) && !raw.Contains("GPU", StringComparison.OrdinalIgnoreCase))
            raw = "GPU " + raw;
        return raw.Replace("Fan Control #", "Fan ");
    }

    public IReadOnlyList<(string Name, float? Rpm)> VisibleFanSpeeds()
    {
        var catalog = Hardware.Catalog;
        if (catalog is null)
            return Array.Empty<(string, float?)>();
        return catalog.Fans
            .Where(f => f.HasRpm && !(Config.Fans.Hardware.FirstOrDefault(h => h.ControlId == f.ControlId)?.Hidden ?? false))
            .Select(f => (FanName(f), f.Rpm))
            .ToList();
    }

    public void FlushPendingSave()
    {
        ConfigStore.WaitForWrites();
        if (!_saveDebounce.IsEnabled)
            return;
        _saveDebounce.Stop();
        SaveNow();
    }

    public void Dispose()
    {
        _tick.Stop();
        FlushPendingSave();
        Plugins.StopAll();
        FlushPendingSave();
        Safely(() => Tray?.Dispose());
        Tray = null;
        Safely(WindowProfiles.Dispose);
        Safely(Displays.Dispose);
        Safely(Telemetry.Dispose);
        Safely(Watcher.Dispose);
        Safely(Network.Dispose);
        Safely(Overlay.Dispose);
        Safely(Frames.Dispose);
        Safely(Hardware.Dispose);
        Safely(Hotkeys.Dispose);
    }

    private static void Safely(Action step)
    {
        try { step(); }
        catch (Exception ex) { App.LogError(ex); }
    }
}

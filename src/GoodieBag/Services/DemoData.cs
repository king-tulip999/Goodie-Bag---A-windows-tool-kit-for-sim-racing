using GoodieBag.Config;

namespace GoodieBag.Services;

internal static class DemoData
{
    private const string Triple = "demo:demo-left,-2560,0,2560,1440,165,0;demo-center,0,0,2560,1440,165,0;demo-right,2560,0,2560,1440,165,0";
    private const string Single = "demo:demo-center,0,0,2560,1440,165,0";
    private const string SimPath = @"C:\Program Files (x86)\iRacing\iRacingSim64DX11.exe";
    private const string SimHubPath = @"C:\Program Files (x86)\SimHub\SimHubWPF.exe";
    private const string Stream = "demo:demo-center,0,0,2560,1440,144,0;demo-tv,2560,0,3840,2160,60,0";

    public static bool Stress { get; } = Environment.GetCommandLineArgs().Contains("--stress", StringComparer.OrdinalIgnoreCase);

    public static AppConfig Config()
    {
        var config = Build();
        if (Stress) Lengthen(config);
        return config;
    }

    internal static string Long(string name, int index) =>
        (name + " " + index + " · a very long name that fills the whole box").PadRight(Views.Ui.NameLength, 'x')[..Views.Ui.NameLength];

    private static void Lengthen(AppConfig config)
    {
        int n = 0;
        foreach (var m in config.Modes)
        {
            string to = Long(m.Name, ++n);
            ConfigReferences.Mode(config, m.Name, to);
            m.Name = to;
        }
        foreach (var p in config.Displays.Profiles)
        {
            string to = Long(p.Name, ++n);
            ConfigReferences.DisplayProfile(config, p.Name, to);
            p.Name = to;
        }
        foreach (var p in config.Fans.Presets)
        {
            string to = Long(p.Name, ++n);
            ConfigReferences.FanPreset(config, p.Name, to);
            if (string.Equals(config.Fans.ActivePreset, p.Name, StringComparison.OrdinalIgnoreCase)) config.Fans.ActivePreset = to;
            p.Name = to;
        }
        foreach (var c in config.Fans.Curves)
        {
            string to = Long(c.Name, ++n);
            ConfigReferences.FanCurve(config, c.Name, to);
            c.Name = to;
        }
        foreach (var l in config.Overlay.Layouts)
        {
            string to = Long(l.Name, ++n);
            ConfigReferences.OverlayLayout(config, l.Name, to);
            if (string.Equals(config.Overlay.ActiveLayout, l.Name, StringComparison.OrdinalIgnoreCase)) config.Overlay.ActiveLayout = to;
            l.Name = to;
        }
        foreach (var l in config.SimRacing.Hud.Layouts.Select(l => l.Name).Where(l => !RaceHudSettings.IsDefaultName(l)).ToList())
            config.SimRacing.Hud.RenameLayout(l, Long(l, ++n));
        foreach (var h in config.Fans.Hardware)
            h.CustomName = h.CustomName is null ? null : Long(h.CustomName, ++n);
    }

    private static AppConfig Build()
    {
        var config = new AppConfig
        {
            SimRacing = new SimRacingSettings
            {
                Sim = "iRacing",
                Setups = { SelectedCar = "porsche963gtp", IntroSeen = true },
                Compare =
                {
                    new FovSetup { Name = "Before seat move", DistanceCm = 78 },
                    new FovSetup { Name = "32\" upgrade idea", Centre = new FovScreen { DiagonalIn = 32 }, DistanceCm = 80 },
                },
            },
            Modes =
            {
                new Mode
                {
                    Name = "Sim Racing", Hotkey = "Ctrl+Alt+3", Overlay = OverlayMode.Show, FanPreset = "Race",
                    DisplayProfile = "Triples",
                    Windows =
                    {
                        new WindowRule { Name = "RaceRoom on triples", ProcessName = "RRRE64.exe", DelayMs = 4000, Width = 7680, Height = 1440, X = -2560, Y = 0, RemoveBorders = true },
                        new WindowRule { Name = "Forza Horizon 4", ProcessName = "ForzaHorizon4.exe", DelayMs = 8000, Width = 7680, Height = 1440, X = -2560, Y = 0, RemoveBorders = true, HideTitleBar = true },
                        new WindowRule { Name = "SimHub dash", ProcessName = "SimHubWPF.exe", Width = 1280, Height = 480, X = 2560, Y = 960 },
                    },
                    Audio = new AudioSnapshot { PlaybackId = "demo-headset", PlaybackName = "Headset (Arctis Nova Pro)", RecordingId = "demo-mic", RecordingName = "Microphone (Arctis Nova Pro)" },
                    Program = new ModeProgram { Path = SimPath },
                    Launch = { new LaunchItem { Path = SimHubPath }, new LaunchItem { Path = @"C:\Program Files (x86)\Britton IT Ltd\CrewChiefV4\CrewChiefV4.exe" } },
                    CloseLaunchedOnExit = true,
                    Priority = AutoPriority.High,
                    Triggers = { new AutomationRule { Trigger = TriggerType.WhileRunning, App = "iracingsim64dx11", Name = "iRacing", CooldownSeconds = 0 } },
                },
                new Mode
                {
                    Name = "Desk", Hotkey = "Ctrl+Alt+1", Overlay = OverlayMode.Hide, FanPreset = "Quiet",
                    DisplayProfile = "Desk",
                    Windows = { new WindowRule { Name = "Crew Chief bottom left", ProcessName = "CrewChiefV4.exe", X = 0, Y = 1000 } },
                    Audio = new AudioSnapshot { PlaybackId = "demo-speakers", PlaybackName = "Speakers (Realtek Audio)", RecordingId = "demo-mic2", RecordingName = "Microphone (Shure MV7)" },
                    Triggers = { new AutomationRule { Trigger = TriggerType.WindowsStartup, CooldownSeconds = 0 } },
                },
                new Mode
                {
                    Name = "Forza Horizon 5", Overlay = OverlayMode.Show, FanPreset = "Quiet", DisplayProfile = "Desk",
                    Program = new ModeProgram { Path = "steam://rungameid/1551360", ProcessName = "forzahorizon5" },
                    Triggers = { new AutomationRule { Trigger = TriggerType.WhileRunning, App = "forzahorizon5", Name = "Forza Horizon 5", CooldownSeconds = 0 } },
                },
                new Mode
                {
                    Name = "Streaming", DisplayProfile = "Desk + TV",
                    Triggers = { new AutomationRule { Trigger = TriggerType.WhileRunning, App = "obs64", Name = "OBS Studio", CooldownSeconds = 0, Enabled = false } },
                },
                new Mode
                {
                    Name = "League night", OneOff = true, Launch = { new LaunchItem { Path = SimHubPath } }, Message = "League race at 20:00: practice is open.",
                    Triggers = { new AutomationRule { Trigger = TriggerType.Weekly, Days = { DayOfWeek.Tuesday }, AtMinute = 19 * 60 + 15, OnlyIfRunning = "steam" } },
                },
                new Mode
                {
                    Name = "Cool the GPU down", OneOff = true, FanPreset = "Race",
                    Triggers = { new AutomationRule { Trigger = TriggerType.GpuTempAbove, Threshold = 85 } },
                },
                new Mode
                {
                    Name = "Quiet when away", OneOff = true, FanPreset = "Quiet", Overlay = OverlayMode.Hide,
                    Triggers = { new AutomationRule { Trigger = TriggerType.Idle, Threshold = 15 } },
                },
            },
            Overlay = new OverlaySettings { Visible = true, Corner = OverlayCorner.TopLeft },
            Displays = new DisplaySettings
            {
                Profiles =
                {
                    new DisplayProfile { Name = "Triples", Hotkey = "Ctrl+Alt+F3", Taskbar = TaskbarMode.MainOnly, Hdr = HdrMode.Off, Volume = 70,
                        Audio = new AudioSnapshot { PlaybackId = "demo-speakers", PlaybackName = "Speakers (Realtek Audio)", RecordingId = "demo-mic2", RecordingName = "Microphone (Shure MV7)" },
                        Layout = Snapshot(Triple, ("LG 27GP850", "demo-center"), ("Dell S2721DGF", "demo-left"), ("Dell S2721DGF", "demo-right")) },
                    new DisplayProfile { Name = "Desk", Hotkey = "Ctrl+Alt+F1", Layout = Snapshot(Single, ("LG 27GP850", "demo-center")),
                        Audio = new AudioSnapshot { PlaybackId = "demo-headset", PlaybackName = "Headset (Arctis Nova Pro)", RecordingId = "demo-mic", RecordingName = "Microphone (Arctis Nova Pro)" },
                        Wallpapers = { ["demo-center"] = @"C:\Users\Public\Pictures\desk.jpg" } },
                    new DisplayProfile { Name = "Desk + TV", Hdr = HdrMode.On, Layout = Snapshot(Stream, ("LG 27GP850", "demo-center"), ("LG TV SSCR2", "demo-tv")) },
                },
            },
        };
        config.Overlay.Normalize();

        var fans = config.Fans;
        fans.Enabled = true;
        fans.Curves.Add(new FanCurve { Name = "CPU", SensorId = "/amdcpu/0/temperature/2", SensorName = "Core (Tctl/Tdie)" });
        fans.Curves.Add(new FanCurve
        {
            Name = "GPU", SensorId = "/gpu-amd/0/temperature/0", SensorName = "GPU Core",
            Points = { new() { Temp = 40, Percent = 30 }, new() { Temp = 60, Percent = 45 }, new() { Temp = 75, Percent = 70 }, new() { Temp = 88, Percent = 100 } },
        });
        fans.Curves[1].Points.RemoveRange(0, 4);
        fans.Curves.Add(new FanCurve { Name = "Case", Type = FanCurveType.Mix, MixOf = { "CPU", "GPU" } });
        fans.Curves.Add(new FanCurve { Name = "Pump", Type = FanCurveType.Flat, FlatPercent = 70 });

        fans.Hardware.AddRange(new[]
        {
            new FanHardware { ControlId = "/lpc/nct6798d/control/1", CustomName = "CPU Fan" },
            new FanHardware { ControlId = "/lpc/nct6798d/control/0", CustomName = "AIO Pump", IsPump = true, MinPercent = 60 },
            new FanHardware { ControlId = "/gpu-amd/0/control/0", CustomName = "GPU Fans" },
            new FanHardware
            {
                ControlId = "/lpc/nct6798d/control/2", CustomName = "Front Intake", MinPercent = 0, AllowStop = true,
                Calibration = new FanCalibration
                {
                    Date = new DateTime(2026, 10, 3), MaxRpm = 1500, MinStablePercent = 20, StopPercent = 15, StartPercent = 25,
                    Points = { new() { Percent = 100, Rpm = 1500 }, new() { Percent = 80, Rpm = 1270 }, new() { Percent = 60, Rpm = 1030 },
                               new() { Percent = 40, Rpm = 800 }, new() { Percent = 25, Rpm = 610 }, new() { Percent = 20, Rpm = 540 }, new() { Percent = 15, Rpm = 0 } },
                },
            },
            new FanHardware { ControlId = "/lpc/nct6798d/control/3", CustomName = "Rear Exhaust" },
            new FanHardware { ControlId = "/lpc/nct6798d/control/4", Hidden = true },
            new FanHardware { ControlId = "/lpc/nct6798d/control/6", Hidden = true },
            new FanHardware { ControlId = "/lpc/nct6798d/fan/5", Hidden = true },
        });

        FanPreset Preset(string name, int manualCase) => new()
        {
            Name = name,
            Fans =
            {
                new() { ControlId = "/lpc/nct6798d/control/1", Mode = FanMode.Curve, CurveName = "CPU" },
                new() { ControlId = "/lpc/nct6798d/control/0", Mode = FanMode.Curve, CurveName = "Pump" },
                new() { ControlId = "/gpu-amd/0/control/0", Mode = FanMode.Curve, CurveName = "GPU" },
                new() { ControlId = "/lpc/nct6798d/control/2", Mode = manualCase > 0 ? FanMode.Manual : FanMode.Curve, CurveName = "Case", ManualPercent = manualCase },
                new() { ControlId = "/lpc/nct6798d/control/3", Mode = manualCase > 0 ? FanMode.Manual : FanMode.Curve, CurveName = "Case", ManualPercent = manualCase },
            },
        };
        fans.Presets.Add(Preset("Quiet", 30));
        fans.Presets.Add(Preset("Default", 0));
        fans.Presets.Add(Preset("Race", 85));
        fans.ActivePreset = "Default";
        fans.Normalize();
        var hud = config.SimRacing.Hud;
        hud.Normalize(SimRacing.Hud.HudPresets.DefaultLayouts, SimRacing.Hud.HudPresets.DefaultRules);
        hud.Enabled = true;
        hud.Remember("GT3", "Ferrari 296 GT3", "Circuit de Spa-Francorchamps");
        hud.Remember("GTP", "Porsche 963 GTP", "Daytona International Speedway");
        hud.Remember("GT3", "BMW M4 GT3", "Autodromo Nazionale Monza");
        config.Plugins[Grr.GrrPlugin.PluginId] = true;
        config.Ui.MonitorHiddenSections.Clear();
        config.Ui.MonitorSectionsVersion = 1;
        return config;
    }

    private static DisplaySnapshot Snapshot(string paths, params (string Name, string Key)[] monitors) => new()
    {
        Paths = paths,
        Summary = monitors.Select(m => $"{m.Name} · 2560×1440 · 165 Hz").ToList(),
        TargetDevicePaths = paths["demo:".Length..].Split(';').Select(p => p.Split(',')[0]).ToList(),
    };
}

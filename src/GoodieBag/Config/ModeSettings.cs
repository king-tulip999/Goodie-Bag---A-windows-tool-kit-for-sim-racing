namespace GoodieBag.Config;

public enum HdrMode { Unchanged, On, Off }

public enum OverlayMode { Unchanged, Show, Hide, Toggle }

public enum ModeKind { Custom, Desktop, Gaming, SimRacing, Work, Streaming, LaptopOnly, Dual, Triple }

public enum AutoPriority { Low, Normal, High }

public sealed class Mode
{
    public string Name { get; set; } = "";

    public ModeKind Kind { get; set; } = ModeKind.Custom;

    public string? Hotkey { get; set; }

    public AutoPriority Priority { get; set; } = AutoPriority.Normal;

    public bool OneOff { get; set; }

    public List<AutomationRule> Triggers { get; set; } = new();

    public string? Message { get; set; }

    public string? DisplayProfile { get; set; }

    public List<WindowRule> Windows { get; set; } = new();

    public string? WindowProfile { get; set; }

    public DisplaySnapshot? Display { get; set; }
    public HdrMode? Hdr { get; set; }

    public AudioSnapshot? Audio { get; set; }

    public int? Volume { get; set; }

    public OverlayMode Overlay { get; set; } = OverlayMode.Unchanged;

    public string? OverlayLayout { get; set; }

    public string? FanPreset { get; set; }

    public List<LaunchItem> Launch { get; set; } = new();

    public List<string> Close { get; set; } = new();

    public bool ForceClose { get; set; }

    public ModeProgram? Program { get; set; }

    public bool CloseLaunchedOnExit { get; set; }
}

public sealed class WindowRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    public string ProcessName { get; set; } = "";

    public int DelayMs { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }

    public int X { get; set; }
    public int Y { get; set; }

    public bool RemoveBorders { get; set; }

    public bool HideTitleBar { get; set; }

    public string Label => Name.Length > 0 ? Name : ProcessName;

    public WindowRule Copy() => (WindowRule)MemberwiseClone();
}

public sealed class DisplaySnapshot
{
    public List<string> Summary { get; set; } = new();

    public List<string> TargetDevicePaths { get; set; } = new();

    public string Paths { get; set; } = "";

    public string Modes { get; set; } = "";
}

public sealed class AudioSnapshot
{
    public string? PlaybackId { get; set; }
    public string? PlaybackName { get; set; }
    public string? RecordingId { get; set; }
    public string? RecordingName { get; set; }

    public bool SetCommunications { get; set; } = true;
}

public sealed class LaunchItem
{
    public string Path { get; set; } = "";
    public string? Arguments { get; set; }

    public bool AsAdmin { get; set; }
}

public sealed class ModeProgram
{
    public string Path { get; set; } = "";
    public string? Arguments { get; set; }
    public bool AsAdmin { get; set; }

    public string? IconPath { get; set; }

    public string? ProcessName { get; set; }

    public ModeProgram Copy() => (ModeProgram)MemberwiseClone();
}

public sealed class ModeOverrides
{
    public string? DisplayProfile { get; set; }
    public HdrMode? Hdr { get; set; }

    public string? DisplayFrom { get; set; }
    public AudioSnapshot? Audio { get; set; }
    public int? Volume { get; set; }
    public OverlayMode? Overlay { get; set; }
    public string? OverlayLayout { get; set; }
    public string? FanPreset { get; set; }

    public bool IsEmpty => DisplayProfile is null && Hdr is null && Audio is null && Volume is null && Overlay is null
                           && OverlayLayout is null && FanPreset is null;
}

public sealed class AppProfile
{
    public string Process { get; set; } = "";

    public string? Name { get; set; }

    public string? Path { get; set; }

    public string? ModeName { get; set; }

    public bool Enabled { get; set; } = true;

    public ModeOverrides Overrides { get; set; } = new();

    public string Label => string.IsNullOrWhiteSpace(Name) ? Process + ".exe" : Name!;
}

public sealed class ModeSwitchingSettings
{
    public bool Enabled { get; set; } = true;

    public bool RestorePrevious { get; set; } = true;

    public string? StartupMode { get; set; }
}

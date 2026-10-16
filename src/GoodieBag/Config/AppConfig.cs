using System.Text.Json.Serialization;

namespace GoodieBag.Config;

public sealed class AppConfig
{
    public bool ShowNotifications { get; set; } = true;

    [JsonPropertyName("profiles")]
    public List<Mode> Modes { get; set; } = new();

    public List<AppProfile>? Apps { get; set; }

    public ModeSwitchingSettings ModeSwitching { get; set; } = new();

    public AutomationSettings? Automations { get; set; }

    public LauncherSettings? Launcher { get; set; }

    public NetworkSettings Network { get; set; } = new();

    public DisplaySettings Displays { get; set; } = new();

    public LegacyWindowsmith? Windowsmith { get; set; }

    public OverlaySettings Overlay { get; set; } = new();

    public FanSettings Fans { get; set; } = new();

    public UiSettings Ui { get; set; } = new();

    public Dictionary<string, bool> Plugins { get; set; } = new();

    public SimRacingSettings SimRacing { get; set; } = new();

    public SessionStatsSettings SessionStats { get; set; } = new();

    public UpdateSettings Updates { get; set; } = new();

    public PerformanceSettings Performance { get; set; } = new();

    public ActionHotkeys Hotkeys { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }

    private readonly Dictionary<string, object> _sections = new(StringComparer.OrdinalIgnoreCase);

    public T Section<T>(string key) where T : class, new()
    {
        lock (_sections)
        {
            if (_sections.TryGetValue(key, out var live))
                return (T)live;
            T? value = null;
            if (Extra?.TryGetValue(key, out var stored) == true)
            {
                try { value = System.Text.Json.JsonSerializer.Deserialize<T>(stored, ConfigStore.JsonOptions); }
                catch (System.Text.Json.JsonException) { Extra![key + ".unreadable"] = stored; }
            }
            value ??= new T();
            _sections[key] = value;
            return value;
        }
    }

    internal void StoreSections()
    {
        lock (_sections)
        {
            foreach (var (key, value) in _sections)
            {
                Extra ??= new();
                Extra[key] = System.Text.Json.JsonSerializer.SerializeToElement(value, value.GetType(), ConfigStore.JsonOptions);
            }
        }
    }
}

public sealed class ActionHotkeys
{
    public string? ShowWindow { get; set; }
    public string? NextMode { get; set; }
    public string? PreviousMode { get; set; }
    public string? NextDisplayProfile { get; set; }
    public string? PreviousDisplayProfile { get; set; }
    public string? FanControl { get; set; }
    public string? NextFanPreset { get; set; }
    public string? PreviousFanPreset { get; set; }
    public string? NextOverlayLayout { get; set; }
}

public enum UpdateCheck { OnStart, Daily, Weekly, Manual }

public sealed class UpdateSettings
{
    public UpdateCheck Check { get; set; } = UpdateCheck.Daily;
    public DateTime? LastCheckUtc { get; set; }

    public string? NotifiedVersion { get; set; }
}

public enum PanelSize { Small, Medium, Large }

[JsonConverter(typeof(AppThemeConverter))]
public readonly struct AppTheme : IEquatable<AppTheme>
{
    private readonly string? _id;

    public AppTheme(string id) => _id = string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    public string Id => _id ?? "Light";

    public static readonly AppTheme Light = new("Light");
    public static readonly AppTheme Dark = new("Dark");
    public static readonly AppTheme Custom = new("CustomTheme");
    public static readonly AppTheme OledPastel = new("OledPastel");

    internal static readonly AppTheme[] Numbered = { Light, Dark, Light, Dark, Custom };

    public bool Equals(AppTheme other) => string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is AppTheme other && Equals(other);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
    public static bool operator ==(AppTheme a, AppTheme b) => a.Equals(b);
    public static bool operator !=(AppTheme a, AppTheme b) => !a.Equals(b);
    public override string ToString() => Id;

    public static AppTheme Named(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        null or "" or "light" or "softlight" or "light2048" or "vanillalight" => Light,
        "dark" or "vanilladark" or "dark2048" or "softdark" => Dark,
        "oledneon" or "customtheme" => Custom,
        _ => new AppTheme(name),
    };
}

internal sealed class AppThemeConverter : JsonConverter<AppTheme>
{
    public override AppTheme Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case System.Text.Json.JsonTokenType.String:
                return AppTheme.Named(reader.GetString());
            case System.Text.Json.JsonTokenType.Number:
                return reader.TryGetInt32(out int i) && i >= 0 && i < AppTheme.Numbered.Length ? AppTheme.Numbered[i] : AppTheme.Light;
            default:
                reader.Skip();
                return AppTheme.Light;
        }
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, AppTheme value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Id);
}

public sealed class PanelSlot
{
    public string Id { get; set; } = "";

    public int? Col { get; set; }
    public int? Row { get; set; }

    public int? Cols { get; set; }

    public int? Rows { get; set; }

    public static void MigrateToOrder(List<PanelSlot> slots)
    {
        if (slots.All(s => s.Col is null && s.Row is null)) return;
        var ordered = slots.OrderBy(s => s.Row ?? int.MaxValue).ThenBy(s => s.Col ?? int.MaxValue).ToList();
        slots.Clear();
        slots.AddRange(ordered);
        foreach (var s in slots) s.Col = s.Row = null;
    }
}

public sealed class UiSettings
{
    public bool CloseToTray { get; set; } = true;

    public string StartPage { get; set; } = "plugins";

    public List<PanelSlot> LandingLayout { get; set; } = new();

    public int LandingVersion { get; set; }

    public bool AlwaysOnTop { get; set; }

    public WindowBounds? Window { get; set; }

    public bool StartMinimized { get; set; }

    public bool Animations { get; set; } = true;

    public bool TopoBackground { get; set; } = true;

    public string? TopoColor { get; set; }

    public bool TrayTipShown { get; set; }

    public bool HidePawnIoPrompt { get; set; }

    public bool SetupPending { get; set; }

    public AppTheme Theme { get; set; }

    public CustomThemeSettings OledCustom { get; set; } = new();

    public string? PastelPreset { get; set; }

    public bool? DarkMode { get; set; }

    public int InterfaceScale { get; set; } = 100;

    public int InterfaceScaleVersion { get; set; }

    public static readonly int[] InterfaceScales = { 90, 100, 110 };

    public const double BaseScale = 0.81;

    internal static int MigrateScale(int old) => new[] { 80, 90, 100 }.OrderBy(o => Math.Abs(o - old)).First() switch { 80 => 90, 90 => 100, _ => 110 };

    [System.Text.Json.Serialization.JsonIgnore]
    public double ScaleFactor => InterfaceScales.OrderBy(o => Math.Abs(o - InterfaceScale)).First() / 100.0 * BaseScale;

    public List<string> NavOrder { get; set; } = new();

    public int? PerformanceIntervalMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SearchKey { get; set; } = "Ctrl+F";

    public bool KeybindsOpen { get; set; } = true;

    public List<string> KeybindsFolded { get; set; } = new();

    public List<string> KeybindOrder { get; set; } = new();

    public List<string> MonitorCollapsed { get; set; } = new();

    public List<string> MonitorSectionOrder { get; set; } = new();

    public List<string> MonitorHiddenSections { get; set; } = new();

    public List<PanelSlot> SensorsLayout { get; set; } = new();

    public List<string> HiddenSensorDevices { get; set; } = new();
}

public sealed class FanSettings
{
    public bool Enabled { get; set; }

    public int EmergencyTempC { get; set; } = 95;

    public List<FanCurve> Curves { get; set; } = new();

    public List<FanHardware> Hardware { get; set; } = new();

    public List<FanPreset> Presets { get; set; } = new();

    public string? ActivePreset { get; set; }

    public List<PanelSlot> Layout { get; set; } = new();

    public List<LegacyFanAssignment>? Fans { get; set; }

    public FanPreset? Active => Presets.FirstOrDefault(p => p.Name == ActivePreset) ?? Presets.FirstOrDefault();

    public FanHardware HardwareFor(string controlId)
    {
        var hw = Hardware.FirstOrDefault(h => h.ControlId == controlId);
        if (hw is null)
        {
            hw = new FanHardware { ControlId = controlId };
            Hardware.Add(hw);
        }
        return hw;
    }

    public void ResetFan(string controlId)
    {
        int i = Hardware.FindIndex(h => h.ControlId == controlId);
        if (i >= 0)
        {
            var old = Hardware[i];
            Hardware[i] = new FanHardware { ControlId = controlId, Calibration = old.Calibration, IsPump = old.IsPump };
        }
        foreach (var preset in Presets)
            preset.Fans.RemoveAll(e => e.ControlId == controlId);
    }

    public void ResetAllFans()
    {
        var ids = Hardware.Select(h => h.ControlId).Concat(Presets.SelectMany(p => p.Fans).Select(e => e.ControlId)).Distinct().ToList();
        foreach (var id in ids)
            ResetFan(id);
    }

    public const string DefaultPresetName = "Default", OldDefaultPresetName = "Balanced";

    public int PresetNamesVersion { get; set; }

    public void Normalize()
    {
        if (Fans is { Count: > 0 })
        {
            var preset = new FanPreset { Name = DefaultPresetName };
            foreach (var old in Fans)
            {
                var hw = HardwareFor(old.ControlId);
                hw.CustomName ??= old.CustomName;
                hw.MinPercent = old.MinPercent;
                preset.Fans.Add(new FanPresetEntry { ControlId = old.ControlId, Mode = old.Mode, CurveName = old.CurveName, ManualPercent = old.ManualPercent });
            }
            Presets.Insert(0, preset);
            ActivePreset ??= preset.Name;
        }
        Fans = null;

        if (Presets.Count == 0)
            Presets.Add(new FanPreset { Name = DefaultPresetName });
        if (Active is null || ActivePreset != Active.Name)
            ActivePreset = Presets[0].Name;
    }
}

public sealed class FanPreset
{
    public string Name { get; set; } = "";
    public string? Hotkey { get; set; }
    public List<FanPresetEntry> Fans { get; set; } = new();

    public FanPresetEntry EntryFor(string controlId)
    {
        var e = Fans.FirstOrDefault(f => f.ControlId == controlId);
        if (e is null)
        {
            e = new FanPresetEntry { ControlId = controlId };
            Fans.Add(e);
        }
        return e;
    }
}

public enum FanMode { Auto, Curve, Manual }

public sealed class FanPresetEntry
{
    public string ControlId { get; set; } = "";
    public FanMode Mode { get; set; } = FanMode.Auto;
    public string? CurveName { get; set; }
    public int ManualPercent { get; set; } = 50;
}

public sealed class FanHardware
{
    public string ControlId { get; set; } = "";
    public string? CustomName { get; set; }

    public bool Hidden { get; set; }

    public int MinPercent { get; set; } = 20;

    public int MaxPercent { get; set; } = 100;

    public bool AllowStop { get; set; }

    public bool IsPump { get; set; }

    public int? StartPercent { get; set; }

    public int? StopPercent { get; set; }

    public int StepUpPerSecond { get; set; }
    public int StepDownPerSecond { get; set; }

    public FanCalibration? Calibration { get; set; }
}

public sealed class FanCalibration
{
    public DateTime Date { get; set; }

    public bool Manual { get; set; }

    public List<CalibrationPoint> Points { get; set; } = new();

    public int MaxRpm { get; set; }

    public int MinStablePercent { get; set; }

    public int? StopPercent { get; set; }

    public int? StartPercent { get; set; }

    public bool RespondsToControl { get; set; } = true;

    public bool StableReadings { get; set; } = true;
}

public sealed class CalibrationPoint
{
    public int Percent { get; set; }
    public int Rpm { get; set; }
}

public enum FanCurveType { Graph, Flat, Mix }

public enum MixFunction { Max, Min, Average, Sum, Subtract }

public sealed class FanCurve
{
    public string Name { get; set; } = "";
    public FanCurveType Type { get; set; } = FanCurveType.Graph;

    public string? SensorId { get; set; }

    public string? SensorName { get; set; }

    public List<CurvePoint> Points { get; set; } = new()
    {
        new() { Temp = 30, Percent = 25 },
        new() { Temp = 50, Percent = 35 },
        new() { Temp = 70, Percent = 60 },
        new() { Temp = 85, Percent = 100 },
    };

    public int HysteresisC { get; set; } = 2;

    public int StepUpPerSecond { get; set; } = 10;
    public int StepDownPerSecond { get; set; } = 4;

    public int FlatPercent { get; set; } = 50;

    public List<string> MixOf { get; set; } = new();

    public MixFunction Function { get; set; } = MixFunction.Max;
}

public sealed class CurvePoint
{
    public float Temp { get; set; }
    public float Percent { get; set; }
}

public sealed class LegacyFanAssignment
{
    public string ControlId { get; set; } = "";
    public string? CustomName { get; set; }
    public FanMode Mode { get; set; } = FanMode.Auto;
    public string? CurveName { get; set; }
    public int ManualPercent { get; set; } = 50;
    public int MinPercent { get; set; } = 20;
}

public sealed class WindowBounds
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

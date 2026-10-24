namespace GoodieBag.Config;

public enum OverlayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

public enum OverlayMetric
{
    Fps, AverageFps, OnePercentLow, FrameTime,
    GpuTemp, AverageGpuTemp, GpuLoad, GpuHotSpot, GpuClock, GpuPower, GpuFan, Vram,
    CpuTemp, AverageCpuTemp, CpuLoad, CpuClock, CpuPower,
    Ram, Ping, Network, Fans,
}

public enum OverlayStyle { Expanded, Compact }

[System.Text.Json.Serialization.JsonConverter(typeof(OverlayColorPresetConverter))]
public readonly struct OverlayColorPreset : IEquatable<OverlayColorPreset>
{
    private readonly string? _id;

    public OverlayColorPreset(string id) => _id = string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    public string Id => _id ?? AppTheme.Light.Id;

    public static readonly OverlayColorPreset Custom = new("Custom");
    public static readonly OverlayColorPreset App = new("FollowApp");
    public static readonly OverlayColorPreset Light = For(AppTheme.Light);
    public static readonly OverlayColorPreset Dark = For(AppTheme.Dark);
    public static readonly OverlayColorPreset CustomTheme = For(AppTheme.Custom);

    public bool IsCustom => Equals(Custom);

    public bool IsApp => Equals(App);

    public AppTheme? Theme => IsCustom || IsApp ? null : new AppTheme(Id);

    public static OverlayColorPreset For(AppTheme theme) => new(theme.Id);

    public static OverlayColorPreset Named(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        null or "" or "app" => Light,
        "dark" or "default" => Dark,
        "light" => Light,
        "highcontrast" or "oledpastel" => CustomTheme,
        "custom" => Custom,
        "followapp" => App,
        _ => For(AppTheme.Named(name)),
    };

    public bool Equals(OverlayColorPreset other) => string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is OverlayColorPreset other && Equals(other);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
    public static bool operator ==(OverlayColorPreset a, OverlayColorPreset b) => a.Equals(b);
    public static bool operator !=(OverlayColorPreset a, OverlayColorPreset b) => !a.Equals(b);
    public override string ToString() => Id;
}

public sealed class OverlayColors
{
    public string Text { get; set; } = "#3D3026";
    public string Secondary { get; set; } = "#6A5848";
    public string Accent { get; set; } = "#A8325E";
    public string Background { get; set; } = "#ECE2D0";
    public string Graph { get; set; } = "#CB7196";
    public string Warning { get; set; } = "#C08417";
    public string Critical { get; set; } = "#C4342B";
    public string Fps { get; set; } = "#466B28";
}

public sealed class OverlayMetricEntry
{
    public OverlayMetric Metric { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class OverlayLayout
{
    public const string DefaultName = "Default";

    public string Name { get; set; } = DefaultName;

    public bool IsDefault => string.Equals(Name, DefaultName, StringComparison.OrdinalIgnoreCase);

    public List<OverlayMetricEntry> Metrics { get; set; } = DefaultMetrics();

    public OverlayStyle Style { get; set; } = OverlayStyle.Expanded;

    public OverlayCorner Corner { get; set; } = OverlayCorner.TopLeft;

    public string Monitor { get; set; } = "primary";

    public int OffsetX { get; set; } = 16;
    public int OffsetY { get; set; } = 16;

    public int FontScale { get; set; } = 100;

    public int BackgroundOpacity { get; set; } = 72;

    public Dictionary<string, string> GroupNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<OverlayMetric, string> MetricTags { get; set; } = new();

    public string GroupName(string group) =>
        GroupNames.TryGetValue(group, out var own) && !string.IsNullOrWhiteSpace(own) ? own.Trim() : group;

    public string MetricTag(OverlayMetric metric, string standard) =>
        MetricTags.TryGetValue(metric, out var own) ? own.Trim() : standard;

    public bool ShowGraph { get; set; }

    [System.Text.Json.Serialization.JsonConverter(typeof(OverlayColorPresetConverter))]
    public OverlayColorPreset Colors { get; set; } = OverlayColorPreset.App;

    public string? ColorSet { get; set; }

    public OverlayColors Custom { get; set; } = new();

    public bool Shows(OverlayMetric metric) => Metrics.Any(m => m.Metric == metric && m.Enabled);

    public static string GroupOf(OverlayMetric m) => m switch
    {
        OverlayMetric.Fps or OverlayMetric.AverageFps or OverlayMetric.OnePercentLow or OverlayMetric.FrameTime => "FPS",
        OverlayMetric.GpuTemp or OverlayMetric.AverageGpuTemp or OverlayMetric.GpuLoad or OverlayMetric.GpuHotSpot or OverlayMetric.GpuClock or OverlayMetric.GpuPower or OverlayMetric.GpuFan => "GPU",
        OverlayMetric.Vram => "VRAM",
        OverlayMetric.CpuTemp or OverlayMetric.AverageCpuTemp or OverlayMetric.CpuLoad or OverlayMetric.CpuClock or OverlayMetric.CpuPower => "CPU",
        OverlayMetric.Ram => "RAM",
        OverlayMetric.Ping or OverlayMetric.Network => "NET",
        _ => "FAN",
    };

    public List<string> GroupOrder() => Metrics.Select(m => GroupOf(m.Metric)).Distinct().ToList();

    public void MoveGroup(string group, int index)
    {
        var order = GroupOrder();
        if (!order.Remove(group)) return;
        order.Insert(Math.Clamp(index, 0, order.Count), group);
        Metrics = order.SelectMany(g => Metrics.Where(m => GroupOf(m.Metric) == g)).ToList();
    }

    public void MoveWithinGroup(OverlayMetric metric, int index)
    {
        string group = GroupOf(metric);
        var members = Metrics.Where(m => GroupOf(m.Metric) == group).ToList();
        var entry = members.First(m => m.Metric == metric);
        members.Remove(entry);
        members.Insert(Math.Clamp(index, 0, members.Count), entry);
        var order = GroupOrder();
        Metrics = order.SelectMany(g => g == group ? members : Metrics.Where(m => GroupOf(m.Metric) == g)).ToList();
    }

    public static List<OverlayMetricEntry> DefaultMetrics()
    {
        var on = new[]
        {
            OverlayMetric.Fps, OverlayMetric.AverageFps, OverlayMetric.OnePercentLow, OverlayMetric.FrameTime,
            OverlayMetric.GpuTemp, OverlayMetric.GpuLoad, OverlayMetric.GpuClock, OverlayMetric.GpuPower, OverlayMetric.Vram,
            OverlayMetric.CpuTemp, OverlayMetric.CpuLoad, OverlayMetric.CpuClock, OverlayMetric.CpuPower, OverlayMetric.Ram,
        };
        return on.Select(m => new OverlayMetricEntry { Metric = m })
            .Concat(Enum.GetValues<OverlayMetric>().Except(on).Select(m => new OverlayMetricEntry { Metric = m, Enabled = false }))
            .ToList();
    }

    public void Complete()
    {
        Metrics = Metrics.GroupBy(m => m.Metric).Select(g => g.First()).ToList();
        foreach (var missing in Enum.GetValues<OverlayMetric>().Where(m => Metrics.All(e => e.Metric != m)))
            Metrics.Add(new OverlayMetricEntry { Metric = missing, Enabled = false });
        Metrics = GroupOrder().SelectMany(g => Metrics.Where(m => GroupOf(m.Metric) == g)).ToList();
        FontScale = Math.Clamp(FontScale, 60, 200);
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0, 100);
        GroupNames = new Dictionary<string, string>((GroupNames ?? new()).Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key, p => p.Value.Trim().Length > 24 ? p.Value.Trim()[..24] : p.Value.Trim()), StringComparer.OrdinalIgnoreCase);
        MetricTags = (MetricTags ?? new()).Where(p => Enum.IsDefined(p.Key)).ToDictionary(p => p.Key, p => (p.Value ?? "").Trim().Length > 16 ? p.Value!.Trim()[..16] : (p.Value ?? "").Trim());
        Colors = OverlayColorPreset.Named(Colors.Id);
    }

    public static OverlayColorPreset For(AppTheme theme) => OverlayColorPreset.For(theme);
}

public sealed class OverlaySettings
{
    public bool Visible { get; set; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? Hotkey { get; set; } = "Ctrl+Alt+O";

    public string? ResetFpsHotkey { get; set; }

    public int UpdateIntervalMs { get; set; } = 500;

    public string ActiveLayout { get; set; } = "Default";

    public List<OverlayLayout> Layouts { get; set; } = new();

    public int ColorsVersion { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public OverlayLayout Current => Layouts.FirstOrDefault(l => l.Name == ActiveLayout) ?? Layouts[0];

    public OverlayCorner? Corner { get; set; }
    public string? Monitor { get; set; }
    public int? OffsetX { get; set; }
    public int? OffsetY { get; set; }
    public string? Size { get; set; }
    public int? BackgroundOpacity { get; set; }
    public string? Palette { get; set; }
    public string? Background { get; set; }
    public bool? ShowGpu { get; set; }
    public bool? ShowVram { get; set; }
    public bool? ShowCpu { get; set; }
    public bool? ShowRam { get; set; }
    public bool? ShowFps { get; set; }
    public bool? ShowFrameTime { get; set; }
    public bool? ShowClocks { get; set; }
    public bool? ShowPower { get; set; }
    public bool? ShowFans { get; set; }

    public void Normalize()
    {
        if (Layouts.Count == 0)
            Layouts.Add(ShowGpu is null ? new OverlayLayout() : Migrated());
        if (!Layouts.Any(l => l.IsDefault))
            Layouts.Insert(0, new OverlayLayout());
        foreach (var layout in Layouts)
            layout.Complete();
        if (Layouts.All(l => l.Name != ActiveLayout))
            ActiveLayout = Layouts[0].Name;

        Corner = null; Monitor = null; OffsetX = OffsetY = null; Size = null; BackgroundOpacity = null; Palette = Background = null;
        ShowGpu = ShowVram = ShowCpu = ShowRam = ShowFps = ShowFrameTime = ShowClocks = ShowPower = ShowFans = null;
    }

    private OverlayLayout Migrated()
    {
        var order = new List<(OverlayMetric Metric, bool On)>
        {
            (OverlayMetric.GpuTemp, ShowGpu == true), (OverlayMetric.GpuLoad, ShowGpu == true),
            (OverlayMetric.GpuClock, ShowGpu == true && ShowClocks == true), (OverlayMetric.GpuPower, ShowGpu == true && ShowPower == true),
            (OverlayMetric.Vram, ShowVram == true),
            (OverlayMetric.CpuTemp, ShowCpu == true), (OverlayMetric.CpuLoad, ShowCpu == true),
            (OverlayMetric.CpuClock, ShowCpu == true && ShowClocks == true), (OverlayMetric.CpuPower, ShowCpu == true && ShowPower == true),
            (OverlayMetric.Ram, ShowRam == true),
            (OverlayMetric.Fps, ShowFps == true), (OverlayMetric.AverageFps, ShowFps == true), (OverlayMetric.OnePercentLow, ShowFps == true),
            (OverlayMetric.FrameTime, ShowFps == true && ShowFrameTime == true),
            (OverlayMetric.Fans, ShowFans == true),
        };
        var layout = new OverlayLayout
        {
            Metrics = order.Select(o => new OverlayMetricEntry { Metric = o.Metric, Enabled = o.On }).ToList(),
            Corner = Corner ?? OverlayCorner.TopLeft,
            Monitor = Monitor ?? "primary",
            OffsetX = OffsetX ?? 16,
            OffsetY = OffsetY ?? 16,
            FontScale = Size switch { "Small" => 80, "Large" => 130, _ => 100 },
            BackgroundOpacity = BackgroundOpacity ?? 72,
        };
        return layout;
    }
}

internal sealed class OverlayColorPresetConverter : System.Text.Json.Serialization.JsonConverter<OverlayColorPreset>
{
    public override OverlayColorPreset Read(ref System.Text.Json.Utf8JsonReader reader, Type type, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType != System.Text.Json.JsonTokenType.String)
        {
            reader.Skip();
            return OverlayColorPreset.Light;
        }
        return OverlayColorPreset.Named(reader.GetString());
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, OverlayColorPreset value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Id);
}

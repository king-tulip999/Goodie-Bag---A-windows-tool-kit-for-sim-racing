using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoodieBag.Config;

public enum TriggerType { AppStarts, AppCloses, WindowsStartup, Idle, CpuTempAbove, GpuTempAbove, Manual, Daily, Weekly, Once, Plugin, WhileRunning }

public enum ActionType { ActivateMode, LaunchApp, CloseApp, FanPreset, Overlay, OverlayLayout, DisplayLayout, WindowProfile, Hdr, AudioDevice, Volume, PowerPlan, Notify }

public sealed class AutomationSettings
{
    public bool Enabled { get; set; } = true;

    public List<AutomationRule> Rules { get; set; } = new();
}

public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? Name { get; set; }
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public TriggerType Trigger { get; set; } = TriggerType.AppStarts;

    [JsonIgnore]
    public string? PluginTrigger { get; set; }

    [JsonPropertyName("trigger")]
    [JsonConverter(typeof(LooseTextConverter))]
    public string? TriggerName
    {
        get => Trigger == TriggerType.Plugin ? PluginTrigger : Trigger.ToString();
        set
        {
            if (string.IsNullOrWhiteSpace(value)) { Trigger = TriggerType.Manual; PluginTrigger = null; return; }
            value = value.Trim();
            if (Enum.TryParse<TriggerType>(value, true, out var t) && Enum.IsDefined(t) && t != TriggerType.Plugin)
            {
                Trigger = t;
                PluginTrigger = null;
            }
            else if (int.TryParse(value, out _))
            {
                Trigger = TriggerType.Manual;
                PluginTrigger = null;
            }
            else
            {
                Trigger = TriggerType.Plugin;
                PluginTrigger = value;
            }
        }
    }

    public string? App { get; set; }

    public int Threshold { get; set; }

    public int AtMinute { get; set; } = 19 * 60;

    public List<DayOfWeek> Days { get; set; } = new();

    public DateTime? Date { get; set; }

    public DateTime? LastDue { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public T? Option<T>(string key)
    {
        if (Extra is null || !Extra.TryGetValue(key, out var value)) return default;
        try { return value.Deserialize<T>(ConfigStore.JsonOptions); }
        catch (JsonException) { return default; }
    }

    public void SetOption<T>(string key, T? value)
    {
        if (value is null) { Extra?.Remove(key); return; }
        Extra ??= new();
        Extra[key] = JsonSerializer.SerializeToElement(value, ConfigStore.JsonOptions);
    }

    public string? Hotkey { get; set; }

    public string? OnlyInMode { get; set; }
    public string? OnlyIfRunning { get; set; }

    public List<AutomationAction>? Actions { get; set; }

    public int CooldownSeconds { get; set; } = 10;
}

public sealed class AutomationAction
{
    public ActionType Type { get; set; }
    public string? Target { get; set; }
    public string? Argument { get; set; }
    public int Value { get; set; }
}

internal sealed class LooseTextConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String: return reader.GetString();
            case JsonTokenType.Number: return reader.TryGetInt64(out var n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            default: reader.Skip(); return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value);
    }
}

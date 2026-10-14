using System.Text.Json.Serialization;

namespace GoodieBag.Config;

public enum SensorRate { Balanced, Fast, Low, Custom }

public enum BackgroundUsage { Standard, Reduced }

public sealed class PerformanceSettings
{
    public SensorRate Rate { get; set; } = SensorRate.Balanced;

    public int CustomMs { get; set; } = 1500;

    public BackgroundUsage Background { get; set; } = BackgroundUsage.Reduced;

    public bool LightWhileRacing { get; set; } = true;

    public const int MinMs = 250;
    public const int MaxMs = 5000;

    public const int ReducedHiddenMs = 3000;

    public const int FanLoopMs = 1000;

    public const int CalibrationLoopMs = 500;

    public static readonly SensorRate[] Presets = { SensorRate.Low, SensorRate.Balanced, SensorRate.Fast };

    public static int IntervalFor(SensorRate rate, int customMs) => rate switch
    {
        SensorRate.Fast => 500,
        SensorRate.Low => 2000,
        SensorRate.Custom => Math.Clamp(customMs, MinMs, MaxMs),
        _ => 1000,
    };

    [JsonIgnore]
    public int IntervalMs => IntervalFor(Rate, CustomMs);

    public static int Effective(int intervalMs, BackgroundUsage background, bool windowHidden) =>
        background == BackgroundUsage.Reduced && windowHidden ? Math.Max(intervalMs, ReducedHiddenMs) : intervalMs;

    public static SensorRate FromLegacy(int ms) => ms switch
    {
        <= 500 => SensorRate.Fast,
        >= 2000 => SensorRate.Low,
        _ => SensorRate.Balanced,
    };

    public static string Name(SensorRate rate) => rate switch
    {
        SensorRate.Fast => "Fast",
        SensorRate.Low => "Low",
        SensorRate.Custom => "Custom",
        _ => "Balanced",
    };

    public static string Describe(int ms) => ms < 1000 ? $"{ms} ms" : $"{ms / 1000.0:0.##} s";

    public static int LoopWaitMs(long untilNextReadMs, int monitorMs, bool fanControl, bool calibrating = false)
    {
        int wait = (int)Math.Clamp(untilNextReadMs, 10, Math.Max(10, monitorMs));
        return calibrating ? Math.Min(wait, CalibrationLoopMs) : fanControl ? Math.Min(wait, FanLoopMs) : wait;
    }
}

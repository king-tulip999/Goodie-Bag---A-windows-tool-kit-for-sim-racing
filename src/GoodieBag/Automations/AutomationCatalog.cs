using GoodieBag.Services;
using GoodieBag.Config;
using GoodieBag.Processes;

namespace GoodieBag.Automations;

internal static class AutomationCatalog
{
    public static readonly (TriggerType Type, string Label, string Help)[] Triggers =
    {
        (TriggerType.WhileRunning, "While a program runs", "Switches to this mode while the program runs; when it closes, the mode from before comes back. Several running: the mode with the higher priority wins, then the program started last."),
        (TriggerType.AppStarts, "A program starts", "Each time the program starts. Nothing switches back when it closes."),
        (TriggerType.AppCloses, "A program closes", "When the program has fully closed (its last copy exited)."),
        (TriggerType.WindowsStartup, "Windows starts", "When Goodie Bag starts with Windows (Settings → Start with Windows). A program already running with its own mode keeps it; this one comes back when it closes."),
        (TriggerType.Idle, "The PC becomes idle", "No keyboard or mouse input for the number of minutes you choose. Again only after you've been back."),
        (TriggerType.CpuTempAbove, "CPU temperature goes above", "When the CPU crosses the temperature, and again only after it has cooled 5 °C below it."),
        (TriggerType.GpuTempAbove, "GPU temperature goes above", "When the GPU crosses the temperature, and again only after it has cooled 5 °C below it."),
        (TriggerType.Daily, "Every day at", "Every day at the time you choose. If the PC was asleep then, still when it wakes within 15 minutes."),
        (TriggerType.Weekly, "On days of the week", "On the days you tick, at the time you choose (e.g. league night). Missed by under 15 minutes still counts."),
        (TriggerType.Once, "On a date", "Once, at the date and time you choose."),
    };

    public static string Label(TriggerType t) => Triggers.FirstOrDefault(x => x.Type == t).Label ?? t.ToString();

    public static Func<string?, PluginTrigger?> FindPluginTrigger { get; set; } = _ => null;

    public static bool UsesApp(TriggerType t) => t is TriggerType.AppStarts or TriggerType.AppCloses or TriggerType.WhileRunning;
    public static bool UsesThreshold(TriggerType t) => t is TriggerType.Idle or TriggerType.CpuTempAbove or TriggerType.GpuTempAbove;
    public static bool UsesTemperature(TriggerType t) => t is TriggerType.CpuTempAbove or TriggerType.GpuTempAbove;

    public static bool IsEvent(TriggerType t) => t is not (TriggerType.WhileRunning or TriggerType.Manual);

    public static int DefaultThreshold(TriggerType t) => t switch
    {
        TriggerType.Idle => 10,
        TriggerType.CpuTempAbove => 85,
        TriggerType.GpuTempAbove => 85,
        _ => 0,
    };

    public static string When(AutomationRule r) => r.Trigger switch
    {
        TriggerType.WhileRunning => $"While {AppName(r)} runs",
        TriggerType.AppStarts => $"When {AppName(r)} starts",
        TriggerType.AppCloses => $"When {AppName(r)} closes",
        TriggerType.WindowsStartup => "When Windows starts",
        TriggerType.Idle => $"After {r.Threshold} min without input",
        TriggerType.CpuTempAbove => $"When the CPU goes above {r.Threshold} °C",
        TriggerType.GpuTempAbove => $"When the GPU goes above {r.Threshold} °C",
        TriggerType.Manual => "When I run it",
        TriggerType.Daily => $"Every day at {ScheduleTriggers.Time(r.AtMinute)}",
        TriggerType.Weekly => r.Days.Count == 0 ? "On no days yet" : $"{Capital(ScheduleTriggers.DayList(r.Days))} at {ScheduleTriggers.Time(r.AtMinute)}",
        TriggerType.Once => r.Date is { } d ? $"On {d:ddd} {Dates.Date(d)} at {Dates.Time(d)}" : "On a date not set yet",
        TriggerType.Plugin => FindPluginTrigger(r.PluginTrigger) is { } t ? Summary(t, r) : $"When “{r.PluginTrigger}” happens (not installed)",
        _ => r.Trigger.ToString(),
    };

    private static string Summary(PluginTrigger t, AutomationRule r)
    {
        try { return t.Summary(r); }
        catch (Exception ex) { App.LogError(ex); return t.Label; }
    }

    public static string If(AutomationRule r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.OnlyInMode)) parts.Add($"only in {r.OnlyInMode}");
        if (!string.IsNullOrWhiteSpace(r.OnlyIfRunning)) parts.Add($"only if {AppName(r.OnlyIfRunning)} is running");
        return string.Join(", ", parts);
    }

    public static string Sentence(AutomationRule r)
    {
        string cond = If(r);
        return cond.Length > 0 ? $"{When(r)} ({cond})" : When(r);
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    private static string AppName(AutomationRule r) => string.IsNullOrWhiteSpace(r.Name) ? AppName(r.App) : r.Name!;

    private static string AppName(string? app) => string.IsNullOrWhiteSpace(app) ? "a program" : AppWatcher.Normalize(app);
}

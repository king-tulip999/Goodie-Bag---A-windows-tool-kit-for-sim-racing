using GoodieBag.Config;

namespace GoodieBag.Automations;

internal static class ScheduleTriggers
{
    public static bool IsScheduled(TriggerType t) => t is TriggerType.Daily or TriggerType.Weekly or TriggerType.Once or TriggerType.Plugin;

    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    public static DateTime SearchFrom(DateTime now, DateTime? lastDue, DateTime? armedAt = null)
    {
        var from = now - Grace;
        if (lastDue is { } last && last >= from) from = last.AddSeconds(1);
        return armedAt is { } armed && armed > from ? armed : from;
    }

    public static string Signature(AutomationRule r) =>
        $"{r.Trigger}|{r.AtMinute}|{string.Join(",", r.Days)}|{r.Date:o}|{r.PluginTrigger}";

    public static DateTime? NextDue(AutomationRule rule, DateTime from)
    {
        switch (rule.Trigger)
        {
            case TriggerType.Once:
                return rule.Date is { } at && at >= from ? at : null;
            case TriggerType.Daily:
            case TriggerType.Weekly:
                if (rule.Trigger == TriggerType.Weekly && rule.Days.Count == 0) return null;
                var time = TimeSpan.FromMinutes(Math.Clamp(rule.AtMinute, 0, 24 * 60 - 1));
                for (int day = 0; day <= 7; day++)
                {
                    var candidate = from.Date.AddDays(day) + time;
                    if (candidate < from) continue;
                    if (rule.Trigger == TriggerType.Daily || rule.Days.Contains(candidate.DayOfWeek)) return candidate;
                }
                return null;
            default:
                return null;
        }
    }

    public static string Time(int atMinute) => DateTime.Today.AddMinutes(atMinute).ToString("t");

    public static int? ParseTime(string text)
    {
        text = text.Trim().ToLowerInvariant();
        if (text.Length == 0) return null;
        foreach (var form in new[] { text, text.Replace(".", "") })
            if (DateTime.TryParse(form, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.NoCurrentDateDefault, out var t)
                || DateTime.TryParse(form, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.NoCurrentDateDefault, out t))
                return t.Hour * 60 + t.Minute;
        text = text.Replace(".", "");
        var m = System.Text.RegularExpressions.Regex.Match(text, @"^(\d{1,2})\s*(am|pm)$");
        if (!m.Success) return null;
        int h = int.Parse(m.Groups[1].Value);
        if (h is < 1 or > 12) return null;
        return (h % 12 + (m.Groups[2].Value == "pm" ? 12 : 0)) * 60;
    }

    public static string DayList(IReadOnlyCollection<DayOfWeek> days)
    {
        if (days.Count == 7) return "every day";
        var week = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        if (days.Count == 5 && week.All(days.Contains)) return "weekdays";
        if (days.Count == 2 && days.Contains(DayOfWeek.Saturday) && days.Contains(DayOfWeek.Sunday)) return "weekends";
        var names = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
        return string.Join(", ", Enumerable.Range(1, 7).Select(i => (DayOfWeek)(i % 7)).Where(days.Contains).Select(d => names.GetAbbreviatedDayName(d)));
    }
}

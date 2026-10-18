using System.Globalization;

namespace GoodieBag.Services;

internal static class Dates
{
    private static CultureInfo C => CultureInfo.CurrentCulture;

    public static string Date(DateTime t) => t.ToString("d", C);

    public static string Time(DateTime t) => t.ToString("t", C);

    public static string TimeWithSeconds(DateTime t) => t.ToString("T", C);

    public static string DateTime(DateTime t) => Date(t) + " · " + Time(t);

    public static string ShortPattern => C.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM");

    public static string Short(DateTime t) => t.ToString(ShortPattern, C);

    public static string Weekday(DateTime t) => t.ToString("ddd", C);

    public static string ShortWithDay(DateTime t) => t.ToString("dddd", C) + " " + Short(t);

    public static string ShortTime(DateTime t) => Short(t) + " · " + Time(t);

    public static string MonthYear(DateTime t) => t.ToString(C.DateTimeFormat.YearMonthPattern, C);

    public static string ShortMonthYear(DateTime t) => t.ToString(C.DateTimeFormat.YearMonthPattern.Replace("MMMM", "MMM"), C);
}

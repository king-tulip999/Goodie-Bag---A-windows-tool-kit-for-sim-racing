using System.Globalization;
using GoodieBag.Config;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Race;

internal static class RaceFormat
{
    public const string Missing = "–";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string LapTime(double? seconds, int decimals = 3)
    {
        if (seconds is not { } s || s <= 0 || !double.IsFinite(s)) return Missing;
        decimals = Math.Clamp(decimals, 1, 3);
        double r = Math.Round(s, decimals);
        int min = (int)(r / 60);
        double rest = Math.Max(0, r - min * 60);
        string sec = decimals switch { 1 => "0.0", 2 => "0.00", _ => "0.000" };
        return min > 0 ? $"{min}:{rest.ToString("0" + sec, Inv)}" : rest.ToString(sec, Inv);
    }

    public static string Gap(double? seconds, int decimals = 1)
    {
        if (seconds is not { } s || !double.IsFinite(s)) return Missing;
        string v = Math.Abs(s).ToString(decimals == 2 ? "0.00" : decimals == 3 ? "0.000" : "0.0", Inv);
        if (double.Parse(v, Inv) == 0) return v;
        return (s > 0 ? "+" : "−") + v;
    }

    public static string Abs(double? seconds, int decimals = 1) =>
        seconds is { } s && double.IsFinite(s) ? Math.Abs(s).ToString(decimals switch { <= 0 => "0", 2 => "0.00", >= 3 => "0.000", _ => "0.0" }, Inv) : Missing;

    public static string LapTime(double? seconds, string format)
    {
        if (seconds is not { } s || s <= 0 || !double.IsFinite(s)) return Missing;
        int decimals = format switch { "mixed" or "smixed" => 1, "minutes" or "s" => 0, _ => 3 };
        if (format is "sfull" or "smixed" or "s")
        {
            double r = Math.Round(s, decimals);
            return (r - Math.Floor(r / 60) * 60).ToString(decimals switch { 0 => "0", 1 => "0.0", _ => "0.000" }, Inv);
        }
        if (decimals == 0)
        {
            int whole = (int)Math.Round(s);
            return $"{whole / 60}:{whole % 60:00}";
        }
        return LapTime(s, decimals);
    }

    public static string Name(string name, string format, bool noDigits = false)
    {
        if (noDigits)
        {
            var kept = new System.Text.StringBuilder(name.Length);
            foreach (char c in name) if (!char.IsDigit(c)) kept.Append(c);
            name = string.Join(' ', kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return name;
        string first = words[0], last = string.Join(' ', words[1..]);
        return format switch
        {
            "initial" => $"{first[0]}. {last}",
            "last" => last,
            "first" => $"{first} {last[0]}.",
            _ => name,
        };
    }

    public static string Delta(double? seconds) => Gap(seconds, seconds is { } s && Math.Abs(s) < 10 ? 3 : 1);

    public static string TimeOfDay(DateTime t) => t.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    public static string Clock(double? seconds)
    {
        if (seconds is not { } s || s < 0 || !double.IsFinite(s)) return Missing;
        var t = TimeSpan.FromSeconds(Math.Floor(s));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static string Speed(double metresPerSecond, HudUnits u) =>
        u == HudUnits.Imperial ? $"{metresPerSecond * 2.23694:0} mph" : $"{metresPerSecond * 3.6:0} km/h";

    public static string SpeedKph(double kph, HudUnits u) =>
        u == HudUnits.Imperial ? $"{kph / 1.609344:0} mph" : $"{kph:0} km/h";

    public static double SpeedValue(double metresPerSecond, HudUnits u) => u == HudUnits.Imperial ? metresPerSecond * 2.23694 : metresPerSecond * 3.6;

    public static string SpeedUnit(HudUnits u) => u == HudUnits.Imperial ? "mph" : "km/h";

    public static double FuelValue(double litres, HudUnits u) => u == HudUnits.Imperial ? litres / 3.785411784 : litres;

    public static string FuelUnit(HudUnits u) => u == HudUnits.Imperial ? "gal" : "L";

    public static string Fuel(double? litres, HudUnits u)
    {
        if (litres is not { } l || !double.IsFinite(l)) return Missing;
        double v = FuelValue(l, u);
        return (u == HudUnits.Imperial ? v.ToString("0.00", Inv) : v.ToString(Math.Abs(v) < 10 ? "0.00" : "0.0", Inv)) + " " + FuelUnit(u);
    }

    public static string Temp(double? celsius, HudUnits u)
    {
        if (celsius is not { } c || !double.IsFinite(c)) return Missing;
        return u == HudUnits.Imperial ? $"{c * 9 / 5 + 32:0}°F" : $"{c:0}°C";
    }

    public static string Pressure(double? kpa, HudUnits u)
    {
        if (kpa is not { } p || !double.IsFinite(p) || p <= 0) return Missing;
        return u == HudUnits.Imperial ? $"{p * 0.145038:0.0} psi" : $"{p:0} kPa";
    }

    public static string Distance(double metres, HudUnits u) =>
        u == HudUnits.Imperial ? $"{metres * 3.28084:0} ft" : $"{metres:0} m";

    public static string Gear(int? gear) => gear switch { null => Missing, -1 => "R", 0 => "N", { } g => g.ToString(Inv) };

    public static string Warnings(EngineWarnings w)
    {
        var parts = new List<string>();
        if (w.HasFlag(EngineWarnings.WaterTemp)) parts.Add("Water temp");
        if (w.HasFlag(EngineWarnings.OilTemp)) parts.Add("Oil temp");
        if (w.HasFlag(EngineWarnings.OilPressure)) parts.Add("Oil pressure");
        if (w.HasFlag(EngineWarnings.FuelPressure)) parts.Add("Fuel pressure");
        if (w.HasFlag(EngineWarnings.Stalled)) parts.Add("Stalled");
        return string.Join(" · ", parts);
    }

    public static string Wetness(int? w) => w switch
    {
        null => Missing,
        <= 1 => "Dry",
        2 => "Mostly dry",
        3 => "Very lightly wet",
        4 => "Lightly wet",
        5 => "Moderately wet",
        6 => "Very wet",
        _ => "Extremely wet",
    };

    public static string Pos(int position) => position > 0 ? "P" + position.ToString(Inv) : Missing;

    public static string Compass(double? radians)
    {
        if (radians is not { } r || !double.IsFinite(r)) return Missing;
        string[] points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        double deg = (r * 180 / Math.PI % 360 + 360) % 360;
        return points[(int)Math.Round(deg / 45) % 8];
    }

    public static string License(string lic) => lic.StartsWith("Rookie", StringComparison.OrdinalIgnoreCase) ? "R" + lic[6..] : lic;

    public static string IRating(int? ir) => ir is not { } v ? Missing : v >= 1000 ? (v / 1000.0).ToString("0.0", Inv) + "k" : v.ToString(Inv);
}

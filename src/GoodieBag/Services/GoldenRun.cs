using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using GoodieBag.SessionStats;

namespace GoodieBag.Services;

internal static class GoldenRun
{
    public const string Arg = "--golden";

    private static readonly CultureInfo I = CultureInfo.InvariantCulture;

    public static void Run(Application app, string ibtFolder, string output, bool approve)
    {
        string current = Path.Combine(output, "current"), approved = Path.Combine(output, "approved");
        var report = new List<string>();
        try
        {
            Directory.CreateDirectory(current);
            foreach (var file in Directory.EnumerateFiles(ibtFolder, "*.ibt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(file) + ".txt";
                string text;
                try { text = Snapshot(SessionStatsService.ReadFile(file, traces: false, default) ?? new()); }
                catch (Exception ex) { text = "error " + ex.GetType().Name + ": " + ex.Message + "\n"; }
                File.WriteAllText(Path.Combine(current, name), text);
                string before = Path.Combine(approved, name);
                if (!File.Exists(before)) report.Add($"new {name}");
                else if (File.ReadAllText(before) is var old && old != text) report.Add($"changed {name}: {FirstDifference(old, text)}");
            }
            if (approve)
            {
                if (Directory.Exists(approved)) Directory.Delete(approved, recursive: true);
                Directory.Move(current, approved);
                report.Add("approved");
            }
        }
        catch (Exception ex)
        {
            report.Add("error " + ex);
        }
        File.WriteAllLines(Path.Combine(output, "golden.txt"), report.Count == 0 ? new[] { "same as approved" } : report);
        app.Shutdown(report.Any(r => r.StartsWith("changed", StringComparison.Ordinal) || r.StartsWith("error", StringComparison.Ordinal)) ? 2 : 0);
    }

    private static string FirstDifference(string a, string b)
    {
        var x = a.Split('\n');
        var y = b.Split('\n');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            string l = i < x.Length ? x[i] : "(nothing)", r = i < y.Length ? y[i] : "(nothing)";
            if (l != r) return $"line {i + 1}: “{l}” → “{r}”";
        }
        return "same lines";
    }

    private static string F(double? v, string format = "0.000") => v is { } d ? d.ToString(format, I) : "-";

    internal static string Snapshot(List<(SessionRow Row, List<LapRow> Laps)> sessions)
    {
        var sb = new StringBuilder();
        foreach (var (s, laps) in sessions)
        {
            sb.Append(I, $"session {s.SessionType} #{s.SessionNum} car={s.Car} class={s.CarClass} track={s.Track} / {s.TrackConfig}\n");
            sb.Append(I, $"  laps={s.Laps} incidents={s.Incidents?.ToString(I) ?? "-"} start={s.StartPos?.ToString(I) ?? "-"} finish={s.FinishPos?.ToString(I) ?? "-"} finished={s.Finished}\n");
            sb.Append(I, $"  drive={F(s.DriveS, "0")}s garage={F(s.GarageS, "0")}s stint={F(s.LongestStintS, "0")}s pits={s.PitStops} pit={F(s.PitS, "0.0")}s km={F(s.DistanceKm, "0.0")} tank={F(s.TankL, "0.0")}\n");
            foreach (var l in laps)
                sb.Append(I, $"  lap {l.Lap} {F(l.Time)} {(l.Clean ? "clean" : "dirty")} flags={l.Flags} run={l.Run?.ToString(I) ?? "-"} fuel={F(l.FuelUsed, "0.00")} sectors={(l.Sectors is { } sec ? string.Join(",", sec.Select(x => x.ToString("0.000", I))) : "-")}\n");
        }
        return sb.ToString();
    }
}

using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace GoodieBag.Services;

internal static class PerfBudget
{
    internal sealed record Limit(string Pattern, double Max, string Reason);

    public static IReadOnlyList<Limit> Read()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string file = Path.Combine(dir.FullName, "devtools", "tools", "perf-budget.txt");
            if (File.Exists(file)) return Parse(File.ReadAllLines(file));
        }
        return Array.Empty<Limit>();
    }

    internal static IReadOnlyList<Limit> Parse(IEnumerable<string> lines) =>
        lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split(new[] { ' ', '\t' }, 3, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 2 && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            .Select(p => new Limit(p[0], double.Parse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture), p.Length > 2 ? p[2] : ""))
            .ToList();

    public static List<string> Check(IEnumerable<string> results, IReadOnlyList<Limit> limits)
    {
        var values = new List<(string Name, double Value)>();
        foreach (var line in results)
        {
            var parts = line.Split(' ');
            if (parts.Length == 2 && !line.StartsWith('#')
                && double.TryParse(parts[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                values.Add((parts[0], v));
        }
        var output = new List<string>();
        int checkedCount = 0;
        foreach (var limit in limits)
        {
            var match = new Regex("^" + Regex.Escape(limit.Pattern).Replace("\\*", ".*") + "$", RegexOptions.CultureInvariant);
            var hits = values.Where(v => match.IsMatch(v.Name)).ToList();
            if (hits.Count == 0) { output.Add($"budget MISSING {limit.Pattern} (no such figure measured)"); continue; }
            foreach (var (name, value) in hits)
            {
                checkedCount++;
                if (value > limit.Max)
                    output.Add($"budget FAIL {name} {value.ToString("0.0", CultureInfo.InvariantCulture)} > {limit.Max.ToString(CultureInfo.InvariantCulture)} {limit.Reason}".TrimEnd());
            }
        }
        output.Add(output.Any(o => o.StartsWith("budget FAIL", StringComparison.Ordinal) || o.StartsWith("budget MISSING", StringComparison.Ordinal))
            ? $"budget failed ({checkedCount} figures checked)"
            : $"budget ok ({checkedCount} figures checked)");
        return output;
    }
}

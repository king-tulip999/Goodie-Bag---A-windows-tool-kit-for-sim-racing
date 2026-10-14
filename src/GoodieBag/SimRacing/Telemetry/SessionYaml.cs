using System.Globalization;

namespace GoodieBag.SimRacing.Telemetry;

internal static class SessionYaml
{
    private readonly record struct Line(int Indent, bool Item, string Key, string Value);

    public static Dictionary<string, object> Parse(string text)
    {
        var lines = new List<Line>();
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r', ' ', '\t');
            string trimmed = line.TrimStart(' ');
            if (trimmed.Length == 0 || trimmed == "---" || trimmed == "..." || trimmed.StartsWith('#')) continue;
            int indent = line.Length - trimmed.Length;
            bool item = trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-";
            string body = item ? trimmed.Length > 1 ? trimmed[2..].TrimStart(' ') : "" : trimmed;
            int colon = KeyEnd(body);
            if (colon < 0)
            {
                if (item) lines.Add(new Line(indent, true, "", Unquote(body)));
                continue;
            }
            lines.Add(new Line(indent, item, body[..colon].Trim(), Unquote(body[(colon + 1)..].Trim())));
        }
        int i = 0;
        var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (i < lines.Count)
        {
            int before = i;
            if (ParseNode(lines, ref i) is Dictionary<string, object> map)
                foreach (var (k, v) in map) root[k] = v;
            if (i == before) i++;
        }
        return root;
    }

    private static int KeyEnd(string body)
    {
        for (int i = 0; i < body.Length; i++)
            if (body[i] == ':' && (i + 1 == body.Length || body[i + 1] == ' ')) return i;
        return -1;
    }

    private static string Unquote(string v) =>
        v.Length >= 2 && (v[0] == '"' && v[^1] == '"' || v[0] == '\'' && v[^1] == '\'') ? v[1..^1] : v;

    private static object? ParseNode(List<Line> lines, ref int i)
    {
        if (i >= lines.Count) return null;
        return lines[i].Item ? ParseList(lines, ref i) : ParseMap(lines, ref i, lines[i].Indent, null);
    }

    private static List<object> ParseList(List<Line> lines, ref int i)
    {
        var list = new List<object>();
        int indent = lines[i].Indent;
        while (i < lines.Count && lines[i].Item && lines[i].Indent == indent)
        {
            var first = lines[i];
            if (first.Key.Length == 0)
            {
                list.Add(first.Value);
                i++;
                continue;
            }
            list.Add(ParseMap(lines, ref i, indent + 2, first));
        }
        return list;
    }

    private static Dictionary<string, object> ParseMap(List<Line> lines, ref int i, int col, Line? first)
    {
        var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        bool usedFirst = first is null;
        while (i < lines.Count)
        {
            Line line;
            if (!usedFirst) { line = first!.Value; usedFirst = true; }
            else
            {
                line = lines[i];
                if (line.Item || line.Indent != col) break;
            }
            i++;
            if (line.Value.Length == 0 && i < lines.Count && (lines[i].Indent > col || lines[i].Item && lines[i].Indent == col))
                map[line.Key] = ParseNode(lines, ref i) ?? "";
            else map[line.Key] = line.Value;
        }
        return map;
    }

    public static object? At(object? node, params string[] path)
    {
        foreach (var key in path)
        {
            if (node is not Dictionary<string, object> map || !map.TryGetValue(key, out node)) return null;
        }
        return node;
    }

    public static string? Str(object? node, params string[] path) => At(node, path) as string is { Length: > 0 } s ? s : null;

    public static IEnumerable<Dictionary<string, object>> Items(object? node, params string[] path) =>
        (At(node, path) as List<object>)?.OfType<Dictionary<string, object>>() ?? Enumerable.Empty<Dictionary<string, object>>();

    public static double? Num(object? node, params string[] path)
    {
        var s = Str(node, path);
        if (s is null) return null;
        int end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] is '.' or '-' or '+' or 'e' or 'E')) end++;
        return end > 0 && double.TryParse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static int? Int(object? node, params string[] path)
    {
        var s = Str(node, path);
        if (s is null) return null;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        return Num(node, path) is { } d ? (int)Math.Round(d) : null;
    }
}

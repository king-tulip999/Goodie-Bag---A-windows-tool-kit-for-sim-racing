using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GoodieBag.SimRacing.IracingFiles;

internal sealed class SettingsCatalog
{
    public List<AreaDef> Areas { get; set; } = new();
    public List<string> Leads { get; set; } = new();
    public List<SectionDef> Sections { get; set; } = new();
    public List<SettingDef> Settings { get; set; } = new();

    private Dictionary<string, SettingDef>? _byKey;
    private Dictionary<string, SectionDef>? _sections;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Lazy<SettingsCatalog> EmbeddedCatalog = new(() => Load<SettingsCatalog>("catalog.json"));

    public static SettingsCatalog Embedded => EmbeddedCatalog.Value;

    private static T Load<T>(string name)
    {
        var asm = typeof(SettingsCatalog).Assembly;
        var resource = asm.GetManifestResourceNames().First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<T>(stream, Json)!;
    }

    public static string FileKey(string fileName) =>
        fileName.StartsWith("rendererDX11", StringComparison.OrdinalIgnoreCase) ? "renderer" : fileName.ToLowerInvariant();

    public SettingDef? Find(string fileName, string section, string key)
    {
        _byKey ??= Settings.GroupBy(s => Id(s.File, s.Section, s.Key), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return _byKey.GetValueOrDefault(Id(FileKey(fileName), section, key));
    }

    public SectionDef? FindSection(string fileName, string section)
    {
        _sections ??= Sections.GroupBy(s => s.File + "|" + s.Section, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return _sections.GetValueOrDefault(FileKey(fileName) + "|" + section);
    }

    private static string Id(string file, string section, string key) => file + "|" + section + "|" + key;
}

internal sealed class AreaDef
{
    public string Title { get; set; } = "";
    public List<string> Sections { get; set; } = new();
}

internal sealed class SectionDef
{
    public string File { get; set; } = "";
    public string Section { get; set; } = "";
    public string? Name { get; set; }
    public string? Use { get; set; }
    public string? Why { get; set; }
    public bool ManagedBySim { get; set; }
    public bool KnownOnly { get; set; }
}

internal sealed class OptionDef
{
    public double Value { get; set; }
    public string Label { get; set; } = "";
    public string? Tip { get; set; }
}

internal sealed class TextChoiceDef
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Tip { get; set; }
}

internal sealed class DependencyDef
{
    public string Key { get; set; } = "";
    public string? Op { get; set; }
    public double Value { get; set; }
    public string? Message { get; set; }
}

internal sealed class SettingDef
{
    public string File { get; set; } = "";
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Help { get; set; } = "";
    public string Type { get; set; } = "text";
    public List<OptionDef>? Options { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public string? Unit { get; set; }
    [JsonPropertyName("std")] public double? Default { get; set; }
    public bool ReadOnly { get; set; }
    public string? ReadOnlyNote { get; set; }
    public bool Hidden { get; set; }
    public string? Pick { get; set; }
    public List<TextChoiceDef>? Choices { get; set; }
    public bool Invert { get; set; }
    public string? Danger { get; set; }
    public string? Caution { get; set; }
    public DependencyDef? DependsOn { get; set; }
    public double? UnusualAbove { get; set; }
    public string? UnusualNote { get; set; }
    public double? DisplayScale { get; set; }
    public string? DisplayUnit { get; set; }
    public int? DisplayDecimals { get; set; }
    public string? Tier { get; set; }
    public string? Group { get; set; }
    public int? Order { get; set; }
}

internal sealed record TextChoice(string Value, string Label, string? Tip = null);

internal static class ValueSources
{
    public const string Choices = "choices";
    public const string Folder = "folder";
    public const string HudProfiles = "hudProfiles";
    public const string ControlProfiles = "controlProfiles";
    public const string VoicePacks = "voicePacks";
    public const string ArxPacks = "arxPacks";

    public static bool IsListed(string? pick) => pick is HudProfiles or ControlProfiles or VoicePacks or ArxPacks;

    public static string? FolderOf(string pick, string? documents, string? install) => pick switch
    {
        HudProfiles when !string.IsNullOrEmpty(documents) => Path.Combine(documents, "profiles", "hud"),
        ControlProfiles when !string.IsNullOrEmpty(documents) => Path.Combine(documents, "profiles", "controls"),
        ArxPacks when !string.IsNullOrEmpty(documents) => Path.Combine(documents, "arx"),
        VoicePacks when !string.IsNullOrEmpty(install) => Path.Combine(install, "sound", "spcc"),
        _ => null,
    };

    public static IReadOnlyList<string>? List(string pick, string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !IsListed(pick) || !Directory.Exists(folder)) return null;
        try
        {
            IEnumerable<string> names = pick == HudProfiles
                ? Directory.EnumerateFiles(folder)
                    .Where(f => string.Equals(Path.GetExtension(f), ".ini", StringComparison.OrdinalIgnoreCase))
                    .Select(f => Path.GetFileNameWithoutExtension(f) ?? "")
                : Directory.EnumerateDirectories(folder).Select(d => Path.GetFileName(d) ?? "");
            return names.Where(n => n.Trim().Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static List<TextChoice> Merge(IEnumerable<TextChoiceDef>? fixedChoices, IEnumerable<string>? found, string? current)
    {
        current ??= "";
        var list = new List<TextChoice>();
        foreach (var c in fixedChoices ?? Enumerable.Empty<TextChoiceDef>())
            if (!list.Any(x => Same(x.Value, c.Value ?? "")))
                list.Add(new TextChoice(c.Value ?? "", c.Label, c.Tip));
        bool listed = found is not null;
        foreach (var name in found ?? Enumerable.Empty<string>())
            if (!list.Any(x => Same(x.Value, name)))
                list.Add(new TextChoice(name, name));
        int at = list.FindIndex(x => Same(x.Value, current));
        if (at >= 0)
        {
            if (list[at].Value != current) list[at] = list[at] with { Value = current };
        }
        else
        {
            string label = current.Length == 0 ? "(empty)" : current + (listed ? " (not found)" : " (not a listed option)");
            list.Insert(0, new TextChoice(current, label, listed ? "Not in the folder any more; kept as it is unless you pick another." : null));
        }
        return list;
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

internal static class CommentInference
{
    private const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex ReadOnlyNote = new(@"don'?t edit|system use only|do not edit", I);
    private static readonly Regex Pairs = new(@"(-?\d+)\s*[=]\s*([^,=;]+?)(?=\s*[,;]|\s+-?\d+\s*=|\s*\)?\s*$)", I);
    private static readonly Regex NamePairs = new(@"([A-Za-z]\w*)\s*=\s*(-?\d+)", I);
    private static readonly Regex DashPairs = new(@"(\d+)\s*-\s*([A-Za-z][^,]*?)(?=\s*,|\s+or\s+\d|\s*\.?\s*$)", I);
    private static readonly Regex DefaultNote = new(@"\((\d+) default\)|(\d+)\s*=\s*default", I);
    private static readonly Regex RangeTo = new(@"(-?\d+(?:\.\d+)?)\s*to\s*\+?(-?\d+(?:\.\d+)?)", I);
    private static readonly Regex RangeDash = new(@"^(-?\d+)\s*-\s*(\d+)\s*[:=]", I);
    private static readonly Regex Integer = new(@"^-?\d+$");
    private static readonly Regex Boolish = new(
        @"^(true if|enable|disable|show|hide|set (this )?to|should|does|is |use |force|allow|automatic|display|log |report|call out|remove|leave|mute|ask|pause|update|run |occasionally|motivational|keep|present|animate|play|send|detects|always|revert|if |vibrate|enables|include|apply|turn|let |reduce|record|choose|click|center|orient|fade|attempt|in looping|options search|compress|cut output|debounce|smooth)", I);
    private static readonly string[] OnOffWords = { "off", "on", "disabled", "enabled", "disable", "enable", "no", "yes", "false", "true", "none", "hide", "show" };

    private static readonly HashSet<string> ExpertSections = new(StringComparer.OrdinalIgnoreCase)
        { "XXX Dev Use Only", "Debug", "Communications", "AutoCfg", "Laser Scan", "SERVER", "Task", "FileLRUCache" };

    public static SettingDef Infer(string fileName, string section, string key, string? value, string? comment)
    {
        comment = (comment ?? "").Trim();
        var d = new SettingDef
        {
            File = SettingsCatalog.FileKey(fileName), Section = section, Key = key,
            Label = Humanize(key),
            Help = comment.Length > 0 ? comment : "No description in the file.",
            Type = "text",
            Tier = ExpertSections.Contains(section) ? "expert" : "more",
            ReadOnly = ReadOnlyNote.IsMatch(comment) || section.Equals("AutoCfg", StringComparison.OrdinalIgnoreCase),
        };

        var pairs = Pairs.Matches(comment).Select(m => (V: m.Groups[1].Value, L: m.Groups[2].Value)).ToList();
        if (pairs.Count < 2 && NamePairs.Matches(comment).Select(m => (V: m.Groups[2].Value, L: m.Groups[1].Value)).ToList() is { Count: >= 2 } np) pairs = np;
        if (pairs.Count < 2 && DashPairs.Matches(comment).Select(m => (V: m.Groups[1].Value, L: m.Groups[2].Value)).ToList() is { Count: >= 2 } dp) pairs = dp;
        pairs = pairs.Where(p => int.TryParse(p.V, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .Select(p => (p.V, L: p.L.Trim().Trim('(', ')').Trim())).ToList();
        var labels = pairs.Select(p => p.L.ToLowerInvariant()).ToHashSet();

        var std = DefaultNote.Match(comment);
        if (std.Success) d.Default = double.Parse(std.Groups[1].Success ? std.Groups[1].Value : std.Groups[2].Value, CultureInfo.InvariantCulture);
        var range = RangeTo.Match(comment);
        if (!range.Success) range = RangeDash.Match(comment);

        if (!TryNumber(value, out var n)) return d;
        bool isInt = Integer.IsMatch(value!.Trim());
        var pairValues = pairs.Select(p => int.Parse(p.V, CultureInfo.InvariantCulture)).ToList();

        if (comment.Contains("bitwise", StringComparison.OrdinalIgnoreCase))
        {
            d.Type = "num";
        }
        else if (pairs.Count > 0 && labels.All(l => l is "min" or "max" or "default"))
        {
            d.Type = "num"; d.Min = pairValues.Min(); d.Max = pairValues.Max();
        }
        else if (range.Success && pairs.Count > 0 && Num(range.Groups[2].Value) > pairValues.Max())
        {
            d.Type = "num"; d.Min = Num(range.Groups[1].Value); d.Max = Num(range.Groups[2].Value);
        }
        else if (pairs.Count >= 2 && isInt && pairValues.Contains((int)n))
        {
            var distinct = pairValues.Distinct().OrderBy(v => v).ToList();
            bool onOff = distinct.SequenceEqual(new[] { 0, 1 }) && labels.All(l => OnOffWords.Any(w => Regex.IsMatch(l, @"\b" + w + @"\b", I)));
            if (onOff) d.Type = "bool";
            else
            {
                d.Type = "enum";
                d.Options = pairs.OrderBy(p => int.Parse(p.V, CultureInfo.InvariantCulture))
                    .Select(p => new OptionDef { Value = int.Parse(p.V, CultureInfo.InvariantCulture), Label = p.L.TrimEnd('.').Trim() }).ToList();
            }
        }
        else if (isInt && (n == 0 || n == 1) && (Boolish.IsMatch(comment) || comment.EndsWith('?') || comment.Length == 0) && !range.Success)
        {
            d.Type = "bool";
        }
        else
        {
            d.Type = "num";
            if (range.Success) { d.Min = Num(range.Groups[1].Value); d.Max = Num(range.Groups[2].Value); }
            if (!isInt) d.Step = Math.Abs(n) < 5 ? 0.05 : 0.5;
            if (comment.Contains("dB") || key.EndsWith("_db", StringComparison.OrdinalIgnoreCase) || key.StartsWith("loudness", StringComparison.Ordinal)) d.Unit = "dB";
            else if (comment.Contains("(mm)")) d.Unit = "mm";
            else if (comment.Contains("(MB)") || comment.Contains(" MB")) d.Unit = "MB";
        }
        return d;
    }

    public static string Humanize(string key)
    {
        var k = Regex.Replace(key.Replace('_', ' '), "(?<=[a-z0-9])(?=[A-Z])", " ");
        k = Regex.Replace(k, "(?<=[A-Z])(?=[A-Z][a-z])", " ");
        var words = k.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => (w.Length > 1 && w.All(c => !char.IsLetter(c) || char.IsUpper(c))) || w.Any(char.IsDigit) ? w : w.ToLowerInvariant());
        var s = string.Join(" ", words);
        return s.Length == 0 ? key : char.ToUpperInvariant(s[0]) + s[1..];
    }

    public static bool TryNumber(string? s, out double n) =>
        double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n);

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}

using System.Globalization;
using System.IO;

namespace GoodieBag.SimRacing.IracingFiles;

internal sealed class Setting
{
    internal Setting(SettingsSession session, string fileName, IniLine line, SettingDef def, SectionInfo section)
    {
        Session = session;
        FileName = fileName;
        Line = line;
        Def = def;
        SectionInfo = section;
        Draft = Applied;
    }

    public SettingsSession Session { get; }
    public string FileName { get; }
    public IniLine Line { get; }
    public SettingDef Def { get; }
    public SectionInfo SectionInfo { get; }

    public string Id => FileName + "|" + Section + "|" + Key;
    public string Section => Line.Section ?? "";
    public string Key => Line.Key ?? "";
    public string Tier => Def.Tier ?? "more";
    public bool IsExpert => Tier == "expert";

    public string Applied => Line.DisplayValue ?? "";

    public string Draft { get; internal set; }

    public bool IsChanged => !SameValue(Draft, Applied);
    public bool IsModified => !IsReadOnly && !SectionInfo.ManagedBySim && Def.Default is not null && !IsDefault(Draft);

    public bool IsDefault(string value) =>
        Def.Default is { } std && (SameValue(value, Format(std)) || Def.SimWrites is { } kept && SameValue(value, Format(kept)));

    public bool IsReadOnly => Def.ReadOnly;
    public bool IsHidden => Def.Hidden;

    public bool IsUnusual
    {
        get
        {
            if (Session.IsIgnored(this)) return false;
            if (Def.UnusualAbove is { } above) return CommentInference.TryNumber(Draft, out var v) && v > above;
            if (Def.Default is null || SectionInfo.ManagedBySim) return false;
            return !IsDefault(Draft);
        }
    }

    public string? UnusualNote => Def.UnusualNote ?? (Def.Default is { } d ? $"Default is {Show(Format(d))}." : null);

    public bool IsInert
    {
        get
        {
            if (Def.DependsOn is not { } dep) return false;
            if (Session.Find(FileName, Section, dep.Key) is not { } parent || !CommentInference.TryNumber(parent.Draft, out var v)) return false;
            return dep.Op switch
            {
                "gt" => !(v > dep.Value),
                "ge" => !(v >= dep.Value),
                _ => Math.Abs(v - dep.Value) > 1e-9,
            };
        }
    }

    public bool IsOn(string value) => Def.Invert ? SameValue(value, "0") : SameValue(value, "1");

    public string Show(string? value)
    {
        if (value is null) return "";
        switch (Def.Type)
        {
            case "bool": return IsOn(value) ? "On" : "Off";
            case "enum": return Def.Options?.FirstOrDefault(o => SameValue(value, Format(o.Value)))?.Label ?? value;
            case "text":
                if (Def.Choices?.FirstOrDefault(c => string.Equals(c.Value ?? "", value, StringComparison.OrdinalIgnoreCase)) is { } choice) return choice.Label;
                return value.Length == 0 ? "(empty)" : value;
        }
        if (!CommentInference.TryNumber(value, out var n)) return value;
        if (Def.DisplayScale is { } scale)
            return (n * scale).ToString("F" + (Def.DisplayDecimals ?? 2), CultureInfo.InvariantCulture) + " " + Def.DisplayUnit;
        var shown = Format(n);
        if (string.IsNullOrEmpty(Def.Unit)) return shown;
        return Def.Unit is "°" or "%" ? shown + Def.Unit : shown + " " + Def.Unit;
    }

    public static bool SameValue(string? a, string? b)
    {
        if (a == b) return true;
        if (a is null || b is null) return false;
        return CommentInference.TryNumber(a, out var x) && CommentInference.TryNumber(b, out var y) && Math.Abs(x - y) < 1e-9;
    }

    public static string Format(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}

internal sealed class SectionInfo
{
    public string FileName { get; init; } = "";
    public string Section { get; init; } = "";
    public string Name { get; init; } = "";
    public string Area { get; init; } = "";
    public int AreaIndex { get; init; }
    public int Order { get; init; }
    public bool IsRare { get; set; }
    public bool ManagedBySim { get; init; }
    public string? Note { get; init; }
}

internal sealed record WriteResult(Backup? Backup, int Written, IReadOnlyList<string> Skipped);

internal sealed class SettingsSession
{
    private readonly Dictionary<string, Setting> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _ignored;
    private readonly List<Setting> _settings = new();
    private readonly List<SectionInfo> _sections = new();
    private readonly List<string> _files = new();

    private SettingsSession(string folder, string rendererFile, SettingsCatalog catalog, Dictionary<string, string> ignored)
    {
        Folder = folder;
        RendererFile = rendererFile;
        Catalog = catalog;
        _ignored = ignored;
    }

    public string Folder { get; }
    public string RendererFile { get; }
    public SettingsCatalog Catalog { get; }
    public IReadOnlyList<Setting> Settings => _settings;
    public IReadOnlyList<SectionInfo> Sections => _sections;
    public IReadOnlyList<string> FileNames => _files;
    public IEnumerable<Setting> Pending => _settings.Where(s => s.IsChanged);
    public bool InMemory => Folder.Length == 0;

    public string PathOf(string fileName) => Path.Combine(Folder, fileName);

    public static SettingsSession Load(string folder, string rendererFile, SettingsCatalog catalog, Dictionary<string, string> ignored)
    {
        var session = new SettingsSession(folder, rendererFile, catalog, ignored);
        foreach (var name in new[] { rendererFile, IracingPaths.App, IracingPaths.Core })
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path)) session.Add(name, IniDocument.Load(path));
        }
        session.SortSections();
        return session;
    }

    public static SettingsSession FromDocuments(string rendererFile, SettingsCatalog catalog, IEnumerable<(string Name, IniDocument Doc)> docs, Dictionary<string, string> ignored)
    {
        var session = new SettingsSession("", rendererFile, catalog, ignored);
        foreach (var (name, doc) in docs) session.Add(name, doc);
        session.SortSections();
        return session;
    }

    private void Add(string fileName, IniDocument doc)
    {
        _files.Add(fileName);
        var sections = new Dictionary<string, SectionInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in doc.Entries)
        {
            string section = line.Section ?? "", key = line.Key ?? "";
            if (!sections.TryGetValue(section, out var info))
            {
                info = DescribeSection(fileName, section);
                sections[section] = info;
                _sections.Add(info);
            }
            var known = Catalog.Find(fileName, section, key);
            if (known is null && Catalog.FindSection(fileName, section)?.KnownOnly == true)
                continue;
            var def = known ?? CommentInference.Infer(fileName, section, key, line.DisplayValue, line.Comment);
            var setting = new Setting(this, fileName, line, def, info);
            if (_byId.TryAdd(setting.Id, setting))
                _settings.Add(setting);
        }
        foreach (var info in sections.Values)
            if (_settings.Where(s => s.SectionInfo == info).All(s => s.IsExpert)) info.IsRare = true;
    }

    private SectionInfo DescribeSection(string fileName, string section)
    {
        var def = Catalog.FindSection(fileName, section);
        var key = SettingsCatalog.FileKey(fileName) + "|" + section;
        int areaIndex = -1, order = 1000;
        for (int i = 0; i < Catalog.Areas.Count && areaIndex < 0; i++)
        {
            int at = Catalog.Areas[i].Sections.FindIndex(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) { areaIndex = i; order = at; }
        }
        bool renderer = SettingsCatalog.FileKey(fileName) == "renderer";
        if (areaIndex < 0) areaIndex = renderer ? 0 : Math.Max(0, Catalog.Areas.Count - 1);
        return new SectionInfo
        {
            FileName = fileName,
            Section = section,
            Name = def?.Name ?? section,
            Area = Catalog.Areas.Count > 0 ? Catalog.Areas[areaIndex].Title : "",
            AreaIndex = areaIndex,
            Order = order,
            IsRare = def is not null ? def.Use == "rare" : !renderer,
            ManagedBySim = def?.ManagedBySim ?? false,
            Note = def?.Note,
        };
    }

    private void SortSections()
    {
        var sorted = _sections.OrderBy(s => s.AreaIndex).ThenBy(s => s.IsRare).ThenBy(s => s.Order).ToList();
        _sections.Clear();
        _sections.AddRange(sorted);
    }

    public Setting? Find(string fileName, string section, string key) => _byId.GetValueOrDefault(fileName + "|" + section + "|" + key);

    public IEnumerable<Setting> In(SectionInfo section) => _settings.Where(s => s.SectionInfo == section).OrderBy(SortKey);

    private int SortKey(Setting s)
    {
        int lead = Catalog.Leads.FindIndex(l => string.Equals(l, s.Section + "|" + s.Key, StringComparison.OrdinalIgnoreCase));
        if (lead >= 0) return lead - 100;
        return s.Def.Order ?? 10000 + s.Line.LineNumber;
    }

    public string? SetDraft(Setting s, string? value)
    {
        if (s.IsReadOnly) return "iRacing marks this setting as not to be edited.";
        value = (value ?? "").Trim();
        switch (s.Def.Type)
        {
            case "bool":
                if (value is not ("0" or "1")) return "Must be on or off.";
                break;
            case "enum" or "num" or "range":
                if (!CommentInference.TryNumber(value, out var n) || !double.IsFinite(n)) return "Must be a number.";
                if (s.Def.Min is { } min && n < min) n = min;
                if (s.Def.Max is { } max && n > max) n = max;
                value = KeepStyle(s.Applied, n);
                break;
            default:
                if (value.IndexOfAny(new[] { '\r', '\n' }) >= 0 || value.Contains("\t;")) return "Can't contain a line break or a tab followed by ';'.";
                break;
        }
        s.Draft = Setting.SameValue(value, s.Applied) ? s.Applied : value;
        return null;
    }

    public void Discard(Setting s) => s.Draft = s.Applied;

    public void DiscardAll()
    {
        foreach (var s in _settings) s.Draft = s.Applied;
    }

    public WriteResult Write(BackupStore store)
    {
        if (InMemory) throw new InvalidOperationException("Demo settings can't be written.");
        var pending = Pending.ToList();
        if (pending.Count == 0) return new WriteResult(null, 0, Array.Empty<string>());
        var skipped = new List<string>();
        int written = 0;
        var updated = new List<(string Path, IniDocument Doc)>();
        foreach (var g in pending.GroupBy(s => s.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var path = PathOf(g.Key);
            var fresh = IniDocument.Load(path);
            foreach (var s in g)
            {
                if (fresh.TrySet(s.Section, s.Key, s.Draft)) written++;
                else skipped.Add(s.Def.Label);
            }
            if (fresh.IsDirty) updated.Add((path, fresh));
        }
        if (updated.Select(u => u.Path).FirstOrDefault(f => new FileInfo(f).IsReadOnly) is { } locked)
            throw new UnauthorizedAccessException($"{Path.GetFileName(locked)} is read-only, so nothing was written.");
        var backup = store.Create($"Before writing {pending.Count} {(pending.Count == 1 ? "change" : "changes")}", IracingPaths.BackupFiles(Folder), Folder);
        SafeFileWriter.WriteAllAtomic(updated.Select(u => (u.Path, u.Doc.ToBytes())).ToList(), "written");
        return new WriteResult(backup, written, skipped);
    }

    public string? DefaultOf(Setting s) => (s.Def.SimWrites ?? s.Def.Default) is { } d ? KeepStyle(s.Applied, d) : null;

    public bool IsIgnored(Setting s) => _ignored.TryGetValue(s.Id, out var v) && Setting.SameValue(v, s.Draft);

    public void Ignore(Setting s) => _ignored[s.Id] = s.Draft;

    private static string KeepStyle(string current, double value)
    {
        var c = current.Trim();
        int dot = c.IndexOf('.');
        if (dot >= 0 && CommentInference.TryNumber(c, out _))
            return value.ToString("F" + (c.Length - dot - 1), CultureInfo.InvariantCulture);
        if (Math.Abs(value - Math.Round(value)) < 1e-9) return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
        return Setting.Format(value);
    }
}

using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace GoodieBag.SimRacing.TrackMaps;

internal sealed record TrackMapEntry
{
    public required string Key { get; init; }
    public int? TrackId { get; init; }
    public string TrackName { get; init; } = "";
    public string Family { get; init; } = "";
    public string Config { get; init; } = "";
    public string Category { get; init; } = "";
    public string Source { get; init; } = "";
    public string File { get; init; } = "";
    public List<string> Layers { get; init; } = new();
}

internal sealed record TrackMapAlias(string TrackName, string Key, string Why);

internal sealed class TrackMapManifest
{
    public int Version { get; init; } = 1;
    public string Generated { get; init; } = "";
    public List<TrackMapEntry> Entries { get; init; } = new();
    public List<TrackMapAlias> Aliases { get; init; } = new();

    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static string Normalize(string? trackName) =>
        string.Join(' ', (trackName ?? "").Trim().ToLowerInvariant().Split(new[] { ' ', '\\', '/', '_' }, StringSplitOptions.RemoveEmptyEntries));

    public TrackMapEntry? Find(int? trackId, string? trackName)
    {
        if (trackId is > 0 and var id && Entries.FirstOrDefault(e => e.TrackId == id) is { } byId) return byId;
        string name = Normalize(trackName);
        if (name.Length == 0) return null;
        if (Entries.FirstOrDefault(e => Normalize(e.TrackName) == name) is { } byName)
            return byName.TrackId is { } other && trackId is > 0 && other != trackId ? null : byName;
        if (Aliases.FirstOrDefault(a => Normalize(a.TrackName) == name) is { } alias) return Entries.FirstOrDefault(e => e.Key == alias.Key);
        return null;
    }

    public TrackMapEntry? FindByDisplay(string? family, string? config)
    {
        string f = Normalize(family), c = Normalize(config);
        if (f.Length == 0) return null;
        var matches = Entries.Where(e => Normalize(e.Family) == f && Normalize(e.Config) == c).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public List<string> Problems()
    {
        var problems = new List<string>();
        foreach (var g in Entries.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) problems.Add($"duplicate key {g.Key}");
        foreach (var g in Entries.Where(e => e.TrackId is not null).GroupBy(e => e.TrackId).Where(g => g.Count() > 1)) problems.Add($"duplicate TrackID {g.Key}");
        foreach (var g in Entries.Where(e => e.TrackName.Length > 0).GroupBy(e => Normalize(e.TrackName)).Where(g => g.Count() > 1)) problems.Add($"duplicate track name \"{g.Key}\"");
        foreach (var e in Entries.Where(e => e.File.Length == 0)) problems.Add($"{e.Key}: no file");
        foreach (var a in Aliases.Where(a => Entries.All(e => e.Key != a.Key))) problems.Add($"alias \"{a.TrackName}\" points at missing {a.Key}");
        foreach (var a in Aliases.Where(a => Entries.Any(e => Normalize(e.TrackName) == Normalize(a.TrackName)))) problems.Add($"alias \"{a.TrackName}\" hides a configuration of its own");
        return problems;
    }
}

internal static class TrackMapLibrary
{
    private const string Resource = "TrackMaps.zip";
    private static Task<TrackMapManifest?>? _manifest;
    private static readonly Dictionary<string, Task<TrackMap?>> _maps = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Lock = new();

    public static string LearnedFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GoodieBag", "iRacing", "TrackMaps", "Learned");

    public static string LearnedKey(int? trackId, string trackName) =>
        trackId is > 0 and var id ? "id" + id : "name-" + string.Concat(TrackMapManifest.Normalize(trackName).Select(c => char.IsLetterOrDigit(c) ? c : '-'));

    public static Task<TrackMapManifest?> Manifest()
    {
        lock (Lock) return _manifest ??= Task.Run(ReadManifest);
    }

    private static TrackMapManifest? ReadManifest()
    {
        try
        {
            using var zip = OpenZip();
            if (zip?.GetEntry("manifest.json") is not { } entry) return null;
            using var s = entry.Open();
            return JsonSerializer.Deserialize<TrackMapManifest>(s, TrackMapManifest.Json);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            App.LogError(ex);
            return null;
        }
    }

    private static ZipArchive? OpenZip() =>
        typeof(TrackMapLibrary).Assembly.GetManifestResourceStream(Resource) is { } stream ? new ZipArchive(stream, ZipArchiveMode.Read) : null;

    public static Task<TrackMap?> For(int? trackId, string trackName)
    {
        string learnedKey = LearnedKey(trackId, trackName);
        lock (Lock)
        {
            string cacheKey = $"{trackId}|{TrackMapManifest.Normalize(trackName)}";
            if (_maps.TryGetValue(cacheKey, out var known)) return known;
            var task = Task.Run(async () =>
            {
                var manifest = await Manifest().ConfigureAwait(false);
                if (manifest?.Find(trackId, trackName) is { } entry && Load(entry) is { } bundled) return bundled;
                return LoadLearned(learnedKey);
            });
            _maps[cacheKey] = task;
            return task;
        }
    }

    public static async Task<TrackMap?> ForSession(int? trackId, string trackName, string display, string config)
    {
        if ((trackId is > 0 || trackName.Length > 0) && await For(trackId, trackName).ConfigureAwait(false) is { } map) return map;
        var manifest = await Manifest().ConfigureAwait(false);
        return manifest?.FindByDisplay(display, config) is { } entry ? await ByKey(entry.Key).ConfigureAwait(false) : null;
    }

    public static Task<TrackMap?> ByKey(string key)
    {
        lock (Lock)
        {
            if (_maps.TryGetValue("key|" + key, out var known)) return known;
            var task = Task.Run(async () => (await Manifest().ConfigureAwait(false))?.Entries.FirstOrDefault(e => e.Key == key) is { } entry ? Load(entry) : null);
            _maps["key|" + key] = task;
            return task;
        }
    }

    private static TrackMap? Load(TrackMapEntry entry)
    {
        try
        {
            using var zip = OpenZip();
            if (zip?.GetEntry(entry.File) is not { } file) return null;
            using var reader = new StreamReader(file.Open());
            return TrackMap.FromSvg(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or System.Xml.XmlException)
        {
            App.LogError(ex);
            return null;
        }
    }

    private static TrackMap? LoadLearned(string key)
    {
        string path = Path.Combine(LearnedFolder, key + ".svg");
        try { return File.Exists(path) ? TrackMap.FromSvg(File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Xml.XmlException)
        {
            App.LogError(ex);
            return null;
        }
    }

    public const string SampleKey = "523";
}

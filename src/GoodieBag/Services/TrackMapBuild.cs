using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GoodieBag.SimRacing.TrackMaps;

namespace GoodieBag.Services;

internal static class TrackMapBuild
{
    public const string Arg = "--track-maps";

    public static void Run(App app, IReadOnlyList<string> args)
    {
        string report = args.Count > 3 ? args[3] : ".";
        var log = new StringBuilder();
        try
        {
            Build(args[0], args[1] == "-" ? null : args[1], args[2], report, log, useLayers: !(args.Count > 4 && args[4] == "nolayers"));
        }
        catch (Exception ex)
        {
            log.AppendLine("error " + ex);
        }
        try
        {
            Directory.CreateDirectory(report);
            File.WriteAllText(Path.Combine(report, "build.txt"), log.ToString());
        }
        catch { }
        app.Shutdown();
    }

    internal sealed record Row(string Key, int? TrackId, string TrackName, string Family, string Config, string Category, string Source, string Status, string Detail);

    private static string CategoryName(string c) => c switch
    {
        "road" => "Road", "oval" => "Oval", "dirt_road" => "Dirt road", "dirt_oval" => "Dirt oval", _ => "Retired / unknown",
    };

    public static void Build(string iracingDir, string? gpxDir, string zipPath, string reportDir, StringBuilder log, bool useLayers = true)
    {
        var rows = new List<Row>();
        var maps = new List<(TrackMapEntry Entry, string Svg)>();
        var gpx = gpxDir is null ? new Dictionary<string, JsonElement>() : GpxIndex(gpxDir);
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(iracingDir, "iracing-tracks-metadata.json")).TrimStart('﻿'));
        foreach (var track in meta.RootElement.GetProperty("tracks").EnumerateArray())
        {
            string family = Str(track, "track_name");
            string category = CategoryName(Str(track, "category"));
            foreach (var cfg in track.GetProperty("configurations").EnumerateArray())
            {
                int id = cfg.GetProperty("track_id").GetInt32();
                string dir = Str(cfg, "track_dirpath");
                string trackName = TrackMapManifest.Normalize(dir);
                string config = Str(cfg, "config_name");
                string key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                covered.Add(GpxName(trackName));
                string folder = Path.Combine(iracingDir, Str(cfg, "svg_local_path").Replace('/', Path.DirectorySeparatorChar));
                string? Layer(string name) => useLayers && File.Exists(Path.Combine(folder, name + ".svg")) ? File.ReadAllText(Path.Combine(folder, name + ".svg")) : null;
                var result = TrackMapImport.FromIracingLayers(key, id, trackName, Layer("active"), Layer("start-finish"), Layer("pitroad"), Layer("turns"));
                string? iracingProblem = result.Problem;
                if (result.Map is null && gpx.TryGetValue(GpxName(trackName), out var fallback))
                    result = FromGpx(key, id, trackName, fallback, gpxDir!) is { Map: not null } g
                        ? g with { Notes = useLayers ? g.Notes.Prepend("iRacing layers: " + iracingProblem).ToList() : g.Notes } : result;
                Add(result, key, id, trackName, family, config, category);
            }
        }
        foreach (var (name, json) in gpx.Where(g => !covered.Contains(g.Key)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            string trackName = TrackMapManifest.Normalize(name);
            var result = FromGpx("name-" + name, null, trackName, json, gpxDir!);
            Add(result, "name-" + name, null, trackName, trackName, "", CategoryName(""));
        }

        void Add(ImportResult result, string key, int? id, string trackName, string family, string config, string category)
        {
            if (result.Map is not { } map)
            {
                rows.Add(new Row(key, id, trackName, family, config, category, "", "missing", result.Problem ?? "no geometry"));
                return;
            }
            string svg = map.ToSvg();
            var problems = Validate(map, svg);
            var entry = new TrackMapEntry
            {
                Key = map.Key, TrackId = map.TrackId, TrackName = map.TrackName, Family = family, Config = config, Category = category,
                Source = map.Source.ToString(), File = $"maps/{map.Key}.svg", Layers = Layers(map),
            };
            if (problems.Count > 0)
            {
                rows.Add(new Row(map.Key, map.TrackId, map.TrackName, family, config, category, map.Source.ToString(), "invalid", string.Join("; ", problems)));
                return;
            }
            maps.Add((entry, svg));
            rows.Add(new Row(map.Key, map.TrackId, map.TrackName, family, config, category, map.Source.ToString(), "ok", string.Join("; ", result.Notes)));
        }

        var manifest = new TrackMapManifest
        {
            Generated = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Entries = maps.Select(m => m.Entry).ToList(),
        };
        var duplicateProblems = manifest.Problems();
        if (duplicateProblems.Count > 0) throw new InvalidDataException("Manifest problems: " + string.Join("; ", duplicateProblems));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        string temp = zipPath + ".tmp";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            var m = zip.CreateEntry("manifest.json", CompressionLevel.SmallestSize);
            using (var s = m.Open()) JsonSerializer.Serialize(s, manifest, TrackMapManifest.Json);
            foreach (var (entry, svg) in maps)
            {
                var e = zip.CreateEntry(entry.File, CompressionLevel.SmallestSize);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(svg);
            }
            var notices = new List<(string Name, string Path)> { ("NOTICES.md", Path.Combine(reportDir, "NOTICES.md")) };
            if (gpxDir is not null)
                foreach (var (name, file) in new[] { ("LICENSE-GPX.txt", "LICENSE.TXT"), ("NOTICE-GPX.txt", "NOTICE.TXT"), ("COPYRIGHT-GPX.txt", "COPYRIGHT.TXT") })
                    if (FindUp(gpxDir, file) is { } found) notices.Add((name, found));
            foreach (var (name, path) in notices.Where(n => File.Exists(n.Path)))
            {
                var e = zip.CreateEntry(name, CompressionLevel.SmallestSize);
                using var w = e.Open();
                w.Write(File.ReadAllBytes(path));
            }
        }
        File.Move(temp, zipPath, overwrite: true);
        using (var check = ZipFile.OpenRead(zipPath))
            foreach (var (entry, _) in maps)
            {
                using var r = new StreamReader(check.GetEntry(entry.File)!.Open());
                TrackMap.FromSvg(r.ReadToEnd());
            }

        Directory.CreateDirectory(reportDir);
        File.WriteAllText(Path.Combine(reportDir, "coverage.md"), Coverage(rows));
        File.WriteAllText(Path.Combine(reportDir, "missing.md"), Missing(rows));
        log.AppendLine($"maps {maps.Count}, missing {rows.Count(r => r.Status == "missing")}, invalid {rows.Count(r => r.Status == "invalid")}, zip {new FileInfo(zipPath).Length / 1024} KB");
        foreach (var g in rows.GroupBy(r => r.Source.Length == 0 ? "-" : r.Source)) log.AppendLine($"  {g.Key}: {g.Count(r => r.Status == "ok")} ok");
    }

    private static List<string> Layers(TrackMap map)
    {
        var l = new List<string>();
        if (map.Surface.Count > 0) l.Add("surface");
        l.Add("centerline");
        if (map.Pit.Count > 0) l.Add("pit");
        if (map.StartLine is not null) l.Add("start-finish");
        if (map.Arrow is not null) l.Add("direction");
        if (map.Turns.Count > 0) l.Add("turns");
        return l;
    }

    internal static List<string> Validate(TrackMap map, string svg)
    {
        var problems = new List<string>();
        if (map.Centerline.Length < 100) problems.Add($"centreline has {map.Centerline.Length} points");
        if (map.Centerline.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) problems.Add("centreline has invalid points");
        var b = map.Bounds;
        if (!(b.Width > 10 && b.Height > 10 && float.IsFinite(b.Width) && float.IsFinite(b.Height) && b.Width < 20000 && b.Height < 20000)) problems.Add($"bounds {b}");
        if (map.Surface.Any(r => r.Length < 3)) problems.Add("a surface ring with under 3 points");
        if (map.StartLine is null) problems.Add("no start / finish line");
        if (svg.Length < 200) problems.Add("empty SVG");
        try
        {
            var back = TrackMap.FromSvg(svg);
            if (back.Centerline.Length != map.Centerline.Length || back.Surface.Count != map.Surface.Count || back.Key != map.Key || back.TrackId != map.TrackId)
                problems.Add("doesn't read back the same");
            if (Math.Abs(back.Length - map.Length) > map.Length * 0.01) problems.Add("centreline length changes when read back");
        }
        catch (Exception ex) when (ex is FormatException or System.Xml.XmlException)
        {
            problems.Add("invalid SVG: " + ex.Message);
        }
        return problems;
    }

    private static string? FindUp(string dir, string file)
    {
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, file))) return Path.Combine(d.FullName, file);
        return null;
    }

    private static string GpxName(string trackName) => trackName.Replace(' ', '_');

    private static Dictionary<string, JsonElement> GpxIndex(string dir)
    {
        var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        var index = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file).TrimStart('﻿'), options);
                if (doc.RootElement.TryGetProperty("GPX", out var g) && g.TryGetProperty("ONTRACK", out _))
                    index[Path.GetFileNameWithoutExtension(file)] = doc.RootElement.Clone();
            }
            catch (JsonException) { }
        }
        return index;
    }

    private static ImportResult FromGpx(string key, int? id, string trackName, JsonElement json, string dir)
    {
        string? File(string layer) => json.GetProperty("GPX").TryGetProperty(layer, out var f) && f.GetString() is { Length: > 0 } name && System.IO.File.Exists(Path.Combine(dir, name))
            ? System.IO.File.ReadAllText(Path.Combine(dir, name)) : null;
        if (File("ONTRACK") is not { } track) return new(null, "GPX route file missing", Array.Empty<string>());
        var points = TrackMapImport.GpxPoints(track);
        if (points.Count < 20) return new(null, $"GPX route has {points.Count} points", Array.Empty<string>());
        double lat0 = points.Average(p => p.Lat), lon0 = points.Average(p => p.Lon);
        var line = TrackMapImport.Project(points, lat0, lon0);
        if (line.Count > 2 && TrackMap.Distance(line[0], line[^1]) is > 1 and var gap && gap < TrackMap.Perimeter(line.ToArray(), false) * 0.03) line.Add(line[0]);
        List<PointF>? pit = File("ONPITROAD") is { } pitXml ? TrackMapImport.Project(TrackMapImport.GpxPoints(pitXml), lat0, lon0) : null;
        return TrackMapImport.FromPath(key, id, trackName, TrackMapSource.Gpx, line, pit);
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Coverage(List<Row> rows)
    {
        var sb = new StringBuilder("# Track map coverage\n\nGenerated by devtools\tools\\track-maps.cmd. Status: ok = bundled; missing = no usable geometry; invalid = failed the checks (not bundled).\n\n");
        sb.AppendLine("| Category | Configurations | Bundled | iRacing layers | GPX route | Missing / invalid |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var c in rows.GroupBy(r => r.Category).OrderBy(g => g.Key))
            sb.AppendLine($"| {c.Key} | {c.Count()} | {c.Count(r => r.Status == "ok")} | {c.Count(r => r.Status == "ok" && r.Source == "IracingSvg")} | {c.Count(r => r.Status == "ok" && r.Source == "Gpx")} | {c.Count(r => r.Status != "ok")} |");
        sb.AppendLine($"| **All** | {rows.Count} | {rows.Count(r => r.Status == "ok")} | {rows.Count(r => r.Status == "ok" && r.Source == "IracingSvg")} | {rows.Count(r => r.Status == "ok" && r.Source == "Gpx")} | {rows.Count(r => r.Status != "ok")} |\n");
        foreach (var c in rows.GroupBy(r => r.Category).OrderBy(g => g.Key))
        {
            sb.AppendLine($"## {c.Key}\n");
            foreach (var fam in c.GroupBy(r => r.Family).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"### {fam.Key}\n");
                sb.AppendLine("| Configuration | TrackID | TrackName | Source | Status | Notes |");
                sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
                foreach (var r in fam.OrderBy(r => r.Config, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| {(r.Config.Length > 0 ? r.Config : "–")} | {r.TrackId?.ToString() ?? "–"} | {r.TrackName} | {r.Source} | {r.Status} | {r.Detail.Replace("|", "/")} |");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private static string Missing(List<Row> rows)
    {
        var sb = new StringBuilder("# Configurations without a usable map\n\nThese show \"No map for this track configuration\" and are drawn from the driver's next clean lap instead (kept on their PC).\n\n");
        var bad = rows.Where(r => r.Status != "ok").ToList();
        if (bad.Count == 0) sb.AppendLine("None.");
        else
        {
            sb.AppendLine("| Category | Track | Configuration | TrackID | Status | Why |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var r in bad.OrderBy(r => r.Category).ThenBy(r => r.Family))
                sb.AppendLine($"| {r.Category} | {r.Family} | {(r.Config.Length > 0 ? r.Config : "–")} | {r.TrackId?.ToString() ?? "–"} | {r.Status} | {r.Detail.Replace("|", "/")} |");
        }
        return sb.ToString();
    }
}

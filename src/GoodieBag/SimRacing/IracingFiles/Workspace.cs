using System.IO;
using System.Text;
using GoodieBag.Config;
using GoodieBag.Processes;

namespace GoodieBag.SimRacing.IracingFiles;

internal sealed class Workspace
{
    private readonly IracingControllerSettings _settings;
    private readonly bool _demo;

    public Workspace(IracingControllerSettings settings, bool demo)
    {
        _settings = settings;
        _demo = demo;
        Store = new BackupStore(IracingPaths.BackupFolder);
    }

    public BackupStore Store { get; }
    public string? Folder { get; private set; }
    public string? InstallFolder { get; private set; }
    public SettingsSession? Session { get; private set; }
    public string? LoadError { get; private set; }
    public bool Demo => _demo;

    public List<(string File, string Label)> Modes { get; } = new();

    public void Load()
    {
        LoadError = null;
        Session = null;
        Modes.Clear();
        _found.Clear();
        _telemetryRead = false;
        try
        {
            if (_demo)
            {
                Folder = @"C:\Users\You\Documents\iRacing";
                Modes.Add(IracingPaths.RendererModes[0]);
                Session = SettingsSession.FromDocuments(IracingPaths.MonitorRenderer, SettingsCatalog.Embedded, DemoDocuments(), _settings.IgnoredValues);
                return;
            }
            Folder = IracingPaths.FindDocumentsFolder();
            InstallFolder ??= IracingPaths.FindInstallFolder();
            if (Folder is null)
                return;
            Modes.AddRange(IracingPaths.RendererModes.Where(m => File.Exists(Path.Combine(Folder, m.File))));
            if (Modes.Count == 0 && !File.Exists(Path.Combine(Folder, IracingPaths.App)))
                return;
            string renderer = Modes.Any(m => m.File == _settings.RendererFile) ? _settings.RendererFile : Modes.Count > 0 ? Modes[0].File : IracingPaths.MonitorRenderer;
            Session = SettingsSession.Load(Folder, renderer, SettingsCatalog.Embedded, _settings.IgnoredValues);
        }
        catch (Exception ex)
        {
            Session = null;
            LoadError = ex.Message;
        }
    }

    public void SetMode(string rendererFile)
    {
        _settings.RendererFile = rendererFile;
        Load();
    }

    public bool SimRunning => !_demo && AppWatcher.IsRunningNow(IracingPaths.SimProcess);

    private readonly Dictionary<string, IReadOnlyList<string>?> _found = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string>? Found(string source)
    {
        if (_found.TryGetValue(source, out var list)) return list;
        list = _demo
            ? source switch
            {
                ValueSources.HudProfiles => new[] { "1080p", "Baseline", "Endurance" },
                ValueSources.ControlProfiles => new[] { "Baseline", "GT3", "Formula" },
                ValueSources.VoicePacks or ValueSources.ArxPacks => Array.Empty<string>(),
                _ => null,
            }
            : ValueSources.List(source, ValueSources.FolderOf(source, Folder, InstallFolder));
        _found[source] = list;
        return list;
    }

    private TelemetryFolder? _telemetry;
    private bool _telemetryRead;

    public HealthReport Health()
    {
        if (!_telemetryRead)
        {
            _telemetryRead = true;
            try { _telemetry = _demo || Folder is null ? null : TelemetryFolder.Scan(Folder); }
            catch { _telemetry = null; }
        }
        return HealthCheck.Run(Session!, Folder, InstallFolder, _telemetry, _settings.IgnoredFindings);
    }

    public void ForgetTelemetry() => _telemetryRead = false;

    public IReadOnlyList<StrayCopy> StrayCopies() =>
        _demo || Folder is null ? Array.Empty<StrayCopy>() : StrayCopy.Find(Folder, Store.ImportedSources());

    public IReadOnlyList<Backup> Backups() => _demo ? Array.Empty<Backup>() : Store.List();

    private static IEnumerable<(string Name, IniDocument Doc)> DemoDocuments()
    {
        var changed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["renderer|MonitorSetup|NumMonitors"] = "3", ["renderer|MonitorSetup|ScreenWidth"] = "597", ["renderer|MonitorSetup|MonitorWidth"] = "613",
            ["renderer|MonitorSetup|ViewingDist"] = "700", ["renderer|MonitorSetup|ScreenAngles"] = "48", ["renderer|MonitorSetup|RadiusOfCurvature"] = "1800",
            ["app.ini|Misc|irsdkEnableMem"] = "1", ["app.ini|Misc|irsdkAutoLogDisk"] = "1",
        };
        foreach (var s in SettingsCatalog.Embedded.Settings.Where(s => s.Default.HasValue && s.Tier == "core").Take(3))
            changed[s.File + "|" + s.Section + "|" + s.Key] = Setting.Format(s.Max ?? s.Default!.Value + 1);

        foreach (var (file, name, column) in new[] { ("renderer", IracingPaths.MonitorRenderer, 48), ("app.ini", IracingPaths.App, 40), ("core.ini", IracingPaths.Core, 40) })
        {
            var sb = new StringBuilder();
            foreach (var section in SettingsCatalog.Embedded.Settings.Where(s => s.File == file).GroupBy(s => s.Section))
            {
                sb.Append('[').Append(section.Key).Append("]\r\n");
                foreach (var s in section)
                {
                    string value = changed.GetValueOrDefault(file + "|" + s.Section + "|" + s.Key) ?? DemoValue(s);
                    sb.Append((s.Key + "=" + value).PadRight(column)).Append("\t; ").Append(s.Label).Append("\r\n");
                }
                sb.Append("\r\n");
            }
            yield return (name, IniDocument.Parse(Encoding.Latin1.GetBytes(sb.ToString())));
        }
    }

    private static string DemoValue(SettingDef s) => s.Default is { } d ? Setting.Format(d) : s.Type switch
    {
        "bool" => "1",
        "enum" => Setting.Format(s.Options?.FirstOrDefault()?.Value ?? 0),
        "text" => "",
        _ => Setting.Format(s.Min ?? 0),
    };
}

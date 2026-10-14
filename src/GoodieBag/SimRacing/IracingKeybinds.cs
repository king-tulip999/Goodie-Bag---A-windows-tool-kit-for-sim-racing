using System.IO;
using System.Text.RegularExpressions;
using GoodieBag.Processes;
using GoodieBag.SimRacing.IracingFiles;

namespace GoodieBag.SimRacing;

internal sealed record IracingAction(ControlBinding Binding, string Group, string Cluster, string Label, IracingKey? Default);

internal sealed class IracingKeybinds
{
    private readonly bool _demo;
    private readonly Config.IracingControllerSettings _settings;
    private DateTime _readAt;
    private readonly HashSet<string> _backedUp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ControlsFile> _demoFiles = new(StringComparer.OrdinalIgnoreCase);
    private string? _folder;

    public IracingKeybinds(bool demo, Config.IracingControllerSettings settings)
    {
        _demo = demo;
        _settings = settings;
    }

    public string? FilePath { get; private set; }
    public ControlsFile? File { get; private set; }
    public string? Error { get; private set; }
    public int CarFiles { get; private set; }

    public IReadOnlyList<string> Profiles { get; private set; } = Array.Empty<string>();
    public string? DefaultProfile { get; private set; }
    public string? Profile { get; private set; }

    public void SelectProfile(string? profile)
    {
        _settings.KeybindProfile = profile;
        File = null;
        Refresh();
    }

    public IReadOnlyList<IracingAction> Actions()
    {
        Refresh();
        return File is null ? Array.Empty<IracingAction>() : Describe(File);
    }

    public static string? Resolve(string? picked, IReadOnlyList<string> profiles, string? defaultProfile) =>
        picked == "" ? null
        : picked is not null && profiles.Contains(picked, StringComparer.OrdinalIgnoreCase) ? profiles.First(p => p.Equals(picked, StringComparison.OrdinalIgnoreCase))
        : defaultProfile is not null && profiles.Contains(defaultProfile, StringComparer.OrdinalIgnoreCase) ? profiles.First(p => p.Equals(defaultProfile, StringComparison.OrdinalIgnoreCase))
        : null;

    public static string? ReadDefaultProfile(IEnumerable<string> appIni)
    {
        bool inside = false;
        foreach (var raw in appIni)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inside = line.Equals("[ControlProfiles]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inside) continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals("Global", StringComparison.OrdinalIgnoreCase))
            {
                var value = line[(eq + 1)..];
                int comment = value.IndexOf(';');
                value = (comment >= 0 ? value[..comment] : value).Trim();
                return value.Length == 0 ? null : value;
            }
        }
        return null;
    }

    public void Refresh()
    {
        if (_demo)
        {
            Profiles = new[] { "Baseline", "gt3" };
            DefaultProfile = "gt3";
            Profile = Resolve(_settings.KeybindProfile, Profiles, DefaultProfile);
            string key = Profile ?? "";
            if (!_demoFiles.TryGetValue(key, out var demoFile))
                _demoFiles[key] = demoFile = ControlsFile.Parse(ControlsFile.Sample(DemoEntries()));
            File = demoFile;
            FilePath = Profile is null ? @"Documents\iRacing\controls.cfg" : $@"Documents\iRacing\profiles\controls\{Profile}\controls.cfg";
            return;
        }
        _folder = IracingPaths.FindDocumentsFolder();
        if (_folder is null)
        {
            (File, FilePath, Error, Profiles) = (null, null, "iRacing's settings folder (Documents\\iRacing) wasn't found. Start iRacing once to create it.", Array.Empty<string>());
            return;
        }
        var profilesFolder = Path.Combine(_folder, "profiles", "controls");
        try
        {
            Profiles = Directory.Exists(profilesFolder)
                ? Directory.GetDirectories(profilesFolder).Where(d => System.IO.File.Exists(Path.Combine(d, ControlsFile.FileName)))
                    .Select(d => Path.GetFileName(d)).OfType<string>().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Profiles = Array.Empty<string>(); }
        var appIni = Path.Combine(_folder, IracingPaths.App);
        try { DefaultProfile = System.IO.File.Exists(appIni) ? ReadDefaultProfile(System.IO.File.ReadLines(appIni)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { DefaultProfile = null; }
        Profile = Resolve(_settings.KeybindProfile, Profiles, DefaultProfile);

        var path = Profile is null ? Path.Combine(_folder, ControlsFile.FileName) : Path.Combine(profilesFolder, Profile, ControlsFile.FileName);
        if (!System.IO.File.Exists(path))
        {
            (File, FilePath, Error) = (null, path, "iRacing's controls file wasn't found (Documents\\iRacing\\controls.cfg). Start iRacing once to create it.");
            return;
        }
        var stamp = System.IO.File.GetLastWriteTimeUtc(path);
        if (File is not null && path == FilePath && stamp == _readAt)
            return;
        try
        {
            File = ControlsFile.Parse(System.IO.File.ReadAllBytes(path));
            (FilePath, _readAt, Error) = (path, stamp, null);
            var setups = Path.Combine(_folder, "setups");
            CarFiles = Directory.Exists(setups) ? Directory.GetDirectories(setups).Count(d => System.IO.File.Exists(Path.Combine(d, ControlsFile.FileName))) : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            (File, FilePath, Error) = (null, path, ex is FormatException ? "controls.cfg isn't laid out as expected, so it's shown read-only: " + ex.Message : "controls.cfg couldn't be read: " + ex.Message);
        }
    }

    public void Set(string name, IracingKey? key)
    {
        if (!_demo && AppWatcher.IsRunningNow(IracingPaths.SimProcess))
            throw new InvalidOperationException("Close iRacing first: it saves its own controls when it exits, which would undo this change.");
        string? shown = FilePath;
        Refresh();
        if (!_demo && shown is not null && !string.Equals(shown, FilePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The control profile changed since the list was shown, so nothing was written. Check the control profile picked above and set the key again.");
        var binding = File?.Find(name) ?? throw new InvalidOperationException(Error ?? $"“{name}” isn't in controls.cfg any more.");
        File!.Set(binding, key);
        if (_demo)
            return;
        try
        {
            if (!_backedUp.Contains(FilePath!))
            {
                new BackupStore(IracingPaths.BackupFolder).Create(
                    "Before changing keybinds" + (Profile is null ? "" : $" ({Profile} profile)"), new[] { FilePath! }, _folder);
                _backedUp.Add(FilePath!);
            }
            SafeFileWriter.WriteAtomic(FilePath!, File.ToBytes());
            _readAt = System.IO.File.GetLastWriteTimeUtc(FilePath!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            File = null;
            throw new InvalidOperationException("controls.cfg couldn't be saved: " + ex.Message);
        }
    }

    private static readonly HashSet<string> Analog = new(StringComparer.Ordinal)
        { "Throttle", "Throttle2", "Brake", "Brake2", "Clutch", "Clutch2", "SteerLeft", "SteerRight", "Handbrake", "ClutchLaunchAssist" };

    public static List<IracingAction> Describe(ControlsFile file)
    {
        var listed = file.Bindings.Where(b => !Analog.Contains(b.Name) && !b.Name.EndsWith("Level", StringComparison.Ordinal)).ToList();
        var firstOfStem = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < listed.Count; i++)
            firstOfStem.TryAdd(Polarity(listed[i].Name).Stem, i);
        return listed
            .Select((b, i) => (b, i, p: Polarity(b.Name)))
            .OrderBy(x => firstOfStem[x.p.Stem]).ThenBy(x => x.p.Sign).ThenBy(x => x.i)
            .Select(x => new IracingAction(x.b, GroupOf(x.b.Name), ClusterOf(x.b.Name), LabelOf(x.b.Name), Defaults.TryGetValue(x.b.Name, out var d) ? d : null))
            .ToList();
    }

    private static readonly Regex Positive = new("(Up|Inc|Next|FF)$|^(Next)", RegexOptions.CultureInvariant);
    private static readonly Regex Negative = new("(Down|Dec|Prev|Rew)$|^(Prev)", RegexOptions.CultureInvariant);

    public static (string Stem, int Sign) Polarity(string name)
    {
        var n = SecondKey(name);
        if (Positive.Match(n) is { Success: true } p) return (n.Remove(p.Index, p.Length), 0);
        if (Negative.Match(n) is { Success: true } m) return (n.Remove(m.Index, m.Length), 1);
        return (n, 0);
    }

    private static string SecondKey(string n) =>
        n.Length > 1 && n[^1] == '2' && !char.IsDigit(n[^2]) && !n.StartsWith("Gear") && !n.StartsWith("DashPage") && !n.StartsWith("TractionControl") ? n[..^1] : n;

    public static string ClusterOf(string name)
    {
        var n = SecondKey(name);
        foreach (var (prefix, cluster) in Families)
            if (n.StartsWith(prefix, StringComparison.Ordinal)) return cluster;
        if (n is "Ignition" or "Starter") return "Engine";
        if (n is "PitSpeedLimiter" or "RevLimiter") return "Limiters";
        if (n is "PushToPass" or "DRS" or "HysBoostHold" or "HysRegenHold" or "HysNoBoostToggle") return "Boost";
        if (n is "Reset" or "TellTaleReset") return "Reset";
        if (n is "HandUpWarning" or "TearOffVisor" or "ToggleWindshieldWipers" or "TriggerWindshieldWipers" or "HeadlightFlash") return "Driver";
        if (n is "SaveActiveReset" or "RunActiveReset") return "ResetPoint";
        if (n is "LowFuelAccept" or "FuelNoCutToggle" or "InLapToggle" or "FCYToggle" or "RFBrakeAttachedToggle") return "Pit and race";
        if (n is "ToggleSpeedGearDisplay" or "ToggleRadioDisplay" or "ToggleSystemMeterDisplay" or "ToggleVideoTimerDisplay" or "ToggleDrivingLine" or "ToggleRadar" or "ToggleRefCar") return "Displays";
        if (n is "ToggleUIVisible" or "ToggleEditLayoutMode" or "ScaleUIUp" or "ScaleUIDown" or "DashBoxToggle") return "UI";
        if (n is "ToggleVideoCapture" or "TriggerScreenshotCapture" or "TriggerGiantScreenshotCapture") return "Capture";
        if (n is "ToggleirsdkDiskLogging" or "IrsdkDriverMarker" or "ReportLatency") return "Telemetry";
        if (n is "RecenterTiltAxis" or "RecenterHeadMountedDisplay" or "ToggleVirtualMirror") return "Recenter";
        if (n is "AutoFFB" or "IncFFB" or "DecFFB") return "FFB";
        if (n is "RpyPausePlay" or "RpyStop" or "RpyIncreaseRate" or "RpyFF" or "RpyRew" or "RpyFrameAdv" or "RpyFrameRew") return "Replay transport";
        if (n is "RpyToStart" or "RpyToEnd") return "Replay jump";
        if (n is "CamInsert" or "CamRemove" or "CamCopy" or "CamPaste" or "GroupCopy" or "GroupPaste") return "Camera edit";
        if (n is "CamLoadTrack" or "CamSaveTrack" or "CamLoadCar" or "CamSaveCar") return "Camera files";
        if (n.StartsWith("CamToggle")) return "Camera toggles";
        if (n is "CamAcquireStart" or "CamAcquireEnd") return "Camera acquire";
        var stem = Polarity(n).Stem;
        return stem.EndsWith("Toggle", StringComparison.Ordinal) ? stem[..^6] : stem;
    }

    private static readonly (string Prefix, string Cluster)[] Families =
    {
        ("Shift", "Shift"), ("Gear", "Gears"), ("Look", "Look"), ("BlackBoxF", "Black box pages"), ("BlackBox", "Black box"),
        ("PeakBrakeBias", "Brake bias"), ("BrakeBias", "Brake bias"), ("BrakeMisc", "Brake bias"), ("AntiRoll", "Anti-roll bars"),
        ("WeightJacker", "Weight jacker"), ("Shock", "Shocks"), ("TractionControl", "Traction control"), ("ABS", "ABS"),
        ("DashPage", "Dash pages"), ("Wing", "Wings"), ("Diff", "Differential"), ("MGUK", "MGU-K"), ("FuelToEnd", "Fuel to end"),
        ("AutoChat", "Chat macros"), ("TChat", "Text chat"), ("VChat", "Voice chat"), ("SPCC", "Spotter"),
        ("RpySession", "Replay sessions"), ("RpyLap", "Replay laps"), ("RpyIncident", "Replay incidents"),
        ("NextRpyCamGroup", "Replay cameras"), ("PrevRpyCamGroup", "Replay cameras"), ("NextRpyCamCam", "Replay cameras"), ("PrevRpyCamCam", "Replay cameras"),
        ("Focus", "Replay cars"), ("CustSect", "Custom sector"), ("SplitsDelta", "Splits"),
    };

    public static string GroupOf(string n) => n switch
    {
        _ when n.StartsWith("BlackBox") || n.StartsWith("DashBox") => "Black boxes",
        _ when n.StartsWith("Rpy") || n.StartsWith("NextRpyCam") || n.StartsWith("PrevRpyCam") || n is "FocusNextCar" or "FocusPrevCar" or "FocusPlayerCar" => "Replay and TV cameras",
        _ when n.StartsWith("Cam") || n is "GroupCopy" or "GroupPaste" => "Camera tool",
        _ when n.StartsWith("DCam") || n.StartsWith("Look") || n is "NextDrivingCam" or "PrevDrivingCam" or "RecenterTiltAxis" or "RecenterHeadMountedDisplay" or "ToggleVirtualMirror" => "View",
        _ when n.StartsWith("TChat") || n.StartsWith("AutoChat") || n.StartsWith("VChat") => "Chat",
        _ when n.StartsWith("SPCC") || n.Contains("Volume") || n.EndsWith("FFB") => "Spotter, sound and force feedback",
        _ when n.EndsWith("Inc") || n.EndsWith("Dec") || (n.EndsWith("Toggle") && !n.StartsWith("Toggle")) || n is "LowFuelAccept" or "HysBoostHold" or "HysRegenHold" => "In-car adjustments",
        _ when n.StartsWith("Toggle") || n.StartsWith("Trigger") || n.StartsWith("ScaleUI") || n.StartsWith("SplitsDelta") || n is "ReportLatency" or "IrsdkDriverMarker" or "ReloadCarTexture" or "Pause" => "Screen and UI",
        _ => "Driving",
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["Reset"] = "Enter / exit / tow car", ["PitSpeedLimiter"] = "Pit speed limiter", ["PushToPass"] = "Push to pass",
        ["TellTaleReset"] = "Reset tell-tales", ["HandUpWarning"] = "Hand up (warning)", ["TearOffVisor"] = "Tear off visor",
        ["ToggleWindshieldWipers"] = "Wipers on / off", ["TriggerWindshieldWipers"] = "Wipe once", ["HeadlightFlash"] = "Flash headlights",
        ["RFBrakeAttachedToggle"] = "Right-front brake attached", ["FuelNoCutToggle"] = "Fuel no-cut", ["LowFuelAccept"] = "Accept low-fuel warning",
        ["InLapToggle"] = "In lap", ["FCYToggle"] = "Full-course yellow mode",
        ["ToggleSpeedGearDisplay"] = "Speed / gear / pedals display", ["ToggleRadioDisplay"] = "Radio display", ["ToggleSystemMeterDisplay"] = "FPS / network display",
        ["ToggleVideoTimerDisplay"] = "Video timer", ["ToggleVideoCapture"] = "Start / stop video recording", ["TriggerScreenshotCapture"] = "Take screenshot",
        ["TriggerGiantScreenshotCapture"] = "Take giant screenshot", ["ReportLatency"] = "Report latency", ["ToggleirsdkDiskLogging"] = "Telemetry logging on / off",
        ["IrsdkDriverMarker"] = "Mark event in telemetry", ["ToggleEditLayoutMode"] = "Edit UI layout", ["ToggleDrivingLine"] = "Racing line",
        ["ToggleRadar"] = "Car radar", ["ToggleUIVisible"] = "Hide / show UI", ["ScaleUIUp"] = "UI bigger", ["ScaleUIDown"] = "UI smaller",
        ["ToggleRefCar"] = "Reference car", ["ReloadCarTexture"] = "Reload car paint", ["SplitsDeltaNext"] = "Next splits / delta mode", ["SplitsDeltaPrev"] = "Previous splits / delta mode",
        ["ToggleVirtualMirror"] = "Virtual mirror", ["RecenterTiltAxis"] = "Recenter tilt axis", ["RecenterHeadMountedDisplay"] = "Recenter VR headset",
        ["NextDrivingCam"] = "Next driving camera", ["PrevDrivingCam"] = "Previous driving camera",
        ["DCamFOVInc"] = "Field of view up", ["DCamFOVDec"] = "Field of view down", ["DCamVanishYInc"] = "Driving view up", ["DCamVanishYDec"] = "Driving view down",
        ["DCamDrvHeightInc"] = "Driver eye height up", ["DCamDrvHeightDec"] = "Driver eye height down",
        ["TChatInitiate"] = "Text chat", ["TChatReply"] = "Reply to chat", ["TChatToggle"] = "Text chat window", ["VChatPushToTalk"] = "Push to talk",
        ["VChatMute"] = "Mute voice chat", ["VChatDriverMute"] = "Mute a driver", ["SPCCShutUp"] = "Silence spotter", ["SPCCToggleLapTimes"] = "Spotter lap times",
        ["SPCCAnounceLeaderLine"] = "Spotter: leader on the line", ["SPCCDamageReport"] = "Spotter: damage report", ["SPCCWeatherReport"] = "Spotter: weather report",
        ["AutoFFB"] = "Auto-set force feedback", ["IncFFB"] = "Force feedback stronger", ["DecFFB"] = "Force feedback weaker",
        ["DashBoxToggle"] = "Dash box", ["BlackBoxNext"] = "Next black box", ["BlackBoxPrev"] = "Previous black box", ["BlackBoxToggle"] = "Black boxes on / off",
        ["BlackBoxSize"] = "Black box size", ["BlackBoxShowNext"] = "Show next black box", ["BlackBoxShowPrev"] = "Show previous black box",
        ["BlackBoxF1"] = "Lap timing", ["BlackBoxF2"] = "Standings", ["BlackBoxF3"] = "Relative", ["BlackBoxF4"] = "Fuel", ["BlackBoxF5"] = "Tires",
        ["BlackBoxF6"] = "Tire info", ["BlackBoxF7"] = "Pit-stop adjustments", ["BlackBoxF8"] = "In-car adjustments", ["BlackBoxF9"] = "Mirror adjustments",
        ["BlackBoxF10"] = "Radio adjustments", ["BlackBoxF11"] = "Black box F11", ["BlackBoxF12"] = "Black box F12", ["BlackBoxInc"] = "Black box: increase",
        ["BlackBoxDec"] = "Black box: decrease",
        ["RpyPausePlay"] = "Play / pause", ["RpyStop"] = "Stop", ["RpyFF"] = "Fast forward", ["RpyRew"] = "Rewind", ["RpyFrameAdv"] = "Next frame",
        ["RpyFrameRew"] = "Previous frame", ["RpySessionFF"] = "Next session", ["RpySessionRew"] = "Previous session", ["RpyLapFF"] = "Next lap",
        ["RpyLapRew"] = "Previous lap", ["RpyIncidentFF"] = "Next incident", ["RpyIncidentRew"] = "Previous incident", ["RpyToStart"] = "Start of recording",
        ["RpyToEnd"] = "End of recording", ["RpyIncreaseRate"] = "Slow motion / speed", ["NextRpyCamGroup"] = "Next camera", ["PrevRpyCamGroup"] = "Previous camera",
        ["NextRpyCamCam"] = "Next sub-camera", ["PrevRpyCamCam"] = "Previous sub-camera", ["FocusNextCar"] = "Next car", ["FocusPrevCar"] = "Previous car",
        ["FocusPlayerCar"] = "Your car", ["CamToggleCamTool"] = "Open camera tool", ["SaveActiveReset"] = "Save reset point", ["RunActiveReset"] = "Go to reset point",
        ["CustSectSetStart"] = "Custom sector start", ["CustSectSetEnd"] = "Custom sector end", ["Pause"] = "Pause sim",
    };

    private static readonly (string From, string To)[] Prefixes =
        { ("DCam", "DrivingCam"), ("VChat", "VoiceChat"), ("TChat", "TextChat"), ("SPCC", "Spotter"), ("Hys", "Hybrid"), ("irsdk", "Telemetry"), ("Rpy", "Replay") };

    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["Inc"] = "up", ["Dec"] = "down", ["Cam"] = "camera", ["FF"] = "fast forward", ["Rew"] = "rewind", ["Prev"] = "previous",
        ["LFE"] = "bass shaker", ["MGUK"] = "MGU-K", ["Rpy"] = "replay",
    };

    public static string LabelOf(string name)
    {
        if (Labels.TryGetValue(name, out var label))
            return label;
        bool second = name.Length > 1 && name[^1] == '2' && !char.IsDigit(name[^2]) && !name.StartsWith("Gear");
        string s = second ? name[..^1] : name;
        if (Labels.TryGetValue(s, out var baseLabel))
            return baseLabel + " (2nd key)";
        foreach (var (from, to) in Prefixes)
            if (s.StartsWith(from, StringComparison.Ordinal)) { s = to + s[from.Length..]; break; }
        s = Regex.Replace(s, "(?<=[a-z0-9])(?=[A-Z])", " ");
        s = Regex.Replace(s, "(?<=[A-Z])(?=[A-Z][a-z])", " ");
        s = Regex.Replace(s, "(?<=[a-z])(?=[0-9])", " ");
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Words.TryGetValue(w, out var r) ? r : w.Length > 1 && w.All(char.IsUpper) ? w : w.ToLowerInvariant()).ToList();
        var text = string.Join(" ", words);
        text = text.Length == 0 ? name : char.ToUpperInvariant(text[0]) + text[1..];
        return second ? text + " (2nd key)" : text;
    }

    private static IracingKey K(byte code, byte mods = 0) => new(code, mods);
    private const byte S = IracingKey.Shift, C = IracingKey.Ctrl, A = IracingKey.Alt;

    public static readonly IReadOnlyDictionary<string, IracingKey> Defaults = new Dictionary<string, IracingKey>(StringComparer.Ordinal)
    {
        ["Starter"] = K((byte)'S'), ["Reset"] = K((byte)'R', S), ["BrakeBiasInc"] = K(0x72), ["BrakeBiasDec"] = K(0x6C),
        ["LookLeft"] = K((byte)'Z'), ["LookRight"] = K((byte)'X'), ["TChatInitiate"] = K((byte)'T'), ["TChatReply"] = K((byte)'R'),
        ["DCamFOVInc"] = K(0x78), ["DCamFOVDec"] = K(0x76), ["DCamVanishYInc"] = K(0x78, S), ["DCamVanishYDec"] = K(0x76, S),
        ["ToggleSpeedGearDisplay"] = K((byte)'P'), ["ToggleRadioDisplay"] = K((byte)'O'), ["ToggleSystemMeterDisplay"] = K((byte)'F'),
        ["ToggleVideoTimerDisplay"] = K((byte)'V', A), ["ReportLatency"] = K((byte)'L'), ["RecenterTiltAxis"] = K((byte)'Y'),
        ["ToggleirsdkDiskLogging"] = K((byte)'L', A), ["IrsdkDriverMarker"] = K((byte)'M'), ["ScaleUIUp"] = K(0x21, C), ["ScaleUIDown"] = K(0x22, C),
        ["ReloadCarTexture"] = K((byte)'R', C), ["ToggleEditLayoutMode"] = K((byte)'K', A), ["ToggleUIVisible"] = K(0x20),
        ["BlackBoxF1"] = K(0xC5), ["BlackBoxF2"] = K(0xC6), ["BlackBoxF3"] = K(0xC7), ["BlackBoxF4"] = K(0xC8), ["BlackBoxF5"] = K(0xC9),
        ["BlackBoxF6"] = K(0xCA), ["BlackBoxF7"] = K(0xCB), ["BlackBoxF8"] = K(0xCC), ["BlackBoxF9"] = K(0xCD), ["BlackBoxF10"] = K(0xCE),
        ["RpyPausePlay"] = K(0x0C), ["RpyStop"] = K(0xB9), ["RpyFF"] = K(0xB6, S), ["RpyRew"] = K(0xB4, S), ["RpyFrameAdv"] = K(0xB6),
        ["RpyFrameRew"] = K(0xB4), ["RpySessionFF"] = K(0xB6, C), ["RpySessionRew"] = K(0xB4, C), ["RpyLapFF"] = K(0xB1, S), ["RpyLapRew"] = K(0xB2, S),
        ["RpyIncidentFF"] = K(0xB1, C), ["RpyIncidentRew"] = K(0xB2, C), ["RpyToStart"] = K(0xB3), ["RpyToEnd"] = K(0xB2),
        ["NextRpyCamGroup"] = K((byte)'C'), ["PrevRpyCamGroup"] = K((byte)'C', S), ["NextRpyCamCam"] = K((byte)'B'), ["PrevRpyCamCam"] = K((byte)'B', S),
        ["FocusNextCar"] = K((byte)'V'), ["FocusPrevCar"] = K((byte)'V', S), ["FocusPlayerCar"] = K((byte)'V', C),
        ["NextDrivingCam"] = K(0x21), ["PrevDrivingCam"] = K(0x22),
        ["ToggleVideoCapture"] = K((byte)'V', C | A | S), ["TriggerScreenshotCapture"] = K((byte)'S', C | A | S), ["CamToggleCamTool"] = K(0xD0, C),
        ["CamLatInc"] = K((byte)'A'), ["CamLatDec"] = K((byte)'D'), ["CamLongInc"] = K((byte)'W'), ["CamLongDec"] = K((byte)'S'),
        ["CamAltInc"] = K((byte)'W', A), ["CamAltDec"] = K((byte)'S', A), ["CamYawInc"] = K((byte)'A', C), ["CamYawDec"] = K((byte)'D', C),
        ["CamPitchInc"] = K((byte)'S', C), ["CamPitchDec"] = K((byte)'W', C), ["CamRollInc"] = K((byte)'D', A), ["CamRollDec"] = K((byte)'A', A),
        ["CamFOVInc"] = K(0x78), ["CamFOVDec"] = K(0x76), ["CamMoveMultInc"] = K(0x72), ["CamMoveMultDec"] = K(0x6C),
        ["CamToggleMoveMultAccel"] = K((byte)'P', C), ["CamToggleMoveMultScale"] = K((byte)'P', A), ["CamListenerGainBiasInc"] = K(0x26, A),
        ["CamListenerGainBiasDec"] = K(0x28, A), ["CamListenerGainBiasAuto"] = K(0x28, C | A), ["CamToggleParabolicMic"] = K((byte)'O', C),
        ["CamAcquireStart"] = K((byte)'Q', C), ["CamSaveTrack"] = K(0xCF, C), ["CamToggleFocus"] = K((byte)'F', C),
    };

    private static IEnumerable<(string, IracingKey?, bool)> DemoEntries()
    {
        foreach (var n in new[] { "Throttle", "Brake", "Clutch", "SteerLeft", "SteerRight", "ShiftDown", "ShiftUp" })
            yield return (n, null, true);
        foreach (var n in new[] { "Ignition", "Starter", "PitSpeedLimiter", "Reset", "LookLeft", "LookRight", "HeadlightFlash",
                     "BrakeBiasInc", "BrakeBiasDec", "ABSInc", "ABSDec", "TractionControlInc", "TractionControlDec",
                     "ToggleSpeedGearDisplay", "ToggleSystemMeterDisplay", "TriggerScreenshotCapture", "ToggleUIVisible",
                     "BlackBoxF1", "BlackBoxF2", "BlackBoxF3", "BlackBoxF4", "BlackBoxF5", "TChatInitiate", "TChatReply",
                     "VChatPushToTalk", "SPCCShutUp", "MasterVolumeUp", "MasterVolumeDown", "RpyPausePlay", "RpyFF", "RpyRew",
                     "NextDrivingCam", "PrevDrivingCam", "DCamFOVInc", "DCamFOVDec", "CamLatInc", "CamLatDec" })
        {
            IracingKey? key = n switch
            {
                "Ignition" => K((byte)'I'), "LookLeft" => K(0x25), "LookRight" => K(0x27), "ABSInc" => K((byte)'B', A), "ABSDec" => K((byte)'N', A),
                "TriggerScreenshotCapture" => K((byte)'S', A | S),
                _ => Defaults.TryGetValue(n, out var d) ? d : null,
            };
            yield return (n, key, n is "PitSpeedLimiter" or "Reset" or "HeadlightFlash" or "BrakeBiasInc" or "BrakeBiasDec");
        }
    }
}

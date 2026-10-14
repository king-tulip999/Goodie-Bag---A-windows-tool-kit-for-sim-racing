using GoodieBag.Audio;
using GoodieBag.Config;
using GoodieBag.Modes;
using GoodieBag.Services;

namespace GoodieBag.Display;

internal sealed class DisplayProfileService
{
    private readonly AppServices _app;
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    public DisplayProfileService(AppServices app)
    {
        _app = app;
    }

    private List<DisplayProfile> Profiles => _app.Config.Displays.Profiles;

    public string? LastApplied { get; private set; }

    public int ApplyCount { get; private set; }

    public async Task ChangeByHandAsync(Action change)
    {
        await _applyLock.WaitAsync();
        try
        {
            ApplyCount++;
            await Task.Run(change);
        }
        finally { _applyLock.Release(); }
    }

    public event Action? Changed;

    public DisplayProfile? Find(string? name) =>
        name is null ? null : Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public DisplayProfile? Active
    {
        get
        {
            var on = ActiveMonitorKeys();
            if (Find(LastApplied) is { } last && (last.Layout is null || SameMonitors(last.Layout, on)))
                return last;
            return Profiles.FirstOrDefault(p => p.Layout is not null && SameMonitors(p.Layout, on));
        }
    }

    public async Task<List<string>> ApplyAsync(DisplayProfile profile, HdrMode? hdrOverride = null, bool quiet = false) =>
        (await ApplyCoreAsync(profile, hdrOverride, quiet)).Problems;

    internal async Task<(List<string> Problems, bool MonitorsChanged)> ApplyCoreAsync(DisplayProfile profile, HdrMode? hdrOverride = null, bool quiet = false)
    {
        var problems = new List<string>();
        await _applyLock.WaitAsync();
        bool displayChanged = false;
        bool touched = false;
        string? before = LastApplied;
        ApplyCount++;
        try
        {
            if (profile.Layout is not null)
            {
                touched = true;
                try
                {
                    var on = ActiveMonitorKeys();
                    var layout = profile.Layout;
                    if (_app.Demo)
                        await Task.Delay(300);
                    else
                        await Task.Run(() => DisplayManager.Apply(layout));
                    displayChanged = !SameMonitors(layout, on);
                }
                catch (Exception ex)
                {
                    problems.Add("Display: " + ex.Message);
                    if (ex is LayoutApplyException { Restored: false })
                        displayChanged = true;
                }
            }

            if (displayChanged && !_app.Demo)
                await Task.Delay(750);

            var hdr = hdrOverride ?? profile.Hdr;
            if (hdr != HdrMode.Unchanged && !_app.Demo)
            {
                touched = true;
                try
                {
                    bool enable = hdr == HdrMode.On;
                    var result = await Task.Run(() => HdrManager.Apply(enable));
                    if (hdr == HdrMode.On && result.Unsupported > 0)
                        problems.Add($"HDR: {result.Unsupported} monitor(s) don't support HDR");
                    problems.AddRange(result.Errors.Select(e => "HDR: " + e));
                }
                catch (Exception ex)
                {
                    problems.Add("HDR: " + ex.Message);
                }
            }

            if (profile.Taskbar != TaskbarMode.Unchanged && !_app.Demo)
            {
                try { Taskbar.SetOnAllDisplays(profile.Taskbar == TaskbarMode.AllDisplays); }
                catch (Exception ex) { problems.Add("Taskbar: " + ex.Message); }
            }

            if (profile.Wallpapers.Count > 0 && !_app.Demo)
                problems.AddRange(Wallpapers.Apply(profile.Wallpapers).Select(e => "Wallpaper: " + e));

            if (!_app.Demo)
                await AudioManager.ApplyAsync(profile.Audio, profile.Volume, displayChanged, problems);

            LastApplied = profile.Name;
        }
        finally
        {
            _applyLock.Release();
            if (touched || !string.Equals(before, LastApplied, StringComparison.Ordinal))
                _app.Displays.NotifyChanged();
            Changed?.Invoke();
        }

        if (!quiet)
        {
            if (problems.Count == 0)
                _app.Notify("Display profile applied", profile.Name, NoticeKind.Success);
            else
                _app.Notify($"{profile.Name}: some settings weren't applied", string.Join("\n", problems), NoticeKind.Warning, important: true);
        }
        return (problems, displayChanged);
    }

    public List<ModeChange> Preview(DisplayProfile profile, HdrMode? hdrOverride = null, bool withAudio = true)
    {
        var changes = profile.Layout is not null ? LayoutChanges(profile.Layout) : new List<ModeChange>();
        var hdr = hdrOverride ?? profile.Hdr;
        if (hdr != HdrMode.Unchanged)
            changes.Add(new ModeChange("Display", hdr == HdrMode.On ? "Turn HDR on" : "Turn HDR off"));
        if (profile.Taskbar != TaskbarMode.Unchanged && (_app.Demo || SafeTaskbarOnAll() != (profile.Taskbar == TaskbarMode.AllDisplays)))
            changes.Add(new ModeChange("Display", profile.Taskbar == TaskbarMode.AllDisplays ? "Show the taskbar on every display" : "Taskbar on the main display only"));
        if (profile.Wallpapers.Count > 0)
            changes.Add(new ModeChange("Display", profile.Wallpapers.Count == 1 ? "Set the wallpaper" : $"Set {profile.Wallpapers.Count} wallpapers"));
        if (!withAudio)
            return changes;
        if (_app.Demo)
        {
            if (profile.Audio?.PlaybackName is { } speakers) changes.Add(new ModeChange("Audio", "Speakers → " + speakers));
        }
        else
            changes.AddRange(AudioManager.Changes(profile.Audio, profile.Volume).Select(c => new ModeChange("Audio", c)));
        return changes;
    }

    private List<ModeChange> LayoutChanges(DisplaySnapshot layout)
    {
        var changes = new List<ModeChange>();
        var all = SafeMonitors();
        var target = _app.Displays.Describe(layout);
        var now = all.Where(m => m.Active).ToList();
        var origin = now.FirstOrDefault(m => m.Primary) is { } p ? (p.X, p.Y) : (0, 0);
        string Label(string key, string name) =>
            all.Count(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) > 1 && all.FirstOrDefault(m => m.Key == key) is { Number: > 0 } same
                ? $"{name} ({same.Number})" : name;
        foreach (var t in target)
        {
            string name = Label(t.Key, t.Name);
            var cur = now.FirstOrDefault(m => m.Key == t.Key);
            if (cur is null)
            {
                changes.Add(new ModeChange("Display", all.Any(m => m.Key == t.Key)
                    ? $"Turn on {name} at {t.Width}×{t.Height}, {t.Hz} Hz"
                    : $"{name} isn't connected; the profile can't be applied until it is"));
                continue;
            }
            if (cur.Width != t.Width || cur.Height != t.Height)
                changes.Add(new ModeChange("Display", $"{name}: {cur.Width}×{cur.Height} → {t.Width}×{t.Height}"));
            if (cur.Hz != t.Hz && t.Hz > 0)
                changes.Add(new ModeChange("Display", $"{name}: {cur.Hz} Hz → {t.Hz} Hz"));
            if (cur.Rotation != t.Rotation)
                changes.Add(new ModeChange("Display", $"{name}: rotate to {RotationName(t.Rotation)}"));
            if (t.Primary && !cur.Primary)
                changes.Add(new ModeChange("Display", $"{name} becomes the main display"));
            else if (!t.Primary && !cur.Primary && (cur.X - origin.Item1 != t.X || cur.Y - origin.Item2 != t.Y))
                changes.Add(new ModeChange("Display", $"{name}: moved in the arrangement"));
        }
        foreach (var cur in now.Where(m => target.All(t => t.Key != m.Key)))
            changes.Add(new ModeChange("Display", $"Turn off {Label(cur.Key, cur.Name)}"));
        return changes;
    }

    public DisplayProfile CaptureCurrent(string name)
    {
        var profile = new DisplayProfile { Name = name };
        Recapture(profile);
        return profile;
    }

    public void Recapture(DisplayProfile profile)
    {
        try { profile.Layout = _app.Displays.CaptureForMode(); } catch { }
        if (_app.Demo)
        {
            profile.Audio ??= new AudioSnapshot { PlaybackId = "demo-headset", PlaybackName = "Headset (Arctis Nova Pro)", RecordingId = "demo-mic", RecordingName = "Microphone (Arctis Nova Pro)" };
            return;
        }
        profile.Wallpapers = Wallpapers.Current();
        try { profile.Audio = AudioManager.Capture(); } catch { }
    }

    public string? Validate(string name, string? hotkey, DisplayProfile? self)
    {
        if (name.Length == 0)
            return "Give the profile a name.";
        if (Profiles.Any(p => p != self && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"There's already a display profile named “{name}”.";
        return _app.Hotkeys.Clash(hotkey, except: self);
    }

    public void Renamed(string oldName, string newName)
    {
        ConfigReferences.DisplayProfile(_app.Config, oldName, newName);
        if (string.Equals(LastApplied, oldName, StringComparison.OrdinalIgnoreCase)) LastApplied = newName;
    }

    public void Remove(DisplayProfile profile)
    {
        _app.Config.Displays.Profiles.Remove(profile);
        ConfigReferences.DisplayProfile(_app.Config, profile.Name, null);
        if (string.Equals(LastApplied, profile.Name, StringComparison.OrdinalIgnoreCase)) LastApplied = null;
        RaiseChanged();
    }

    public List<string> UsedBy(DisplayProfile profile)
    {
        bool Same(string? n) => string.Equals(n, profile.Name, StringComparison.OrdinalIgnoreCase);
        var c = _app.Config;
        return c.Modes.Where(m => Same(m.DisplayProfile)).Select(m => $"“{m.Name}”").Distinct().ToList();
    }

    public static string UsersText(List<string> users) => (users.Count == 1 ? "mode " : "modes ") + string.Join(", ", users);

    public void RaiseChanged()
    {
        _app.Save();
        _app.Hotkeys.RegisterAll();
        Changed?.Invoke();
    }

    public static string RotationName(int degrees) => degrees switch
    {
        90 => "portrait",
        180 => "landscape (flipped)",
        270 => "portrait (flipped)",
        _ => "landscape",
    };

    private static bool? SafeTaskbarOnAll()
    {
        try { return Taskbar.OnAllDisplays(); }
        catch { return null; }
    }

    private List<MonitorInfo> SafeMonitors()
    {
        try { return _app.Displays.GetMonitors(); }
        catch { return new List<MonitorInfo>(); }
    }

    private HashSet<string> ActiveMonitorKeys() =>
        SafeMonitors().Where(m => m.Active).Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool SameMonitors(DisplaySnapshot snapshot, HashSet<string> active)
    {
        var wanted = snapshot.TargetDevicePaths.Where(p => p.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return wanted.Count > 0 && wanted.SetEquals(active);
    }
}

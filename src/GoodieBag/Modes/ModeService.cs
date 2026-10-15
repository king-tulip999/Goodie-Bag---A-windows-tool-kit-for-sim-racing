using System.IO;
using GoodieBag.Audio;
using GoodieBag.Config;
using GoodieBag.Display;
using GoodieBag.Plugins;
using GoodieBag.Processes;
using GoodieBag.Services;

namespace GoodieBag.Modes;

internal sealed record ModeChange(string Area, string Text);

internal sealed class ModeService
{
    private readonly AppServices _app;
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    private readonly Dictionary<Mode, Task<bool>> _waiting = new();

    public ModeService(AppServices app)
    {
        _app = app;
    }

    public string? LastApplied { get; private set; }

    public string? AppliedFor { get; private set; }

    public string? Current =>
        Find(LastApplied) is { OneOff: false } last && MatchesScreens(last) ? last.Name : OnlyMatch()?.Name;

    private bool MatchesScreens(Mode m) =>
        m.DisplayProfile is null || _app.DisplayProfiles.Active is { } display && string.Equals(m.DisplayProfile, display.Name, StringComparison.OrdinalIgnoreCase);

    private Mode? OnlyMatch()
    {
        var display = _app.DisplayProfiles.Active;
        if (display is null) return null;
        var matches = _app.Config.Modes.Where(m => !m.OneOff && string.Equals(m.DisplayProfile, display.Name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public event Action? ActiveChanged;

    public event Action? OneOffRan;

    public Mode? Find(string? name) => name is null ? null : _app.Config.Modes.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public Mode? Active
    {
        get
        {
            var display = _app.DisplayProfiles.Active;
            bool Matches(Mode m) => m.DisplayProfile is null || (display is not null && string.Equals(m.DisplayProfile, display.Name, StringComparison.OrdinalIgnoreCase));
            if (Find(LastApplied) is { OneOff: false } last && Matches(last))
                return last;
            return display is null ? null : _app.Config.Modes.FirstOrDefault(m => !m.OneOff && m.DisplayProfile is not null && Matches(m));
        }
    }

    public Task<bool> ApplyAsync(Mode mode, string? appName = null, bool quiet = false)
    {
        if (_waiting.TryGetValue(mode, out var waiting))
            return waiting;
        bool busy = _applyLock.CurrentCount == 0;
        var task = ApplyQueuedAsync(mode, appName, quiet);
        if (busy && !task.IsCompleted)
            _waiting[mode] = task;
        return task;
    }

    private async Task<bool> ApplyQueuedAsync(Mode mode, string? appName, bool quiet)
    {
        var m = mode;
        var problems = new List<string>();
        await _applyLock.WaitAsync();
        _waiting.Remove(mode);
        try
        {
            bool monitorsChanged = false;
            bool displaysOn = _app.Plugins.IsActive(PluginIds.Displays);
            if (displaysOn && _app.DisplayProfiles.Find(m.DisplayProfile) is { } profile)
            {
                var (profileProblems, changed) = await _app.DisplayProfiles.ApplyCoreAsync(profile, null, quiet: true);
                problems.AddRange(profileProblems);
                monitorsChanged = changed;
            }
            else if (displaysOn && m.DisplayProfile is not null)
            {
                problems.Add($"Display profile “{m.DisplayProfile}” doesn't exist any more");
            }

            if (!_app.Demo)
                await AudioManager.ApplyAsync(m.Audio, m.Volume, monitorsChanged, problems);

            if (m.OverlayLayout is { } layout && _app.Plugins.IsActive(PluginIds.Overlay) && !_app.UseOverlayLayout(layout))
                problems.Add($"Overlay layout “{layout}” doesn't exist any more");
            if (m.Overlay != OverlayMode.Unchanged && _app.Plugins.IsActive(PluginIds.Overlay))
                _app.SetOverlayVisible(m.Overlay == OverlayMode.Toggle ? !_app.Config.Overlay.Visible : m.Overlay == OverlayMode.Show);
            if (m.FanPreset is { } missing && _app.Plugins.IsActive(PluginIds.FanControl) && !_app.Config.Fans.Presets.Any(p => p.Name == missing))
                problems.Add($"Fan preset “{missing}” doesn't exist any more");
            if (m.FanPreset is { } preset && _app.Plugins.IsActive(PluginIds.FanControl) && _app.Config.Fans.Presets.Any(p => p.Name == preset))
                _app.SetFanPreset(preset);

            if (!mode.OneOff)
            {
                LastApplied = mode.Name;
                AppliedFor = appName;
                _app.WindowRules.Refresh();
            }
            if (!_app.Demo)
                await ApplyProgramsAsync(m, problems);
            if (m.Windows.Count > 0 && !mode.OneOff)
                _ = ApplyWindowsAsync(mode);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            problems.Add(ex.Message);
        }
        finally
        {
            _applyLock.Release();
            if (!mode.OneOff) ActiveChanged?.Invoke();
            else OneOffRan?.Invoke();
        }

        string title = mode.OneOff ? "Mode ran" : appName is null ? "Mode applied" : $"Mode applied for {appName}";
        if (!string.IsNullOrWhiteSpace(mode.Message))
            _app.Notify(mode.Name, mode.Message!, NoticeKind.Info, important: true);
        if (problems.Count == 0)
        {
            if (!quiet && string.IsNullOrWhiteSpace(mode.Message))
                _app.Notify(title, mode.Name, NoticeKind.Success);
        }
        else
        {
            _app.Notify($"{mode.Name}: some settings weren't applied", string.Join("\n", problems), NoticeKind.Warning, important: true);
        }
        return problems.Count == 0;
    }

    private static async Task ApplyProgramsAsync(Mode m, List<string> problems)
    {
        foreach (var name in m.Close)
            await ProcessLauncher.CloseAsync(name, m.ForceClose);
        if (m.Launch.Count == 0)
            return;
        var running = AppWatcher.Snapshot();
        foreach (var item in m.Launch)
        {
            if (running.Contains(AppWatcher.Normalize(item.Path)))
                continue;
            if (ProcessLauncher.Launch(item.Path, item.Arguments, item.AsAdmin) is { } error)
                problems.Add($"Launch {Path.GetFileName(item.Path)}: {error}");
        }
    }

    public void MarkApplied(string? name)
    {
        LastApplied = name;
        AppliedFor = null;
        ActiveChanged?.Invoke();
    }

    public void Renamed(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;
        ConfigReferences.Mode(_app.Config, oldName, newName);
        if (string.Equals(LastApplied, oldName, StringComparison.OrdinalIgnoreCase)) LastApplied = newName;
        _app.Switcher.Renamed(oldName, newName);
    }

    public void Remove(Mode mode)
    {
        _app.Config.Modes.Remove(mode);
        ConfigReferences.Mode(_app.Config, mode.Name, null);
        _app.Switcher.Removed(mode.Name);
        if (string.Equals(LastApplied, mode.Name, StringComparison.OrdinalIgnoreCase))
        {
            LastApplied = null;
            AppliedFor = null;
            ActiveChanged?.Invoke();
        }
    }

    private async Task ApplyWindowsAsync(Mode mode)
    {
        List<string> problems;
        try { problems = await _app.WindowRules.ApplyRunningAsync(mode); }
        catch (Exception ex) { problems = new List<string> { ex.Message }; }
        if (problems.Count > 0)
            _app.Notify($"{mode.Name}: some windows weren't moved", string.Join("\n", problems), NoticeKind.Warning);
    }

    public List<ModeChange> Preview(Mode mode)
    {
        var changes = new List<ModeChange>();

        bool displaysOn = _app.Plugins.IsActive(PluginIds.Displays);
        var profile = displaysOn ? _app.DisplayProfiles.Find(mode.DisplayProfile) : null;
        if (!displaysOn && mode.DisplayProfile is not null)
            changes.Add(new ModeChange("Display", $"Display profile “{mode.DisplayProfile}” skipped: Displays is turned off"));
        else if (profile is not null)
            changes.AddRange(_app.DisplayProfiles.Preview(profile, withAudio: false));
        else if (mode.DisplayProfile is not null)
            changes.Add(new ModeChange("Display", $"Display profile “{mode.DisplayProfile}” doesn't exist any more"));
        foreach (var rule in mode.OneOff ? new() : mode.Windows)
            changes.Add(new ModeChange("Display", $"{rule.Label} window → {WindowRuleService.Describe(rule)}"));

        var audio = mode.Audio ?? profile?.Audio;
        var volume = mode.Volume ?? profile?.Volume;
        if (_app.Demo)
        {
            if (audio is not null) changes.Add(new ModeChange("Audio", $"Speakers → {audio.PlaybackName}"));
            if (volume is { } v) changes.Add(new ModeChange("Audio", $"Volume → {v}%"));
        }
        else
            changes.AddRange(AudioManager.Changes(audio, volume).Select(c => new ModeChange("Audio", c)));

        if (mode.Overlay == OverlayMode.Toggle && _app.Plugins.IsActive(PluginIds.Overlay))
            changes.Add(new ModeChange("Overlay", _app.Config.Overlay.Visible ? "Hide the overlay (toggle)" : "Show the overlay (toggle)"));
        else if (mode.Overlay != OverlayMode.Unchanged && _app.Plugins.IsActive(PluginIds.Overlay) && (mode.Overlay == OverlayMode.Show) != _app.Config.Overlay.Visible)
            changes.Add(new ModeChange("Overlay", mode.Overlay == OverlayMode.Show ? "Show the overlay" : "Hide the overlay"));
        if (mode.OverlayLayout is { } layout && _app.Plugins.IsActive(PluginIds.Overlay) && layout != _app.Config.Overlay.ActiveLayout)
            changes.Add(new ModeChange("Overlay", $"Overlay layout → {layout}"));
        if (mode.FanPreset is { } preset && _app.Plugins.IsActive(PluginIds.FanControl) && preset != _app.Config.Fans.ActivePreset)
            changes.Add(new ModeChange("Fans", $"Fan preset → {preset}"));
        foreach (var name in mode.Close)
            changes.Add(new ModeChange("Apps", $"Close {AppWatcher.Normalize(name)}"));
        foreach (var item in mode.Launch)
            changes.Add(new ModeChange("Apps", $"Start {Path.GetFileName(item.Path)} (if it isn't running)"));
        return changes;
    }

    public Mode CaptureCurrent(string name)
    {
        var mode = new Mode { Name = name, DisplayProfile = _app.DisplayProfiles.Active?.Name };
        if (mode.DisplayProfile is not null)
            return mode;
        if (!_app.Demo)
        {
            try { mode.Audio = AudioManager.Capture(); } catch { }
        }
        else
        {
            mode.Audio = new AudioSnapshot { PlaybackId = "demo-headset", PlaybackName = "Headset (Arctis Nova Pro)", RecordingId = "demo-mic", RecordingName = "Microphone (Arctis Nova Pro)" };
        }
        return mode;
    }

    public string? Validate(string name, string? hotkey, Mode? self)
    {
        if (name.Length == 0)
            return "Give the mode a name.";
        if (_app.Config.Modes.Any(p => p != self && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"There's already a mode named “{name}”.";
        return _app.Hotkeys.Clash(hotkey, except: self);
    }
}

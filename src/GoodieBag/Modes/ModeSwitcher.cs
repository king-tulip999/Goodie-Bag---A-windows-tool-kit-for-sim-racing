using GoodieBag.Config;
using GoodieBag.Processes;
using GoodieBag.Services;

namespace GoodieBag.Modes;

internal sealed class ModeSwitcher
{
    private const string Consumer = "Mode switching";
    private const int MaxSwitchesPerMinute = 6;

    private readonly AppServices _app;
    private readonly Dictionary<string, long> _startedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly LoopGuard _guard = new(MaxSwitchesPerMinute, TimeSpan.FromMinutes(1));
    private (Mode Mode, AutomationRule Trigger)? _inCharge;
    private string? _appliedMode;
    private string? _restoreTo;
    private (string? Mode, long At, string? Process, HashSet<string> Helpers)? _pendingRestore;
    private bool _evaluating, _again;

    public ModeSwitcher(AppServices app)
    {
        _app = app;
        _app.Watcher.Started += OnStarted;
        _app.Watcher.Stopped += OnStopped;
    }

    private ModeSwitchingSettings S => _app.Config.ModeSwitching;

    private bool ModesOn => _app.Plugins.IsActive(Plugins.PluginIds.Modes);

    public (Mode Mode, AutomationRule Trigger)? InCharge => _inCharge;

    private static string Label(AutomationRule t) => string.IsNullOrWhiteSpace(t.Name) ? AppWatcher.Normalize(t.App ?? "") : t.Name!;

    private IEnumerable<(Mode Mode, AutomationRule Trigger)> Programs() =>
        _app.Config.Modes.Where(m => !m.OneOff).SelectMany(m => m.Triggers
            .Where(t => t.Enabled && t.Trigger == TriggerType.WhileRunning && !string.IsNullOrWhiteSpace(t.App)).Select(t => (m, t)));

    public bool Paused => _guard.Tripped;

    public event Action? Changed;

    public void Renamed(string oldName, string newName)
    {
        static string? Swap(string? name, string oldName, string newName) =>
            string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase) ? newName : name;
        _appliedMode = Swap(_appliedMode, oldName, newName);
        _restoreTo = Swap(_restoreTo, oldName, newName);
        if (_pendingRestore is { } pending) _pendingRestore = pending with { Mode = Swap(pending.Mode, oldName, newName) };
    }

    public void Removed(string name)
    {
        static bool Is(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        if (Is(_appliedMode, name)) _appliedMode = null;
        if (Is(_restoreTo, name)) _restoreTo = null;
        if (_pendingRestore is { } pending && Is(pending.Mode, name)) _pendingRestore = null;
    }

    public void Refresh()
    {
        var names = S.Enabled && ModesOn ? Programs().Select(x => AppWatcher.Normalize(x.Trigger.App!)).ToList() : new List<string>();
        if (_inCharge is { } held)
        {
            _inCharge = Programs().Select(x => ((Mode Mode, AutomationRule Trigger)?)x).FirstOrDefault(x => x!.Value.Trigger.Id == held.Trigger.Id);
            if (_inCharge is null)
            {
                _appliedMode = null;
                _restoreTo = null;
            }
        }
        if (names.Count == 0)
        {
            _app.Watcher.Release(Consumer);
            _inCharge = null;
            _appliedMode = null;
            _restoreTo = null;
        }
        else
        {
            _app.Watcher.Acquire(Consumer, names);
            _ = EvaluateAsync();
        }
        Changed?.Invoke();
    }

    public void NoteRestorePoint(string? modeBefore, string? process, IEnumerable<string> helpers)
    {
        if (_inCharge is null)
            _pendingRestore = (modeBefore, Environment.TickCount64, process,
                helpers.Select(AppWatcher.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    internal static bool RestorePointFor(string process, string? launched, IReadOnlySet<string> helpers) =>
        launched is not null ? string.Equals(process, launched, StringComparison.OrdinalIgnoreCase) : !helpers.Contains(process);

    public void Resume()
    {
        _guard.Reset();
        Refresh();
    }

    public void RestoreLater(string mode) => _restoreTo = mode;

    private void OnStarted(string name)
    {
        if (_app.Watcher.CatchingUp) _startedAt[name] = 0;
        else _startedAt[name] = Environment.TickCount64;
        _ = EvaluateAsync();
    }

    private void OnStopped(string name)
    {
        _startedAt.Remove(name);
        _ = EvaluateAsync();
    }

    private async Task EvaluateAsync()
    {
        if (_evaluating)
        {
            _again = true;
            return;
        }
        _evaluating = true;
        try
        {
            do
            {
                _again = false;
                await EvaluateOnceAsync();
            } while (_again);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
        finally
        {
            _evaluating = false;
        }
    }

    private async Task EvaluateOnceAsync()
    {
        if (!S.Enabled || _guard.Tripped || !ModesOn)
            return;

        var found = Programs()
            .Select((x, index) => (x.Mode, x.Trigger, Process: AppWatcher.Normalize(x.Trigger.App!), Index: index))
            .Where(x => _app.Watcher.IsRunning(x.Process))
            .OrderByDescending(x => x.Mode.Priority)
            .ThenByDescending(x => _startedAt.GetValueOrDefault(x.Process))
            .ThenBy(x => x.Index)
            .Select(x => ((Mode Mode, AutomationRule Trigger)?)(x.Mode, x.Trigger))
            .FirstOrDefault();

        if (found is { } same && _inCharge is { } current && same.Trigger.Id == current.Trigger.Id || found is null && _inCharge is null)
            return;

        if (found is { } winner)
        {
            var mode = winner.Mode;
            if (_pendingRestore is { } pending && Environment.TickCount64 - pending.At >= (long)ModeLauncher.StartTimeout.TotalMilliseconds) _pendingRestore = null;
            if (_inCharge is null && _pendingRestore is { } point && RestorePointFor(AppWatcher.Normalize(winner.Trigger.App!), point.Process, point.Helpers))
            {
                _restoreTo = point.Mode;
                _pendingRestore = null;
            }
            string? before = _app.Modes.Current;
            if (!string.Equals(before, mode.Name, StringComparison.OrdinalIgnoreCase)) _restoreTo ??= before;
            bool nothingToApply = string.Equals(_app.Modes.LastApplied, mode.Name, StringComparison.OrdinalIgnoreCase) && _app.Modes.AppliedFor is null;
            if (nothingToApply)
            {
                _inCharge = winner;
                _appliedMode = _app.Modes.LastApplied;
                Changed?.Invoke();
                return;
            }
            if (!AllowSwitch())
                return;
            _inCharge = winner;
            _appliedMode = mode.Name;
            Changed?.Invoke();
            await _app.Modes.ApplyAsync(mode, Label(winner.Trigger));
            return;
        }

        bool userChangedIt = !string.Equals(_app.Modes.LastApplied, _appliedMode, StringComparison.OrdinalIgnoreCase);
        var restore = _app.Modes.Find(_restoreTo);
        if (string.Equals(restore?.Name, _appliedMode, StringComparison.OrdinalIgnoreCase)) restore = null;
        if (S.RestorePrevious && !userChangedIt && restore is not null && !AllowSwitch())
            return;
        _inCharge = null;
        _appliedMode = null;
        _restoreTo = null;
        Changed?.Invoke();
        if (S.RestorePrevious && !userChangedIt && restore is not null)
            await _app.Modes.ApplyAsync(restore);
    }

    private bool AllowSwitch()
    {
        if (_guard.Allow())
            return true;
        _app.Notify("Automatic mode switching paused",
            $"Modes switched more than {MaxSwitchesPerMinute} times in a minute, which usually means two programs or modes keep triggering each other. Check the modes' “while a program runs” triggers, then resume on Automations and Modes.",
            NoticeKind.Warning, important: true);
        Changed?.Invoke();
        return false;
    }
}

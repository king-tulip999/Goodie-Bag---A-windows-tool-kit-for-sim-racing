using GoodieBag.Config;
using GoodieBag.Processes;
using GoodieBag.Services;
using System.Windows.Threading;

namespace GoodieBag.Automations;

internal sealed record RuleRun(DateTime At, string? Error);

internal sealed class AutomationService
{
    private const string Consumer = "Automations";
    private const int MaxRunsPerMinute = 20;
    private const int TempRearmC = 5;

    private readonly AppServices _app;
    private readonly LoopGuard _guard = new(MaxRunsPerMinute, TimeSpan.FromMinutes(1));
    private readonly Dictionary<string, long> _lastRun = new();
    private readonly HashSet<string> _armed = new();
    private readonly HashSet<string> _thresholdIds = new();
    private readonly HashSet<string> _running = new();
    private bool _active;
    private (Mode Mode, AutomationRule Trigger)[] _live = Array.Empty<(Mode, AutomationRule)>();
    private (Mode Mode, AutomationRule Trigger)[] _thresholds = Array.Empty<(Mode, AutomationRule)>();
    private readonly DispatcherTimer _schedule = new(DispatcherPriority.Background);
    private readonly HashSet<PluginTrigger> _inUse = new();
    private readonly Dictionary<string, (string Signature, DateTime? ArmedAt)> _scheduled = new();
    private bool _scheduleSeen;
    private readonly DateTime _created = DateTime.Now;

    public AutomationService(AppServices app)
    {
        _app = app;
        _app.Watcher.Started += name => OnApp(TriggerType.AppStarts, name);
        _app.Watcher.Stopped += name => OnApp(TriggerType.AppCloses, name);
        _schedule.Tick += (_, _) => OnSchedule();
        AutomationCatalog.FindPluginTrigger = Find;
    }

    public IEnumerable<PluginTrigger> PluginTriggers => _app.Plugins.Active.SelectMany(p => p.Triggers(_app));

    public PluginTrigger? Find(string? id) => id is null ? null : PluginTriggers.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    public void Reschedule() => RescheduleTimer();

    private bool On => _active && _app.Config.ModeSwitching.Enabled;

    public Dictionary<string, RuleRun> LastRuns { get; } = new();

    public bool Paused => _guard.Tripped;

    public event Action? Changed;

    public void SetActive(bool active)
    {
        _active = active;
        Refresh();
    }

    public void Refresh()
    {
        _live = On
            ? _app.Config.Modes.SelectMany(m => m.Triggers.Where(t => AutomationCatalog.IsEvent(t.Trigger) && Live(t)).Select(t => (m, t))).ToArray()
            : Array.Empty<(Mode, AutomationRule)>();
        var triggers = _live.Select(x => x.Trigger).ToList();

        var apps = triggers.Where(t => AutomationCatalog.UsesApp(t.Trigger) && !string.IsNullOrWhiteSpace(t.App)).Select(t => t.App!)
            .Concat(triggers.Where(t => !string.IsNullOrWhiteSpace(t.OnlyIfRunning)).Select(t => t.OnlyIfRunning!)).ToList();
        if (apps.Count > 0) _app.Watcher.Acquire(Consumer, apps);
        else _app.Watcher.Release(Consumer);

        if (triggers.Any(t => AutomationCatalog.UsesTemperature(t.Trigger))) _app.Hardware.Acquire(Consumer);
        else _app.Hardware.Release(Consumer);

        _thresholds = _live.Where(x => AutomationCatalog.UsesThreshold(x.Trigger.Trigger)).ToArray();
        _app.Tick -= OnTick;
        if (_thresholds.Length > 0)
            _app.Tick += OnTick;

        var thresholdIds = _thresholds.Select(x => x.Trigger.Id).ToHashSet();
        _thresholdIds.IntersectWith(thresholdIds);
        _armed.IntersectWith(thresholdIds);
        foreach (var id in thresholdIds)
            if (_thresholdIds.Add(id))
                _armed.Add(id);

        var scheduled = triggers.Where(t => ScheduleTriggers.IsScheduled(t.Trigger)).ToList();
        var now = DateTime.Now;
        if (now - _created > TimeSpan.FromMinutes(1)) _scheduleSeen = true;
        foreach (var id in _scheduled.Keys.Where(id => !scheduled.Any(t => t.Id == id)).ToList()) _scheduled.Remove(id);
        foreach (var t in scheduled)
        {
            var signature = ScheduleTriggers.Signature(t);
            if (!_scheduled.TryGetValue(t.Id, out var seen) || seen.Signature != signature)
                _scheduled[t.Id] = (signature, _scheduleSeen ? now : null);
        }
        if (On) _scheduleSeen = true;

        var used = triggers.Where(t => t.Trigger == TriggerType.Plugin).Select(t => Find(t.PluginTrigger)).OfType<PluginTrigger>().ToHashSet();
        foreach (var t in _inUse.Except(used).ToList()) { _inUse.Remove(t); Safely(() => t.SetInUse(false)); }
        foreach (var t in used.Except(_inUse).ToList()) { _inUse.Add(t); Safely(() => t.SetInUse(true)); }
        RescheduleTimer();
        Changed?.Invoke();
    }

    public DateTime? NextDue(AutomationRule trigger) => DueAfter(trigger, DateTime.Now);

    private DateTime? DueAfter(AutomationRule trigger, DateTime now)
    {
        var from = ScheduleTriggers.SearchFrom(now, trigger.LastDue, _scheduled.TryGetValue(trigger.Id, out var seen) ? seen.ArmedAt : null);
        if (trigger.Trigger != TriggerType.Plugin) return ScheduleTriggers.NextDue(trigger, from);
        try { return Find(trigger.PluginTrigger)?.NextDue(trigger, from); }
        catch (Exception ex) { App.LogError(ex); return null; }
    }

    private static void Safely(Action act)
    {
        try { act(); }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void RescheduleTimer()
    {
        _schedule.Stop();
        var now = DateTime.Now;
        var next = Matching(ScheduleTriggers.IsScheduled).Select(x => DueAfter(x.Trigger, now)).Min();
        if (next is not { } due) return;
        var wait = due - now;
        _schedule.Interval = wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : wait;
        _schedule.Start();
    }

    private void OnSchedule()
    {
        var now = DateTime.Now;
        foreach (var (mode, trigger) in Matching(ScheduleTriggers.IsScheduled))
        {
            if (DueAfter(trigger, now) is { } due && due <= now)
            {
                trigger.LastDue = due;
                _app.Save();
                _ = FireAsync(mode, trigger);
            }
        }
        RescheduleTimer();
    }

    public void Resume()
    {
        _guard.Reset();
        Changed?.Invoke();
    }

    public void OnWindowsStartup()
    {
        foreach (var (mode, trigger) in Matching(t => t == TriggerType.WindowsStartup))
            _ = FireAsync(mode, trigger);
    }

    private IEnumerable<(Mode Mode, AutomationRule Trigger)> Matching(Func<TriggerType, bool> trigger) => _live.Where(x => trigger(x.Trigger.Trigger)).ToList();

    private bool Live(AutomationRule t) => t.Enabled && TriggerOn(t);

    public bool TriggerOn(AutomationRule t) => t.Trigger != TriggerType.Plugin || Find(t.PluginTrigger) is { IsOn: true };

    public string? TriggerOffNote(AutomationRule t) =>
        t.Trigger != TriggerType.Plugin ? null
        : Find(t.PluginTrigger) is { } p ? (p.IsOn ? null : p.OffNote)
        : $"It comes from a plugin that's off or no longer exists (“{t.PluginTrigger}”), so it waits until that's back.";

    private void OnApp(TriggerType type, string name)
    {
        if (_app.Watcher.CatchingUp) return;
        foreach (var (mode, trigger) in _live)
            if (trigger.Trigger == type && trigger.App is { } a && AppWatcher.Normalize(a) == name)
                _ = FireAsync(mode, trigger);
    }

    private void OnTick()
    {
        double idleMinutes = IdleTime.Current.TotalMinutes;
        var hw = _app.Hardware.Latest;
        foreach (var (mode, trigger) in _thresholds)
        {
            double? value = trigger.Trigger switch
            {
                TriggerType.Idle => idleMinutes,
                TriggerType.CpuTempAbove => hw?.CpuTemp,
                TriggerType.GpuTempAbove => hw?.GpuTemp,
                _ => null,
            };
            if (value is not { } v)
                continue;

            bool over = v >= trigger.Threshold;
            bool rearm = trigger.Trigger == TriggerType.Idle ? v < 0.1 : v <= trigger.Threshold - TempRearmC;
            if (rearm)
                _armed.Add(trigger.Id);
            else if (over && _armed.Remove(trigger.Id))
                _ = FireAsync(mode, trigger);
        }
    }

    private async Task FireAsync(Mode mode, AutomationRule trigger)
    {
        if (_running.Contains(trigger.Id) || !ConditionsHold(trigger))
            return;
        long now = Environment.TickCount64;
        if (_lastRun.TryGetValue(trigger.Id, out long last) && now - last < trigger.CooldownSeconds * 1000L)
            return;
        bool wasPaused = _guard.Tripped;
        if (!_guard.Allow())
        {
            if (!wasPaused)
                _app.Notify("Triggers paused",
                    $"Modes were triggered more than {MaxRunsPerMinute} times in a minute, which usually means two of them trigger each other. Check Automations and Modes, then resume there.",
                    NoticeKind.Warning, important: true);
            Changed?.Invoke();
            return;
        }

        _lastRun[trigger.Id] = now;
        _running.Add(trigger.Id);
        bool ok;
        try
        {
            if (trigger.Trigger == TriggerType.WindowsStartup && !mode.OneOff && _app.Switcher.InCharge is not null)
            {
                _app.Switcher.RestoreLater(mode.Name);
                ok = true;
            }
            else
                ok = await _app.Modes.ApplyAsync(mode);
        }
        finally
        {
            _running.Remove(trigger.Id);
        }
        LastRuns[trigger.Id] = new RuleRun(DateTime.Now, ok ? null : "Some settings weren't applied (see the notification).");
        Changed?.Invoke();
    }

    private bool ConditionsHold(AutomationRule trigger)
    {
        if (!string.IsNullOrWhiteSpace(trigger.OnlyInMode) && !string.Equals(_app.Modes.Current, trigger.OnlyInMode, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(trigger.OnlyIfRunning) && !_app.Watcher.IsRunning(trigger.OnlyIfRunning))
            return false;
        return true;
    }
}

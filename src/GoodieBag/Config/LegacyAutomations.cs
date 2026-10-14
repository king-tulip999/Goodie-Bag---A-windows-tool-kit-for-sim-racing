using GoodieBag.Automations;
using GoodieBag.Processes;

namespace GoodieBag.Config;

internal static class LegacyAutomations
{
    public static void Migrate(AppConfig config)
    {
        var switching = config.ModeSwitching;
        if (config.Apps is null && config.Automations is null && switching.StartupMode is null) return;
        bool modesPlugin = Plugins.PluginStates.IsEnabled(config.Plugins, Plugins.PluginIds.Modes, byDefault: true);
        bool appsOn = switching.Enabled && modesPlugin;
        bool rulesOn = (config.Automations?.Enabled ?? true) && Plugins.PluginStates.IsEnabled(config.Plugins, "automations", byDefault: true);
        var startup = modesPlugin ? Find(config, switching.StartupMode) : null;
        switching.Enabled = appsOn || rulesOn || startup is not null;
        bool master = switching.Enabled;

        startup?.Triggers.Add(new AutomationRule { Trigger = TriggerType.WindowsStartup, CooldownSeconds = 0 });
        switching.StartupMode = null;

        foreach (var app in config.Apps ?? new())
            MigrateApp(config, app, app.Enabled && (appsOn || !master));
        config.Apps = null;

        foreach (var rule in config.Automations?.Rules ?? new())
            MigrateRule(config, rule, rule.Enabled && (rulesOn || !master));
        config.Automations = null;
    }

    private static void MigrateApp(AppConfig config, AppProfile app, bool enabled)
    {
        string process = AppWatcher.Normalize(app.Process ?? "");
        if (process.Length == 0) return;
        var trigger = new AutomationRule { Trigger = TriggerType.WhileRunning, App = process, Name = app.Name, Enabled = enabled, CooldownSeconds = 0 };
        var mode = Find(config, app.ModeName);
        var o = app.Overrides ?? new ModeOverrides();
        if (o.IsEmpty)
        {
            mode?.Triggers.Add(trigger);
            return;
        }
        var own = mode is null ? new Mode() : ConfigStore.Clone(mode);
        own.Name = Unique(config, mode is null ? app.Label : $"{mode.Name} · {app.Label}");
        own.Hotkey = null;
        own.Program = null;
        own.OneOff = false;
        own.Triggers = new() { trigger };
        if (o.DisplayProfile is { } display) own.DisplayProfile = display;
        if (o.Hdr is HdrMode.On or HdrMode.Off) own.DisplayProfile = HdrProfile(config, own.DisplayProfile, o.Hdr.Value);
        if (o.Audio is { } audio) own.Audio = audio;
        if (o.Volume is { } volume) own.Volume = volume;
        if (o.Overlay is { } overlay) own.Overlay = overlay;
        if (o.OverlayLayout is { } layout) own.OverlayLayout = layout;
        if (o.FanPreset is { } fans) own.FanPreset = fans;
        config.Modes.Add(own);
    }

    private static void MigrateRule(AppConfig config, AutomationRule rule, bool enabled)
    {
        var steps = (rule.Actions ?? new()).Where(a => a.Type is not (ActionType.PowerPlan or ActionType.WindowProfile)).ToList();
        bool manual = rule.Trigger == TriggerType.Manual;
        string? hotkey = string.IsNullOrWhiteSpace(rule.Hotkey) ? null : rule.Hotkey;
        var targets = steps.Where(a => a.Type == ActionType.ActivateMode).Select(a => Find(config, a.Target)).OfType<Mode>().Distinct().ToList();
        var others = steps.Where(a => a.Type != ActionType.ActivateMode).ToList();
        bool first = true;

        AutomationRule Trigger()
        {
            var t = ConfigStore.Clone(rule);
            if (!first) t.Id = Guid.NewGuid().ToString("N");
            first = false;
            t.Name = null;
            t.Actions = null;
            t.Hotkey = null;
            t.Enabled = enabled;
            return t;
        }

        foreach (var mode in targets)
        {
            if (!manual) mode.Triggers.Add(Trigger());
            else if (others.Count == 0 && hotkey is not null && mode.Hotkey is null && !HotkeyUsed(config, hotkey)) mode.Hotkey = hotkey;
        }
        if (others.Count == 0) return;

        var oneOff = new Mode { Name = Unique(config, string.IsNullOrWhiteSpace(rule.Name) ? DefaultName(rule) : rule.Name), OneOff = true };
        bool? hdr = null;
        var notes = new List<string>();
        foreach (var a in others)
        {
            switch (a.Type)
            {
                case ActionType.LaunchApp when !string.IsNullOrWhiteSpace(a.Target):
                    oneOff.Launch.Add(new LaunchItem { Path = a.Target!, Arguments = a.Argument });
                    break;
                case ActionType.CloseApp when !string.IsNullOrWhiteSpace(a.Target):
                    oneOff.Close.Add(AppWatcher.Normalize(a.Target!));
                    if (a.Value == 1) oneOff.ForceClose = true;
                    break;
                case ActionType.FanPreset: oneOff.FanPreset = a.Target; break;
                case ActionType.Overlay: oneOff.Overlay = a.Value switch { 1 => OverlayMode.Show, 2 => OverlayMode.Toggle, _ => OverlayMode.Hide }; break;
                case ActionType.OverlayLayout: oneOff.OverlayLayout = a.Target; break;
                case ActionType.DisplayLayout: oneOff.DisplayProfile = a.Target; break;
                case ActionType.Hdr: hdr = a.Value == 1; break;
                case ActionType.AudioDevice when !string.IsNullOrWhiteSpace(a.Target):
                    oneOff.Audio = new AudioSnapshot { PlaybackId = a.Target, PlaybackName = a.Argument };
                    break;
                case ActionType.Volume: oneOff.Volume = Math.Clamp(a.Value, 0, 100); break;
                case ActionType.Notify:
                    foreach (var text in new[] { a.Target, a.Argument })
                        if (!string.IsNullOrWhiteSpace(text)) notes.Add(text!.Trim());
                    break;
            }
        }
        if (hdr is { } on) oneOff.DisplayProfile = HdrProfile(config, oneOff.DisplayProfile, on ? HdrMode.On : HdrMode.Off);
        if (notes.Count > 0) oneOff.Message = string.Join("\n", notes);
        if (!manual) oneOff.Triggers.Add(Trigger());
        else if (hotkey is not null && !HotkeyUsed(config, hotkey)) oneOff.Hotkey = hotkey;
        config.Modes.Add(oneOff);
    }

    private static string DefaultName(AutomationRule rule) => rule.Trigger switch
    {
        TriggerType.Plugin => rule.PluginTrigger ?? "Automation",
        TriggerType.Manual => "Automation",
        _ => AutomationCatalog.When(rule),
    };

    private static string HdrProfile(AppConfig config, string? profile, HdrMode hdr)
    {
        string state = hdr == HdrMode.On ? "HDR on" : "HDR off";
        var profiles = config.Displays.Profiles;
        var source = profiles.FirstOrDefault(p => Same(p.Name, profile));
        if (source?.Hdr == hdr) return source.Name;
        string name = source is null ? state : $"{source.Name} · {state}";
        if (profiles.FirstOrDefault(p => Same(p.Name, name)) is { } existing) return existing.Name;
        var made = source is null ? new DisplayProfile() : ConfigStore.Clone(source);
        made.Name = name;
        made.Hotkey = null;
        made.Hdr = hdr;
        profiles.Add(made);
        return name;
    }

    private static bool HotkeyUsed(AppConfig config, string hotkey) =>
        config.Modes.Any(m => Hotkeys.Hotkey.SameKey(m.Hotkey, hotkey)) || config.Displays.Profiles.Any(p => Hotkeys.Hotkey.SameKey(p.Hotkey, hotkey));

    private static Mode? Find(AppConfig config, string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : config.Modes.FirstOrDefault(m => Same(m.Name, name));

    internal static string Unique(AppConfig config, string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "Mode" : name.Trim();
        string candidate = name;
        for (int i = 2; config.Modes.Any(m => Same(m.Name, candidate)); i++)
            candidate = $"{name} {i}";
        return candidate;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

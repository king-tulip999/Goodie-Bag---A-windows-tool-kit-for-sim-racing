using GoodieBag.Config;
using GoodieBag.Modes;
using GoodieBag.Services;
using GoodieBag.Views.Pages;
using WinForms = System.Windows.Forms;

namespace GoodieBag.Plugins.BuiltIn;

internal sealed class AutomationsAndModesPlugin : Plugin
{
    public override string Id => PluginIds.Modes;
    public override string Name => "Automations and Modes";
    public override string Description => "Modes are whole-PC setups (displays, audio, program windows, fans, overlay, programs) applied in one click, by keybind, or by their own triggers: while a program runs, when it starts or closes, on a schedule, when the PC is idle or hot. A mode with a game launches it too; a one-off mode just runs its changes (e.g. idle → quiet fans).";
    public override string Icon => "I.Layers";
    public override (string Soft, string Deep) Colors => ("AccentSoft", "AccentText");
    public override int Order => 20;
    public override string Reads =>
        "Which programs start and close (program triggers, window placement); a mode capture reads the current display profile or audio devices; temperature and idle triggers read CPU / GPU temperature and idle time.";
    public override string BackgroundWork =>
        "Only for what you set up: switched-on triggers (Windows' process events, sensors for temperature triggers, the idle timer, one timer for schedules), a mode's window placement or a launched game's helpers. Nothing set up: never.";
    public override string Changes =>
        "When a mode is applied, launched or triggered: display profile, HDR, audio devices and volume, program windows, fan preset, overlay, and programs started or closed.";

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage("modes", "Automations and Modes", "I.Layers", (app, window) => new ModesPage(app, window)),
    };

    public override IEnumerable<PluginHotkey> Hotkeys(AppServices app)
    {
        var k = app.Config.Hotkeys;
        void Step(int delta)
        {
            if (HotkeyCatalog.Step(app.Config.Modes.Where(m => !m.OneOff).ToList(), app.Modes.Active, delta) is { } mode)
                _ = app.Modes.ApplyAsync(mode);
        }
        yield return new PluginHotkey("modes.next", "Next mode", () => k.NextMode, hk => k.NextMode = hk, () => Step(1), "Switch modes");
        yield return new PluginHotkey("modes.previous", "Previous mode", () => k.PreviousMode, hk => k.PreviousMode = hk, () => Step(-1), "Switch modes");
    }

    public override void AddTrayItems(WinForms.ToolStripItemCollection items, AppServices app)
    {
        var games = app.Config.Modes.Where(m => m.Program is not null).ToList();
        if (games.Count > 0)
        {
            var launch = new WinForms.ToolStripMenuItem("Launch");
            foreach (var mode in games)
            {
                var m = mode;
                launch.DropDownItems.Add(new WinForms.ToolStripMenuItem(m.Name, null, (_, _) => _ = ModeLauncher.LaunchAsync(app, m)));
            }
            items.Add(launch);
        }
    }

    public override string? Status(AppServices app)
    {
        int n = app.Config.Modes.Count;
        if (n == 0) return "No modes yet";
        string modes = app.Modes.Active is { } active ? $"{n} mode{(n == 1 ? "" : "s")}, {active.Name} active" : $"{n} mode{(n == 1 ? "" : "s")}";
        int triggers = app.Config.Modes.Sum(m => m.Triggers.Count(t => t.Enabled));
        string auto = !app.Config.ModeSwitching.Enabled ? "triggers off"
            : app.Automations.Paused || app.Switcher.Paused ? "triggers paused: modes kept triggering each other"
            : triggers == 0 ? "no triggers" : $"{triggers} trigger{(triggers == 1 ? "" : "s")} on";
        return $"{modes} · {auto}";
    }

    protected override void OnStart(AppServices app)
    {
        app.Switcher.Refresh();
        app.WindowRules.Refresh();
        app.Automations.SetActive(true);
    }

    protected override void OnStop(AppServices app)
    {
        app.Switcher.Refresh();
        app.WindowRules.Refresh();
        app.Automations.SetActive(false);
    }
}

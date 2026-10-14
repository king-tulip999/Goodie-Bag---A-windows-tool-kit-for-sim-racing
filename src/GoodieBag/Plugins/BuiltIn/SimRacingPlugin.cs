using GoodieBag.Services;
using GoodieBag.SimRacing;
using GoodieBag.SimRacing.Hud;
using WinForms = System.Windows.Forms;
using GoodieBag.Views.Pages;

namespace GoodieBag.Plugins.BuiltIn;

internal sealed class SimRacingPlugin : Plugin
{
    public override string Id => PluginIds.SimRacing;
    public override string Name => "Sim Racing Tools";
    public override string Description => "iRacing Overlay (in-game widgets from iRacing's live telemetry: Relative, Standings, fuel, delta, track map, alerts and more), FOV and triple-screen calculator, iRacing Controller for iRacing's settings (plain-language editing, health check, backups), the iRacing Setup Manager for car setups and launchers for Trading Paints and Garage 61.";
    public override string Icon => "I.FormulaWheel";
    public override (string Soft, string Deep) Colors => ("CoralSoft", "CoralDeep");
    public override int Order => 10;
    public override string Reads =>
        "With the iRacing Overlay on: iRacing's live telemetry (its shared memory, read only) while iRacing runs, and whether iRacing is running. iRacing's settings files and setups folder, and recent setups in Downloads, while its page is open. With “Launch when iRacing starts” on: whether iRacing is running.";
    public override string BackgroundWork =>
        "With the iRacing Overlay switched on: waits for iRacing to start (process events), then reads its telemetry about 60 times a second and draws the widgets at their own rates; while iRacing is closed it only waits. Hiding a panel under Panels turns that tool off like a plugin (its background work and keybinds go with it). Only with “Launch when iRacing starts” on for Trading Paints or Garage 61: watches for iRacing starting (process events, no polling). While the Setup Manager is open on screen: file events for the setups folder and Downloads. Otherwise never.";
    public override string Changes =>
        "The iRacing Overlay draws its own windows on top of the game; nothing is injected into iRacing or written to it (track outlines it records are saved in Goodie Bag's folder). Only when you ask: iRacing's settings files when you save in iRacing Controller or write the FOV (backed up first), setup files and folders you import, rename, move, delete (to the Recycle Bin) or export in the Setup Manager, and starting or closing Trading Paints / Garage 61.";

    public static bool FeatureOn(AppServices app, string panel) =>
        app.Plugins.IsActive(PluginIds.SimRacing) && !app.Config.SimRacing.IsPanelHidden(panel);

    public static void PanelsChanged(AppServices app)
    {
        if (app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin { } plugin)
        {
            plugin.Hud?.SetActive(FeatureOn(app, "hud"));
            plugin.TradingPaints?.SetActive(FeatureOn(app, "tradingpaints"));
            plugin.Garage61?.SetActive(FeatureOn(app, "garage61"));
        }
        app.Plugins.RaiseChanged();
    }

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage("simracing", "Sim Racing Tools", "I.FormulaWheel", (app, window) => new SimRacingPage(app, window)),
    };

    public HudHost? Hud { get; private set; }

    public override IEnumerable<PluginHotkey> Hotkeys(AppServices app)
    {
        if (!FeatureOn(app, "hud")) yield break;
        var h = app.Config.SimRacing.Hud;
        yield return new PluginHotkey("hud.toggle", "Show / hide the iRacing Overlay", () => h.ToggleHotkey, k => h.ToggleHotkey = k, () => Hud?.ToggleHidden(), "iRacing Overlay", "Ctrl+Alt+H");
        yield return new PluginHotkey("hud.edit", "Arrange the iRacing Overlay", () => h.EditHotkey, k => h.EditHotkey = k, () =>
        {
            if (Hud is null) return;
            if (Hud.Editing) Hud.EndEdit(keep: true); else Hud.BeginEdit();
        }, "iRacing Overlay");
        yield return new PluginHotkey("hud.quickedit", "In-game editor for the iRacing Overlay", () => h.QuickEditHotkey, k => h.QuickEditHotkey = k, () =>
        {
            if (Hud is { } hud) GoodieBag.Views.HudQuickEditor.Toggle(app, hud);
        }, "iRacing Overlay", Config.RaceHudSettings.QuickEditDefault);
        yield return new PluginHotkey("hud.layout", "Next iRacing Overlay layout", () => h.NextLayoutHotkey, k => h.NextLayoutHotkey = k, () => Hud?.NextLayout(), "iRacing Overlay");
        yield return new PluginHotkey("hud.layout.previous", "Previous iRacing Overlay layout", () => h.PreviousLayoutHotkey, k => h.PreviousLayoutHotkey = k, () => Hud?.PreviousLayout(), "iRacing Overlay");
        yield return new PluginHotkey("hud.dismiss", "Dismiss iRacing Overlay alerts", () => h.DismissHotkey, k => h.DismissHotkey = k, () => Hud?.DismissAlerts(), "iRacing Overlay");
    }

    public override void AddTrayItems(WinForms.ToolStripItemCollection items, AppServices app)
    {
        if (!FeatureOn(app, "hud")) return;
        var h = app.Config.SimRacing.Hud;
        var item = new WinForms.ToolStripMenuItem("iRacing Overlay") { Checked = h.Enabled, ShortcutKeyDisplayString = GoodieBag.Hotkeys.Hotkey.Display(h.ToggleHotkey) };
        item.Click += (_, _) => Hud?.SetEnabled(!h.Enabled);
        items.Add(item);
    }

    public override string? Status(AppServices app) =>
        !FeatureOn(app, "hud") || Hud is null ? null
        : !app.Config.SimRacing.Hud.Enabled ? "iRacing Overlay off"
        : Hud.OnScreen ? $"iRacing Overlay on screen · {Hud.LayoutName}"
        : app.Telemetry.Status switch
        {
            SimRacing.Telemetry.SimStatus.Connected => "iRacing Overlay on · iRacing connected",
            SimRacing.Telemetry.SimStatus.Waiting => "iRacing Overlay on · iRacing loading",
            _ => "iRacing Overlay on · waiting for iRacing",
        };

    public CompanionApp? TradingPaints { get; private set; }

    public CompanionApp? Garage61 { get; private set; }

    protected override void OnStart(AppServices app)
    {
        Hud = new HudHost(app);
        Hud.SetActive(FeatureOn(app, "hud"));
        TradingPaints = new CompanionApp(app, CompanionApp.TradingPaints, () => app.Config.SimRacing.TradingPaints);
        Garage61 = new CompanionApp(app, CompanionApp.Garage61, () => app.Config.SimRacing.Garage61);
        if (FeatureOn(app, "tradingpaints")) TradingPaints.Start(); else TradingPaints.SetActive(false);
        if (FeatureOn(app, "garage61")) Garage61.Start(); else Garage61.SetActive(false);
    }

    protected override void OnStop(AppServices app)
    {
        Hud?.Dispose();
        Hud = null;
        TradingPaints?.Stop();
        Garage61?.Stop();
        TradingPaints = Garage61 = null;
    }
}

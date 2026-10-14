using GoodieBag.Services;
using GoodieBag.Views;
using WinForms = System.Windows.Forms;

namespace GoodieBag.Plugins.BuiltIn;

internal sealed class OverlayPlugin : Plugin
{
    public override string Id => PluginIds.Overlay;
    public override string Name => "Performance Overlay";
    public override string Description => "FPS, temperatures, load and fan speeds drawn on top of your game, toggled with a keybind. Set it up in Hardware Monitor → Performance Overlay.";
    public override string Icon => "I.Overlay";
    public override (string Soft, string Deep) Colors => ("ApricotSoft", "ApricotDeep");
    public override int Order => 55;
    public override string Reads => "The sensors and frame rate it shows, at its update rate, only while it's on screen.";
    public override string BackgroundWork =>
        "Only while the overlay is on screen. While it's hidden, only its keybinds are registered.";
    public override string Changes => "Nothing. It draws its own window on top of your game; nothing is injected into the game.";

    public override IReadOnlyList<PluginPage> Pages { get; } = Array.Empty<PluginPage>();

    public override IEnumerable<SearchEntry> SearchEntries { get; } = new SearchEntry[]
    {
        new("Performance Overlay", "FPS and sensors drawn over your game (Hardware Monitor → Performance Overlay).", "Performance Overlay", "monitor", "monitor.overlay", "osd fps hud"),
        new("Show overlay", "Turn the overlay on or off (the switch on its preview; also its keybind and the tray).", "Performance Overlay", "monitor", "overlay.visible", "toggle on off show hide osd hud"),
        new("Overlay keybind", "Show / hide the overlay from anywhere.", "Performance Overlay", "monitor", "overlay.hotkey", "hotkey shortcut toggle"),
        new("Reset FPS statistics keybind", "Start the overlay FPS average and 1% low over from anywhere.", "Performance Overlay", "monitor", "overlay.fpshotkey", "hotkey shortcut fps reset"),
        new("Overlay update rate", "How often overlay values refresh.", "Performance Overlay", "monitor", "overlay.rate", "refresh interval"),
        new("Overlay layouts", "Different overlay setups per mode or app.", "Performance Overlay", "monitor", "overlay.layouts", "profile configuration"),
        new("Overlay metrics", "Switch FPS, 1% low, temps, clocks, power, network… on or off; drag them in the preview to reorder", "Performance Overlay", "monitor", "overlay.metrics", "fps frametime cpu gpu ram vram ping order arrange drag"),
        new("Overlay position", "Pick the corner and monitor the overlay sits on.", "Performance Overlay", "monitor", "overlay.position", "corner monitor move place"),
        new("Overlay style", "Compact or expanded layout.", "Performance Overlay", "monitor", "overlay.style", "compact expanded"),
        new("Overlay names", "Your own names for the groups and values the overlay shows, e.g. GPU → 7900 XTX, or the hotspot tag.", "Performance Overlay", "monitor", "overlay.names", "rename name label gpu cpu custom text tag hotspot hot spot"),
        new("Overlay spacing", "Horizontal and vertical gaps between the overlay's values and rows.", "Performance Overlay", "monitor", "overlay.spacing", "spacing gap padding horizontal vertical rows tight loose"),
        new("Overlay text size", "Font size of the overlay.", "Performance Overlay", "monitor", "overlay.font", "font scale"),
        new("Overlay opacity", "Transparency, background opacity and spacing.", "Performance Overlay", "monitor", "overlay.opacity", "transparency background spacing"),
        new("Overlay frame-time graph", "Small graph of recent frame times.", "Performance Overlay", "monitor", "overlay.graph", "frametime stutter graph"),
        new("Overlay colors", "Same as the app, one of the app's themes, or your own colors.", "Performance Overlay", "monitor", "overlay.colors", "color color theme text accent warning critical"),
    };

    public override IEnumerable<PluginHotkey> Hotkeys(AppServices app)
    {
        var o = app.Config.Overlay;
        var k = app.Config.Hotkeys;
        yield return new PluginHotkey("overlay", "Show / hide overlay", () => o.Hotkey, hk => o.Hotkey = hk, () => app.SetOverlayVisible(!o.Visible), Default: "Ctrl+Alt+O");
        yield return new PluginHotkey("overlay.fps", "Reset FPS statistics", () => o.ResetFpsHotkey, hk => o.ResetFpsHotkey = hk, app.ResetFpsStats);
        yield return new PluginHotkey("overlay.layout", "Next overlay layout", () => k.NextOverlayLayout, hk => k.NextOverlayLayout = hk, () =>
        {
            if (HotkeyCatalog.Step(o.Layouts, o.Layouts.FirstOrDefault(l => l.Name == o.ActiveLayout), 1) is { } layout && app.UseOverlayLayout(layout.Name))
                app.Notify("Overlay layout applied", layout.Name, NoticeKind.Success);
        });
    }

    public override void AddTrayItems(WinForms.ToolStripItemCollection items, AppServices app)
    {
        var o = app.Config.Overlay;
        var item = new WinForms.ToolStripMenuItem("Show overlay") { Checked = o.Visible, ShortcutKeyDisplayString = GoodieBag.Hotkeys.Hotkey.Display(o.Hotkey) };
        item.Click += (_, _) => app.SetOverlayVisible(!o.Visible);
        items.Add(item);
    }

    public override string? Status(AppServices app) => app.Overlay.Showing ? "On screen"
        : app.Config.Overlay.Visible && app.Demo ? "Switched on (not drawn in demo mode)"
        : "Hidden";

    protected override void OnStart(AppServices app) => app.Overlay.Enabled = true;

    protected override void OnStop(AppServices app) => app.Overlay.Enabled = false;
}

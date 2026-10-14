using GoodieBag.Services;
using GoodieBag.Views.Pages;
using WinForms = System.Windows.Forms;

namespace GoodieBag.Plugins.BuiltIn;

internal sealed class FanControlPlugin : Plugin
{
    public override string Id => PluginIds.FanControl;
    public override string Name => "Fan Control";
    public override string Description => "Control case, CPU and GPU fans with curves, presets and calibration, with an emergency full-speed failsafe.";
    public override string Icon => "I.Fan";
    public override (string Soft, string Deep) Colors => ("SageSoft", "SageDeep");
    public override int Order => 30;
    public override string Reads => "CPU, GPU and motherboard temperatures and fan speeds, once a second while fan control is switched on.";
    public override string BackgroundWork =>
        "While fan control is switched on: reads temperatures once a second and drives your fans. Switched off: never.";
    public override string Changes =>
        "Fan speeds, while fan control is switched on. Turning the plugin (or fan control) off hands every fan back to the BIOS / GPU driver.";

    public override IEnumerable<PluginHotkey> Hotkeys(AppServices app)
    {
        var k = app.Config.Hotkeys;
        var fans = app.Config.Fans;
        void Step(int delta)
        {
            var current = fans.Presets.FirstOrDefault(p => p.Name == fans.ActivePreset);
            if (HotkeyCatalog.Step(fans.Presets, current, delta) is { } preset)
            {
                app.SetFanPreset(preset.Name);
                app.Notify("Fan preset applied", preset.Name, NoticeKind.Success);
            }
        }
        yield return new PluginHotkey("fans.toggle", "Fan control on / off", () => k.FanControl, hk => k.FanControl = hk, () => app.SetFanControlEnabled(!fans.Enabled));
        yield return new PluginHotkey("fans.next", "Next preset", () => k.NextFanPreset, hk => k.NextFanPreset = hk, () => Step(1), "Switch presets");
        yield return new PluginHotkey("fans.previous", "Previous preset", () => k.PreviousFanPreset, hk => k.PreviousFanPreset = hk, () => Step(-1), "Switch presets");
        foreach (var preset in fans.Presets)
        {
            var p = preset;
            yield return new PluginHotkey("fanpreset:" + p.Name, p.Name, () => p.Hotkey, hk => p.Hotkey = hk, () => Step0(p), "Apply a preset");
        }
        void Step0(Config.FanPreset p)
        {
            if (!fans.Presets.Contains(p)) return;
            app.SetFanPreset(p.Name);
            app.Notify("Fan preset applied", p.Name, NoticeKind.Success);
        }
    }

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage("fans", "Fan Control", "I.Fan", (app, window) => new FansPage(app, window), PageSensors.Basic),
    };


    public override void AddTrayItems(WinForms.ToolStripItemCollection items, AppServices app)
    {
        var fans = app.Config.Fans;
        var toggle = new WinForms.ToolStripMenuItem("Fan control") { Checked = fans.Enabled };
        toggle.Click += (_, _) => app.SetFanControlEnabled(!fans.Enabled);
        items.Add(toggle);

        if (fans.Presets.Count > 1)
        {
            var presets = new WinForms.ToolStripMenuItem("Fan preset");
            foreach (var preset in fans.Presets)
            {
                var item = new WinForms.ToolStripMenuItem(preset.Name) { Checked = preset.Name == fans.ActivePreset };
                var name = preset.Name;
                item.Click += (_, _) => app.SetFanPreset(name);
                presets.DropDownItems.Add(item);
            }
            items.Add(presets);
        }
    }

    public override string? Status(AppServices app) =>
        !app.Config.Fans.Enabled ? "Fan control switched off · BIOS / driver in charge"
        : app.Hardware.Catalog?.Emergency == true ? "Emergency: fans at 100%"
        : $"Following preset “{app.Config.Fans.ActivePreset}”";

    protected override void OnStart(AppServices app) => app.SetFanControlAvailable(true);

    protected override void OnStop(AppServices app) => app.SetFanControlAvailable(false);
}

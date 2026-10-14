using GoodieBag.Services;
using GoodieBag.Views.Pages;

namespace GoodieBag.Plugins.BuiltIn;

internal sealed class DisplaysPlugin : Plugin
{
    public override string Id => PluginIds.Displays;
    public override string Name => "Displays";
    public override string Description => "Display profiles (monitors, arrangement, HDR, taskbar, wallpapers, audio) with keybinds, and the live arrangement.";
    public override string Icon => "I.Monitor";
    public override (string Soft, string Deep) Colors => ("SlateSoft", "SlateDeep");
    public override int Order => 40;
    public override string Reads =>
        "Your monitors (arrangement, resolution, refresh rate, HDR), taskbar, wallpapers and audio devices, only while its page is open or a profile is applied.";
    public override string BackgroundWork => "Never. Only its profile keybinds are registered.";
    public override string Changes =>
        "Only when you apply a display profile: monitor arrangement, resolution, HDR, taskbar, wallpapers and default audio device.";

    public override IEnumerable<PluginHotkey> Hotkeys(AppServices app)
    {
        var k = app.Config.Hotkeys;
        void Step(int delta)
        {
            if (HotkeyCatalog.Step(app.Config.Displays.Profiles, app.DisplayProfiles.Active, delta) is { } profile)
                _ = app.DisplayProfiles.ApplyAsync(profile);
        }
        yield return new PluginHotkey("displays.next", "Next display profile", () => k.NextDisplayProfile, hk => k.NextDisplayProfile = hk, () => Step(1), "Switch profiles");
        yield return new PluginHotkey("displays.previous", "Previous display profile", () => k.PreviousDisplayProfile, hk => k.PreviousDisplayProfile = hk, () => Step(-1), "Switch profiles");
    }

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage("displays", "Displays", "I.Monitor", (app, window) => new DisplaysPage(app, window)),
    };

    public override string? Status(AppServices app)
    {
        int profiles = app.Config.Displays.Profiles.Count;
        return $"{profiles} display profile{(profiles == 1 ? "" : "s")}";
    }

    protected override void OnStart(AppServices app) { }

    protected override void OnStop(AppServices app) { }
}

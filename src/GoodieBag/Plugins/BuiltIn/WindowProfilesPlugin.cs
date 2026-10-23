using GoodieBag.Processes;
using GoodieBag.Services;
using GoodieBag.Views;
using GoodieBag.Views.Pages;
using GoodieBag.WindowProfiles;
using WinForms = System.Windows.Forms;

namespace GoodieBag.Plugins.BuiltIn;

internal sealed class WindowProfilesPlugin : Plugin
{
    public const string PageIcon = "I.Triple";

    public override string Id => PluginIds.WindowProfiles;
    public override string Name => "Window Profiles";
    public override string Description => "Pick a program and give its window an exact size and spot: a game stretched across triple screens, a fixed resolution, borderless. Applied when the program starts, with a keybind, from the tray or by one click; groups move several programs at once.";
    public override string Icon => PageIcon;
    public override (string Soft, string Deep) Colors => ("AccentSoft", "AccentText");
    public override int Order => 20;
    public override string Reads => "Which programs start and close (only the ones with an automatic profile), and the size and spot of their windows.";
    public override string BackgroundWork => "Only with automatic profiles: Windows' program start events for those programs, and for about 35 s after a profile is applied a check once a second that the game didn't move its window back. Nothing set up: never.";
    public override string Changes => "The size, position and borders of the windows your profiles name. Nothing else.";

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage(PluginIds.WindowProfilesPage, "Window Profiles", PageIcon, (app, window) => new WindowProfilesPage(app, window)),
    };

    public override IEnumerable<SearchEntry> SearchEntries { get; } = new SearchEntry[]
    {
        new("Window profiles", "Force a program's window to an exact size and spot: a game across triple screens, a fixed resolution, borderless.", "Window Profiles", PluginIds.WindowProfilesPage, null, "resize window size position borderless triple stretch span game resolution custom resolution windowed"),
        new("Apply window profiles automatically", "Apply each automatic profile when its program starts.", "Window Profiles", PluginIds.WindowProfilesPage, WindowProfilesPage.AutoAnchor, "auto process watcher launch start detect"),
        new("Window profile groups", "One keybind or click moves every running program in the group.", "Window Profiles", PluginIds.WindowProfilesPage, null, "group several multiple programs keybind"),
    };

    public override void AddTrayItems(WinForms.ToolStripItemCollection items, AppServices app)
    {
        var s = app.Config.WindowProfiles;
        if (s.Profiles.Count == 0) return;
        var menu = new WinForms.ToolStripMenuItem("Window profiles");
        var running = AppWatcher.Snapshot();
        foreach (var group in s.Groups.Where(g => s.Members(g).Any()))
        {
            var g = group;
            menu.DropDownItems.Add(new WinForms.ToolStripMenuItem(g.Name, null, (_, _) => _ = app.WindowProfiles.ApplyGroupAsync(g))
            {
                ShortcutKeyDisplayString = GoodieBag.Hotkeys.Hotkey.Display(g.Hotkey),
                ToolTipText = "Moves every running program in this group",
            });
        }
        if (menu.DropDownItems.Count > 0) menu.DropDownItems.Add(new WinForms.ToolStripSeparator());
        foreach (var profile in s.Profiles.OrderByDescending(p => WindowProfileService.IsRunning(p, running)))
        {
            var p = profile;
            bool on = WindowProfileService.IsRunning(p, running);
            menu.DropDownItems.Add(new WinForms.ToolStripMenuItem(p.Name, null, (_, _) => _ = app.WindowProfiles.ApplyAsync(p))
            {
                ShortcutKeyDisplayString = GoodieBag.Hotkeys.Hotkey.Display(p.Hotkey),
                Enabled = on,
                ToolTipText = on ? WindowProfileService.Describe(p) : $"{p.ProcessName} isn't running",
            });
        }
        items.Add(menu);
    }

    public override string? Status(AppServices app)
    {
        var s = app.Config.WindowProfiles;
        int n = s.Profiles.Count;
        if (n == 0) return "No profiles yet";
        int auto = s.Profiles.Count(p => p.Auto);
        string count = SimRacing.Race.RaceFormat.Count(n, "profile");
        return !s.AutoApply ? $"{count} · applying automatically is off" : auto == 0 ? count : $"{count}, {auto} automatic";
    }

    protected override void OnStart(AppServices app) => app.WindowProfiles.Refresh();

    protected override void OnStop(AppServices app) => app.WindowProfiles.Refresh();
}

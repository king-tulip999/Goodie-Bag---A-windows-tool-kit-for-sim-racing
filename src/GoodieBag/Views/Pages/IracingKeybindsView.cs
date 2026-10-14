using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.SimRacing.IracingFiles;

namespace GoodieBag.Views.Pages;

internal static class IracingKeybindsView
{
    public static UIElement Build(AppServices app, Workspace ws)
    {
        var host = new Border();
        void Rebuild() => host.Child = Content(app, ws, Rebuild);
        Rebuild();
        return host;
    }

    private static UIElement Content(AppServices app, Workspace ws, Action rebuild)
    {
        var stack = new StackPanel();
        var keys = app.IracingKeys;
        keys.Refresh();

        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = "iRacing keybinds", Style = Ui.Res<Style>("H3") });
        info.Children.Add(new TextBlock
        {
            Text = "The keys iRacing listens for, read from its controls file" + (keys.FilePath is { } path ? $" ({path})" : "") +
                   ". A change is saved straight away (a backup is made first) and only while iRacing is closed, because iRacing saves its own copy when it exits. " +
                   "Wheel and button-box bindings are shown but set in iRacing. To bind a number-pad key, turn Num Lock off first.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        if (ProfileRow(app, rebuild) is { } row) info.Children.Add(row);
        if (keys.CarFiles > 0)
            info.Children.Add(Note($"{keys.CarFiles} car{(keys.CarFiles == 1 ? " uses its" : "s use their")} own controls (setups folder); those aren't changed here."));
        if (ws.SimRunning)
            info.Children.Add(Note("iRacing is running: close it to change keybinds.", warning: true));
        if (keys.Error is { } error)
            info.Children.Add(Note(error, warning: true));
        stack.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 12), Child = info });

        if (keys.File is not null)
            stack.Children.Add(new KeybindTree(app, e => e.Owner == HotkeyOwner.Iracing, skip: 1));
        return stack;
    }

    public static FrameworkElement? ProfileRow(AppServices app, Action changed)
    {
        var keys = app.IracingKeys;
        if (keys.Profiles.Count == 0) return null;
        var choices = keys.Profiles.Select(p => new Choice(p, p == keys.DefaultProfile ? p + " (iRacing's default)" : p)).ToList();
        choices.Add(new Choice("", "No profile (Documents\\iRacing\\controls.cfg)"));
        var pick = Ui.Combo(choices, keys.Profile ?? "", c =>
        {
            keys.SelectProfile(c.Id == keys.DefaultProfile ? null : c.Id);
            app.Save();
            changed();
        }, 260);
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var label = new TextBlock { Text = "Control profile", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        DockPanel.SetDock(pick, Dock.Left);
        row.Children.Add(pick);
        row.Children.Add(new TextBlock
        {
            Text = "Each iRacing control profile has its own keys, and cars can use different profiles. Changes go to the profile picked here.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
        });
        return row;
    }

    private static TextBlock Note(string text, bool warning = false) => new()
    {
        Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        Foreground = Ui.Res<Brush>(warning ? "ApricotDeep" : "TextMuted"),
    };
}

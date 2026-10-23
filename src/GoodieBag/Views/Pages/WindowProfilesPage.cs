using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Plugins;
using GoodieBag.Services;
using GoodieBag.Theme;
using GoodieBag.WindowProfiles;

namespace GoodieBag.Views.Pages;

internal sealed class WindowProfilesPage : UserControl, IPage, IScreenshotStates
{
    public const string AutoAnchor = "windows.auto";
    public const string ScriptAnchor = "windows.scripts";
    private const string Consumer = "Window Profiles page";
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private bool _shown, _buildQueued;
    private HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);

    public WindowProfilesPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        Content = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body), Focusable = false };
    }

    private WindowProfileSettings S => _app.Config.WindowProfiles;

    public void OnShown()
    {
        _shown = true;
        Watch();
        Build();
        _app.Watcher.Started += OnProcess;
        _app.Watcher.Stopped += OnProcess;
        _app.WindowProfiles.Changed += OnChanged;
        Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _shown = false;
        _app.Watcher.Release(Consumer);
        _watched.Clear();
        _app.Watcher.Started -= OnProcess;
        _app.Watcher.Stopped -= OnProcess;
        _app.WindowProfiles.Changed -= OnChanged;
    }

    private void Watch()
    {
        _watched = S.Profiles.Where(p => p.ProcessName.Length > 0).Select(p => Processes.AppWatcher.Normalize(p.ProcessName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_watched.Count > 0 && !_app.Demo) _app.Watcher.Acquire(Consumer, _watched);
        else _app.Watcher.Release(Consumer);
    }

    private void OnChanged()
    {
        Watch();
        QueueBuild();
    }

    private void OnProcess(string name)
    {
        if (_watched.Contains(name)) QueueBuild();
    }

    private void QueueBuild()
    {
        if (_buildQueued) return;
        _buildQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _buildQueued = false;
            if (_shown) Build();
        }, DispatcherPriority.Background);
    }

    private bool Running(WindowProfile p) =>
        _app.Demo ? p.ProcessName.Equals("SimHubWPF.exe", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("RRRE64.exe", StringComparison.OrdinalIgnoreCase)
            : p.ProcessName.Length > 0 && _app.Watcher.IsRunning(p.ProcessName);

    private void Build()
    {
        S.Normalize();
        _body.Children.Clear();
        var header = new Grid { Margin = new Thickness(0, 0, 0, Ui.HeaderGap) };
        header.Children.Add(Ui.PluginTitle(PluginIds.WindowProfiles, "Window Profiles"));
        _body.Children.Add(header);
        _body.Children.Add(SettingsCard());

        if (S.Profiles.Count == 0 && S.Groups.Count == 0)
        {
            _body.Children.Add(EmptyState());
            return;
        }
        var newGroup = Ui.LabeledButton("I.Folder", "New group", () => _ = EditGroupAsync(new WindowGroup(), isNew: true), "GhostButton");
        newGroup.Margin = new Thickness(0, 0, 8, 0);
        var create = Ui.LabeledButton("I.Plus", "New profile", () => _ = EditAsync(new WindowProfile(), isNew: true), "PrimaryButton");
        int n = S.Profiles.Count;
        _body.Children.Add(Ui.SectionHeader("Your profiles", RaceFormatCount(n) + " · drag to arrange", newGroup, create));

        var grid = new PanelGrid(S.Layout, _app.Save) { LiveEdit = true, RowHeight = 1, Gap = 0, PanelSpacing = 12 };
        int order = 0;
        foreach (var slot in S.Layout.ToList())
        {
            if (S.Groups.FirstOrDefault(g => WindowProfileSettings.KeyOf(g) == slot.Id) is { } group)
            {
                var g = group;
                grid.Add(new PanelDef(slot.Id, g.Name, _ => GroupCard(g)) { FullWidth = true, Order = order++, VeilClick = () => _ = EditGroupAsync(g, isNew: false) });
            }
            else if (S.Profiles.FirstOrDefault(p => WindowProfileSettings.KeyOf(p) == slot.Id) is { } profile)
            {
                var p = profile;
                grid.Add(new PanelDef(slot.Id, p.Name, _ => ProfileCard(p)) { FullWidth = true, Order = order++, VeilClick = () => _ = EditAsync(p, isNew: false) });
            }
        }
        _body.Children.Add(grid);
    }

    private Tile SettingsCard()
    {
        var flow = new SettingsFlow();
        var auto = Ui.SettingRow("Apply automatically", S.AutoApply ? "Each profile set to apply when its program starts does so." : "Off: profiles apply only with Apply, their keybind or the tray.",
            Ui.Switch(S.AutoApply, v => { S.AutoApply = v; _app.WindowProfiles.RaiseChanged(); }));
        Ui.Anchor(auto, AutoAnchor);
        flow.Children.Add(auto);
        flow.Children.Add(Ui.Divider());
        var copy = Ui.LabeledButton("I.Copy", "Copy command", CopyCommand, "GhostButton");
        copy.ToolTip = "Copies a PowerShell command that applies a profile, for a script or a Stream Deck";
        var scripts = Ui.SettingRow("From scripts and Stream Deck", $"Write apply-window \"name\" to the pipe \\\\.\\pipe\\{CommandPipe.Name} while Goodie Bag runs (a profile or group name).", copy);
        Ui.Anchor(scripts, ScriptAnchor);
        flow.Children.Add(scripts);
        return new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 18), Padding = new Thickness(20, 8, 20, 8), Child = flow };
    }

    private void CopyCommand()
    {
        string name = S.Profiles.FirstOrDefault()?.Name ?? "Profile name";
        try
        {
            Clipboard.SetText(CommandPipe.PowerShell(name));
            _app.Notify("Command copied", $"Applies “{name}”. Change the name in it for another profile or group.", NoticeKind.Success);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException)
        {
            _app.Notify("Couldn't copy", "Another program is holding the clipboard. Try again.", NoticeKind.Warning);
        }
    }

    private Tile EmptyState()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 24) };
        var colors = Hues.Tool(PluginIds.WindowProfiles, Hues.Accent);
        var icon = new NavIcon { Glyph = Plugins.BuiltIn.WindowProfilesPlugin.PageIcon, IsActive = true, Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetResourceReference(NavIcon.ForegroundProperty, colors.Deep);
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock { Text = "No window profiles yet", Style = Ui.Res<Style>("H2"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 4) });
        stack.Children.Add(new TextBlock
        {
            Text = "Pick a program and the exact size and spot for its window, e.g. a game stretched across triple screens or held at 1920 × 1080, borderless.",
            Style = Ui.Res<Style>("Muted"), TextAlignment = TextAlignment.Center, MaxWidth = 480, TextWrapping = TextWrapping.Wrap,
        });
        var create = Ui.LabeledButton("I.Plus", "New profile", () => _ = EditAsync(new WindowProfile(), isNew: true), "PrimaryButton");
        create.HorizontalAlignment = HorizontalAlignment.Center;
        create.Margin = new Thickness(0, 18, 0, 6);
        stack.Children.Add(create);
        return new Tile { Style = Ui.Res<Style>("Card"), Child = stack };
    }

    private Tile ProfileCard(WindowProfile p) =>
        new() { Style = Ui.Res<Style>("Card"), Padding = new Thickness(18, 14, 18, 16), Child = ProfileRow(p), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Click to edit, drag to arrange" };

    private StackPanel ProfileRow(WindowProfile p)
    {
        bool running = Running(p);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = Ui.Badge("I.AppWindow", running ? "AccentSoft" : "SurfaceAlt", running ? "AccentText" : "TextMuted", 40, 0, 20);
        badge.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(badge);

        var title = new TrimRow { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = p.Name, Style = Ui.Res<Style>("H3"), FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }));
        if (running)
        {
            var chip = Ui.Chip("Running", "ActiveModeBg", "ActiveModeFg");
            chip.Margin = new Thickness(0, 2, 6, 2);
            chip.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(chip);
        }
        Grid.SetColumn(title, 1);
        row.Children.Add(title);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var apply = Ui.LabeledButton("I.Zap", "Apply", () => _ = _app.WindowProfiles.ApplyAsync(p), running ? "PrimaryButton" : "GhostButton");
        apply.ToolTip = running ? "Move the window now" + Keys(p.Hotkey) : $"{p.ProcessName} isn't running";
        actions.Children.Add(apply);
        var edit = Ui.LabeledButton("I.Pencil", "Edit", () => _ = EditAsync(p, isNew: false), "GhostButton");
        edit.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(edit);
        var delete = Ui.IconButton("I.Trash", "Delete this profile", () => _ = DeleteAsync(p));
        delete.Margin = new Thickness(6, 0, 0, 0);
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(Facts(p));
        return stack;
    }

    private Grid Facts(WindowProfile p)
    {
        var facts = new (string Label, string Value, bool Verbatim)[]
        {
            ("Program", p.ProcessName.Length > 0 ? p.ProcessName : SimRacing.Race.RaceFormat.Missing, true),
            ("Size", p.Width is { } w && p.Height is { } h ? $"{w}×{h}" : "Keep its size", false),
            ("Position", $"{p.X}, {p.Y}", false),
            ("Style", StyleOf(p), false),
            ("Keybind", p.Hotkey is null ? "Not set" : Hotkeys.Hotkey.Display(p.Hotkey), false),
            ("Applying", ApplyingOf(p), false),
        };
        var grid = new Grid { Margin = new Thickness(54, 14, 0, 0) };
        for (int i = 0; i < facts.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 0 ? 1.4 : 1, GridUnitType.Star) });
            var cell = new Border { BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0), Padding = new Thickness(i == 0 ? 0 : 14, 1, 10, 1) };
            cell.SetResourceReference(Border.BorderBrushProperty, "Line");
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = facts[i].Label, Style = Ui.Res<Style>("Caption") });
            var value = TextFit.TipWhenTrimmed(new TextBlock { Text = facts[i].Value, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
            if (facts[i].Verbatim) TextCase.Verbatim(value);
            text.Children.Add(value);
            cell.Child = text;
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private static string StyleOf(WindowProfile p) => (p.RemoveBorders, p.HideTitleBar) switch
    {
        (true, true) => "Borderless, no title bar",
        (true, false) => "Borderless",
        (false, true) => "No title bar",
        _ => "As it is",
    };

    private string ApplyingOf(WindowProfile p) =>
        !p.Auto ? "By hand"
        : !S.AutoApply ? "By hand (automatic is off)"
        : p.DelayMs > 0 ? $"When it starts · {p.DelayMs / 1000.0:0.##} s" : "When it starts";

    private static string Keys(string? hotkey) => hotkey is null ? "" : $" ({Hotkeys.Hotkey.Display(hotkey)})";

    private Tile GroupCard(WindowGroup g)
    {
        var members = S.Members(g).ToList();
        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = Ui.Badge("I.Folder", "AccentSoft", "AccentText", 40, 0, 20);
        badge.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(badge);
        var text = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TrimRow();
        title.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = g.Name, Style = Ui.Res<Style>("H3"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }));
        if (g.Hotkey is not null)
        {
            var keys = Ui.Chip(Hotkeys.Hotkey.Display(g.Hotkey));
            keys.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(keys);
        }
        text.Children.Add(title);
        text.Children.Add(Ui.Text(members.Count == 0 ? "Empty: pick this group in a profile's editor" : $"Group · {RaceFormatCount(members.Count)}", "Caption"));
        Grid.SetColumn(text, 1);
        head.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var apply = Ui.LabeledButton("I.Zap", "Apply all", () => _ = _app.WindowProfiles.ApplyGroupAsync(g), members.Any(Running) ? "PrimaryButton" : "GhostButton");
        apply.ToolTip = "Move every program in the group that's running" + Keys(g.Hotkey);
        apply.IsEnabled = members.Count > 0;
        actions.Children.Add(apply);
        var add = Ui.LabeledButton("I.Plus", "Add profile", () => _ = EditAsync(new WindowProfile { GroupId = g.Id }, isNew: true), "SoftButton");
        add.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(add);
        var edit = Ui.LabeledButton("I.Pencil", "Edit", () => _ = EditGroupAsync(g, isNew: false), "GhostButton");
        edit.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(edit);
        var delete = Ui.IconButton("I.Trash", "Delete this group (its profiles stay)", () => _ = DeleteGroupAsync(g));
        delete.Margin = new Thickness(6, 0, 0, 0);
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 2);
        head.Children.Add(actions);
        stack.Children.Add(head);

        if (members.Count > 0)
        {
            var list = new ReorderGrid { MaxColumns = 1, MinCellWidth = 200, Gap = 6, Margin = new Thickness(0, 12, 0, 2) };
            foreach (var m in members)
            {
                var member = m;
                var tile = new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(14, 12, 14, 14), Child = ProfileRow(member), ToolTip = "Drag to arrange within the group" };
                tile.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not DependencyObject d || !InButton(d)) _ = EditAsync(member, isNew: false); };
                list.Children.Add(tile);
            }
            list.Moved += (from, to) => { S.MoveWithinGroup(g, from, to); _app.Save(); };
            PanelGrid.SetNoDrag(list, true);
            stack.Children.Add(list);
        }
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(16, 12, 16, 14), Child = stack, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Click to edit the group, drag to arrange" };
    }

    private static bool InButton(DependencyObject d)
    {
        for (var e = d; e is not null; e = System.Windows.Media.VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
            if (e is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    private static string RaceFormatCount(int n) => SimRacing.Race.RaceFormat.Count(n, "profile");

    private async Task EditAsync(WindowProfile profile, bool isNew)
    {
        var editor = new WindowProfileEditor(_app, _window, profile, isNew);
        if (!await _window.ShowModal(editor.Card)) return;
        if (isNew) S.Profiles.Add(profile);
        S.Normalize();
        _app.WindowProfiles.RaiseChanged();
        _app.Notify(isNew ? "Profile saved" : "Profile updated", $"{profile.Name} · {WindowProfileService.Describe(profile)}", NoticeKind.Success);
    }

    private async Task EditGroupAsync(WindowGroup group, bool isNew)
    {
        var editor = new WindowGroupEditor(_app, _window, group, isNew);
        if (!await _window.ShowModal(editor.Card)) return;
        if (isNew) S.Groups.Add(group);
        S.Normalize();
        _app.WindowProfiles.RaiseChanged();
    }

    private async Task DeleteAsync(WindowProfile profile)
    {
        if (!await _window.Confirm($"Delete “{profile.Name}”?", "The profile is removed. Windows stay where they are now.", "Delete", danger: true)) return;
        S.Profiles.Remove(profile);
        S.Normalize();
        _app.WindowProfiles.RaiseChanged();
    }

    private async Task DeleteGroupAsync(WindowGroup group)
    {
        if (!await _window.Confirm($"Delete the group “{group.Name}”?", "Its profiles stay, outside any group.", "Delete", danger: true)) return;
        S.Remove(group);
        S.Normalize();
        _app.WindowProfiles.RaiseChanged();
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("editor", () =>
        {
            if (S.Profiles.FirstOrDefault() is { } first) _ = EditAsync(first, isNew: false);
            return Task.CompletedTask;
        });
        yield return ("editor-bottom", () =>
        {
            if (_window.FindModalScroll() is { } sv) sv.ScrollToEnd();
            return Task.CompletedTask;
        });
        yield return ("close-editor", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        yield return ("new-editor", () =>
        {
            _ = EditAsync(new WindowProfile(), isNew: true);
            return Task.CompletedTask;
        });
        yield return ("close-new-editor", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        yield return ("group-editor", () =>
        {
            if (S.Groups.FirstOrDefault() is { } group) _ = EditGroupAsync(group, isNew: false);
            return Task.CompletedTask;
        });
        yield return ("close-group-editor", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
    }
}

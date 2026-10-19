using System.IO;
using System.Windows;
using System.Windows.Controls;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Display;
using GoodieBag.Services;
using GoodieBag.Theme;
using GoodieBag.WindowProfiles;

namespace GoodieBag.Views.Pages;

internal sealed class WindowProfileEditor
{
    private const double LabelWidth = 170;
    private readonly AppServices _app;
    private readonly WindowProfile _profile;
    private readonly TextBox _name = new() { MaxLength = Ui.NameLength, Tag = "e.g. RaceRoom on triples" };
    private readonly TextBox _process = new() { Tag = "e.g. RRRE64.exe" };
    private readonly HotkeyBox _hotkey = new();
    private readonly TextBlock _shared = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(LabelWidth, 0, 0, 6) };
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed };
    private readonly NumberBox _width, _height, _x, _y, _delay;
    private readonly CheckBox _keepSize = new() { Content = "Keep its size", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
    private readonly CheckBox _borders, _titleBar, _auto;
    private readonly MonitorLayoutView _preview = new() { Height = 130 };
    private readonly List<MonitorInfo> _monitors;
    private string? _groupId;
    private bool _nameTouched;

    public Border Card { get; }

    public WindowProfileEditor(AppServices app, MainWindow window, WindowProfile profile, bool isNew)
    {
        _app = app;
        _profile = profile;
        _monitors = Monitors(app);
        _groupId = profile.GroupId;
        var settings = app.Config.WindowProfiles;

        _name.Text = profile.Name;
        _nameTouched = !isNew;
        _name.TextChanged += (_, _) => { if (_name.IsKeyboardFocusWithin) _nameTouched = true; };
        _process.Text = profile.ProcessName;
        _process.TextChanged += (_, _) =>
        {
            if (!_nameTouched) _name.Text = Path.GetFileNameWithoutExtension(_process.Text.Trim());
        };
        TextCase.Verbatim(_process);

        var program = new DockPanel();
        var browse = Ui.IconButton("I.Folder", "Choose the program's .exe", () =>
        {
            if (ProgramPicker.BrowseExe("Programs (*.exe)|*.exe") is { } path) _process.Text = Path.GetFileName(path);
        });
        var all = Ui.IconButton("I.Search", "All running processes (also ones without a window)", () => { });
        var pick = Ui.IconButton("I.AppWindow", "Pick from the programs with a window open now", () => { });
        pick.Click += (_, _) => ShowPrograms(pick, all: false);
        all.Click += (_, _) => ShowPrograms(all, all: true);
        foreach (var b in new[] { browse, all, pick })
        {
            b.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(b, Dock.Right);
            program.Children.Add(b);
        }
        program.Children.Add(_process);

        var stack = new StackPanel();
        stack.Children.Add(Ui.FieldRow("Program", program, LabelWidth));
        program.HorizontalAlignment = HorizontalAlignment.Stretch;
        stack.Children.Add(Ui.FieldRow("Name", _name, LabelWidth));
        if (settings.Groups.Count > 0)
        {
            var groups = new[] { new Choice(null, "No group") }.Concat(settings.Groups.Select(g => new Choice(g.Id, g.Name))).ToList();
            stack.Children.Add(Ui.FieldRow("Group", Ui.Combo(groups, _groupId, c => _groupId = c.Id, 260), LabelWidth));
        }

        stack.Children.Add(Ui.EditorSection("Size and position"));
        var presets = PresetBox();
        var current = Ui.LabeledButton("I.Target", "Where it is now", ReadCurrent, "GhostButton");
        current.ToolTip = "Copy the size and spot of the program's window as it is right now";
        current.Margin = new Thickness(8, 0, 0, 0);
        var presetRow = new DockPanel();
        DockPanel.SetDock(current, Dock.Right);
        presetRow.Children.Add(current);
        presetRow.Children.Add(presets);
        stack.Children.Add(Ui.FieldRow("Copy values from", presetRow, LabelWidth));

        var screen = Span();
        _width = new NumberBox(profile.Width ?? screen.Width, "px", 96) { Minimum = 100, Maximum = 32000 };
        _height = new NumberBox(profile.Height ?? screen.Height, "px", 96) { Minimum = 100, Maximum = 32000 };
        _x = new NumberBox(profile.X, "px", 96) { Minimum = -32000, Maximum = 32000 };
        _y = new NumberBox(profile.Y, "px", 96) { Minimum = -32000, Maximum = 32000 };
        _keepSize.IsChecked = profile.Width is null;
        _keepSize.ToolTip = "Only move the window; leave its size as the program sets it";
        _keepSize.Click += (_, _) => SizeChanged();
        foreach (var box in new[] { _width, _height, _x, _y }) box.ValueChanged += _ => SizeChanged();
        stack.Children.Add(Ui.FieldRow("Size", Pair(_width, "×", _height, _keepSize), LabelWidth));
        stack.Children.Add(Ui.FieldRow("Position", Pair(_x, ",", _y), LabelWidth));
        stack.Children.Add(new TextBlock
        {
            Text = "Pixels on the Windows desktop: 0, 0 is the top left of the main display, so a screen to its left starts at a negative x.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(LabelWidth, 0, 0, 6),
        });
        stack.Children.Add(new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(8), Margin = new Thickness(LabelWidth, 2, 0, 4), Child = _preview });

        stack.Children.Add(Ui.EditorSection("Window"));
        _borders = new CheckBox { Content = "Remove borders", IsChecked = profile.RemoveBorders, ToolTip = "Strips the frame and title bar. Only for games without a borderless option of their own." };
        _titleBar = new CheckBox { Content = "Hide the title bar of Store / Xbox games", IsChecked = profile.HideTitleBar, Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "For Store / Xbox app games (e.g. Forza Horizon 4) that keep a title strip after removing borders: pushes it above the screen. Does nothing for other programs." };
        _borders.Click += (_, _) => _titleBar.IsEnabled = _borders.IsChecked == true;
        _titleBar.IsEnabled = profile.RemoveBorders;
        var look = new StackPanel();
        look.Children.Add(_borders);
        look.Children.Add(_titleBar);
        stack.Children.Add(Ui.FieldRow("Style", look, LabelWidth));

        stack.Children.Add(Ui.EditorSection("Applying"));
        _auto = new CheckBox { Content = "When the program starts", IsChecked = profile.Auto, VerticalAlignment = VerticalAlignment.Center };
        _delay = new NumberBox(profile.DelayMs, "ms", 96) { Minimum = 0, Maximum = WindowProfileSettings.MaxDelayMs, Step = 250 };
        _delay.ToolTip = "Wait this long after the program starts. For about 35 s after that, Goodie Bag moves the window back if the game resizes it.";
        _auto.Click += (_, _) => _delay.IsEnabled = _auto.IsChecked == true;
        _delay.IsEnabled = profile.Auto;
        var autoRow = new StackPanel { Orientation = Orientation.Horizontal };
        autoRow.Children.Add(_auto);
        autoRow.Children.Add(new TextBlock { Text = "after", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 10, 0) });
        autoRow.Children.Add(_delay);
        stack.Children.Add(Ui.FieldRow("Automatically", autoRow, LabelWidth));
        if (!settings.AutoApply)
            stack.Children.Add(new TextBlock { Text = "Applying automatically is off for every profile (switch at the top of the page).", Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<System.Windows.Media.Brush>("Warn"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(LabelWidth, 0, 0, 6) });
        _hotkey.SetHotkey(profile.Hotkey);
        _hotkey.HotkeyChanged += _ => UpdateShared();
        stack.Children.Add(Ui.FieldRow("Keybind", _hotkey, LabelWidth));
        _shared.Style = Ui.Res<Style>("Caption");
        stack.Children.Add(_shared);
        UpdateShared();
        SizeChanged();

        var buttons = Ui.DialogButtons(window, isNew ? "Save profile" : "Save changes", Save);
        var test = Ui.LabeledButton("I.Play", "Try it now", TryNow, "GhostButton");
        test.ToolTip = "Move the program's window with these values now, without saving";
        test.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Insert(0, test);
        Card = Ui.Dialog(window, isNew ? "New window profile" : $"Edit “{profile.Name}”", "Applied when the program starts, with its keybind, from the tray or with Apply.", stack, buttons, _error, 840);
    }

    private static List<MonitorInfo> Monitors(AppServices app)
    {
        try { return app.Displays.GetMonitors().Where(m => m.Active).ToList(); }
        catch { return new List<MonitorInfo>(); }
    }

    private List<ScreenArea> Screens() => _monitors.Select(m => new ScreenArea(m.Name, m.X, m.Y, m.Width, m.Height, m.Primary)).ToList();

    private ScreenArea Span() => _monitors.Count == 0 ? new ScreenArea("", 0, 0, 1920, 1080, true) : WindowPresets.Span(Screens());

    private ComboBox PresetBox()
    {
        var box = new ComboBox { MinWidth = 300 };
        box.Items.Add(new ComboBoxItem { Content = "Pick a preset", Tag = null });
        var presets = WindowPresets.For(Screens());
        string? group = null;
        foreach (var preset in presets)
        {
            if (preset.Group != group)
            {
                group = preset.Group;
                box.Items.Add(new ComboBoxItem { Content = group, IsEnabled = false, FontWeight = FontWeights.SemiBold });
            }
            box.Items.Add(new ComboBoxItem { Content = preset.Text, Tag = preset, Padding = new Thickness(18, 6, 8, 6) });
        }
        box.SelectedIndex = 0;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not ComboBoxItem { Tag: WindowPreset p }) return;
            Fill(p.X, p.Y, p.Width, p.Height);
            box.SelectedIndex = 0;
        };
        return box;
    }

    private void Fill(int x, int y, int w, int h)
    {
        (_x.Value, _y.Value, _width.Value, _height.Value) = (x, y, w, h);
        _keepSize.IsChecked = false;
        SizeChanged();
    }

    private void ReadCurrent()
    {
        string process = _process.Text.Trim();
        if (_app.Demo)
        {
            _app.Notify("Demo mode", "This reads the program's window as it is now (not available with demo data).", NoticeKind.Info);
            return;
        }
        if (process.Length > 0 && WindowProfileService.CurrentRect(process) is { } r) Fill(r.X, r.Y, r.W, r.H);
        else _app.Notify("No window to read", $"Start {(process.Length > 0 ? process : "the program")} first, then try again.", NoticeKind.Info);
    }

    private void SizeChanged()
    {
        bool keep = _keepSize.IsChecked == true;
        _width.IsEnabled = _height.IsEnabled = !keep;
        _preview.SetTiles(_monitors.OrderBy(m => m.X).Select(m => new LayoutTile(m.X, m.Y, m.Width, m.Height, m.Number > 0 ? m.Number.ToString() : m.Name, $"{m.Width}×{m.Height}", m.Primary)).ToList());
        _preview.SetTarget(keep ? null : new Rect(_x.Value, _y.Value, _width.Value, _height.Value));
    }

    private static StackPanel Pair(FrameworkElement a, string between, FrameworkElement b, FrameworkElement? extra = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(a);
        panel.Children.Add(new TextBlock { Text = between, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(b);
        if (extra is not null) panel.Children.Add(extra);
        return panel;
    }

    private void ShowPrograms(FrameworkElement anchor, bool all)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxHeight = 420 };
        IEnumerable<(string Process, string? Title)> items;
        if (_app.Demo)
            items = new (string, string?)[] { ("RRRE64.exe", "RaceRoom Racing Experience"), ("acs.exe", "Assetto Corsa"), ("SimHubWPF.exe", "SimHub") };
        else if (all)
            items = System.Diagnostics.Process.GetProcesses().Select(p => { try { return p.ProcessName + ".exe"; } catch { return ""; } finally { p.Dispose(); } })
                .Where(n => n.Length > 4).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => (n, (string?)null));
        else
            items = WindowProfileService.ProgramsWithWindows().Select(w => (w.Process, (string?)w.Title));
        foreach (var (process, title) in items)
        {
            var item = new MenuItem { Header = title is null ? process : $"{process}  ·  {title}" };
            TextCase.Verbatim(item);
            item.Click += (_, _) => _process.Text = process;
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "Nothing running with a window", IsEnabled = false });
        menu.IsOpen = true;
    }

    private void UpdateShared()
    {
        var s = _app.Config.WindowProfiles;
        var hk = _hotkey.Hotkey;
        var others = string.IsNullOrWhiteSpace(hk) ? new List<string>()
            : s.Profiles.Where(p => p != _profile && Hotkeys.Hotkey.SameKey(p.Hotkey, hk)).Select(p => $"“{p.Name}”")
                .Concat(s.Groups.Where(g => Hotkeys.Hotkey.SameKey(g.Hotkey, hk)).Select(g => $"the group “{g.Name}”")).ToList();
        _shared.Text = others.Count == 0 ? "" : $"Also used by {string.Join(", ", others)}: pressing it moves whichever of their programs are running.";
        _shared.Visibility = others.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private WindowProfile Values()
    {
        var p = _profile.Copy();
        p.Name = _name.Text.Trim();
        p.ProcessName = NormalizeProcess(_process.Text);
        bool keep = _keepSize.IsChecked == true;
        p.Width = keep ? null : (int)_width.Value;
        p.Height = keep ? null : (int)_height.Value;
        p.X = (int)_x.Value;
        p.Y = (int)_y.Value;
        p.RemoveBorders = _borders.IsChecked == true;
        p.HideTitleBar = p.RemoveBorders && _titleBar.IsChecked == true;
        p.Auto = _auto.IsChecked == true;
        p.DelayMs = (int)_delay.Value;
        p.Hotkey = _hotkey.Hotkey;
        p.GroupId = _groupId;
        if (p.Name.Length == 0) p.Name = Path.GetFileNameWithoutExtension(p.ProcessName);
        return p;
    }

    internal static string NormalizeProcess(string text)
    {
        string name = Path.GetFileName(text.Trim().Trim('"'));
        if (name.Length == 0) return "";
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
    }

    private void TryNow()
    {
        var p = Values();
        if (p.ProcessName.Length == 0) { Show("Choose the program first."); return; }
        _ = _app.WindowProfiles.ApplyAsync(p, retry: false, watch: false);
    }

    private void Show(string? problem)
    {
        _error.Text = problem ?? "";
        _error.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool Save()
    {
        var p = Values();
        string? problem = p.ProcessName.Length == 0 ? "Choose the program whose window this profile moves."
            : _app.Config.WindowProfiles.NameProblem(p.Name, _profile)
            ?? _app.Hotkeys.Clash(p.Hotkey, _profile);
        Show(problem);
        if (problem is not null) return false;
        _profile.Name = p.Name;
        _profile.ProcessName = p.ProcessName;
        _profile.Width = p.Width;
        _profile.Height = p.Height;
        _profile.X = p.X;
        _profile.Y = p.Y;
        _profile.RemoveBorders = p.RemoveBorders;
        _profile.HideTitleBar = p.HideTitleBar;
        _profile.Auto = p.Auto;
        _profile.DelayMs = p.DelayMs;
        _profile.Hotkey = p.Hotkey;
        _profile.GroupId = p.GroupId;
        return true;
    }
}

internal sealed class WindowGroupEditor
{
    private readonly AppServices _app;
    private readonly WindowGroup _group;
    private readonly TextBox _name = new() { MaxLength = Ui.NameLength, Tag = "e.g. Rig screens" };
    private readonly HotkeyBox _hotkey = new();
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed };

    public Border Card { get; }

    public WindowGroupEditor(AppServices app, MainWindow window, WindowGroup group, bool isNew)
    {
        _app = app;
        _group = group;
        _name.Text = group.Name;
        _hotkey.SetHotkey(group.Hotkey);
        var stack = new StackPanel();
        stack.Children.Add(Ui.FieldRow("Name", _name, 120));
        stack.Children.Add(Ui.FieldRow("Keybind", _hotkey, 120));
        stack.Children.Add(new TextBlock { Text = "Moves every program in the group that's running. Put profiles in it from their editor.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(120, 0, 0, 0) });
        Card = Ui.Dialog(window, isNew ? "New group" : "Edit group", "Several window profiles applied together.", stack,
            Ui.DialogButtons(window, isNew ? "Save group" : "Save changes", Save), _error, 520);
    }

    private bool Save()
    {
        string name = _name.Text.Trim();
        string? problem = _app.Config.WindowProfiles.NameProblem(name, _group) ?? _app.Hotkeys.Clash(_hotkey.Hotkey, _group);
        _error.Text = problem ?? "";
        _error.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        if (problem is not null) return false;
        _group.Name = name;
        _group.Hotkey = _hotkey.Hotkey;
        return true;
    }
}

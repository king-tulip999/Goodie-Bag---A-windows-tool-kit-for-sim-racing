using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GoodieBag.Audio;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Plugins;
using GoodieBag.Processes;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class ModeEditor
{
    private readonly AppServices _app;
    private readonly Mode _mode;
    private readonly bool _isNew;
    private readonly TextBox _name = new() { MaxLength = Ui.NameLength };
    private readonly HotkeyBox _hotkey = new();
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, FontSize = 12.5 };
    private readonly ComboBox _playback = new() { MinWidth = 280 };
    private readonly ComboBox _recording = new() { MinWidth = 280 };
    private readonly CheckBox _comms = new() { Content = "Also use them for calls (Discord, Teams…)" };
    private readonly MonitorLayoutView _preview = new() { Height = 96 };
    private readonly StackPanel _launchRows = new();
    private readonly WrapPanel _closeChips = new();

    private string? _displayProfile;
    private bool _audioOverride;
    private readonly StackPanel _audio = new();
    private readonly TextBlock _audioNote = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _overrideButton;
    private readonly WindowRulesEditor _windows;
    private OverlayMode _overlay;
    private string? _overlayLayout, _fanPreset;
    private int? _volume;
    private AutoPriority _priority;
    private readonly List<LaunchItem> _launch;
    private readonly TextBox _programPath = new(), _programArgs = new(), _programIcon = new(), _programProcess = new();
    private bool _programAdmin, _closeLaunched;
    private readonly List<string> _close;
    private bool _forceClose;
    private bool _oneOff;
    private readonly List<TriggerEditor> _triggers = new();
    private readonly StackPanel _triggerRows = new();
    private readonly TextBox _message = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 34 };
    private readonly List<UIElement> _modeOnly = new();

    public Border Card { get; }

    public ModeEditor(AppServices app, MainWindow window, Mode mode, bool isNew, bool addTrigger = false)
    {
        _app = app;
        _mode = mode;
        _isNew = isNew;
        _displayProfile = mode.DisplayProfile;
        _audioOverride = mode.Audio is not null || mode.Volume is not null;
        _windows = new WindowRulesEditor(app, mode.Windows);
        _overlay = mode.Overlay;
        _overlayLayout = mode.OverlayLayout;
        _fanPreset = mode.FanPreset;
        _volume = mode.Volume;
        _priority = mode.Priority;
        _launch = mode.Launch.Select(l => new LaunchItem { Path = l.Path, Arguments = l.Arguments, AsAdmin = l.AsAdmin }).ToList();
        _close = mode.Close.ToList();
        _forceClose = mode.ForceClose;
        _programAdmin = mode.Program?.AsAdmin ?? false;
        _closeLaunched = mode.CloseLaunchedOnExit;
        _oneOff = mode.OneOff;
        foreach (var trigger in mode.Triggers)
            AddTrigger(ConfigStore.Clone(trigger));
        if (addTrigger)
            AddTrigger(NewTrigger());

        var stack = new StackPanel();
        var right = new StackPanel();

        _name.Text = mode.Name;
        _name.Tag = "e.g. Sim Racing";
        stack.Children.Add(Ui.FieldRow("Name", _name));
        _hotkey.SetHotkey(mode.Hotkey);
        stack.Children.Add(Ui.FieldRow("Keybind", _hotkey));
        stack.Children.Add(Ui.FieldRow("Kind", Ui.Segmented(new[] { "Mode", "One-off" }, _oneOff ? 1 : 0, i => { _oneOff = i == 1; UpdateKind(); }, "MKind",
            new[] { "Becomes the active mode: its windows hold while it's on, and a program it switched for brings the previous mode back", "Runs its changes without becoming the active mode, e.g. idle → quiet fans" })));

        stack.Children.Add(Ui.EditorSection("When"));
        stack.Children.Add(new TextBlock
        {
            Text = "Besides Apply, the tray and its keybind. Each trigger switches to it (a one-off: runs it) on its own.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        });
        stack.Children.Add(_triggerRows);
        var add = Ui.LabeledButton("I.Plus", "Add trigger", () => { AddTrigger(NewTrigger()); FillTriggers(); }, "SoftButton");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(add);
        var priority = Ui.FieldRow("Switching priority", Ui.Segmented(new[] { "Low", "Normal", "High" }, (int)_priority, i => _priority = (AutoPriority)i, "Pri",
            new[] { "Loses to other modes whose programs are running", "The usual priority", "Wins over other modes whose programs are running" }));
        priority.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(ModeOnly(priority));
        stack.Children.Add(ModeOnly(new TextBlock { Text = "When programs with “while it runs” triggers for several modes are running, the higher priority wins.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(150, 0, 0, 0) }));
        FillTriggers();

        right.Children.Add(Ui.EditorSection("Display"));
        var profiles = new[] { new Choice(null, "Don't change displays") }.Concat(app.Config.Displays.Profiles.Select(p => new Choice(p.Name, p.Name))).ToList();
        var profileRow = new DockPanel();
        var manage = Ui.LabeledButton("I.Monitor", "Manage profiles", () => { window.CloseModal(false); window.Navigate("displays"); }, "GhostButton");
        manage.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(manage, Dock.Right);
        profileRow.Children.Add(manage);
        profileRow.Children.Add(Ui.Combo(profiles, _displayProfile, c => { _displayProfile = c.Id; UpdatePreview(); UpdateAudioLock(); }));
        right.Children.Add(Ui.FieldRow("Display profile", profileRow));
        right.Children.Add(new TextBlock
        {
            Text = app.Config.Displays.Profiles.Count == 0
                ? "No display profiles yet: save your monitor setups on the Displays page, then pick one here."
                : "Monitors, arrangement, resolution, refresh rate, main display, HDR, taskbar and wallpapers come from the profile.",
            Style = Ui.Res<Style>("Caption"), Margin = new Thickness(150, 0, 0, 4),
        });
        right.Children.Add(new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 4), Child = _preview });
        UpdatePreview();
        right.Children.Add(ModeOnly(Ui.EditorSection("Windows")));
        right.Children.Add(ModeOnly(_windows));

        right.Children.Add(Ui.EditorSection("Audio"));
        var lockRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        _overrideButton = Ui.LabeledButton("I.Pencil", "Override", () => { _audioOverride = !_audioOverride; UpdateAudioLock(); }, "SoftButton");
        DockPanel.SetDock(_overrideButton, Dock.Right);
        lockRow.Children.Add(_overrideButton);
        _audioNote.Style = Ui.Res<Style>("Caption");
        _audioNote.Margin = new Thickness(0, 0, 12, 0);
        lockRow.Children.Add(_audioNote);
        right.Children.Add(lockRow);
        FillAudio(_playback, EDataFlow.Render, mode.Audio?.PlaybackId, mode.Audio?.PlaybackName);
        FillAudio(_recording, EDataFlow.Capture, mode.Audio?.RecordingId, mode.Audio?.RecordingName);
        _audio.Children.Add(Ui.FieldRow("Speakers / headset", _playback));
        _audio.Children.Add(Ui.FieldRow("Microphone", _recording));
        _comms.IsChecked = mode.Audio?.SetCommunications ?? true;
        _comms.Margin = new Thickness(150, 4, 0, 4);
        _audio.Children.Add(_comms);
        _audio.Children.Add(Ui.FieldRow("Volume", VolumeControl()));
        right.Children.Add(_audio);
        UpdateAudioLock();

        right.Children.Add(Ui.EditorSection("Performance"));
        if (_app.Plugins.IsActive(PluginIds.FanControl))
            right.Children.Add(Ui.FieldRow("Fan preset", Ui.Combo(new[] { new Choice(null, "Leave as is") }.Concat(_app.Config.Fans.Presets.Select(p => new Choice(p.Name, p.Name))), _fanPreset, c => _fanPreset = c.Id)));
        if (_app.Plugins.IsActive(PluginIds.Overlay))
        {
            right.Children.Add(Ui.FieldRow("Overlay", Ui.Segmented(new[] { "Leave as is", "Show", "Hide", "Toggle" }, (int)_overlay, i => _overlay = (OverlayMode)i, "Ovl",
                new[] { "Don't touch the overlay when this mode is applied", "Show the overlay", "Hide the overlay", "Show it if it's hidden, hide it if it's showing" })));
            right.Children.Add(Ui.FieldRow("Overlay layout", Ui.Combo(new[] { new Choice(null, "Keep the current layout") }.Concat(_app.Config.Overlay.Layouts.Select(l => new Choice(l.Name, l.Name))), _overlayLayout, c => _overlayLayout = c.Id)));
        }

        var launchPart = new StackPanel();
        launchPart.Children.Add(Ui.EditorSection("Launch"));
        launchPart.Children.Add(new TextBlock
        {
            Text = "Optional: the game or program this mode is for. Its Launch button applies the mode, starts the programs below, then this. Applying the mode on its own doesn't start it.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        });
        _programPath.Text = mode.Program?.Path ?? "";
        _programPath.Tag = "C:\\…\\game.exe, a shortcut, or steam://rungameid/…";
        var browse = Ui.IconButton("I.Folder", "Browse", () =>
        {
            if (ProgramPicker.BrowseExe() is { } p)
            {
                _programPath.Text = p;
                if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = System.IO.Path.GetFileNameWithoutExtension(p);
            }
        });
        launchPart.Children.Add(Ui.FieldRow("Program", WithButton(_programPath, browse)));
        _programArgs.Text = mode.Program?.Arguments ?? "";
        _programArgs.Tag = "Optional";
        launchPart.Children.Add(Ui.FieldRow("Arguments", _programArgs));
        var programAdmin = new CheckBox { Content = "Start as administrator", IsChecked = _programAdmin };
        programAdmin.Click += (_, _) => _programAdmin = programAdmin.IsChecked == true;
        launchPart.Children.Add(Ui.FieldRow("", programAdmin));
        var closeLaunched = new CheckBox { Content = new TextBlock { Text = "When it exits, close the programs this launch started", TextWrapping = TextWrapping.Wrap }, IsChecked = _closeLaunched };
        closeLaunched.Click += (_, _) => _closeLaunched = closeLaunched.IsChecked == true;
        launchPart.Children.Add(Ui.FieldRow("", closeLaunched));
        launchPart.Children.Add(new TextBlock
        {
            Text = "Only programs that weren't already running are closed (each is asked to close, then ended after 5 seconds).",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(150, 0, 0, 4),
        });
        _programIcon.Text = mode.Program?.IconPath ?? "";
        _programIcon.Tag = "Image or .ico (default: the program's own icon)";
        var iconBrowse = Ui.IconButton("I.Folder", "Choose image", () =>
        {
            if (ProgramPicker.BrowseExe("Images and icons (*.png;*.jpg;*.ico;*.exe)|*.png;*.jpg;*.jpeg;*.ico;*.exe|All files (*.*)|*.*") is { } p)
                _programIcon.Text = p;
        });
        launchPart.Children.Add(Ui.FieldRow("Icon", WithButton(_programIcon, iconBrowse)));
        _programProcess.Text = mode.Program?.ProcessName ?? "";
        _programProcess.Tag = "e.g. iRacingSim64DX11 (for links and launchers)";
        launchPart.Children.Add(Ui.FieldRow("Process to detect", _programProcess));
        launchPart.Children.Add(new TextBlock
        {
            Text = "How Goodie Bag tells the game is running when the program above is a link or a launcher rather than the game itself.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(150, 0, 0, 0),
        });

        stack.Children.Add(ModeOnly(launchPart));

        stack.Children.Add(Ui.EditorSection("Start when applied"));
        stack.Children.Add(_launchRows);
        var addLaunch = Ui.LabeledButton("I.Plus", "Add program or script…", () =>
        {
            if (ProgramPicker.BrowseExe() is { } path)
            {
                _launch.Add(new LaunchItem { Path = path });
                FillLaunch();
            }
        }, "SoftButton");
        addLaunch.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(addLaunch);

        stack.Children.Add(Ui.EditorSection("Close when applied"));
        stack.Children.Add(_closeChips);
        var closeRow = new StackPanel { Orientation = Orientation.Horizontal };
        var addClose = Ui.LabeledButton("I.Plus", "Add program…", () => { }, "SoftButton");
        addClose.Click += (_, _) => ProgramPicker.Show(addClose, picked =>
        {
            if (!_close.Contains(picked.Process, StringComparer.OrdinalIgnoreCase)) _close.Add(picked.Process);
            FillClose();
        });
        closeRow.Children.Add(addClose);
        var force = new CheckBox { Content = "Force-close if they don't exit within 4 s", IsChecked = _forceClose, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        force.Click += (_, _) => _forceClose = force.IsChecked == true;
        closeRow.Children.Add(force);
        stack.Children.Add(closeRow);

        stack.Children.Add(Ui.EditorSection("Notification"));
        _message.Text = mode.Message ?? "";
        _message.Tag = "Optional: shown each time it's applied, e.g. Race in 30 minutes";
        stack.Children.Add(_message);

        UpdateKind();
        FillLaunch();
        FillClose();

        var buttons = Ui.DialogButtons(window, isNew ? "Save mode" : "Save changes", Save);
        Card = Ui.Dialog(window, isNew ? "New mode" : $"Edit “{mode.Name}”",
            isNew ? "Starts from what's on right now (the active display profile, or your audio devices); everything else is optional." : "What it changes when it's applied, and what switches to it. Anything left as is isn't touched.",
            Columns(stack, right), buttons, _error, DialogWidth(window));
    }

    private const double ColumnsFrom = 1000, SingleWidth = 680, WideWidth = 1280;

    private static double DialogWidth(MainWindow window)
    {
        double room = ((FrameworkElement)window.ModalLayer.Parent).ActualWidth - 80;
        return room >= ColumnsFrom ? Math.Min(WideWidth, room) : SingleWidth;
    }

    private static Grid Columns(StackPanel left, StackPanel right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(left);
        grid.Children.Add(right);
        grid.SizeChanged += (_, e) =>
        {
            bool wide = e.NewSize.Width >= ColumnsFrom - 60;
            grid.ColumnDefinitions[1].Width = new GridLength(wide ? 36 : 0);
            grid.ColumnDefinitions[2].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(right, wide ? 2 : 0);
            Grid.SetRow(right, wide ? 0 : 1);
        };
        return grid;
    }

    private StackPanel VolumeControl() => VolumeControl(_volume, v => _volume = v);

    private static DockPanel WithButton(TextBox box, Button button)
    {
        var row = new DockPanel();
        DockPanel.SetDock(button, Dock.Right);
        row.Children.Add(button);
        row.Children.Add(box);
        return row;
    }

    private void UpdateAudioLock()
    {
        var profile = _app.DisplayProfiles.Find(_displayProfile);
        bool locked = _displayProfile is not null && !_audioOverride;
        _audio.IsEnabled = !locked;
        _audio.Opacity = locked ? 0.45 : 1;
        _overrideButton.Visibility = _displayProfile is null ? Visibility.Collapsed : Visibility.Visible;
        ((TextBlock)((StackPanel)_overrideButton.Content).Children[^1]).Text = ThemeManager.Case(_audioOverride ? "Use the profile's" : "Override");
        string? speakers = profile?.Audio?.PlaybackName, mic = profile?.Audio?.RecordingName;
        string what = profile is null ? "" : speakers is null && mic is null && profile.Volume is null
            ? "It leaves audio as it is."
            : "It sets " + string.Join(", ", new[] { speakers is null ? null : "speakers " + speakers, mic is null ? null : "microphone " + mic, profile.Volume is { } v ? $"volume {v}%" : null }.Where(x => x is not null)) + ".";
        _audioNote.Text = _displayProfile is null ? "Speakers, microphone and volume while this mode is applied."
            : _audioOverride ? $"Overriding the display profile “{_displayProfile}”: these values are used instead of its own."
            : $"Set by the display profile “{_displayProfile}”. {what}";
    }

    public static StackPanel VolumeControl(int? initial, Action<int?> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var on = new CheckBox { Content = "Set to", IsChecked = initial is not null, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var slider = Ui.SliderWithValue(0, 100, initial ?? 50, "{0:0}%", v => { if (on.IsChecked == true) set((int)v); }, 180, 1);
        slider.IsEnabled = initial is not null;
        on.Click += (_, _) =>
        {
            slider.IsEnabled = on.IsChecked == true;
            set(on.IsChecked == true ? (int)((Slider)slider.Children[0]).Value : null);
        };
        panel.Children.Add(on);
        panel.Children.Add(slider);
        return panel;
    }

    private T ModeOnly<T>(T element) where T : UIElement
    {
        _modeOnly.Add(element);
        return element;
    }

    private void UpdateKind()
    {
        foreach (var element in _modeOnly)
            element.Visibility = _oneOff ? Visibility.Collapsed : Visibility.Visible;
    }

    private static AutomationRule NewTrigger() => new() { Trigger = TriggerType.AppStarts };

    private void AddTrigger(AutomationRule trigger)
    {
        TriggerEditor? editor = null;
        editor = new TriggerEditor(_app, trigger, () => { _triggers.Remove(editor!); FillTriggers(); });
        _triggers.Add(editor);
    }

    private void FillTriggers()
    {
        _triggerRows.Children.Clear();
        foreach (var t in _triggers)
            _triggerRows.Children.Add(t.View);
        if (_triggers.Count == 0)
            _triggerRows.Children.Add(new TextBlock { Text = "None: it switches only when you pick it.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 6) });
    }

    private void FillLaunch()
    {
        _launchRows.Children.Clear();
        foreach (var item in _launch.ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(TextCase.Verbatim(new TextBlock { Text = System.IO.Path.GetFileName(item.Path), ToolTip = TextCase.VerbatimTip(item.Path), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }));
            var args = new TextBox { Text = item.Arguments ?? "", Tag = "Arguments (optional)", Margin = new Thickness(8, 0, 8, 0) };
            args.TextChanged += (_, _) => item.Arguments = string.IsNullOrWhiteSpace(args.Text) ? null : args.Text;
            Grid.SetColumn(args, 1);
            row.Children.Add(args);
            var admin = new CheckBox { Content = "Admin", IsChecked = item.AsAdmin, ToolTip = "Start elevated (most programs shouldn't be)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            admin.Click += (_, _) => item.AsAdmin = admin.IsChecked == true;
            Grid.SetColumn(admin, 2);
            row.Children.Add(admin);
            var remove = Ui.IconButton("I.Trash", "Remove", () => { _launch.Remove(item); FillLaunch(); });
            Grid.SetColumn(remove, 3);
            row.Children.Add(remove);
            _launchRows.Children.Add(row);
        }
        if (_launch.Count == 0)
            _launchRows.Children.Add(new TextBlock { Text = "Nothing. Programs already running are never started twice.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 6) });
    }

    private void FillClose()
    {
        _closeChips.Children.Clear();
        foreach (var name in _close.ToList())
            _closeChips.Children.Add(Ui.RemovableChip(name + ".exe", () => { _close.Remove(name); FillClose(); }));
        if (_close.Count == 0)
            _closeChips.Children.Add(new TextBlock { Text = "Nothing.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 6) });
    }

    private void UpdatePreview()
    {
        var snap = _app.DisplayProfiles.Find(_displayProfile)?.Layout;
        _preview.SetTiles(snap is null ? Array.Empty<LayoutTile>() : ModeLayout.Tiles(_app, snap));
        ((FrameworkElement)_preview.Parent).Visibility = snap is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void FillAudio(ComboBox box, EDataFlow flow, string? selectedId, string? selectedName) => FillAudio(_app, box, flow, selectedId, selectedName);

    public static void FillAudio(AppServices app, ComboBox box, EDataFlow flow, string? selectedId, string? selectedName)
    {
        box.Items.Add(new Choice(null, "Don't change"));
        foreach (var d in AudioDevices(app, flow))
            box.Items.Add(new Choice(d.Id, d.Name, d.Name));
        if (selectedId is not null && box.Items.Cast<Choice>().All(c => c.Id != selectedId))
            box.Items.Add(new Choice(selectedId, $"{selectedName ?? "Unknown device"} (not connected)", selectedName));
        box.SelectedItem = box.Items.Cast<Choice>().FirstOrDefault(c => c.Id == selectedId) ?? box.Items[0];
    }

    public static List<Choice> ModeChoices(AppServices app, string none) =>
        new[] { new Choice(null, none) }.Concat(app.Config.Modes.Select(m => new Choice(m.Name, m.Name))).ToList();

    public static IEnumerable<AudioDevice> AudioDevices(AppServices app, EDataFlow flow)
    {
        if (app.Demo)
        {
            return flow == EDataFlow.Render
                ? new[] { new AudioDevice("demo-headset", "Headset (Arctis Nova Pro)"), new AudioDevice("demo-speakers", "Speakers (Realtek Audio)"), new AudioDevice("demo-tv", "LG TV SSCR2 (NVIDIA High Definition Audio)") }
                : new[] { new AudioDevice("demo-mic", "Microphone (Arctis Nova Pro)"), new AudioDevice("demo-mic2", "Microphone (Shure MV7)") };
        }
        try { return AudioManager.List(flow); }
        catch { return Array.Empty<AudioDevice>(); }
    }

    private static bool Missing(string path)
    {
        var exists = Task.Run(() => System.IO.File.Exists(Environment.ExpandEnvironmentVariables(path)));
        return exists.Wait(TimeSpan.FromSeconds(2)) && !exists.Result;
    }

    private bool Save()
    {
        foreach (var copy in _triggers.Select(t => t.Trigger))
            if (_mode.Triggers.FirstOrDefault(t => t.Id == copy.Id)?.LastDue is { } fired && (copy.LastDue is null || fired > copy.LastDue))
                copy.LastDue = fired;
        string name = _name.Text.Trim();
        string program = _oneOff ? "" : _programPath.Text.Trim().Trim('"');
        var problem = _app.Modes.Validate(name, _hotkey.Hotkey, _isNew ? null : _mode)
            ?? _triggers.Select(t => t.Problem(_oneOff)).FirstOrDefault(p => p is not null)
            ?? (_oneOff ? null : _windows.Problem)
            ?? (program.Length > 0 && !program.Contains("://") && Missing(program) ? $"The program to launch, {program}, doesn't exist." : null);
        if (problem is not null)
        {
            _error.Text = problem;
            _error.Visibility = Visibility.Visible;
            return false;
        }

        _mode.Name = name;
        _mode.OneOff = _oneOff;
        _mode.Triggers = _triggers.Select(t => t.Trigger).ToList();
        _mode.Message = string.IsNullOrWhiteSpace(_message.Text) ? null : _message.Text.Trim();
        _mode.Hotkey = _hotkey.Hotkey;
        _mode.DisplayProfile = _displayProfile;
        _mode.Windows = _windows.Rules;
        _mode.Overlay = _overlay;
        _mode.OverlayLayout = _overlayLayout;
        _mode.FanPreset = _fanPreset;
        bool ownAudio = _displayProfile is null || _audioOverride;
        _mode.Volume = ownAudio ? _volume : null;
        _mode.Priority = _priority;
        _mode.Launch = _launch;
        _mode.Close = _close;
        _mode.ForceClose = _forceClose;
        _mode.Program = program.Length == 0 ? null : new ModeProgram
        {
            Path = program,
            Arguments = string.IsNullOrWhiteSpace(_programArgs.Text) ? null : _programArgs.Text.Trim(),
            AsAdmin = _programAdmin,
            IconPath = string.IsNullOrWhiteSpace(_programIcon.Text) ? null : _programIcon.Text.Trim().Trim('"'),
            ProcessName = string.IsNullOrWhiteSpace(_programProcess.Text) ? null : AppWatcher.Normalize(_programProcess.Text),
        };
        _mode.CloseLaunchedOnExit = _closeLaunched;

        var playback = _playback.SelectedItem as Choice;
        var recording = _recording.SelectedItem as Choice;
        _mode.Audio = !ownAudio || playback?.Id is null && recording?.Id is null ? null : new AudioSnapshot
        {
            PlaybackId = playback?.Id,
            PlaybackName = playback?.Extra,
            RecordingId = recording?.Id,
            RecordingName = recording?.Extra,
            SetCommunications = _comms.IsChecked == true,
        };
        return true;
    }
}

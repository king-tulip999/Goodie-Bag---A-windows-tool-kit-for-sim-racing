using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Audio;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Services;

namespace GoodieBag.Views.Pages;

internal sealed class DisplayProfilesView : Tile
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly MasonryPanel _grid = new() { MinColumnWidth = 440, MaxColumns = 3, Gap = 12, CenterFew = true, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Func<bool> _hasPending;

    public DisplayProfilesView(AppServices app, MainWindow window, Func<bool> hasPending)
    {
        _app = app;
        _window = window;
        _hasPending = hasPending;
        Style = Ui.Res<Style>("Card");
        Margin = new Thickness(0, 0, 0, 16);

        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Ui.Text("Saved profiles", "H2");
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        var save = Ui.LabeledButton("I.Save", "Save current setup", SaveCurrentAsync, "PrimaryButton");
        Grid.SetColumn(save, 1);
        head.Children.Add(save);
        stack.Children.Add(head);
        var caption = Ui.Text("Saved setups like “Desk” and “Sim rig”: which displays are on, arrangement, resolution, refresh rate, main display, HDR, taskbar, wallpapers and audio devices. Give each a keybind to switch between them.", "Caption");
        caption.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(caption);
        stack.Children.Add(_grid);
        Child = stack;
        Ui.Anchor(this, "displays.profiles");
    }

    public void Refresh()
    {
        _grid.Children.Clear();
        var profiles = _app.Config.Displays.Profiles;
        if (profiles.Count == 0)
        {
            _grid.Children.Add(new TextBlock
            {
                Text = "No profiles yet. Set your monitors up the way you like (here or in Windows), then Save current setup. Do that for each setup: desk, triples, TV…",
                Style = Ui.Res<Style>("Muted"), Margin = new Thickness(6, 8, 6, 4),
            });
            return;
        }
        var active = _app.DisplayProfiles.Active;
        foreach (var profile in profiles)
            _grid.Children.Add(ProfileTile(profile, profile == active));
    }

    private FrameworkElement ProfileTile(DisplayProfile profile, bool isActive)
    {
        var stack = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameRow = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        nameRow.Children.Add(new TextBlock { Text = profile.Name, ToolTip = profile.Name, Style = Ui.Res<Style>("H3"), TextTrimming = TextTrimming.CharacterEllipsis });
        var tags = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        if (isActive)
            tags.Children.Add(Ui.Chip("Active", "SageSoft", "SageDeep"));
        if (profile.Hotkey is { } hk)
            tags.Children.Add(Ui.Chip(GoodieBag.Hotkeys.Hotkey.Display(hk)));
        if (tags.Children.Count > 0)
            nameRow.Children.Add(tags);
        top.Children.Add(nameRow);
        var apply = isActive
            ? Ui.LabeledButton("I.Check", "Applied", () => _ = _app.DisplayProfiles.ApplyAsync(profile), "GhostButton")
            : Ui.LabeledButton("I.Zap", "Apply", () => _ = _app.DisplayProfiles.ApplyAsync(profile), "PrimaryButton");
        apply.VerticalAlignment = VerticalAlignment.Top;
        apply.ToolTip = isActive ? "Apply again" : "Apply this profile";
        Grid.SetColumn(apply, 1);
        top.Children.Add(apply);
        stack.Children.Add(top);

        var previewBox = new Tile { Style = Ui.Res<Style>("Inset"), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(6) };
        if (profile.Layout is null)
            previewBox.Child = new TextBlock { Text = "Leaves the arrangement as it is", Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 39, 0, 39) };
        else
        {
            var preview = new MonitorLayoutView { Height = 110 };
            preview.SetTiles(ProfileLayout.Tiles(_app, profile.Layout));
            previewBox.Child = preview;
        }
        stack.Children.Add(previewBox);

        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (profile.Hdr != HdrMode.Unchanged) chips.Children.Add(Ui.Chip(profile.Hdr == HdrMode.On ? "HDR on" : "HDR off"));
        if (profile.Taskbar != TaskbarMode.Unchanged) chips.Children.Add(Ui.Chip(profile.Taskbar == TaskbarMode.MainOnly ? "Taskbar: main display only" : "Taskbar: every display"));
        if (profile.Wallpapers.Count > 0) chips.Children.Add(Ui.Chip(profile.Wallpapers.Count == 1 ? "1 wallpaper" : $"{profile.Wallpapers.Count} wallpapers"));
        if (profile.Audio?.PlaybackName is { } speakers) chips.Children.Add(Ui.Chip("Audio: " + speakers));
        if (profile.Volume is { } vol) chips.Children.Add(Ui.Chip($"Volume {vol}%"));
        if (chips.Children.Count > 0)
            stack.Children.Add(chips);

        if (!isActive)
        {
            var changes = _app.DisplayProfiles.Preview(profile);
            if (changes.Count > 0)
            {
                stack.Children.Add(new TextBlock { Text = "Applying it will", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 8, 0, 4) });
                foreach (var change in changes.Take(4))
                    stack.Children.Add(Ui.ChangeLine(change.Text, change.Area));
                if (changes.Count > 4)
                    stack.Children.Add(new TextBlock { Text = $"…and {changes.Count - 4} more", Style = Ui.Res<Style>("Caption") });
            }
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var edit = Ui.LabeledButton("I.Pencil", "Edit", () => _ = EditAsync(profile, isNew: false), "GhostButton");
        edit.Margin = new Thickness(0, 0, 8, 8);
        actions.Children.Add(edit);
        var update = Ui.LabeledButton("I.Refresh", "Update to current", () => UpdateToCurrent(profile), "GhostButton");
        update.ToolTip = "Save the setup that's on right now (layout, wallpapers and audio devices) into this profile";
        update.Margin = new Thickness(0, 0, 8, 8);
        actions.Children.Add(update);
        var delete = Ui.LabeledButton("I.Trash", "Delete", () => _ = DeleteAsync(profile), "DangerButton");
        delete.Margin = new Thickness(0, 0, 0, 8);
        actions.Children.Add(delete);
        stack.Children.Add(actions);

        return new Tile
        {
            Style = Ui.Res<Style>("Inset"), Padding = new Thickness(14, 12, 14, 6), Child = stack,
            BorderBrush = Ui.Res<Brush>(isActive ? "Sage" : "Line"),
        };
    }

    private async void SaveCurrentAsync()
    {
        if (_hasPending())
        {
            _app.Notify("Apply your changes first", "Profiles save the setup that's actually on.", NoticeKind.Info, important: true);
            return;
        }
        int n = _app.Config.Displays.Profiles.Count + 1;
        string name = $"Profile {n}";
        while (_app.DisplayProfiles.Find(name) is not null) name = $"Profile {++n}";
        await EditAsync(_app.DisplayProfiles.CaptureCurrent(name), isNew: true);
    }

    private void UpdateToCurrent(DisplayProfile profile)
    {
        if (_hasPending())
        {
            _app.Notify("Apply your changes first", "Profiles save the setup that's actually on.", NoticeKind.Info, important: true);
            return;
        }
        _app.DisplayProfiles.Recapture(profile);
        _app.DisplayProfiles.RaiseChanged();
        _app.Notify("Profile updated", $"{profile.Name} now matches your current setup.", NoticeKind.Success);
    }

    private async Task DeleteAsync(DisplayProfile profile)
    {
        string message = "This can't be undone.";
        if (!await _window.Confirm($"Delete “{profile.Name}”?", message, "Delete", danger: true))
            return;
        _app.DisplayProfiles.Remove(profile);
    }

    public void OpenNew() => SaveCurrentAsync();

    public void OpenEditor(int index)
    {
        var profiles = _app.Config.Displays.Profiles;
        if (index < profiles.Count)
            _ = EditAsync(profiles[index], false);
    }

    private async Task EditAsync(DisplayProfile profile, bool isNew)
    {
        var editor = new DisplayProfileEditor(_app, _window, profile, isNew);
        if (!await _window.ShowModal(editor.Card))
            return;
        string oldName = profile.Name;
        editor.SaveInto(profile);
        if (isNew)
            _app.Config.Displays.Profiles.Add(profile);
        else
            _app.DisplayProfiles.Renamed(oldName, profile.Name);
        _app.DisplayProfiles.RaiseChanged();
    }
}

internal sealed class DisplayProfileEditor
{
    private const double LabelWidth = 170;
    private readonly AppServices _app;
    private readonly DisplayProfile _profile;
    private readonly TextBox _name = new() { MaxLength = Ui.NameLength };
    private readonly HotkeyBox _hotkey = new();
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, TextBox> _wallpapers = new(StringComparer.OrdinalIgnoreCase);
    private DisplaySnapshot? _current;
    private int _layoutChoice;
    private HdrMode _hdr;
    private TaskbarMode _taskbar;
    private int? _volume;
    private readonly ComboBox _playback = new(), _recording = new();
    private readonly CheckBox _comms = new() { Content = "Also use them for calls (Discord, Teams…)" };

    public Border Card { get; }

    public DisplayProfileEditor(AppServices app, MainWindow window, DisplayProfile profile, bool isNew)
    {
        _app = app;
        _profile = profile;
        _name.Text = profile.Name;
        _hotkey.SetHotkey(profile.Hotkey);
        _hdr = profile.Hdr;
        _taskbar = profile.Taskbar;
        _layoutChoice = profile.Layout is null ? 2 : 0;
        try { _current = isNew ? profile.Layout : app.Displays.CaptureLayout(); } catch { _current = null; }

        var stack = new StackPanel();
        stack.Children.Add(Ui.FieldRow("Name", _name, LabelWidth));
        stack.Children.Add(Ui.FieldRow("Keybind", _hotkey, LabelWidth));

        stack.Children.Add(Ui.EditorSection("Monitors"));
        string[] layoutOptions = isNew ? new[] { "Save this layout", "", "Don't change displays" } : new[] { "Keep saved layout", "Use current layout", "Don't change displays" };
        var options = layoutOptions.Select((t, i) => (t, i)).Where(x => x.t.Length > 0).ToList();
        stack.Children.Add(Ui.FieldRow("Layout", Ui.Segmented(options.Select(o => o.t).ToList(), Math.Max(0, options.FindIndex(o => o.i == _layoutChoice)),
            i => _layoutChoice = options[i].i, "layout"), LabelWidth,
            "Which monitors are on, their arrangement, resolution, refresh rate, orientation and the main display."));
        stack.Children.Add(Ui.FieldRow("HDR", Ui.Segmented(new[] { "Leave as is", "On", "Off" }, (int)_hdr, i => _hdr = (HdrMode)i, "hdr",
            new[] { "Don't change HDR when this profile is applied", "Turn HDR on (on displays that support it)", "Turn HDR off" }), LabelWidth));
        stack.Children.Add(Ui.FieldRow("Taskbar", Ui.Segmented(new[] { "Leave as is", "Every display", "Main display only" }, (int)_taskbar, i => _taskbar = (TaskbarMode)i, "taskbar",
            new[] { "Don't change the taskbar", "Show the taskbar on every display", "Show the taskbar only on the main display (e.g. keeps it off side screens in a triple setup)" }), LabelWidth));

        stack.Children.Add(Ui.EditorSection("Audio"));
        stack.Children.Add(Ui.FieldHint("The devices to switch to, e.g. headphones at the desk and the rig's speakers on the triples. Saving the current setup fills these in.", 0));
        _volume = profile.Volume;
        AudioPicker.Fill(app, _playback, EDataFlow.Render, profile.Audio?.PlaybackId, profile.Audio?.PlaybackName);
        AudioPicker.Fill(app, _recording, EDataFlow.Capture, profile.Audio?.RecordingId, profile.Audio?.RecordingName);
        stack.Children.Add(Ui.FieldRow("Speakers / headset", _playback, LabelWidth));
        stack.Children.Add(Ui.FieldRow("Microphone", _recording, LabelWidth));
        _comms.IsChecked = profile.Audio?.SetCommunications ?? true;
        stack.Children.Add(Ui.FieldRow("", _comms, LabelWidth));
        stack.Children.Add(Ui.FieldRow("Volume", AudioPicker.Volume(_volume, v => _volume = v), LabelWidth));

        stack.Children.Add(Ui.EditorSection("Wallpapers"));
        stack.Children.Add(Ui.FieldHint("One per monitor; empty keeps whatever it has. Saving the current setup fills these in.", 0));
        foreach (var (key, name) in WallpaperMonitors())
        {
            var box = new TextBox { Tag = "Keep its wallpaper", Text = profile.Wallpapers.FirstOrDefault(w => string.Equals(w.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? "" };
            _wallpapers[key] = box;
            var row = new DockPanel();
            var clear = Ui.IconButton("I.X", "Keep its wallpaper", () => box.Text = "");
            var browse = Ui.IconButton("I.Folder", "Choose an image", () =>
            {
                if (ProgramPicker.BrowseExe("Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*") is { } path)
                    box.Text = path;
            });
            DockPanel.SetDock(clear, Dock.Right);
            DockPanel.SetDock(browse, Dock.Right);
            row.Children.Add(clear);
            row.Children.Add(browse);
            row.Children.Add(box);
            stack.Children.Add(Ui.FieldRow(name, row, LabelWidth));
        }

        var buttons = Ui.DialogButtons(window, isNew ? "Save profile" : "Save changes", Validate);
        Card = Ui.Dialog(window, isNew ? "New display profile" : $"Edit “{profile.Name}”", "Applied with its keybind or from the tray.", stack, buttons, _error, 900);
    }

    private IEnumerable<(string Key, string Name)> WallpaperMonitors()
    {
        var layout = _profile.Layout ?? _current;
        if (layout is not null)
        {
            var listed = _app.Displays.Describe(layout).Where(m => m.Key.Length > 0).Select(m => (m.Key, m.Name)).ToList();
            if (listed.Count > 0)
                return listed;
        }
        try { return _app.Displays.GetMonitors().Where(m => m.Active).Select(m => (m.Key, m.Name)).ToList(); }
        catch { return Array.Empty<(string, string)>(); }
    }

    private bool Validate()
    {
        var problem = _app.DisplayProfiles.Validate(_name.Text.Trim(), _hotkey.Hotkey, _profile)
                      ?? _wallpapers.Values.Select(b => b.Text.Trim()).Where(p => p.Length > 0 && !File.Exists(p)).Select(p => $"Can't find {Path.GetFileName(p)}.").FirstOrDefault();
        _error.Text = problem ?? "";
        _error.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        return problem is null;
    }

    public void SaveInto(DisplayProfile profile)
    {
        profile.Name = _name.Text.Trim();
        profile.Hotkey = _hotkey.Hotkey;
        profile.Hdr = _hdr;
        profile.Taskbar = _taskbar;
        profile.Layout = _layoutChoice switch { 1 => _current ?? profile.Layout, 2 => null, _ => profile.Layout };
        var wallpapers = profile.Wallpapers.Where(w => !_wallpapers.ContainsKey(w.Key)).ToDictionary(w => w.Key, w => w.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, box) in _wallpapers)
            if (box.Text.Trim() is { Length: > 0 } path) wallpapers[key] = path;
        profile.Wallpapers = wallpapers;
        var playback = _playback.SelectedItem as Choice;
        var recording = _recording.SelectedItem as Choice;
        profile.Audio = playback?.Id is null && recording?.Id is null ? null : new AudioSnapshot
        {
            PlaybackId = playback?.Id, PlaybackName = playback?.Extra,
            RecordingId = recording?.Id, RecordingName = recording?.Extra,
            SetCommunications = _comms.IsChecked == true,
        };
        profile.Volume = _volume;
    }
}

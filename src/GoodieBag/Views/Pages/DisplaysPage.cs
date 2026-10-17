using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Display;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class DisplaysPage : UserControl, IPage, IScreenshotStates
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private readonly ArrangementCanvas _canvas = new() { Height = 440 };
    private readonly StackPanel _details = new();
    private readonly WrapPanel _offMonitors = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly Border _pendingBar;
    private readonly TextBlock _pendingText = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly DisplayProfilesView _profiles;

    private List<MonitorInfo> _monitors = new();
    private List<MonitorEdit> _edits = new();
    private List<ArrangeTile> _tiles = new();
    private Dictionary<string, string> _tileOf = new();
    private string? _selected;

    public DisplaysPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Ui.PageFrame(_body), Focusable = false };

        _canvas.Selected += key => { _selected = key; BuildDetails(); };
        _canvas.Moved += key =>
        {
            var tile = _tiles.First(t => t.Key == key);
            foreach (var edit in GroupOf(key))
            {
                edit.X = (int)Math.Round(tile.X);
                edit.Y = (int)Math.Round(tile.Y);
            }
            UpdatePending();
        };

        var revert = new Button { Content = "Discard", Margin = new Thickness(0, 0, 8, 0) };
        revert.Click += (_, _) => Reload();
        var apply = Ui.LabeledButton("I.Check", "Apply changes", () => _ = ApplyAsync(), "PrimaryButton");
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new Tile { Width = 30, Height = 30, CornerRadius = ThemeManager.Round(9), Background = Ui.Res<Brush>("ApricotSoft"),
            Child = new Icon { Data = Ui.Res<Geometry>("I.Info"), Width = 16, Height = 16, Foreground = Ui.Res<Brush>("ApricotDeep") } };
        bar.Children.Add(dot);
        _pendingText.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_pendingText, 1);
        bar.Children.Add(_pendingText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(revert);
        buttons.Children.Add(apply);
        Grid.SetColumn(buttons, 2);
        bar.Children.Add(buttons);
        _profiles = new DisplayProfilesView(app, window, () => HasPending);
        _pendingBar = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(14, 10, 12, 10), Margin = new Thickness(0, 0, 0, 16), Child = bar, Visibility = Visibility.Collapsed, BorderBrush = Ui.Res<Brush>("Apricot") };

        BuildPage();
    }

    private bool _shown, _reloadQueued, _profilesQueued;

    public void OnShown()
    {
        _shown = true;
        if (!HasPending)
            Reload();
        _profiles.Refresh();
        _app.Displays.Changed += OnDisplaysChanged;
        _app.DisplayProfiles.Changed += QueueProfiles;
        Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _shown = false;
        _app.Displays.Changed -= OnDisplaysChanged;
        _app.DisplayProfiles.Changed -= QueueProfiles;
    }

    private void OnDisplaysChanged()
    {
        QueueProfiles();
        if (_reloadQueued) return;
        _reloadQueued = true;
        Later(() =>
        {
            if (!_reloadQueued) return;
            _reloadQueued = false;
            if (_shown && !HasPending) Reload();
        });
    }

    private void QueueProfiles()
    {
        if (_profilesQueued) return;
        _profilesQueued = true;
        Later(() => { _profilesQueued = false; if (_shown) _profiles.Refresh(); });
    }

    private void Later(Action work) => Dispatcher.InvokeAsync(work, DispatcherPriority.Background);

    private void BuildPage()
    {
        var titles = new StackPanel { Margin = new Thickness(0, 0, 0, Ui.HeaderGap) };
        titles.Children.Add(Ui.PluginTitle(Plugins.PluginIds.Displays, "Displays"));
        _body.Children.Add(titles);
        _body.Children.Add(_profiles);
        _body.Children.Add(_pendingBar);

        var arrange = new StackPanel();
        var arrangeHead = new DockPanel { LastChildFill = false };
        var title = Ui.Text("Arrangement", "H2");
        title.VerticalAlignment = VerticalAlignment.Center;
        arrangeHead.Children.Add(title);
        var identify = Ui.LabeledButton("I.Hash", "Identify", () => IdentifyDisplays.Show(_app), "GhostButton");
        identify.Margin = new Thickness(14, 0, 0, 0);
        arrangeHead.Children.Add(identify);
        var refresh = Ui.IconButton("I.Refresh", "Re-read displays", Reload);
        refresh.Margin = new Thickness(6, 0, 0, 0);
        arrangeHead.Children.Add(refresh);
        var hint = new TextBlock { Text = "Monitors snap together edge to edge", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(hint, Dock.Right);
        arrangeHead.Children.Add(hint);
        arrange.Children.Add(arrangeHead);
        var canvasBox = new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(0), Margin = new Thickness(0, 12, 0, 0), Child = _canvas, ClipToBounds = true };
        canvasBox.Background = Ui.Res<Brush>("SurfaceAlt");
        arrange.Children.Add(canvasBox);
        arrange.Children.Add(_offMonitors);
        var arrangeCard = new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 16), Child = arrange };
        Ui.Anchor(arrangeCard, "displays.arrange");

        var detailsCard = new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 16), Child = _details };
        Ui.Anchor(detailsCard, "displays.details");

        _body.Children.Add(arrangeCard);
        _body.Children.Add(detailsCard);

        var note = new Grid();
        note.ColumnDefinitions.Add(new ColumnDefinition());
        note.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        note.Children.Add(new TextBlock
        {
            Text = "Text and app scaling (100%, 125%, 150%…) has no supported Windows API, so it stays in Windows Settings. Night light and color profiles live there too.",
            Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), TextWrapping = TextWrapping.Wrap,
        });
        var open = Ui.LabeledButton("I.Settings", "Windows display settings", () => Ui.OpenUrl("ms-settings:display"), "GhostButton");
        Grid.SetColumn(open, 1);
        note.Children.Add(open);
        _body.Children.Add(new Tile { Style = Ui.Res<Style>("Inset"), Child = note });
    }

    private bool HasPending => _edits.Any(IsEdited);

    private bool IsEdited(MonitorEdit e)
    {
        var m = _monitors.FirstOrDefault(x => x.Key == e.Key);
        return m is not null && (m.X != e.X || m.Y != e.Y || m.Landscape != (e.Width, e.Height) || m.Hz != e.Hz || m.Rotation != e.Rotation || m.Primary != e.Primary);
    }

    private void Reload()
    {
        _reloadQueued = false;
        try
        {
            _monitors = _app.Displays.GetMonitors();
        }
        catch (Exception ex)
        {
            _monitors = new List<MonitorInfo>();
            _app.Notify("Couldn't read displays", ex.Message, NoticeKind.Error, important: true);
        }

        var active = _monitors.Where(m => m.Active).ToList();
        _edits = active.Select(m => new MonitorEdit
        {
            Key = m.Key, X = m.X, Y = m.Y, Width = m.Landscape.W, Height = m.Landscape.H, Hz = m.Hz, Rotation = m.Rotation, Primary = m.Primary,
        }).ToList();
        var groups = active.GroupBy(m => m.Desktop.Length > 0 ? m.Desktop : m.Key).Select(g => g.ToList()).ToList();
        _tileOf = groups.SelectMany(g => g.Select(m => (m.Key, Tile: g[0].Key))).ToDictionary(x => x.Key, x => x.Tile);
        _tiles = groups.Select(g =>
        {
            var m = g[0];
            return new ArrangeTile
            {
                Key = m.Key, Number = string.Join(" + ", g.Select(x => x.Label)), Name = string.Join(" + ", g.Select(x => x.Name)),
                X = m.X, Y = m.Y, Width = m.Width, Height = m.Height, Primary = g.Any(x => x.Primary),
                Detail = $"{m.Width}×{m.Height} · {m.Hz} Hz",
            };
        }).ToList();

        if (_selected is null || _monitors.All(m => m.Key != _selected))
            _selected = active.FirstOrDefault(m => m.Primary)?.Key ?? active.FirstOrDefault()?.Key;
        _canvas.Animate = Motion.Enabled;
        _canvas.SetTiles(_tiles, TileKey(_selected));
        BuildOffMonitors();
        BuildDetails();
        UpdatePending();
    }

    private string? TileKey(string? monitor) => monitor is not null && _tileOf.TryGetValue(monitor, out var tile) ? tile : monitor;

    private IEnumerable<MonitorEdit> GroupOf(string tileKey) => _edits.Where(e => TileKey(e.Key) == tileKey);

    private void BuildOffMonitors()
    {
        _offMonitors.Children.Clear();
        foreach (var m in _monitors.Where(m => !m.Active))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.Monitor"), Width = 15, Height = 15, Foreground = Ui.Res<Brush>("TextFaint") });
            row.Children.Add(new TextBlock { Text = $"{m.Name}", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var state = new TextBlock { Style = Ui.Res<Style>("Caption"), Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            state.Inlines.Add(new System.Windows.Documents.Run("Off") { Foreground = Ui.Res<Brush>("Danger"), FontWeight = FontWeights.SemiBold });
            if (m.Connector.Length > 0) state.Inlines.Add(new System.Windows.Documents.Run(" · " + m.Connector));
            row.Children.Add(state);
            var monitor = m;
            var on = Ui.LabeledButton("I.Power", "Turn on", () => _ = SetEnabledAsync(monitor, true), "SoftButton");
            on.Padding = new Thickness(10, 5, 12, 5);
            row.Children.Add(on);
            _offMonitors.Children.Add(new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(12, 7, 8, 7), Margin = new Thickness(0, 0, 10, 0), Child = row });
        }
    }

    private async Task RenameAsync(MonitorInfo monitor)
    {
        var name = await _window.Prompt("Rename display", $"A label for this display in Goodie Bag (Windows calls it {monitor.HardwareName}). Leave it empty for that name again.",
            monitor.Name == monitor.HardwareName ? "" : monitor.Name, text => text.Length > 40 ? "Keep it to 40 characters" : null);
        if (name is null) return;
        if (name.Length == 0 || name == monitor.HardwareName) _app.Config.Displays.Names.Remove(monitor.Key);
        else _app.Config.Displays.Names[monitor.Key] = name;
        _app.Save();
        Reload();
    }

    private void BuildDetails()
    {
        _details.Children.Clear();
        var monitor = _monitors.FirstOrDefault(m => m.Key == _selected && m.Active);
        var edit = _edits.FirstOrDefault(e => e.Key == _selected);
        if (monitor is null || edit is null)
        {
            _details.Children.Add(Ui.Text("No active display selected.", "Muted"));
            return;
        }

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Tile { Style = Ui.Res<Style>("IconBadge"), Width = 42, Height = 42, Child = new TextBlock { Text = monitor.Label, FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        head.Children.Add(badge);
        var names = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(Ui.Text(monitor.Name, "H2"));
        var rename = Ui.IconButton("I.Pencil", "Rename this display (just a label in Goodie Bag, e.g. Left monitor or Dash)", () => _ = RenameAsync(monitor), "QuietIconButton");
        rename.Width = rename.Height = 28;
        rename.MinHeight = 0;
        rename.Margin = new Thickness(6, 0, 0, 0);
        rename.VerticalAlignment = VerticalAlignment.Center;
        nameRow.Children.Add(rename);
        names.Children.Add(nameRow);
        var bits = new List<string>();
        if (monitor.Connector.Length > 0) bits.Add(monitor.Connector);
        bits.Add($"currently {monitor.Width}×{monitor.Height} at {monitor.Hz} Hz");
        if (monitor.Primary) bits.Add("main display");
        names.Children.Add(Ui.Text(string.Join(" · ", bits), "Caption"));
        Grid.SetColumn(names, 1);
        head.Children.Add(names);
        _details.Children.Add(head);

        var grid = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        _details.Children.Add(grid);

        var resolutions = monitor.Modes.Select(m => (m.Width, m.Height)).Distinct().ToList();
        if (resolutions.Count == 0)
            resolutions.Add((edit.Width, edit.Height));
        var largest = resolutions.OrderByDescending(r => r.Width * r.Height).First();
        var resBox = new ComboBox { MinWidth = 220 };
        foreach (var r in resolutions)
            resBox.Items.Add(new Option<(int, int)>((r.Width, r.Height), $"{r.Width} × {r.Height}" + (r == largest ? "  (native)" : "")));
        Select(resBox, (edit.Width, edit.Height));
        left.Children.Add(Field("Resolution", resBox));

        var hzBox = new ComboBox { MinWidth = 220 };
        void FillHz()
        {
            hzBox.Items.Clear();
            var rates = monitor.Modes.Where(m => m.Width == edit.Width && m.Height == edit.Height).Select(m => m.Hz).Distinct().OrderByDescending(h => h).ToList();
            if (rates.Count == 0) rates.Add(edit.Hz);
            if (!rates.Contains(edit.Hz)) edit.Hz = rates[0];
            foreach (var hz in rates)
                hzBox.Items.Add(new Option<int>(hz, $"{hz} Hz" + (hz == rates[0] ? "  (highest)" : "")));
            Select(hzBox, edit.Hz);
        }
        FillHz();
        left.Children.Add(Field("Refresh rate", hzBox));

        resBox.SelectionChanged += (_, _) =>
        {
            if (resBox.SelectedItem is not Option<(int, int)> o) return;
            (edit.Width, edit.Height) = o.Value;
            FillHz();
            SyncTile(edit);
        };
        hzBox.SelectionChanged += (_, _) =>
        {
            if (hzBox.SelectedItem is not Option<int> o) return;
            edit.Hz = o.Value;
            SyncTile(edit);
        };

        var orientation = new ComboBox { MinWidth = 220 };
        foreach (var deg in new[] { 0, 90, 270, 180 })
            orientation.Items.Add(new Option<int>(deg, deg switch { 90 => "Portrait", 270 => "Portrait (flipped)", 180 => "Landscape (flipped)", _ => "Landscape" }));
        Select(orientation, edit.Rotation);
        orientation.SelectionChanged += (_, _) =>
        {
            if (orientation.SelectedItem is not Option<int> o) return;
            edit.Rotation = o.Value;
            SyncTile(edit);
        };
        right.Children.Add(Field("Orientation", orientation));

        var primary = Ui.Switch(edit.Primary, on =>
        {
            if (!on)
            {
                BuildDetails();
                return;
            }
            foreach (var e in _edits) e.Primary = e == edit;
            foreach (var t in _tiles) t.Primary = t.Key == edit.Key;
            _canvas.Refresh();
            UpdatePending();
            BuildDetails();
        });
        primary.IsEnabled = !edit.Primary;
        right.Children.Add(Field("Main display", primary, edit.Primary ? "This is your main display (taskbar, new windows)." : "Make this the main display."));

        var twins = _monitors.Where(m => m.Active && m.Key != monitor.Key && TileKey(m.Key) == TileKey(monitor.Key)).ToList();
        var others = _monitors.Where(m => m.Active && m.Key != monitor.Key && m.Adapter.Equals(monitor.Adapter)).ToList();
        var show = new ComboBox { MinWidth = 220 };
        show.Items.Add(new Option<string>("", "Its own desktop (extend)"));
        foreach (var o in others)
            show.Items.Add(new Option<string>(o.Key, $"Duplicate {o.Name} ({o.Label})"));
        Select(show, twins.FirstOrDefault()?.Key ?? "");
        show.IsEnabled = others.Count > 0;
        show.SelectionChanged += (_, _) =>
        {
            if (show.SelectedItem is not Option<string> o) return;
            if (o.Value.Length == 0 ? twins.Count == 0 : twins.Any(t => t.Key == o.Value)) return;
            var with = o.Value.Length == 0 ? null : _monitors.FirstOrDefault(m => m.Key == o.Value);
            _ = SetDuplicateAsync(monitor, with);
        };
        string showHelp = others.Count == 0
            ? (_monitors.Count(m => m.Active) > 1 ? "Windows can only duplicate displays on the same graphics card." : "Turn on another display to duplicate it.")
            : twins.Count > 0 ? $"Same picture as {string.Join(", ", twins.Select(t => t.Name))}." : "Applies right away (you'll be asked to keep the change).";
        right.Children.Add(Field("Show", show, showHelp));
        if (twins.Count > 0)
        {
            var switchTo = new WrapPanel { Margin = new Thickness(0, -6, 0, 14) };
            foreach (var t in twins)
            {
                var twin = t;
                var b = Ui.LabeledButton("I.Monitor", $"Settings for {twin.Name} ({twin.Label})", () => { _selected = twin.Key; BuildDetails(); }, "GhostButton");
                b.Margin = new Thickness(0, 0, 8, 0);
                switchTo.Children.Add(b);
            }
            right.Children.Add(switchTo);
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        if (monitor.HdrSupported)
        {
            var hdr = new CheckBox { Style = Ui.Res<Style>("Switch"), IsChecked = monitor.HdrOn, Content = "HDR", Margin = new Thickness(0, 0, 24, 0) };
            hdr.Click += async (_, _) =>
            {
                bool on = hdr.IsChecked == true;
                hdr.IsEnabled = false;
                try
                {
                    await _app.DisplayProfiles.ChangeByHandAsync(() => _app.Displays.SetHdr(monitor, on));
                    _app.Notify(on ? "HDR on" : "HDR off", monitor.Name, NoticeKind.Success);
                }
                catch (Exception ex)
                {
                    _app.Notify("HDR not changed", ex.Message, NoticeKind.Warning, important: true);
                }
                finally { hdr.IsEnabled = true; }
            };
            actions.Children.Add(hdr);
        }
        var off = Ui.LabeledButton("I.Power", "Turn off this display", () => _ = SetEnabledAsync(monitor, false), "DangerButton");
        off.IsEnabled = _monitors.Count(m => m.Active) > 1;
        off.ToolTip = off.IsEnabled ? "Applies right away (you'll be asked to keep the change)" : "You can't turn off your only display";
        actions.Children.Add(off);
        _details.Children.Add(actions);
    }

    private sealed record Option<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }

    private static void Select<T>(ComboBox box, T value) =>
        box.SelectedItem = box.Items.Cast<Option<T>>().FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Value, value)) ?? (box.Items.Count > 0 ? box.Items[0] : null);

    private static StackPanel Field(string label, FrameworkElement control, string? help = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("H3"), Margin = new Thickness(0, 0, 0, 6) });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(control);
        if (help is not null)
            panel.Children.Add(new TextBlock { Text = help, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 5, 0, 0) });
        return panel;
    }

    private void SyncTile(MonitorEdit edit)
    {
        var tile = _tiles.First(t => t.Key == TileKey(edit.Key));
        foreach (var twin in GroupOf(tile.Key).Where(e => e != edit))
            (twin.Width, twin.Height, twin.Rotation) = (edit.Width, edit.Height, edit.Rotation);
        if (tile.Width != edit.DesktopWidth || tile.Height != edit.DesktopHeight)
        {
            var rects = _tiles.Select(t => ((int)Math.Round(t.X), (int)Math.Round(t.Y), (int)Math.Round(t.Width), (int)Math.Round(t.Height))).ToList();
            var at = Arrangement.Resize(rects, _tiles.IndexOf(tile), edit.DesktopWidth, edit.DesktopHeight);
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i] == tile) continue;
                (_tiles[i].X, _tiles[i].Y) = at[i];
                foreach (var other in GroupOf(_tiles[i].Key))
                    (other.X, other.Y) = at[i];
            }
        }
        tile.Width = edit.DesktopWidth;
        tile.Height = edit.DesktopHeight;
        tile.Detail = $"{edit.DesktopWidth}×{edit.DesktopHeight} · {edit.Hz} Hz";
        _canvas.Refresh();
        UpdatePending();
    }

    private void UpdatePending()
    {
        int count = _edits.Count(IsEdited);
        bool pending = count > 0;
        _pendingText.Text = count == 1 ? "1 display has unsaved changes" : $"{count} displays have unsaved changes";
        if (pending && _pendingBar.Visibility != Visibility.Visible)
        {
            _pendingBar.Visibility = Visibility.Visible;
            Motion.Enter(_pendingBar, distance: -8);
        }
        else if (!pending)
        {
            _pendingBar.Visibility = Visibility.Collapsed;
        }
    }

    private bool _changing;

    private async Task ApplyAsync()
    {
        if (_changing) return;
        _changing = true;
        var undo = _app.Displays.CaptureForUndo();
        var edits = _edits.Select(e => e.Copy()).ToList();
        try
        {
            await _app.DisplayProfiles.ChangeByHandAsync(() => _app.Displays.ApplyLayout(edits));
        }
        catch (LayoutApplyException ex) when (ex.Restored)
        {
            _changing = false;
            _app.Notify("Display changes weren't applied", ex.Message, NoticeKind.Warning, important: true);
            return;
        }
        catch (Exception ex)
        {
            _app.Notify("Some display changes didn't apply", ex.Message, NoticeKind.Warning, important: true);
        }
        _changing = false;
        Reload();
        await ConfirmKeepAsync(undo);
    }

    private async Task SetEnabledAsync(MonitorInfo monitor, bool on)
    {
        if (_changing) return;
        _changing = true;
        var undo = _app.Displays.CaptureForUndo();
        try
        {
            await _app.DisplayProfiles.ChangeByHandAsync(() => _app.Displays.SetEnabled(monitor, on));
        }
        catch (Exception ex)
        {
            _changing = false;
            _app.Notify(on ? "Couldn't turn the display on" : "Couldn't turn the display off", ex.Message, NoticeKind.Warning, important: true);
            return;
        }
        await Task.Delay(400);
        _changing = false;
        if (on) _selected = monitor.Key;
        Reload();
        await ConfirmKeepAsync(undo);
    }

    private async Task SetDuplicateAsync(MonitorInfo monitor, MonitorInfo? with)
    {
        if (_changing) return;
        _changing = true;
        var undo = _app.Displays.CaptureForUndo();
        try
        {
            await _app.DisplayProfiles.ChangeByHandAsync(() => _app.Displays.SetDuplicate(monitor, with));
        }
        catch (Exception ex)
        {
            _changing = false;
            _app.Notify(with is null ? "Couldn't extend the display" : "Couldn't duplicate the display", ex.Message, NoticeKind.Warning, important: true);
            BuildDetails();
            return;
        }
        await Task.Delay(400);
        _changing = false;
        _selected = monitor.Key;
        Reload();
        await ConfirmKeepAsync(undo);
    }

    private async Task ConfirmKeepAsync(DisplaySnapshot? undo)
    {
        if (undo is null)
            return;

        int seconds = 15;
        int applies = _app.DisplayProfiles.ApplyCount;
        bool superseded = false;
        var card = _window.DialogCard("Keep these display settings?", "", out var buttons);
        var message = (TextBlock)((StackPanel)card.Child).Children[1];
        void SetText() => message.Text = $"If you can see this, choose Keep. Otherwise everything goes back to how it was in {seconds} seconds.";
        SetText();
        var revert = new Button { Content = "Revert", Margin = new Thickness(0, 0, 8, 0) };
        var keep = new Button { Content = "Keep changes", Style = Ui.Res<Style>("PrimaryButton") };
        revert.Click += (_, _) => _window.CloseModal(false);
        keep.Click += (_, _) => _window.CloseModal(true);
        buttons.Children.Add(revert);
        buttons.Children.Add(keep);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            superseded |= _app.DisplayProfiles.ApplyCount != applies;
            seconds--;
            SetText();
            if (superseded || seconds <= 0)
            {
                timer.Stop();
                _window.CloseModal(false);
            }
        };
        timer.Start();
        bool kept = await _window.ShowModal(card);
        timer.Stop();
        superseded |= _app.DisplayProfiles.ApplyCount != applies;

        if (superseded)
        {
            Reload();
        }
        else if (!kept)
        {
            try
            {
                await _app.DisplayProfiles.ChangeByHandAsync(() => _app.Displays.Restore(undo));
                _app.Notify("Display settings reverted", "Everything is back the way it was.", NoticeKind.Info);
            }
            catch (Exception ex)
            {
                _app.Notify("Couldn't revert", ex.Message, NoticeKind.Error, important: true);
            }
            Reload();
        }
        else
        {
            _app.Modes.MarkApplied(null);
        }
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("edited", () =>
        {
            var right = _edits.OrderByDescending(e => e.X).FirstOrDefault();
            if (right is not null)
            {
                _selected = right.Key;
                right.Hz = 144;
                SyncTile(right);
                _canvas.Select(TileKey(right.Key)!);
                BuildDetails();
            }
            return Task.CompletedTask;
        });
        yield return ("keep-prompt", () =>
        {
            _ = ConfirmKeepAsync(new DisplaySnapshot { Paths = "demo:undo" });
            return Task.CompletedTask;
        });
        yield return ("reverted", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        yield return ("duplicated", () =>
        {
            var left = _monitors.Where(m => m.Active).OrderBy(m => m.X).FirstOrDefault();
            var center = _monitors.FirstOrDefault(m => m.Active && m.Primary);
            if (left is null || center is null || left == center) return Task.CompletedTask;
            _app.Displays.SetDuplicate(left, center);
            _selected = left.Key;
            Reload();
            return Task.CompletedTask;
        });
        yield return ("extended", () =>
        {
            if (_monitors.FirstOrDefault(m => m.Key == _selected) is { } left)
            {
                _app.Displays.SetDuplicate(left, null);
                Reload();
            }
            return Task.CompletedTask;
        });
        yield return ("profile-editor", () =>
        {
            _profiles.OpenEditor(0);
            return Task.CompletedTask;
        });
        yield return ("profile-editor-closed", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
    }
}

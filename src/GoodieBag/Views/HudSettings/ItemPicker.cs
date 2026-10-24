using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GoodieBag.Controls;
using GoodieBag.SimRacing.Hud;
using GoodieBag.SimRacing.Hud.Editors;

namespace GoodieBag.Views.HudSettings;

internal sealed record PickerLane(string Key, string? Name, string Value, string Default);

internal sealed class ItemPicker : StackPanel
{
    private const double DragStart = 5;

    private readonly IReadOnlyList<HudCategory> _categories;
    private readonly IReadOnlyList<PickerLane> _lanes;
    private readonly Action<IReadOnlyList<string>> _save;
    private readonly ItemPickerModel _model;
    private readonly Grid _body = new() { Background = Brushes.Transparent };
    private readonly StackPanel _lanesHost = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly List<WrapPanel> _lanePanels = new();
    private readonly Dictionary<string, (IReadOnlySet<string> Hidden, IReadOnlySet<string> Blocked)> _marks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Button _add;
    private readonly string _title;
    private int _addLane;

    private (int Lane, string Id, FrameworkElement Chip, Point At)? _press;
    private bool _dragging;
    private Border? _ghost;
    private Rectangle? _marker;
    private (int Lane, int Index)? _target;
    private (int Lane, string Id)? _refocus;

    public ItemPicker(string title, string? help, IReadOnlyList<HudChoice> choices, IReadOnlyList<HudCategory> categories, IReadOnlyList<PickerLane> lanes, Action<IReadOnlyList<string>> save, bool showTitle = true)
    {
        _title = title;
        _categories = categories;
        _lanes = lanes;
        _save = save;
        _model = new ItemPickerModel(choices, lanes.Select(l => ItemPickerModel.Split(l.Value)));
        Margin = new Thickness(0, 4, 0, 10);

        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _add = Ui.LabeledButton("I.Plus", "Add", () => { }, "SoftButton");
        _add.Click += (_, _) => OpenAdd();
        System.Windows.Automation.AutomationProperties.SetName(_add, "Add to " + title);
        var more = Ui.IconButton("I.More", "All, none or default", () => { });
        more.Click += (_, _) => OpenMore(more);
        more.Margin = new Thickness(6, 0, 0, 0);
        if (help is not null && !showTitle) tools.Children.Add(HelpTip.For(help, title));
        tools.Children.Add(_add);
        tools.Children.Add(more);
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools);
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (showTitle) name.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        if (help is not null && showTitle) name.Children.Add(HelpTip.For(help, title));
        head.Children.Add(name);
        Children.Add(head);

        _body.Children.Add(_lanesHost);
        _body.Children.Add(_overlay);
        _body.PreviewMouseMove += OnMove;
        _body.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_dragging) { DropChip(); e.Handled = true; }
            _press = null;
        };
        _body.LostMouseCapture += (_, _) => { if (_dragging) EndDrag(); };
        Children.Add(_body);
        Rebuild();
    }

    public void ShowAdd() => OpenAdd();

    public void Reload(IReadOnlyList<string> values)
    {
        _model.Reset(values);
        Rebuild();
    }

    public void Mark(string key, IReadOnlySet<string> hidden, IReadOnlySet<string> blocked)
    {
        _marks[key] = (hidden, blocked);
        Rebuild();
    }

    private bool Blocked(int lane, string id) => _marks.TryGetValue(_lanes[lane].Key, out var m) && m.Blocked.Contains(id);

    private bool Hidden(int lane, string id) => _marks.TryGetValue(_lanes[lane].Key, out var m) && m.Hidden.Contains(id);

    private void Save()
    {
        _save(_model.Values);
        Rebuild();
    }

    private void Rebuild()
    {
        _lanesHost.Children.Clear();
        _lanePanels.Clear();
        for (int lane = 0; lane < _lanes.Count; lane++)
        {
            if (_lanes.Count > 1)
                _lanesHost.Children.Add(new TextBlock { Text = _lanes[lane].Name, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, lane == 0 ? 0 : 10, 0, 4) });
            var panel = new WrapPanel { Tag = lane, MinHeight = 32 };
            foreach (var id in _model.Lanes[lane]) panel.Children.Add(Chip(lane, id));
            if (_model.Lanes[lane].Count == 0)
            {
                var empty = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 6), Tag = "empty" };
                empty.SetResourceReference(Border.BorderBrushProperty, "Line");
                empty.Child = new TextBlock { Text = _lanes.Count > 1 ? "Empty: drag items here, or Add" : "Nothing shown: Add some", Style = Ui.Res<Style>("Caption") };
                panel.Children.Add(empty);
            }
            _lanePanels.Add(panel);
            _lanesHost.Children.Add(panel);
        }
        if (_refocus is { } r)
        {
            _refocus = null;
            var chip = _lanePanels.ElementAtOrDefault(r.Lane)?.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Tag as string == r.Id);
            if (chip is not null) Dispatcher.BeginInvoke(() => chip.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private FrameworkElement Chip(int lane, string id)
    {
        var choice = _model.Choice(id);
        bool hidden = Hidden(lane, id);
        var grip = new Icon { Data = Ui.Res<Geometry>("I.Grip"), Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Opacity = 0.7 };
        grip.SetResourceReference(Control.ForegroundProperty, "AccentText");
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(grip);
        row.Children.Add(new TextBlock { Text = choice.Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        if (hidden) row.Children.Add(new TextBlock { Text = "doesn't fit", FontStyle = FontStyles.Italic, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var x = new Button { Style = Ui.Res<Style>("QuietButton"), Padding = new Thickness(4), ToolTip = "Take out", Focusable = false, Content = new Icon { Data = Ui.Res<Geometry>("I.X"), Width = 10, Height = 10 } };
        System.Windows.Automation.AutomationProperties.SetName(x, "Take out " + choice.Label);
        x.Click += (_, _) => { _model.Remove(lane, id); Save(); };
        row.Children.Add(x);
        var chip = new Tile
        {
            Style = Ui.Res<Style>("Chip"), Child = row, Tag = id, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(6, 0, 2, 0), MinHeight = 30,
            Cursor = Cursors.SizeAll, Focusable = true, FocusVisualStyle = Ui.Res<Style>("FocusRing"), Opacity = hidden ? 0.6 : 1,
            ToolTip = hidden ? "Doesn't fit at this width: widen the widget, or drag it earlier" : choice.Help,
        };
        chip.SetValue(TextElement.FontSizeProperty, 12.5);
        chip.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        chip.SetResourceReference(TextElement.ForegroundProperty, "AccentText");
        System.Windows.Automation.AutomationProperties.SetName(chip, choice.Label + (_lanes.Count > 1 ? $" ({_lanes[lane].Name})" : "") + ". Drag, or Ctrl+arrows to move; Delete to take out");
        chip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInside(d, x)) return;
            _press = (lane, id, chip, e.GetPosition(_body));
            chip.Focus();
        };
        chip.KeyDown += (_, e) => ChipKey(lane, id, e);
        return chip;
    }

    private static bool IsInside(DependencyObject d, DependencyObject ancestor)
    {
        for (var at = d; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (ReferenceEquals(at, ancestor)) return true;
        return false;
    }

    private void ChipKey(int lane, string id, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        int at = _model.Lanes[lane].IndexOf(id);
        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                _model.Remove(lane, id);
                Save();
                e.Handled = true;
                break;
            case Key.Left when ctrl:
            case Key.Right when ctrl:
                int to = e.Key == Key.Left ? at - 1 : at + 2;
                if (to >= 0 && _model.Move(lane, id, lane, to)) { _refocus = (lane, id); Save(); }
                e.Handled = true;
                break;
            case Key.Up when ctrl && lane > 0:
            case Key.Down when ctrl && lane < _lanes.Count - 1:
                int other = e.Key == Key.Up ? lane - 1 : lane + 1;
                if (_model.Move(lane, id, other, _model.Lanes[other].Count)) { _refocus = (other, id); Save(); }
                e.Handled = true;
                break;
        }
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_press is not { } press) return;
        if (e.LeftButton != MouseButtonState.Pressed) { _press = null; return; }
        var at = e.GetPosition(_body);
        if (!_dragging)
        {
            if ((at - press.At).Length < DragStart) return;
            _dragging = true;
            _body.CaptureMouse();
            press.Chip.Opacity = 0.35;
            _ghost = new Border { Padding = new Thickness(10, 5, 10, 5), BorderThickness = new Thickness(1.5), Child = new TextBlock { Text = _model.Choice(press.Id).Label, FontSize = 12.5 } };
            _ghost.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            _ghost.SetResourceReference(Border.BorderBrushProperty, "AccentText");
            _ghost.SetResourceReference(TextElement.ForegroundProperty, "AccentText");
            _marker = new Rectangle { Width = 2, Height = 28 };
            _marker.SetResourceReference(Shape.FillProperty, "AccentText");
            _overlay.Children.Add(_marker);
            _overlay.Children.Add(_ghost);
        }
        Canvas.SetLeft(_ghost!, at.X + 8);
        Canvas.SetTop(_ghost!, at.Y - 14);
        _target = Target(at);
        PlaceMarker();
    }

    private (int Lane, int Index) Target(Point at)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < _lanePanels.Count; i++)
        {
            var r = Bounds(_lanePanels[i]);
            double distance = at.Y < r.Top ? r.Top - at.Y : at.Y > r.Bottom ? at.Y - r.Bottom : 0;
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }
        int index = 0;
        foreach (var chip in Chips(best))
        {
            var r = Bounds(chip);
            bool before = r.Bottom < at.Y || (at.Y >= r.Top && at.Y <= r.Bottom && r.Left + r.Width / 2 < at.X);
            if (before) index++;
        }
        return (best, index);
    }

    private IEnumerable<FrameworkElement> Chips(int lane) =>
        _lanePanels[lane].Children.OfType<FrameworkElement>().Where(c => c.Tag is string id && id != "empty");

    private Rect Bounds(FrameworkElement e) => e.TransformToAncestor(_body).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    private void PlaceMarker()
    {
        if (_marker is null || _target is not { } t) return;
        var chips = Chips(t.Lane).ToList();
        Rect r;
        if (chips.Count == 0) { r = Bounds(_lanePanels[t.Lane]); Canvas.SetLeft(_marker, r.Left); }
        else if (t.Index < chips.Count) { r = Bounds(chips[t.Index]); Canvas.SetLeft(_marker, r.Left - 4); }
        else { r = Bounds(chips[^1]); Canvas.SetLeft(_marker, r.Right - 4); }
        Canvas.SetTop(_marker, r.Top);
        _marker.Height = Math.Max(20, Math.Min(r.Height, 30));
    }

    private void DropChip()
    {
        var press = _press;
        var target = _target;
        EndDrag();
        if (press is { } p && target is { } t && _model.Move(p.Lane, p.Id, t.Lane, t.Index))
        {
            _refocus = (t.Lane, p.Id);
            Save();
        }
        else Rebuild();
    }

    private void EndDrag()
    {
        _dragging = false;
        _press = null;
        _target = null;
        _overlay.Children.Clear();
        _ghost = null;
        _marker = null;
        if (_body.IsMouseCaptured) _body.ReleaseMouseCapture();
        foreach (var panel in _lanePanels)
            foreach (var chip in panel.Children.OfType<FrameworkElement>()) if (chip.Opacity < 0.5) chip.Opacity = 1;
    }

    private void OpenMore(Button anchor)
    {
        bool full = _marks.Values.Any(m => m.Hidden.Count > 0 || m.Blocked.Count > 0);
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        void Item(string header, string tip, Action act, bool enabled = true)
        {
            var item = new MenuItem { Header = header, ToolTip = tip, IsEnabled = enabled };
            ToolTipService.SetShowOnDisabled(item, true);
            item.Click += (_, _) => act();
            menu.Items.Add(item);
        }
        Item("All", full ? "No room: widen the widget first" : "Add every one (the ones shown keep their order)", () => { _model.AddAll(); Save(); }, !full);
        Item("None", "Take every one out", () => { _model.Clear(); Save(); });
        Item("Default", "As the widget starts", () => { _model.Reset(_lanes.Select(l => l.Default).ToList()); Save(); });
        menu.IsOpen = true;
    }

    private void OpenAdd()
    {
        var search = new TextBox { Tag = "Search…", Padding = new Thickness(28, 6, 8, 6), MinWidth = 280 };
        System.Windows.Automation.AutomationProperties.SetName(search, "Search " + _title);
        var searchIcon = new Icon { Data = Ui.Res<Geometry>("I.Search"), Width = 13, Height = 13, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10, 0, 0, 0) };
        searchIcon.SetResourceReference(Control.ForegroundProperty, "TextFaint");
        var card = new StackPanel();
        card.Children.Add(new Grid { Children = { search, searchIcon } });
        if (_lanes.Count > 1)
        {
            var lane = Ui.Segmented(_lanes.Select(l => "Add to " + l.Name).ToList(), _addLane, i => _addLane = i, "addlane");
            lane.Margin = new Thickness(0, 8, 0, 0);
            card.Children.Add(lane);
        }
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        KeyboardNavigation.SetDirectionalNavigation(list, KeyboardNavigationMode.Contained);
        card.Children.Add(new ScrollViewer { MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list, Focusable = false });
        var popup = new Popup
        {
            PlacementTarget = _add, Placement = PlacementMode.Custom, StaysOpen = false, AllowsTransparency = true,
            CustomPopupPlacementCallback = (size, target, _) => new[] { new CustomPopupPlacement(new Point(target.Width - size.Width + 40, target.Height), PopupPrimaryAxis.Horizontal) },
            Child = new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(10), Margin = new Thickness(0, 4, 0, 0), Child = card, MaxWidth = 380 },
        };
        RenderOptions.SetClearTypeHint(popup.Child, ClearTypeHint.Enabled);
        Ui.FollowTarget(popup);

        void Fill()
        {
            list.Children.Clear();
            var groups = _model.Grouped(_categories, search.Text.Trim());
            foreach (var (title, items) in groups)
            {
                if (title is not null)
                    list.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(6, list.Children.Count == 0 ? 2 : 10, 0, 4) });
                foreach (var c in items) list.Children.Add(Option(c));
            }
            if (groups.Count == 0) list.Children.Add(new TextBlock { Text = "Nothing matches", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(6, 4, 0, 4) });
        }

        Button Option(HudChoice c)
        {
            bool added = _model.IsAdded(c.Id), blocked = !added && Blocked(_addLane, c.Id);
            var line = new DockPanel { LastChildFill = true };
            if (added)
            {
                var tag = new TextBlock { Text = "Added", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                DockPanel.SetDock(tag, Dock.Right);
                line.Children.Add(tag);
            }
            var label = new TextBlock { Text = c.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            if (!added && !blocked) label.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            line.Children.Add(label);
            var b = new Button
            {
                Style = Ui.Res<Style>("QuietButton"), Content = line, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 6, 8, 6),
                IsEnabled = !added && !blocked, Opacity = added || blocked ? 0.5 : 1,
                ToolTip = blocked ? "No room: take one out first, or widen the widget" : c.Help,
            };
            ToolTipService.SetShowOnDisabled(b, true);
            b.Click += (_, _) =>
            {
                if (_model.Add(c.Id, _addLane)) Save();
                Fill();
                search.Focus();
            };
            return b;
        }

        search.TextChanged += (_, _) => Fill();
        search.PreviewKeyDown += (_, e) =>
        {
            var first = list.Children.OfType<Button>().FirstOrDefault(b => b.IsEnabled);
            if (e.Key == Key.Down && first is not null) { first.Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter && first is not null && search.Text.Trim().Length > 0) { first.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); e.Handled = true; }
        };
        popup.Child.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            popup.IsOpen = false;
            _add.Focus();
            e.Handled = true;
        };
        Fill();
        popup.IsOpen = true;
        Dispatcher.BeginInvoke(() => { search.Focus(); Keyboard.Focus(search); }, System.Windows.Threading.DispatcherPriority.Input);
    }
}

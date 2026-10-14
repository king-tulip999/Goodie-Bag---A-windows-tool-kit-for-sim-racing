using GoodieBag.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GoodieBag.Controls;
using GoodieBag.SimRacing.Setups;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class SetupTree : Grid
{
    public const double RowHeight = 30, Indent = 20, GapHeight = 14;
    private const double Threshold = 6;

    private readonly StackPanel _rows = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly ScrollViewer _scroll;

    public SetupTree(ScrollViewer scroll)
    {
        _scroll = scroll;
        Background = Brushes.Transparent;
        Children.Add(_rows);
        Children.Add(_overlay);
        PreviewMouseLeftButtonDown += OnPress;
        PreviewMouseMove += OnMove;
        PreviewMouseLeftButtonUp += OnRelease;
        LostMouseCapture += (_, e) => { if (_drag is not null && ReferenceEquals(e.OriginalSource, this)) Cancel(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _drag is not null) { Cancel(); e.Handled = true; } };
    }

    public event Action<SetupNode, MouseButtonEventArgs>? RowClicked;

    public event Action<SetupNode>? Toggled;

    public event Action<SetupNode>? Checked;

    public event Action<List<SetupNode>, SetupNode?, SetupNode?, List<SetupNode>?>? Dropped;

    public Func<MouseEventArgs, (SetupNode Folder, FrameworkElement Element)?>? OutsideTarget { get; set; }

    public IEnumerable<SetupNode> Visible => _rows.Children.OfType<FrameworkElement>().Select(r => r.Tag).OfType<SetupNode>();

    public void Show(IEnumerable<(SetupNode Node, int Depth, bool Open, bool Selected, bool Target, int NewCount)> items)
    {
        _rows.Children.Clear();
        var open = new Stack<OpenFolder>();
        void Close(bool more)
        {
            var f = open.Pop();
            if (f.Listed) _rows.Children.Add(Gap(f.Node, f.Depth, more));
        }
        foreach (var (node, depth, isOpen, selected, target, fresh) in items)
        {
            while (open.Count > 0 && open.Peek().Depth >= depth) Close(more: true);
            if (open.Count > 0) open.Peek().Listed = true;
            _rows.Children.Add(Row(node, depth, isOpen, selected, target, fresh));
            if (node.IsFolder && isOpen) open.Push(new OpenFolder(node, depth));
        }
        while (open.Count > 0) Close(more: false);
    }

    private sealed class OpenFolder(SetupNode node, int depth)
    {
        public SetupNode Node { get; } = node;
        public int Depth { get; } = depth;
        public bool Listed { get; set; }
    }

    private FrameworkElement Row(SetupNode node, int depth, bool open, bool selected, bool target, int fresh)
    {
        var grid = new Grid { Height = RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(depth * Indent + 4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (node.IsFolder)
        {
            var arrow = new Button
            {
                Style = Ui.Res<Style>("QuietIconButton"), Width = 22, Height = 22, Padding = new Thickness(0), Focusable = false,
                Content = new Icon { Data = Ui.Res<Geometry>(open ? "I.ChevronDown" : "I.ChevronRight"), Width = 13, Height = 13 },
                ToolTip = open ? "Close folder" : "Open folder",
            };
            ClickGuard.SetAllowRepeat(arrow, true);
            arrow.Click += (_, _) => Toggled?.Invoke(node);
            Grid.SetColumn(arrow, 1);
            grid.Children.Add(arrow);
        }
        else
        {
            var tick = new CheckBox { IsChecked = selected, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Focusable = false, ToolTip = "Select" };
            tick.Click += (_, _) => Checked?.Invoke(node);
            Grid.SetColumn(tick, 1);
            grid.Children.Add(tick);
        }
        var icon = new Icon
        {
            Data = Ui.Res<Geometry>(node.IsFolder ? "I.Folder" : "I.FileText"), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Ui.Res<Brush>(node.IsFolder ? (target ? "AccentText" : "GoldDeep") : "TextFaint"),
        };
        Grid.SetColumn(icon, 2);
        grid.Children.Add(icon);

        var name = TextCase.Verbatim(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) });
        string stem = node.IsFolder ? node.Name : System.IO.Path.GetFileNameWithoutExtension(node.Name);
        name.Children.Add(new TextBlock
        {
            Text = stem, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Style = Ui.Res<Style>("Body"), FontWeight = node.IsFolder ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Ui.Res<Brush>(target ? "AccentText" : "Text"),
        });
        if (!node.IsFolder)
            name.Children.Add(new TextBlock { Text = System.IO.Path.GetExtension(node.Name), Foreground = Ui.Res<Brush>("TextFaint"), VerticalAlignment = VerticalAlignment.Center });
        if (!node.IsFolder && fresh > 0)
        {
            var chip = Ui.Chip("New", "AccentSoft", "AccentText");
            chip.Margin = new Thickness(8, 0, 0, 0);
            chip.Padding = new Thickness(6, 0, 6, 0);
            chip.VerticalAlignment = VerticalAlignment.Center;
            name.Children.Add(chip);
        }
        Grid.SetColumn(name, 3);
        grid.Children.Add(name);

        string meta = node.IsFolder
            ? Plural(node.Files, "setup") + (fresh > 0 ? $" · {fresh} new" : "")
            : $"{Size(node.Size)} · {Date(node.Modified)}";
        var metaText = new TextBlock { Text = meta, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        if (node.IsFolder && fresh > 0) metaText.Foreground = Ui.Res<Brush>("AccentText");
        Grid.SetColumn(metaText, 4);
        grid.Children.Add(metaText);

        var row = new Tile
        {
            Tag = node, Child = grid, CornerRadius = ThemeManager.Round(6), Cursor = Cursors.Arrow,
            Background = selected ? Ui.Res<Brush>("AccentSoft") : Brushes.Transparent,
            RenderTransform = new TranslateTransform(),
        };
        row.SetValue(DepthProperty, depth);
        row.SetValue(ParentFolderProperty, node.Parent);
        row.MouseEnter += (_, _) => { if (_drag is null && !selected) row.Background = Ui.Res<Brush>("SurfaceAlt"); };
        row.MouseLeave += (_, _) => { if (_drag is null && !selected) row.Background = Brushes.Transparent; };
        return row;
    }

    private FrameworkElement Gap(SetupNode folder, int depth, bool divider)
    {
        var gap = new Grid { Height = GapHeight, Tag = new GapOf(folder), Background = Brushes.Transparent, RenderTransform = new TranslateTransform() };
        gap.SetValue(DepthProperty, depth);
        gap.SetValue(ParentFolderProperty, folder.Parent);
        if (divider)
            gap.Children.Add(new Border
            {
                Height = 1, VerticalAlignment = VerticalAlignment.Center, Background = Ui.Res<Brush>("LineStrong"),
                Margin = new Thickness((depth + 1) * Indent + 4, 0, 16, 0), IsHitTestVisible = false,
            });
        return gap;
    }

    private sealed record GapOf(SetupNode Folder);

    private static readonly DependencyProperty DepthProperty = DependencyProperty.RegisterAttached("Depth", typeof(int), typeof(SetupTree));
    private static readonly DependencyProperty ParentFolderProperty = DependencyProperty.RegisterAttached("ParentFolder", typeof(SetupNode), typeof(SetupTree));

    private static int DepthOf(UIElement e) => (int)e.GetValue(DepthProperty);
    private static SetupNode? ParentOf(UIElement e) => (SetupNode?)e.GetValue(ParentFolderProperty);

    public static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
    public static string Size(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576.0:0.0} MB" : $"{bytes / 1024.0:0.0} KB";
    public static string Date(DateTime t)
    {
        double days = (DateTime.Now.Date - t.Date).TotalDays;
        return days < 1 ? "Today" : days < 2 ? "Yesterday" : t.Year == DateTime.Now.Year ? Dates.Short(t) : Dates.Date(t);
    }

    private sealed class Drag
    {
        public required FrameworkElement Slot;
        public required List<SetupNode> Nodes;
        public required SetupNode OriginalParent;
        public required Border Ghost;
        public double GrabY;
        public SetupNode? Into;
        public FrameworkElement? IntoElement;
        public Brush? IntoBackground;
        public readonly List<UIElement> Hidden = new();
    }

    private FrameworkElement? _pressed;
    private Point _pressAt;
    private Drag? _drag;

    public Func<SetupNode, List<SetupNode>>? DragSet { get; set; }

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressed = null;
        if (e.OriginalSource is DependencyObject d && (Inside<ButtonBase>(d) || Inside<TextBox>(d))) return;
        _pressed = RowOf(e.OriginalSource as DependencyObject);
        _pressAt = e.GetPosition(_rows);
    }

    private static bool Inside<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T) return true;
        return false;
    }

    private FrameworkElement? RowOf(DependencyObject? d)
    {
        for (; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement fe && ReferenceEquals(VisualTreeHelper.GetParent(fe), _rows) && fe.Tag is SetupNode) return fe;
        return null;
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not null) { Finish(); e.Handled = true; return; }
        if (_pressed?.Tag is SetupNode node && RowOf(e.OriginalSource as DependencyObject) == _pressed) RowClicked?.Invoke(node, e);
        _pressed = null;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_rows);
        if (_drag is null)
        {
            if (_pressed is null || e.LeftButton != MouseButtonState.Pressed) return;
            if (Math.Abs(p.Y - _pressAt.Y) < Threshold && Math.Abs(p.X - _pressAt.X) < Threshold) return;
            Start(_pressed);
            if (_drag is null) return;
        }
        var d = _drag;
        Canvas.SetTop(d.Ghost, e.GetPosition(_overlay).Y - d.GrabY);
        AutoScroll(e);

        SetupNode? into = null;
        FrameworkElement? intoElement = null;
        if (OutsideTarget?.Invoke(e) is { } outside)
        {
            if (SetupStore.Movable(d.Nodes, outside.Folder).Count > 0) (into, intoElement) = outside;
        }
        else if (Hit(p.Y) is { } hit && hit != d.Slot)
        {
            if (hit.Tag is GapOf gap)
            {
                var parent = gap.Folder.Parent;
                if (parent is not null && Allowed(d, parent)) PlaceAfter(d, hit, parent, DepthOf(hit));
            }
            else if (hit.Tag is SetupNode node && !d.Hidden.Contains(hit))
            {
                var parent = ParentOf(hit);
                double f = (p.Y - Top(hit)) / hit.ActualHeight;
                bool below = _rows.Children.IndexOf(hit) > _rows.Children.IndexOf(d.Slot);
                bool farEdge = below ? f > 0.75 : f < 0.25;
                if (node.IsFolder && !farEdge && SetupStore.Movable(d.Nodes, node).Count > 0) { into = node; intoElement = hit; }
                else if (parent is not null && Allowed(d, parent))
                {
                    if (below) PlaceAfter(d, SubtreeEnd(hit), parent, DepthOf(hit));
                    else PlaceBefore(d, hit, parent, DepthOf(hit));
                }
            }
        }
        else if (Hit(p.Y) is null && p.Y > 0 && d.Nodes[0].Car.Root is { } root && Allowed(d, root))
            PlaceAfter(d, LastVisible(), root, 0);

        if (into != d.Into) Highlight(d, into, intoElement);
    }

    private static bool Allowed(Drag d, SetupNode parent) =>
        parent == d.OriginalParent || d.Nodes.All(n => n.Car == parent.Car && n != parent && !n.IsAncestorOf(parent));

    private void Start(FrameworkElement row)
    {
        var node = (SetupNode)row.Tag;
        var nodes = DragSet?.Invoke(node) ?? new List<SetupNode> { node };
        if (!nodes.Contains(node)) nodes.Insert(0, node);
        var slotTop = Top(row);
        var ghost = new Tile
        {
            Width = row.ActualWidth, Height = RowHeight, CornerRadius = ThemeManager.Round(6), Opacity = 0.95,
            Background = Ui.Res<Brush>("TileRaised"), BorderBrush = Ui.Res<Brush>("Accent"), BorderThickness = new Thickness(1.5),
            Child = GhostContent(node, nodes.Count),
        };
        _drag = new Drag { Slot = row, Nodes = nodes, OriginalParent = node.Parent!, Ghost = ghost, GrabY = _pressAt.Y - slotTop };
        if (ghost.Child is StackPanel content) content.Margin = new Thickness(10 + DepthOf(row) * Indent, 0, 10, 0);
        Canvas.SetLeft(ghost, 0);
        Canvas.SetTop(ghost, slotTop);
        _overlay.Children.Add(ghost);
        if (row is Tile slot) { slot.Child.Visibility = Visibility.Hidden; slot.BorderBrush = Ui.Res<Brush>("Accent"); slot.BorderThickness = new Thickness(1); slot.Background = Ui.Res<Brush>("AccentSoft"); }
        foreach (var hide in Subtree(row)) { hide.Visibility = Visibility.Collapsed; _drag.Hidden.Add(hide); }
        foreach (var other in _rows.Children.OfType<FrameworkElement>().Where(r => r != row && r.Tag is SetupNode n && nodes.Contains(n)).ToList())
        {
            other.Opacity = 0.4;
            _drag.Hidden.Add(other);
            foreach (var hide in Subtree(other)) { hide.Visibility = Visibility.Collapsed; _drag.Hidden.Add(hide); }
        }
        CaptureMouse();
        Focus();
    }

    private static FrameworkElement GhostContent(SetupNode node, int count)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        panel.Children.Add(new Icon { Data = Ui.Res<Geometry>(node.IsFolder ? "I.Folder" : "I.FileText"), Width = 16, Height = 16, Foreground = Ui.Res<Brush>("AccentText") });
        panel.Children.Add(new TextBlock { Text = node.Name, Margin = new Thickness(8, 0, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("Text"), VerticalAlignment = VerticalAlignment.Center });
        if (count > 1)
        {
            var chip = Ui.Chip($"{count} items", "AccentSoft", "AccentText");
            chip.Margin = new Thickness(10, 0, 0, 0);
            panel.Children.Add(chip);
        }
        return panel;
    }

    private FrameworkElement? Hit(double y)
    {
        foreach (FrameworkElement child in _rows.Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            double top = Top(child);
            if (y >= top && y < top + child.ActualHeight) return child;
        }
        return null;
    }

    private static double Top(FrameworkElement e) => LayoutInformation.GetLayoutSlot(e).Top;

    private List<FrameworkElement> Subtree(FrameworkElement row)
    {
        var list = new List<FrameworkElement>();
        if (row.Tag is not SetupNode { IsFolder: true } folder) return list;
        int depth = DepthOf(row);
        for (int i = _rows.Children.IndexOf(row) + 1; i < _rows.Children.Count; i++)
        {
            var e = (FrameworkElement)_rows.Children[i];
            bool mine = DepthOf(e) > depth || (e.Tag is GapOf g && g.Folder == folder);
            if (!mine) break;
            list.Add(e);
        }
        return list;
    }

    private FrameworkElement SubtreeEnd(FrameworkElement row) => Subtree(row).LastOrDefault() ?? row;

    private FrameworkElement? LastVisible() => _rows.Children.OfType<FrameworkElement>().LastOrDefault(c => c.Visibility == Visibility.Visible && c != _drag?.Slot);

    private void PlaceBefore(Drag d, FrameworkElement target, SetupNode parent, int depth)
    {
        int at = _rows.Children.IndexOf(target), from = _rows.Children.IndexOf(d.Slot);
        if (from == at - 1 && ParentOf(d.Slot) == parent) return;
        MoveSlot(d, at > from ? at - 1 : at, parent, depth);
    }

    private void PlaceAfter(Drag d, FrameworkElement? target, SetupNode parent, int depth)
    {
        if (target is null) return;
        int at = _rows.Children.IndexOf(target), from = _rows.Children.IndexOf(d.Slot);
        if (from == at + 1 && ParentOf(d.Slot) == parent) return;
        MoveSlot(d, at >= from ? at : at + 1, parent, depth);
    }

    private void MoveSlot(Drag d, int index, SetupNode parent, int depth)
    {
        var block = new List<UIElement> { d.Slot };
        block.AddRange(Subtree(d.Slot));
        var before = _rows.Children.OfType<FrameworkElement>().ToDictionary(c => c, Top);
        foreach (var e in block) _rows.Children.Remove(e);
        index = Math.Clamp(index, 0, _rows.Children.Count);
        for (int i = 0; i < block.Count; i++) _rows.Children.Insert(index + i, block[i]);
        d.Slot.SetValue(ParentFolderProperty, parent);
        d.Slot.SetValue(DepthProperty, depth);
        if (d.Slot is Tile { Child: Grid g }) g.ColumnDefinitions[0].Width = new GridLength(depth * Indent + 4);
        _rows.UpdateLayout();
        foreach (var (e, top) in before)
        {
            if (e == d.Slot || e.Visibility != Visibility.Visible) continue;
            double dy = top - Top(e);
            if (Math.Abs(dy) < 0.5 || e.RenderTransform is not TranslateTransform t) continue;
            if (!Motion.Enabled) continue;
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = Motion.Ease });
        }
        if (d.Ghost.Child is StackPanel ghost) ghost.Margin = new Thickness(10 + depth * Indent, 0, 10, 0);
    }

    private static void Highlight(Drag d, SetupNode? into, FrameworkElement? element)
    {
        if (d.IntoElement is Tile old) { old.BorderThickness = new Thickness(0); old.Background = d.IntoBackground; }
        if (d.IntoElement is Control oldControl) oldControl.ClearValue(Control.BorderBrushProperty);
        d.Into = into;
        d.IntoElement = element;
        if (element is Tile tile)
        {
            d.IntoBackground = tile.Background;
            tile.BorderBrush = Ui.Res<Brush>("Accent");
            tile.BorderThickness = new Thickness(1.5);
            tile.Background = Ui.Res<Brush>("AccentSoft");
        }
        if (element is Control control) control.BorderBrush = Ui.Res<Brush>("Accent");
    }

    private void AutoScroll(MouseEventArgs e)
    {
        double y = e.GetPosition(_scroll).Y;
        if (y < 28) _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - 12);
        else if (y > _scroll.ViewportHeight - 28) _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + 12);
    }

    private void Finish()
    {
        var d = _drag!;
        var dropInto = d.Into;
        End();
        if (dropInto is { } into)
        {
            Dropped?.Invoke(d.Nodes, into, null, null);
            return;
        }
        var parent = ParentOf(d.Slot);
        if (parent is null) return;
        var order = new List<SetupNode>();
        foreach (FrameworkElement e in _rows.Children)
        {
            if (e == d.Slot) { order.AddRange(d.Nodes); continue; }
            if (e.Tag is SetupNode n && ParentOf(e) == parent && !d.Nodes.Contains(n)) order.Add(n);
        }
        Dropped?.Invoke(d.Nodes, null, parent, order);
    }

    private void Cancel()
    {
        End();
        Dropped?.Invoke(new List<SetupNode>(), null, null, null);
    }

    private void End()
    {
        var d = _drag!;
        _drag = null;
        _pressed = null;
        _overlay.Children.Remove(d.Ghost);
        if (IsMouseCaptured) ReleaseMouseCapture();
        Highlight(d, null, null);
    }
}

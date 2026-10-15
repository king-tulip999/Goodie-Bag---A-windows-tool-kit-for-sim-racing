using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal sealed record PanelTier(PanelSize Size, double MinWidth, double MinHeight = 0);

internal sealed record PanelDef(string Id, string Label, Func<PanelSize, FrameworkElement> Build)
{
    public IReadOnlyList<PanelTier> Tiers { get; init; } = new[] { new PanelTier(PanelSize.Medium, 0) };

    public double DefaultWidth { get; init; } = 470;

    public double MinWidth { get; init; } = 260;

    public double MaxWidth { get; init; } = 470;

    public Func<double>? MaxWidthFor { get; init; }

    public bool FullWidth { get; init; }

    public Action<double>? Fit { get; init; }

    public int Order { get; init; } = int.MaxValue;

    public Func<UIElement>? VeilExtra { get; init; }

    public Action? VeilClick { get; init; }

    public string? VeilClickTip { get; init; }

    public bool FreeHeight { get; init; }

    public double MinHeight { get; init; }

    public bool Resizable => !FullWidth && (FreeHeight || MaxWidthFor is not null || MaxWidth > MinWidth + 1 || Tiers.Count > 1);
}

internal sealed class PanelGrid : Panel
{
    public double MinColumnWidth { get; init; } = 38;
    public double RowHeight { get; init; } = 12;
    public double Gap { get; init; } = 16;
    public int MinColumns { get; init; } = 2;

    public double PanelSpacing { get; init; }

    public bool Dividers { get; init; }

    public bool LiveEdit { get; init; }

    private bool _centered;

    public bool Centered
    {
        get => _centered;
        set { if (_centered == value) return; _centered = value; InvalidateArrange(); }
    }

    private double _offsetX;

    private sealed class Item
    {
        public required PanelDef Def;
        public required PanelSlot Slot;
        public required GridBox Box;
        public required Grid Host;
        public required Border Veil;
        public FrameworkElement? LiveGrip;
        public bool GripShown;
        public readonly TranslateTransform Shift = new();
        public PanelSize? Tier;
        public Rect Last = Rect.Empty;
    }

    private readonly List<PanelSlot> _slots;
    private readonly Action _save;
    private readonly List<Item> _items = new();
    private readonly List<Item> _added = new();
    private readonly GhostOutline _ghost = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private int _columns = 1;
    private double _columnWidth = 100;
    private double _width;
    private int _usedRows;
    private bool _editing;

    private Item? _pressed, _drag;
    private Point _pressAt, _grab;
    private Rect _dragFrom;
    private List<Item>? _dragOthers;
    private int _dragIndex;
    private (int Col, int Row) _dragCell = (-1, -1);
    private Item? _resize;
    private Point _resizeAt;
    private Size _resizeFrom;
    private (int? Cols, int? Rows) _resizeUndo;

    private double ColStep => _columnWidth + Gap;
    private double RowStep => RowHeight + Gap;

    private static bool _liveResize;

    public static bool LiveResize
    {
        get => _liveResize;
        set
        {
            if (_liveResize == value) return;
            _liveResize = value;
            if (!value) LiveResizeEnded?.Invoke();
        }
    }

    private static event Action? LiveResizeEnded;

    public PanelGrid(List<PanelSlot> slots, Action save)
    {
        _slots = slots;
        _save = save;
        Loaded += (_, _) => { LiveResizeEnded -= InvalidateMeasure; LiveResizeEnded += InvalidateMeasure; };
        Unloaded += (_, _) => LiveResizeEnded -= InvalidateMeasure;
        _ghost.SetResourceReference(GhostOutline.StrokeProperty, "Accent");
        _ghost.SetResourceReference(GhostOutline.FillProperty, "AccentSoft");
        Children.Add(_ghost);
        Background = Brushes.Transparent;

        var reset = Ui.LabeledButton("I.Undo", "Default order", ResetLayout, "GhostButton");
        reset.ToolTip = "Put every panel back in this page's default order (sizes stay as they are)";
        EditTools = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Children = { reset } };
    }

    public StackPanel EditTools { get; }

    public StackPanel TakeEditTools()
    {
        if (EditTools.Parent is Panel parent) parent.Children.Remove(EditTools);
        return EditTools;
    }

    public void ResetLayout()
    {
        CancelAll();
        Commit(_added.OrderBy(i => i.Def.Order).ToList());
    }

    public void Resize(string id, double width)
    {
        if (_items.FirstOrDefault(i => i.Def.Id == id && i.Def.Resizable) is not { } item) return;
        var (min, max) = ColumnRange(item.Def);
        item.Slot.Cols = Math.Clamp(ColumnsFor(width), min, max);
        item.Slot.Rows = null;
        InvalidateMeasure();
    }

    public bool Editing
    {
        get => _editing;
        set
        {
            if (_editing == value) return;
            _editing = value;
            EditTools.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (!value) CancelAll();
            foreach (var item in _items) item.Veil.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public IEnumerable<string> Ids => _items.Select(i => i.Def.Id);

    private List<Item> Ordered()
    {
        int Key(Item i) { int k = _slots.IndexOf(i.Slot); return k < 0 ? int.MaxValue : k; }
        return _items.OrderBy(Key).ToList();
    }

    public bool Contains(string id) => _items.Any(i => i.Def.Id == id);

    public void Clear()
    {
        CancelAll();
        foreach (var item in _items) Children.Remove(item.Host);
        _items.Clear();
        _added.Clear();
    }

    public void Add(PanelDef def)
    {
        var slot = _slots.FirstOrDefault(s => s.Id == def.Id);
        if (slot is null)
        {
            slot = new PanelSlot { Id = def.Id };
            _slots.Add(slot);
        }
        var box = new GridBox { Id = def.Id };
        var host = new Grid();
        var item = new Item { Def = def, Slot = slot, Box = box, Host = host, Veil = null! };
        item.Veil = Veil(item);
        host.RenderTransform = item.Shift;
        item.Veil.Visibility = _editing ? Visibility.Visible : Visibility.Collapsed;
        host.Children.Add(new Border());
        host.Children.Add(item.Veil);
        if (LiveEdit) HookLive(item);
        else HookDrag(item);
        _items.Add(item);
        _added.Add(item);
        _dragOthers?.Add(item);
        Children.Add(host);
    }

    public void Remove(string id)
    {
        var item = _items.FirstOrDefault(i => i.Def.Id == id);
        if (item is null) return;
        if (item == _drag || item == _pressed || item == _resize) CancelAll();
        else if (_dragOthers is { } others && others.Remove(item))
        {
            _dragIndex = Math.Min(_dragIndex, others.Count);
            _dragCell = (-1, -1);
        }
        _items.Remove(item);
        _added.Remove(item);
        Children.Remove(item.Host);
    }

    private void SetContent(Item item, PanelSize tier)
    {
        item.Tier = tier;
        var content = item.Def.Build(tier);
        content.Margin = new Thickness(0, 0, 0, PanelSpacing);
        if (ReferenceEquals(item.Host.Children[0], content)) return;
        if (content.Parent is Panel previous) previous.Children.Remove(content);
        item.Host.Children.RemoveAt(0);
        item.Host.Children.Insert(0, content);
    }

    private double ColumnsWidth(int cols) => cols * _columnWidth + (cols - 1) * Gap;

    private int ColumnsFor(double width, int round = 0)
    {
        double exact = (width + Gap) / ColStep;
        int cols = round < 0 ? (int)Math.Floor(exact + 0.001) : round > 0 ? (int)Math.Ceiling(exact - 0.001) : (int)Math.Round(exact);
        return Math.Clamp(cols, 1, _columns);
    }

    private (int Min, int Max) ColumnRange(PanelDef def)
    {
        if (def.FullWidth) return (_columns, _columns);
        int min = ColumnsFor(def.MinWidth, +1);
        return (min, Math.Max(min, ColumnsFor(def.MaxWidthFor?.Invoke() ?? def.MaxWidth, -1)));
    }

    private int ColumnsOf(Item item)
    {
        var (min, max) = ColumnRange(item.Def);
        return Math.Clamp(item.Slot.Cols ?? ColumnsFor(item.Def.DefaultWidth), min, max);
    }

    private static PanelSize TierFor(PanelDef def, double width, double? height)
    {
        var tier = def.Tiers[0];
        foreach (var t in def.Tiers.Skip(1))
            if (t.MinWidth <= width + 0.5 && (height is not { } h || t.MinHeight <= h + 0.5))
                tier = t;
        return tier.Size;
    }

    private Border Veil(Item item)
    {
        var def = item.Def;
        var veil = new Tile
        {
            CardOutline = true,
            Margin = new Thickness(0, 0, 0, PanelSpacing),
            BorderBrush = Ui.Res<Brush>("Accent"), BorderThickness = new Thickness(1.5), CornerRadius = ThemeManager.CardCorner,
            ToolTip = $"Drag {def.Label} to move it in the order" + (def.Resizable ? "; drag its corner to resize" : "")
                + (def.VeilClick is not null && def.VeilClickTip is { } tip ? "; click to " + tip : ""),
        };
        var surface = ((SolidColorBrush)Ui.Res<Brush>("Surface")).Color;
        veil.Background = new SolidColorBrush(Color.FromArgb(130, surface.R, surface.G, surface.B));

        var tag = new StackPanel { Orientation = Orientation.Horizontal };
        tag.Children.Add(new TextBlock { Text = def.Label, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("AccentText"), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        if (def.VeilExtra?.Invoke() is { } extra) tag.Children.Add(extra);
        var layers = new Grid();
        layers.Children.Add(new Tile
        {
            Background = Ui.Res<Brush>("AccentSoft"), BorderBrush = Ui.Res<Brush>("Accent"), BorderThickness = new Thickness(1), CornerRadius = ThemeManager.Round(12),
            Padding = new Thickness(12, 4, 4, 4), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = tag,
        });
        if (def.Resizable)
            layers.Children.Add(Grip(item));
        veil.Child = layers;
        return veil;
    }

    private FrameworkElement Grip(Item item)
    {
        var grip = new Tile
        {
            Width = 30, Height = 30, CornerRadius = ThemeManager.Round(10, 4, 14, 4), Margin = new Thickness(0, 0, 3, 3),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Ui.Res<Brush>("Accent"), Cursor = Cursors.SizeNWSE, ToolTip = "Drag to resize: bigger shows more",
            Child = new Icon { Data = Ui.Res<Geometry>("I.Resize"), Width = 16, Height = 16, Foreground = Ui.Res<Brush>("OnAccent") },
        };
        grip.MouseLeftButtonDown += (_, e) =>
        {
            CancelAll();
            _resize = item;
            _resizeAt = e.GetPosition(this);
            _resizeFrom = new Size(ColumnsWidth(item.Box.Cols), item.Slot.Rows is { } r ? r * RowStep - Gap : item.Box.Rows * RowStep - Gap);
            _resizeUndo = (item.Slot.Cols, item.Slot.Rows);
            Panel.SetZIndex(item.Host, 100);
            _ghost.Solid = true;
            Panel.SetZIndex(_ghost, 101);
            _ghost.Visibility = Visibility.Visible;
            grip.CaptureMouse();
            e.Handled = true;
        };
        grip.MouseMove += (_, e) =>
        {
            if (_resize != item || e.LeftButton != MouseButtonState.Pressed) return;
            ResizeTo(e.GetPosition(this));
            e.Handled = true;
        };
        grip.MouseLeftButtonUp += (_, e) =>
        {
            if (_resize != item) return;
            _resize = null;
            grip.ReleaseMouseCapture();
            EndResizeVisuals(item);
            Commit(Ordered());
            e.Handled = true;
        };
        grip.LostMouseCapture += (_, _) =>
        {
            if (_resize != item) return;
            (item.Slot.Cols, item.Slot.Rows) = _resizeUndo;
            _resize = null;
            EndResizeVisuals(item);
            Reload();
        };
        return grip;
    }

    private void ResizeTo(Point pointer)
    {
        if (_resize is not { } item) return;
        double w = _resizeFrom.Width + pointer.X - _resizeAt.X, h = Math.Max(RowHeight, _resizeFrom.Height + pointer.Y - _resizeAt.Y);
        var (min, max) = ColumnRange(item.Def);
        int cols = Math.Clamp(ColumnsFor(Math.Max(1, w)), min, max);
        int rows = Math.Max(1, (int)Math.Round((h + Gap) / RowStep));
        if (item.Slot.Cols == cols && item.Slot.Rows == rows) return;
        item.Slot.Cols = cols;
        item.Slot.Rows = rows;
        InvalidateMeasure();
        AutoScroll(pointer);
    }

    private void EndResizeVisuals(Item item)
    {
        Panel.SetZIndex(item.Host, 0);
        _ghost.Visibility = Visibility.Collapsed;
        _ghost.Solid = false;
        Panel.SetZIndex(_ghost, 0);
        InvalidateMeasure();
    }

    private void HookDrag(Item item)
    {
        item.Veil.MouseLeftButtonDown += (_, e) =>
        {
            _pressed = item;
            _pressAt = e.GetPosition(this);
            item.Veil.CaptureMouse();
            e.Handled = true;
        };
        item.Veil.MouseMove += (_, e) =>
        {
            if (_pressed != item || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (_drag is null)
            {
                if (Math.Abs(p.X - _pressAt.X) < 5 && Math.Abs(p.Y - _pressAt.Y) < 5) return;
                StartDrag(item);
            }
            DragTo(p);
        };
        item.Veil.MouseLeftButtonUp += (_, _) =>
        {
            if (_pressed != item) return;
            bool moved = _drag == item;
            var order = DragOrder();
            _pressed = null;
            _drag = null;
            if (item.Veil.IsMouseCaptured) item.Veil.ReleaseMouseCapture();
            if (item.Host.IsMouseCaptured) item.Host.ReleaseMouseCapture();
            EndDragVisuals(item);
            if (moved && order is not null) Commit(order);
            else if (!moved) item.Def.VeilClick?.Invoke();
        };
        item.Veil.LostMouseCapture += (_, _) =>
        {
            if (_pressed != item) return;
            bool moved = _drag == item;
            _pressed = null;
            _drag = null;
            EndDragVisuals(item);
            if (moved) Reload();
        };
    }

    private const double GripZone = 46;

    private void HookLive(Item item)
    {
        var host = item.Host;
        if (item.Def.Resizable)
        {
            var grip = Grip(item);
            grip.Opacity = 0;
            grip.IsHitTestVisible = false;
            grip.Margin = new Thickness(0, 0, -6, -6);
            Panel.SetZIndex(grip, 10);
            item.LiveGrip = grip;
            host.Children.Add(grip);
        }
        host.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_drag is null) _pressed = null;
            if (_resize is not null || IsControl(e.OriginalSource as DependencyObject, host)) return;
            _pressed = item;
            _pressAt = e.GetPosition(this);
        };
        host.PreviewMouseMove += (_, e) =>
        {
            ShowGrip(item, e.GetPosition(host));
            if (_pressed != item || e.LeftButton != MouseButtonState.Pressed || _resize is not null) return;
            var p = e.GetPosition(this);
            if (_drag is null)
            {
                if (Math.Abs(p.X - _pressAt.X) < 5 && Math.Abs(p.Y - _pressAt.Y) < 5) return;
                StartDrag(item);
                host.CaptureMouse();
            }
            DragTo(p);
            e.Handled = true;
        };
        host.MouseLeave += (_, _) =>
        {
            if (_resize != item) ShowGrip(item, null);
            if (_drag is null && _pressed == item) _pressed = null;
        };
        host.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_pressed != item) return;
            bool moved = _drag == item;
            var order = DragOrder();
            _pressed = null;
            _drag = null;
            if (host.IsMouseCaptured) host.ReleaseMouseCapture();
            if (moved)
            {
                EndDragVisuals(item);
                if (order is not null) Commit(order);
                e.Handled = true;
            }
            else item.Def.VeilClick?.Invoke();
        };
        host.LostMouseCapture += (_, _) =>
        {
            if (_drag != item) return;
            _pressed = null;
            _drag = null;
            EndDragVisuals(item);
            Reload();
        };
    }

    private void ShowGrip(Item item, Point? at)
    {
        if (item.LiveGrip is not { } grip) return;
        bool show = _drag is null && (_resize == item || at is { } p && item.Host.ActualWidth - p.X < GripZone && item.Host.ActualHeight - p.Y < GripZone);
        if (show == item.GripShown) return;
        item.GripShown = show;
        grip.IsHitTestVisible = show;
        Motion.FadeTo(grip, show ? 1 : 0, show ? 120 : 180);
    }

    public static readonly DependencyProperty NoDragProperty =
        DependencyProperty.RegisterAttached("NoDrag", typeof(bool), typeof(PanelGrid), new PropertyMetadata(false));

    public static void SetNoDrag(DependencyObject d, bool value) => d.SetValue(NoDragProperty, value);
    public static bool GetNoDrag(DependencyObject d) => (bool)d.GetValue(NoDragProperty);

    internal static bool IsControl(DependencyObject? d, DependencyObject stop)
    {
        for (; d is not null && d != stop; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (GetNoDrag(d)) return true;
            if (d is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase or PasswordBox or ComboBox
                or Slider or System.Windows.Controls.Primitives.ScrollBar or System.Windows.Controls.Primitives.Thumb or System.Windows.Controls.Primitives.Selector
                or System.Windows.Documents.Hyperlink)
                return true;
            if (d is FrameworkElement { Cursor: { } c } fe && c == Cursors.Hand && VisualTreeHelper.GetParent(fe) != stop) return true;
        }
        return false;
    }

    private void StartDrag(Item item)
    {
        _drag = item;
        var order = Ordered();
        _dragIndex = order.IndexOf(item);
        order.Remove(item);
        _dragOthers = order;
        _dragCell = (item.Box.Col, item.Box.Row);
        _dragFrom = item.Last.IsEmpty ? new Rect(item.Box.Col * ColStep, item.Box.Row * RowStep, 0, 0) : item.Last;
        _grab = new Point(_pressAt.X - _dragFrom.X, _pressAt.Y - _dragFrom.Y);
        Panel.SetZIndex(item.Host, 100);
        item.Host.Opacity = 0.92;
        _ghost.Visibility = Visibility.Visible;
    }

    private void DragTo(Point pointer)
    {
        if (_drag is not { } item) return;
        item.Shift.BeginAnimation(TranslateTransform.XProperty, null);
        item.Shift.BeginAnimation(TranslateTransform.YProperty, null);
        item.Shift.X = item.Def.FullWidth ? 0 : pointer.X - _grab.X - _dragFrom.X;
        item.Shift.Y = pointer.Y - _grab.Y - _dragFrom.Y;

        int col = (int)Math.Round((pointer.X - _grab.X - _offsetX) / ColStep);
        int row = (int)Math.Round((pointer.Y - _grab.Y) / RowStep);
        col = Math.Clamp(col, 0, Math.Max(0, _columns - item.Box.Cols));
        row = Math.Max(0, row);
        if ((col, row) != _dragCell && _dragOthers is { } others)
        {
            _dragCell = (col, row);
            _dragIndex = GridLayout.DropIndex(others.Select(i => i.Box).ToList(), item.Box, _columns, col, row, _dragIndex, ColStep / RowStep);
            InvalidateMeasure();
        }
        AutoScroll(pointer);
    }

    private List<Item>? DragOrder() => _drag is { } drag && _dragOthers is { } others
        ? others.Take(_dragIndex).Append(drag).Concat(others.Skip(_dragIndex)).ToList()
        : null;

    private void AutoScroll(Point pointer)
    {
        DependencyObject? d = this;
        while (d is not null and not ScrollViewer) d = VisualTreeHelper.GetParent(d);
        if (d is not ScrollViewer sv) return;
        var inView = TranslatePoint(pointer, sv);
        const double edge = 48, speed = 18;
        if (inView.Y < edge) sv.ScrollToVerticalOffset(sv.VerticalOffset - speed);
        else if (inView.Y > sv.ViewportHeight - edge) sv.ScrollToVerticalOffset(sv.VerticalOffset + speed);
    }

    private void EndDragVisuals(Item item)
    {
        _dragOthers = null;
        Panel.SetZIndex(item.Host, 0);
        item.Host.Opacity = 1;
        if (!_dragFrom.IsEmpty && (item.Shift.X != 0 || item.Shift.Y != 0))
            item.Last = new Rect(_dragFrom.X + item.Shift.X, _dragFrom.Y + item.Shift.Y, _dragFrom.Width, _dragFrom.Height);
        item.Shift.X = item.Shift.Y = 0;
        _ghost.Visibility = Visibility.Collapsed;
        InvalidateMeasure();
    }

    private void CancelAll()
    {
        var item = _drag ?? _pressed;
        bool moved = _drag is not null;
        _drag = null;
        _pressed = null;
        if (item is not null)
        {
            if (item.Veil.IsMouseCaptured) item.Veil.ReleaseMouseCapture();
            if (item.Host.IsMouseCaptured) item.Host.ReleaseMouseCapture();
            EndDragVisuals(item);
        }
        if (_resize is { } resized)
        {
            (resized.Slot.Cols, resized.Slot.Rows) = _resizeUndo;
            _resize = null;
            if (Mouse.Captured is UIElement grip && resized.Host.IsAncestorOf(grip)) grip.ReleaseMouseCapture();
            EndResizeVisuals(resized);
            moved = true;
        }
        if (moved) Reload();
    }

    private void Reload() => InvalidateMeasure();

    private void Commit(List<Item> order)
    {
        var mine = order.Select(i => i.Slot).ToList();
        var places = _slots.Select((slot, index) => (slot, index)).Where(x => mine.Contains(x.slot)).Select(x => x.index).ToList();
        for (int k = 0; k < places.Count && k < mine.Count; k++) _slots[places[k]] = mine[k];
        foreach (var item in order)
            if (!item.Def.Resizable) item.Slot.Cols = item.Slot.Rows = null;
        _save();
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 1000 : available.Width;
        _columns = Math.Max(MinColumns, (int)((width + Gap) / (MinColumnWidth + Gap)));
        _columnWidth = Math.Min(MinColumnWidth, Math.Max(1, (width - (_columns - 1) * Gap) / _columns));
        _width = width;

        foreach (var item in _items)
        {
            int cols = ColumnsOf(item);
            double w = item.Def.FullWidth ? width : ColumnsWidth(cols);
            var tier = TierFor(item.Def, w, item.Slot.Rows is { } r ? r * RowStep - Gap : null);
            if (item.Tier != tier && (item.Tier is null || !LiveResize)) SetContent(item, tier);
            item.Def.Fit?.Invoke(w);
            double? height = item.Def.FreeHeight && item.Slot.Rows is { } userRows ? Math.Max(userRows * RowStep - Gap, item.Def.MinHeight) : null;
            item.Host.Measure(new Size(w, height ?? double.PositiveInfinity));
            item.Box.Cols = cols;
            item.Box.Rows = Math.Max(1, (int)Math.Ceiling(((height ?? item.Host.DesiredSize.Height) + Gap) / RowStep - 0.001));
        }
        GridLayout.Pack((DragOrder() ?? Ordered()).Select(i => i.Box).ToList(), _columns);
        _usedRows = GridLayout.Bottom(_items.Select(i => i.Box));
        _ghost.Measure(new Size(width, double.PositiveInfinity));

        int rows = _usedRows + (_editing ? (int)Math.Ceiling(220 / RowStep) : 0);
        return new Size(width, Math.Max(0, rows * RowStep - Gap));
    }

    protected override Size ArrangeOverride(Size final)
    {
        double used = _items.Count == 0 ? 0 : _items.Max(i => i.Def.FullWidth ? _width : (i.Box.Col + i.Box.Cols) * ColStep - Gap);
        _offsetX = _centered ? Math.Max(0, (_width - used) / 2) : 0;
        foreach (var item in _items)
        {
            var b = item.Box;
            var rect = new Rect(b.Col * ColStep + (item.Def.FullWidth ? 0 : _offsetX), b.Row * RowStep, item.Def.FullWidth ? _width : ColumnsWidth(b.Cols), b.Rows * RowStep - Gap);
            if (item == _drag)
            {
                item.Host.Arrange(new Rect(_dragFrom.Location, rect.Size));
                _ghost.Arrange(rect);
                continue;
            }
            item.Host.Arrange(rect);
            if (item == _resize) _ghost.Arrange(rect);
            if (!item.Last.IsEmpty && item.Last.Location != rect.Location && Motion.Enabled && (_editing || LiveEdit))
            {
                var duration = TimeSpan.FromMilliseconds(180);
                item.Shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(item.Last.X - rect.X + item.Shift.X, 0, duration) { EasingFunction = Motion.Ease });
                item.Shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(item.Last.Y - rect.Y + item.Shift.Y, 0, duration) { EasingFunction = Motion.Ease });
            }
            item.Last = rect;
        }
        if (_drag is null && _resize is null) _ghost.Arrange(new Rect());
        if (Dividers) InvalidateVisual();
        return final;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!_editing) DrawDividers(dc);
        if (!_editing || ActualHeight <= 0) return;
        var line = Ui.Res<Brush>("Line");
        if (_columnPen is null || _columnPen.Brush != line)
        {
            _columnPen = new Pen(line, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
            _columnPen.Freeze();
        }
        for (int c = 0; c < _columns; c++)
            dc.DrawGeometry(null, _columnPen, Tile.CardShape(new Rect(c * ColStep, 0, _columnWidth, ActualHeight), 10));
    }

    private Pen? _columnPen;

    private void DrawDividers(DrawingContext dc)
    {
        if (!Dividers) return;
        var stacked = _items.Where(i => !i.Last.IsEmpty).OrderBy(i => i.Last.Top).ToList();
        for (int k = 1; k < stacked.Count; k++)
        {
            Rect a = stacked[k - 1].Last, b = stacked[k].Last;
            if (b.Top - a.Bottom > Gap + 1 || b.Top < a.Bottom) continue;
            var above = stacked[k - 1].Host.Children.OfType<FrameworkElement>().FirstOrDefault();
            var below = stacked[k].Host.Children.OfType<FrameworkElement>().FirstOrDefault();
            double top = a.Bottom - (above?.Margin.Bottom ?? 0), bottom = b.Top + (below?.Margin.Top ?? 0);
            Views.Dividers.Draw(dc, Math.Max(a.Left, b.Left) + 24, Math.Min(a.Right, b.Right) - 24, Math.Round((top + bottom) / 2));
        }
    }

    private sealed class GhostOutline : FrameworkElement
    {
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke), typeof(Brush), typeof(GhostOutline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(GhostOutline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush? Stroke
        {
            get => (Brush?)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public Brush? Fill
        {
            get => (Brush?)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        private Pen? _pen, _solidPen;
        private Size _penFor;
        private bool _solid;

        public bool Solid
        {
            get => _solid;
            set { if (_solid == value) return; _solid = value; InvalidateVisual(); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth <= 2 || ActualHeight <= 2) return;
            if (_solid)
            {
                if (_solidPen is null || _solidPen.Brush != Stroke)
                {
                    _solidPen = new Pen(Stroke, 1.5) { LineJoin = PenLineJoin.Miter };
                    _solidPen.Freeze();
                }
                dc.DrawGeometry(null, _solidPen, Tile.CardShape(new Rect(0.75, 0.75, ActualWidth - 1.5, ActualHeight - 1.5), 16));
                return;
            }
            var shape = Tile.CardShape(new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), 16);
            var size = new Size(ActualWidth, ActualHeight);
            if (_pen is null || _pen.Brush != Stroke || _penFor != size)
            {
                const double period = 7;
                double length = Perimeter(shape), unit = 2 * period;
                double scale = length > unit ? length / (Math.Max(1, Math.Round(length / unit)) * unit) : 1;
                _pen = new Pen(Stroke, 2) { DashStyle = new DashStyle(new[] { 4 * scale, 3 * scale }, 5.5 * scale), DashCap = PenLineCap.Flat };
                _pen.Freeze();
                _penFor = size;
            }
            dc.DrawGeometry(Fill, _pen, shape);
        }

        private static double Perimeter(Geometry shape)
        {
            double total = 0;
            foreach (var figure in shape.GetFlattenedPathGeometry().Figures)
            {
                var at = figure.StartPoint;
                foreach (var segment in figure.Segments)
                    if (segment is PolyLineSegment poly)
                        foreach (var p in poly.Points) { total += (p - at).Length; at = p; }
                    else if (segment is LineSegment line) { total += (line.Point - at).Length; at = line.Point; }
                if (figure.IsClosed) total += (figure.StartPoint - at).Length;
            }
            return total;
        }
    }
}

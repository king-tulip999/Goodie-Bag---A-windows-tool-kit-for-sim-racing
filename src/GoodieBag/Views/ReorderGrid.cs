using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GoodieBag.Controls;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal sealed class ReorderGrid : Panel
{
    private const double Threshold = 6;

    public double MinCellWidth { get; init; } = 160;
    public double Gap { get; init; } = 8;
    public int MaxColumns { get; init; } = int.MaxValue;

    public bool Swap { get; init; }

    public bool Flow { get; init; }

    public bool CenterRows { get; init; }

    private double _gridWidth;

    private readonly Dictionary<UIElement, double> _widths = new();
    private Rect[] _rects = Array.Empty<Rect>();

    public event Action<int, int>? Moved;

    private int _columns = 1;
    private double _cellWidth, _cellHeight;

    private UIElement? _pressed, _drag;
    private Point _pressAt, _grab;
    private int _from, _to;

    public ReorderGrid()
    {
        Background = Brushes.Transparent;
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPress), true);
        PreviewMouseMove += OnMove;
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_drag is not null) { Settle(); e.Handled = true; }
            _pressed = null;
        };
        LostMouseCapture += (_, e) => { if (_drag is not null && ReferenceEquals(e.OriginalSource, this)) Cancel(); };
        MouseLeave += (_, _) => { if (_drag is null) _pressed = null; };
    }

    protected override void OnVisualChildrenChanged(DependencyObject added, DependencyObject removed)
    {
        base.OnVisualChildrenChanged(added, removed);
        if (added is UIElement element) element.RenderTransform = new TranslateTransform();
    }

    protected override Size MeasureOverride(Size available)
    {
        int n = InternalChildren.Count;
        if (Flow)
        {
            _width = double.IsInfinity(available.Width) ? double.MaxValue : available.Width;
            _widths.Clear();
            _cellHeight = 0;
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _widths[child] = Math.Min(Math.Max(MinCellWidth, Math.Ceiling(child.DesiredSize.Width)), _width);
                _cellHeight = Math.Max(_cellHeight, child.DesiredSize.Height);
            }
            _rects = Layout(InternalChildren.Cast<UIElement>().ToList());
            double used = _rects.Length == 0 ? 0 : _rects.Max(r => r.Right);
            return new Size(double.IsInfinity(available.Width) ? used : available.Width, _rects.Length == 0 ? 0 : _rects.Max(r => r.Bottom));
        }
        double width = double.IsInfinity(available.Width) ? Math.Max(1, n) * (MinCellWidth + Gap) - Gap : available.Width;
        _columns = Math.Clamp((int)((width + Gap) / (MinCellWidth + Gap)), 1, Math.Max(1, MaxColumns));
        _cellWidth = Math.Max(0, Math.Floor((width - Gap * (_columns - 1)) / _columns));
        _cellHeight = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(_cellWidth, double.PositiveInfinity));
            _cellHeight = Math.Max(_cellHeight, child.DesiredSize.Height);
        }
        _gridWidth = width;
        int rows = (n + _columns - 1) / _columns;
        return new Size(width, rows == 0 ? 0 : rows * _cellHeight + (rows - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Flow)
        {
            _width = final.Width;
            _rects = Layout(InternalChildren.Cast<UIElement>().ToList());
            for (int i = 0; i < InternalChildren.Count; i++) InternalChildren[i].Arrange(_rects[i]);
            return final;
        }
        _gridWidth = final.Width;
        for (int i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(new Rect(Slot(i), new Size(_cellWidth, _cellHeight)));
        return final;
    }

    private double _width;

    private Rect[] Layout(IList<UIElement> order)
    {
        var rects = new Rect[order.Count];
        double x = 0, y = 0;
        for (int i = 0; i < order.Count; i++)
        {
            double w = _widths.TryGetValue(order[i], out var known) ? known : MinCellWidth;
            if (x > 0 && x + w > _width + 0.5) { x = 0; y += _cellHeight + Gap; }
            rects[i] = new Rect(x, y, w, _cellHeight);
            x += w + Gap;
        }
        return rects;
    }

    private Point Slot(int index) => Flow
        ? (index < _rects.Length ? _rects[index].Location : new Point())
        : new(RowStart(index / _columns) + index % _columns * (_cellWidth + Gap), index / _columns * (_cellHeight + Gap));

    private double RowStart(int row)
    {
        if (!CenterRows) return 0;
        int inRow = Math.Clamp(InternalChildren.Count - row * _columns, 1, _columns);
        return Math.Max(0, Math.Floor((_gridWidth - inRow * _cellWidth - (inRow - 1) * Gap) / 2));
    }

    private Size CellSize(int index) => Flow ? (index < _rects.Length ? _rects[index].Size : new Size()) : new Size(_cellWidth, _cellHeight);

    private int IndexAt(Point p)
    {
        if (Flow)
        {
            int best = 0;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < _rects.Length; i++)
            {
                var c = new Point(_rects[i].X + _rects[i].Width / 2, _rects[i].Y + _rects[i].Height / 2);
                double d = Math.Abs(c.Y - p.Y) * 4 + Math.Abs(c.X - p.X);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }
        int row = Math.Max(0, (int)(p.Y / (_cellHeight + Gap)));
        int col = Math.Clamp((int)((p.X - RowStart(row)) / (_cellWidth + Gap)), 0, _columns - 1);
        return Math.Clamp(row * _columns + col, 0, InternalChildren.Count - 1);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_drag is null) return;
        var pen = new Pen(Ui.Res<Brush>("LineStrong"), 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
        pen.Freeze();
        for (int i = 0; i < InternalChildren.Count; i++)
            dc.DrawGeometry(null, pen, Tile.Shape(new Rect(Slot(i), CellSize(i)), ThemeManager.Round(8)));
    }

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressed = CellOf(e.OriginalSource as DependencyObject);
        _pressAt = e.GetPosition(this);
    }

    private UIElement? CellOf(DependencyObject? d)
    {
        while (d is not null)
        {
            var parent = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            if (ReferenceEquals(parent, this)) return d as UIElement;
            d = parent;
        }
        return null;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_drag is null)
        {
            if (_pressed is null || e.LeftButton != MouseButtonState.Pressed || InternalChildren.Count < 2) return;
            if (Math.Abs(p.X - _pressAt.X) < Threshold && Math.Abs(p.Y - _pressAt.Y) < Threshold) return;
            if (InternalChildren.IndexOf(_pressed) < 0) { _pressed = null; return; }
            _drag = _pressed;
            _from = _to = InternalChildren.IndexOf(_drag);
            var at = Slot(_from);
            _grab = new Point(_pressAt.X - at.X, _pressAt.Y - at.Y);
            SetZIndex(_drag, 1);
            _drag.Opacity = 0.9;
            CaptureMouse();
            InvalidateVisual();
            if (_drag is null) return;
        }
        if (!ReferenceEquals(VisualTreeHelper.GetParent(_drag), this))
        {
            Cancel();
            return;
        }
        var slot = Slot(_from);
        var move = Shift(_drag);
        move.BeginAnimation(TranslateTransform.XProperty, null);
        move.BeginAnimation(TranslateTransform.YProperty, null);
        move.X = p.X - _grab.X - slot.X;
        move.Y = p.Y - _grab.Y - slot.Y;

        var size = CellSize(_from);
        int to = IndexAt(new Point(p.X - _grab.X + size.Width / 2, p.Y - _grab.Y + size.Height / 2));
        if (to == _to) return;
        _to = to;
        if (Flow)
        {
            var order = InternalChildren.Cast<UIElement>().ToList();
            var dragged = order[_from];
            order.RemoveAt(_from);
            order.Insert(_to, dragged);
            var preview = Layout(order);
            for (int k = 0; k < order.Count; k++)
            {
                if (ReferenceEquals(order[k], dragged)) continue;
                int i = InternalChildren.IndexOf(order[k]);
                Slide(order[k], preview[k].Location - Slot(i));
            }
            return;
        }
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            if (i == _from) continue;
            int shown = i;
            if (Swap) shown = i == _to ? _from : i;
            else if (_from < _to && i > _from && i <= _to) shown = i - 1;
            else if (_from > _to && i >= _to && i < _from) shown = i + 1;
            Slide(InternalChildren[i], Slot(shown) - Slot(i));
        }
    }

    private static TranslateTransform Shift(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } t) return t;
        var fresh = new TranslateTransform();
        element.RenderTransform = fresh;
        return fresh;
    }

    private static void Slide(UIElement element, Vector by)
    {
        var move = Shift(element);
        if (!Motion.Enabled)
        {
            move.BeginAnimation(TranslateTransform.XProperty, null);
            move.BeginAnimation(TranslateTransform.YProperty, null);
            move.X = by.X;
            move.Y = by.Y;
            return;
        }
        var d = TimeSpan.FromMilliseconds(160);
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(by.X, d) { EasingFunction = Motion.Ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(by.Y, d) { EasingFunction = Motion.Ease });
    }

    private void Settle()
    {
        var drag = _drag!;
        var move = Shift(drag);
        var shownAt = Slot(_from) + new Vector(move.X, move.Y);
        int from = _from, to = _to;
        EndDrag();
        int count = InternalChildren.Count;
        if (from == to || from >= count || to >= count || !ReferenceEquals(InternalChildren[from], drag))
        {
            Slide(drag, new Vector());
            return;
        }
        if (Swap)
        {
            int lo = Math.Min(from, to), hi = Math.Max(from, to);
            UIElement a = InternalChildren[lo], b = InternalChildren[hi];
            InternalChildren.RemoveAt(hi);
            InternalChildren.RemoveAt(lo);
            InternalChildren.Insert(lo, b);
            InternalChildren.Insert(hi, a);
        }
        else
        {
            InternalChildren.RemoveAt(from);
            InternalChildren.Insert(to, drag);
        }
        var settle = Shift(drag);
        if (Flow) _rects = Layout(InternalChildren.Cast<UIElement>().ToList());
        var target = Slot(to);
        settle.X = shownAt.X - target.X;
        settle.Y = shownAt.Y - target.Y;
        Slide(drag, new Vector());
        Moved?.Invoke(from, to);
    }

    private void Cancel()
    {
        var drag = _drag!;
        EndDrag();
        Slide(drag, new Vector());
    }

    private void EndDrag()
    {
        var drag = _drag!;
        _drag = null;
        _pressed = null;
        drag.Opacity = 1;
        SetZIndex(drag, 0);
        foreach (UIElement child in InternalChildren)
        {
            if (child == drag) continue;
            var t = Shift(child);
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.BeginAnimation(TranslateTransform.YProperty, null);
            t.X = t.Y = 0;
        }
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }
}

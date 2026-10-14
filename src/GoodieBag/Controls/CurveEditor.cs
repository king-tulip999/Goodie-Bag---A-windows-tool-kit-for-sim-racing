using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Config;

using GoodieBag.Theme;

namespace GoodieBag.Controls;

public sealed class CurveEditor : FrameworkElement
{
    private const double MinTemp = 20, MaxTemp = 105;
    private const double PointRadius = 6.5;

    public static readonly DependencyProperty LiveTempProperty = DependencyProperty.Register(nameof(LiveTemp), typeof(double), typeof(CurveEditor),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LiveOutputProperty = DependencyProperty.Register(nameof(LiveOutput), typeof(double), typeof(CurveEditor),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    private static Typeface Face => ThemeManager.UiFace();
    private static Typeface Bold => ThemeManager.UiFace(bold: true);
    private static Brush _accent => ThemeManager.Brush("Accent", Brushes.SlateBlue);
    private static Brush _live => ThemeManager.Brush("ApricotDeep", Brushes.DarkOrange);
    private static Brush _grid => ThemeManager.Brush("GridLine", Brushes.LightGray);
    private static Brush _axis => ThemeManager.Brush("LineStrong", Brushes.Gray);
    private static Brush _label => ThemeManager.Brush("TextFaint", Brushes.Gray);

    private List<CurvePoint> _points = new();
    private int _drag = -1, _hover = -1;

    public double LiveTemp { get => (double)GetValue(LiveTempProperty); set => SetValue(LiveTempProperty, value); }
    public double LiveOutput { get => (double)GetValue(LiveOutputProperty); set => SetValue(LiveOutputProperty, value); }

    private DrawingGroup? _graph, _handles;
    private bool _dirty = true;
    private Brush? _graphTheme;
    private double _graphDpi;
    private Pen? _livePen, _liveRing;
    private FormattedText? _liveText;
    private (string Text, Brush Brush, double Dpi) _liveTextKey;

    private void Changed()
    {
        _dirty = true;
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _dirty = true;
    }

    public event EventHandler? PointsChanged;

    public CurveEditor()
    {
        Focusable = false;
        Cursor = Cursors.Arrow;
        MinHeight = 220;
    }

    public bool ReadOnly { get; init; }

    public void Refresh() => Changed();

    public void SetPoints(List<CurvePoint> points)
    {
        _points = points;
        _drag = _hover = -1;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Changed();
    }

    private Rect Plot => ReadOnly
        ? new(34, 6, Math.Max(10, ActualWidth - 34 - 10), Math.Max(10, ActualHeight - 6 - 20))
        : new(46, 12, Math.Max(10, ActualWidth - 46 - 16), Math.Max(10, ActualHeight - 12 - 30));

    internal IReadOnlyList<CurvePoint> Points => _points;

    internal Point ToScreen(double temp, double pct)
    {
        var p = Plot;
        return new Point(p.Left + (temp - MinTemp) / (MaxTemp - MinTemp) * p.Width, p.Bottom - pct / 100 * p.Height);
    }

    private (float Temp, float Pct) FromScreen(Point pt)
    {
        var p = Plot;
        double t = MinTemp + (pt.X - p.Left) / p.Width * (MaxTemp - MinTemp);
        double v = (p.Bottom - pt.Y) / p.Height * 100;
        return ((float)Math.Round(Math.Clamp(t, MinTemp, MaxTemp)), (float)Math.Round(Math.Clamp(v, 0, 100)));
    }

    private int HitTest(Point pt)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            var s = ToScreen(_points[i].Temp, _points[i].Percent);
            if (Math.Abs(s.X - pt.X) <= PointRadius + 4 && Math.Abs(s.Y - pt.Y) <= PointRadius + 4)
                return i;
        }
        return -1;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (ReadOnly) return;
        var pos = e.GetPosition(this);
        if (e.ClickCount == 2)
        {
            if (HitTest(pos) >= 0 || !Plot.Contains(pos))
                return;
            var (t, v) = FromScreen(pos);
            if (_points.Any(p => Math.Abs(p.Temp - t) < 1))
                return;
            _points.Add(new CurvePoint { Temp = t, Percent = v });
            Sort();
            Changed();
            PointsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        Sort();
        _drag = HitTest(pos);
        if (_drag >= 0)
        {
            CaptureMouse();
            e.Handled = true;
            Changed();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (ReadOnly) return;
        var pos = e.GetPosition(this);
        if (_drag >= 0)
        {
            var (t, v) = FromScreen(pos);
            float lo = _drag > 0 ? _points[_drag - 1].Temp + 1 : (float)MinTemp;
            float hi = _drag < _points.Count - 1 ? _points[_drag + 1].Temp - 1 : (float)MaxTemp;
            _points[_drag].Temp = Math.Clamp(t, lo, Math.Max(lo, hi));
            _points[_drag].Percent = v;
            Changed();
            return;
        }
        int hover = HitTest(pos);
        if (hover != _hover)
        {
            _hover = hover;
            Cursor = hover >= 0 ? Cursors.Hand : Cursors.Arrow;
            Changed();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag >= 0)
        {
            _drag = -1;
            ReleaseMouseCapture();
            Changed();
            PointsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_drag < 0)
            return;
        _drag = -1;
        Changed();
        PointsChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (ReadOnly || _drag >= 0) return;
        int hit = HitTest(e.GetPosition(this));
        if (hit >= 0 && _points.Count > 2)
        {
            _points.RemoveAt(hit);
            _hover = -1;
            Changed();
            PointsChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover >= 0 && _drag < 0)
        {
            _hover = -1;
            Changed();
        }
    }

    private void Sort() => _points.Sort((a, b) => a.Temp.CompareTo(b.Temp));

    protected override void OnRender(DrawingContext dc)
    {
        var plot = Plot;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var accent = _accent;
        if (_dirty || _graph is null || _handles is null || accent != _graphTheme || dpi != _graphDpi)
        {
            _graph = Draw(g => DrawGraph(g, plot, dpi));
            _handles = Draw(g => DrawHandles(g, plot, dpi));
            _graphTheme = accent;
            _graphDpi = dpi;
            _dirty = false;
        }
        dc.DrawDrawing(_graph);
        if (_points.Count > 0)
            DrawLive(dc, plot, dpi);
        if (!ReadOnly) dc.DrawDrawing(_handles);
    }

    private static DrawingGroup Draw(Action<DrawingContext> draw)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
            draw(dc);
        if (group.CanFreeze)
            group.Freeze();
        return group;
    }

    private static FormattedText Text(string s, double size, Brush brush, double dpi, Typeface? face = null) =>
        new(ThemeManager.Case(s), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face ?? Face, size, brush, dpi);

    private void DrawGraph(DrawingContext dc, Rect plot, double dpi)
    {
        var label = _label;
        var gridPen = new Pen(_grid, 1);
        var axisPen = new Pen(_axis, 1);
        gridPen.Freeze();
        axisPen.Freeze();
        for (int pct = 0; pct <= 100; pct += ReadOnly ? 50 : 20)
        {
            double y = ToScreen(MinTemp, pct).Y;
            dc.DrawLine(pct == 0 ? axisPen : gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var t = Text($"{pct}%", 10.5, label, dpi);
            dc.DrawText(t, new Point(plot.Left - t.Width - 8, y - t.Height / 2));
        }
        for (int t = 20; t <= 100; t += ReadOnly ? 20 : 10)
        {
            double x = ToScreen(t, 0).X;
            dc.DrawLine(t == 20 ? axisPen : gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var text = Text($"{t}°", 10.5, label, dpi);
            dc.DrawText(text, new Point(x - text.Width / 2, plot.Bottom + 6));
        }

        if (_points.Count == 0)
            return;

        var sorted = _points.OrderBy(p => p.Temp).ToList();
        var pts = new List<Point> { ToScreen(MinTemp, sorted[0].Percent) };
        pts.AddRange(sorted.Select(p => ToScreen(p.Temp, p.Percent)));
        pts.Add(ToScreen(MaxTemp, sorted[^1].Percent));

        var area = new StreamGeometry();
        using (var c = area.Open())
        {
            c.BeginFigure(new Point(pts[0].X, plot.Bottom), true, true);
            foreach (var p in pts) c.LineTo(p, false, true);
            c.LineTo(new Point(pts[^1].X, plot.Bottom), false, false);
        }
        area.Freeze();
        var ac = ThemeManager.ColorOf("Accent", Colors.DarkOrange);
        var fill = new SolidColorBrush(Color.FromArgb(26, ac.R, ac.G, ac.B));
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);

        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Count; i++) c.LineTo(pts[i], true, true);
        }
        line.Freeze();
        var pen = new Pen(_accent, 2.4) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }

    private void DrawLive(DrawingContext dc, Rect plot, double dpi)
    {
        if (double.IsNaN(LiveTemp))
            return;
        var live = _live;
        var surface = ThemeManager.Brush("Surface", Brushes.White);
        if (_livePen is null || _livePen.Brush != live)
        {
            _livePen = new Pen(live, 1.5) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            _livePen.Freeze();
        }
        if (_liveRing is null || _liveRing.Brush != surface)
        {
            _liveRing = new Pen(surface, 2);
            _liveRing.Freeze();
        }
        double lt = Math.Clamp(LiveTemp, MinTemp, MaxTemp);
        double x = ToScreen(lt, 0).X;
        dc.DrawLine(_livePen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        string caption = double.IsNaN(LiveOutput) ? $"{LiveTemp:0.0}°" : $"{LiveTemp:0.0}° → {LiveOutput:0}%";
        if (!double.IsNaN(LiveOutput))
        {
            var dot = ToScreen(lt, LiveOutput);
            dc.DrawEllipse(live, _liveRing, dot, 5, 5);
        }
        if (_liveText is null || _liveTextKey != (caption, live, dpi))
        {
            _liveText = Text(caption, 11, live, dpi, Bold);
            _liveTextKey = (caption, live, dpi);
        }
        var t = _liveText;
        double tx = x + 6 + t.Width > plot.Right ? x - 6 - t.Width : x + 6;
        dc.DrawText(t, new Point(tx, plot.Top + 2));
    }

    private void DrawHandles(DrawingContext dc, Rect plot, double dpi)
    {
        var accent = _accent;
        var surface = ThemeManager.Brush("Surface", Brushes.White);
        var ring = new Pen(accent, 2.2);
        ring.Freeze();
        for (int i = 0; i < _points.Count; i++)
        {
            var s = ToScreen(_points[i].Temp, _points[i].Percent);
            bool active = i == _hover || i == _drag;
            double r = active ? PointRadius + 1.5 : PointRadius;
            dc.DrawEllipse(active ? accent : surface, ring, s, r, r);
            if (active)
            {
                string label = $"{_points[i].Temp:0}° → {_points[i].Percent:0}%";
                var t = Text(label, 11.5, ThemeManager.Brush("TooltipText", Brushes.White), dpi, Bold);
                double bx = Math.Max(plot.Left, Math.Min(s.X - t.Width / 2 - 8, plot.Right - t.Width - 16));
                double by = s.Y - r - t.Height - 14 < plot.Top ? s.Y + r + 8 : s.Y - r - t.Height - 14;
                dc.DrawGeometry(ThemeManager.Brush("TooltipBg", Brushes.DimGray), null, Tile.Shape(new Rect(bx, by, t.Width + 16, t.Height + 6), ThemeManager.Round(6)));
                dc.DrawText(t, new Point(bx + 8, by + 3));
            }
        }
    }
}

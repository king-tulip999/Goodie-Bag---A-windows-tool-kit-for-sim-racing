using System.Windows;
using System.Windows.Media;

using GoodieBag.Theme;

namespace GoodieBag.Controls;

public sealed class Sparkline : FrameworkElement
{
    private readonly List<double> _values = new();
    private Pen? _pen;
    private Brush? _fill;

    public int Capacity { get; set; } = 60;
    public Brush Stroke { get; set; } = ThemeManager.Brush("Accent", Brushes.SteelBlue);

    public double? Min { get; set; }
    public double? Max { get; set; }

    public void Push(double? value)
    {
        if (value is null || double.IsNaN(value.Value))
            return;
        _values.Add(value.Value);
        while (_values.Count > Capacity)
            _values.RemoveAt(0);
        InvalidateVisual();
    }

    public void SetValues(IEnumerable<double?> values)
    {
        _values.Clear();
        foreach (var v in values.Where(v => v is not null).TakeLast(Capacity))
            _values.Add(v!.Value);
        InvalidateVisual();
    }

    public void Clear()
    {
        _values.Clear();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (_values.Count < 2 || w <= 0 || h <= 0)
            return;

        double low = double.MaxValue, high = double.MinValue;
        foreach (double v in _values)
        {
            if (v < low) low = v;
            if (v > high) high = v;
        }
        double min = Min ?? low, max = Max ?? high;
        if (Min is null && Max is null)
        {
            double pad = Math.Max(1, (max - min) * 0.25);
            min -= pad;
            max += pad;
        }
        if (max - min < 0.001)
            max = min + 1;

        double step = w / (Capacity - 1);
        double x0 = w - step * (_values.Count - 1);
        Point P(int i) => new(x0 + i * step, h - (Math.Clamp(_values[i], min, max) - min) / (max - min) * (h - 3) - 1.5);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(P(0), false, false);
            for (int i = 1; i < _values.Count; i++)
                ctx.LineTo(P(i), true, true);
        }
        line.Freeze();

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(x0, h), true, true);
            for (int i = 0; i < _values.Count; i++)
                ctx.LineTo(P(i), true, true);
            ctx.LineTo(new Point(w, h), true, true);
        }
        area.Freeze();

        if (_pen is null || _fill is null || _pen.Brush != Stroke)
        {
            var c = (Stroke as SolidColorBrush)?.Color ?? Colors.Gray;
            _fill = new SolidColorBrush(Color.FromArgb(34, c.R, c.G, c.B));
            _fill.Freeze();
            _pen = new Pen(Stroke, 1.8) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _pen.Freeze();
        }
        var pen = _pen;
        dc.DrawGeometry(_fill, null, area);
        dc.DrawGeometry(null, pen, line);

        var last = P(_values.Count - 1);
        dc.DrawEllipse(ThemeManager.Brush("Surface", Brushes.White), pen, last, 2.6, 2.6);
    }
}

public sealed class Meter : FrameworkElement
{
    public const double Pad = 3;
    private const double Edge = 1.5;

    static Meter()
    {
        HeightProperty.OverrideMetadata(typeof(Meter), new FrameworkPropertyMetadata(double.NaN, null,
            (_, v) => v is double h && !double.IsNaN(h) && !double.IsInfinity(h) ? h + 2 * Pad : v));
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Meter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush Fill { get; set; } = ThemeManager.Brush("Accent", Brushes.SteelBlue);

    public bool Tiered { get; set; }

    public int Steps { get; set; }

    public bool StepsUseFill { get; set; }

    public Meter()
    {
        Height = 6;
    }

    private (double W, double H, double Cut) _trackKey;
    private Geometry? _outer, _ring, _inner;
    private (double End, double W, double H, double Cut) _fillKey;
    private Geometry? _fill;
    private Brush? _penBrush;
    private Pen? _pen;

    private static Brush FillFor(Brush edge)
    {
        var fill = ThemeManager.Brush("MeterFill", Brushes.SteelBlue);
        return fill is SolidColorBrush f && edge is SolidColorBrush e && f.Color == e.Color ? ThemeManager.Brush("Accent", Brushes.SteelBlue) : fill;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 2 * Pad || h <= 2 * Pad) return;
        var track = ThemeManager.Brush("MeterTrack", Brushes.LightGray);
        double value = Math.Clamp(Value, 0, 1);
        Brush edge = Steps > 0
            ? StepsUseFill ? Fill : ThemeManager.StepBrush(Math.Max(1, Math.Ceiling(value * Steps)) / Steps)
            : Tiered ? ThemeManager.RampBrush(value) : Fill;
        double cut = Tile.ControlCut(w, h, ThemeManager.Current.ControlChamfer);
        double r = Math.Min(h / 2, ThemeManager.Round(3).TopLeft), ri = Math.Max(0, Math.Min((h - 2 * Pad) / 2, r - Pad / 2));
        var inner = new Rect(Pad, Pad, w - 2 * Pad, h - 2 * Pad);

        if (cut > 0)
        {
            if (_outer is null || _trackKey != (w, h, cut))
            {
                var outer = new Rect(0, 0, w, h);
                _trackKey = (w, h, cut);
                _outer = Tile.Chamfered(outer, cut);
                _ring = Tile.ChamferedRing(outer, cut, Edge);
                _inner = Tile.Chamfered(outer, cut, Pad);
            }
            dc.DrawGeometry(track, null, _outer!);
            dc.DrawGeometry(edge, null, _ring!);
        }
        else
        {
            if (_pen is null || !ReferenceEquals(_penBrush, edge))
            {
                _penBrush = edge;
                _pen = new Pen(edge, Edge);
                _pen.Freeze();
            }
            dc.DrawRoundedRectangle(track, _pen, new Rect(Edge / 2, Edge / 2, w - Edge, h - Edge), r, r);
        }

        if (Steps > 0)
        {
            double gap = Math.Clamp(inner.Width / Steps * 0.25, 1, 4);
            double seg = (inner.Width - gap * (Steps - 1)) / Steps, done = value * Steps;
            if (cut > 0) dc.PushClip(_inner!);
            for (int i = 0; i < Steps; i++)
            {
                double part = Math.Clamp(done - i, 0, 1);
                if (part <= 0.02) continue;
                var box = new Rect(inner.X + i * (seg + gap), inner.Y, Math.Max(0, Math.Min(seg, Math.Max(inner.Height, seg * part))), inner.Height);
                var brush = StepsUseFill ? Fill : ThemeManager.StepBrush((i + 1.0) / Steps);
                if (cut > 0) dc.DrawRectangle(brush, null, box);
                else dc.DrawRoundedRectangle(brush, null, box, Math.Min(ri, seg / 2), Math.Min(ri, seg / 2));
            }
            if (cut > 0) dc.Pop();
            return;
        }

        double fw = value * inner.Width;
        if (fw <= 0.5) return;
        double end = Math.Max(inner.Height, fw);
        var fill = FillFor(edge);
        if (cut > 0)
        {
            double icut = Math.Max(0, cut - Pad * (2 - Math.Sqrt(2)));
            var key = (Math.Round(end * 2) / 2, w, h, icut);
            if (_fill is null || _fillKey != key)
            {
                _fillKey = key;
                _fill = Tile.Chamfered(new Rect(inner.X, inner.Y, key.Item1, inner.Height), Math.Min(icut, inner.Height / 2));
            }
            dc.DrawGeometry(fill, null, _fill);
        }
        else
            dc.DrawRoundedRectangle(fill, null, new Rect(inner.X, inner.Y, end, inner.Height), ri, ri);
        if (ThemeManager.MeterHead is { } head && end >= 8)
        {
            if (cut > 0) dc.PushClip(_fill!);
            dc.DrawRectangle(head, null, new Rect(inner.X + end - 3, inner.Y, 3, inner.Height));
            if (cut > 0) dc.Pop();
        }
    }
}

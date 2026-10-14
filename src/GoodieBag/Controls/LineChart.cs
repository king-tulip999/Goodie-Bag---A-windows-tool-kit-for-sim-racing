using System.Globalization;
using System.Windows;
using System.Windows.Media;

using GoodieBag.Theme;

namespace GoodieBag.Controls;

public sealed class ChartSeries
{
    public required Brush Stroke { get; init; }
    public required string Unit { get; init; }
    public IReadOnlyList<double?> Values { get; set; } = Array.Empty<double?>();
    public double Min { get; init; }
    public double Max { get; init; } = 100;

    public bool AutoMax { get; init; }

    public Func<double, string>? Format { get; init; }
}

public sealed class LineChart : FrameworkElement
{
    private static Typeface Face => ThemeManager.UiFace();

    public List<ChartSeries> Series { get; } = new();
    public int Capacity { get; set; } = 120;

    public string StartLabel { get; set; } = "2 min ago";
    public string EndLabel { get; set; } = "now";

    public void Refresh() => InvalidateVisual();

    private Pen? _gridPen;
    private (double Min, double Max)[] _ranges = Array.Empty<(double, double)>();
    private readonly Dictionary<ChartSeries, (Pen Pen, Brush? Fill)> _seriesPens = new();
    private readonly Dictionary<(string Text, Brush Brush, double Dpi), FormattedText> _labels = new();

    private FormattedText Label(string text, Brush brush, double dpi)
    {
        if (_labels.TryGetValue((text, brush, dpi), out var cached))
            return cached;
        if (_labels.Count > 64)
            _labels.Clear();
        var formatted = new FormattedText(ThemeManager.Case(text), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 10.5, brush, dpi);
        _labels[(text, brush, dpi)] = formatted;
        return formatted;
    }

    private (Pen Pen, Brush? Fill) PenFor(ChartSeries s, int index)
    {
        if (_seriesPens.TryGetValue(s, out var cached))
            return cached;
        Brush? fill = null;
        if (index == 0 && s.Stroke is SolidColorBrush solid)
        {
            var c = solid.Color;
            fill = new SolidColorBrush(Color.FromArgb(30, c.R, c.G, c.B));
            fill.Freeze();
        }
        var pen = new Pen(s.Stroke, 2) { LineJoin = PenLineJoin.Round };
        if (index == 1)
            pen.DashStyle = new DashStyle(new double[] { 3, 2 }, 0);
        pen.Freeze();
        return _seriesPens[s] = (pen, fill);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (w < 40 || h < 40 || Series.Count == 0)
            return;
        var gridBrush = ThemeManager.Brush("GridLine", Brushes.LightGray);
        if (_gridPen is null || _gridPen.Brush != gridBrush)
        {
            _gridPen = new Pen(gridBrush, 1);
            _gridPen.Freeze();
        }
        var gridPen = _gridPen;
        var labelBrush = ThemeManager.Brush("TextFaint", Brushes.Gray);

        const double left = 38, right = 38, top = 8, bottom = 20;
        var plot = new Rect(left, top, Math.Max(1, w - left - right), Math.Max(1, h - top - bottom));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (_ranges.Length < Series.Count)
            _ranges = new (double, double)[Series.Count];
        var ranges = _ranges;
        for (int si = 0; si < Series.Count; si++)
        {
            var s = Series[si];
            double max = s.Max;
            if (s.AutoMax)
            {
                double peak = 0;
                foreach (var v in s.Values)
                    if (v is { } x && x > peak)
                        peak = x;
                max = Math.Max(s.Max, Math.Ceiling(peak * 1.15 / 30) * 30);
            }
            ranges[si] = (s.Min, max);
        }

        for (int i = 0; i <= 4; i++)
        {
            double y = plot.Top + plot.Height * i / 4;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            for (int si = 0; si < Math.Min(2, Series.Count); si++)
            {
                var (min, max) = ranges[si];
                double value = max - (max - min) * i / 4;
                string label = Series[si].Format is { } format ? format(value) : $"{value:0}{Series[si].Unit}";
                var text = Label(label, labelBrush, dpi);
                double x = si == 0 ? plot.Left - text.Width - 6 : plot.Right + 6;
                dc.DrawText(text, new Point(x, y - text.Height / 2));
            }
        }
        dc.DrawText(Label(StartLabel, labelBrush, dpi), new Point(plot.Left, plot.Bottom + 4));
        var now = Label(EndLabel, labelBrush, dpi);
        dc.DrawText(now, new Point(plot.Right - now.Width, plot.Bottom + 4));

        double step = plot.Width / (Capacity - 1);
        for (int si = Series.Count - 1; si >= 0; si--)
        {
            var s = Series[si];
            var (min, max) = ranges[si];
            int count = s.Values.Count;
            double x0 = plot.Right - step * (count - 1);
            Point P(int i) => new(x0 + i * step, plot.Bottom - (Math.Clamp(s.Values[i]!.Value, min, max) - min) / (max - min) * plot.Height);

            var line = new StreamGeometry();
            var area = new StreamGeometry();
            using (var lc = line.Open())
            using (var ac = area.Open())
            {
                bool open = false;
                int runStart = -1;
                for (int i = 0; i < count; i++)
                {
                    if (s.Values[i] is null)
                    {
                        if (open && si == 0) ac.LineTo(new Point(P(i - 1).X, plot.Bottom), false, false);
                        open = false;
                        continue;
                    }
                    var p = P(i);
                    if (!open)
                    {
                        lc.BeginFigure(p, false, false);
                        if (si == 0) { ac.BeginFigure(new Point(p.X, plot.Bottom), true, true); ac.LineTo(p, false, false); }
                        open = true;
                        runStart = i;
                    }
                    else
                    {
                        lc.LineTo(p, true, true);
                        if (si == 0) ac.LineTo(p, false, false);
                    }
                }
                if (open && si == 0 && runStart >= 0)
                    ac.LineTo(new Point(P(count - 1).X, plot.Bottom), false, false);
            }
            line.Freeze();
            area.Freeze();

            var (pen, fill) = PenFor(s, si);
            if (fill is not null)
                dc.DrawGeometry(fill, null, area);
            dc.DrawGeometry(null, pen, line);
        }
    }
}

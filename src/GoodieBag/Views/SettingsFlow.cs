using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GoodieBag.Views;

internal sealed class SettingRowGrid : Grid;

internal sealed class SettingDivider : Border;

internal sealed class SettingsFlow : StackPanel
{
    public double MinColumnWidth { get; init; } = 340;
    public int MaxColumns { get; init; } = 3;
    public double ColumnGap { get; init; } = 32;

    private const double RuleSpace = 5;

    private readonly List<(UIElement Child, Rect Place)> _places = new();
    private readonly List<Rect> _rules = new();
    private readonly List<(double X, double Top, double Bottom)> _columnRules = new();
    private bool _flowing;
    private double _laidOutWidth = double.NaN;

    private int Columns(double width) =>
        double.IsInfinity(width) || double.IsNaN(width) ? 1 : Math.Clamp((int)((width + ColumnGap) / (MinColumnWidth + ColumnGap)), 1, MaxColumns);

    protected override Size MeasureOverride(Size available)
    {
        _flowing = Columns(available.Width) > 1;
        if (!_flowing)
        {
            _places.Clear();
            _rules.Clear();
            _columnRules.Clear();
            return base.MeasureOverride(available);
        }
        return new Size(available.Width, Layout(available.Width));
    }

    private double Layout(double width, bool measure = true)
    {
        _laidOutWidth = width;
        _places.Clear();
        _rules.Clear();
        _columnRules.Clear();
        int columns = Columns(width);
        double y = 0;
        var run = new List<UIElement>();

        void Flush()
        {
            if (run.Count == 0) return;
            int rows = (int)Math.Ceiling(run.Count / (double)columns);
            int cols = (int)Math.Ceiling(run.Count / (double)rows);
            int share = Math.Max(cols, 2);
            double cell = (width - ColumnGap * (share - 1)) / share, top = y;
            for (int r = 0; r < rows; r++)
            {
                if (r > 0)
                {
                    for (int c = 0; c < cols; c++)
                        _rules.Add(new Rect(c * (cell + ColumnGap), y + 2, cell, 1));
                    y += RuleSpace;
                }
                double height = 0;
                var line = run.Skip(r * cols).Take(cols).ToList();
                foreach (var child in line)
                {
                    if (measure) child.Measure(new Size(cell, double.PositiveInfinity));
                    height = Math.Max(height, child.DesiredSize.Height);
                }
                for (int c = 0; c < line.Count; c++)
                    _places.Add((line[c], new Rect(c * (cell + ColumnGap), y + (height - line[c].DesiredSize.Height) / 2, cell, line[c].DesiredSize.Height)));
                y += height;
            }
            for (int c = 1; c < cols; c++)
                _columnRules.Add((Math.Floor(c * (cell + ColumnGap) - ColumnGap / 2), top, y));
            run.Clear();
        }

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            if (child is SettingDivider)
            {
                if (measure) child.Measure(new Size(0, 0));
                continue;
            }
            if (child is SettingRowGrid) { run.Add(child); continue; }
            Flush();
            if (measure) child.Measure(new Size(width, double.PositiveInfinity));
            _places.Add((child, new Rect(0, y, width, child.DesiredSize.Height)));
            y += child.DesiredSize.Height;
        }
        Flush();
        return y;
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (!_flowing) return base.ArrangeOverride(final);
        if (Math.Abs(final.Width - _laidOutWidth) > 0.5) Layout(final.Width, measure: false);
        foreach (UIElement child in InternalChildren)
            if (child is SettingDivider) child.Arrange(new Rect(0, 0, 0, 0));
        foreach (var (child, place) in _places)
            child.Arrange(place);
        InvalidateVisual();
        return final;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!_flowing || TryFindResource("Line") is not Brush line) return;
        foreach (var (x, top, bottom) in _columnRules)
            Dividers.DrawVertical(dc, x, top, bottom);
        foreach (var rule in _rules)
            dc.DrawRectangle(line, null, rule);
    }
}

using System.Windows;
using System.Windows.Controls;

namespace GoodieBag.Controls;

public sealed class MasonryPanel : Panel
{
    public double MinColumnWidth { get; set; } = 460;
    public int MaxColumns { get; set; } = 3;
    public double Gap { get; set; } = 16;
    public bool CenterFew { get; set; }
    public bool Stable { get; set; }

    private int ColumnFor(double[] heights, int index) => Stable ? index % heights.Length : Array.IndexOf(heights, heights.Min());

    private int Used(int columns) =>
        CenterFew ? Math.Clamp(InternalChildren.Cast<UIElement>().Count(c => c.Visibility != Visibility.Collapsed), 1, columns) : columns;

    private int Columns(double width) =>
        double.IsInfinity(width) ? 1 : Math.Clamp((int)((width + Gap) / (MinColumnWidth + Gap)), 1, Math.Max(1, MaxColumns));

    private double ColumnWidth(double width, int columns) => Math.Max(0, (width - Gap * (columns - 1)) / columns);

    protected override Size MeasureOverride(Size available)
    {
        int columns = Columns(available.Width);
        double w = double.IsInfinity(available.Width) ? MinColumnWidth : ColumnWidth(available.Width, columns);
        var heights = new double[Used(columns)];
        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(w, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) continue;
            int c = ColumnFor(heights, index++);
            heights[c] += child.DesiredSize.Height + Gap;
        }
        double height = Math.Max(0, heights.Max() - Gap);
        return new Size(double.IsInfinity(available.Width) ? w : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        int columns = Columns(final.Width);
        double w = ColumnWidth(final.Width, columns);
        int used = Used(columns);
        double left = (final.Width - (used * w + (used - 1) * Gap)) / 2 * (used < columns ? 1 : 0);
        var heights = new double[used];
        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) { child.Arrange(new Rect()); continue; }
            int c = ColumnFor(heights, index++);
            child.Arrange(new Rect(left + c * (w + Gap), heights[c], w, child.DesiredSize.Height));
            heights[c] += child.DesiredSize.Height + Gap;
        }
        return final;
    }
}

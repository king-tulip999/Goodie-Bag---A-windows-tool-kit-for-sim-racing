using System.Windows;
using System.Windows.Controls;

namespace GoodieBag.Views;

internal sealed class TrimRow : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double tails = 0, height = 0;
        for (int i = 1; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            tails += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        if (InternalChildren.Count == 0) return new Size(0, 0);
        var first = InternalChildren[0];
        first.Measure(new Size(Math.Max(0, available.Width - tails), available.Height));
        height = Math.Max(height, first.DesiredSize.Height);
        return new Size(first.DesiredSize.Width + tails, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double tails = 0;
        for (int i = 1; i < InternalChildren.Count; i++) tails += InternalChildren[i].DesiredSize.Width;
        double x = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            double width = i == 0 ? Math.Min(child.DesiredSize.Width, Math.Max(0, final.Width - tails)) : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, final.Height));
            x += width;
        }
        return final;
    }
}

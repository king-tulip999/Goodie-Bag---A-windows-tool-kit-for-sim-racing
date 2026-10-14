using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GoodieBag.Views;

internal sealed class PanelStack : StackPanel
{
    public Func<UIElement, bool> IsPanel { get; init; } = child => child is Collapsible;

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = base.ArrangeOverride(arrangeSize);
        InvalidateVisual();
        return size;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        FrameworkElement? above = null;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility != Visibility.Visible || child is not FrameworkElement fe || fe.ActualHeight <= 0 || !IsPanel(child)) continue;
            if (above is not null)
            {
                double bottom = above.TranslatePoint(new Point(0, above.ActualHeight), this).Y, top = fe.TranslatePoint(new Point(0, 0), this).Y;
                if (top - bottom > 2)
                    Dividers.Draw(dc, 24, ActualWidth - 24, Math.Round((bottom + top) / 2));
            }
            above = fe;
        }
    }
}

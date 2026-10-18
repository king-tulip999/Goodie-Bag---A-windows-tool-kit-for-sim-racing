using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal static class TextFit
{
    private static FormattedText Format(TextBlock tb, string text) =>
        new(text, CultureInfo.CurrentUICulture, tb.FlowDirection, new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
            tb.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(tb).PixelsPerDip);

    public static double Width(TextBlock tb, string text) => Format(tb, text).WidthIncludingTrailingWhitespace;

    public static bool IsTrimmed(TextBlock tb)
    {
        if (tb.TextTrimming == TextTrimming.None || tb.Text.Length == 0 || tb.ActualWidth <= 0) return false;
        double width = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
        if (tb.TextWrapping == TextWrapping.NoWrap) return Width(tb, tb.Text) > width + 0.5;
        var text = Format(tb, tb.Text);
        text.MaxTextWidth = Math.Max(1, width);
        if (!double.IsNaN(tb.LineHeight)) text.LineHeight = tb.LineHeight;
        return text.Height > tb.ActualHeight - tb.Padding.Top - tb.Padding.Bottom + 0.5;
    }

    private static readonly DependencyProperty TrimTipProperty =
        DependencyProperty.RegisterAttached("TrimTip", typeof(bool), typeof(TextFit), new PropertyMetadata(false));

    public static T TipWhenTrimmed<T>(T tb) where T : TextBlock
    {
        tb.SetValue(TrimTipProperty, true);
        tb.SizeChanged += (_, _) => Update(tb);
        tb.Loaded += (_, _) => Update(tb);
        tb.ToolTipOpening += (_, e) =>
        {
            if (!Update(tb)) e.Handled = true;
        };
        return tb;
    }

    public static bool HasTrimTip(DependencyObject d) => (bool)d.GetValue(TrimTipProperty);

    private static bool Update(TextBlock tb)
    {
        if (IsTrimmed(tb))
        {
            if (tb.ToolTip is not ToolTip { Content: string shown } || shown != tb.Text) tb.ToolTip = TextCase.VerbatimTip(tb.Text);
            return true;
        }
        if (tb.ToolTip is ToolTip) tb.ClearValue(FrameworkElement.ToolTipProperty);
        return false;
    }
}

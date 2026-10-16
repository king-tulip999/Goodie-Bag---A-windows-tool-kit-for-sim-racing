using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GoodieBag.Showcase;

internal static class Find
{
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var d = queue.Dequeue();
            if (!ReferenceEquals(d, root)) yield return d;
            if (d is not Visual and not System.Windows.Media.Media3D.Visual3D) continue;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) queue.Enqueue(VisualTreeHelper.GetChild(d, i));
        }
    }

    public static IEnumerable<T> Shown<T>(DependencyObject root) where T : FrameworkElement =>
        Descendants(root).OfType<T>().Where(e => e.IsVisible && e.ActualWidth > 0);

    public static string TextOf(object? content) => content switch
    {
        null => "",
        string s => s,
        TextBlock t => t.Text,
        ContentControl c when c.Content is string s => s,
        DependencyObject d => string.Join(" ", Descendants(d).OfType<TextBlock>().Select(t => t.Text)),
        _ => content.ToString() ?? "",
    };

    public static bool Is(string? shown, string text) => shown is not null && string.Equals(Theme.TextCase.Plain(shown).Trim(), text, StringComparison.OrdinalIgnoreCase);

    public static bool Has(string? shown, string text) => shown is not null && Theme.TextCase.Plain(shown).Contains(text, StringComparison.OrdinalIgnoreCase);

    public static T? Button<T>(DependencyObject root, Func<string, bool> text) where T : FrameworkElement =>
        Shown<T>(root).FirstOrDefault(b => text(TextOf(b)));

    public static FrameworkElement? RowOf(DependencyObject root, string label, Func<FrameworkElement, bool> has)
    {
        var text = Shown<TextBlock>(root).FirstOrDefault(t => Is(t.Text, label));
        for (DependencyObject? d = text; d is not null && !ReferenceEquals(d, root); d = VisualTreeHelper.GetParent(d))
            if (d is FrameworkElement fe && has(fe)) return fe;
        return null;
    }

    public static Point At(FrameworkElement e, double fx = 0.5, double fy = 0.5) => e.PointToScreen(new Point(e.ActualWidth * fx, e.ActualHeight * fy));

    public static ScrollViewer? PageScroll(DependencyObject page) => Descendants(page).OfType<ScrollViewer>().FirstOrDefault();
}

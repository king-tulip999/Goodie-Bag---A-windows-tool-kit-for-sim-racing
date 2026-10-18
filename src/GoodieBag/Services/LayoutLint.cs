using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GoodieBag.Services;

internal static class LayoutLint
{
    private const double Slack = 1.0;

    private static readonly string NarrowSpace = ((char)0x202F).ToString();

    internal sealed record Finding(string Rule, string Where, string Text, string Detail)
    {
        public override string ToString() => $"layout {Rule} [{Where}] “{Text}” {Detail}";
    }

    private sealed record Allowed(string Rule, string Where, string Text, string Reason);

    private static List<Allowed>? _allowed;

    private static List<Allowed> AllowList => _allowed ??= ReadAllowList();

    private static List<Allowed> ReadAllowList()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string file = Path.Combine(dir.FullName, "devtools", "tools", "layout-lint.allow.txt");
            if (!File.Exists(file)) continue;
            return File.ReadAllLines(file)
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .Select(l => l.Split('\t'))
                .Where(p => p.Length == 4 && p[3].Trim().Length > 0)
                .Select(p => new Allowed(p[0], p[1], p[2], p[3]))
                .ToList();
        }
        return new List<Allowed>();
    }

    private static bool IsAllowed(Finding f) => AllowList.Any(a =>
        a.Rule == f.Rule
        && (a.Where == "*" || f.Where.StartsWith(a.Where, StringComparison.OrdinalIgnoreCase))
        && (a.Text == "*" || f.Text.Contains(a.Text, StringComparison.OrdinalIgnoreCase)));

    public static List<Finding> Inspect(Visual root, string where)
    {
        var found = new List<Finding>();
        Walk(root, root, where, found);
        return found.Where(f => !IsAllowed(f)).DistinctBy(f => (f.Rule, f.Text)).ToList();
    }

    private static void Walk(Visual node, Visual root, string where, List<Finding> found)
    {
        if (node is UIElement { IsVisible: false }) return;
        if (node is UIElement { Opacity: < 0.05 }) return;
        switch (node)
        {
            case TextBlock tb:
                CheckText(tb, where, found);
                break;
            case ScrollViewer sv when IsPageScroll(sv) && sv.ScrollableWidth > Slack:
                found.Add(new Finding("hscroll", where, Name(sv), $"page scrolls sideways by {sv.ScrollableWidth:0} px"));
                break;
            case Panel panel:
                CheckOverlap(panel, where, found);
                break;
        }
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            if (VisualTreeHelper.GetChild(node, i) is Visual child)
                Walk(child, root, where, found);
    }

    private static bool IsPageScroll(ScrollViewer sv) =>
        sv.Style is { } style && Application.Current?.TryFindResource("PageScroll") is Style page && ReferenceEquals(style, page);

    private static string Name(FrameworkElement e) =>
        e.Name is { Length: > 0 } n ? n : e.GetType().Name;

    private static string Short(string text)
    {
        text = text.Replace(NarrowSpace, "").Replace(Environment.NewLine, " ").Replace("\n", " ");
        return text.Length > 60 ? text[..57] + "…" : text;
    }

    private static double VisibleWidth(FrameworkElement e)
    {
        double width = e.ActualWidth;
        if (LayoutInformation.GetLayoutClip(e) is { } clip && !clip.Bounds.IsEmpty)
            width = Math.Min(width, clip.Bounds.Right);
        for (DependencyObject? p = VisualTreeHelper.GetParent(e), child = e; p is FrameworkElement parent; child = p, p = VisualTreeHelper.GetParent(p))
        {
            if (parent is ScrollContentPresenter or ScrollViewer) break;
            if (parent is Canvas) break;
            if (child is FrameworkElement c && LayoutInformation.GetLayoutClip(c) is { } cc && !cc.Bounds.IsEmpty && child != e)
            {
                try
                {
                    var right = e.TransformToAncestor(c).Transform(new Point(width, 0)).X;
                    if (right > cc.Bounds.Right + Slack)
                        width -= right - cc.Bounds.Right;
                }
                catch (InvalidOperationException) { break; }
            }
            if (parent.ClipToBounds)
            {
                try
                {
                    var right = e.TransformToAncestor(parent).Transform(new Point(width, 0)).X;
                    if (right > parent.ActualWidth + Slack)
                        width -= right - parent.ActualWidth;
                }
                catch (InvalidOperationException) { break; }
            }
        }
        return width;
    }

    private static readonly Regex Word = new(@"[^ \t\r\n\u2014\u2013\u2009\u200B/\u00B7-]+", RegexOptions.Compiled);

    private static void CheckText(TextBlock tb, string where, List<Finding> found)
    {
        string text = tb.Text;
        if (string.IsNullOrWhiteSpace(text) || tb.ActualWidth <= 0 || tb.FontSize <= 0 || !tb.IsMeasureValid) return;
        if (tb.Inlines.Count > 0 && tb.Inlines.FirstInline is not System.Windows.Documents.Run) return;
        double room = VisibleWidth(tb) - tb.Padding.Left - tb.Padding.Right;
        if (room <= 0) return;
        double formatted = Views.TextFit.Width(tb, text);
        double scale = 1;
        if (formatted > 0)
        {
            double laidOut = Natural(tb) - tb.Padding.Left - tb.Padding.Right;
            if (laidOut > 0) scale = laidOut / formatted;
        }
        if (tb.TextWrapping == TextWrapping.NoWrap)
        {
            double natural = formatted * scale;
            if (natural <= room + Slack) return;
            if (tb.TextTrimming == TextTrimming.None)
                found.Add(new Finding("cut", where, Short(text), $"needs {natural:0} px, has {room:0} in {Ancestry(tb)}"));
            else if (!HasTip(tb))
                found.Add(new Finding("trimmed-no-tip", where, Short(text), $"needs {natural:0} px, has {room:0}"));
            return;
        }
        double longest = 0;
        string longestWord = "";
        foreach (Match m in Word.Matches(text))
        {
            double w = Views.TextFit.Width(tb, m.Value) * scale;
            if (w > longest) { longest = w; longestWord = m.Value; }
        }
        if (longest > room + Slack && tb.TextTrimming == TextTrimming.None)
            found.Add(new Finding("mid-word", where, Short(text), $"“{Short(longestWord)}” needs {longest:0} px, has {room:0}"));
    }

    private static double Natural(TextBlock tb)
    {
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = tb.DesiredSize.Width - tb.Margin.Left - tb.Margin.Right;
        tb.InvalidateMeasure();

        return width;
    }

    private static string Ancestry(DependencyObject e)
    {
        var parts = new List<string>();
        for (var p = VisualTreeHelper.GetParent(e); p is not null && parts.Count < 5; p = VisualTreeHelper.GetParent(p))
            if (p is FrameworkElement fe) parts.Add(fe.Name is { Length: > 0 } n ? $"{fe.GetType().Name}#{n}" : fe.GetType().Name);
        var top = e;
        for (int i = 0; i < 4 && VisualTreeHelper.GetParent(top) is { } up; i++) top = up;
        return string.Join(" < ", parts) + $" ({Short(string.Join(" | ", Texts(top).Take(4)))})";
    }

    private static bool HasTip(FrameworkElement e)
    {
        DependencyObject? node = e;
        for (int i = 0; i < 6 && node is FrameworkElement fe; i++, node = VisualTreeHelper.GetParent(node))
            if (fe.ToolTip is not null || Views.TextFit.HasTrimTip(fe) || fe is ComboBox || fe.TemplatedParent is ComboBox) return true;
        return false;
    }

    private static void CheckOverlap(Panel panel, string where, List<Finding> found)
    {
        if (panel is Canvas || panel.Children.Count < 2) return;
        if (panel is Grid { ColumnDefinitions.Count: <= 1, RowDefinitions.Count: <= 1 }) return;
        var items = new List<(FrameworkElement E, Rect R)>();
        foreach (UIElement child in panel.Children)
        {
            if (child is not FrameworkElement fe || !fe.IsVisible || fe.ActualWidth < 2 || fe.ActualHeight < 2 || fe.Opacity < 0.05) continue;
            if (fe.RenderTransform is { } t && !t.Value.IsIdentity) continue;
            if (fe.Margin.Left < 0 || fe.Margin.Top < 0 || fe.Margin.Right < 0 || fe.Margin.Bottom < 0) continue;
            var offset = VisualTreeHelper.GetOffset(fe);
            double width = fe.ActualWidth, height = fe.ActualHeight;
            if (LayoutInformation.GetLayoutClip(fe) is { } clip && !clip.Bounds.IsEmpty)
            {
                width = Math.Min(width, clip.Bounds.Right);
                height = Math.Min(height, clip.Bounds.Bottom);
            }
            items.Add((fe, new Rect(offset.X, offset.Y, width, height)));
        }
        for (int i = 0; i < items.Count; i++)
            for (int j = i + 1; j < items.Count; j++)
            {
                var (a, ra) = items[i];
                var (b, rb) = items[j];
                if (panel is Grid && SameCell(a, b)) continue;
                var both = Rect.Intersect(ra, rb);
                if (both.IsEmpty || both.Width <= Slack || both.Height <= Slack) continue;
                found.Add(new Finding("overlap", where, Short(Describe(a) + " × " + Describe(b)), $"{both.Width:0}×{both.Height:0} px in a {panel.GetType().Name}"));
            }
    }

    private static bool SameCell(UIElement a, UIElement b)
    {
        int ac = Grid.GetColumn(a), ar = Grid.GetRow(a), acs = Grid.GetColumnSpan(a), ars = Grid.GetRowSpan(a);
        int bc = Grid.GetColumn(b), br = Grid.GetRow(b), bcs = Grid.GetColumnSpan(b), brs = Grid.GetRowSpan(b);
        bool columns = ac < bc + bcs && bc < ac + acs;
        bool rows = ar < br + brs && br < ar + ars;
        return columns && rows;
    }

    private static string Describe(FrameworkElement e)
    {
        var text = FirstText(e);
        return text is null ? Name(e) : $"{Name(e)} {text}";
    }

    private static IEnumerable<string> Texts(DependencyObject node)
    {
        if (node is TextBlock { Text.Length: > 0 } tb) yield return tb.Text;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            foreach (var t in Texts(VisualTreeHelper.GetChild(node, i))) yield return t;
    }

    private static string? FirstText(DependencyObject node)
    {
        if (node is TextBlock { Text.Length: > 0 } tb) return Short(tb.Text);
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            if (FirstText(VisualTreeHelper.GetChild(node, i)) is { } t) return t;
        return null;
    }
}

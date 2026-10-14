using System.Windows;
using System.Windows.Controls;
using GoodieBag.Services;
using GoodieBag.Views;

namespace GoodieBag.Grr.Views;

internal sealed class GrrPage : UserControl, IPage, IScreenshotStates
{
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private readonly ScrollViewer _scroll;
    private readonly GrrDashboardSection _dashboard;

    public GrrPage(AppServices app, MainWindow window)
    {
        _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = _body, Focusable = false };
        Content = _scroll;
        var titles = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        titles.Children.Add(Ui.Text("GRR League", "H1"));
        titles.Children.Add(new TextBlock
        {
            Text = "Your league's next race, championship, results and schedule from the GRR website, without leaving the app.",
            Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 4, 0, 0), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap,
        });
        _body.Children.Add(titles);
        _dashboard = new GrrDashboardSection(app, window);
        Ui.Anchor(_dashboard, GrrParts.Anchor);
        _body.Children.Add(_dashboard);
    }

    public void OnShown() => _dashboard.OnShown();

    public void OnHidden() => _dashboard.OnHidden();

    internal void Reveal(GrrDashboardSection.Part part)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _body.UpdateLayout();
            FrameworkElement? at = _dashboard.CardFor(part) is { IsVisible: true } card ? card : null;
            _body.UpdateLayout();
            _scroll.ScrollToVerticalOffset(at is null ? 0 : Math.Max(0, at.TranslatePoint(new Point(), _body).Y - 12));
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        foreach (var state in _dashboard.ScreenshotStates())
            yield return state;
        yield return ("grr-narrow", () => { _body.MaxWidth = 760; _body.HorizontalAlignment = HorizontalAlignment.Left; _scroll.ScrollToTop(); return Task.CompletedTask; });
        yield return ("grr-wide", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); return Task.CompletedTask; });
        yield return ("grr-reveal-schedule", () => { Reveal(GrrDashboardSection.Part.Schedule); return Task.CompletedTask; });
    }
}

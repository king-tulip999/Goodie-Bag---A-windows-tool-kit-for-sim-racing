using System.Windows;
using System.Windows.Controls;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.Views;

namespace GoodieBag.Grr.Views;

internal sealed class GrrPage : UserControl, IPage, IScreenshotStates, ISectionedPage, IPageHistory
{
    private readonly AppServices _app;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private readonly ScrollViewer _scroll;
    private readonly GrrDashboardSection _dashboard;
    private readonly ContentControl _title = new() { Focusable = false };
    private string _signature = "";
    private bool _shown;

    public event Action? SectionsChanged;

    private static readonly Dictionary<string, string> Icons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cup"] = NavIcon.CarGlyph + "grr-cup", ["gt"] = NavIcon.CarGlyph + "grr-gt", ["indycar"] = NavIcon.CarGlyph + "grr-indycar",
    };

    public GrrPage(AppServices app, MainWindow window)
    {
        _app = app;
        _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body), Focusable = false };
        Content = _scroll;
        _dashboard = new GrrDashboardSection(app, window, leagueTabs: false);
        var header = Ui.SplitRow(_title, _dashboard.Tools!);
        header.Margin = new Thickness(0, 0, 0, Ui.HeaderGap);
        _body.Children.Add(header);
        Ui.Anchor(_dashboard, GrrParts.Anchor);
        _body.Children.Add(_dashboard);
        SyncTitle();
        _signature = Signature();
    }

    internal static SectionList SectionsFor(AppServices app)
    {
        var s = app.Config.Grr;
        var sections = s.Leagues.Select(GrrLeagues.Find).OfType<GrrLeague>()
            .Select(l => new PageSection(l.Id, l.Name, Icons.GetValueOrDefault(l.Id, GrrPlugin.LogoIcon), Hues.Tool(GrrPlugin.PluginId, ("SageSoft", "SageDeep")))).ToList();
        return new SectionList(sections, s.DashboardLeagueOrNull());
    }

    public IReadOnlyList<PageSection> Sections => SectionsFor(_app).Sections;

    public string? CurrentSection => _app.Config.Grr.DashboardLeagueOrNull();

    private string Signature() => string.Join("|", _app.Config.Grr.Leagues) + "#" + CurrentSection;

    private void SyncTitle()
    {
        string name = CurrentSection is { } league ? GrrLeagues.Name(league) : "GRR League";
        _title.Content = Ui.PluginTitle(GrrPlugin.PluginId, name);
    }

    public void ShowSection(string id)
    {
        if (!_app.Config.Grr.Leagues.Contains(id, StringComparer.OrdinalIgnoreCase) || string.Equals(CurrentSection, id, StringComparison.OrdinalIgnoreCase)) return;
        _app.Config.Grr.DashboardLeague = GrrLeagues.Find(id)!.Id;
        _app.Save();
        _dashboard.ShowLeague();
        _scroll.ScrollToTop();
        Changed();
        if (_shown && Motion.Enabled) Motion.Enter(_dashboard, distance: 12, durationMs: 240);
    }

    public bool ShowSectionOf(DependencyObject target) => false;

    public void MoveSections(IReadOnlyList<string> order)
    {
        var s = _app.Config.Grr;
        s.Leagues = order.Where(id => s.Leagues.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Concat(s.Leagues.Where(id => !order.Contains(id, StringComparer.OrdinalIgnoreCase))).ToList();
        _app.Save();
        Changed();
    }

    private void Changed()
    {
        _signature = Signature();
        SyncTitle();
        SectionsChanged?.Invoke();
    }

    public object? HistoryState => CurrentSection;

    public void RestoreHistory(object? state)
    {
        if (state is string id) ShowSection(id);
    }

    public void OnShown()
    {
        _shown = true;
        _app.Grr.Changed -= OnGrrChanged;
        _app.Grr.Changed += OnGrrChanged;
        if (Signature() != _signature) Changed();
        _dashboard.OnShown();
    }

    public void OnHidden()
    {
        _shown = false;
        _app.Grr.Changed -= OnGrrChanged;
        _dashboard.OnHidden();
    }

    private void OnGrrChanged()
    {
        if (Signature() != _signature) Changed();
    }

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
            yield return (state.Name, async () => { await state.Enter(); if (Signature() != _signature) Changed(); });
        yield return ("grr-sidebar-gt", () =>
        {
            ShowSection("gt");
            if (CurrentSection != "gt") throw new InvalidOperationException("Picking GT League in the sidebar didn't show it.");
            return Task.CompletedTask;
        });
        yield return ("grr-narrow", () => { _body.MaxWidth = 760; _body.HorizontalAlignment = HorizontalAlignment.Left; _scroll.ScrollToTop(); return Task.CompletedTask; });
        yield return ("grr-wide", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); return Task.CompletedTask; });
        yield return ("grr-reveal-schedule", () => { Reveal(GrrDashboardSection.Part.Schedule); return Task.CompletedTask; });
    }
}

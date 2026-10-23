using System.Windows;
using System.Windows.Controls;
using GoodieBag.Plugins;
using GoodieBag.Services;

namespace GoodieBag.Views.Pages;

internal sealed class HardwareMonitorPage : UserControl, IPage, IScreenshotStates, ICancelEdit, IRevealAnchor, ISectionedPage, IPageHistory
{
    private const string PageKey = "monitor";

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private readonly ScrollViewer _scroll;
    private readonly PerformanceSection _performance;
    private readonly SensorsSection _sensors;
    private OverlayPage? _overlay;
    private readonly SectionView _view;
    private bool _shown;

    internal static readonly string[] DefaultSections = { "overlay", "performance", "sensors" };

    public event Action? SectionsChanged;

    public HardwareMonitorPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body) };
        Content = _scroll;
        _view = new SectionView(() => _scroll);

        _view.Empty = new SectionDef("", "Hardware Monitor", "Every section is off. Switch them on again in Settings → Plugins.", "I.HeartPulse",
            Hues.Tool(PluginIds.HardwareMonitor, Hues.Accent), () => new Border());
        _view.Heading.Margin = new Thickness(0, 0, 0, Ui.HeaderGap);
        _body.Children.Add(_view.Heading);
        _body.Children.Add(_view.Content);

        _performance = new PerformanceSection(app, () => window.GoTo("settings", "settings.performance"));
        _sensors = new SensorsSection(app);
        _view.Add(Def("overlay", "A small, click-through panel with FPS, temperatures and load on top of your game. Nothing is injected into the game, so it's safe with anti-cheat. Make several layouts and switch between them with a keybind.",
            () => _overlay = new OverlayPage(_app, _window, embedded: true), "monitor.overlay",
            () => _overlay?.OnShown(), () => _overlay?.OnHidden()));
        _view.Add(Def("performance", "CPU, graphics card, memory, storage, network, motherboard, fans and frame rate, with the last two minutes on graphs.",
            () => _performance, "monitor.performance", _performance.OnShown, _performance.OnHidden, _performance.Tools));
        _view.Add(Def("sensors", "Every reading with min and max, one card per device. Arrange moves the devices up and down.",
            () => _sensors, null, _sensors.OnShown, _sensors.OnHidden, _sensors.Tools));
        Layout(animate: false);
    }

    private bool OverlayAvailable => _app.Plugins.IsActive(PluginIds.Overlay);

    private static readonly PageSection[] Parts =
    {
        new("overlay", "Performance Overlay", "I.Overlay", ("ApricotSoft", "ApricotDeep")),
        new("performance", "Performance", "I.Activity", ("SageSoft", "SageDeep")),
        new("sensors", "Sensors", "I.Cpu", ("SlateSoft", "SlateDeep")),
    };

    private static List<string> OrderedFor(AppServices app)
    {
        var order = app.Config.Ui.MonitorSectionOrder;
        int Rank(string id)
        {
            int i = order.IndexOf(id);
            return i >= 0 ? i : order.Count + Array.IndexOf(DefaultSections, id);
        }
        return DefaultSections.Where(id => id != "overlay" || app.Plugins.IsActive(PluginIds.Overlay)).OrderBy(Rank).ToList();
    }

    private static List<string> VisibleFor(AppServices app) => OrderedFor(app).Where(id => !app.Config.Ui.MonitorHiddenSections.Contains(id)).ToList();

    internal static SectionList SectionsFor(AppServices app)
    {
        var visible = VisibleFor(app);
        string? saved = app.Config.Ui.PageSections.GetValueOrDefault(PageKey);
        return new SectionList(visible.Select(id => Parts.First(p => p.Id == id)).ToList(), saved is not null && visible.Contains(saved) ? saved : visible.FirstOrDefault());
    }

    internal static IReadOnlyList<PageSection> Switchable => Parts.Where(p => p.Id != "overlay").ToList();

    internal static bool IsSectionOn(AppServices app, string id) => !app.Config.Ui.MonitorHiddenSections.Contains(id);

    internal static void SetSectionOn(AppServices app, string id, bool on)
    {
        var hidden = app.Config.Ui.MonitorHiddenSections;
        if (hidden.Contains(id) == !on) return;
        hidden.Remove(id);
        if (!on) hidden.Add(id);
        app.Save();
        app.Plugins.RaiseChanged();
    }

    private static SectionDef Def(string id, string subtitle, Func<UIElement> build, string? anchor, Action? shown, Action? hidden, FrameworkElement? tools = null)
    {
        var p = Parts.First(x => x.Id == id);
        return new SectionDef(id, p.Title, subtitle, p.Icon, p.Colors, build, anchor, shown, hidden, tools);
    }

    private List<string> Order => _app.Config.Ui.MonitorSectionOrder;

    private List<string> HiddenSections => _app.Config.Ui.MonitorHiddenSections;

    private List<string> Visible() => VisibleFor(_app);

    public IReadOnlyList<PageSection> Sections => Visible().Select(id => Parts.First(p => p.Id == id)).ToList();

    public string? CurrentSection => _view.Current;

    private void Layout(bool animate)
    {
        var visible = Visible();
        string? saved = _app.Config.Ui.PageSections.GetValueOrDefault(PageKey);
        string? want = _view.Current is { } current && visible.Contains(current) ? current
            : saved is not null && visible.Contains(saved) ? saved
            : visible.FirstOrDefault();
        _view.Show(want, animate && _shown);
        SectionsChanged?.Invoke();
    }

    public void ShowSection(string id)
    {
        if (!Visible().Contains(id) || _view.Current == id) return;
        _view.Show(id, _shown);
        _app.Config.Ui.PageSections[PageKey] = id;
        _app.Save();
        SectionsChanged?.Invoke();
    }

    public bool ShowSectionOf(DependencyObject target)
    {
        if (_view.SectionOf(target) is not { } id) return false;
        ShowSection(id);
        return true;
    }

    public void MoveSections(IReadOnlyList<string> order)
    {
        var all = order.Concat(DefaultSections.Where(id => !order.Contains(id))).ToList();
        Order.Clear();
        Order.AddRange(all);
        _app.Save();
        SectionsChanged?.Invoke();
    }

    public object? HistoryState => _view.Current;

    public void RestoreHistory(object? state)
    {
        if (state is string id) ShowSection(id);
    }

    public bool Reveal(string anchor)
    {
        string? id = anchor is "monitor.performance" or "monitor.network" || anchor.StartsWith("performance.", StringComparison.Ordinal) ? "performance" : null;
        if (id is not null && HiddenSections.Contains(id))
        {
            SetSectionHidden(id, false);
            return true;
        }
        bool built = false;
        foreach (var section in Visible().Where(s => !_view.Built(s)))
        {
            _view.Build(section);
            built = true;
        }
        return built;
    }

    private void SetSectionHidden(string id, bool hidden)
    {
        HiddenSections.Remove(id);
        if (hidden) HiddenSections.Add(id);
        _app.Save();
        Layout(animate: true);
    }

    internal FrameworkElement? Sensors => _view.Host("sensors");

    public void OnShown()
    {
        _shown = true;
        _app.Plugins.Changed -= OnPluginsChanged;
        _app.Plugins.Changed += OnPluginsChanged;
        Layout(animate: false);
        _view.SetActive(true);
        Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _shown = false;
        _app.Plugins.Changed -= OnPluginsChanged;
        _view.SetActive(false);
    }

    private void OnPluginsChanged()
    {
        if (!OverlayAvailable && _view.Current == "overlay") _view.Show(null, false);
        if (!OverlayAvailable) { _view.Reset("overlay"); _overlay = null; }
        Layout(animate: true);
    }

    public bool CancelEdit() => _sensors.CancelEdit();

    private static Dictionary<FrameworkElement, double> Heights(DependencyObject root)
    {
        var all = new Dictionary<FrameworkElement, double>();
        void Walk(DependencyObject d)
        {
            if (d is Controls.Tile t) all[t] = t.ActualHeight;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
        }
        Walk(root);
        return all;
    }

    private static string Label(DependencyObject d)
    {
        if (d is TextBlock { Text.Length: > 0 } t) return "“" + t.Text + "”";
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            if (Label(System.Windows.Media.VisualTreeHelper.GetChild(d, i)) is { Length: > 0 } found) return found;
        return "";
    }

    private void Only(string id)
    {
        if (HiddenSections.Contains(id)) SetSectionHidden(id, false);
        ShowSection(id);
        _scroll.ScrollToHome();
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        Only("performance");
        for (int page = 2; page <= 5 && _scroll.VerticalOffset + _scroll.ViewportHeight < _scroll.ExtentHeight - 8; page++)
            yield return ($"dashboard-{page}", () =>
            {
                _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + _scroll.ViewportHeight - 60);
                return Task.CompletedTask;
            });
        yield return ("sensors", () => { Only("sensors"); return Task.CompletedTask; });
        foreach (var state in _sensors.ScreenshotStates())
            yield return state;
        if (OverlayAvailable)
        {
            yield return ("overlay", () => { Only("overlay"); return Task.CompletedTask; });
            foreach (var state in _overlay?.ScreenshotStates() ?? Enumerable.Empty<(string, Func<Task>)>())
                yield return ("overlay-" + state.Name, state.Enter);
        }
        yield return ("sections-reordered", () =>
        {
            MoveSections(Visible().AsEnumerable().Reverse().ToList());
            if (Sections[0].Id != "sensors") throw new InvalidOperationException("Moving the sections didn't change their order.");
            return Task.CompletedTask;
        });
        yield return ("sections-default-order", () => { MoveSections(DefaultSections); return Task.CompletedTask; });
        yield return ("sensors-hidden", () =>
        {
            SetSectionHidden("sensors", true);
            if (Sections.Any(s => s.Id == "sensors") || _view.Current == "sensors") throw new InvalidOperationException("A section that's off is still in the sidebar.");
            _scroll.ScrollToHome();
            return Task.CompletedTask;
        });
        yield return ("sensors-shown", () => { SetSectionHidden("sensors", false); return Task.CompletedTask; });
        yield return ("reentered", async () =>
        {
            Only("performance");
            var performance = _view.Host("performance")!;
            _window.Navigate("settings");
            await Task.Delay(400);
            _window.Navigate(PageKey);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            double first = performance.ActualHeight;
            var before = Heights(performance);
            await Task.Delay(1500);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            if (Math.Abs(performance.ActualHeight - first) > 2)
            {
                var after = Heights(performance);
                var grew = before.Where(b => after.TryGetValue(b.Key, out var h) && Math.Abs(h - b.Value) > 2).Select(b => $"{Label(b.Key)} {b.Value:0}→{after[b.Key]:0}").Take(8);
                throw new InvalidOperationException($"Hardware Monitor changed height after opening: {first:0} → {performance.ActualHeight:0} px; grew: {string.Join("; ", grew)}");
            }
        });
    }
}

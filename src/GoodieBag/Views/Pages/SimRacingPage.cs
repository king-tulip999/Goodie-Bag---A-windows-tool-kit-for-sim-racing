using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Config;
using GoodieBag.Services;
using GoodieBag.Plugins;

namespace GoodieBag.Views.Pages;

internal sealed class SimRacingPage : UserControl, IPage, IScreenshotStates, ICancelEdit, IRevealAnchor, ISectionedPage, IPageHistory
{
    internal static readonly string[] DefaultPanelOrder = { "setups", "hud", "fov", "iracing", "tradingpaints", "garage61" };

    private const string PageKey = "simracing";

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private readonly FovSection _fov;
    private IracingControllerSection? _controller;
    private CompanionAppSection? _tradingPaints, _garage61;
    private SetupManagerSection? _setups;
    private RaceHudSection? _hud;
    private readonly ScrollViewer _scroll;
    private readonly SectionView _view;
    private bool _shown;

    public event Action? SectionsChanged;

    public SimRacingPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        var scroll = _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body), Focusable = false };
        Ui.ChainWheel(scroll);
        Content = scroll;
        _view = new SectionView(() => _scroll);

        var heading = _view.Heading;
        heading.Margin = new Thickness(0, 0, 0, Ui.HeaderGap);
        _view.Empty = new SectionDef("", "Sim Racing Tools", "Every tool is turned off. Switch the ones you want on in Settings → Plugins.", "I.FormulaWheel",
            Hues.Tool(PluginIds.SimRacing, Hues.Accent), () => new Border());
        _body.Children.Add(heading);
        _body.Children.Add(_view.Content);

        _fov = new FovSection(app, window);
        _view.Add(Def("fov", "The correct field of view for your seat and screens, so speed, distance and apexes look real.",
            () => _fov, "simracing.fov"));
        _view.Add(Def("iracing", "Every iRacing setting with a plain-language description, a health check and backups.",
            () => _controller = new IracingControllerSection(_app, _window), "simracing.iracing"));
        _view.Add(Def("setups", "Your car setups by car: find, rename, move, import and share them with league mates without File Explorer.",
            () => _setups = new SetupManagerSection(_app, _window), "simracing.setups",
            () => _setups?.OnShown(), () => _setups?.OnHidden()));
        _view.Add(Def("hud", "",
            () => _hud = new RaceHudSection(_app, _window), "simracing.hud.panel",
            () => _hud?.OnShown(), () => _hud?.OnHidden()));
        _view.Add(Def("tradingpaints", "Start the official Trading Paints Downloader, by hand or automatically with Goodie Bag or iRacing.",
            () => _tradingPaints = new CompanionAppSection(_app, p => p.TradingPaints,
                "It downloads the custom paints of the cars around you so you see them in iRacing; it's free."), "simracing.tradingpaints",
            () => _tradingPaints?.OnShown(), () => _tradingPaints?.OnHidden()));
        _view.Add(Def("garage61", "Start the Garage 61 Telemetry Agent, which records your iRacing laps for garage61.net, by hand or automatically.",
            () => _garage61 = new CompanionAppSection(_app, p => p.Garage61,
                "It records your iRacing laps and telemetry and uploads them to garage61.net, to compare with your team and other drivers."), "simracing.garage61",
            () => _garage61?.OnShown(), () => _garage61?.OnHidden()));

        Layout(animate: false);
    }

    private static SectionDef Def(string id, string subtitle, Func<UIElement> build, string anchor, Action? shown = null, Action? hidden = null)
    {
        var t = Tool(id);
        return new SectionDef(id, t.Title, subtitle, t.Icon, t.Colors, build, anchor, shown, hidden);
    }

    private SimRacingSettings S => _app.Config.SimRacing;

    private static readonly PageSection[] Tools =
    {
        new("setups", "iRacing Setup Manager", "I.Folder", ("GoldSoft", "GoldDeep")),
        new("hud", "iRacing Overlay", "I.RaceOverlay", ("ApricotSoft", "ApricotDeep")),
        new("fov", "FOV calculator", "I.Compass", ("CoralSoft", "CoralDeep")),
        new("iracing", "iRacing Controller", "I.Keyboard", ("SlateSoft", "SlateDeep")),
        new("tradingpaints", "Trading Paints", "I.Palette", ("PlumSoft", "PlumDeep")),
        new("garage61", "Garage 61", "I.Activity", ("SageSoft", "SageDeep")),
    };

    private static List<string> OrderedFor(SimRacingSettings s)
    {
        int Slot(string id) { int i = s.PanelLayout.FindIndex(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }
        return DefaultPanelOrder.OrderBy(Slot).ThenBy(id => Array.IndexOf(DefaultPanelOrder, id)).ToList();
    }

    private static List<string> VisibleFor(SimRacingSettings s) => OrderedFor(s).Where(id => !s.IsPanelHidden(id)).ToList();

    private static List<PageSection> SectionsOf(SimRacingSettings s) => VisibleFor(s).Select(id => Tools.First(t => t.Id == id)).ToList();

    internal static SectionList SectionsFor(AppServices app)
    {
        var visible = VisibleFor(app.Config.SimRacing);
        string? saved = app.Config.Ui.PageSections.GetValueOrDefault(PageKey);
        return new SectionList(SectionsOf(app.Config.SimRacing), visible.FirstOrDefault(v => string.Equals(v, saved, StringComparison.OrdinalIgnoreCase)) ?? visible.FirstOrDefault());
    }

    private static PageSection Tool(string id) => Tools.First(t => t.Id == id);

    internal static IReadOnlyList<PageSection> ToolsInOrder(AppServices app) => OrderedFor(app.Config.SimRacing).Select(Tool).ToList();

    internal static bool IsToolOn(AppServices app, string id) => !app.Config.SimRacing.IsPanelHidden(id);

    internal static void SetToolOn(AppServices app, string id, bool on)
    {
        if (app.Config.SimRacing.IsPanelHidden(id) == !on) return;
        app.Config.SimRacing.SetPanelHidden(id, !on);
        app.Save();
        GoodieBag.Plugins.BuiltIn.SimRacingPlugin.PanelsChanged(app);
    }

    private List<string> Ordered() => OrderedFor(S);

    private List<string> Visible() => VisibleFor(S);

    public IReadOnlyList<PageSection> Sections => SectionsOf(S);

    public string? CurrentSection => _view.Current;

    private string? Saved => _app.Config.Ui.PageSections.GetValueOrDefault(PageKey);

    private void Layout(bool animate)
    {
        var visible = Visible();
        string? want = _view.Current is { } current && visible.Contains(current) ? current
            : Saved is { } saved && visible.Contains(saved, StringComparer.OrdinalIgnoreCase) ? visible.First(v => string.Equals(v, saved, StringComparison.OrdinalIgnoreCase))
            : visible.FirstOrDefault();
        _view.Show(want, animate && _shown);
        SectionsChanged?.Invoke();
    }

    public void ShowSection(string id)
    {
        if (!Visible().Contains(id, StringComparer.OrdinalIgnoreCase) || string.Equals(_view.Current, id, StringComparison.OrdinalIgnoreCase)) return;
        _view.Show(Visible().First(v => string.Equals(v, id, StringComparison.OrdinalIgnoreCase)), _shown);
        _app.Config.Ui.PageSections[PageKey] = _view.Current!;
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
        var slots = order.Select(id => S.PanelLayout.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? new PanelSlot { Id = id }).ToList();
        foreach (var id in Ordered().Where(id => !order.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            var slot = S.PanelLayout.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? new PanelSlot { Id = id };
            int at = Array.IndexOf(DefaultPanelOrder, id);
            slots.Insert(Math.Min(slots.Count, Math.Max(0, at)), slot);
        }
        S.PanelLayout.Clear();
        S.PanelLayout.AddRange(slots);
        _app.Save();
        SectionsChanged?.Invoke();
    }

    private void DefaultOrder()
    {
        S.PanelLayout.Clear();
        S.PanelLayout.AddRange(DefaultPanelOrder.Select(id => new PanelSlot { Id = id }));
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
        bool built = false;
        foreach (var id in Visible().Where(id => !_view.Built(id)))
        {
            if (HitchWatch.On) HitchWatch.Note = id;
            _view.Build(id);
            built = true;
        }
        return built;
    }

    public void OnShown()
    {
        Layout(animate: false);
        _shown = true;
        _view.SetActive(true);
        Motion.Stagger(_body);
        _view.BuildLater(Visible());
    }

    public void OnHidden()
    {
        _shown = false;
        _view.SetActive(false);
    }

    private void SetHidden(string id, bool hidden)
    {
        if (S.IsPanelHidden(id) == hidden) return;
        S.SetPanelHidden(id, hidden);
        _app.Save();
        Layout(animate: true);
        GoodieBag.Plugins.BuiltIn.SimRacingPlugin.PanelsChanged(_app);
    }

    public bool CancelEdit() => _hud?.CancelEdit() == true;

    internal ScrollViewer Scroll => _scroll;

    internal SetupManagerSection? Setups => _setups;

    internal FrameworkElement? PanelSection(string id) => _view.Host(id);

    private void Only(string id)
    {
        if (S.IsPanelHidden(id)) SetHidden(id, false);
        ShowSection(id);
        _body.UpdateLayout();
        _scroll.ScrollToTop();
    }

    private void WheelCheck()
    {
        Only("setups");
        var setups = _view.Host("setups")!;
        setups.UpdateLayout();
        var inner = Descendants(setups).OfType<ScrollViewer>().Where(v => v.IsVisible && v.ActualHeight > 40).ToList();
        if (inner.Count == 0) throw new InvalidOperationException("wheel check: no scroll areas in the Setup Manager");
        foreach (var v in inner)
        {
            _scroll.ScrollToTop();
            v.ScrollToTop();
            _scroll.UpdateLayout();
            bool own = v.ScrollableHeight > 0.5;
            double page = _scroll.VerticalOffset, before = v.VerticalOffset;
            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = PreviewMouseWheelEvent, Source = v };
            v.RaiseEvent(args);
            if (!args.Handled) v.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = MouseWheelEvent, Source = v });
            _scroll.UpdateLayout();
            bool pageMoved = _scroll.VerticalOffset > page + 1, innerMoved = v.VerticalOffset > before + 1;
            if (own ? pageMoved || !innerMoved : !pageMoved && _scroll.ScrollableHeight > 1)
                throw new InvalidOperationException($"wheel check: {(own ? "scrollable" : "non-scrollable")} area {v.ActualWidth:0}x{v.ActualHeight:0}: page moved {pageMoved}, area moved {innerMoved}");
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        string? start = _view.Current;
        Only("iracing");
        foreach (var (name, enter) in _controller?.ScreenshotStates() ?? Enumerable.Empty<(string, Func<Task>)>())
            yield return ("controller-" + name, async () => { await enter(); _scroll.ScrollToTop(); });
        yield return ("controller-narrow", () => { _body.MaxWidth = 760; _body.HorizontalAlignment = HorizontalAlignment.Left; _scroll.ScrollToTop(); return Task.CompletedTask; });
        yield return ("controller-wide", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); return Task.CompletedTask; });
        ComboBox? dropdown = null;
        yield return ("dropdown-open", async () =>
        {
            dropdown = Descendants(_view.Host("iracing")!).OfType<ComboBox>().FirstOrDefault(c => c.IsVisible);
            if (dropdown is not null) { dropdown.Focus(); dropdown.IsDropDownOpen = true; }
            await Task.Delay(400);
        });
        yield return ("menus-closed", () => { if (dropdown is not null) dropdown.IsDropDownOpen = false; return Task.CompletedTask; });
        Only("fov");
        foreach (var state in _fov.ScreenshotStates())
            yield return state;
        Only("setups");
        foreach (var state in _setups?.ScreenshotStates() ?? Enumerable.Empty<(string, Func<Task>)>())
            yield return state;
        yield return ("setups-narrow", () => { _body.MaxWidth = 760; _body.HorizontalAlignment = HorizontalAlignment.Left; _scroll.ScrollToTop(); return Task.CompletedTask; });
        yield return ("setups-wide", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); return Task.CompletedTask; });
        yield return ("hud", () => { Only("hud"); return Task.CompletedTask; });
        foreach (var state in _hud?.ScreenshotStates() ?? Enumerable.Empty<(string, Func<Task>)>())
            yield return state;
        yield return ("hud-narrow", async () =>
        {
            _body.MaxWidth = 760;
            _body.HorizontalAlignment = HorizontalAlignment.Left;
            if (_hud?.ScreenshotStates().FirstOrDefault(s => s.Name == "hud-standings") is { Enter: { } open }) await open();
        });
        yield return ("hud-wide", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); return Task.CompletedTask; });
        yield return ("tradingpaints", () => { Only("tradingpaints"); return Task.CompletedTask; });
        yield return ("garage61", () => { Only("garage61"); return Task.CompletedTask; });
        yield return ("companions-turned-off", () =>
        {
            SetHidden("tradingpaints", true);
            SetHidden("garage61", true);
            if (GoodieBag.Plugins.BuiltIn.SimRacingPlugin.FeatureOn(_app, "tradingpaints") || GoodieBag.Plugins.BuiltIn.SimRacingPlugin.FeatureOn(_app, "garage61"))
                throw new InvalidOperationException("A companion app stayed on with its panel hidden.");
            if (Sections.Any(s => s.Id is "tradingpaints" or "garage61") || _view.Current is "tradingpaints" or "garage61")
                throw new InvalidOperationException("A tool that was turned off is still in the sidebar.");
            return Task.CompletedTask;
        });
        yield return ("companions-turned-on", () =>
        {
            SetHidden("tradingpaints", false);
            SetHidden("garage61", false);
            if (!GoodieBag.Plugins.BuiltIn.SimRacingPlugin.FeatureOn(_app, "tradingpaints") || !GoodieBag.Plugins.BuiltIn.SimRacingPlugin.FeatureOn(_app, "garage61"))
                throw new InvalidOperationException("A companion app stayed off with its panel shown again.");
            return Task.CompletedTask;
        });
        yield return ("reordered", () =>
        {
            MoveSections(Visible().AsEnumerable().Reverse().ToList());
            if (Sections[0].Id != "garage61") throw new InvalidOperationException("Moving the tools didn't change their order.");
            return Task.CompletedTask;
        });
        yield return ("default-order", () =>
        {
            DefaultOrder();
            if (!Sections.Select(s => s.Id).SequenceEqual(DefaultPanelOrder)) throw new InvalidOperationException("Default order didn't put the tools back.");
            return Task.CompletedTask;
        });
        yield return ("wheel", () => { WheelCheck(); return Task.CompletedTask; });
        yield return ("back-to-start", () => { if (start is not null) ShowSection(start); _scroll.ScrollToTop(); return Task.CompletedTask; });
    }
}

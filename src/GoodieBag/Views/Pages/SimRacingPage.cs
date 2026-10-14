using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.Theme;
using GoodieBag.Plugins;
using GoodieBag.Plugins.BuiltIn;
using GoodieBag.SimRacing;

namespace GoodieBag.Views.Pages;

internal sealed class SimRacingPage : UserControl, IPage, IScreenshotStates, ICancelEdit
{
    internal static readonly string[] DefaultPanelOrder = { "setups", "hud", "fov", "iracing", "tradingpaints", "garage61" };

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private readonly FovSection _fov;
    private IracingControllerSection? _controller;
    private CompanionAppSection? _tradingPaints, _garage61;
    private SetupManagerSection? _setups;
    private RaceHudSection? _hud;
    private readonly Collapsible _fovSection, _controllerSection, _setupsSection, _hudSection;
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, PagePanel> _panels = new(StringComparer.OrdinalIgnoreCase);
    private readonly PanelGrid _grid;
    private readonly StackPanel _titles;
    private readonly Button _panelsButton, _doneButton;
    private readonly Tile _panelTools;
    private readonly CollapseAllButton _collapseAllButton;
    private readonly TextBlock _arrangeHint, _allHidden;
    private bool _shown, _arranging, _quiet;
    private ContextMenu? _panelsMenu;

    public SimRacingPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        _grid = new PanelGrid(app.Config.SimRacing.PanelLayout, app.Save)
        {
            MinColumns = 1,
            RowHeight = 1, Gap = 0, PanelSpacing = Ui.PanelSpacing,
            Dividers = true,
        };
        var scroll = _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body), Focusable = false };
        Ui.ChainWheel(scroll);
        Content = scroll;

        _titles = new StackPanel { Margin = new Thickness(0, 0, 0, Ui.HeaderGap) };
        var heading = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(right, Dock.Right);
        heading.Children.Add(right);
        var title = Ui.PluginTitle(Plugins.PluginIds.SimRacing, "Sim Racing Tools");
        title.HorizontalAlignment = HorizontalAlignment.Left;
        heading.Children.Add(title);
        _collapseAllButton = new CollapseAllButton();
        _collapseAllButton.Requested += fold => { if (fold) CollapseAll(); else ExpandAll(); };
        _panelTools = Ui.PanelTools("Turn tools on or off (a hidden panel is off everywhere, like a plugin), or arrange them", "simracing.panels", OpenPanelsMenu, _collapseAllButton, out _panelsButton);
        right.Children.Add(_panelTools);
        _doneButton = Ui.LabeledButton("I.Check", "Done", FinishArrange, "PrimaryButton");
        _doneButton.Margin = new Thickness(14, 0, 0, 0);
        _doneButton.VerticalAlignment = VerticalAlignment.Center;
        _doneButton.ToolTip = "Finish arranging; the panels that were open open again";
        _doneButton.Visibility = Visibility.Collapsed;
        right.Children.Add(_doneButton);
        var tools = _grid.TakeEditTools();
        tools.Margin = new Thickness(8, 0, 0, 0);
        tools.VerticalAlignment = VerticalAlignment.Center;
        right.Children.Add(tools);
        _titles.Children.Add(heading);
        _arrangeHint = new TextBlock
        {
            Text = "Drag a panel up or down to move it. They're folded while you arrange; press Done and the ones that were open open again (Esc puts the order back).",
            Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 4, 0, 0), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        _titles.Children.Add(_arrangeHint);
        _allHidden = new TextBlock
        {
            Text = "Every tool is turned off. Tick the ones you want under Panels at the top right.",
            Style = Ui.Res<Style>("Muted"), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap,
        };

        _fov = new FovSection(app, window);
        _fovSection = Section("fov", "FOV calculator", "The correct field of view for your seat and screens, so speed, distance and apexes look real.",
            "I.Triple", ("CoralSoft", "CoralDeep"), () => _fov);
        Ui.Anchor(_fovSection, "simracing.fov");
        Add("fov", "FOV calculator", _fovSection);

        _controllerSection = Section("iracing", "iRacing Controller", "Every iRacing setting with a plain-language description, a health check, the triple-screen layout and backups.",
            "I.Shield", ("SlateSoft", "SlateDeep"), () => _controller = new IracingControllerSection(_app, _window, () => S.Current));
        Ui.Anchor(_controllerSection, "simracing.iracing");
        Add("iracing", "iRacing Controller", _controllerSection);

        _setupsSection = Section("setups", "iRacing Setup Manager", "Your car setups by car: find, rename, move, import and share them with league mates without File Explorer.",
            "I.Folder", ("GoldSoft", "GoldDeep"), () => _setups = new SetupManagerSection(_app, _window));
        Ui.Anchor(_setupsSection, "simracing.setups");
        Add("setups", "iRacing Setup Manager", _setupsSection, () => { if (_setupsSection.Expanded) _setups?.OnShown(); }, () => _setups?.OnHidden());

        _hudSection = Section("hud", "iRacing Overlay", "In-game widgets from iRacing's live telemetry: Relative, Standings, fuel, delta, flags, inputs and more, in layouts that switch by session.",
            "I.Checkered", ("ApricotSoft", "ApricotDeep"), () => _hud = new RaceHudSection(_app, _window));
        Ui.Anchor(_hudSection, "simracing.hud.panel");
        Add("hud", "iRacing Overlay", _hudSection, () => { if (_hudSection.Expanded) _hud?.OnShown(); }, () => _hud?.OnHidden());

        var paints = Section("tradingpaints", "Trading Paints", "Start the official Trading Paints Downloader, by hand or automatically with Goodie Bag or iRacing.",
            "I.Palette", ("PlumSoft", "PlumDeep"), () => _tradingPaints = new CompanionAppSection(_app, p => p.TradingPaints,
                "It downloads the custom paints of the cars around you so you see them in iRacing; it's free."));
        Ui.Anchor(paints, "simracing.tradingpaints");
        Add("tradingpaints", "Trading Paints", paints, () => { if (paints.Expanded) _tradingPaints?.OnShown(); }, () => _tradingPaints?.OnHidden());

        var garage61 = Section("garage61", "Garage 61", "Start the Garage 61 Telemetry Agent, which records your iRacing laps for garage61.net, by hand or automatically.",
            "I.Activity", ("SageSoft", "SageDeep"), () => _garage61 = new CompanionAppSection(_app, p => p.Garage61,
                "It records your iRacing laps and telemetry and uploads them to garage61.net, to compare with your team and other drivers."));
        Ui.Anchor(garage61, "simracing.garage61");
        Add("garage61", "Garage 61", garage61, () => { if (garage61.Expanded) _garage61?.OnShown(); }, () => _garage61?.OnHidden());
        UseProgramIcon(paints, CompanionApp.TradingPaints, p => p.TradingPaints);
        UseProgramIcon(garage61, CompanionApp.Garage61, p => p.Garage61);

        _building = true;
        Layout();
        _building = false;
    }

    private bool _building;

    private void BuildPanels()
    {
        var waiting = Ordered().Where(p => !S.IsPanelHidden(p.Id) && p.Section.Deferred).ToList();
        for (int i = 0; i < waiting.Count; i++)
        {
            var panel = waiting[i];
            if (i == 0 || !_building) BuildPanel(panel);
            else Dispatcher.BeginInvoke(() => BuildPanel(panel), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void BuildPanel(PagePanel panel)
    {
        if (!S.IsPanelHidden(panel.Id) && panel.Section.BuildDeferred() && _shown) panel.Shown?.Invoke();
    }

    private SimRacingSettings S => _app.Config.SimRacing;

    public void OnShown()
    {
        _shown = true;
        foreach (var panel in _panels.Values)
            if (!S.IsPanelHidden(panel.Id)) panel.Shown?.Invoke();
        Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        if (_arranging) FinishArrange();
        _shown = false;
        foreach (var panel in _panels.Values)
            panel.Hidden?.Invoke();
    }

    private Collapsible Section(string id, string title, string subtitle, string icon, (string, string) colors, Func<UIElement> build, FrameworkElement? tools = null) =>
        new(title, subtitle, icon, colors, !S.Collapsed.Contains(id), build, open =>
        {
            if (!_quiet)
            {
                S.Collapsed.Remove(id);
                if (!open) S.Collapsed.Add(id);
                _app.Save();
            }
            if (!_quiet) SyncCollapseAll();
            if (id == "tradingpaints" && _shown) { if (open) _tradingPaints?.OnShown(); else _tradingPaints?.OnHidden(); }
            if (id == "garage61" && _shown) { if (open) _garage61?.OnShown(); else _garage61?.OnHidden(); }
            if (id == "setups" && _shown) { if (open) _setups?.OnShown(); else _setups?.OnHidden(); }
            if (id == "hud" && _shown) { if (open) _hud?.OnShown(); else _hud?.OnHidden(); }
        }, tools, framed: true, deferred: true);

    private async void UseProgramIcon(Collapsible section, CompanionSpec spec, Func<SimRacingPlugin, CompanionApp?> pick)
    {
        var companion = _app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin plugin ? pick(plugin) : null;
        if (await ProgramIcons.GetAsync(() => companion?.ExePath, spec.Site) is { } icon) section.SetBadgeImage(icon);
    }

    private sealed record PagePanel(string Id, string Title, Collapsible Section, int Order, Action? Shown, Action? Hidden);

    private void Add(string id, string title, Collapsible section, Action? shown = null, Action? hidden = null) =>
        _panels[id] = new PagePanel(id, title, section, Array.IndexOf(DefaultPanelOrder, id), shown, hidden);

    private List<PagePanel> Ordered()
    {
        int Slot(PagePanel p) { int i = S.PanelLayout.FindIndex(x => string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }
        return _panels.Values.OrderBy(Slot).ThenBy(p => p.Order).ToList();
    }

    private void Layout()
    {
        foreach (var panel in Ordered())
        {
            bool on = _grid.Contains(panel.Id), want = !S.IsPanelHidden(panel.Id);
            if (on && !want) _grid.Remove(panel.Id);
            else if (!on && want)
            {
                var section = panel.Section;
                _grid.Add(new PanelDef(panel.Id, panel.Title, _ => section) { FullWidth = true, Order = panel.Order });
            }
        }
        _body.Children.Clear();
        _body.Children.Add(_titles);
        _body.Children.Add(_panels.Keys.All(S.IsPanelHidden) ? _allHidden : _grid);
        _panelTools.Visibility = _arranging ? Visibility.Collapsed : Visibility.Visible;
        _doneButton.Visibility = _arranging ? Visibility.Visible : Visibility.Collapsed;
        _arrangeHint.Visibility = _arranging ? Visibility.Visible : Visibility.Collapsed;
        _grid.Editing = _arranging;
        SyncCollapseAll();
        BuildPanels();
    }

    private void OpenPanelsMenu(Button button)
    {
        var menu = _panelsMenu = new ContextMenu { PlacementTarget = _panelTools, Placement = PlacementMode.Bottom };
        MenuItem? showAll = null, arrange = null;
        void Refresh()
        {
            if (showAll is not null) showAll.IsEnabled = _panels.Keys.Any(S.IsPanelHidden);
            if (arrange is not null) arrange.IsEnabled = _panels.Keys.Count(id => !S.IsPanelHidden(id)) > 1;
        }
        var items = new List<(string Id, MenuItem Item)>();
        menu.Items.Add(new MenuItem
        {
            IsEnabled = false,
            Header = new TextBlock
            {
                Text = "Untick a tool to turn it off, like a plugin: its panel, keybinds and background work go until you tick it again. Settings are kept.",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 260,
            },
        });
        menu.Items.Add(new Separator());
        foreach (var panel in Ordered())
        {
            var item = new MenuItem
            {
                Header = panel.Title, IsCheckable = true, IsChecked = !S.IsPanelHidden(panel.Id), StaysOpenOnClick = true,
                ToolTip = $"Untick to turn {panel.Title} off everywhere in Goodie Bag",
            };
            var id = panel.Id;
            item.Click += (_, _) => { SetHidden(id, !item.IsChecked); Refresh(); };
            items.Add((id, item));
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        showAll = new MenuItem { Header = "Show all panels", StaysOpenOnClick = true };
        showAll.Click += (_, _) =>
        {
            foreach (var (id, item) in items)
            {
                if (S.IsPanelHidden(id)) SetHidden(id, false);
                item.IsChecked = true;
            }
            Refresh();
        };
        menu.Items.Add(showAll);
        arrange = new MenuItem { Header = "Arrange panels…" };
        arrange.Click += (_, _) => EnterArrange();
        menu.Items.Add(arrange);
        var reset = new MenuItem { Header = "Default order" };
        reset.Click += (_, _) => _grid.ResetLayout();
        menu.Items.Add(reset);
        Refresh();
        menu.IsOpen = true;
    }

    private void CollapseAll()
    {
        _controller?.CollapseAll();
        foreach (var panel in _panels.Values)
            if (panel.Section.Expanded) panel.Section.Toggle();
    }

    private void ExpandAll()
    {
        foreach (var panel in _panels.Values.Where(p => !S.IsPanelHidden(p.Id)))
            if (!panel.Section.Expanded) panel.Section.Toggle();
    }

    private void SyncCollapseAll(bool animate = true)
    {
        if (_collapseAllButton is null) return;
        _grid.InvalidateVisual();
        var shown = _panels.Values.Where(p => !S.IsPanelHidden(p.Id)).ToList();
        _collapseAllButton.SetOpen(shown.Count(p => p.Section.Expanded), shown.Count, animate);
    }

    private void SetHidden(string id, bool hidden)
    {
        if (S.IsPanelHidden(id) == hidden) return;
        S.SetPanelHidden(id, hidden);
        _app.Save();
        if (_shown && _panels.TryGetValue(id, out var panel))
        {
            if (hidden) panel.Hidden?.Invoke();
            else panel.Shown?.Invoke();
        }
        Layout();
        GoodieBag.Plugins.BuiltIn.SimRacingPlugin.PanelsChanged(_app);
    }

    private void EnterArrange()
    {
        if (_arranging) return;
        _arranging = true;
        _undo = new EditUndo().Slots(S.PanelLayout);
        _quiet = true;
        try
        {
            foreach (var panel in _panels.Values)
                if (panel.Section.Expanded) panel.Section.Toggle();
        }
        finally { _quiet = false; }
        Layout();
    }

    private EditUndo? _undo;

    public bool CancelEdit()
    {
        if (_hud?.CancelEdit() == true) return true;
        if (!_arranging) return false;
        var undo = _undo;
        FinishArrange();
        if (undo?.Restore(_app) == true) _grid.InvalidateMeasure();
        return true;
    }

    private void FinishArrange()
    {
        if (!_arranging) return;
        _arranging = false;
        _undo = null;
        _quiet = true;
        try
        {
            foreach (var panel in _panels.Values)
                if (panel.Section.Expanded == S.Collapsed.Contains(panel.Id)) panel.Section.Toggle();
        }
        finally { _quiet = false; }
        Layout();
    }

    internal ScrollViewer Scroll => _scroll;

    internal SetupManagerSection? Setups => _setups;

    internal Collapsible? PanelSection(string id) => _panels.TryGetValue(id, out var panel) ? panel.Section : null;

    private void OnlyOpen(FrameworkElement keep)
    {
        foreach (var panel in _panels.Values)
            if (panel.Section.Expanded != (panel.Section == keep)) panel.Section.Toggle();
        _scroll.ScrollToTop();
    }

    private void WheelCheck()
    {
        OnlyOpen(_setupsSection);
        _setupsSection.UpdateLayout();
        var inner = Descendants(_setupsSection).OfType<ScrollViewer>().Where(v => v.IsVisible && v.ActualHeight > 40).ToList();
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
            if (own ? pageMoved || !innerMoved : !pageMoved)
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
        if (!_controllerSection.Expanded) _controllerSection.Toggle();
        if (_fovSection.Expanded) _fovSection.Toggle();
        bool setupsWasOpen = _setupsSection.Expanded;
        if (setupsWasOpen) _setupsSection.Toggle();
        void ShowController() { _body.UpdateLayout(); _scroll.ScrollToVerticalOffset(Math.Max(0, _controllerSection.TranslatePoint(new Point(), _body).Y - 12)); }
        foreach (var (name, enter) in _controller?.ScreenshotStates() ?? Enumerable.Empty<(string, Func<Task>)>())
            yield return ("controller-" + name, async () => { await enter(); ShowController(); });
        yield return ("controller-narrow", () => { _body.MaxWidth = 760; _body.HorizontalAlignment = HorizontalAlignment.Left; ShowController(); return Task.CompletedTask; });
        yield return ("controller-wide", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); ShowController(); return Task.CompletedTask; });
        ComboBox? dropdown = null;
        yield return ("panels-menu", () => { _scroll.ScrollToTop(); OpenPanelsMenu(_panelsButton); return Task.CompletedTask; });
        yield return ("dropdown-open", async () =>
        {
            if (_panelsMenu is not null) _panelsMenu.IsOpen = false;
            ShowController();
            dropdown = Descendants(_controllerSection).OfType<ComboBox>().FirstOrDefault(c => c.IsVisible);
            if (dropdown is not null) { dropdown.Focus(); dropdown.IsDropDownOpen = true; }
            await Task.Delay(400);
        });
        yield return ("menus-closed", () => { if (dropdown is not null) dropdown.IsDropDownOpen = false; return Task.CompletedTask; });
        if (setupsWasOpen && !_setupsSection.Expanded) _setupsSection.Toggle();
        if (!_fovSection.Expanded) _fovSection.Toggle();
        if (_controllerSection.Expanded) _controllerSection.Toggle();
        foreach (var state in _fov.ScreenshotStates())
            yield return state;
        if (!_setupsSection.Expanded) _setupsSection.Toggle();
        foreach (var state in _setups?.ScreenshotStates() ?? Enumerable.Empty<(string, Func<Task>)>())
            yield return state;
        yield return ("setups-narrow", () => { _body.MaxWidth = 760; _body.HorizontalAlignment = HorizontalAlignment.Left; _scroll.ScrollToTop(); return Task.CompletedTask; });
        yield return ("setups-wide", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); return Task.CompletedTask; });
        yield return ("all-folded", () => { CollapseAll(); _scroll.ScrollToTop(); return Task.CompletedTask; });
        yield return ("one-open", () => { OnlyOpen(_setupsSection); return Task.CompletedTask; });
        yield return ("hud", () => { OnlyOpen(_hudSection); _hud?.OnShown(); return Task.CompletedTask; });
        foreach (var state in _hud?.ScreenshotStates() ?? Enumerable.Empty<(string, Func<Task>)>())
            yield return state;
        yield return ("hud-narrow", async () =>
        {
            _body.MaxWidth = 760;
            _body.HorizontalAlignment = HorizontalAlignment.Left;
            if (_hud?.ScreenshotStates().FirstOrDefault(s => s.Name == "hud-standings") is { Enter: { } open }) await open();
        });
        yield return ("hud-wide", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); return Task.CompletedTask; });
        yield return ("companions-turned-off", () =>
        {
            SetHidden("tradingpaints", true);
            SetHidden("garage61", true);
            if (GoodieBag.Plugins.BuiltIn.SimRacingPlugin.FeatureOn(_app, "tradingpaints") || GoodieBag.Plugins.BuiltIn.SimRacingPlugin.FeatureOn(_app, "garage61"))
                throw new InvalidOperationException("A companion app stayed on with its panel hidden.");
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
        yield return ("dividers", () =>
        {
            ExpandAll();
            _body.UpdateLayout();
            var tops = _panels.Values.Where(p => p.Section.IsVisible).Select(p => p.Section.TranslatePoint(new Point(), _body).Y).OrderBy(y => y).ToList();
            _scroll.ScrollToVerticalOffset(tops.Count > 1 ? tops[1] - 260 : 0);
            return Task.CompletedTask;
        });
        yield return ("wheel", () => { WheelCheck(); ExpandAll(); return Task.CompletedTask; });
        yield return ("arrange", () => { EnterArrange(); return Task.CompletedTask; });
        yield return ("arrange-done", () => { FinishArrange(); return Task.CompletedTask; });
    }
}

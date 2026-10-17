using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GoodieBag.Plugins;
using GoodieBag.Services;

namespace GoodieBag.Views.Pages;

internal sealed class HardwareMonitorPage : UserControl, IPage, IScreenshotStates, ICancelEdit, IRevealAnchor
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private readonly PanelStack _sectionHost = new();
    private readonly ScrollViewer _scroll;
    private readonly PerformanceSection _performance;
    private readonly SensorsSection _sensors;
    private OverlayPage? _overlay;
    private readonly Collapsible _performanceSection, _sensorsSection;
    private Collapsible? _overlaySection;
    private readonly Controls.CollapseAllButton _collapseAll = new();
    private readonly Controls.Tile _panelTools;
    private ArrangeList? _arrange;
    private bool _shown;

    internal static readonly string[] DefaultSections = { "overlay", "performance", "sensors" };

    public HardwareMonitorPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body) };
        Content = _scroll;

        var titles = new StackPanel { Margin = new Thickness(0, 0, 0, Ui.HeaderGap) };
        var heading = new DockPanel();
        _collapseAll.Requested += fold => { if (fold) CollapseAll(); else ExpandAll(); };
        _panelTools = Ui.PanelTools("Show, hide and arrange the sections", "monitor.panels", _ => OpenPanelsMenu(), _collapseAll, out _);
        DockPanel.SetDock(_panelTools, Dock.Right);
        heading.Children.Add(_panelTools);
        var title = Ui.PluginTitle(Plugins.PluginIds.HardwareMonitor, "Hardware Monitor");
        title.HorizontalAlignment = HorizontalAlignment.Left;
        heading.Children.Add(title);
        titles.Children.Add(heading);
        _body.Children.Add(titles);
        _body.Children.Add(_sectionHost);

        _performance = new PerformanceSection(app, () => window.GoTo("settings", "settings.performance"));
        _performanceSection = Section("performance", "Performance", "CPU, graphics card, memory, storage, network, motherboard, fans and frame rate, with the last two minutes on graphs.",
            "I.Activity", ("SageSoft", "SageDeep"), _performance, _performance.Tools);
        Ui.Anchor(_performanceSection, "monitor.performance");

        _sensors = new SensorsSection(app);
        _sensorsSection = Section("sensors", "Sensors", "Every reading with min and max, one card per device. Arrange moves the devices up and down.",
            "I.Cpu", ("SlateSoft", "SlateDeep"), _sensors, _sensors.Tools);

        if (app.Plugins.IsActive(PluginIds.Overlay)) BuildOverlay();
        FillSections();
        SyncCollapseAll(animate: false);
    }

    private void BuildOverlay()
    {
        _overlay = new OverlayPage(_app, _window, embedded: true);
        _overlaySection = Section("overlay", "Performance Overlay", "A small, click-through panel with FPS, temperatures and load on top of your game. Nothing is injected into the game, so it's safe with anti-cheat. Make several layouts and let each mode or game pick one.",
            "I.Overlay", ("ApricotSoft", "ApricotDeep"), _overlay, null);
        Ui.Anchor(_overlaySection, "monitor.overlay");
    }

    private bool OverlayOn => _app.Plugins.IsActive(PluginIds.Overlay) && !HiddenSections.Contains("overlay");

    private void SetOverlayOn(bool on)
    {
        if (_app.Plugins.Find(PluginIds.Overlay) is not { } plugin) return;
        bool wasOn = OverlayOn;
        HiddenSections.Remove("overlay");
        if (!on && _overlaySection is { } section && _overlay is { } overlay)
        {
            if (wasOn && _shown && section.Expanded) overlay.OnHidden();
            _overlay = null;
            _overlaySection = null;
        }
        _app.Plugins.SetEnabled(plugin, on);
        if (on && _overlaySection is null)
        {
            BuildOverlay();
            if (_shown && _overlaySection!.Expanded) _overlay!.OnShown();
        }
        else if (on && !wasOn && _shown && _overlaySection is { Expanded: true }) _overlay!.OnShown();
        FillSections();
        SyncCollapseAll();
    }

    private List<string> Order => _app.Config.Ui.MonitorSectionOrder;
    private List<string> HiddenSections => _app.Config.Ui.MonitorHiddenSections;

    private IEnumerable<(string Id, string Title, Collapsible Section, IPage Content)> Sections()
    {
        var all = new List<(string, string, Collapsible, IPage)> { ("performance", "Performance", _performanceSection, _performance), ("sensors", "Sensors", _sensorsSection, _sensors) };
        if (_overlaySection is not null && _overlay is not null) all.Add(("overlay", "Performance Overlay", _overlaySection, _overlay));
        int Rank(string id)
        {
            int i = Order.IndexOf(id);
            return i >= 0 ? i : Order.Count + Array.IndexOf(DefaultSections, id);
        }
        return all.OrderBy(s => Rank(s.Item1));
    }

    private IEnumerable<(string Id, string Title, Collapsible Section, IPage Content)> Visible() => Sections().Where(s => !HiddenSections.Contains(s.Id));

    private void FillSections()
    {
        _sectionHost.Children.Clear();
        if (_arrange is { Editing: true } arrange)
        {
            arrange.Fill();
            _sectionHost.Children.Add(arrange.Button);
            _sectionHost.Children.Add(new TextBlock { Text = "Drag the sections into the order you want, then press Done (Esc puts them back).", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 8) });
            _sectionHost.Children.Add(arrange.View);
            return;
        }
        foreach (var s in Visible())
            _sectionHost.Children.Add(s.Section);
        if (!Visible().Any())
            _sectionHost.Children.Add(new TextBlock { Text = "Every section is off. Switch them on again under Panels.", Style = Ui.Res<Style>("Muted") });
    }

    public bool Reveal(string anchor)
    {
        string? id = anchor is "monitor.performance" or "monitor.network" || anchor.StartsWith("performance.", StringComparison.Ordinal) ? "performance" : null;
        if (id is null || !HiddenSections.Contains(id)) return false;
        SetSectionHidden(id, false);
        return true;
    }

    private void SetSectionHidden(string id, bool hidden)
    {
        var s = Sections().First(x => x.Id == id);
        HiddenSections.Remove(id);
        if (hidden) HiddenSections.Add(id);
        _app.Save();
        if (_shown && s.Section.Expanded)
        {
            if (hidden) s.Content.OnHidden();
            else s.Content.OnShown();
        }
        FillSections();
        SyncCollapseAll();
    }

    private void OpenPanelsMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _panelTools, Placement = PlacementMode.Bottom, MinWidth = 260 };
        MenuItem Toggle(string label, bool on, Action<bool> set)
        {
            var line = new DockPanel { LastChildFill = true, MinWidth = 220 };
            var sw = Ui.Switch(on, _ => { });
            sw.IsHitTestVisible = false;
            sw.Focusable = false;
            DockPanel.SetDock(sw, Dock.Right);
            line.Children.Add(sw);
            line.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var item = new MenuItem { Header = line, StaysOpenOnClick = true };
            item.Click += (_, _) => { on = !on; sw.IsChecked = on; set(on); };
            return item;
        }
        menu.Items.Add(new MenuItem
        {
            IsEnabled = false,
            Header = new TextBlock
            {
                Text = "A section that's off reads nothing in the background. Turning off Performance Overlay also stops the overlay and its keybinds. Settings are kept.",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 260,
            },
        });
        menu.Items.Add(new Separator());
        foreach (var s in Sections().Where(s => s.Id != "overlay").ToList())
        {
            var id = s.Id;
            var item = Toggle(s.Title, !HiddenSections.Contains(id), on => SetSectionHidden(id, !on));
            item.ToolTip = $"Off: {s.Title} stops reading sensors until it's switched on again";
            menu.Items.Add(item);
        }
        if (_app.Plugins.Find(PluginIds.Overlay) is not null)
        {
            var overlay = Toggle("Performance Overlay", OverlayOn, on => { menu.IsOpen = false; SetOverlayOn(on); });
            overlay.ToolTip = "Off: the Performance Overlay plugin is turned off everywhere in Goodie Bag (also under Settings → Plugins)";
            menu.Items.Add(overlay);
        }
        menu.Items.Add(new Separator());
        var arrange = new MenuItem { Header = "Arrange sections…", IsEnabled = Visible().Count() > 1 && _arrange is not { Editing: true } };
        arrange.Click += (_, _) => StartArrange();
        menu.Items.Add(arrange);
        menu.IsOpen = true;
    }

    private void StartArrange()
    {
        _arrange = new ArrangeList(
            () => Visible().Select(s => (s.Id, s.Title, (Func<FrameworkElement>)(() => ArrangeCard(s.Title)))).ToList(),
            () => Sections().Select(s => s.Id).ToList(),
            order =>
            {
                Order.Clear();
                Order.AddRange(order.Concat(Sections().Select(s => s.Id).Where(id => !order.Contains(id))));
                _app.Save();
            }, "Arrange", "Drag the sections into a new order");
        _arrange.EditingChanged += on =>
        {
            if (!on) { _arrange = null; FillSections(); }
        };
        _arrange.Button.Margin = new Thickness(0, 0, 0, 10);
        _arrange.Button.HorizontalAlignment = HorizontalAlignment.Left;
        _arrange.SetEditing(true);
        FillSections();
    }

    private static FrameworkElement ArrangeCard(string title) => new Controls.Tile
    {
        Style = Ui.Res<Style>("Card"), Padding = new Thickness(16, 12, 16, 12),
        Child = new TextBlock { Text = title, Style = Ui.Res<Style>("H2") },
    };

    private void SyncCollapseAll(bool animate = true)
    {
        var visible = Visible().ToList();
        _collapseAll.SetOpen(visible.Count(s => s.Section.Expanded), Math.Max(1, visible.Count), animate);
    }

    private void ExpandAll()
    {
        _sensors.ExpandAll();
        foreach (var s in Visible().ToList())
            if (!s.Section.Expanded) s.Section.Toggle();
    }

    private List<string> Collapsed => _app.Config.Ui.MonitorCollapsed;

    private void CollapseAll()
    {
        _sensors.CollapseAll();
        foreach (var s in Visible().ToList())
            if (s.Section.Expanded) s.Section.Toggle();
    }

    private Collapsible Section(string id, string title, string subtitle, string icon, (string, string) colors, IPage content, FrameworkElement? tools) =>
        new(title, subtitle, icon, colors, !Collapsed.Contains(id), () => (UIElement)content, open =>
        {
            Collapsed.Remove(id);
            if (!open) Collapsed.Add(id);
            _app.Save();
            SyncCollapseAll();
            if (!_shown || HiddenSections.Contains(id)) return;
            if (open) content.OnShown();
            else content.OnHidden();
        }, tools, eager: true, framed: true);

    internal Collapsible Sensors => _sensorsSection;

    public void OnShown()
    {
        _shown = true;
        foreach (var s in Visible())
            if (s.Section.Expanded) s.Content.OnShown();
        Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _shown = false;
        foreach (var s in Visible())
            if (s.Section.Expanded) s.Content.OnHidden();
    }

    public bool CancelEdit()
    {
        if (_arrange?.Cancel() == true) return true;
        return _sensors.CancelEdit();
    }

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

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        if (!_performanceSection.Expanded) _performanceSection.Toggle();
        for (int page = 2; page <= 5 && _scroll.VerticalOffset + _scroll.ViewportHeight < _scroll.ExtentHeight - 8; page++)
            yield return ($"dashboard-{page}", () =>
            {
                _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + _scroll.ViewportHeight - 60);
                return Task.CompletedTask;
            });
        yield return ("sensors", () =>
        {
            _scroll.ScrollToHome();
            if (_performanceSection.Expanded) _performanceSection.Toggle();
            return Task.CompletedTask;
        });
        foreach (var state in _sensors.ScreenshotStates())
            yield return state;
        yield return ("both-open", () =>
        {
            if (!_performanceSection.Expanded) _performanceSection.Toggle();
            return Task.CompletedTask;
        });
        if (_overlaySection is not null && _overlay is not null)
        {
            yield return ("overlay", () =>
            {
                if (!_overlaySection.Expanded) _overlaySection.Toggle();
                _overlaySection.BringIntoView();
                return Task.CompletedTask;
            });
            foreach (var state in _overlay.ScreenshotStates())
                yield return ("overlay-" + state.Name, state.Enter);
        }
        yield return ("sections-arrange", () => { StartArrange(); _scroll.ScrollToHome(); return Task.CompletedTask; });
        yield return ("sections-arrange-hide", () => { if (_arrange is not { Editing: true }) StartArrange(); SetSectionHidden("sensors", true); return Task.CompletedTask; });
        yield return ("sections-arrange-show", () =>
        {
            SetSectionHidden("sensors", false);
            if (_arrange?.Button.Parent is null) throw new InvalidOperationException("The Done button went missing while arranging.");
            return Task.CompletedTask;
        });
        yield return ("sections-done", () => { _arrange?.SetEditing(false); return Task.CompletedTask; });
        yield return ("sections-folded", () =>
        {
            foreach (var x in Visible().ToList()) if (x.Section.Expanded) x.Section.Toggle();
            return Task.CompletedTask;
        });
        yield return ("sections-unfolded", () =>
        {
            foreach (var x in Visible().ToList()) if (!x.Section.Expanded) x.Section.Toggle();
            return Task.CompletedTask;
        });
        yield return ("sensors-hidden", () => { SetSectionHidden("sensors", true); _scroll.ScrollToHome(); return Task.CompletedTask; });
        yield return ("sensors-shown", () => { SetSectionHidden("sensors", false); return Task.CompletedTask; });
        yield return ("reentered", async () =>
        {
            _scroll.ScrollToHome();
            _window.Navigate("plugins");
            await Task.Delay(400);
            _window.Navigate("monitor");
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            double Measured() => _performanceSection.ActualHeight + _sensorsSection.ActualHeight;
            double first = Measured();
            var before = Heights(_performanceSection);
            await Task.Delay(1500);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            if (Math.Abs(Measured() - first) > 2)
            {
                var after = Heights(_performanceSection);
                var grew = before.Where(b => after.TryGetValue(b.Key, out var h) && Math.Abs(h - b.Value) > 2).Select(b => $"{Label(b.Key)} {b.Value:0}→{after[b.Key]:0}").Take(8);
                throw new InvalidOperationException($"Hardware Monitor changed height after opening: {first:0} → {Measured():0} px; grew: {string.Join("; ", grew)}");
            }
        });
    }
}

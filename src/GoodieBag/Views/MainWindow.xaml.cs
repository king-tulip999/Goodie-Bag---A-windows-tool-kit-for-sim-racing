using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using GoodieBag.Controls;
using GoodieBag.Plugins;
using GoodieBag.Services;
using GoodieBag.Theme;
using GoodieBag.Views.Pages;

namespace GoodieBag.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _app;

    internal TopographyLayer Topography { get; }

    internal AppServices Services => _app;
    private const string PageConsumer = "Open page";
    private readonly Dictionary<string, string> _nav = new();
    private readonly Dictionary<string, Func<FrameworkElement>> _factories = new();
    private readonly Dictionary<string, FrameworkElement> _pages = new();
    internal FrameworkElement? PageOf(string key) => _pages.GetValueOrDefault(key);
    private long _lastNavigation;
    private string? _current;

    public string? CurrentPage => _current;
    private bool _themeRebuild;
    private TaskCompletionSource<bool>? _modal;

    internal MainWindow(AppServices app)
    {
        _app = app;
        InitializeComponent();
        SidebarHost.Content = _sidebar;
        _sidebar.PageClicked += Navigate;
        _sidebar.SectionClicked += OpenSection;
        _sidebar.PagesMoved += keys => SetNavOrder(keys);
        _sidebar.SectionsMoved += (key, order) => { if (_nav.ContainsKey(key) && EnsurePage(key) is ISectionedPage page) page.MoveSections(order); };
        _searchButton = Ui.IconButton("I.Search", "Search settings and keybinds (Ctrl+F)", OpenSearch);
        System.Windows.Automation.AutomationProperties.SetName(_searchButton, "Search");
        TextFit.TipWhenTrimmed(QuoteText);
        QuoteBar.MouseLeftButtonDown += OnQuoteBarDown;
        _quoteTimer.Tick += (_, _) => NextQuote(animate: true);
        ApplySidebarSide();
        ApplyInterfaceScale();
        Width = 1280 + SidebarDips(_app.Config.Ui.ScaleFactor);
        Topmost = _app.Config.Ui.AlwaysOnTop;
        ApplySavedBounds();
        LocationChanged += (_, _) => RememberBounds();
        SizeChanged += (_, _) => RememberBounds();
        PageArea.SizeChanged += (_, _) => { if (ModalLayer.Visibility == Visibility.Visible) PlaceModal(); };
        StateChanged += (_, _) => RememberBounds();

        Topography = new TopographyLayer(_app, this, TopoLayer, TrackDecor);
        BuildNav();
        FitBrand();
        ApplyThemeIcon();
        _app.Plugins.Changed += OnPluginsChanged;
        ThemeManager.Changed += OnThemeChanged;
        IsVisibleChanged += (_, _) => { UpdatePageSensors(); UpdatePageActive(); ScheduleTrim(); UpdateQuoteTimer(); };
        _trimTimer.Tick += (_, _) => { _trimTimer.Stop(); if (!OnScreen) TrimWhileHidden(); };

        MinButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaxButton.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        CloseButton.Click += (_, _) => Close();
        SetupMinButton.Click += (_, _) => WindowState = WindowState.Minimized;
        SetupCloseButton.Click += (_, _) => Close();
        StateChanged += (_, _) => { OnStateChanged(); UpdatePageSensors(); UpdatePageActive(); ScheduleTrim(); UpdateQuoteTimer(); };
        TitleBar.SizeChanged += (_, _) => FitThemeLogo();
        ThemeLogo.SizeChanged += (_, _) => FitThemeLogo();
        Loaded += (_, _) => FitThemeLogo();
        SourceInitialized += (_, _) =>
        {
            SquareCorners();
            ApplyBackdrop();
            (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WatchSizeMove);
            ApplyLight();
            Topography.Refresh();
        };
        _app.LightChanged += ApplyLight;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnMouseHistoryButton;
        KeyDown += OnEscapeEdit;
        _app.IsWindowVisible = () => IsVisible && WindowState != WindowState.Minimized;
        _app.Notice += (title, message, kind) => ShowToast(title, message, kind);
        _app.Updates.Changed += UpdateBadgeState;
        UpdateBadge.Click += (_, _) => _ = _app.Updates.InstallAsync();

        Activated += (_, _) => Motion.SetWindowActive(true);
        Deactivated += (_, _) => Motion.SetWindowActive(false);

        Loaded += (_, _) =>
        {
            PerfProbe.Mark("loaded");
            if (_current is null) Navigate(_app.Config.Ui.StartPage);
            PerfProbe.Mark("navigated");
            if (_app.Config.Ui.SetupPending) ShowSetup(firstRun: true);
            else if (_app.Updates.JustUpdated)
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => ChangelogDialog.Show(this, _app.Updates, _app.Updates.UpdatedFrom, justUpdated: true));
        };
    }

    private static (string Page, string Anchor)? Moved(string? key) => key switch
    {
        "overlay" => ("monitor", "monitor.overlay"),
        "automations" or "modes" or "launcher" => ("windows", "windows.auto"),
        _ => null,
    };

    public void Navigate(string key)
    {
        if (Moved(key) is { } moved)
        {
            GoTo(moved.Page, moved.Anchor, flash: false);
            return;
        }
        if (key is null || !_nav.ContainsKey(key))
            key = key is not null && key.Length == 0 && _app.Config.Ui.LastPage is { } last && _nav.ContainsKey(last) ? last : FirstPage;
        if (_current == key)
        {
            SyncSidebar();
            return;
        }

        DeactivatePage();
        var page = EnsurePage(key);

        _current = key;
        Show(page);
        WatchSections(page as ISectionedPage);
        SyncSidebar();
        if (_app.Config.Ui.LastPage != key)
        {
            _app.Config.Ui.LastPage = key;
            if (!ScreenshotRunner.Active) _app.Save();
        }

        UpdatePageSensors();
        UpdatePageActive();
        page.UpdateLayout();
        if (!_themeRebuild) Swipe();
        RecordHistory();
    }

    private FrameworkElement EnsurePage(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = _factories[key]();
            _pages[key] = page;
        }
        return page;
    }

    internal string FirstPage => _nav.Keys.FirstOrDefault(k => k != "settings") ?? "settings";

    private readonly Sidebar _sidebar = new();

    internal FrameworkElement SidebarView => _sidebar;

    private ISectionedPage? _watched;

    private ISectionedPage? CurrentSectioned => _current is not null ? _pages.GetValueOrDefault(_current) as ISectionedPage : null;

    private void WatchSections(ISectionedPage? page)
    {
        if (ReferenceEquals(page, _watched)) return;
        if (_watched is not null) _watched.SectionsChanged -= OnSectionsChanged;
        _watched = page;
        if (page is not null) page.SectionsChanged += OnSectionsChanged;
    }

    private void OnSectionsChanged()
    {
        SyncSidebar();
        RecordHistory();
    }

    private void SyncSidebar(bool animate = true) => _sidebar.Select(_current, SectionsOf, _app.Config.Ui.SidebarAllOpen, animate && !_themeRebuild);

    internal void RefreshSidebar() => SyncSidebar();

    private SectionList? SectionsOf(string key)
    {
        if (_pages.GetValueOrDefault(key) is ISectionedPage page) return new SectionList(page.Sections, page.CurrentSection, page.SectionsMovable);
        if (key == "settings") return SettingsPage.SectionsFor(_app);
        return _app.Plugins.PageOwner(key) is { Page.Sections: { } sections } ? sections(_app) : null;
    }

    internal void OpenSection(string key, string section)
    {
        if (!_nav.ContainsKey(key)) return;
        if (_current != key && EnsurePage(key) is ISectionedPage waiting) waiting.ShowSection(section);
        Navigate(key);
        CurrentSectioned?.ShowSection(section);
    }

    internal static double SidebarDips(double scale) => (Sidebar.PanelWidth + 16) * scale;

    internal const double BaseMinWidth = 960;

    internal void ApplySidebarSide()
    {
        bool right = _app.Config.Ui.SidebarRight;
        Grid.SetColumn(SidebarHost, right ? 2 : 0);
        SidebarHost.Margin = right ? new Thickness(0, 14, 16, 16) : new Thickness(16, 14, 0, 16);
        _sidebar.SetSide(right);
    }

    private void StepPage(int by)
    {
        var keys = _nav.Keys.ToList();
        if (keys.Count == 0) return;
        int at = _current is null ? -1 : keys.IndexOf(_current);
        Navigate(keys[((at + by) % keys.Count + keys.Count) % keys.Count]);
    }

    private void StepSection(int by)
    {
        if (CurrentSectioned is not { } page || page.Sections.Count < 2) return;
        var ids = page.Sections.Select(s => s.Id).ToList();
        int at = page.CurrentSection is { } id ? ids.FindIndex(i => string.Equals(i, id, StringComparison.OrdinalIgnoreCase)) : -1;
        page.ShowSection(ids[((at + by) % ids.Count + ids.Count) % ids.Count]);
    }

    private readonly List<(string Page, object? State)> _history = new();
    private int _historyAt = -1;
    private bool _walkingHistory;

    internal void RecordHistory()
    {
        if (_walkingHistory || _current is null) return;
        var state = (_pages.GetValueOrDefault(_current) as IPageHistory)?.HistoryState;
        if (_historyAt >= 0 && _history[_historyAt].Page == _current && Equals(_history[_historyAt].State, state)) return;
        _history.RemoveRange(_historyAt + 1, _history.Count - _historyAt - 1);
        _history.Add((_current, state));
        if (_history.Count > 50) _history.RemoveAt(0);
        _historyAt = _history.Count - 1;
    }

    private void StepHistory(int by)
    {
        int at = _historyAt + by;
        if (at < 0 || at >= _history.Count) return;
        _historyAt = at;
        var (key, state) = _history[at];
        _walkingHistory = true;
        try
        {
            Navigate(key);
            if (_current == key && _pages.GetValueOrDefault(key) is IPageHistory page && !Equals(page.HistoryState, state))
                page.RestoreHistory(state);
        }
        finally { _walkingHistory = false; }
    }

    private void OnMouseHistoryButton(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2) || _modal is not null || SetupOpen || Mouse.Captured is not null) return;
        e.Handled = true;
        StepHistory(e.ChangedButton == MouseButton.XButton1 ? -1 : 1);
    }

    private static readonly BlurEffect SwipeBlur = new() { Radius = 0, RenderingBias = RenderingBias.Performance };

    private EventHandler? _pendingSwipe;

    private void Swipe()
    {
        long now = Environment.TickCount64;
        bool quick = now - _lastNavigation < 300;
        _lastNavigation = now;
        if (_pendingSwipe is not null)
        {
            CompositionTarget.Rendering -= _pendingSwipe;
            _pendingSwipe = null;
        }
        PageContent.BeginAnimation(OpacityProperty, null);
        PageMove.BeginAnimation(TranslateTransform.YProperty, null);
        SwipeBlur.BeginAnimation(BlurEffect.RadiusProperty, null);
        PageContent.Effect = null;
        PageContent.Opacity = 1;
        PageMove.Y = 0;
        Motion.PageSettlesAt = 0;
        if (!Motion.Enabled || Showcase.ShowcaseRunner.Active)
            return;
        bool effects = PcClass.Effects && !quick;
        double fromY = quick ? -12 : -48, fromOpacity = quick ? 0.7 : 0.35;
        PageMove.Y = fromY;
        if (PcClass.Effects) PageContent.Opacity = fromOpacity;
        if (effects)
        {
            SwipeBlur.Radius = 8;
            PageContent.Effect = SwipeBlur;
        }
        if (!ScreenshotRunner.Active) (Motion.PageAnimatesFrom, Motion.PageSettlesAt) = (long.MaxValue / 2, now + 2000);
        int frames = 0;
        _pendingSwipe = (_, _) =>
        {
            if (++frames < 2) return;
            CompositionTarget.Rendering -= _pendingSwipe;
            _pendingSwipe = null;
            StartSwipe(quick, effects);
        };
        CompositionTarget.Rendering += _pendingSwipe;
    }

    private void StartSwipe(bool quick, bool effects)
    {
        long now = Environment.TickCount64;
        if (!ScreenshotRunner.Active) (Motion.PageAnimatesFrom, Motion.PageSettlesAt) = (now, now + (quick ? 130 : 460));
        PageMove.Y = 0;
        PageContent.Opacity = 1;
        if (quick)
        {
            var short_ = TimeSpan.FromMilliseconds(130);
            if (PcClass.Effects) PageContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.7, 1, short_) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
            PageMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, 0, short_) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
            return;
        }
        var fade = TimeSpan.FromMilliseconds(240);
        if (PcClass.Effects) PageContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, fade) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
        PageMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-48, 0, TimeSpan.FromMilliseconds(460)) { EasingFunction = PageDrop, FillBehavior = FillBehavior.Stop });
        if (!effects) return;
        SwipeBlur.Radius = 0;
        var blur = new DoubleAnimation(8, 0, fade) { EasingFunction = Motion.Ease };
        blur.Completed += (_, _) =>
        {
            if (PageContent.Effect == SwipeBlur && SwipeBlur.Radius == 0)
                PageContent.Effect = null;
        };
        SwipeBlur.BeginAnimation(BlurEffect.RadiusProperty, blur);
    }

    private static readonly IEasingFunction PageDrop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 };

    private bool _pageActive;

    private bool OnScreen => IsVisible && WindowState != WindowState.Minimized;

    private void UpdatePageActive()
    {
        if (OnScreen && !_pageActive && _current is not null && _pages.GetValueOrDefault(_current) is IPage page)
        {
            _pageActive = true;
            page.OnShown();
        }
        else if (!OnScreen)
            DeactivatePage();
    }

    private void DeactivatePage()
    {
        if (_pageActive && _current is not null && _pages.GetValueOrDefault(_current) is IPage page)
            page.OnHidden();
        if (_current is not null && _pages.GetValueOrDefault(_current) is DependencyObject leaving)
            ClosePopupsOf(leaving);
        _pageActive = false;
    }

    private static void ClosePopupsOf(DependencyObject page)
    {
        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            if (source.RootVisual is not FrameworkElement { IsVisible: true } root || root.GetType().Name != "PopupRoot") continue;
            if (root.Parent is not System.Windows.Controls.Primitives.Popup popup) continue;
            if (popup.Child is ContextMenu { PlacementTarget: { } menuTarget } menu)
            {
                if (menuTarget.IsDescendantOf(page)) menu.IsOpen = false;
            }
            else if (popup.Child is not System.Windows.Controls.ToolTip && popup.PlacementTarget is { } target && target.IsDescendantOf(page)
                     && !System.Windows.Data.BindingOperations.IsDataBound(popup, System.Windows.Controls.Primitives.Popup.IsOpenProperty))
                popup.IsOpen = false;
        }
    }

    internal IReadOnlyList<string> NavKeys => _nav.Keys.ToList();

    private void BuildNav()
    {
        _nav.Clear();
        _factories.Clear();
        void Item(string key, string title, Func<FrameworkElement> create)
        {
            _nav[key] = title;
            _factories[key] = create;
        }

        var order = _app.Config.Ui.NavOrder;
        foreach (var page in _app.Plugins.Active.SelectMany(p => p.Pages).OrderBy(p => order.IndexOf(p.Key) is var i && i >= 0 ? i : int.MaxValue))
        {
            var p = page;
            Item(p.Key, p.Title, () => p.Create(_app, this));
        }
        Item("settings", "Settings", () => new SettingsPage(_app, this));
        var titles = _nav.Select(n => (n.Key, n.Value)).ToList();
        _app.NavPages = () => titles;
        BuildSidebar();
    }

    private void BuildSidebar()
    {
        var keys = _nav.Keys.ToList();
        var pages = new List<SidebarPage>();
        foreach (var plugin in _app.Plugins.Active)
            foreach (var page in plugin.Pages.Where(p => _nav.ContainsKey(p.Key)))
                pages.Add(new SidebarPage(page.Key, page.Title, page.Icon, Hues.Tool(plugin.Id, plugin.Colors)));
        pages = pages.OrderBy(p => keys.IndexOf(p.Key)).ToList();
        _sidebar.Build(pages, new SidebarPage("settings", "Settings", "I.Tune", Hues.Accent), _searchButton, _searchHost, ThemeButton());
        _sidebar.SetSide(_app.Config.Ui.SidebarRight);
        SyncSidebar(animate: false);
    }

    private Button? ThemeButton()
    {
        if (Themes.Variant(ThemeManager.Current) is { } variant)
            return Ui.IconButton(ThemeManager.Current.Dark ? "I.Sun" : "I.Moon", $"Switch to {variant.Name}", () => _app.SetTheme(variant.Id));
        if (Themes.OledSibling(ThemeManager.Current) is { } sibling)
            return Ui.IconButton(sibling.Id == Config.AppTheme.OledPastel ? "I.Palette" : "I.Sparkles", $"Switch to {sibling.Name}", () => _app.SetTheme(sibling.Id));
        return null;
    }

    internal void SetNavOrder(IReadOnlyList<string> keys)
    {
        _app.Config.Ui.NavOrder = keys.Concat(_app.Config.Ui.NavOrder.Where(k => !keys.Contains(k))).ToList();
        _app.Save();
        BuildNav();
    }

    private void OnPluginsChanged()
    {
        DropPages(keepCurrent: true);
        BuildNav();
        if (_current is not null && !_nav.ContainsKey(_current))
            Navigate(FirstPage);
        foreach (var key in _pages.Keys.Where(k => !_nav.ContainsKey(k)).ToList())
        {
            if (key == _current)
                DeactivatePage();
            RemovePage(key);
        }
        UpdatePageSensors();
    }

    private void ApplyLight()
    {
        if (PresentationSource.FromVisual(this) is HwndSource { CompositionTarget: { } target })
            target.RenderMode = _app.Light ? RenderMode.SoftwareOnly : RenderMode.Default;
        Motion.Refresh();
    }

    internal void ApplyInterfaceScale(bool rebuild = false)
    {
        double scale = _app.Config.Ui.ScaleFactor;
        Root.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
        MinWidth = BaseMinWidth + SidebarDips(scale);
        if (rebuild) Dispatcher.BeginInvoke(new Action(OnThemeChanged), DispatcherPriority.Background);
    }

    private void ApplyThemeIcon()
    {
        try
        {
            var spec = ThemeManager.Current;
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(spec.WindowIcon
                ?? new Uri(Themes.BuiltIn.Contains(spec) ? $"pack://application:,,,/Assets/Themes/GoodieBag-{spec.Id}.ico" : "pack://application:,,,/Assets/GoodieBag.ico"));
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }

    private void OnThemeChanged()
    {
        TitleRule.InvalidateVisual();
        FitBrand();
        ApplyThemeIcon();
        ApplyBackdrop();
        string? key = _current;
        double offset = key is not null && _pages.GetValueOrDefault(key) is UserControl { Content: ScrollViewer sv } ? sv.VerticalOffset : 0;
        if (Showcase.ShowcaseRunner.Active)
        {
            DeactivatePage();
            if (key is not null) RemovePage(key);
        }
        else DropPages(keepCurrent: false);
        BuildNav();
        if (key is null)
            return;
        _current = null;
        _themeRebuild = true;
        try { Navigate(key); }
        finally { _themeRebuild = false; }
        if (offset > 0 && _pages.GetValueOrDefault(key) is UserControl { Content: ScrollViewer fresh })
            Dispatcher.BeginInvoke(new Action(() => fresh.ScrollToVerticalOffset(offset)), DispatcherPriority.Loaded);
    }

    private readonly DispatcherTimer _trimTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };

    private void ScheduleTrim()
    {
        _trimTimer.Stop();
        if (!OnScreen && !ScreenshotRunner.Active) _trimTimer.Start();
    }

    internal void TrimWhileHidden()
    {
        DropPages(keepCurrent: true);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        try { using var me = System.Diagnostics.Process.GetCurrentProcess(); SetProcessWorkingSetSize(me.Handle, -1, -1); }
        catch (Exception ex) { App.LogError(ex); }
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    private void DropPages(bool keepCurrent)
    {
        foreach (var key in _pages.Keys.ToList())
        {
            if (keepCurrent && key == _current)
                continue;
            if (key == _current)
                DeactivatePage();
            RemovePage(key);
        }
    }

    private void Show(FrameworkElement page)
    {
        PageContent.Content = page;
        page.UpdateLayout();
    }

    internal bool Showing(FrameworkElement page) => ReferenceEquals(PageContent.Content, page);

    private void RemovePage(string key)
    {
        if (_pages.Remove(key, out var page) && ReferenceEquals(PageContent.Content, page)) PageContent.Content = null;
    }

    private void UpdatePageSensors()
    {
        var need = PageSensors.None;
        if (_current is not null && IsVisible && WindowState != WindowState.Minimized && _app.Plugins.PageOwner(_current) is { } owner && owner.Plugin.IsActive)
            need = owner.Page.Sensors;
        if (need == PageSensors.None)
            _app.Hardware.Release(PageConsumer);
        else
            _app.Hardware.Acquire(PageConsumer, fullTree: need == PageSensors.Full);
    }

    internal void GoTo(string page, string? anchor, bool flash = true)
    {
        if (Moved(page) is { } moved)
        {
            flash &= anchor is not null;
            (page, anchor) = (moved.Page, anchor ?? moved.Anchor);
        }
        Navigate(page);
        if (anchor is null || _current != page)
            return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (PageFor(page) is IRevealAnchor hidden && Ui.FindAnchor((DependencyObject)hidden, anchor) is null && hidden.Reveal(anchor))
            {
                GoTo(page, anchor, flash);
                return;
            }
            if (PageFor(page) is { } p && Ui.FindAnchor(p, anchor) is { } target)
            {
                if (p is ISectionedPage sectioned) sectioned.ShowSectionOf(target);
                for (DependencyObject? cur = target; cur is not null; cur = LogicalTreeHelper.GetParent(cur))
                    if (cur is Collapsible { Expanded: false } section) section.Toggle();
                target.UpdateLayout();
                target.BringIntoView();
                if (flash) HighlightAdorner.Flash(target);
            }
            if (anchor == "settings.access" && PageFor(page) is Pages.SettingsPage settings)
                _ = settings.OfferPawnIoAsync();
        }), DispatcherPriority.ContextIdle);
    }

    private const double TitleButtonHeight = 34;

    private readonly Button _searchButton;
    private readonly Border _searchHost = new() { Visibility = Visibility.Collapsed };
    private SettingsSearchBox? _search;

    internal void OpenSearch()
    {
        if (_search is null)
        {
            _search = new SettingsSearchBox(this) { VerticalAlignment = VerticalAlignment.Center };
            _search.Opened += CloseSearch;
            _search.Dismissed += CloseSearch;
            _searchHost.Child = _search;
        }
        _searchHost.Visibility = Visibility.Visible;
        _searchButton.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(new Action(_search.FocusBox), DispatcherPriority.Loaded);
    }

    private void CloseSearch()
    {
        _searchHost.Visibility = Visibility.Collapsed;
        _searchButton.Visibility = Visibility.Visible;
    }

    private const int QuoteSeconds = 30;

    private readonly DispatcherTimer _quoteTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(QuoteSeconds) };

    private void UpdateQuoteTimer()
    {
        if (OnScreen)
        {
            if (_quoteTimer.IsEnabled) return;
            NextQuote(animate: false);
            _quoteTimer.Start();
        }
        else _quoteTimer.Stop();
    }

    private void NextQuote(bool animate)
    {
        if (Quotes.Next() is not { } quote)
        {
            QuoteBar.Visibility = Visibility.Collapsed;
            return;
        }
        string text = $"“{quote.Text}”" + (quote.Author.Length > 0 ? " — " + quote.Author : "");
        QuoteBar.Visibility = Visibility.Visible;
        if (!animate || !Motion.Enabled)
        {
            QuoteText.Text = text;
            return;
        }
        Motion.FadeTo(QuoteText, 0, 220, () =>
        {
            QuoteText.Text = text;
            Motion.FadeTo(QuoteText, 1, 320);
        });
    }

    private void OnQuoteBarDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        e.Handled = true;
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else DragMove();
    }

    internal List<SearchEntry> Search(string query) => SettingsIndex.Find(
        SettingsIndex.With(_app.Plugins.Active).Concat(SettingsIndex.Keybinds(_app.Hotkeys.Catalog().Where(e => e.Locked is null && _app.Hotkeys.IsAvailable(e)))),
        query, e => _nav.ContainsKey(e.Page));

    internal void OpenSearchResult(SearchEntry entry)
    {
        if (entry.Keybind is { } label)
        {
            Navigate("settings");
            if (PageFor("settings") is SettingsPage settings) settings.ShowKeybind(label);
            return;
        }
        GoTo(entry.Page, entry.Anchor);
    }

    internal FrameworkElement? PageFor(string key) => _pages.GetValueOrDefault(key);

    internal void ToggleFromHotkey()
    {
        if (IsVisible && IsActive && WindowState != WindowState.Minimized)
        {
            if (_app.Config.Ui.CloseToTray && _app.Tray is not null) Hide();
            else WindowState = WindowState.Minimized;
            return;
        }
        ShowFromTray(null);
    }

    internal void ShowFromTray(string? page)
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = _app.Config.Ui.AlwaysOnTop;
        if (page is not null)
            Navigate(page);
    }

    private void OnStateChanged()
    {
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxIcon.Kind = WindowState == WindowState.Maximized ? CaptionKind.Restore : CaptionKind.Maximize;
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (App.ExitRequested || ScreenshotRunner.Active)
            return;

        if (_app.Config.Ui.CloseToTray && _app.Tray is not null)
        {
            e.Cancel = true;
            Hide();
            if (!_app.Config.Ui.TrayTipShown)
            {
                _app.Config.Ui.TrayTipShown = true;
                _app.Save();
                _app.Tray.ShowBalloon("Goodie Bag is still running",
                    "Keybinds, fan control and the overlay keep working. Click the bag icon to open it again, or right-click it to quit.",
                    NoticeKind.Info);
            }
            return;
        }

        e.Cancel = true;
        _ = RequestQuitAsync();
    }

    internal async Task RequestQuitAsync()
    {
        if (_quitAsked) return;
        bool fansActive = !_app.Demo && _app.FanControlAvailable && _app.Config.Fans.Enabled;
        if (!fansActive) { App.Quit(); return; }
        _quitAsked = true;
        try
        {
            if (!IsVisible) ShowFromTray(null);
            if (SetupOpen) CloseSetup();
            var card = DialogCard("Fan control is on",
                "Goodie Bag is driving your fans. Keep them on your curves after it closes, or hand them back to the BIOS / graphics driver?", out var buttons);
            int choice = 0;
            var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            var stop = new Button { Content = "Stop and quit", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
            var keep = new Button { Content = "Keep running", Style = (Style)FindResource("PrimaryButton"), IsDefault = true };
            keep.ToolTip = "Goodie Bag closes; a small background helper (tray icon) keeps your fan curves running until you stop it or open Goodie Bag again";
            stop.ToolTip = "Goodie Bag closes and the fans go back to the BIOS / graphics driver";
            cancel.Click += (_, _) => CloseModal(false);
            stop.Click += (_, _) => { choice = 2; CloseModal(true); };
            keep.Click += (_, _) => { choice = 1; CloseModal(true); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(stop);
            buttons.Children.Add(keep);
            card.Width = 520;
            var shown = ShowModal(card);
            keep.Focus();
            await shown;
            if (choice == 1 && !FanService.Start())
            {
                _app.Notify("Couldn't keep fan control running", "The background helper didn't start, so Goodie Bag stays open. Quit again and pick Stop to close it.", NoticeKind.Error, important: true);
                return;
            }
            if (choice != 0) App.Quit();
        }
        finally { _quitAsked = false; }
    }

    private bool _quitAsked;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is HotkeyBox)
            return;
        if (_modal is null && !SetupOpen && Keyboard.Modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift) && e.Key is Key.Tab or Key.PageDown or Key.PageUp)
        {
            bool back = e.Key == Key.PageUp || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (e.Key == Key.Tab) StepPage(back ? -1 : 1);
            else if (Keyboard.Modifiers == ModifierKeys.Control) StepSection(back ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (_modal is null && !SetupOpen && (IsShortcut(e, _app.Config.Ui.SearchKey) || (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K)))
        {
            OpenSearch();
            e.Handled = true;
        }
    }

    private void OnEscapeEdit(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _modal is not null && Keyboard.FocusedElement is not HotkeyBox)
        {
            CloseModal(false);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None || _modal is not null || SetupOpen || Keyboard.FocusedElement is HotkeyBox)
            return;
        if (Mouse.Captured is not null)
        {
            Mouse.Capture(null);
            e.Handled = true;
        }
        else if (_current is not null && _pages.GetValueOrDefault(_current) is ICancelEdit page && page.CancelEdit())
            e.Handled = true;
    }

    private static bool IsShortcut(KeyEventArgs e, string? shortcut)
    {
        if (!GoodieBag.Hotkeys.Hotkey.TryParse(shortcut, out var wanted))
            return false;
        if (GoodieBag.Hotkeys.Hotkey.FromWpf(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers) is not { } pressed || pressed != wanted)
            return false;
        const uint chord = GoodieBag.Hotkeys.Hotkey.ModControl | GoodieBag.Hotkeys.Hotkey.ModAlt | GoodieBag.Hotkeys.Hotkey.ModWin;
        return (wanted.Modifiers & chord) != 0 || Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase;
    }

    private bool Remembers => !_app.Demo && !ScreenshotRunner.Active;

    private void ApplySavedBounds()
    {
        if (!Remembers || _app.Config.Ui.Window is not { Width: > 0, Height: > 0 } b) return;
        if (_app.Config.Ui.SidebarVersion < 1)
        {
            _app.Config.Ui.SidebarVersion = 1;
            if (!b.Maximized) b.Width = Math.Min(b.Width + SidebarDips(_app.Config.Ui.ScaleFactor), SystemParameters.VirtualScreenWidth);
            _app.Save();
        }
        double width = Math.Max(MinWidth, b.Width), height = Math.Max(MinHeight, b.Height);
        var titleBar = new Rect(b.Left + 40, b.Top, Math.Max(1, width - 80), 40);
        var primary = System.Windows.Forms.Screen.PrimaryScreen;
        double toDip = primary is { Bounds.Width: > 0 } p ? SystemParameters.PrimaryScreenWidth / p.Bounds.Width : 1;
        bool onScreen = System.Windows.Forms.Screen.AllScreens.Any(s =>
        {
            var area = new Rect(s.WorkingArea.X * toDip, s.WorkingArea.Y * toDip, s.WorkingArea.Width * toDip, s.WorkingArea.Height * toDip);
            return area.IntersectsWith(titleBar) && Rect.Intersect(area, titleBar) is { Width: >= 120, Height: >= 10 };
        });
        if (!onScreen) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        (Left, Top, Width, Height) = (b.Left, b.Top, width, height);
        if (b.Maximized) WindowState = WindowState.Maximized;
    }

    private void RememberBounds()
    {
        if (!Remembers || !IsLoaded || WindowState == WindowState.Minimized || SetupOpen) return;
        var normal = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, ActualWidth, ActualHeight);
        if (normal.IsEmpty || normal.Width <= 0) return;
        var b = _app.Config.Ui.Window ??= new Config.WindowBounds();
        bool maximized = WindowState == WindowState.Maximized;
        if (b.Left == normal.Left && b.Top == normal.Top && b.Width == normal.Width && b.Height == normal.Height && b.Maximized == maximized) return;
        (b.Left, b.Top, b.Width, b.Height, b.Maximized) = (normal.Left, normal.Top, normal.Width, normal.Height, maximized);
        _app.Config.Ui.SidebarVersion = 1;
        _app.Save();
    }

    private void SquareCorners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int preference = 1;
            DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private IntPtr _classBrush;

    private void ApplyBackdrop()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source || TryFindResource("Bg") is not SolidColorBrush bg)
            return;
        var c = bg.Color;
        if (source.CompositionTarget is { } target)
            target.BackgroundColor = c;
        int dark = ThemeManager.Dark ? 1 : 0, colorRef = c.R | c.G << 8 | c.B << 16;
        DwmSetWindowAttribute(source.Handle, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(source.Handle, 34, ref colorRef, sizeof(int));
        var brush = CreateSolidBrush((uint)colorRef);
        if (brush == IntPtr.Zero) return;
        SetClassLongPtr(source.Handle, -10, brush);
        if (_classBrush != IntPtr.Zero) DeleteObject(_classBrush);
        _classBrush = brush;
    }

    private IntPtr WatchSizeMove(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232;
        if (msg == WM_ENTERSIZEMOVE) PanelGrid.LiveResize = true;
        else if (msg == WM_EXITSIZEMOVE) PanelGrid.LiveResize = false;
        return IntPtr.Zero;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint colorRef);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr hwnd, int index, IntPtr value);

    private void UpdateBadgeState()
    {
        var u = _app.Updates;
        UpdateBadge.Visibility = u.Available ? Visibility.Visible : Visibility.Collapsed;
        if (u.Latest is not { } r) return;
        bool downloading = u.State == UpdateState.Downloading;
        UpdateBadge.IsEnabled = !downloading;
        UpdateBadge.Content = downloading ? $"Downloading… {u.Progress:0%}" : "Update available";
        bool self = r.ExeUrl is not null && UpdateService.CanSelfInstall && !_app.Demo;
        UpdateBadge.ToolTip = "A new version of Goodie Bag is out. "
            + (self ? "Click to download it, close Goodie Bag, swap the file and start it again; your settings stay." : "Click to open the release page and download it.");
    }

    internal void ShowToast(string title, string message, NoticeKind kind)
    {
        var (icon, fg, bg) = kind switch
        {
            NoticeKind.Success => ("I.Check", "SageDeep", "SageSoft"),
            NoticeKind.Warning => ("I.Alert", "ApricotDeep", "ApricotSoft"),
            NoticeKind.Error => ("I.Alert", "Danger", "DangerSoft"),
            _ => ("I.Info", "AccentText", "AccentSoft"),
        };

        var badge = Ui.Badge(icon, bg, fg, 30, 9, 16);
        badge.VerticalAlignment = VerticalAlignment.Top;
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(message))
            text.Children.Add(new TextBlock { Text = message, Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 2, 0, 0) });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(badge);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (kind == NoticeKind.Success && ThemeManager.Current.RacingDecor)
        {
            var flag = Ui.CheckerStrip(48, 12, 0.35);
            flag.HorizontalAlignment = HorizontalAlignment.Right;
            flag.VerticalAlignment = VerticalAlignment.Top;
            flag.Margin = new Thickness(0, -12, -16, 0);
            Grid.SetColumn(flag, 1);
            grid.Children.Add(flag);
            if (Motion.Enabled)
            {
                var flash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) };
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
                flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.35, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
                flag.BeginAnimation(OpacityProperty, flash);
            }
        }

        var toast = new Tile
        {
            Style = (Style)FindResource("LiftedCard"),
            Padding = new Thickness(14, 12, 16, 12),
            Margin = new Thickness(0, 8, 0, 0),
            Child = grid,
            Cursor = Cursors.Hand,
        };
        if (kind == NoticeKind.Error)
        {
            toast.Background = (Brush)FindResource("DangerSoft");
            toast.BorderBrush = (Brush)FindResource("Danger");
            toast.BorderThickness = new Thickness(1.5);
            var heading = (TextBlock)text.Children[0];
            heading.Foreground = (Brush)FindResource("DangerDeep");
            heading.FontWeight = FontWeights.Bold;
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind is NoticeKind.Warning or NoticeKind.Error ? 8 : 4) };
        timer.Tick += (_, _) => { timer.Stop(); Dismiss(toast); };
        toast.MouseLeftButtonUp += (_, _) => { timer.Stop(); Dismiss(toast); };
        Toasts.Children.Add(toast);
        while (Toasts.Children.Count > 3)
            Toasts.Children.RemoveAt(0);
        Motion.Enter(toast, distance: 16);
        timer.Start();
    }

    private void Dismiss(Border toast) =>
        Motion.FadeTo(toast, 0, 220, () => Toasts.Children.Remove(toast));

    internal double ModalRoom => Root.ActualHeight - 90;

    internal Task<bool> ShowModal(FrameworkElement content)
    {
        _modal?.TrySetResult(false);
        _modal = new TaskCompletionSource<bool>();
        ClosePopupsOf(ModalContent);
        ModalContent.Content = content;
        PlaceModal();
        ModalLayer.Visibility = Visibility.Visible;
        ModalLayer.BeginAnimation(OpacityProperty, null);
        ModalLayer.Opacity = 0;
        Motion.FadeTo(ModalLayer, 1, 160);
        Motion.Enter(ModalContent, distance: 12, durationMs: 240);
        _focusBeforeModal ??= Keyboard.FocusedElement as IInputElement;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_modal is not null && !ModalLayer.IsKeyboardFocusWithin)
                ModalContent.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        });
        return _modal.Task;
    }

    private IInputElement? _focusBeforeModal;

    private void PlaceModal()
    {
        if (!PageArea.IsLoaded || PageArea.ActualWidth <= 0)
        {
            ModalContent.Margin = new Thickness(0);
            return;
        }
        var at = PageArea.TranslatePoint(new Point(0, 0), ContentArea);
        double right = Math.Max(0, ContentArea.ActualWidth - at.X - PageArea.ActualWidth);
        double bottom = Math.Max(0, ContentArea.ActualHeight - at.Y - PageArea.ActualHeight);
        ModalContent.Margin = new Thickness(Math.Max(0, at.X), Math.Max(0, at.Y), right, bottom);
    }

    internal void CloseModal(bool result)
    {
        var modal = _modal;
        _modal = null;
        ClosePopupsOf(ModalContent);
        Motion.FadeTo(ModalLayer, 0, 140, () =>
        {
            if (_modal is null)
            {
                ModalLayer.Visibility = Visibility.Collapsed;
                ModalContent.Content = null;
            }
        });
        if (_focusBeforeModal is UIElement { IsVisible: true } back) back.Focus();
        _focusBeforeModal = null;
        modal?.TrySetResult(result);
    }

    internal ScrollViewer? FindModalScroll()
    {
        static ScrollViewer? Find(object node)
        {
            if (node is ScrollViewer sv)
                return sv;
            if (node is DependencyObject d)
                foreach (var child in LogicalTreeHelper.GetChildren(d))
                    if (Find(child) is { } found)
                        return found;
            return null;
        }
        return ModalContent.Content is { } content ? Find(content) : null;
    }

    private void FitBrand()
    {
        Ui.FitLockup(LogoTile, LogoGlyph, Wordmark, maxHeight: TitleButtonHeight, width: 260, gap: 10);
        ShowThemeLink(ThemeManager.Current.Link);
        FitThemeLogo();
    }

    private double _logoTarget = -1;

    private void ShowThemeLink(ThemeLink? link)
    {
        var logo = link is null ? null : ThemePicker.Logo(link.Logo);
        _themeLink = logo is null ? null : link;
        ThemeLogo.Visibility = _themeLink is null ? Visibility.Collapsed : Visibility.Visible;
        if (_themeLink is null) return;
        ThemeLogoImage.Source = logo;
        ThemeLogoTitle.Text = _themeLink.Title;
        ThemeLogoTip.Text = _themeLink.Tip;
        System.Windows.Automation.AutomationProperties.SetName(ThemeLogo, _themeLink.Name);
    }

    private ThemeLink? _themeLink;

    private void FitThemeLogo()
    {
        if (ThemeLogo.Visibility != Visibility.Visible || !IsLoaded) return;
        double width = ThemeLogo.ActualWidth > 0 ? ThemeLogo.ActualWidth : 96;
        double free = TitleBar.ActualWidth - CaptionButtons.ActualWidth - ThemeLogo.Margin.Right - width;
        double target = Math.Clamp((free - 24) / 120, 0, 1);
        ThemeLogo.IsHitTestVisible = target > 0.5;
        if (Math.Abs(target - _logoTarget) < 0.01) return;
        bool step = _logoTarget < 0 || Math.Abs(target - _logoTarget) > 0.25 && !PanelGrid.LiveResize;
        _logoTarget = target;
        if (step) Motion.FadeTo(ThemeLogo, target, 180);
        else
        {
            ThemeLogo.BeginAnimation(OpacityProperty, null);
            ThemeLogo.Opacity = target;
        }
    }

    private void OnThemeLogoClick(object sender, RoutedEventArgs e)
    {
        if (_themeLink is { } link) Ui.OpenUrl(link.Url);
    }

    internal bool SetupOpen => SetupLayer.Visibility == Visibility.Visible;

    internal void ShowSetup(bool firstRun)
    {
        SetupContent.Content = new SetupScreen(_app, firstRun, () => CloseSetup());
        SetupLayer.BeginAnimation(OpacityProperty, null);
        SetupLayer.Opacity = 1;
        SetupLayer.Visibility = Visibility.Visible;
        Motion.Enter(SetupContent, distance: 10, durationMs: 260);
    }

    internal void CloseSetup()
    {
        if (_app.Config.Ui.SetupPending)
        {
            _app.Config.Ui.SetupPending = false;
            _app.Save();
        }
        Motion.FadeTo(SetupLayer, 0, 180, () =>
        {
            SetupLayer.Visibility = Visibility.Collapsed;
            SetupLayer.BeginAnimation(OpacityProperty, null);
            SetupLayer.Opacity = 1;
            SetupContent.Content = null;
        });
        if (_pages.GetValueOrDefault("settings") is Pages.SettingsPage settings) settings.Invalidate();
        if (_current is not null && _current == "settings")
        {
            DeactivatePage();
            UpdatePageActive();
        }
        Navigate(_current ?? _app.Config.Ui.StartPage);
    }

    internal Task<bool> Confirm(string title, string message, string confirmText, bool danger = false)
    {
        var card = DialogCard(title, message, out var buttons);
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        var ok = danger
            ? new Button { Content = Ui.IconLabel("I.Alert", confirmText), Style = (Style)FindResource("DangerConfirmButton") }
            : new Button { Content = confirmText, Style = (Style)FindResource("PrimaryButton") };
        cancel.Click += (_, _) => CloseModal(false);
        ok.Click += (_, _) => CloseModal(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var task = ShowModal(card);
        ok.Focus();
        return task;
    }

    internal async Task<string?> Prompt(string title, string message, string initial, Func<string, string?> validate, int maxLength = Ui.NameLength)
    {
        var card = DialogCard(title, message, out var buttons);
        var box = new TextBox { Text = initial, MaxLength = maxLength, Margin = new Thickness(0, 14, 0, 0) };
        var error = new TextBlock { Foreground = (Brush)FindResource("Danger"), FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        ((StackPanel)card.Child).Children.Insert(2, box);
        ((StackPanel)card.Child).Children.Insert(3, error);

        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Save", Style = (Style)FindResource("PrimaryButton"), IsDefault = true };
        cancel.Click += (_, _) => CloseModal(false);
        ok.Click += (_, _) =>
        {
            var problem = validate(box.Text.Trim());
            if (problem is null)
                CloseModal(true);
            else
            {
                error.Text = problem;
                error.Visibility = Visibility.Visible;
            }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var task = ShowModal(card);
        await Dispatcher.InvokeAsync(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
        return await task ? box.Text.Trim() : null;
    }

    internal Border DialogCard(string title, string message, out WrapPanel buttons)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("H2") });
        stack.Children.Add(new TextBlock { Text = message, Style = (Style)FindResource("Muted"), Margin = new Thickness(0, 6, 0, 0) });
        buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 2) };
        stack.Children.Add(buttons);
        return new Tile
        {
            Style = (Style)FindResource("LiftedCard"),
            Width = 440,
            Padding = new Thickness(24, 22, 24, 18),
            Child = stack,
        };
    }
}

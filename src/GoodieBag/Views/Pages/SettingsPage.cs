using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Hardware;
using GoodieBag.Plugins;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class SettingsPage : UserControl, IPage, IScreenshotStates, ICancelEdit, ISectionedPage, IPageHistory
{
    private const string PageKey = "settings", GeneralId = "general", KeybindsId = "keybinds";

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly PanelStack _body;
    private readonly MasonryPanel _sections = new() { MinColumnWidth = 440, MaxColumns = 3 };
    private readonly ScrollViewer _scroll;
    private FrameworkElement? _heading, _keybindsView;

    public event Action? SectionsChanged;

    private static readonly (PageSection Section, string Anchor)[] Cards =
    {
        (new("app", "Application", "I.Power", Hues.Accent), "settings.app"),
        (new("plugins", "Plugins", "I.Plug", Hues.Accent), "settings.plugins"),
        (new("look", "Appearance", "I.Palette", Hues.Accent), "settings.look"),
        (new("performance", "Performance", "I.Gauge", Hues.Accent), "settings.performance"),
        (new("access", "Hardware access", "I.Cpu", Hues.Accent), "settings.access"),
        (new("data", "Data & Configuration", "I.Folder", Hues.Accent), "settings.data"),
        (new("updates", "About & updates", "I.Info", Hues.Accent), "settings.updates"),
    };

    private static readonly PageSection[] SidebarViews =
    {
        new(GeneralId, "General", "I.Tune", Hues.Accent),
        new(KeybindsId, "Keybinds", "I.Keyboard", Hues.Accent),
    };

    internal static SectionList SectionsFor(AppServices app)
    {
        string? saved = app.Config.Ui.PageSections.GetValueOrDefault(PageKey);
        return new SectionList(SidebarViews, saved == KeybindsId ? KeybindsId : GeneralId, Movable: false);
    }

    public IReadOnlyList<PageSection> Sections => SectionsFor(_app).Sections;

    public string? CurrentSection => SectionsFor(_app).Current;

    public bool SectionsMovable => false;

    public void MoveSections(IReadOnlyList<string> order) { }

    public object? HistoryState => CurrentSection;

    public void RestoreHistory(object? state)
    {
        if (state is string id) ShowSection(id);
    }

    public void ShowSection(string id)
    {
        string? anchor = Cards.FirstOrDefault(c => c.Section.Id == id).Anchor;
        if (id != GeneralId && id != KeybindsId && anchor is null) return;
        string view = id == KeybindsId ? KeybindsId : GeneralId;
        bool changed = CurrentSection != view;
        U.PageSections[PageKey] = view;
        if (changed) _app.Save();
        bool wasKeybinds = _keybindsView?.Visibility == Visibility.Visible;
        ApplyView();
        if (id == KeybindsId)
        {
            _scroll.ScrollToTop();
            if (!wasKeybinds && _shown && _keybindsView is not null) Motion.Enter(_keybindsView, distance: 12, durationMs: 240);
        }
        else
        {
            if (wasKeybinds && _shown) Motion.Enter(_sections, distance: 12, durationMs: 240);
            if (anchor is null)
            {
                _scroll.ScrollToTop();
                if (changed) SectionsChanged?.Invoke();
                return;
            }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Ui.FindAnchor(_sections, anchor) is not { } card || _scroll.Content is not UIElement frame) return;
                _body.UpdateLayout();
                double top = card.TranslatePoint(new Point(), frame).Y;
                _scroll.ScrollToVerticalOffset(Math.Max(0, top - 12));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        if (changed) SectionsChanged?.Invoke();
    }

    public bool ShowSectionOf(DependencyObject target)
    {
        if (_keybindsView is not null && (ReferenceEquals(target, _keybindsView) || _keybindsView.IsAncestorOf(target)))
        {
            if (CurrentSection != KeybindsId) ShowSection(KeybindsId);
            return true;
        }
        foreach (var (_, anchor) in Cards)
            if (Ui.FindAnchor(_sections, anchor) is { } card && (ReferenceEquals(card, target) || card.IsAncestorOf(target)))
            {
                if (CurrentSection == KeybindsId)
                {
                    U.PageSections[PageKey] = GeneralId;
                    ApplyView();
                    SectionsChanged?.Invoke();
                }
                return true;
            }
        return false;
    }

    private void ApplyView()
    {
        bool keybinds = CurrentSection == KeybindsId;
        if (_heading is not null) _heading.Visibility = keybinds ? Visibility.Collapsed : Visibility.Visible;
        _sections.Visibility = keybinds ? Visibility.Collapsed : Visibility.Visible;
        if (_keybindsView is not null) _keybindsView.Visibility = keybinds ? Visibility.Visible : Visibility.Collapsed;
    }

    public SettingsPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        _body = new PanelStack { Margin = new Thickness(28, 0, 28, 28), IsPanel = child => child == _sections };
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Ui.PageFrame(_body), Focusable = false };
        Content = _scroll;
    }

    private bool _shown;

    public void OnShown()
    {
        _shown = true;
        string built = Signature();
        if (built != _builtFor)
        {
            _builtFor = built;
            Build();
            Motion.Stagger(_body);
        }
        else if (_keybindTree is { } tree && KeybindSignature() != _keybindsFor)
            Dispatcher.BeginInvoke(new Action(() => { _keybindsFor = KeybindSignature(); tree.Rebuild(); }), System.Windows.Threading.DispatcherPriority.Background);
        else if (_keybindHost is { Child: null } host && _keybindTree is { } kept)
            AfterFirstFrame(() =>
            {
                if (!_shown || host.Child is not null || !ReferenceEquals(_keybindTree, kept)) return;
                host.Child = kept;
                host.MinHeight = 0;
            });
        if (!s_pawnIoOffered)
        {
            s_pawnIoOffered = true;
            Dispatcher.BeginInvoke(new Action(() => { if (_shown) _ = OfferPawnIoAsync(); }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        FillUpdates();
        _app.Updates.Changed += FillUpdates;
        _app.Plugins.Changed += OnPluginsChanged;
    }

    internal void Invalidate()
    {
        _builtFor = null;
        _startupState = null;
        _startupError = null;
    }

    private void OnPluginsChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (!_shown) return;
        _builtFor = Signature();
        Build();
    }), System.Windows.Threading.DispatcherPriority.Background);

    private string? _builtFor, _keybindsFor;

    private string Signature()
    {
        var p = _app.Config.Performance;
        return string.Join("|", _app.Plugins.All.Select(x => x.IsActive ? "1" : "0")) + $"|{p.Rate}|{p.IntervalMs}|{p.Background}|{_app.Hardware.Ready}|{_app.Hardware.OpenError}|{_app.Hardware.PawnIoState}|{_app.Hardware.CpuTempMissing}|{_app.Hardware.Devices?.Count}|{_app.Demo}";
    }

    private string KeybindSignature() => string.Join("\n", _app.Hotkeys.Catalog().Select(e => e.Id + "=" + e.Get()));

    public void OnHidden()
    {
        _shown = false;
        if (_keybindTree is not null) _keybindTree.Arranging = false;
        if (_keybindHost is { Child: FrameworkElement tree } host)
        {
            host.MinHeight = tree.ActualHeight;
            host.Child = null;
        }
        _app.Updates.Changed -= FillUpdates;
        _app.Plugins.Changed -= OnPluginsChanged;
    }

    private UiSettings U => _app.Config.Ui;

    private void Build()
    {
        _body.Children.Clear();
        var heading = Ui.Text("Settings", "H1");
        heading.Margin = new Thickness(0, 0, 0, Ui.HeaderGap);
        _heading = heading;
        _body.Children.Add(heading);
        _sections.Children.Clear();
        _body.Children.Add(_sections);

        BuildApplication();
        BuildPlugins();
        BuildAppearance();
        BuildPerformance();
        BuildHardware();
        BuildData();
        BuildAboutAndUpdates();
        _keybindsView = BuildKeybinds();
        _body.Children.Add(_keybindsView);
        ApplyView();
    }

    internal void ShowKeybind(string label) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_keybindSearch is null || _keybindFind is null) return;
        ShowSection(KeybindsId);
        if (_keybindSearch.Visibility != Visibility.Visible) _keybindFind.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        _keybindSearch.Text = label;
    }), System.Windows.Threading.DispatcherPriority.Loaded);

    private StartupState? _startupState;
    private string? _startupError;
    private bool _startupBusy;

    private void BuildApplication()
    {
        var app = Section("Application settings", "I.Power", "settings.app");
        var startup = Group(app, "Startup", "settings.startup");
        BuildStartup(startup);
        var window = Group(app, "Window", "settings.window");
        BuildWindow(window);
    }

    private void BuildPlugins()
    {
        var group = Section("Plugins", "I.Plug", "settings.plugins");
        group.Children.Add(new TextBlock
        {
            Text = "Turn on the tools you use. One that's off leaves the sidebar and has no page, keybind or tray item, and runs nothing in the background; its settings are kept for when you turn it back on.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4),
        });
        group.Children.Add(Ui.IconText("I.Info", new TextBlock
        {
            Text = "Drag a tool up or down in the sidebar to change its place; this list follows that order.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap,
        }, "TextMuted"));
        foreach (var plugin in PluginManager.InNavOrder(_app.Plugins.All, U.NavOrder))
        {
            var p = plugin;
            if (p.Id == PluginIds.Overlay) continue;
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var (soft, deep) = p.Colors;
            head.Children.Add(Ui.Badge(p.Pages.Count > 0 ? p.Pages[0].Icon : p.Icon, soft, deep, 28, 8, 15));
            var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
            head.Children.Add(titles);
            head.Children.Add(HudSettings.HelpTip.For($"Reads: {p.Reads}\n\nRuns in the background: {p.BackgroundWork}\n\nChanges on your PC: {p.Changes}", $"What {p.Name} reads and changes"));
            var sw = Ui.Switch(_app.Plugins.IsEnabled(p), on =>
            {
                _app.Plugins.SetEnabled(p, on);
                _app.Notify(on ? $"{p.Name} on" : $"{p.Name} off", on ? "It's in the sidebar." : "Its background work has stopped. Settings are kept.", NoticeKind.Success);
            });
            AutomationProperties.SetName(sw, p.Name);
            var row = new Grid { Margin = new Thickness(0, 8, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(head);
            text.Children.Add(new TextBlock { Text = p.Description, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(38, 3, 16, 0) });
            row.Children.Add(text);
            sw.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(sw, 1);
            row.Children.Add(sw);
            group.Children.Add(row);
            bool parentOn = _app.Plugins.IsEnabled(p);
            if (p.Id == PluginIds.HardwareMonitor)
            {
                if (_app.Plugins.Find(PluginIds.Overlay) is { } overlay)
                    group.Children.Add(SubRow(overlay.Name, "FPS, temperatures, load and fan speeds drawn on top of your game.", _app.Plugins.IsEnabled(overlay), true, on =>
                    {
                        _app.Plugins.SetEnabled(overlay, on);
                        _app.Notify(on ? $"{overlay.Name} on" : $"{overlay.Name} off", on ? "It's in Hardware Monitor → Performance Overlay." : "Its background work has stopped. Settings are kept.", NoticeKind.Success);
                    }));
                foreach (var part in HardwareMonitorPage.Switchable)
                    group.Children.Add(SubRow(part.Title, PartNotes.GetValueOrDefault(part.Id, ""), HardwareMonitorPage.IsSectionOn(_app, part.Id), parentOn,
                        on => HardwareMonitorPage.SetSectionOn(_app, part.Id, on)));
            }
            if (p.Id == PluginIds.SimRacing)
                foreach (var tool in SimRacingPage.ToolsInOrder(_app))
                    group.Children.Add(SubRow(tool.Title, PartNotes.GetValueOrDefault(tool.Id, ""), SimRacingPage.IsToolOn(_app, tool.Id), parentOn,
                        on => SimRacingPage.SetToolOn(_app, tool.Id, on)));
        }
    }

    private static readonly Dictionary<string, string> PartNotes = new()
    {
        ["performance"] = "CPU, graphics card, memory, storage, network and frame rate with graphs.",
        ["sensors"] = "Every sensor reading with min and max, one card per device.",
        ["setups"] = "Your car setups by car: find, rename, move, import and share.",
        ["hud"] = "In-game widgets from iRacing's live telemetry; off stops reading it.",
        ["fov"] = "The correct field of view for your seat and screens.",
        ["iracing"] = "Every iRacing setting explained, a health check and backups.",
        ["tradingpaints"] = "Starts the Trading Paints Downloader by hand or with iRacing.",
        ["garage61"] = "Starts the Garage 61 Telemetry Agent by hand or with iRacing.",
    };

    private static Grid SubRow(string title, string note, bool on, bool enabled, Action<bool> set)
    {
        var sw = Ui.Switch(on, set);
        sw.IsEnabled = enabled;
        AutomationProperties.SetName(sw, title);
        var row = new Grid { Margin = new Thickness(38, 2, 0, 4), Opacity = enabled ? 1 : 0.55 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new Border { BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(12, 2, 16, 2) };
        text.SetResourceReference(Border.BorderBrushProperty, "Line");
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        if (note.Length > 0)
            stack.Children.Add(new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        text.Child = stack;
        row.Children.Add(text);
        sw.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(sw, 1);
        row.Children.Add(sw);
        return row;
    }

    private static StackPanel Group(StackPanel section, string title, string anchor)
    {
        var group = new StackPanel { Margin = new Thickness(0, section.Children.Count > 1 ? 10 : 4, 0, 0) };
        group.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 4, 0, 0) });
        Ui.Anchor(group, anchor);
        section.Children.Add(group);
        return group;
    }

    private void BuildStartup(StackPanel startup)
    {
        if (_startupState is null && _startupError is null && !_app.Demo && !_startupBusy)
            RunStartupTask(null);
        bool on = _startupState?.Enabled == true;

        string? description = _app.Demo ? "Not available in demo mode."
            : on ? "Runs as administrator without a UAC prompt (a Windows scheduled task)."
            : null;
        var toggle = Ui.Switch(on, SetStartup);
        toggle.IsEnabled = !_app.Demo && !_startupBusy;
        startup.Children.Add(Ui.SettingRow("Start with Windows", description, toggle));

        if (_startupState?.PointsElsewhere == true)
        {
            var row = Notice("ApricotSoft", "ApricotDeep", "I.Alert",
                $"The startup entry points to a different copy of Goodie Bag ({_startupState.Command}), so it won't start this one.");
            var repair = Ui.LabeledButton("I.Wand", "Repair", () => SetStartup(true), "SoftButton");
            Grid.SetColumn(repair, 2);
            row.Children.Add(repair);
            startup.Children.Add(row);
        }
        if (_startupError is not null)
        {
            var banner = Ui.DangerBanner("Windows didn't let Goodie Bag change its startup task",
                $"{_startupError}\n" +
                $"To add it by hand: Task Scheduler → Create Task → tick “Run with highest privileges”, add an “At log on” trigger and the action “{Environment.ProcessPath}” with the argument --minimized.");
            banner.Margin = new Thickness(0, 4, 0, 8);
            var row = (Grid)banner.Child;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var open = Ui.LabeledButton("I.ChevronRight", "Open Task Scheduler", () => Ui.OpenUrl("taskschd.msc"), "SoftButton");
            open.VerticalAlignment = VerticalAlignment.Center;
            open.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(open, 2);
            row.Children.Add(open);
            startup.Children.Add(banner);
        }

        startup.Children.Add(Ui.Divider());
        startup.Children.Add(Ui.SettingRow("Start in background",
            "Opens in the tray; keybinds, fans and the overlay run as normal.",
            Ui.Switch(U.StartMinimized, v => { U.StartMinimized = v; _app.Save(); })));
    }

    private void SetStartup(bool on)
    {
        if (_app.Demo || _startupBusy) return;
        RunStartupTask(on);
    }

    private async void RunStartupTask(bool? enable)
    {
        _startupBusy = true;
        StartupState? state = null;
        string? error = null;
        await Task.Run(() =>
        {
            try
            {
                if (enable is { } on)
                    StartupManager.SetEnabled(on);
            }
            catch (Exception ex) { error = ex.Message; }
            try { state = StartupManager.State(); }
            catch (Exception ex) { error ??= ex.Message; }
        });
        _startupBusy = false;
        _startupState = state;
        _startupError = error;
        if (enable is { } enabled && error is null)
            _app.Notify(enabled ? "Starts with Windows" : "Won't start with Windows", "", NoticeKind.Success);
        if (_shown || enable is not null)
            Build();
    }

    private void BuildWindow(StackPanel window)
    {
        window.Children.Add(Ui.SettingRow("Close in background",
            U.CloseToTray
                ? "X hides to the tray; Goodie Bag keeps running (keybinds, fans, overlay)."
                : "X quits Goodie Bag.",
            Ui.Switch(U.CloseToTray, v => { U.CloseToTray = v; _app.Save(); Build(); })));
        window.Children.Add(Ui.Divider());
        var pages = _app.NavPages().Prepend((Key: UiSettings.LastUsedPage, Title: "Last page used")).ToList();
        var start = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (key, title) in pages) start.Items.Add(new ComboBoxItem { Content = title, Tag = key });
        int at = pages.FindIndex(p => p.Key == U.StartPage);
        start.SelectedIndex = Math.Max(0, at);
        start.SelectionChanged += (_, _) =>
        {
            if (start.SelectedItem is ComboBoxItem { Tag: string key }) { U.StartPage = key; _app.Save(); }
        };
        var startRow = Ui.SettingRow("Open on", null, start,
            new Ui.SettingDefault(() => U.StartPage == UiSettings.LastUsedPage, () => { U.StartPage = UiSettings.LastUsedPage; _app.Save(); Build(); }, "Last page used"));
        Ui.Anchor(startRow, "settings.startpage");
        window.Children.Add(startRow);
        window.Children.Add(Ui.Divider());
        void SetSide(bool right)
        {
            if (U.SidebarRight == right) return;
            U.SidebarRight = right;
            _app.Save();
            _window.ApplySidebarSide();
        }
        var side = Ui.Segmented(new[] { "Left", "Right" }, U.SidebarRight ? 1 : 0, i => SetSide(i == 1), "sidebar-side");
        var sideRow = Ui.SettingRow("Sidebar", "Which side of the window the list of tools sits on. Drag tools and their sections in it to reorder them.", side,
            new Ui.SettingDefault(() => !U.SidebarRight, () => { SetSide(false); Build(); }, "Left"));
        Ui.Anchor(sideRow, "settings.sidebar");
        window.Children.Add(sideRow);
        window.Children.Add(Ui.Divider());
        void SetAllOpen(bool on)
        {
            if (U.SidebarAllOpen == on) return;
            U.SidebarAllOpen = on;
            _app.Save();
            _window.RefreshSidebar();
        }
        var allOpenRow = Ui.SettingRow("Show every tool's sections", "Off: only the open tool lists its sections in the sidebar.",
            Ui.Switch(U.SidebarAllOpen, SetAllOpen),
            new Ui.SettingDefault(() => !U.SidebarAllOpen, () => { SetAllOpen(false); Build(); }, "Off"));
        Ui.Anchor(allOpenRow, "settings.sidebaropen");
        window.Children.Add(allOpenRow);
        window.Children.Add(Ui.Divider());
        var onTop = Ui.SettingRow("Keep on top", null,
            Ui.Switch(U.AlwaysOnTop, v => { U.AlwaysOnTop = v; _window.Topmost = v; _app.Save(); }),
            new Ui.SettingDefault(() => !U.AlwaysOnTop, () => { U.AlwaysOnTop = false; _window.Topmost = false; _app.Save(); Build(); }, "Off"));
        Ui.Anchor(onTop, "settings.ontop");
        window.Children.Add(onTop);
        window.Children.Add(Ui.Divider());
        window.Children.Add(Ui.SettingRow("Notifications", "Warnings always show.",
            Ui.Switch(_app.Config.ShowNotifications, v => { _app.Config.ShowNotifications = v; _app.Save(); }),
            new Ui.SettingDefault(() => _app.Config.ShowNotifications, () => { _app.Config.ShowNotifications = true; _app.Save(); Build(); }, "On")));
        window.Children.Add(Ui.Divider());
        var quit = Ui.LabeledButton("I.Power", "Quit Goodie Bag", () => _ = _window.RequestQuitAsync(), "DangerButton");
        quit.ToolTip = "Closes Goodie Bag: keybinds, the overlay and the tray icon stop";
        window.Children.Add(Ui.SettingRow("Quit", null, quit, more: "With fan control on, it asks first whether a small helper keeps your fan curves running or the fans go back to the BIOS / driver."));
    }

    private void BuildAppearance()
    {
        var look = Section("Appearance", "I.Palette", "settings.look");
        look.Children.Add(new TextBlock { Text = "Theme", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        look.Children.Add(new Border { Height = 10 });
        look.Children.Add(new ThemePicker(U.Theme, _app.SetTheme, columns: 2, height: 64, oled: _app.UseOledColors, pastel: _app.UsePastelPreset) { Margin = new Thickness(0, 0, -10, 4) });
        look.Children.Add(Ui.Divider());
        var custom = new CustomThemeSection(_app, _window);
        Ui.Anchor(custom, "settings.customcolors");
        look.Children.Add(custom);
        look.Children.Add(Ui.Divider());
        AddTopography(look);
        int scaleIndex = Array.IndexOf(UiSettings.InterfaceScales, UiSettings.InterfaceScales.OrderBy(o => Math.Abs(o - U.InterfaceScale)).First());
        var size = Ui.Segmented(UiSettings.InterfaceScales.Select(p => $"{p}%").ToArray(), scaleIndex, i =>
        {
            U.InterfaceScale = UiSettings.InterfaceScales[i];
            _app.Save();
            _window.ApplyInterfaceScale(rebuild: true);
        }, "Scale", UiSettings.InterfaceScales.Select(p => (string?)(p switch
        {
            90 => "90%: fits the most on a small or low-resolution screen",
            100 => "100%: the normal size",
            _ => "110%: larger text and controls",
        })).ToList());
        var sizeRow = Ui.SettingRow("Interface size", null, size, new Ui.SettingDefault(() => U.InterfaceScale == 100, () =>
        {
            U.InterfaceScale = 100;
            _app.Save();
            _window.ApplyInterfaceScale(rebuild: true);
        }, "100%"));
        Ui.Anchor(sizeRow, "settings.scale");
        look.Children.Add(sizeRow);
        look.Children.Add(Ui.Divider());

        string animationNote = SystemParameters.ClientAreaAnimation
            ? "Page transitions, card entrances, spinning fan icons. Nothing animates while Goodie Bag is in the tray." + (PcClass.Weak ? " This PC gets a lighter version: 30 frames a second, no blur, a still topography background." : "")
            : "Windows' “Animation effects” setting is off, so animations stay off.";
        var anim = Ui.Switch(U.Animations, v => { U.Animations = v; _app.Save(); NavIcon.RefreshAll(); _window.Topography.Refresh(); });
        anim.IsEnabled = SystemParameters.ClientAreaAnimation;
        look.Children.Add(Ui.SettingRow("Animations", animationNote, anim,
            SystemParameters.ClientAreaAnimation ? new Ui.SettingDefault(() => U.Animations, () => { U.Animations = true; _app.Save(); NavIcon.RefreshAll(); _window.Topography.Refresh(); Build(); }, "On") : null));
    }

    private FrameworkElement? _topoRow;

    private void AddTopography(StackPanel look)
    {
        var topo = _window.Topography;
        string? note = !TopographyEffect.Available
            ? "Needs hardware graphics (pixel shader 3.0); the track drawing shows instead."
            : _app.Light ? "Paused while iRacing runs (lighter drawing); the track drawing shows meanwhile."
            : PcClass.Weak ? "Holds still on this PC (lighter drawing): moving, it redraws the whole window 30 times a second." : null;
        var row = Ui.SettingRow("Topography background", note, Ui.Switch(U.TopoBackground, v =>
        {
            U.TopoBackground = v;
            _app.Save();
            topo.Refresh();
            Build();
        }));
        Ui.Anchor(row, "settings.topo");
        _topoRow = row;
        look.Children.Add(row);
        look.Children.Add(Ui.Divider());

        var reset = Ui.LabeledButton("I.Undo", "Reset to theme", () =>
        {
            U.TopoColor = null;
            _app.Save();
            topo.Refresh();
            Build();
        });
        reset.IsEnabled = U.TopoColor is not null;
        var field = ColorField.Build(() => Hex(topo.LineColor), hex =>
        {
            U.TopoColor = hex;
            _app.Save();
            topo.Refresh();
            reset.IsEnabled = true;
            return true;
        }, "The contour lines' color");
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(field);
        controls.Children.Add(new Border { Width = 8 });
        controls.Children.Add(reset);
        controls.IsEnabled = U.TopoBackground;
        var colorRow = Ui.SettingRow("Line color", null, controls);
        Ui.Anchor(colorRow, "settings.topocolor");
        look.Children.Add(colorRow);
        look.Children.Add(Ui.Divider());
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private TextBlock? _rateNote;

    private void BuildPerformance()
    {
        var section = Section("Performance", "I.Gauge", "settings.performance");
        var p = _app.Config.Performance;
        var options = PerformanceSettings.Presets.Append(SensorRate.Custom).ToArray();
        var rate = Ui.Segmented(options.Select(PerformanceSettings.Name).ToArray(), Array.IndexOf(options, p.Rate), i =>
        {
            _app.SetPerformance(options[i]);
            Build();
        }, "SensorRate", options.Select(r => (string?)(r switch
        {
            SensorRate.Low => "Low: every 2 s. The lightest on your PC.",
            SensorRate.Fast => "Fast: every 0.5 s. Smoothest charts; uses a little more CPU.",
            SensorRate.Custom => "Custom: pick your own interval, 0.25 to 5 s.",
            _ => "Balanced: every second. The normal rate.",
        })).ToList());
        _rateNote = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        FillRateNote();
        var rateRow = RowWithNote("Sensor update rate", _rateNote, rate,
            $"How often temperatures, loads, clocks and fan speeds are read for every page and the Hardware Monitor (its Update rate is this same setting). Fan control still checks its curves and the failsafe at least every {PerformanceSettings.FanLoopMs / 1000} s, and the overlay uses its own rate while it's showing. Applies at once.");
        Ui.Anchor(rateRow, "settings.sensorrate");
        section.Children.Add(rateRow);

        if (p.Rate == SensorRate.Custom)
        {
            var custom = new NumberBox(p.CustomMs, "ms") { Minimum = PerformanceSettings.MinMs, Maximum = PerformanceSettings.MaxMs, Step = 50 };
            custom.ValueChanged += v =>
            {
                _app.SetPerformance(SensorRate.Custom, (int)Math.Round(v));
                FillRateNote();
            };
            section.Children.Add(Ui.SettingRow("Custom interval", $"From {PerformanceSettings.MinMs} ms to {PerformanceSettings.MaxMs / 1000} s.", custom));
        }

        section.Children.Add(Ui.Divider());
        var background = Ui.Segmented(new[] { "Standard", "Reduced" }, (int)p.Background,
            i => _app.SetPerformance(_app.Config.Performance.Rate, background: (BackgroundUsage)i), "Background", new[]
            {
                "Standard: the same rate whether the window is open or not.",
                $"Reduced: while the window is in the tray or minimized, sensors are read every {PerformanceSettings.ReducedHiddenMs / 1000} s at most.",
            });
        var backgroundRow = Ui.SettingRow("Background usage", "Fewer sensor reads while the window is hidden.", background,
            new Ui.SettingDefault(() => _app.Config.Performance.Background == BackgroundUsage.Reduced, () => { _app.SetPerformance(_app.Config.Performance.Rate, background: BackgroundUsage.Reduced); Build(); }, "Reduced"),
            $"Reduced reads sensors at most every {PerformanceSettings.ReducedHiddenMs / 1000} s while the window is in the tray or minimized (pages already pause then). Fan control, the overlays, keybinds and window profiles keep working as normal.");
        Ui.Anchor(backgroundRow, "settings.background");
        section.Children.Add(backgroundRow);

        section.Children.Add(Ui.Divider());
        var lightRow = Ui.SettingRow("Lighter drawing while iRacing runs", "Keeps pages quick mid-race.",
            Ui.Switch(p.LightWhileRacing, v => { p.LightWhileRacing = v; _app.Save(); _app.WatchSim(); }),
            new Ui.SettingDefault(() => p.LightWhileRacing, () => { p.LightWhileRacing = true; _app.Save(); _app.WatchSim(); Build(); }, "On"),
            "Goodie Bag's window draws without the graphics card the sim keeps busy, and its animations pause, so pages open quickly mid-race. Back to normal when iRacing closes.");
        Ui.Anchor(lightRow, "settings.light");
        section.Children.Add(lightRow);
    }

    private void FillRateNote()
    {
        if (_rateNote is null) return;
        int ms = _app.Config.Performance.IntervalMs;
        _rateNote.Text = $"Sensors are read every {PerformanceSettings.Describe(ms)}.";
    }

    private static Grid RowWithNote(string title, TextBlock note, UIElement control, string? more = null)
    {
        var grid = Ui.SettingRow(title, null, control, more: more);
        ((StackPanel)grid.Children[0]).Children.Add(note);
        return grid;
    }

    private TextBlock _diagStatus = new();

    private void BuildDiagnostics(StackPanel parent)
    {
        var section = new StackPanel();
        Ui.Anchor(section, "settings.diagnostics");
        parent.Children.Add(section);
        section.Children.Add(new TextBlock { Text = ThemeManager.Case("Diagnostics"), Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 8, 0, 4) });
        section.Children.Add(new TextBlock
        {
            Text = "Tools for troubleshooting hardware and app problems. Reports leave out personal details (user and PC names, folders, IP addresses).",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10),
        });
        var buttons = new WrapPanel();
        var rescan = Ui.LabeledButton("I.Search", "Rescan hardware", Rescan);
        rescan.ToolTip = "Closes the sensor drivers and opens them again, so new or missing temperature sensors, fans and fan controllers are detected. Fans follow the BIOS for a moment meanwhile.";
        Ui.Anchor(rescan, "settings.rescan");
        buttons.Children.Add(Spaced(rescan));
        var refresh = Ui.LabeledButton("I.Refresh", "Refresh sensors", () =>
        {
            _app.Hardware.RefreshNow();
            DiagStatus(_app.Hardware.Running ? "Reading every sensor and fan now." : "The sensors are idle (nothing is using them), so there's nothing to refresh.");
        });
        refresh.ToolTip = "Reads every sensor and fan now instead of waiting for the next update";
        buttons.Children.Add(Spaced(refresh));
        var logs = Ui.LabeledButton("I.FileText", "Open logs", OpenLogs);
        logs.ToolTip = "Opens the settings folder with error.log selected (problems Goodie Bag ran into)";
        Ui.Anchor(logs, "settings.logs");
        buttons.Children.Add(Spaced(logs));
        var export = Ui.LabeledButton("I.Save", "Export diagnostic report", () => _ = ExportReportAsync());
        export.ToolTip = "Saves a text report (versions, detected hardware, fans, sensors, plugins, settings counts, recent errors) to share when asking for help";
        Ui.Anchor(export, "settings.report");
        buttons.Children.Add(Spaced(export));
        var copy = Ui.LabeledButton("I.Copy", "Copy system information", () => _ = CopySystemInfoAsync());
        copy.ToolTip = "Copies a few lines (Goodie Bag and Windows versions, CPU, GPU, sensor status) to paste into a message";
        buttons.Children.Add(Spaced(copy));
        section.Children.Add(buttons);
        _diagStatus = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };
        section.Children.Add(_diagStatus);
    }

    private void DiagStatus(string text)
    {
        _diagStatus.Text = text;
        _diagStatus.Visibility = Visibility.Visible;
    }

    private void Rescan()
    {
        if (_app.Demo)
        {
            _app.Hardware.RefreshNow();
            DiagStatus("Demo data: nothing on this PC is scanned.");
            return;
        }
        DiagStatus(_app.Hardware.Rescan()
            ? "Rescanning: the sensors are opened again; new devices and fans show up in a few seconds."
            : "The sensors aren't in use right now; they're detected afresh the next time a page or plugin needs them.");
    }

    private void OpenLogs()
    {
        if (!GoodieBag.Services.External.Allowed("open the log folder")) return;
        try
        {
            if (File.Exists(DiagnosticReport.LogPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{DiagnosticReport.LogPath}\"") { UseShellExecute = true })?.Dispose();
            else
            {
                OpenFolder(ConfigStore.Folder);
                DiagStatus("No errors logged so far (there's no error.log).");
            }
        }
        catch { OpenFolder(ConfigStore.Folder); }
    }

    private async Task ExportReportAsync()
    {
        if (!GoodieBag.Services.External.Allowed("save dialog")) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export diagnostic report",
            Filter = "Text file (*.txt)|*.txt|Markdown (*.md)|*.md",
            FileName = $"GoodieBag-diagnostics-{DateTime.Now:yyyy-MM-dd-HHmm}.txt",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        DiagStatus("Collecting…");
        try
        {
            string report = await DiagnosticReport.BuildAsync(_app, full: true);
            await File.WriteAllTextAsync(dialog.FileName, report);
            DiagStatus($"Report saved as {Path.GetFileName(dialog.FileName)}.");
            _app.Notify("Diagnostic report saved", Path.GetFileName(dialog.FileName), NoticeKind.Success);
        }
        catch (Exception ex)
        {
            DiagStatus("");
            _diagStatus.Visibility = Visibility.Collapsed;
            _app.Notify("Couldn't save the report", ex.Message, NoticeKind.Error, important: true);
        }
    }

    private async Task CopySystemInfoAsync()
    {
        try
        {
            string info = await DiagnosticReport.BuildAsync(_app, full: false);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Clipboard.SetText(info);
                    break;
                }
                catch (System.Runtime.InteropServices.COMException) when (attempt < 3)
                {
                    await Task.Delay(100);
                }
            }
            DiagStatus("System information copied. Paste it into your message.");
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't copy", ex.Message, NoticeKind.Warning);
        }
    }

    private void BuildData()
    {
        var section = Section("Data & Configuration", "I.Folder", "settings.data");
        var transfer = new StackPanel { Orientation = Orientation.Horizontal };
        var export = Ui.LabeledButton("I.Save", "Export…", () => ExportSettings());
        export.ToolTip = "Saves every setting (theme, window profiles, fan curves and presets, overlay, keybinds, plugins, layouts…) to a file";
        export.Margin = new Thickness(0, 0, 8, 0);
        transfer.Children.Add(export);
        var import = Ui.LabeledButton("I.Download", "Import…", () => _ = ImportSettingsAsync());
        import.ToolTip = "Loads settings from an exported file (shows what's in it first; Goodie Bag then restarts)";
        transfer.Children.Add(import);
        var transferRow = Ui.SettingRow("Export / import settings",
            "Importing replaces your settings (a copy is kept) and restarts Goodie Bag.",
            transfer);
        Ui.Anchor(transferRow, "settings.export");
        section.Children.Add(transferRow);
        section.Children.Add(Ui.Divider());

        var folder = Ui.LabeledButton("I.Folder", "Open folder", () => OpenFolder(ConfigStore.Folder));
        folder.ToolTip = TextCase.VerbatimTip(ConfigStore.Folder);
        var fileRow = Ui.SettingRow("Settings file", "Your settings, the error log, backups made by Reset and Import, and iRacing backups, all in one folder.", folder);
        Ui.Anchor(fileRow, "settings.folder");
        section.Children.Add(fileRow);
        section.Children.Add(Ui.Divider());
        section.Children.Add(ResetRow());
        section.Children.Add(Ui.Divider());
        BuildDiagnostics(section);
    }

    private void ExportSettings()
    {
        if (_app.Demo)
        {
            _app.Notify("Demo mode", "Demo data isn't exported.", NoticeKind.Info);
            return;
        }
        if (!GoodieBag.Services.External.Allowed("save dialog")) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export settings",
            Filter = "Goodie Bag settings (*.json)|*.json",
            FileName = $"GoodieBag-settings-{DateTime.Now:yyyy-MM-dd}.json",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, ConfigTransfer.Export(_app.Config, UpdateService.Show(UpdateService.Current), DateTime.Now));
            _app.Notify("Settings exported", Path.GetFileName(dialog.FileName), NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't export", ex.Message, NoticeKind.Error, important: true);
        }
    }

    private async Task ImportSettingsAsync()
    {
        if (!GoodieBag.Services.External.Allowed("open dialog")) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import settings", Filter = "Goodie Bag settings (*.json)|*.json|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        string text;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 20 * 1024 * 1024)
                throw new IOException("The file is too big to be a settings file.");
            text = await File.ReadAllTextAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't read the file", ex.Message, NoticeKind.Error, important: true);
            return;
        }
        var preview = ConfigTransfer.Read(text, UpdateService.Current);
        if (preview.Config is not { } imported)
        {
            _app.Notify("Nothing imported", preview.Error ?? "The file couldn't be used.", NoticeKind.Warning, important: true);
            return;
        }
        if (!await _window.Confirm("Import these settings?",
                $"{Path.GetFileName(dialog.FileName)} replaces all your current settings with:\n\n" +
                string.Join("\n", preview.Lines.Select(l => "•  " + l)) +
                "\n\nGoodie Bag will restart. A copy of your current settings is kept (config.before-import-….json in the settings folder). Start with Windows and changes already made to Windows aren't affected.",
                "Import and restart", danger: true))
            return;
        if (_app.Demo)
        {
            _app.Notify("Demo mode", "Demo data is never saved, so nothing was imported.", NoticeKind.Info);
            return;
        }
        try
        {
            _app.FlushPendingSave();
            ConfigStore.ReplaceWith(imported);
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't import", ex.Message, NoticeKind.Error, important: true);
            return;
        }
        App.RelaunchAfterExit("");
    }

    private KeybindTree? _keybindTree;
    private int _keybindBuild;
    private Border? _keybindHost;

    private void AfterFirstFrame(Action action)
    {
        void OnFrame(object? sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnFrame;
            Motion.AfterPageSettles(Dispatcher, action);
        }
        CompositionTarget.Rendering += OnFrame;
    }
    private Action? _syncArrange;
    private Button? _keybindFind;
    private TextBox? _keybindSearch;

    public bool CancelEdit()
    {
        if (_keybindTree?.CancelArrange() != true) return false;
        _syncArrange?.Invoke();
        return true;
    }

    private FrameworkElement BuildKeybinds()
    {
        _keybindTree = null;
        _keybindHost = null;
        var arrange = new Button { Style = Ui.Res<Style>("GhostButton") };
        var reset = Ui.LabeledButton("I.Undo", "Default order", () =>
        {
            if (_keybindTree is null) return;
            _keybindTree.ResetOrder();
            _app.Notify("Keybinds", "The categories are back in their default order.", NoticeKind.Info);
        }, "GhostButton");
        reset.ToolTip = "Put the main categories back in their default order (keys and the keybinds inside stay as they are)";
        reset.Margin = new Thickness(8, 0, 0, 0);
        var search = new TextBox { Width = 220, Tag = "Search keybinds", VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(search, "Search keybinds");
        search.TextChanged += (_, _) => { if (_keybindTree is not null) _keybindTree.Query = search.Text; };
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || search.Text.Length == 0) return;
            search.Clear();
            e.Handled = true;
        };
        var find = new Button { Style = Ui.Res<Style>("GhostButton"), Margin = new Thickness(8, 0, 0, 0) };
        void SyncFind()
        {
            bool open = search.Visibility == Visibility.Visible;
            find.Content = Ui.IconLabel(open ? "I.X" : "I.Search", open ? "Close search" : "Search");
            find.ToolTip = open ? "Clear the search and show every keybind" : "Find a keybind by its name, key or category";
        }
        find.Click += (_, _) =>
        {
            bool open = search.Visibility != Visibility.Visible;
            search.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open) Dispatcher.InvokeAsync(() => search.Focus(), System.Windows.Threading.DispatcherPriority.Input);
            else search.Clear();
            SyncFind();
        };
        void SyncArrange()
        {
            bool on = _keybindTree?.Arranging == true;
            arrange.Content = Ui.IconLabel(on ? "I.Check" : "I.Layers", on ? "Done" : "Arrange");
            arrange.Style = Ui.Res<Style>(on ? "PrimaryButton" : "GhostButton");
            arrange.ToolTip = on ? "Finish arranging (Esc puts the order back)" : "Drag the main categories into your own order";
        }
        _syncArrange = SyncArrange;
        arrange.Click += (_, _) =>
        {
            if (_keybindTree is null) return;
            _keybindTree.Arranging = !_keybindTree.Arranging;
            SyncArrange();
        };
        _keybindFind = find;
        _keybindSearch = search;
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Children = { search, arrange, reset, find } };
        var host = new Border { MinHeight = 120 };
        _keybindHost = host;
        int build = ++_keybindBuild;
        AfterFirstFrame(() =>
        {
            if (build != _keybindBuild) return;
            if (!_shown) { _builtFor = null; return; }
            _keybindTree = new KeybindTree(_app, _ => true, navigate: _window.Navigate) { Query = search.Text };
            _keybindsFor = KeybindSignature();
            host.MinHeight = 0;
            host.Child = _keybindTree;
            SyncArrange();
        });
        var icon = new NavIcon { Glyph = "I.Keyboard", IsActive = true, Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(NavIcon.ForegroundProperty, "AccentText");
        var title = Ui.Text("Keybinds", "H1");
        title.VerticalAlignment = VerticalAlignment.Center;
        title.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        var name = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, title } };
        tools.VerticalAlignment = VerticalAlignment.Center;
        var view = new StackPanel();
        view.Children.Add(Ui.SplitRow(name, tools));
        view.Children.Add(new TextBlock
        {
            Text = "Every keyboard shortcut in Goodie Bag, plus iRacing's own keys (saved to its controls file), grouped by where it comes from. Click a box and press the keys; Esc clears it. Keybinds of plugins that are off are hidden (and kept).",
            Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, Ui.HeaderGap),
        });
        view.Children.Add(host);
        SyncArrange();
        SyncFind();
        Ui.Anchor(view, "settings.keybinds");
        return view;
    }

    private void BuildHardware()
    {
        var access = Section("Hardware access", "I.Cpu", "settings.access");
        access.Children.Add(StatusRow("Administrator", _app.IsAdmin || _app.Demo,
            _app.Demo ? "Demo mode: nothing on this PC is changed, so administrator rights aren't needed." : _app.IsAdmin ? "Running as administrator: FPS counting, sensors and fan control are available." : "Not running as administrator, so FPS, CPU temperature and fan control are limited."));
        access.Children.Add(Ui.Divider());
        var pawnState = _app.Hardware.PawnIoState;
        bool pawnOk = pawnState == PawnIoState.Installed;
        var pawn = StatusRow("PawnIO driver", pawnOk, pawnState switch
        {
            PawnIoState.Installed => _app.Hardware.CpuTempMissing
                ? "Installed, but the CPU's temperature sensors didn't answer. Restart Goodie Bag; if it stays missing, this CPU may not be supported yet."
                : "Installed.",
            PawnIoState.RemovalPending => "Uninstalled, but still loaded: programs still have it open (Goodie Bag, other fan or monitoring tools), so Windows can't remove it yet. Restart Windows to finish; it can be installed again after that.",
            _ => "Not installed. CPU temperatures and motherboard fan headers need the free PawnIO driver. Install it, then restart Goodie Bag.",
        });
        if (pawnState == PawnIoState.Missing)
        {
            var install = Ui.LabeledButton("I.Download", "Install PawnIO", InstallPawnIo, "PrimaryButton");
            Grid.SetColumn(install, 1);
            pawn.Children.Add(install);
        }
        access.Children.Add(pawn);
        access.Children.Add(Explainer());
        access.Children.Add(Ui.Divider());
        var hw = _app.Hardware;
        var catalog = hw.Catalog;
        access.Children.Add(StatusRow("Sensors", hw.OpenError is null,
            hw.OpenError ?? (!hw.Running ? "Idle: no plugin or open page needs them right now."
                : $"{GoodieBag.SimRacing.Race.RaceFormat.Count(catalog?.Temperatures.Count ?? 0, "temperature sensor")} and {GoodieBag.SimRacing.Race.RaceFormat.Count(catalog?.Fans.Count ?? 0, "fan")} detected.")));
    }

    private FrameworkElement Explainer()
    {
        var details = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        void Para(string title, string text)
        {
            details.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
            details.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
        }
        Para("What it is",
            "PawnIO is a small, free, open-source Windows driver. Windows only lets drivers read certain chips directly: the CPU's own temperature and power sensors, and the motherboard's fan controller. The sensor library Goodie Bag uses talks to those chips through PawnIO. It replaces the older WinRing0 driver, which Windows Defender now flags as unsafe.");
        Para("What it's used for here",
            "Reading CPU temperature, CPU power and motherboard sensors, and setting motherboard fan speeds when Fan Control is on. Nothing else. Goodie Bag doesn't send data anywhere and has no account or telemetry.");
        Para("Is it required?",
            "No. Without it, everything else works: GPU temperatures and fans, frame rate, displays, modes, overlay, sim racing tools. CPU temperature shows as “Not available” and motherboard fans can't be controlled.");
        Para("Is it safe?",
            "The driver is signed for Windows and works through small modules written for one job each (an AMD or Intel CPU's sensors, a fan controller chip), instead of the old driver's open access to any hardware address. Some anti-cheat systems are strict about any low-level driver; if a game ever complains, uninstall PawnIO and Goodie Bag carries on without it.");
        Para("How to install or remove it",
            "Install PawnIO (above) runs “winget install namazso.PawnIO” in a window you can watch. Remove it any time from Windows Settings → Apps → Installed apps → PawnIO, or “winget uninstall namazso.PawnIO”. Restart Goodie Bag after either.");
        Para("Why Goodie Bag runs as administrator",
            "Counting frames (Windows' own event tracing), reading sensors, controlling fans, changing displays and watching which programs start all need it. Windows asks each time it starts; with “Start with Windows” it starts elevated at sign-in without a prompt.");
        var toggle = Ui.LabeledButton("I.Info", "What is PawnIO, and is it safe?", () => { }, "GhostButton");
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Margin = new Thickness(0, 2, 0, 2);
        toggle.Click += (_, _) => details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        var stack = new StackPanel();
        stack.Children.Add(toggle);
        stack.Children.Add(details);
        Ui.Anchor(stack, "settings.pawnio");
        return stack;
    }

    private StackPanel _updates = new();

    private void BuildAboutAndUpdates()
    {
        var section = Section("About & updates", "I.Info", "settings.updates");
        Ui.Anchor(section, "settings.about");
        var about = new Grid { Margin = new Thickness(0, 2, 0, 8) };
        about.ColumnDefinitions.Add(new ColumnDefinition());
        about.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = $"Goodie Bag {UpdateService.Tag(_app.Updates.Installed)}" + (_app.Demo ? " · demo data" : ""), Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = "A PC control center for sim rigs: window profiles that size and place your games, display profiles, hardware monitoring, the overlay, fan control and sim racing tools.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        about.Children.Add(text);
        var project = Ui.LabeledButton("I.ChevronRight", "Project page", () => Ui.OpenUrl(UpdateService.ProjectPage), "GhostButton");
        var changelog = Ui.LabeledButton("I.ChevronRight", "Changelog", () => ChangelogDialog.Show(_window, _app.Updates, null, justUpdated: false), "GhostButton");
        changelog.Margin = new Thickness(0, 0, 8, 0);
        var links = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        links.Children.Add(changelog);
        links.Children.Add(project);
        Grid.SetColumn(links, 1);
        about.Children.Add(links);
        section.Children.Add(about);
        section.Children.Add(Ui.Divider());
        _updates = new StackPanel();
        section.Children.Add(_updates);
        FillUpdates();
    }

    private void FillUpdates()
    {
        var u = _app.Updates;
        _updates.Children.Clear();
        _updates.Children.Add(new TextBlock { Text = ThemeManager.Case("Updates"), Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 8, 0, 4) });

        if (u.Available && u.Latest is { } r)
        {
            var box = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(Ui.Badge("I.Download", "Accent", "Knob", 32, 10, 17));
            var title = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = "A new version of Goodie Bag is available", Style = Ui.Res<Style>("H3") });
            title.Children.Add(new TextBlock { Text = $"{UpdateService.Tag(u.Installed)} → {UpdateService.Tag(r.Version)}. Install it here; your settings stay.", Style = Ui.Res<Style>("Caption") });
            head.Children.Add(title);
            box.Children.Add(head);
            var buttons = new WrapPanel { Margin = new Thickness(44, 12, 0, 0) };
            if (u.State == UpdateState.Downloading)
            {
                buttons.Children.Add(new Meter { Width = 260, Height = 8, Steps = 8, Value = u.Progress, VerticalAlignment = VerticalAlignment.Center });
                buttons.Children.Add(new TextBlock { Text = $"Downloading… {u.Progress:0%}", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                bool self = r.ExeUrl is not null && UpdateService.CanSelfInstall && !_app.Demo;
                var install = Ui.LabeledButton("I.Download", self ? "Install and restart" : "Download", () => _ = u.InstallAsync(), "PrimaryButton");
                install.ToolTip = self ? "Downloads the new version, closes Goodie Bag, swaps the file and starts it again (your settings stay)"
                    : "Opens the release page to download it";
                buttons.Children.Add(Spaced(install));
            }
            box.Children.Add(buttons);
            _updates.Children.Add(new Tile { Background = Ui.Res<Brush>("AccentSoft"), BorderBrush = Ui.Res<Brush>("Accent"), BorderThickness = new Thickness(1.5), CornerRadius = ThemeManager.Round(12), Padding = new Thickness(14), Margin = new Thickness(0, 4, 0, 10), Child = box });
        }

        string status = u.State switch
        {
            UpdateState.NotConfigured => "Update checks aren't set up in this build.",
            UpdateState.Checking => "Checking…",
            UpdateState.UpToDate => $"You have the latest version ({UpdateService.Tag(u.Installed)}).",
            UpdateState.Available or UpdateState.Downloading => "A newer version is ready (above).",
            UpdateState.Failed => $"The last check didn't work: {u.Error}",
            _ => _app.Config.Updates.LastCheckUtc is { } last ? $"Last checked {Dates.ShortTime(last.ToLocalTime())}." : "Not checked yet.",
        };
        var check = Ui.LabeledButton("I.Refresh", "Check now", () => _ = u.CheckAsync(manual: true));
        check.IsEnabled = u.State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.NotConfigured);
        _updates.Children.Add(Ui.SettingRow("Check for updates", status, check));
        _updates.Children.Add(Ui.Divider());
        var when = Ui.Segmented(new[] { "On start", "Daily", "Weekly", "Manually" }, (int)_app.Config.Updates.Check, i =>
        {
            _app.Config.Updates.Check = (UpdateCheck)i;
            _app.Save();
            u.ApplySchedule();
        }, "UpdateCheck", new[] { "Every time Goodie Bag starts", "Once a day", "Once a week", "Only when you click Check now" });
        _updates.Children.Add(Ui.SettingRow("When to check", "Nothing is downloaded until you click Install.", when));
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("sidebar-right", () =>
        {
            U.SidebarRight = true;
            _window.ApplySidebarSide();
            Build();
            _window.GoTo("settings", "settings.sidebar", flash: false);
            return Task.CompletedTask;
        });
        yield return ("sidebar-left", () =>
        {
            U.SidebarRight = false;
            _window.ApplySidebarSide();
            Build();
            return Task.CompletedTask;
        });
        yield return ("topography", () =>
        {
            U.TopoBackground = true;
            _window.Topography.Refresh();
            Build();
            _topoRow?.BringIntoView();
            return Task.CompletedTask;
        });
        yield return ("topography-off", () =>
        {
            U.TopoBackground = false;
            _window.Topography.Refresh();
            Build();
            return Task.CompletedTask;
        });
        yield return ("keybinds", async () =>
        {
            ShowSection(KeybindsId);
            for (int i = 0; i < 40 && _keybindTree is null; i++) await Task.Delay(50);
        });
        yield return ("keybinds-iracing", () =>
        {
            var open = new[] { "", "/Driving", "/In-car adjustments", "/Replay and TV cameras" }.Select(g => HotkeyCatalog.IracingGroup + g).ToList();
            U.KeybindsFolded.AddRange(open);
            _keybindTree?.Rebuild();
            U.KeybindsFolded.RemoveAll(open.Contains);
            _keybindTree?.UpdateLayout();
            _keybindTree?.ShowGroup(HotkeyCatalog.IracingGroup);
            return Task.CompletedTask;
        });
        yield return ("keybinds-arrange", () =>
        {
            if (_keybindTree is not null) _keybindTree.Arranging = true;
            return Task.CompletedTask;
        });
        yield return ("keybinds-default-order", () =>
        {
            _keybindTree?.ResetOrder();
            return Task.CompletedTask;
        });
        yield return ("keybinds-done", () =>
        {
            if (_keybindTree is not null) _keybindTree.Arranging = false;
            _syncArrange?.Invoke();
            return Task.CompletedTask;
        });
        yield return ("keybinds-search", () =>
        {
            _keybindFind?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (_keybindSearch is not null) _keybindSearch.Text = "next";
            return Task.CompletedTask;
        });
        yield return ("keybinds-search-closed", () =>
        {
            _keybindFind?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return Task.CompletedTask;
        });
        yield return ("data-section", () =>
        {
            ShowSection("data");
            if (_sections.Visibility != Visibility.Visible) throw new InvalidOperationException("Picking a settings card in the sidebar didn't leave Keybinds.");
            return Task.CompletedTask;
        });
        yield return ("sidebar-all-open", () =>
        {
            U.SidebarAllOpen = true;
            _window.RefreshSidebar();
            return Task.CompletedTask;
        });
        yield return ("sidebar-open-tool-only", () =>
        {
            U.SidebarAllOpen = false;
            _window.RefreshSidebar();
            ShowSection("app");
            return Task.CompletedTask;
        });
        yield return ("confirm-delete", () =>
        {
            _ = _window.Confirm("Delete saved colors?", "“Dusk” is removed from My colors. The colors in use now don't change.", "Delete", danger: true);
            return Task.CompletedTask;
        });
        yield return ("confirm-delete-closed", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        yield return ("pawnio-prompt", () =>
        {
            _ = OfferPawnIoAsync(force: true);
            return Task.CompletedTask;
        });
        yield return ("pawnio-prompt-closed", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        var theme = U.Theme;
        yield return ("custom-colors-grayed", () =>
        {
            _window.GoTo("settings", "settings.customcolors", flash: false);
            return Task.CompletedTask;
        });
        yield return ("custom-colors-on", () =>
        {
            U.OledCustom.Enabled = true;
            U.OledCustom.Colors = CustomPalette.Presets[0].Colors.Copy();
            Themes.SetOledCustom(U.OledCustom);
            _app.SetTheme(AppTheme.Custom);
            _window.GoTo("settings", "settings.customcolors", flash: false);
            return Task.CompletedTask;
        });
        yield return ("custom-colors-main-page", () =>
        {
            _window.Navigate(_window.FirstPage);
            return Task.CompletedTask;
        });
        yield return ("custom-colors-back", () =>
        {
            U.OledCustom.Enabled = false;
            U.OledCustom.Colors = new ThemeColors();
            Themes.SetOledCustom(U.OledCustom);
            _app.SetTheme(theme);
            _window.Navigate("settings");
            return Task.CompletedTask;
        });
    }

    private Grid ResetRow()
    {
        var button = Ui.LabeledButton("I.Undo", "Reset Goodie Bag", () => _ = ResetAsync(), "DangerButton");
        button.ToolTip = "Asks first. Your current settings are kept as a copy in the settings folder.";
        var row = Ui.SettingRow("Back to defaults",
            "Every setting, mode, profile, layout and fan curve. Goodie Bag restarts; a copy of your settings is kept.",
            button);
        Ui.Anchor(row, "settings.reset");
        return row;
    }

    private async Task ResetAsync()
    {
        if (_app.Demo)
        {
            _app.Notify("Demo mode", "Demo data is never saved, so there's nothing to reset.", NoticeKind.Info);
            return;
        }
        if (!await _window.Confirm("Reset Goodie Bag to defaults?",
                "Everything goes back to how it was on first install: window profiles and groups, display profiles, " +
                "fan curves and calibration, overlay layouts, page layouts, the sidebar order and these settings. Goodie Bag then restarts.\n\n" +
                "Kept: a copy of your current settings (config.before-reset-….json in the settings folder, rename it to config.json to go back), " +
                "iRacing backups, the PawnIO driver and the Start with Windows task. Changes already made to Windows (displays, sound) aren't undone.",
                "Reset and restart", danger: true))
            return;
        try
        {
            ConfigStore.ResetToDefaults();
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't reset", ex.Message, NoticeKind.Error, important: true);
            return;
        }
        App.RelaunchAfterExit("");
    }

    private static Grid Notice(string soft, string deep, string icon, string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = Ui.Badge(icon, soft, deep, 28, 8, 15);
        badge.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(badge);
        var t = new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(t, 1);
        grid.Children.Add(t);
        return grid;
    }

    private StackPanel Section(string title, string icon, string? anchor = null)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(new Tile { Style = Ui.Res<Style>("IconBadge"), Width = 30, Height = 30, CornerRadius = ThemeManager.Round(9),
            Child = new Icon { Data = Ui.Res<Geometry>(icon), Width = 16, Height = 16 } });
        head.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("H2"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(head);
        var card = new Tile { Style = Ui.Res<Style>("Card"), Child = stack };
        if (anchor is not null)
            Ui.Anchor(card, anchor);
        _sections.Children.Add(card);
        return stack;
    }

    private static Grid StatusRow(string title, bool ok, string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var row = new DockPanel();
        var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = Ui.Res<Brush>(ok ? "Sage" : "Apricot"), Margin = new Thickness(0, 6, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        var text2 = new StackPanel();
        text2.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        text2.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 12, 0) });
        row.Children.Add(text2);
        grid.Children.Add(row);
        return grid;
    }

    private static FrameworkElement Spaced(FrameworkElement e)
    {
        e.Margin = new Thickness(0, 0, 8, 8);
        return e;
    }

    private static bool s_pawnIoOffered;
    private bool _pawnIoOpen;

    internal async Task OfferPawnIoAsync(bool force = false)
    {
        if (_pawnIoOpen || !force && (_app.Demo || U.HidePawnIoPrompt || _app.Hardware.PawnIoState != PawnIoState.Missing))
            return;
        _pawnIoOpen = true;
        try
        {
            var stack = new StackPanel { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
            void Para(string text) => stack.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            Para("CPU temperatures and motherboard fans (case, CPU and pump headers) are read and controlled through PawnIO, a small free driver.");
            Para("It replaces WinRing0, the older driver Windows Defender now flags as unsafe, and is installed for the whole PC.");
            Para("Without it everything else still works: GPU temperatures and fans, frame rate, displays, modes and the overlays.");
            var hide = new CheckBox { Content = "Don't show this again", IsChecked = U.HidePawnIoPrompt, Margin = new Thickness(0, 16, 0, 0) };
            stack.Children.Add(hide);
            var card = Ui.Dialog(_window, "Install the PawnIO driver?", "It isn't installed on this PC.", stack,
                Ui.DialogButtons(_window, "Install", () => true), null, 540);
            bool install = await _window.ShowModal(card);
            if (hide.IsChecked == true != U.HidePawnIoPrompt)
            {
                U.HidePawnIoPrompt = hide.IsChecked == true;
                _app.Save();
            }
            if (install)
                InstallPawnIo();
        }
        finally
        {
            _pawnIoOpen = false;
        }
    }

    private void InstallPawnIo()
    {
        if (_app.Demo)
        {
            _app.Notify("Demo mode", "Nothing is installed in demo mode.", NoticeKind.Info);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/k winget install namazso.PawnIO -e && (echo. & echo Done. Restart Goodie Bag to use it.) || (echo. & echo If it says exit code 1072, an earlier copy is still waiting to be removed: restart Windows, then install again.)") { UseShellExecute = true })?.Dispose();
        }
        catch
        {
            Ui.OpenUrl("https://pawnio.eu");
        }
    }

    private static void OpenFolder(string folder)
    {
        if (!GoodieBag.Services.External.Allowed("open " + folder)) return;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true })?.Dispose();
        }
        catch { }
    }
}

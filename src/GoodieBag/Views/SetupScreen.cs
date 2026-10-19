using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Plugins;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal sealed class SetupScreen : UserControl
{
    private readonly AppServices _app;
    private readonly bool _firstRun;
    private readonly Action _done;
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly StackPanel _updateRow = new();
    private Grid _columns = new();
    private FrameworkElement? _left, _right;
    private StartupState? _startup;
    private bool _wide = true;

    private const double TwoColumnsAt = 1080;

    public SetupScreen(AppServices app, bool firstRun, Action done)
    {
        _app = app;
        _firstRun = firstRun;
        _done = done;
        Content = _scroll;
        SizeChanged += (_, e) => { if (e.WidthChanged) Arrange(); };
        Loaded += (_, _) => { Unsubscribe(); ThemeManager.Changed += Build; _app.Updates.Changed += FillUpdate; };
        Unloaded += (_, _) => Unsubscribe();
        Build();
        _ = LoadStartupAsync();
        if (firstRun) Loaded += FitWindowOnce;
    }

    private void FitWindowOnce(object sender, RoutedEventArgs e)
    {
        Loaded -= FitWindowOnce;
        Dispatcher.BeginInvoke(() =>
        {
            if (Window.GetWindow(this) is not { WindowState: WindowState.Normal } window || ScreenshotRunner.Active) return;
            double extra = _scroll.ExtentHeight - _scroll.ViewportHeight;
            if (extra <= 1) return;
            var area = WorkAreaOf(window);
            double height = Math.Min(area.Height, window.Height + extra * _app.Config.Ui.ScaleFactor + 8);
            window.Height = height;
            window.Top = area.Top + (area.Height - height) / 2;
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static Rect WorkAreaOf(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || PresentationSource.FromVisual(window)?.CompositionTarget is not { } target) return SystemParameters.WorkArea;
        var px = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var m = target.TransformFromDevice;
        var topLeft = m.Transform(new Point(px.Left, px.Top));
        var bottomRight = m.Transform(new Point(px.Right, px.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void Unsubscribe()
    {
        ThemeManager.Changed -= Build;
        _app.Updates.Changed -= FillUpdate;
    }

    private void Build()
    {
        Background = Ui.Res<Brush>("WindowBg");
        _left = Left();
        _right = Right();
        _columns = new Grid { MaxWidth = 1280, Margin = new Thickness(48, 44, 48, 32) };
        _columns.Children.Add(_left);
        _columns.Children.Add(_right);
        _scroll.Content = _columns;
        Arrange(force: true);
    }

    private void Arrange(bool force = false)
    {
        if (_left is null || _right is null) return;
        bool wide = ActualWidth <= 0 || ActualWidth >= TwoColumnsAt;
        if (!force && wide == _wide) return;
        _wide = wide;
        _columns.ColumnDefinitions.Clear();
        _columns.RowDefinitions.Clear();
        if (wide)
        {
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star), MinWidth = 360 });
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });
            Grid.SetColumn(_right, 2);
            Grid.SetRow(_right, 0);
        }
        else
        {
            _columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(_right, 0);
            Grid.SetRow(_right, 1);
        }
    }

    private FrameworkElement Left()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = new Icon { Data = Ui.Res<Geometry>("I.Bag"), StrokeThickness = 2.1, Foreground = Ui.Res<Brush>("LogoFg") };
        var tile = new Tile
        {
            Background = Ui.Res<Brush>("LogoBg"), BorderBrush = Ui.Res<Brush>("LogoEdge"),
            BorderThickness = ThemeManager.Current.LogoEdge, Child = glyph,
        };
        var word = new TextBlock { Foreground = Ui.Res<Brush>("Heading") };
        Ui.FitLockup(tile, glyph, word, maxHeight: 60, width: 400, gap: 18);
        brand.Children.Add(tile);
        brand.Children.Add(word);
        stack.Children.Add(brand);

        stack.Children.Add(new TextBlock { Text = _firstRun ? "WELCOME" : "SETUP", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(2, 30, 0, 6) });
        stack.Children.Add(new TextBlock { Text = "Set up your workspace", Style = Ui.Res<Style>("H1"), FontSize = 32, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock
        {
            Text = "Pick a look and the tools you want. Everything here can be changed later in Settings.",
            Style = Ui.Res<Style>("Muted"), FontSize = 14.5, Margin = new Thickness(0, 8, 0, 22), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left,
        });

        stack.Children.Add(Section("Look", "I.Palette", "Pick a theme; this screen shows the one you pick.",
            new ThemePicker(_app.Config.Ui.Theme, PickTheme, columns: 2, height: 64, oled: set => { _app.UseOledColors(set); PickTheme(AppTheme.Custom); },
                pastel: preset => { _app.UsePastelPreset(preset); PickTheme(AppTheme.OledPastel); }) { Margin = new Thickness(0, 2, -10, -10) }));
        stack.Children.Add(Section("Start", "I.Power", null, StartOptions()));

        var finish = new Button
        {
            Content = _firstRun ? "Get started" : "Done", Style = Ui.Res<Style>("PrimaryButton"), FontSize = 15, Padding = new Thickness(28, 9, 28, 9), Height = double.NaN,
            IsDefault = true, HorizontalAlignment = HorizontalAlignment.Left,
        };
        finish.Click += (_, _) => _done();
        finish.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(finish);
        return stack;
    }

    private void PickTheme(AppTheme theme)
    {
        if (_firstRun)
        {
            foreach (var layout in _app.Config.Overlay.Layouts)
                layout.Colors = OverlayColorPreset.App;
        }
        _app.SetTheme(theme);
    }

    private static FrameworkElement Section(string title, string icon, string? text, UIElement body)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, text is null ? 10 : 4) };
        head.Children.Add(new Tile { Style = Ui.Res<Style>("IconBadge"), Width = 30, Height = 30, Child = new Icon { Data = Ui.Res<Geometry>(icon), Width = 16, Height = 16 } });
        head.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("H2"), FontSize = 19, Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(head);
        if (text is not null)
            stack.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(41, 0, 0, 12) });
        stack.Children.Add(body);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(20, 18, 20, 18), Margin = new Thickness(0, 0, 0, 16), Child = stack };
    }

    private FrameworkElement Right()
    {
        var stack = new StackPanel();
        stack.Children.Add(Section("Features", "I.Plug", "Turn on what you'll use. A plugin that's off has no page, keybind or background work.", Plugins()));
        return stack;
    }

    private FrameworkElement Plugins()
    {
        var panel = new StackPanel();
        var list = new StackPanel();
        var switches = new List<CheckBox>();
        var overlay = _app.Plugins.Find(PluginIds.Overlay);
        foreach (var plugin in PluginManager.InNavOrder(_app.Plugins.All, _app.Config.Ui.NavOrder))
        {
            if (plugin.Id == PluginIds.Overlay) continue;
            var (soft, deep) = plugin.Colors;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var badge = Ui.Badge(plugin.Icon, soft, deep, 30, 8, 16);
            badge.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(badge);
            var text = new StackPanel { Margin = new Thickness(11, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = plugin.Name, Style = Ui.Res<Style>("H3"), TextTrimming = TextTrimming.CharacterEllipsis }));
            text.Children.Add(new TextBlock { Text = plugin.Description, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var toggle = Ui.Switch(_app.Plugins.IsEnabled(plugin), on => _app.Plugins.SetEnabled(plugin, on));
            toggle.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(toggle, plugin.Name);
            Grid.SetColumn(toggle, 2);
            row.Children.Add(toggle);
            switches.Add(toggle);
            FrameworkElement body = row;
            if (plugin.Id == PluginIds.HardwareMonitor && overlay is not null)
            {
                var both = new StackPanel();
                both.Children.Add(row);
                var sub = new Grid { Margin = new Thickness(41, 9, 0, 0) };
                sub.ColumnDefinitions.Add(new ColumnDefinition());
                sub.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var subText = new StackPanel { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                subText.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = overlay.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }));
                subText.Children.Add(new TextBlock { Text = "FPS, temperatures, load and fan speeds drawn on top of your game, toggled with a keybind.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                sub.Children.Add(subText);
                var subToggle = Ui.Switch(_app.Plugins.IsEnabled(overlay), on => _app.Plugins.SetEnabled(overlay, on));
                subToggle.VerticalAlignment = VerticalAlignment.Center;
                System.Windows.Automation.AutomationProperties.SetName(subToggle, overlay.Name);
                Grid.SetColumn(subToggle, 1);
                sub.Children.Add(subToggle);
                switches.Add(subToggle);
                var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(41, 9, 0, 0) };
                rule.SetResourceReference(Border.BorderBrushProperty, "Line");
                both.Children.Add(rule);
                both.Children.Add(sub);
                body = both;
            }
            list.Children.Add(new Border { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(11, 9, 11, 9), Margin = new Thickness(0, 0, 0, 8), Child = body });
        }
        panel.Children.Add(list);

        var bulk = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        void All(bool on)
        {
            _app.Plugins.SetEnabled(_ => on);
            foreach (var toggle in switches)
                toggle.IsChecked = on;
        }
        var allOn = new Button { Content = "Turn all on", Style = Ui.Res<Style>("SoftButton"), Margin = new Thickness(0, 0, 8, 0) };
        allOn.Click += (_, _) => All(true);
        var allOff = new Button { Content = "Turn all off", Style = Ui.Res<Style>("GhostButton") };
        allOff.Click += (_, _) => All(false);
        bulk.Children.Add(allOn);
        bulk.Children.Add(allOff);
        panel.Children.Add(bulk);
        return panel;
    }

    private FrameworkElement StartOptions()
    {
        var u = _app.Config.Ui;
        var panel = new StackPanel();
        var start = Ui.Switch(_startup?.Enabled == true, on => _ = SetStartupAsync(on));
        start.IsEnabled = !_app.Demo && _startup is not null;
        panel.Children.Add(Row(Ui.SettingRow("Start with Windows",
            _app.Demo ? "Not available in demo mode." : "Starts when you sign in, so modes, keybinds and fans are ready. With Start in background on, it starts in the tray.", start)));
        panel.Children.Add(Ui.Divider());
        panel.Children.Add(Row(Ui.SettingRow("Start in background", "Opens in the tray instead of this window whenever Goodie Bag starts. Click the bag icon in the tray to open it.",
            Ui.Switch(u.StartMinimized, v => { u.StartMinimized = v; _app.Save(); }))));
        panel.Children.Add(Ui.Divider());
        panel.Children.Add(Row(Ui.SettingRow("Close in background", "The X button keeps Goodie Bag running in the tray (keybinds, fans, overlay). Off: X quits.",
            Ui.Switch(u.CloseToTray, v => { u.CloseToTray = v; _app.Save(); }))));
        panel.Children.Add(Ui.Divider());
        FillUpdate();
        (_updateRow.Parent as Panel)?.Children.Remove(_updateRow);
        panel.Children.Add(_updateRow);
        return panel;

        static Grid Row(Grid row)
        {
            row.Margin = new Thickness(0, 7, 0, 7);
            return row;
        }
    }

    private void FillUpdate()
    {
        var u = _app.Updates;
        string status = u.State switch
        {
            UpdateState.Checking => "Checking…",
            UpdateState.UpToDate => "You have the latest version.",
            UpdateState.Available or UpdateState.Downloading => "A new version is available.",
            UpdateState.Failed => $"Couldn't check: {u.Error}",
            UpdateState.NotConfigured => "Update checks aren't set up in this build.",
            _ => "If you downloaded Goodie Bag a while ago, check for a newer version.",
        };
        Button action;
        if (u.Available)
        {
            action = Ui.LabeledButton("I.Download", u.State == UpdateState.Downloading ? $"Downloading… {u.Progress:0%}" : "Install update", () => _ = u.InstallAsync(), "PrimaryButton");
            action.IsEnabled = u.State != UpdateState.Downloading;
        }
        else
        {
            action = Ui.LabeledButton("I.Refresh", "Check for updates", () => _ = u.CheckAsync(manual: true));
            action.IsEnabled = u.State is not (UpdateState.Checking or UpdateState.NotConfigured);
        }
        var row = Ui.SettingRow("Updates", status, action);
        row.Margin = new Thickness(0, 7, 0, 2);
        _updateRow.Children.Clear();
        _updateRow.Children.Add(row);
    }

    private async Task LoadStartupAsync()
    {
        if (_app.Demo) return;
        try { _startup = await Task.Run(StartupManager.State); }
        catch { _startup = null; }
        Build();
    }

    private async Task SetStartupAsync(bool on)
    {
        if (_app.Demo) return;
        try
        {
            await Task.Run(() => StartupManager.SetEnabled(on));
            _startup = await Task.Run(StartupManager.State);
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't change start with Windows", ex.Message, NoticeKind.Warning);
        }
        Build();
    }
}

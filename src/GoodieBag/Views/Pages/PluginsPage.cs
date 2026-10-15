using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Plugins;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class PluginsPage : UserControl, IPage, IScreenshotStates, ICancelEdit
{
    private const double RowHeight = Ui.CardRowHeight, IconSize = 44, TitleSize = Ui.CardTitleSize, RowSpacing = 12, CardsWidth = 1280,
        InfoFrom = 760, ThemeFrom = 620,
        GreetingSize = 42;

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private readonly Grid _header = new() { Margin = new Thickness(0, 0, 0, 22), MaxWidth = CardsWidth };
    private readonly TextBlock _greeting = new();
    private readonly Border _quoteBox = new();
    private readonly TextBlock _quoteText = new(), _quoteAuthor = new();
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _settingsButton, _infoButton;
    private Button? _themeButton;
    private readonly ContentFrame _cardHost = new() { DesignWidth = CardsWidth, Centered = true };
    private readonly List<Card> _cards = new();
    private PanelGrid _grid = null!;
    private DispatcherTimer? _quoteTimer;
    private bool _info, _shown;

    private sealed class Card
    {
        public required string Id;
        public Plugin? Plugin;
        public required NavIcon Icon;
        public required FrameworkElement Content;
        public required Brush Edge, HoverFill;
        public required Facts Info;
    }

    private sealed record Facts(string Name, string Description, string Icon, (string Soft, string Deep) Colors, string Reads, string Runs, string Changes);

    private readonly ScrollViewer _pageScroll;
    private readonly Grid _infoLayer = new() { Visibility = Visibility.Collapsed };

    public PluginsPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        _pageScroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = _body, Focusable = false };
        Content = new Grid { Children = { _pageScroll, _infoLayer } };

        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _greeting.Style = Ui.Res<Style>("H1");
        _greeting.FontSize = GreetingSize;
        _greeting.VerticalAlignment = VerticalAlignment.Center;
        _header.Children.Add(_greeting);
        _quoteBox.SetResourceReference(Border.BorderBrushProperty, "Line");
        _quoteBox.BorderThickness = new Thickness(2, 0, 0, 0);
        _quoteBox.Padding = new Thickness(16, 2, 0, 2);
        _quoteBox.Margin = new Thickness(0, 14, 0, 0);
        _quoteBox.HorizontalAlignment = HorizontalAlignment.Left;
        _quoteBox.Visibility = Visibility.Collapsed;
        _quoteText.Style = Ui.Res<Style>("Muted");
        _quoteText.FontStyle = FontStyles.Italic;
        _quoteText.FontSize = 14;
        _quoteText.LineHeight = 19;
        _quoteText.TextWrapping = TextWrapping.Wrap;
        _quoteText.TextTrimming = TextTrimming.WordEllipsis;
        _quoteText.MaxHeight = 38;
        _quoteAuthor.Style = Ui.Res<Style>("Caption");
        _quoteAuthor.TextTrimming = TextTrimming.CharacterEllipsis;
        _quoteAuthor.Margin = new Thickness(0, 3, 0, 0);
        var ghost = new StackPanel { Visibility = Visibility.Hidden, Children =
        {
            new TextBlock { Text = "A\nA", Style = _quoteText.Style, FontStyle = FontStyles.Italic, FontSize = 14, LineHeight = 19 },
            new TextBlock { Text = "A", Style = _quoteAuthor.Style, Margin = _quoteAuthor.Margin },
        } };
        _quoteBox.Child = new Grid { Children = { ghost, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _quoteText, _quoteAuthor } } } };
        Grid.SetRow(_quoteBox, 1);
        Grid.SetColumnSpan(_quoteBox, 2);
        _header.Children.Add(_quoteBox);

        _infoButton = Ui.LabeledButton("I.Info", "Info", () => SetInfo(!_info), "GhostButton");
        _infoButton.ToolTip = "What each tool reads, runs in the background and changes on your PC";
        _infoButton.Margin = new Thickness(8, 0, 0, 0);
        _settingsButton = Ui.LabeledButton("I.Tune", "Settings", () => _window.Navigate("settings"), "GhostButton");
        _settingsButton.ToolTip = "Settings: startup, look, plugins, keybinds, updates";
        _settingsButton.Margin = new Thickness(8, 0, 0, 0);
        if (Themes.Variant(ThemeManager.Current) is { } variant)
        {
            _themeButton = Ui.IconButton(ThemeManager.Current.Dark ? "I.Sun" : "I.Moon", $"Switch to {variant.Name}", () => _app.SetTheme(variant.Id));
            _buttons.Children.Add(_themeButton);
        }
        else if (Themes.OledSibling(ThemeManager.Current) is { } sibling)
        {
            _themeButton = Ui.IconButton(sibling.Id == AppTheme.OledPastel ? "I.Palette" : "I.Sparkles", $"Switch to {sibling.Name}", () => _app.SetTheme(sibling.Id));
            _buttons.Children.Add(_themeButton);
        }
        _buttons.Children.Add(_infoButton);
        _buttons.Children.Add(_settingsButton);
        Grid.SetColumn(_buttons, 1);
        _header.Children.Add(_buttons);
        _header.SizeChanged += (_, e) => FitHeader(e.NewSize.Width);
        _body.Children.Add(_header);
        _body.Children.Add(_cardHost);
        Ui.Anchor(_cardHost, "plugins.order");
        Build();
    }

    private void FitHeader(double width)
    {
        if (width <= 0) return;
        _infoButton.Visibility = width >= InfoFrom ? Visibility.Visible : Visibility.Collapsed;
        if (_themeButton is not null) _themeButton.Visibility = width >= ThemeFrom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NextQuote()
    {
        if (Quotes.Next() is not { } quote) { _quoteBox.Visibility = Visibility.Collapsed; return; }
        _quoteText.Text = $"“{quote.Text}”";
        _quoteAuthor.Text = quote.Author.Length > 0 ? "— " + quote.Author : "";
        _quoteBox.ToolTip = _quoteText.Text + (quote.Author.Length > 0 ? "\n" + _quoteAuthor.Text : "");
        _quoteBox.Visibility = Visibility.Visible;
    }

    private void Build()
    {
        _cards.Clear();
        foreach (var slot in _app.Config.Ui.LandingLayout) slot.Cols = slot.Rows = null;
        _grid = new PanelGrid(_app.Config.Ui.LandingLayout, SaveOrder) { LiveEdit = true, RowHeight = 1, Gap = 0, PanelSpacing = RowSpacing };
        _cardHost.Child = _grid;
        int order = 0;
        foreach (var plugin in PluginManager.InNavOrder(_app.Plugins.Active, _app.Config.Ui.NavOrder))
        {
            var p = plugin;
            if (p.Id == PluginIds.Overlay) continue;
            AddCard(PluginCard(p), p.Id, p.Name, order++, () => Open(p));
        }
    }

    private void AddCard(Card card, string id, string label, int order, Action open)
    {
        _cards.Add(card);
        var tile = new Tile
        {
            Style = Ui.Res<Style>("Card"), Child = card.Content, Height = RowHeight, Cursor = System.Windows.Input.Cursors.Hand,
            BorderBrush = card.Edge, BorderThickness = new Thickness(1.5), Padding = new Thickness(16, 0, 20, 0),
            ToolTip = $"Open {label} (drag to arrange)",
        };
        AutomationProperties.SetName(tile, label);
        tile.MouseEnter += (_, _) => tile.Background = card.HoverFill;
        tile.MouseLeave += (_, _) => tile.ClearValue(Border.BackgroundProperty);
        _grid.Add(new PanelDef(id, label, _ => tile)
        {
            FullWidth = true, Order = order,
            VeilClick = open,
        });
    }

    private void SaveOrder()
    {
        var plugins = _app.Config.Ui.LandingLayout.Select(s => _app.Plugins.Find(s.Id)).OfType<Plugin>().ToList();
        _window.SetNavOrder(PluginManager.PageKeys(plugins));
        _app.Save();
    }

    private void Open(Plugin plugin)
    {
        if (plugin.Pages.Count > 0) _window.Navigate(plugin.Pages[0].Key);
        else _window.GoTo("settings", "settings.plugins");
    }

    private Card PluginCard(Plugin p) => MakeCard(p.Id, p, p.Pages.Count > 0 ? p.Pages[0].Icon : p.Icon, Hues.Tool(p.Id, p.Colors), p.Name, p.Description, p.Reads, p.BackgroundWork, p.Changes);

    private Card MakeCard(string id, Plugin? plugin, string icon, (string Soft, string Deep) colors, string name, string description, string reads, string runs, string changes)
    {
        var deep = Ui.Res<Brush>(colors.Deep);
        var navIcon = new NavIcon { Glyph = icon, Foreground = deep, Width = IconSize, Height = IconSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
        var title = new TextBlock { Text = Ui.Spaced(name), Style = Ui.Res<Style>("H3"), FontSize = TitleSize, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var face = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        face.Children.Add(navIcon);
        face.Children.Add(title);

        return new Card { Id = id, Plugin = plugin, Icon = navIcon, Content = face, Edge = Ui.Res<Brush>("Accent"), HoverFill = Ui.Res<Brush>(colors.Soft),
            Info = new Facts(name, description, icon, colors, reads, runs, changes) };
    }

    private void SetInfo(bool on)
    {
        _info = on;
        Ui.SetToggled(_infoButton, on);
        _pageScroll.Effect = on && Motion.Enabled && PcClass.Effects ? new BlurEffect { Radius = 14, KernelType = KernelType.Gaussian } : null;
        _pageScroll.IsHitTestVisible = !on;
        if (_shown) foreach (var card in _cards) card.Icon.IsActive = !on;
        _infoLayer.Children.Clear();
        _infoLayer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) return;
        var dim = new Border { Opacity = _pageScroll.Effect is null ? 0.92 : 0.6 };
        dim.SetResourceReference(Border.BackgroundProperty, "Bg");
        dim.MouseLeftButtonUp += (_, _) => SetInfo(false);
        _infoLayer.Children.Add(dim);
        var list = new StackPanel { MaxWidth = CardsWidth, Margin = new Thickness(28, 20, 28, 28) };
        var done = Ui.LabeledButton("I.Check", "Done", () => SetInfo(false), "PrimaryButton");
        done.ToolTip = "Close (Esc)";
        done.VerticalAlignment = VerticalAlignment.Top;
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text("What each tool does", "H1"));
        titles.Children.Add(new TextBlock { Text = "What it reads, what runs in the background and what it changes on your PC, for every tool that's on.", Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        list.Children.Add(Ui.SplitRow(titles, done));
        foreach (var card in _cards) list.Children.Add(InfoEntry(card.Info));
        var scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = list, Focusable = false };
        _infoLayer.Children.Add(scroll);
        RenderOptions.SetClearTypeHint(scroll, ClearTypeHint.Enabled);
        if (Motion.Enabled) { _infoLayer.Opacity = 0; Motion.FadeTo(_infoLayer, 1, 160); }
    }

    private static Tile InfoEntry(Facts f)
    {
        var stack = new StackPanel();
        var head = new DockPanel { LastChildFill = true };
        var badge = Ui.Badge(f.Icon, f.Colors.Soft, f.Colors.Deep, 40, 12, 22);
        badge.VerticalAlignment = VerticalAlignment.Top;
        badge.Margin = new Thickness(0, 0, 14, 0);
        head.Children.Add(badge);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text(f.Name, "H3"));
        titles.Children.Add(new TextBlock { Text = f.Description, Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        head.Children.Add(titles);
        stack.Children.Add(head);
        var line = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 4) };
        line.SetResourceReference(Border.BackgroundProperty, "Line");
        stack.Children.Add(line);
        foreach (var (label, text) in new[] { ("Reads", f.Reads), ("Runs in the background", f.Runs), ("Changes on your PC", f.Changes) })
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = ThemeManager.Case(label), Style = Ui.Res<Style>("Eyebrow"), FontSize = 13.5, Margin = new Thickness(0, 0, 16, 0), TextWrapping = TextWrapping.Wrap });
            var value = new TextBlock { Text = text, Style = Ui.Res<Style>("Body"), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            stack.Children.Add(row);
        }
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(20, 18, 20, 18), Margin = new Thickness(0, 14, 0, 0), Child = stack };
    }

    public bool CancelEdit()
    {
        if (!_info) return false;
        SetInfo(false);
        return true;
    }

    public void OnShown()
    {
        _shown = true;
        UpdateGreeting();
        NextQuote();
        _quoteTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
        _quoteTimer.Tick -= OnQuoteTick;
        _quoteTimer.Tick += OnQuoteTick;
        _quoteTimer.Start();
        _app.Plugins.Changed -= OnPluginsChanged;
        _app.Plugins.Changed += OnPluginsChanged;
        foreach (var card in _cards) card.Icon.IsActive = !_info;
        Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _shown = false;
        _quoteTimer?.Stop();
        _app.Plugins.Changed -= OnPluginsChanged;
        foreach (var card in _cards) card.Icon.IsActive = false;
    }

    private void OnQuoteTick(object? sender, EventArgs e)
    {
        UpdateGreeting();
        NextQuote();
    }

    private void UpdateGreeting() => _greeting.Text = Greeting(DateTime.Now.Hour);

    internal static string Greeting(int hour) => hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";

    private void OnPluginsChanged()
    {
        Build();
        if (_shown) foreach (var card in _cards) card.Icon.IsActive = !_info;
        if (_info) SetInfo(true);
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("info", () => { SetInfo(true); return Task.CompletedTask; });
        yield return ("info-off", () => { SetInfo(false); return Task.CompletedTask; });
        yield return ("two-off", () =>
        {
            foreach (var id in new[] { PluginIds.HardwareMonitor, PluginIds.FanControl })
                if (_app.Plugins.Find(id) is { } p) _app.Plugins.SetEnabled(p, false);
            return Task.CompletedTask;
        });
        yield return ("all-on", () =>
        {
            _app.Plugins.SetEnabled(_ => true);
            return Task.CompletedTask;
        });
        yield return ("narrow", () => { _body.MaxWidth = 700; _body.HorizontalAlignment = HorizontalAlignment.Left; return Task.CompletedTask; });
        yield return ("narrower", () => { _body.MaxWidth = 600; return Task.CompletedTask; });
        yield return ("wide-again", () => { _body.ClearValue(MaxWidthProperty); _body.ClearValue(HorizontalAlignmentProperty); return Task.CompletedTask; });
    }
}

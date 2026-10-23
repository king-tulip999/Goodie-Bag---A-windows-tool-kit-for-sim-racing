using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal interface IKeepsWheel { }

internal static class ProfileLayout
{
    public static List<LayoutTile> Tiles(AppServices app, DisplaySnapshot snapshot)
    {
        Dictionary<string, int> numbers;
        try { numbers = app.Displays.GetMonitors().Where(m => m.Number > 0).ToDictionary(m => m.Key, m => m.Number); }
        catch { numbers = new Dictionary<string, int>(); }
        int next = numbers.Count == 0 ? 1 : numbers.Values.Max() + 1;
        return app.Displays.Describe(snapshot).OrderBy(m => m.X).Select(m =>
        {
            int n = numbers.TryGetValue(m.Key, out int known) ? known : next++;
            return new LayoutTile(m.X, m.Y, m.Width, m.Height, n.ToString(), $"{m.Width}×{m.Height} · {m.Hz} Hz", m.Primary);
        }).ToList();
    }
}

internal static class IdentifyDisplays
{
    public static void Show(AppServices app)
    {
        if (app.Demo)
        {
            app.Notify("Identify", "Shows each display's number on that display (not available with demo data).");
            return;
        }

        foreach (var monitor in app.Displays.GetMonitors().Where(m => m.Active))
        {
            var card = new Tile
            {
                Width = 300,
                Height = 210,
                CornerRadius = ThemeManager.Round(26),
                Background = (Brush)Application.Current.FindResource("Surface"),
                BorderBrush = (Brush)Application.Current.FindResource(monitor.Primary ? "Accent" : "Line"),
                BorderThickness = new Thickness(monitor.Primary ? 3 : 1),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = monitor.Label, FontSize = 96, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("AccentText"), FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont") },
                        new TextBlock { Text = monitor.Name, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("Text") },
                        new TextBlock { Text = $"{monitor.Width}×{monitor.Height} · {monitor.Hz} Hz" + (monitor.Primary ? " · main" : ""), FontSize = 12, Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("TextMuted") },
                    },
                },
            };
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = card,
                Opacity = 0,
                FontFamily = (FontFamily)Application.Current.FindResource("UiFont"),
            };
            var m = monitor;
            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                double dpi = 1;
                try { dpi = VisualTreeHelper.GetDpi(window).DpiScaleX; } catch { }
                int w = (int)(300 * dpi), h = (int)(210 * dpi);
                SetWindowPos(hwnd, IntPtr.Zero, m.X + (m.Width - w) / 2, m.Y + (m.Height - h) / 2, 0, 0, 0x0001 | 0x0004 | 0x0010);
            };
            window.Show();
            Motion.FadeTo(window, 1, 180);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Motion.FadeTo(window, 0, 300, window.Close);
            };
            timer.Start();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}

internal static class BitmapConvert
{
    public static BitmapSource ToSource(System.Drawing.Bitmap bitmap)
    {
        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null,
                data.Scan0, data.Stride * bitmap.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}

internal sealed record Choice(string? Id, string Text, string? Extra = null)
{
    public override string ToString() => Text;
}

internal sealed class Collapsible : StackPanel
{
    private readonly Func<UIElement> _build;
    private readonly Action<bool> _toggled;
    private readonly Border _content = new() { Margin = new Thickness(0, 14, 0, 0) };
    private readonly Icon _chevron = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly Icon? _chevronEnd;
    private readonly FrameworkElement? _tools;
    private readonly Border? _frame, _bar, _accent;
    private readonly TextBlock _subtitle;
    private bool _hover, _deferred;

    public Collapsible(string title, string subtitle, string icon, (string Soft, string Deep) colors, bool expanded, Func<UIElement> build, Action<bool> toggled, FrameworkElement? tools = null, bool eager = false, bool framed = false, bool deferred = false)
    {
        _build = build;
        _deferred = deferred && !eager;
        _toggled = toggled;
        _tools = tools;
        Margin = new Thickness(0, 0, 0, Ui.PanelSpacing);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _chevron.Foreground = Ui.Res<Brush>("TextMuted");
        header.Children.Add(_chevron);
        var badge = _badge = framed ? new Tile { Width = 44, Height = 44 } : Ui.Badge(icon, colors.Soft, colors.Deep, 34, 10, 17);
        if (framed)
        {
            var glyph = new NavIcon { Glyph = icon, Width = 44, Height = 44, IsActive = true };
            glyph.SetResourceReference(NavIcon.ForegroundProperty, colors.Deep);
            badge.Child = glyph;
        }
        badge.Margin = framed ? new Thickness(0, 0, 20, 0) : new Thickness(10, 0, 12, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        _subtitle = new TextBlock { Text = framed ? "" : subtitle, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        if (framed)
        {
            var name = TextFit.TipWhenTrimmed(new TextBlock { Text = Ui.Spaced(title), Style = Ui.Res<Style>("H3"), FontSize = Ui.CardTitleSize, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            var face = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
            face.Children.Add(badge);
            face.Children.Add(name);
            if (subtitle.Length > 0) face.ToolTip = subtitle;
            Grid.SetColumn(face, 1);
            Grid.SetColumnSpan(face, 2);
            header.Children.Add(face);
        }
        else
        {
            Grid.SetColumn(badge, 1);
            header.Children.Add(badge);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Ui.Text(title, "H2"));
            titles.Children.Add(_subtitle);
            if (subtitle.Length > 0) titles.ToolTip = subtitle;
            Grid.SetColumn(titles, 2);
            header.Children.Add(titles);
        }
        if (framed)
        {
            _chevronEnd = new Icon { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(-1, 1) };
            _chevronEnd.Foreground = _chevron.Foreground;
            Grid.SetColumn(_chevronEnd, 3);
            header.Children.Add(_chevronEnd);
        }
        if (tools is not null && !framed)
        {
            tools.VerticalAlignment = VerticalAlignment.Center;
            tools.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(tools, 3);
            header.Children.Add(tools);
        }

        Border bar = framed
            ? new Border { MinHeight = Ui.CardRowHeight - 2, Padding = new Thickness(16, 0, 16, 0), Cursor = System.Windows.Input.Cursors.Hand, Child = header, CornerRadius = ThemeManager.Round(10), Background = Brushes.Transparent }
            : new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(16, 12, 16, 12), Cursor = System.Windows.Input.Cursors.Hand, Child = header };
        bar.MouseLeftButtonUp += (_, e) =>
        {
            if (tools is not null && !framed && e.OriginalSource is DependencyObject d && IsInside(d, tools)) return;
            Toggle();
        };
        Motion.Pressable(bar);
        AutomationProperties.SetName(bar, title);
        bar.Focusable = true;
        bar.KeyDown += (_, e) =>
        {
            if (e.Key is not (System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) || e.OriginalSource != bar) return;
            e.Handled = true;
            Toggle();
        };
        if (framed)
        {
            _bar = bar;
            _content.Margin = new Thickness(8);
            _content.Padding = new Thickness(14);
            _content.CornerRadius = ThemeManager.Round(10);
            _content.SetResourceReference(Border.BackgroundProperty, "Panel");
            var inside = new StackPanel();
            inside.Children.Add(bar);
            _accent = new Border { Height = 3 };
            _accent.SetResourceReference(Border.BackgroundProperty, "AccentText");
            inside.Children.Add(_accent);
            if (tools is not null)
            {
                tools.HorizontalAlignment = HorizontalAlignment.Right;
                tools.VerticalAlignment = VerticalAlignment.Center;
                _tools = new Border { Margin = new Thickness(8, 8, 8, 0), Child = tools };
                inside.Children.Add(_tools);
            }
            inside.Children.Add(_content);
            _frame = new Tile { CornerRadius = ThemeManager.Round(14), Child = inside };
            _frame.SetResourceReference(Border.BackgroundProperty, "Surface");
            bar.MouseEnter += (_, _) => { _hover = true; Paint(); };
            bar.MouseLeave += (_, _) => { _hover = false; Paint(); };
            Children.Add(_frame);
        }
        else
        {
            Children.Add(bar);
            Children.Add(_content);
        }
        Expanded = expanded;
        if (eager) _content.Child = _build();
        Apply();
    }

    public bool Expanded { get; private set; }

    private readonly Tile _badge;

    public void SetBadgeImage(ImageSource image)
    {
        double size = _badge.Width * (_frame is null ? 0.7 : 0.85);
        var picture = new Image { Source = image, Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        _badge.Child = picture;
    }

    public void Toggle()
    {
        _deferred = false;
        Expanded = !Expanded;
        Apply();
        _toggled(Expanded);
    }

    public bool Deferred => _deferred;

    public bool BuildDeferred()
    {
        if (!_deferred) return false;
        _deferred = false;
        Apply();
        return true;
    }

    private void Apply()
    {
        _chevron.Data = Ui.Res<Geometry>(Expanded ? "I.ChevronDown" : "I.ChevronRight");
        if (_chevronEnd is not null) _chevronEnd.Data = _chevron.Data;
        if (Expanded && _content.Child is null && !_deferred)
            _content.Child = _build();
        _content.Visibility = Expanded ? Visibility.Visible : Visibility.Collapsed;
        _subtitle.Visibility = Expanded || _subtitle.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_tools is not null) _tools.Visibility = Expanded ? Visibility.Visible : Visibility.Collapsed;
        Paint();
    }

    private void Paint()
    {
        if (_frame is null || _bar is null || _accent is null) return;
        _frame.BorderThickness = new Thickness(1);
        _frame.Padding = new Thickness(0.5);
        _frame.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        _accent.Visibility = Expanded ? Visibility.Visible : Visibility.Collapsed;
        _bar.CornerRadius = ThemeManager.Round(Expanded ? 10 : 13.5);
        if (_hover) _bar.SetResourceReference(Border.BackgroundProperty, "Hover");
        else if (Expanded) _bar.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        else _bar.Background = Brushes.Transparent;
    }

    private static bool IsInside(DependencyObject d, DependencyObject ancestor)
    {
        for (var cur = d; cur is not null; cur = cur is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur == ancestor) return true;
        return false;
    }
}

internal static class Ui
{
    public const double HeaderGap = 20;

    public const double PanelSpacing = 24;

    public const double CardRowHeight = 84, CardTitleSize = 40;

    public static string Spaced(string name) => ThemeManager.Current.Caps ? string.Join((char)0x200A, name.ToUpperInvariant().ToCharArray()) : name;

    public const double PageWidth = 1720;

    public static ContentFrame PageFrame(UIElement body, double width = PageWidth) => new() { Child = body, DesignWidth = width, Centered = true };

    public static void ChainWheel(ScrollViewer page)
    {
        page.PreviewMouseWheel += (_, e) =>
        {
            bool down = e.Delta < 0;
            DependencyObject? d = e.OriginalSource as DependencyObject;
            for (; d is not null && d != page; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (d is NumberBox { IsKeyboardFocusWithin: true } or IKeepsWheel) return;
                if (d is ScrollViewer sv && (down ? sv.VerticalOffset < sv.ScrollableHeight - 0.5 : sv.VerticalOffset > 0.5)) return;
            }
            if (d is null) return;
            e.Handled = true;
            double step = SystemParameters.WheelScrollLines < 0 ? page.ViewportHeight : SystemParameters.WheelScrollLines * 16;
            page.ScrollToVerticalOffset(page.VerticalOffset - e.Delta / 120.0 * step);
        };
    }

    public static void InstallDropDownWheel() =>
        EventManager.RegisterClassHandler(typeof(ComboBox), UIElement.MouseWheelEvent, new MouseWheelEventHandler((sender, e) =>
        {
            if (((ComboBox)sender).IsDropDownOpen) e.Handled = true;
        }));

    public static void FollowTarget(System.Windows.Controls.Primitives.Popup popup)
    {
        var viewers = new List<ScrollViewer>();
        Window? window = null;
        void Reposition()
        {
            if (!popup.IsOpen || popup.PlacementTarget is not FrameworkElement target) return;
            foreach (var sv in viewers)
            {
                if (!target.IsDescendantOf(sv)) continue;
                var box = target.TransformToAncestor(sv).TransformBounds(new Rect(target.RenderSize));
                if (box.Bottom < 0 || box.Top > sv.ViewportHeight) { popup.IsOpen = false; return; }
            }
            double x = popup.HorizontalOffset;
            popup.HorizontalOffset = x + 0.01;
            popup.HorizontalOffset = x;
        }
        void OnScroll(object? _, ScrollChangedEventArgs e) { if (e.VerticalChange != 0 || e.HorizontalChange != 0) Reposition(); }
        void OnMoved(object? _, EventArgs e) => Reposition();
        void OnHidden(object? _, DependencyPropertyChangedEventArgs e) { if (e.NewValue is false) popup.IsOpen = false; }
        void OnUnloaded(object? _, RoutedEventArgs e) => popup.IsOpen = false;
        UIElement? watched = null;
        popup.Opened += (_, _) =>
        {
            watched = popup.PlacementTarget;
            if (watched is not null)
            {
                watched.IsVisibleChanged += OnHidden;
                if (watched is FrameworkElement fe) fe.Unloaded += OnUnloaded;
                popup.Dispatcher.BeginInvoke(() => { if (popup.IsOpen && watched is { IsVisible: false }) popup.IsOpen = false; }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            viewers.Clear();
            for (DependencyObject? d = popup.PlacementTarget; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is ScrollViewer sv) { viewers.Add(sv); sv.ScrollChanged += OnScroll; }
            window = popup.PlacementTarget is null ? null : Window.GetWindow(popup.PlacementTarget);
            if (window is not null) { window.LocationChanged += OnMoved; window.SizeChanged += OnMoved; }
        };
        popup.Closed += (_, _) =>
        {
            if (watched is not null) watched.IsVisibleChanged -= OnHidden;
            if (watched is FrameworkElement fe) fe.Unloaded -= OnUnloaded;
            watched = null;
            foreach (var sv in viewers) sv.ScrollChanged -= OnScroll;
            viewers.Clear();
            if (window is not null) { window.LocationChanged -= OnMoved; window.SizeChanged -= OnMoved; }
            window = null;
        };
    }

    public static ComboBox Combo(IEnumerable<Choice> choices, string? selectedId, Action<Choice> changed, double minWidth = 220)
    {
        var box = new ComboBox { MinWidth = minWidth };
        foreach (var c in choices)
            box.Items.Add(c);
        box.SelectedItem = box.Items.Cast<Choice>().FirstOrDefault(c => string.Equals(c.Id, selectedId, StringComparison.OrdinalIgnoreCase)) ?? (box.Items.Count > 0 ? box.Items[0] : null);
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is Choice c) changed(c); };
        return box;
    }

    public static TextBlock EditorSection(string title)
    {
        var block = new TextBlock { Text = title, Style = Res<Style>("H3"), FontSize = 16, Margin = new Thickness(0, 24, 0, 8) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "EyebrowText");
        return block;
    }

    public const double FieldWidth = 440;
    private const double FieldLine = 34;

    public static Grid FieldRow(string label, UIElement control, double labelWidth = 150, string? hint = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = new TextBlock { Text = label, Style = Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Border { MinHeight = FieldLine, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(0, 0, 12, 0), Child = name });
        var slot = new Grid { MinHeight = FieldLine, VerticalAlignment = VerticalAlignment.Top };
        if (control is FrameworkElement fe)
        {
            if (fe is HotkeyBox) fe.Width = double.NaN;
            bool fill = double.IsNaN(fe.Width) && fe is TextBox or ComboBox or HotkeyBox or DockPanel;
            var first = fe is StackPanel { Orientation: Orientation.Vertical } column ? column.Children.OfType<UIElement>().FirstOrDefault() : fe;
            bool stacked = fe is StackPanel { Orientation: Orientation.Vertical };
            fe.VerticalAlignment = stacked ? VerticalAlignment.Top : VerticalAlignment.Center;
            if (stacked && first is CheckBox) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 7, fe.Margin.Right, fe.Margin.Bottom);
            if (fill)
            {
                fe.HorizontalAlignment = HorizontalAlignment.Stretch;
                slot.Children.Add(new FieldBox { Child = fe, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                fe.HorizontalAlignment = HorizontalAlignment.Left;
                slot.Children.Add(fe);
            }
        }
        else slot.Children.Add(control);
        Grid.SetColumn(slot, 1);
        grid.Children.Add(slot);
        if (hint is not null)
        {
            var note = new TextBlock { Text = hint, Style = Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, MaxWidth = FieldWidth + 160, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetColumn(note, 1);
            Grid.SetRow(note, 1);
            grid.Children.Add(note);
        }
        return grid;
    }

    public static TextBlock FieldHint(string text, double labelWidth) => new()
    {
        Text = text, Style = Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, MaxWidth = labelWidth + FieldWidth + 160,
        HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(labelWidth, 0, 0, 6),
    };

    public static Border RemovableChip(string text, Action remove)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var x = new Button { Style = Res<Style>("QuietButton"), Padding = new Thickness(2), Margin = new Thickness(6, -2, -4, -2), ToolTip = "Remove",
            Content = new Icon { Data = Res<Geometry>("I.X"), Width = 11, Height = 11 } };
        x.Click += (_, _) => remove();
        panel.Children.Add(x);
        return new Tile { Style = Res<Style>("Chip"), Child = panel };
    }

    public static Border Dialog(MainWindow window, string title, string subtitle, UIElement content, StackPanel buttons, TextBlock? error = null, double width = 640)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(title, "H2"));
        stack.Children.Add(new TextBlock { Text = subtitle, Style = Res<Style>("Muted"), Margin = new Thickness(0, 4, 0, 6) });
        stack.Children.Add(content);
        buttons.Orientation = Orientation.Horizontal;
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(0, 18, 0, 0);
        var outer = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        outer.Children.Add(buttons);
        if (error is not null)
        {
            error.Foreground = Res<Brush>("Danger");
            error.TextWrapping = TextWrapping.Wrap;
            error.Margin = new Thickness(0, 10, 0, 0);
            DockPanel.SetDock(error, Dock.Bottom);
            outer.Children.Add(error);
        }
        outer.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 10, 0) });
        return new Tile
        {
            Style = Res<Style>("LiftedCard"),
            Width = width,
            MaxHeight = Math.Max(480, window.ModalRoom),
            Padding = new Thickness(26, 22, 16, 20),
            Child = outer,
        };
    }

    public static StackPanel DialogButtons(MainWindow window, string saveText, Func<bool> save)
    {
        var buttons = new StackPanel();
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => window.CloseModal(false);
        var ok = new Button { Content = saveText, Style = Res<Style>("PrimaryButton"), IsDefault = true };
        ok.Click += (_, _) => { if (save()) window.CloseModal(true); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        return buttons;
    }

    public static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    public const int NameLength = 48;

    public const string Wordmark = "GOODIE BAG";

    public static void FitLockup(Border tile, FrameworkElement glyph, TextBlock text, double maxHeight, double width, double gap)
    {
        static FormattedText Probe(ThemeSpec theme) => new(Wordmark, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(ThemeManager.Family(theme.DisplayFont), theme.HeadingStyle, FontWeights.ExtraBold, FontStretches.Normal), 100, Brushes.Black, 1.0);
        double h = maxHeight;
        foreach (var theme in Themes.All)
        {
            var p = Probe(theme);
            h = Math.Min(h, (width - gap) / (1 + p.Width / p.Extent));
        }
        h = Math.Max(16, Math.Floor(h));
        tile.Width = tile.Height = h;
        tile.VerticalAlignment = VerticalAlignment.Top;
        tile.CornerRadius = ThemeManager.Round(Math.Round(h * 0.3));
        glyph.Width = glyph.Height = Math.Round(h * 0.58);

        var probe = Probe(ThemeManager.Current);
        double size = 100 * h / probe.Extent, k = size / 100;
        double inkTop = (probe.Height + probe.OverhangAfter - probe.Extent) * k;
        text.Text = Wordmark;
        text.FontFamily = ThemeManager.Family(ThemeManager.Current.DisplayFont);
        text.FontStyle = ThemeManager.Current.HeadingStyle;
        text.FontWeight = FontWeights.ExtraBold;
        text.FontSize = size;
        text.VerticalAlignment = VerticalAlignment.Top;
        text.Margin = new Thickness(gap, -inkTop, 0, 0);
    }

    public static readonly DependencyProperty AnchorProperty = DependencyProperty.RegisterAttached("Anchor", typeof(string), typeof(Ui));

    public static void Anchor(FrameworkElement element, string id) => element.SetValue(AnchorProperty, id);

    public static FrameworkElement? FindAnchor(DependencyObject root, string id)
    {
        if (root is FrameworkElement fe && (string?)fe.GetValue(AnchorProperty) == id)
            return fe;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d && FindAnchor(d, id) is { } found)
                return found;
        return null;
    }

    public static StackPanel PluginTitle(string pluginId, string text)
    {
        var plugin = AppServices.Current?.Plugins.Find(pluginId);
        var colors = Hues.Tool(pluginId, plugin?.Colors ?? Hues.Accent);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = new NavIcon
        {
            Glyph = plugin is { Pages.Count: > 0 } ? plugin.Pages[0].Icon : plugin?.Icon, IsActive = true,
            Width = 34, Height = 34, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(NavIcon.ForegroundProperty, colors.Deep);
        row.Children.Add(icon);
        var title = Text(text, "H1");
        title.VerticalAlignment = VerticalAlignment.Center;
        title.SetResourceReference(TextBlock.ForegroundProperty, colors.Deep);
        row.Children.Add(title);
        return row;
    }

    public static TextBlock Text(string text, string style = "Body", double? size = null)
    {
        var t = new TextBlock { Text = text, Style = Res<Style>(style) };
        if (size is { } s) t.FontSize = s;
        return t;
    }

    public static Button IconButton(string icon, string tooltip, Action click, string style = "IconButton")
    {
        var b = new Button { Style = Res<Style>(style), ToolTip = tooltip, Content = new Icon { Data = Res<Geometry>(icon), Width = 16, Height = 16 } };
        b.Click += (_, _) => click();
        return b;
    }

    public static Button LabeledButton(string icon, string label, Action click, string style = "GhostButton")
    {
        var b = new Button { Style = Res<Style>(style), Content = IconLabel(icon, label) };
        b.Click += (_, _) => click();
        return b;
    }

    public static void SetToggled(Button button, bool on) => button.Style = Res<Style>(on ? "SelectedButton" : "GhostButton");

    public static StackPanel IconLabel(string icon, string label, double iconSize = 15, double? fontSize = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Icon { Data = Res<Geometry>(icon), Width = iconSize, Height = iconSize, VerticalAlignment = VerticalAlignment.Center });
        var text = new TextBlock { Text = ThemeManager.Case(label), Margin = new Thickness(Math.Round(iconSize * 0.47), 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (fontSize is { } size) text.FontSize = size;
        panel.Children.Add(text);
        return panel;
    }

    public static Grid SplitRow(FrameworkElement left, FrameworkElement right, double gap = 16, bool stackLeft = false)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.HorizontalAlignment = HorizontalAlignment.Right;
        row.Children.Add(left);
        row.Children.Add(right);
        var margin = right.Margin;
        var beside = new Thickness(margin.Left + gap, margin.Top, margin.Right, margin.Bottom);
        bool stacked = false;
        void Fit()
        {
            if (row.ActualWidth <= 0) return;
            var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
            left.Measure(unbounded);
            right.Measure(unbounded);
            double rightWidth = right.DesiredSize.Width - right.Margin.Left + margin.Left;
            bool stack = left.DesiredSize.Width + rightWidth + gap > row.ActualWidth;
            if (stack == stacked) return;
            stacked = stack;
            Grid.SetRow(right, stack ? 1 : 0);
            Grid.SetColumn(right, stack ? 0 : 1);
            Grid.SetColumnSpan(right, stack ? 2 : 1);
            if (stackLeft) right.HorizontalAlignment = stack ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            right.Margin = stack ? new Thickness(margin.Left, margin.Top + 8, margin.Right, margin.Bottom) : beside;
        }
        right.Margin = beside;
        Grid.SetColumn(right, 1);
        row.SizeChanged += (_, _) => Fit();
        right.SizeChanged += (_, _) => Fit();
        row.Loaded += (_, _) => row.Dispatcher.BeginInvoke(Fit, System.Windows.Threading.DispatcherPriority.Loaded);
        return row;
    }

    public static Tile PanelTools(string tooltip, string anchor, Action<Button> openMenu, CollapseAllButton? collapseAll, out Button panels)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Icon { Data = Res<Geometry>("I.Tune"), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = ThemeManager.Case("Panels"), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new Icon { Data = Res<Geometry>("I.ChevronDown"), Width = 10, Height = 10, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button
        {
            Style = Res<Style>("QuietButton"), Height = 28, Padding = new Thickness(10, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center, Content = content, ToolTip = tooltip,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, "Panels");
        Anchor(button, anchor);
        button.Click += (_, _) => openMenu(button);
        panels = button;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(button);
        if (collapseAll is not null)
        {
            var divider = new Border { Width = 1, Margin = new Thickness(3, 4, 3, 4) };
            divider.SetResourceReference(Border.BackgroundProperty, "Line");
            buttons.Children.Add(divider);
            collapseAll.Style = Res<Style>("QuietButton");
            collapseAll.Height = 28;
            collapseAll.Padding = new Thickness(10, 0, 10, 0);
            collapseAll.VerticalAlignment = VerticalAlignment.Center;
            buttons.Children.Add(collapseAll);
        }
        var tools = new Tile { Padding = new Thickness(2), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), CornerRadius = ThemeManager.Round(8), Child = buttons };
        tools.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        tools.SetResourceReference(Border.BorderBrushProperty, "BtnOutline");
        return tools;
    }

    public static Tile Badge(string icon, string soft, string deep, double size, double radius, double iconSize)
    {
        var glyph = Glyph(icon);
        glyph.Width = glyph.Height = iconSize;
        glyph.Foreground = Res<Brush>(deep);
        return new Tile { Width = size, Height = size, CornerRadius = ThemeManager.Round(radius), Background = Res<Brush>(soft), Child = glyph };
    }

    public static Icon Glyph(string key)
    {
        var icon = new Icon { Data = Res<Geometry>(key) };
        if (key.StartsWith("F.", StringComparison.Ordinal))
        {
            icon.StrokeThickness = 0;
            icon.SetBinding(Icon.FillProperty, new System.Windows.Data.Binding(nameof(Icon.Foreground)) { RelativeSource = System.Windows.Data.RelativeSource.Self });
        }
        return icon;
    }

    public static Border Chip(string text, string? soft = null, string? deep = null)
    {
        var chip = new Tile { Style = Res<Style>("Chip"), Child = TextFit.TipWhenTrimmed(new TextBlock { Text = ThemeManager.Case(text), TextTrimming = TextTrimming.CharacterEllipsis }) };
        if (soft is not null) { chip.Background = Res<Brush>(soft); chip.BorderBrush = Res<Brush>(ThemeManager.Current.Glow && deep is not null ? deep : soft); }
        if (deep is not null) ((TextBlock)chip.Child).Foreground = Res<Brush>(deep);
        if (soft == "DangerSoft") Danger(chip, (TextBlock)chip.Child, 11);
        return chip;
    }

    public static void Danger(Border tile, TextBlock text, double iconSize = 14)
    {
        tile.Background = Res<Brush>("DangerSoft");
        tile.BorderBrush = Res<Brush>("Danger");
        tile.BorderThickness = new Thickness(1.5);
        Tile.SetChamfer(tile, true);
        text.Foreground = Res<Brush>("DangerDeep");
        text.FontWeight = FontWeights.Bold;
        var icon = new Icon { Data = Res<Geometry>("I.Alert"), Width = iconSize, Height = iconSize, Foreground = Res<Brush>("Danger"), Margin = new Thickness(0, 0, 5, 0) };
        text.Inlines.InsertBefore(text.Inlines.FirstInline, new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center });
    }

    public static Tile DangerBanner(string title, string? detail = null) => Notice("Danger", "I.Alert", title, detail);

    public static Tile Notice(string tone, string icon, string? title, string? detail = null, (string Label, Action Run)? action = null)
    {
        string deep = tone == "Accent" ? "AccentText" : tone + "Deep";
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new Icon { Data = Res<Geometry>(icon), Width = 18, Height = 18, Foreground = Res<Brush>(tone), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) });
        var stack = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrWhiteSpace(title))
            stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = Res<Brush>(deep), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(detail))
            stack.Children.Add(new TextBlock { Text = detail, FontSize = 12.5, Foreground = Res<Brush>(title is null ? deep : "Text"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, title is null ? 0 : 2, 0, 0) });
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        if (action is { } a)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var button = new Button { Content = a.Label, Style = Res<Style>("SoftButton"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            button.Click += (_, _) => a.Run();
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }
        var tile = new Tile { Style = Res<Style>("DangerBanner"), Child = grid };
        if (tone != "Danger")
        {
            tile.Background = Res<Brush>(tone + "Soft");
            tile.BorderBrush = Res<Brush>(tone);
            TextElement.SetForeground(tile, Res<Brush>(deep));
        }
        return tile;
    }

    public static Border Segmented(IReadOnlyList<string> options, int selected, Action<int> onChange, string group, IReadOnlyList<string?>? tips = null)
    {
        string name = group + "-" + Guid.NewGuid().ToString("N");
        var panel = new WrapPanel();
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var rb = new RadioButton { Content = options[i], Style = Res<Style>("Segment"), GroupName = name, IsChecked = i == selected };
            if (tips is not null && i < tips.Count && tips[i] is { } tip) rb.ToolTip = tip;
            rb.Checked += (_, _) => onChange(index);
            panel.Children.Add(rb);
        }
        return new Tile { Style = Res<Style>("Segmented"), Child = panel };
    }

    public static void SetSegment(FrameworkElement segmented, int index)
    {
        if (segmented is Border { Child: Panel panel } && index >= 0 && index < panel.Children.Count && panel.Children[index] is RadioButton rb && rb.IsChecked != true)
            rb.IsChecked = true;
    }

    public static Grid SectionHeader(string title, string? caption, params FrameworkElement[] actions)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        titles.Children.Add(Text(title, "H2"));
        if (caption is not null)
            titles.Children.Add(new TextBlock { Text = caption, Style = Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        grid.Children.Add(titles);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        for (int i = 0; i < actions.Length; i++)
        {
            if (i > 0) actions[i].Margin = new Thickness(8, 0, 0, 0);
            tools.Children.Add(actions[i]);
        }
        Grid.SetColumn(tools, 1);
        grid.Children.Add(tools);
        return grid;
    }

    private const double SettingTextMin = 180;

    public sealed record SettingDefault(Func<bool> IsDefault, Action Reset, string Default);

    public static Grid SettingRow(string title, string? description, UIElement control, SettingDefault? fallback = null, string? more = null)
    {
        var grid = new SettingRowGrid { Margin = new Thickness(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        var heading = new TextBlock { Text = title, Style = Res<Style>("Body"), FontWeight = FontWeights.SemiBold };
        if (more is not null) AddInfo(heading, more);
        UIElement? dot = null;
        if (fallback is null) text.Children.Add(heading);
        else
        {
            text.Children.Add(Marked(heading, out var mark));
            dot = mark;
        }
        if (description is not null)
            text.Children.Add(new TextBlock { Text = description, Style = Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0) });
        grid.Children.Add(text);
        if (fallback is not null && dot is not null) control = WithReset(grid, control, dot, fallback);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        if (control is not FrameworkElement fe) return grid;
        fe.VerticalAlignment = VerticalAlignment.Center;
        var align = fe.HorizontalAlignment;
        var margin = fe.Margin;
        bool stacked = false;
        string? measuredFor = null;
        double natural = 0, longest = 0;
        void Fit()
        {
            if (grid.ActualWidth <= 0) return;
            fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            string key = title + "\n" + description;
            if (key != measuredFor)
            {
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                natural = text.DesiredSize.Width - text.Margin.Right;
                longest = LongestWord(heading) + 2;
                measuredFor = key;
            }
            bool titleOnly = description is null;
            double room = grid.ActualWidth - fe.DesiredSize.Width - 18;
            double needed = Math.Max(Math.Min(natural, titleOnly ? 100 : SettingTextMin), longest);
            bool stack = room < needed;
            if (stack == stacked) return;
            stacked = stack;
            Grid.SetRow(fe, stack ? 1 : 0);
            Grid.SetColumn(fe, stack ? 0 : 1);
            fe.HorizontalAlignment = stack ? HorizontalAlignment.Left : align;
            fe.Margin = stack ? new Thickness(margin.Left, margin.Top + 8, margin.Right, margin.Bottom) : margin;
            text.Margin = stack ? new Thickness(0) : new Thickness(0, 0, 18, 0);
        }
        grid.SizeChanged += (_, _) => Fit();
        fe.SizeChanged += (_, _) => Fit();
        text.SizeChanged += (_, _) => Fit();
        return grid;
    }

    public static TextBlock SubHeader(string text, bool first = false)
    {
        var head = new TextBlock { Text = text, Style = Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, first ? 4 : 16, 0, 2) };
        head.SetResourceReference(TextBlock.ForegroundProperty, "EyebrowText");
        return head;
    }

    public static System.Windows.Shapes.Ellipse ChangedDot()
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7 };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Changed");
        return dot;
    }

    public static FrameworkElement Marked(FrameworkElement title, out UIElement dot)
    {
        var mark = ChangedDot();
        mark.Margin = new Thickness(7, 0, 0, 0);
        mark.VerticalAlignment = VerticalAlignment.Center;
        dot = mark;
        if (title is TextBlock block)
        {
            if (block.Inlines.Count == 0 && block.Text.Length > 0)
            {
                string plain = block.Text;
                block.Inlines.Add(new Run(plain));
            }
            block.Inlines.Add(new InlineUIContainer(mark) { BaselineAlignment = BaselineAlignment.Center });
            return block;
        }
        if (title is Panel panel)
        {
            panel.Children.Add(mark);
            return panel;
        }
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(title);
        line.Children.Add(mark);
        return line;
    }

    public static void AddInfo(TextBlock title, string more)
    {
        var icon = new Icon { Data = Res<Geometry>("I.Info"), Width = 13, Height = 13, Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Help, ToolTip = more };
        icon.SetResourceReference(Control.ForegroundProperty, "TextFaint");
        ToolTipService.SetInitialShowDelay(icon, 150);
        ToolTipService.SetShowDuration(icon, 60000);
        string plain = title.Text;
        title.Inlines.Clear();
        title.Inlines.Add(new Run(plain));
        title.Inlines.Add(new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center });
    }

    public static UIElement WithReset(FrameworkElement row, UIElement control, UIElement dot, SettingDefault fallback)
    {
        var reset = IconButton("I.Undo", "Back to the default (" + fallback.Default + ")", () => { });
        reset.Margin = new Thickness(0, 0, 6, 0);
        var box = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (control is FrameworkElement fe) box.HorizontalAlignment = fe.HorizontalAlignment;
        box.Children.Add(reset);
        box.Children.Add(control);
        void Update()
        {
            bool changed = !fallback.IsDefault();
            dot.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
            reset.Visibility = changed ? Visibility.Visible : Visibility.Hidden;
            if (dot is FrameworkElement d) d.ToolTip = changed ? "Changed from the default (" + fallback.Default + ")" : null;
        }
        void Soon() => row.Dispatcher.BeginInvoke(Update, DispatcherPriority.Background);
        reset.Click += (_, _) => { fallback.Reset(); Soon(); };
        row.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => Soon()), true);
        row.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => Soon()), true);
        row.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => Soon()), true);
        row.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => Soon()), true);
        row.AddHandler(System.Windows.Controls.Primitives.RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((_, _) => Soon()), true);
        row.AddHandler(NumberBox.ChangedEvent, new RoutedEventHandler((_, _) => Soon()), true);
        row.AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => Soon()), true);
        Update();
        return box;
    }

    public static void Highlight(TextBlock block, string text, IReadOnlyList<string> words)
    {
        if (words.Count == 0 || text.Length == 0)
        {
            block.Text = text;
            return;
        }
        var hit = new bool[text.Length];
        bool any = false;
        foreach (var word in words)
            for (int i = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); i >= 0 && word.Length > 0; i = text.IndexOf(word, i + 1, StringComparison.OrdinalIgnoreCase))
                for (int k = i; k < i + word.Length; k++) hit[k] = any = true;
        if (!any)
        {
            block.Text = text;
            return;
        }
        block.Inlines.Clear();
        int start = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && hit[i] == hit[start]) continue;
            var run = new Run(text[start..i]);
            if (hit[start])
            {
                run.SetResourceReference(TextElement.BackgroundProperty, "WarnSoft");
                run.SetResourceReference(TextElement.ForegroundProperty, "Warn");
                run.FontWeight = FontWeights.SemiBold;
            }
            block.Inlines.Add(run);
            start = i;
        }
    }

    public static void HoverHighlight(Border row)
    {
        row.Background = Brushes.Transparent;
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Hover");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
    }

    private static double LongestWord(TextBlock tb)
    {
        double longest = 0;
        foreach (var word in tb.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            longest = Math.Max(longest, TextFit.Width(tb, word));
        return longest;
    }

    public static Border Divider() => new SettingDivider { Height = 1, Background = Res<Brush>("Line"), Margin = new Thickness(0, 2, 0, 2) };

    public static Border AccentRule(Thickness margin) => new() { Height = 3, Margin = margin, IsHitTestVisible = false, Background = Res<Brush>("Accent") };

    public static System.Windows.Shapes.Rectangle CheckerStrip(double width, double height, double opacity = 0.5) => new()
    {
        Width = width, Height = height, Opacity = opacity, IsHitTestVisible = false,
        Fill = Res<Brush>("RacingChecker"), OpacityMask = Res<Brush>("RacingFade"),
    };

    public static CheckBox Switch(bool on, Action<bool> changed)
    {
        var cb = new CheckBox { Style = Res<Style>("Switch"), IsChecked = on };
        cb.Click += (_, _) => changed(cb.IsChecked == true);
        return cb;
    }

    public static DockPanel ChangeLine(string text, string area)
    {
        string color = area switch { "Display" => "Slate", "Audio" => "Plum", "Fans" => "Sage", "Apps" => "Coral", "Power" => "Apricot", "Same" => "Sage", _ => "Accent" };
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Res<Brush>(color), Margin = new Thickness(2, 6, 9, 0), VerticalAlignment = VerticalAlignment.Top });
        row.Children.Add(new TextBlock { Text = text, Style = Res<Style>("Body"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        return row;
    }

    public static DockPanel IconText(string icon, TextBlock text, string brush)
    {
        var line = new DockPanel();
        var glyph = new Icon { Data = Res<Geometry>(icon), Width = 13, Height = 13, Margin = new Thickness(0, 2, 6, 0), VerticalAlignment = VerticalAlignment.Top };
        glyph.SetResourceReference(Control.ForegroundProperty, brush);
        text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        DockPanel.SetDock(glyph, Dock.Left);
        line.Children.Add(glyph);
        line.Children.Add(text);
        return line;
    }

    public static StackPanel SliderWithValue(double min, double max, double value, string format, Action<double> changed, double width = 180, double step = 1)
    {
        var label = new TextBlock { Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Res<Brush>("TextMuted"), FontSize = 12.5 };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = width, IsSnapToTickEnabled = true, TickFrequency = step, SmallChange = step, LargeChange = step * 5 };
        label.Text = string.Format(format, value);
        slider.ValueChanged += (_, e) =>
        {
            label.Text = string.Format(format, e.NewValue);
            changed(e.NewValue);
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(slider);
        panel.Children.Add(label);
        return panel;
    }

    public static void OpenUrl(string url)
    {
        if (!GoodieBag.Services.External.Allowed("open " + url)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
}

internal static class ExeIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static bool Cached(string path, out ImageSource? image)
    {
        lock (Cache) return Cache.TryGetValue(path, out image);
    }

    public static ImageSource? For(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("://"))
            return null;
        if (Cached(path, out var cached))
            return cached;
        ImageSource? image = null;
        try
        {
            if (System.IO.File.Exists(path))
            {
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".ico")
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 64;
                    bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image = bitmap;
                }
                else
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon is not null)
                    {
                        var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        image = source;
                    }
                }
            }
        }
        catch
        {
        }
        lock (Cache) Cache[path] = image;
        return image;
    }

    public static Border IconTile(string? path, string fallbackIcon, double size = 40)
    {
        var tile = new Tile { Width = size, Height = size, CornerRadius = ThemeManager.Round(size * 0.28), Background = Ui.Res<Brush>("TileRaised") };
        UIElement Picture(ImageSource image) => new Image { Source = image, Width = size * 0.62, Height = size * 0.62 };
        tile.Child = new Icon { Data = Ui.Res<Geometry>(fallbackIcon), Width = size * 0.5, Height = size * 0.5, Foreground = Ui.Res<Brush>("AccentText") };
        if (string.IsNullOrWhiteSpace(path) || path.Contains("://")) return tile;
        if (Cached(path, out var known))
        {
            if (known is not null) tile.Child = Picture(known);
            return tile;
        }
        Load();
        return tile;

        async void Load()
        {
            if (await Task.Run(() => For(path)) is { } image) tile.Child = Picture(image);
        }
    }
}

internal sealed class FieldBox : Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        double width = Math.Min(constraint.Width, Ui.FieldWidth);
        if (Child is null) return new Size(0, 0);
        Child.Measure(new Size(width, constraint.Height));
        return new Size(double.IsInfinity(width) ? Child.DesiredSize.Width : width, Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = new Size(Math.Min(arrangeSize.Width, Ui.FieldWidth), arrangeSize.Height);
        Child?.Arrange(new Rect(size));
        return size;
    }
}

internal static class Sticky
{
    public static Action Install(FrameworkElement column, FrameworkElement within, Func<bool> on, double gap = 12)
    {
        var shift = new TranslateTransform();
        column.RenderTransform = shift;
        ScrollViewer? page = null;
        void Follow()
        {
            double offset = 0;
            if (on() && page is { } sv && within.IsDescendantOf(sv))
            {
                double top = within.TransformToVisual(sv).Transform(new Point(0, 0)).Y;
                double room = within.ActualHeight - column.ActualHeight;
                if (room > 0 && column.ActualHeight + gap * 2 <= sv.ViewportHeight) offset = Math.Clamp(gap - top, 0, room);
            }
            if (Math.Abs(shift.Y - offset) > 0.5) shift.Y = offset;
        }
        void OnScroll(object? sender, ScrollChangedEventArgs e) => Follow();
        within.Loaded += (_, _) =>
        {
            DependencyObject? d = VisualTreeHelper.GetParent(within);
            while (d is not null and not ScrollViewer) d = VisualTreeHelper.GetParent(d);
            if (page is not null) page.ScrollChanged -= OnScroll;
            page = d as ScrollViewer;
            if (page is not null) page.ScrollChanged += OnScroll;
            Follow();
        };
        within.Unloaded += (_, _) => { if (page is not null) page.ScrollChanged -= OnScroll; page = null; };
        within.SizeChanged += (_, _) => Follow();
        column.SizeChanged += (_, _) => Follow();
        return Follow;
    }
}

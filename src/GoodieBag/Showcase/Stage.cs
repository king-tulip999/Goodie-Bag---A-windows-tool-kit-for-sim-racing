using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using GoodieBag.Controls;
using GoodieBag.Views;

namespace GoodieBag.Showcase;

internal sealed class Stage : IDisposable
{
    public static readonly Duration Fade = TimeSpan.FromMilliseconds(300), LongFade = TimeSpan.FromMilliseconds(500);
    private static readonly IEasingFunction InOut = new CubicEase { EasingMode = EasingMode.EaseInOut };

    private readonly MainWindow _window;
    private readonly Grid _host;
    private readonly Image _still = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Grid _backdrop = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Grid _closing = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly CaptionWindow _caption = new();

    public Stage(MainWindow window, Rect area, ImageSource backdrop)
    {
        _window = window;
        _host = (Grid)window.Content;
        _host.Children.Add(_still);
        _host.Children.Add(_backdrop);
        _host.Children.Add(_closing);
        _backdrop.Children.Add(new Image
        {
            Source = backdrop, Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        _caption.Place(area);
        _caption.Show();
    }

    public void Freeze()
    {
        var dpi = VisualTreeHelper.GetDpi(_window);
        var shot = new RenderTargetBitmap((int)Math.Round(_host.ActualWidth * dpi.DpiScaleX), (int)Math.Round(_host.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        shot.Render(_host);
        shot.Freeze();
        _still.BeginAnimation(UIElement.OpacityProperty, null);
        _still.Opacity = 1;
        _still.Source = shot;
        _still.Visibility = Visibility.Visible;
    }

    public async Task Reveal(Duration? fade = null)
    {
        await Rendered();
        await Animate(_still, 0, fade ?? Fade);
        _still.Visibility = Visibility.Collapsed;
        _still.Source = null;
    }

    public Task ShowBackdrop(bool on, Duration fade) => Show(_backdrop, on, fade);

    public void Caption(string title, string? kicker = null) => _caption.Set(title, kicker);

    public void HideCaption() => _caption.Set(null, null);

    public void CaptionOnTop() => _caption.KeepOnTop();

    public Task ShowClosing(string tagline, Duration fade)
    {
        var tile = new Tile { BorderThickness = new Thickness(0) };
        tile.SetResourceReference(Border.BackgroundProperty, "LogoBg");
        var glyph = new Icon { Data = Ui.Res<Geometry>("I.Bag"), StrokeThickness = 4 };
        glyph.SetResourceReference(Control.ForegroundProperty, "LogoFg");
        tile.Child = glyph;
        var word = new TextBlock();
        word.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        Ui.FitLockup(tile, glyph, word, maxHeight: 120, width: 1100, gap: 34);
        var lockup = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        lockup.Children.Add(tile);
        lockup.Children.Add(word);
        var line = new TextBlock { Text = tagline, FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 34, 0, 0) };
        line.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        var rule = new Border { Height = 3, Width = 120, Margin = new Thickness(0, 30, 0, 0) };
        rule.SetResourceReference(Border.BackgroundProperty, "Accent");
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(lockup);
        stack.Children.Add(rule);
        stack.Children.Add(line);
        _closing.Children.Clear();
        _closing.SetResourceReference(Panel.BackgroundProperty, "Bg");
        _closing.Children.Add(stack);
        return Show(_closing, true, fade);
    }

    private static async Task Show(FrameworkElement layer, bool on, Duration fade)
    {
        if (on)
        {
            layer.Opacity = 0;
            layer.Visibility = Visibility.Visible;
        }
        await Animate(layer, on ? 1 : 0, fade);
        if (!on) layer.Visibility = Visibility.Collapsed;
    }

    public static Task Animate(UIElement element, double to, Duration duration)
    {
        var done = new TaskCompletionSource();
        var animation = new DoubleAnimation(to, duration) { EasingFunction = InOut };
        animation.Completed += (_, _) => done.TrySetResult();
        element.BeginAnimation(UIElement.OpacityProperty, animation);
        return done.Task;
    }

    public static async Task Rendered()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        for (int i = 0; i < 2; i++)
        {
            var frame = new TaskCompletionSource();
            void OnFrame(object? s, EventArgs e) { CompositionTarget.Rendering -= OnFrame; frame.TrySetResult(); }
            CompositionTarget.Rendering += OnFrame;
            await frame.Task;
        }
    }

    public void Dispose()
    {
        _caption.Close();
        _host.Children.Remove(_still);
        _host.Children.Remove(_backdrop);
        _host.Children.Remove(_closing);
    }

    private sealed class CaptionWindow : Window
    {
        private readonly Grid _slot = new();
        private Rect _area;

        public CaptionWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Content = _slot;
            SizeChanged += (_, _) => Position();
            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                long style = GetWindowLongPtr(hwnd, -20).ToInt64() | 0x20 | 0x80 | 0x08000000;
                SetWindowLongPtr(hwnd, -20, new IntPtr(style));
            };
        }

        public void Place(Rect area)
        {
            _area = area;
            Position();
        }

        private void Position()
        {
            if (PresentationSource.FromVisual(this) is not { CompositionTarget: { } target })
            {
                Left = _area.Left + 48;
                Top = _area.Bottom - 200;
                return;
            }
            var toDip = target.TransformFromDevice;
            var corner = toDip.Transform(new Point(_area.Left + 48, _area.Bottom - 48));
            Left = corner.X;
            Top = corner.Y - ActualHeight;
        }

        public void Set(string? title, string? kicker)
        {
            foreach (FrameworkElement old in _slot.Children.OfType<FrameworkElement>().ToList())
            {
                var leaving = old;
                var fade = new DoubleAnimation(0, Fade) { EasingFunction = InOut };
                fade.Completed += (_, _) => _slot.Children.Remove(leaving);
                leaving.BeginAnimation(OpacityProperty, fade);
            }
            if (title is null) return;
            var card = Make(title, kicker);
            card.Opacity = 0;
            var move = new TranslateTransform(-14, 0);
            card.RenderTransform = move;
            _slot.Children.Add(card);
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Fade) { EasingFunction = InOut });
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Fade) { EasingFunction = InOut });
            KeepOnTop();
        }

        private static FrameworkElement Make(string title, string? kicker)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (kicker is not null)
            {
                var k = new TextBlock { Text = kicker, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
                k.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                stack.Children.Add(k);
            }
            var t = new TextBlock { Text = title, FontSize = 30, FontWeight = FontWeights.Bold };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
            t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            stack.Children.Add(t);
            var bar = new Border { Width = 4, Margin = new Thickness(0, 0, 18, 0) };
            bar.SetResourceReference(Border.BackgroundProperty, "Accent");
            var row = new DockPanel();
            DockPanel.SetDock(bar, Dock.Left);
            row.Children.Add(bar);
            row.Children.Add(stack);
            var card = new Border { Padding = new Thickness(0, 14, 26, 14), BorderThickness = new Thickness(1), Child = row, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            card.SetResourceReference(Border.BackgroundProperty, "Surface");
            card.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
            return card;
        }

        public void KeepOnTop()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    }
}

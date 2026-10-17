using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GoodieBag.Views;

namespace GoodieBag.Services;

internal interface IScreenshotStates
{
    IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates();
}

internal static class ScreenshotRunner
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private static readonly List<string> Errors = new();

    public static bool Active { get; private set; }

    public static void RecordError(Exception ex)
    {
        lock (Errors)
            Errors.Add(ex.ToString());
    }

    private static async Task CaptureSetupThemesAsync(MainWindow window, string folder)
    {
        int n = 1;
        foreach (var theme in Theme.Themes.All)
        {
            try
            {
                AppServices.Current?.SetTheme(theme.Id);
                await Task.Delay(1200);
                Capture(window, Path.Combine(folder, $"{n}-{theme.Id}.png"));
                if (window.FindName("SetupContent") is System.Windows.Controls.ContentControl { Content: System.Windows.Controls.UserControl { Content: System.Windows.Controls.ScrollViewer sv } })
                {
                    sv.ScrollToEnd();
                    await Task.Delay(500);
                    Capture(window, Path.Combine(folder, $"{n}z-{theme.Id}-bottom.png"));
                    sv.ScrollToHome();
                }
            }
            catch (Exception ex)
            {
                RecordError(ex);
            }
            n++;
        }
    }

    private static async Task CaptureSetupAsync(MainWindow window, string folder)
    {
        try
        {
            Capture(window, Path.Combine(folder, "00-setup.png"));
            if (window.FindName("SetupContent") is System.Windows.Controls.ContentControl { Content: System.Windows.Controls.UserControl { Content: System.Windows.Controls.ScrollViewer sv } })
            {
                sv.ScrollToEnd();
                await Task.Delay(500);
                Capture(window, Path.Combine(folder, "00z-setup-bottom.png"));
                sv.ScrollToHome();
            }
            window.Width = 1040;
            window.Height = 680;
            await Task.Delay(900);
            Capture(window, Path.Combine(folder, "00a-setup-small.png"));
            (window.Width, window.Height) = BaseSize;
            window.CloseSetup();
            await Task.Delay(800);
        }
        catch (Exception ex)
        {
            RecordError(ex);
        }
    }

    public static bool Quick => Environment.GetCommandLineArgs().Contains("--quick", StringComparer.OrdinalIgnoreCase);

    private static int Wait(int ms) => Quick ? Math.Max(250, ms / 4) : ms;

    public static string? Folder { get; private set; }

    public static async void Run(MainWindow window, string folder)
    {
        Active = true;
        Folder = folder;
        Directory.CreateDirectory(folder);
        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = 0;
            window.Top = 0;
            (window.Width, window.Height) = BaseSize;
            window.Show();
            await Task.Delay(Quick ? 1200 : 2500);
            if (window.SetupOpen && Environment.GetCommandLineArgs().Contains("--setup-themes", StringComparer.OrdinalIgnoreCase))
            {
                await CaptureSetupThemesAsync(window, folder);
                return;
            }
            if (window.SetupOpen)
                await CaptureSetupAsync(window, folder);

            var only = Arg("--pages")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IReadOnlyList<string> pages = only is null ? window.NavKeys : window.NavKeys.Where(k => only.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
            if (only is not null && pages.Count == 0)
                RecordError(new ArgumentException($"--pages: none of '{string.Join(",", only)}' is a page ({string.Join(", ", window.NavKeys)})"));
            int n = 1;
            foreach (var page in pages)
            {
                try
                {
                    KeepOnScreen(window);
                    window.Navigate(page);
                    await Task.Delay(Wait(1400));
                    Capture(window, Path.Combine(folder, $"{n:00}-{page}.png"));
                    CaptureFullPage(window, page, Path.Combine(folder, $"{n:00}-{page}-full.png"));
                    if (window.PageFor(page) is System.Windows.Controls.UserControl { Content: System.Windows.Controls.ScrollViewer sv } && sv.ScrollableHeight > 40)
                    {
                        sv.ScrollToEnd();
                        await Task.Delay(Wait(500));
                        Capture(window, Path.Combine(folder, $"{n:00}z-{page}-bottom.png"));
                        sv.ScrollToHome();
                        await Task.Delay(Wait(300));
                    }
                }
                catch (Exception ex)
                {
                    RecordError(ex);
                }

                if (window.PageFor(page) is IScreenshotStates states)
                {
                    int state = 0;
                    foreach (var (name, enter) in states.ScreenshotStates())
                    {
                        try
                        {
                            await enter();
                            await Task.Delay(Wait(1200));
                            string shot = $"{n:00}{StateTag(state++)}-{page}-{name}";
                            Capture(window, Path.Combine(folder, shot + ".png"));
                            CaptureFullPage(window, page, Path.Combine(folder, shot + "-full.png"));
                        }
                        catch (Exception ex)
                        {
                            RecordError(ex);
                        }
                    }
                }
                n++;
            }

            if (Environment.GetCommandLineArgs().Contains("--sizes", StringComparer.OrdinalIgnoreCase))
                await CaptureSizesAsync(window, folder);
            if (only is null) await CheckSearchAsync(window, folder, n);
            if (only is null) SaveTrayIcons(folder);
            if (!Quick)
            {
                await CaptureUpdateButtonAsync(window, folder);
                if (only is null) await CaptureNavIconsAsync(window, folder);
                if (!Environment.GetCommandLineArgs().Contains("--fresh", StringComparer.OrdinalIgnoreCase))
                    await CaptureWideAsync(window, folder, pages);
            }
        }
        catch (Exception ex)
        {
            RecordError(ex);
        }
        finally
        {
            lock (Errors)
                File.WriteAllText(Path.Combine(folder, "errors.txt"), Errors.Count == 0 ? "none\n" : string.Join("\n\n", Errors));
            App.Quit();
        }
    }

    private static readonly (string Name, string Kind, string Glyph)[] NavIconKinds =
    {
        ("Automations and Modes", "modes", "I.Layers"),
        ("Displays", "displays", "I.Monitor"),
        ("Hardware Monitor", "hardware-monitor", "I.Heartbeat"),
        ("Overlay", "overlay", "I.Overlay"),
        ("Fan Control", "fan-control", "I.Fan"),
        ("Sim Racing", "sim-racing", "I.FormulaWheel"),
        ("Plugins", "plugins", "I.Gears"),
        ("Settings", "settings", "I.Tune"),
    };

    private const int NavIconFrames = 12, NavIconSize = 96;

    private static async Task CaptureNavIconsAsync(MainWindow window, string folder)
    {
        var rows = new List<(string Name, double Seconds, BitmapSource[] Frames)>();
        var loops = new System.Text.StringBuilder();
        string dir = Path.Combine(folder, "nav-icons");
        try
        {
            Directory.CreateDirectory(dir);
            var (fg, rowBg) = NavIconColors();
            foreach (var (name, kind, glyph) in NavIconKinds)
            {
                GoodieBag.Controls.NavIcon? icon = null;
                try
                {
                    icon = new GoodieBag.Controls.NavIcon { Glyph = glyph, Foreground = fg, Width = NavIconSize, Height = NavIconSize, IsActive = true };
                    var host = new System.Windows.Controls.Border { Width = NavIconSize, Height = NavIconSize, Background = rowBg, Child = icon };
                    if (icon.LoopDuration is not { } period || icon.BeginCapture() is not { } controller)
                    {
                        RecordError(new InvalidOperationException($"Nav icon {name} ({glyph}) has no animation to capture"));
                        continue;
                    }
                    await Task.Delay(100);
                    controller.Pause();
                    var frames = new BitmapSource[NavIconFrames];
                    for (int k = 0; k < NavIconFrames; k++)
                    {
                        var at = TimeSpan.FromTicks(period.Ticks * k / NavIconFrames);
                        controller.SeekAlignedToLastTick(at, System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
                        await Task.Delay(20);
                        controller.SeekAlignedToLastTick(at, System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
                        host.Measure(new Size(NavIconSize, NavIconSize));
                        host.Arrange(new Rect(0, 0, NavIconSize, NavIconSize));
                        host.UpdateLayout();
                        var bitmap = new RenderTargetBitmap(NavIconSize, NavIconSize, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(host);
                        bitmap.Freeze();
                        frames[k] = bitmap;
                        SavePng(bitmap, Path.Combine(dir, $"{kind}-{k:00}.png"));
                    }
                    rows.Add((name, period.TotalSeconds, frames));
                    loops.Append(kind).Append(' ').AppendLine(period.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    RecordError(ex);
                }
                finally
                {
                    icon?.EndCapture();
                }
            }
            File.WriteAllText(Path.Combine(dir, "loops.txt"), loops.ToString());
            if (rows.Count > 0)
                SaveNavIconSheet(window, rows, Path.Combine(folder, "nav-icons.png"));
        }
        catch (Exception ex)
        {
            RecordError(ex);
        }
        await CaptureNavIconThemesAsync(window, folder);
    }

    private static (Brush Fg, Brush RowBg) NavIconColors()
    {
        var fg = Theme.ThemeManager.Brush("NavSelText", Brushes.Black);
        var rowBg = new SolidColorBrush(Theme.ThemeManager.Mix(
            Theme.ThemeManager.ColorOf("Panel", Colors.White), Theme.ThemeManager.ColorOf("Accent", Colors.Orange), 0.24));
        rowBg.Freeze();
        return (fg, rowBg);
    }

    private const int NavSpriteFrames = 24;

    private static void SaveTrayIcons(string folder)
    {
        string dir = Path.Combine(folder, "tray-icons");
        Directory.CreateDirectory(dir);
        foreach (var theme in Theme.Themes.All)
            foreach (var (glyph, name) in new[] { ("I.Bag", "bag"), ("I.Fan", "fan") })
            {
                using var icon = TrayService.TileIcon(glyph, theme.Id);
                if (icon is null) { RecordError(new InvalidOperationException($"tray icon {name} not drawn for {theme.Name}")); continue; }
                using var bitmap = icon.ToBitmap();
                bitmap.Save(Path.Combine(dir, $"{theme.Id}-{name}.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
    }

    private static async Task CaptureNavIconThemesAsync(MainWindow window, string folder)
    {
        string dir = Path.Combine(folder, "nav-icons-themes");
        var original = Theme.ThemeManager.Current.Id;
        var seconds = new double?[NavIconKinds.Length];
        var themeLines = new System.Text.StringBuilder();
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            Directory.CreateDirectory(dir);
            foreach (var theme in Theme.Themes.All)
            {
                var icons = new GoodieBag.Controls.NavIcon?[NavIconKinds.Length];
                try
                {
                    Theme.ThemeManager.Apply(theme.Id);
                    await Task.Delay(150);
                    var (fg, rowBg) = NavIconColors();
                    var hosts = new System.Windows.Controls.Border[NavIconKinds.Length];
                    var controllers = new System.Windows.Media.Animation.ClockController?[NavIconKinds.Length];
                    var periods = new TimeSpan[NavIconKinds.Length];
                    for (int i = 0; i < NavIconKinds.Length; i++)
                    {
                        var (name, _, glyph) = NavIconKinds[i];
                        var icon = new GoodieBag.Controls.NavIcon { Glyph = glyph, Foreground = fg, Width = NavIconSize, Height = NavIconSize, IsActive = true };
                        icons[i] = icon;
                        hosts[i] = new System.Windows.Controls.Border { Width = NavIconSize, Height = NavIconSize, Background = rowBg, Child = icon };
                        if (icon.LoopDuration is { } period && icon.BeginCapture() is { } controller)
                        {
                            controllers[i] = controller;
                            periods[i] = period;
                            seconds[i] ??= period.TotalSeconds;
                        }
                        else
                            RecordError(new InvalidOperationException($"Nav icon {name} ({glyph}) has no animation to capture ({theme.Id}); its row shows the still icon"));
                    }
                    await Task.Delay(100);
                    foreach (var controller in controllers)
                        controller?.Pause();

                    int width = NavSpriteFrames * NavIconSize, height = NavIconKinds.Length * NavIconSize;
                    var sheet = new DrawingVisual();
                    using (var dc = sheet.RenderOpen())
                    {
                        for (int k = 0; k < NavSpriteFrames; k++)
                        {
                            for (int pass = 0; pass < 2; pass++)
                            {
                                for (int i = 0; i < controllers.Length; i++)
                                    controllers[i]?.SeekAlignedToLastTick(TimeSpan.FromTicks(periods[i].Ticks * k / NavSpriteFrames),
                                        System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
                                if (pass == 0) await Task.Delay(20);
                            }
                            for (int i = 0; i < hosts.Length; i++)
                            {
                                var host = hosts[i];
                                host.Measure(new Size(NavIconSize, NavIconSize));
                                host.Arrange(new Rect(0, 0, NavIconSize, NavIconSize));
                                host.UpdateLayout();
                                var cell = new RenderTargetBitmap(NavIconSize, NavIconSize, 96, 96, PixelFormats.Pbgra32);
                                cell.Render(host);
                                cell.Freeze();
                                dc.DrawImage(cell, new Rect(k * NavIconSize, i * NavIconSize, NavIconSize, NavIconSize));
                            }
                        }
                    }
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(sheet);
                    SavePng(bitmap, Path.Combine(dir, $"{theme.Id}.png"));
                    themeLines.Append("theme ").Append(theme.Id).Append(' ').AppendLine(theme.Name);
                }
                catch (Exception ex)
                {
                    RecordError(new InvalidOperationException($"Nav icon sprites for theme {theme.Id} failed", ex));
                }
                finally
                {
                    foreach (var icon in icons)
                        icon?.EndCapture();
                }
            }
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < NavIconKinds.Length; i++)
                text.Append(NavIconKinds[i].Kind).Append(' ').AppendLine((seconds[i] ?? 0).ToString("0.###", culture));
            text.Append(themeLines);
            File.WriteAllText(Path.Combine(dir, "sprites.txt"), text.ToString());
        }
        catch (Exception ex)
        {
            RecordError(ex);
        }
        finally
        {
            try
            {
                if (Theme.ThemeManager.Current.Id != original)
                {
                    Theme.ThemeManager.Apply(original);
                    window.Services.Overlay.InvalidateColors();
                }
            }
            catch (Exception ex)
            {
                RecordError(ex);
            }
            await Task.Delay(600);
        }
    }

    private static void SaveNavIconSheet(MainWindow window, List<(string Name, double Seconds, BitmapSource[] Frames)> rows, string path)
    {
        const double Pad = 16, Gap = 8, LabelGap = 20;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var heading = Theme.ThemeManager.Brush("Heading", Brushes.Black);
        var muted = Theme.ThemeManager.Brush("TextMuted", Brushes.Gray);
        var labels = rows.Select(r => (
            Name: new FormattedText(r.Name, culture, FlowDirection.LeftToRight, Theme.ThemeManager.UiFace(bold: true), 16, heading, 1.0),
            Loop: new FormattedText(r.Seconds.ToString("0.###", culture) + " s loop", culture, FlowDirection.LeftToRight, Theme.ThemeManager.UiFace(), 12, muted, 1.0))).ToList();
        double labelWidth = labels.Max(l => Math.Max(l.Name.Width, l.Loop.Width)) + LabelGap;
        int width = (int)Math.Ceiling(Pad * 2 + labelWidth + NavIconFrames * NavIconSize + (NavIconFrames - 1) * Gap);
        int height = (int)Math.Ceiling(Pad * 2 + rows.Count * NavIconSize + (rows.Count - 1) * Gap);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((Brush)window.FindResource("Bg"), null, new Rect(0, 0, width, height));
            for (int i = 0; i < rows.Count; i++)
            {
                double y = Pad + i * (NavIconSize + Gap);
                var (name, loop) = labels[i];
                double textTop = y + (NavIconSize - name.Height - 2 - loop.Height) / 2;
                dc.DrawText(name, new Point(Pad, textTop));
                dc.DrawText(loop, new Point(Pad, textTop + name.Height + 2));
                for (int k = 0; k < rows[i].Frames.Length; k++)
                    dc.DrawImage(rows[i].Frames[k], new Rect(Pad + labelWidth + k * (NavIconSize + Gap), y, NavIconSize, NavIconSize));
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        SavePng(bitmap, path);
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static async Task CaptureUpdateButtonAsync(MainWindow window, string folder)
    {
        try
        {
            var updates = window.Services.Updates;
            var next = UpdateService.Current;
            updates.ShowForScreenshots(new ReleaseInfo(new Version(next.Major, next.Minor, next.Build + 1), "v-next", "", UpdateService.ReleasesPage, null, 0, null));
            await Task.Delay(600);
            Capture(window, Path.Combine(folder, "update-available.png"));
            updates.ShowForScreenshots(null);
            await Task.Delay(600);
            Capture(window, Path.Combine(folder, "update-none.png"));
        }
        catch (Exception ex)
        {
            RecordError(ex);
        }
    }

    private static async Task CheckSearchAsync(MainWindow window, string folder, int n)
    {
        var report = new System.Text.StringBuilder();
        foreach (var query in new[] { "fan", "HDR", "overlay", "ping", "startup" })
            report.AppendLine($"{query}: " + string.Join(" | ", window.Search(query).Select(r => $"{r.Title} [{r.Page}{(r.Anchor is null ? "" : "#" + r.Anchor)}]")));
        File.WriteAllText(Path.Combine(folder, "search.txt"), report.ToString());

        foreach (var entry in SettingsIndex.With(AppServices.Current!.Plugins.Active).Where(e => e.Anchor is not null && window.NavKeys.Contains(e.Page)))
        {
            KeepOnScreen(window);
            window.Navigate(entry.Page);
            await Task.Delay(120);
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (window.PageFor(entry.Page) is IRevealAnchor hidden && Ui.FindAnchor((DependencyObject)hidden, entry.Anchor!) is null && hidden.Reveal(entry.Anchor!))
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (window.PageFor(entry.Page) is not { } page || Ui.FindAnchor(page, entry.Anchor!) is null)
                RecordError(new InvalidOperationException($"Search target missing: {entry.Title} → {entry.Page}#{entry.Anchor}"));
        }

        if (window.Search("fan failsafe").FirstOrDefault() is { } result)
        {
            window.GoTo(result.Page, result.Anchor);
            await Task.Delay(900);
            Capture(window, Path.Combine(folder, $"{n:00}-search-fan-failsafe.png"));
        }
    }

    private static async Task CaptureWideAsync(MainWindow window, string folder, IReadOnlyList<string> pages)
    {
        window.Width = 2560;
        window.Height = 1440;
        await Task.Delay(800);
        foreach (var page in pages)
        {
            try
            {
                window.Navigate(page);
                await Task.Delay(1000);
                Capture(window, Path.Combine(folder, $"wide-{page}.png"));
            }
            catch (Exception ex)
            {
                RecordError(ex);
            }
        }
    }

    private static (double W, double H) BaseSize =>
        Arg("--window")?.Split('x') is [var w, var h] && double.TryParse(w, out double width) && double.TryParse(h, out double height) ? (width, height) : (1280, 820);

    private static string? Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static async Task CaptureSizesAsync(MainWindow window, string folder)
    {
        window.Navigate("plugins");
        var ui = AppServices.Current!.Config.Ui;
        int scale = ui.InterfaceScale;
        foreach (var (w, h, size) in new[] { (1280, 1040, 100), (1280, 820, 100), (1100, 740, 100), (1040, 680, 100), (1040, 680, 90), (960, 600, 110) })
        {
            try
            {
                ui.InterfaceScale = size;
                window.ApplyInterfaceScale();
                window.Width = w;
                window.Height = h;
                await Task.Delay(Wait(1200));
                Capture(window, Path.Combine(folder, $"size-{w}x{h}-{size}.png"));
            }
            catch (Exception ex)
            {
                RecordError(ex);
            }
        }
        ui.InterfaceScale = scale;
        window.ApplyInterfaceScale();
        (window.Width, window.Height) = BaseSize;
    }

    private static void KeepOnScreen(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        if (!window.IsVisible) window.Show();
    }

    private static bool FullPages => Environment.GetCommandLineArgs().Contains("--full-pages", StringComparer.OrdinalIgnoreCase);

    private static void CaptureFullPage(MainWindow window, string page, string path)
    {
        if (!FullPages || window.PageFor(page) is not System.Windows.Controls.UserControl { Content: System.Windows.Controls.ScrollViewer { Content: FrameworkElement content } })
            return;
        int width = (int)Math.Ceiling(content.ActualWidth), height = (int)Math.Ceiling(content.ActualHeight);
        if (width <= 0 || height <= 0)
            return;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((Brush)window.FindResource("Bg"), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        SavePng(bitmap, path);
    }

    private static void Capture(Window window, string path)
    {
        if (window.Content is not FrameworkElement root)
            return;
        int width = (int)Math.Ceiling(root.ActualWidth), height = (int)Math.Ceiling(root.ActualHeight);
        if (width <= 0 || height <= 0)
            return;

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((Brush)window.FindResource("Bg"), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width, height));
            foreach (PresentationSource source in PresentationSource.CurrentSources)
            {
                if (source.RootVisual is not FrameworkElement popup || popup.GetType().Name != "PopupRoot" || !popup.IsVisible || popup.ActualWidth <= 0)
                    continue;
                if (source is System.Windows.Interop.HwndSource hwnd && !IsWindowVisible(hwnd.Handle))
                    continue;
                try
                {
                    var at = root.PointFromScreen(popup.PointToScreen(new Point()));
                    dc.DrawRectangle(new VisualBrush(popup), null, new Rect(at.X, at.Y, popup.ActualWidth, popup.ActualHeight));
                }
                catch (InvalidOperationException) { }
            }
        }
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    internal static string StateTag(int i) => i < 24 ? ((char)('a' + i)).ToString() : "y" + (char)('a' + Math.Min(25, i - 24));
}

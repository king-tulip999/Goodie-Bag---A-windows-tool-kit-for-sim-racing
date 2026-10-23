using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GoodieBag.Controls;
using GoodieBag.SimRacing.Hud;
using GoodieBag.SimRacing.Setups;
using GoodieBag.Theme;
using GoodieBag.Views;
using GoodieBag.Views.Pages;

namespace GoodieBag.Showcase;

internal static class ShowcaseRunner
{
    public static bool Active { get; private set; }

    internal static Overlay.FrameStats SampleFrames()
    {
        double t = Clock.Elapsed.TotalSeconds, fps = 144 + 5 * Math.Sin(t * 1.7) + 2 * Math.Sin(t * 4.3);
        return new Overlay.FrameStats(Math.Round(fps), 141, 118, 1000 / fps);
    }

    private const int Stagger = 120, RequiredDpi = 144;
    private const string Tagline = "A Windows toolkit for sim rigs";

    private static MainWindow W = null!;
    private static Stage S = null!;
    private static Rect _area;
    private static readonly Stopwatch Clock = new();
    private static readonly List<string> Log = new();
    private static double _start;
    private static CursorSprite? _cursor;
    private static Action? _onLeave;

    public static async void Run(MainWindow window, string folder, string? ffmpeg)
    {
        Active = true;
        W = window;
        timeBeginPeriod(1);
        ScreenRecorder? recorder = null;
        HudMontage? hud = null;
        double end = 0;
        string raw = Path.Combine(folder, "raw.mkv");
        try
        {
            Directory.CreateDirectory(folder);
            if (ffmpeg is null || !File.Exists(ffmpeg)) throw new InvalidOperationException("ffmpeg not found (--ffmpeg <path to ffmpeg.exe>)");
            var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            uint dpi = MonitorDpi(screen);
            if (dpi != RequiredDpi)
                throw new InvalidOperationException($"the main monitor's scale is {dpi * 100 / 96} %, the recording needs 150 %: Settings → System → Display → Scale, then run it again");
            Log.Add($"monitor {screen.Width}×{screen.Height} at 150 %");
            double scale = dpi / 96.0;
            Pointer.Scale = scale;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Topmost = true;
            window.Left = screen.X / scale;
            window.Top = screen.Y / scale;
            window.Width = screen.Width / scale;
            window.Height = screen.Height / scale;
            window.Show();
            SquareCorners(window);
            _area = new Rect(screen.X, screen.Y, screen.Width, screen.Height);
            S = new Stage(window, _area, Backdrop(folder));
            await Warm();
            hud = new HudMontage();
            hud.Warm();
            hud.Layout(screen, (float)scale);
            _cursor = new CursorSprite((float)(scale * 1.25));
            await Pointer.Glide(new Point(_area.X + _area.Width * 0.62, _area.Y + _area.Height * 0.6), 200);

            Clock.Start();
            recorder = ScreenRecorder.Start(ffmpeg, raw, Clock);
            for (int i = 0; i < 100 && !recorder.Running; i++) await Task.Delay(50);
            if (!recorder.Running) throw new InvalidOperationException("ffmpeg isn't recording: " + string.Join(" | ", recorder.Errors.TakeLast(4)));
            await Task.Delay(600);
            _start = Clock.Elapsed.TotalSeconds;

            await Home();
            await WindowProfiles();
            await Fans();
            await SimRacing();
            await Displays();
            await Stats();
            await PerformanceOverlay();
            await IracingOverlays(hud);
            await Closing();
            end = Clock.Elapsed.TotalSeconds;
            Mark("end");
            await Task.Delay(300);
        }
        catch (Exception ex)
        {
            Log.Add("error " + ex.ToString().Replace(Environment.NewLine, " | "));
        }
        finally
        {
            if (recorder is not null)
            {
                await recorder.StopAsync();
                if (end > _start)
                {
                    const double lead = 0.25, fadeOut = 0.5;
                    double length = end - _start + lead, from = Math.Max(0, recorder.VideoTime(_start) - lead);
                    string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg!)!, "ffprobe.exe");
                    try
                    {
                        var (ok, report) = ScreenRecorder.CheckFrames(ffprobe, raw, from, length);
                        Log.Add(report);
                        if (!ok) Log.Add("warning: the capture didn't hold a steady 60 fps");
                    }
                    catch (Exception ex) { Log.Add("frame check failed: " + ex.Message); }
                    File.WriteAllText(Path.Combine(folder, "cut.txt"), string.Format(CultureInfo.InvariantCulture,
                        "-ss {0:0.000} -t {1:0.000} -filter_complex \"[0:v]fade=t=out:st={2:0.000}:d={3:0.00},split[uhd][q];[q]scale=2560:1440:flags=lanczos[qhd]\"",
                        from, length, length - fadeOut, fadeOut));
                }
            }
            try { File.WriteAllLines(Path.Combine(folder, "shots.txt"), Log); } catch { }
            _cursor?.Dispose();
            hud?.Dispose();
            timeEndPeriod(1);
            Application.Current.Shutdown();
        }
    }

    private static async Task Home()
    {
        Mark("home");
        S.Caption("Every tool in the sidebar", "Goodie Bag");
        await Task.Delay(650);
        var current = ThemeManager.Current;
        foreach (var theme in Themes.All.Where(t => t.Id != current.Id).Append(current))
        {
            S.Caption(theme.Name, "Themes");
            W.Services.SetTheme(theme.Id);
            await Task.Delay(520);
        }
    }

    private static async Task WindowProfiles()
    {
        var page = await Open("Window Profiles", "windows", "Window profiles", "Window Profiles");
        if (page is null) return;
        if (Find.RowOf(page, "RaceRoom on triples", e => e is Tile && Find.Shown<Button>(e).Any(b => Find.Is(Find.TextOf(b), "Edit"))) is not { } card
            || Find.Button<Button>(card, t => Find.Is(t, "Edit")) is not { } edit)
        {
            Skip("windows: Edit on RaceRoom on triples");
            return;
        }
        S.Caption("Size, spot and presets", "Window Profiles");
        await ClickOn(edit);
        await Stage.Rendered();
        await Task.Delay(1400);
        if (Find.Shown<Button>(W).LastOrDefault(b => Find.Is(Find.TextOf(b), "Cancel")) is not { } cancel)
        {
            Skip("windows: editor Cancel");
            return;
        }
        await ClickOn(cancel);
        await Task.Delay(300);
    }

    private static async Task Fans()
    {
        var page = await Open("Fan Control", "fans", "Fan control");
        if (page is null || Find.PageScroll(page) is not { } scroll) return;
        if (Find.Shown<RadioButton>(page).FirstOrDefault(r => Find.Is(Find.TextOf(r), "Race")) is { } race)
        {
            await ClickOn(race);
            await Stage.Rendered();
            await Task.Delay(300);
        }
        else Skip("fans: Race preset");
        if (Ui.FindAnchor(page, "fans.fans") is { } fanList
            && Find.RowOf(fanList, "Front Intake", e => Find.Shown<Slider>(e).Any()) is { } row
            && Find.Shown<Slider>(row).First() is var slider && Find.Shown<Thumb>(slider).FirstOrDefault() is { } thumb)
        {
            var from = Find.At(thumb);
            double track = Find.At(slider, 1).X - Find.At(slider, 0).X - (Find.At(thumb, 1).X - Find.At(thumb, 0).X);
            double to = from.X + (40 - slider.Value) / (slider.Maximum - slider.Minimum) * track;
            await Pointer.Glide(from);
            await Pointer.Drag(new Point(to, from.Y), 600);
            await Task.Delay(300);
        }
        else Skip("fans: Front Intake speed slider");

        if (Find.Shown<CurveEditor>(page).FirstOrDefault(c => !c.ReadOnly) is { Points.Count: >= 3 } curve)
        {
            S.Caption("Fan curves", "Fan control");
            await Reveal(curve, scroll, 0.3, 650);
            await Stage.Rendered();
            int index = curve.Points.Count - 2;
            var point = curve.Points[index];
            var grab = curve.PointToScreen(curve.ToScreen(point.Temp, point.Percent));
            var drop = curve.PointToScreen(curve.ToScreen(point.Temp + 3, Math.Max(20, point.Percent - 25)));
            await Pointer.Glide(grab);
            await Pointer.Drag(drop, 650);
            await Task.Delay(450);
        }
        else Skip("fans: curve editor");
    }

    private static async Task SimRacing()
    {
        if (await Open("Sim Racing Tools", "simracing", "Setup Manager", "Sim Racing Tools") is not SimRacingPage sim) return;
        await DragSetup(sim, "baseline_safe.sto", "Daytona");
        await DragSetup(sim, "Qualifying.sto", "Road Atlanta");

        if (await Section(sim, "hud", "iRacing Overlay") is { } hud)
        {
            S.Caption("iRacing Overlay", "Sim Racing Tools");
            await Stage.Rendered();
            if (HudCatalog.Find("relative") is { } def
                && Find.Shown<RadioButton>(hud).FirstOrDefault(r => Find.Shown<TextBlock>(r).Any(t => Find.Is(t.Text, def.Name))) is { } item)
            {
                await ClickOn(item);
                await Stage.Rendered();
                await Task.Delay(1400);
            }
            else Skip("hud: widget relative");
        }

        if (await Section(sim, "fov", "FOV calculator") is { } fov)
        {
            S.Caption("FOV calculator", "Sim Racing Tools");
            double drift = Math.Clamp(fov.ActualHeight - sim.Scroll.ViewportHeight * 0.6, 120, 320);
            await ScrollTo(sim.Scroll, drift, 1500, t => t);
        }

        if (await Section(sim, "iracing", "iRacing Controller") is { } controller)
        {
            S.Caption("iRacing Controller", "Sim Racing Tools");
            await Stage.Rendered();
            foreach (var tab in new[] { "Overview and Backups" })
            {
                if (Find.Shown<RadioButton>(controller).FirstOrDefault(r => Find.Is(Find.TextOf(r), tab)) is { } button)
                {
                    await ClickOn(button);
                    await Stage.Rendered();
                    await Task.Delay(650);
                }
                else Skip("controller: tab " + tab);
            }
        }

        if (await Section(sim, "tradingpaints", "Trading Paints") is not null)
        {
            S.Caption("Trading Paints · Garage 61", "Sim Racing Tools");
            await Task.Delay(700);
            await Section(sim, "garage61", "Garage 61");
            await Task.Delay(500);
        }
    }

    private static async Task<FrameworkElement?> Section(SimRacingPage sim, string id, string title)
    {
        var row = Find.Shown<Tile>(W.SidebarView).FirstOrDefault(t => AutomationProperties.GetName(t) == title);
        if (row is not null) await ClickOn(row);
        else Skip("sidebar: section " + title);
        if (sim.CurrentSection != id) sim.ShowSection(id);
        await Stage.Rendered();
        return sim.CurrentSection == id ? sim.PanelSection(id) : null;
    }

    private static async Task DragSetup(SimRacingPage sim, string file, string folder)
    {
        FrameworkElement? Row(string name) => Find.Shown<SetupTree>(sim).FirstOrDefault() is { } tree
            ? Find.Shown<FrameworkElement>(tree).FirstOrDefault(f => f.Tag is SetupNode n && n.Name == name) : null;
        if (Row(file) is not { } from || Row(folder) is not { } into)
        {
            Skip($"setups: {file} → {folder}");
            return;
        }
        await Pointer.Glide(Find.At(from, 0.3));
        await Pointer.Drag(Find.At(into, 0.3), 650);
        await Task.Delay(400);
    }

    private static async Task Displays()
    {
        var page = await Open("Displays", "displays", "Display profiles", "Displays");
        if (page is null || Find.PageScroll(page) is not { } scroll) return;
        if (Find.RowOf(page, "Triples", e => e is Tile && Find.Shown<Button>(e).Any(b => Find.Is(Find.TextOf(b), "Edit"))) is { } profile
            && Find.Button<Button>(profile, t => Find.Is(t, "Edit")) is { } edit)
        {
            await ClickOn(edit);
            await Stage.Rendered();
            await Task.Delay(450);
            if (Find.Shown<RadioButton>(W).FirstOrDefault(r => Find.Is(Find.TextOf(r), "Main display only")) is { } taskbar)
            {
                await ClickOn(taskbar);
                await Task.Delay(400);
            }
            else Skip("displays: taskbar choice");
            if (Find.Shown<Button>(W).LastOrDefault(b => Find.Is(Find.TextOf(b), "Cancel")) is { } cancel) await ClickOn(cancel);
            else W.CloseModal(false);
            await Task.Delay(250);
        }
        else Skip("displays: Edit on Triples");

        if (Find.Shown<ArrangementCanvas>(page).FirstOrDefault() is not { } canvas) return;
        S.Caption("Arrange your monitors", "Displays");
        await Reveal(canvas, scroll, 0.25, 600);
        List<Border> Monitors() => Find.Shown<Border>(canvas).Where(b => b.Tag is string && ReferenceEquals(VisualTreeHelper.GetParent(b), canvas))
            .OrderBy(Canvas.GetLeft).ToList();
        if (Monitors() is not { Count: >= 3 })
        {
            Skip("displays: three monitors in the arrangement");
            return;
        }
        var (right, centre) = (Monitors()[2], Monitors()[1]);
        await Pointer.Glide(Find.At(right));
        await Pointer.Drag(Find.At(centre, 0.5, -0.5), 700);
        await Task.Delay(600);
        if (Find.Shown<Button>(page).FirstOrDefault(b => Find.Is(Find.TextOf(b), "Discard")) is { } discard)
        {
            await ClickOn(discard);
            await Task.Delay(600);
        }
        else Skip("displays: Discard");
    }

    private static async Task Stats()
    {
        var page = await Open("Session Stats", "stats", "Session stats");
        if (page is null || Find.PageScroll(page) is not { } scroll) return;
        await Task.Delay(400);
        var mark = Find.Shown<Icon>(page).FirstOrDefault(i => i.ToolTip is string tip && tip.StartsWith("Lap traces kept", StringComparison.Ordinal));
        Border? hit = null;
        DependencyObject? cell = null;
        for (DependencyObject? d = mark; d is not null && hit is null; cell = d, d = VisualTreeHelper.GetParent(d))
            if (d is Grid table && cell is UIElement c)
                hit = table.Children.OfType<Border>().FirstOrDefault(b => b.ToolTip as string == "Open this session" && Grid.GetRow(b) == Grid.GetRow(c));
        if (hit is null)
        {
            Skip("stats: a session with lap traces");
            return;
        }
        S.Caption("Recent sessions", "Session stats");
        await Reveal(hit, scroll, 0.45, 750);
        await Pointer.Glide(Find.At(hit, 0.2));
        S.Freeze();
        S.Caption("A session, lap by lap", "Session stats");
        await Pointer.Click();
        await S.Reveal();
        await Task.Delay(700);

        if (Find.Shown<CheckBox>(page).FirstOrDefault(c => c.IsEnabled && c.IsChecked != true) is { } second)
        {
            await ClickOn(second);
            await Task.Delay(250);
        }
        if (Find.Shown<Button>(page).FirstOrDefault(b => Find.Is(Find.TextOf(b), "Analyze")) is not { } analyze)
        {
            Skip("stats: Analyze");
            return;
        }
        await Pointer.Glide(Find.At(analyze));
        S.Freeze();
        S.Caption("Lap analysis", "Session stats");
        await Pointer.Click();
        await S.Reveal();
        await Task.Delay(1500);

        if (Find.Shown<LapReplayView>(page).FirstOrDefault() is not { } replay)
        {
            Skip("stats: replay");
            return;
        }
        W.Services.Config.SessionStats.ReplayZoom = 0.85;
        W.Services.Config.SessionStats.ReplaySpeed = 1;
        (page as StatsPage)?.StartReplayAt(80);
        S.Freeze();
        S.Caption("Lap replay", "Session stats");
        replay.BringIntoView();
        await S.Reveal();
        if (Find.Shown<Button>(page).FirstOrDefault(b => b.ToolTip as string == "Play / pause (Space)") is { } play)
        {
            await ClickOn(play);
            await Pointer.Glide(Find.At(play, 4, 2.5), 500);
            await Task.Delay(4300);
        }
        else Skip("stats: play");
    }

    private static async Task PerformanceOverlay()
    {
        var page = await Open("Hardware Monitor", "monitor", "Performance overlay", "Hardware Monitor");
        if (page is null || Find.PageScroll(page) is not { } scroll) return;
        if (Find.Shown<CheckBox>(page).FirstOrDefault(c => Find.Is(Find.TextOf(c), "Show overlay")) is { } show)
        {
            await Reveal(show, scroll, 0.3, 700);
            if (show.IsChecked != true) await ClickOn(show);
            await Task.Delay(800);
        }
        else
        {
            Skip("overlay: Show overlay");
            W.Services.SetOverlayVisible(true);
        }
        if (_cursor is not null) _cursor.Visible = false;
        await S.ShowBackdrop(true, Stage.LongFade);
        await Task.Delay(2100);
    }

    private static async Task IracingOverlays(HudMontage hud)
    {
        Mark("iracing-overlays");
        W.Services.SetOverlayVisible(false);
        S.Caption("iRacing Overlay", $"{hud.Widgets.Count} widgets, live data");
        hud.PopIn(Stagger);
        var hold = hud.PopInTime(Stagger) + TimeSpan.FromMilliseconds(6300);
        var started = Stopwatch.StartNew();
        while (started.Elapsed < hold)
        {
            S.CaptionOnTop();
            await Task.Delay(250);
        }
        _montageOut = hud.FadeOut(500);
    }

    private static Task _montageOut = Task.CompletedTask;

    private static async Task Closing()
    {
        Mark("closing");
        S.HideCaption();
        await S.ShowClosing(Tagline, Stage.LongFade);
        await _montageOut;
        await Task.Delay(1800);
    }

    private static async Task<FrameworkElement?> Open(string card, string key, string caption, string? kicker = null)
    {
        _onLeave?.Invoke();
        _onLeave = null;
        Mark(key);
        var target = Find.Shown<Tile>(W.SidebarView).FirstOrDefault(t => AutomationProperties.GetName(t) == card);
        if (target is not null) await Pointer.Glide(Find.At(target, 0.42));
        else Skip("sidebar: tool " + card);
        S.Freeze();
        S.Caption(caption, kicker);
        if (target is not null) await Pointer.Click();
        if (!Showing(key)) W.Navigate(key);
        await S.Reveal();
        return W.PageFor(key);
    }

    private static bool Showing(string key) => W.PageFor(key) is { } page && W.Showing(page);

    private static async Task Warm()
    {
        var look = W.Services.Config.Overlay.Current;
        look.FontScale = 180;
        look.BackgroundOpacity = 88;
        W.Services.OverlaySettingsChanged();
        foreach (var key in new[] { "windows", "fans", "simracing", "stats", "displays", "monitor" })
        {
            W.Navigate(key);
            await Stage.Rendered();
            if (W.PageFor(key) is SimRacingPage sim)
            {
                sim.Reveal("");
                sim.ShowSection("setups");
                await Stage.Rendered();
                if (sim.Setups?.ScreenshotStates().FirstOrDefault(s => s.Name == "setups") is { Enter: { } enter }) await enter();
                sim.Scroll.ScrollToTop();
            }
            if (key == "monitor")
            {
                W.GoTo("monitor", "monitor.overlay", flash: false);
                await Stage.Rendered();
                if (W.PageFor(key) is { } page) Find.PageScroll(page)?.ScrollToTop();
            }
            await Task.Delay(500);
            await Stage.Rendered();
        }
        W.Navigate(W.FirstPage);
        await Stage.Rendered();
        await Task.Delay(1500);
    }

    private static async Task ClickOn(FrameworkElement target)
    {
        await Pointer.Glide(Find.At(target));
        await Pointer.Click();
    }

    private static ImageSource Backdrop(string folder)
    {
        BitmapSource image = new BitmapImage(new Uri("pack://application:,,,/Assets/OverlayPreview.jpg"));
        string name = "the built-in OverlayPreview.jpg";
        if (new[] { "backdrop.jpg", "backdrop.png" }.Select(n => Path.Combine(folder, n)).FirstOrDefault(File.Exists) is { } path)
        {
            var file = new BitmapImage();
            file.BeginInit();
            file.UriSource = new Uri(Path.GetFullPath(path));
            file.CacheOption = BitmapCacheOption.OnLoad;
            file.EndInit();
            file.Freeze();
            (image, name) = (file, Path.GetFileName(path));
        }
        if (image.PixelWidth < 3840 || image.PixelHeight < 2160)
            Log.Add($"warning: backdrop {name} is {image.PixelWidth}×{image.PixelHeight}, soft at 4K: put a 3840×2160 or larger picture in showcase\\backdrop.jpg");
        return image;
    }

    private static uint MonitorDpi(System.Drawing.Rectangle screen)
    {
        var monitor = MonitorFromPoint(new POINT { X = screen.X + screen.Width / 2, Y = screen.Y + screen.Height / 2 }, 2);
        return GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 ? dpi : 96;
    }

    private static double Top(FrameworkElement e, ScrollViewer scroll) => scroll.VerticalOffset + e.TranslatePoint(new Point(), scroll).Y;

    private static bool InView(FrameworkElement e, ScrollViewer scroll)
    {
        double y = e.TranslatePoint(new Point(), scroll).Y;
        return y >= 0 && y + e.ActualHeight <= scroll.ViewportHeight;
    }

    private static Task Reveal(FrameworkElement e, ScrollViewer scroll, double at, double ms = 600)
    {
        double target = Math.Clamp(Top(e, scroll) - scroll.ViewportHeight * at, 0, scroll.ScrollableHeight);
        return InView(e, scroll) && Math.Abs(target - scroll.VerticalOffset) < scroll.ViewportHeight * 0.25 ? Task.CompletedTask : ScrollTo(scroll, target, ms);
    }

    private static Task ScrollTo(ScrollViewer scroll, double to, double ms, Func<double, double>? ease = null)
    {
        to = Math.Clamp(to, 0, scroll.ScrollableHeight);
        var done = new TaskCompletionSource();
        double from = scroll.VerticalOffset;
        var started = Stopwatch.StartNew();
        void Step(object? s, EventArgs e)
        {
            double t = Math.Min(1, started.Elapsed.TotalMilliseconds / ms);
            double eased = ease?.Invoke(t) ?? (t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2);
            scroll.ScrollToVerticalOffset(from + (to - from) * eased);
            if (t < 1) return;
            CompositionTarget.Rendering -= Step;
            done.TrySetResult();
        }
        CompositionTarget.Rendering += Step;
        return done.Task;
    }

    private static void Mark(string shot) => Log.Add($"{Clock.Elapsed.TotalSeconds - _start:0.00} {shot}");

    private static void Skip(string what) => Log.Add($"{Clock.Elapsed.TotalSeconds - _start:0.00} skipped: {what}");

    private static void SquareCorners(Window window)
    {
        int square = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 33, ref square, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);
}

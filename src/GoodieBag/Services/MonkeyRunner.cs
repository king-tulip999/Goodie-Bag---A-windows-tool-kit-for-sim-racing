using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Config;
using GoodieBag.Views;

namespace GoodieBag.Services;

internal static class MonkeyRunner
{
    private static readonly string[] Junk =
    {
        "19.45", "1,5", "-3", "0", "99999999", "😀🏁", "  ", "", new string('W', 300), "\\ / : * ? \" < > |", "1:23.456", "abc", "NaN", "1e308",
    };

    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { "MinButton", "MaxButton", "CloseButton", "SetupMinButton", "SetupCloseButton" };

    private static readonly List<string> Errors = new();
    private static StreamWriter? _log;
    private static int _step;

    private static readonly List<long> _heaps = new();

    public static bool Active { get; private set; }

    public static void RecordError(Exception ex)
    {
        lock (Errors) Errors.Add($"step {_step}: {ex}");
        Log("  ERROR " + ex.GetType().Name + ": " + ex.Message);
    }

    private static void Problem(string text)
    {
        lock (Errors) Errors.Add($"step {_step}: {text}");
        Log("  PROBLEM " + text);
    }

    private static void Log(string text)
    {
        try { _log?.WriteLine(text); } catch (IOException) { }
    }

    public static async void Run(MainWindow window, string folder, int seed, double minutes)
    {
        Active = true;
        Directory.CreateDirectory(folder);
        _log = new StreamWriter(Path.Combine(folder, "steps.txt")) { AutoFlush = true };
        External.Gate = what => { Log("  blocked: " + what); return false; };
        var rnd = new Random(seed);
        Log($"seed {seed}, {minutes} min");
        var app = AppServices.Current!;
        StartWatchdog(folder);
        HitchWatch.Start(window.Dispatcher, 150, () => $"step {_step} on {window.CurrentPage}");
        HitchWatch.WatchFrames(true);
        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            (window.Left, window.Top, window.Width, window.Height) = (0, 0, 1280, 820);
            window.Show();
            await Task.Delay(1500);
            window.Navigate(window.FirstPage);
            await Settle();
            int baseline = TickHandlers(app);
            Log($"tick handlers on the first page: {baseline}");
            var end = DateTime.UtcNow.AddMinutes(minutes);
            while (DateTime.UtcNow < end)
            {
                _step++;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                try { await StepAsync(window, app, rnd); }
                catch (Exception ex) { RecordError(ex); }
                if (clock.Elapsed.TotalMilliseconds >= 300) Log($"  slow: {clock.Elapsed.TotalMilliseconds:0} ms");
                await Task.Delay(rnd.Next(20, 160));
                if (_step % 30 == 0) await CheckAsync(window, app, baseline);
            }
            await CheckAsync(window, app, baseline);
        }
        catch (Exception ex)
        {
            RecordError(ex);
        }
        finally
        {
            File.WriteAllLines(Path.Combine(folder, "hitches.txt"), HitchWatch.Report(HitchWatch.Take()));
            lock (Errors)
                File.WriteAllText(Path.Combine(folder, "errors.txt"), Errors.Count == 0 ? "none\n" : string.Join("\n\n", Errors));
            Log($"done after {_step} steps");
            _log?.Dispose();
            External.Gate = null;
            App.Quit();
        }
    }

    private static void StartWatchdog(string folder)
    {
        var thread = new Thread(() =>
        {
            int seen = -1;
            var since = DateTime.UtcNow;
            while (Active)
            {
                Thread.Sleep(5000);
                if (_step != seen) { seen = _step; since = DateTime.UtcNow; continue; }
                if (DateTime.UtcNow - since < TimeSpan.FromSeconds(60)) continue;
                lock (Errors)
                {
                    Errors.Add($"step {_step}: the UI thread made no progress for 60 s (a layout loop or a deadlock); the last steps are in steps.txt");
                    File.WriteAllText(Path.Combine(folder, "errors.txt"), string.Join(Environment.NewLine + Environment.NewLine, Errors));
                }
                Log("  STUCK: no progress for 60 s");
                _log?.Flush();
                Environment.Exit(3);
            }
        }) { IsBackground = true, Name = "Monkey watchdog" };
        thread.Start();
    }

    private static Task Settle() => Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task;

    private static async Task StepAsync(MainWindow window, AppServices app, Random rnd)
    {
        if (!window.IsVisible || window.WindowState == WindowState.Minimized)
        {
            Log($"{_step} show window");
            window.WindowState = WindowState.Normal;
            window.Show();
            return;
        }
        int roll = rnd.Next(100);
        switch (roll)
        {
            case < 46:
                Interact(window, rnd);
                break;
            case < 56:
            {
                var keys = window.NavKeys;
                string key = keys[rnd.Next(keys.Count)];
                Log($"{_step} page {key}");
                window.Navigate(key);
                break;
            }
            case < 62:
                Key(window, System.Windows.Input.Key.Escape);
                break;
            case < 66:
                Key(window, System.Windows.Input.Key.Enter);
                break;
            case < 69:
                Key(window, System.Windows.Input.Key.Tab);
                break;
            case < 74:
                Resize(window, rnd);
                break;
            case < 76:
            {
                var themes = Theme.Themes.All;
                var theme = themes[rnd.Next(themes.Count)];
                Log($"{_step} theme {theme.Id}");
                app.SetTheme(theme.Id);
                break;
            }
            case < 86:
                Scroll(window, rnd);
                break;
            case < 94:
                Type(window, rnd);
                break;
            case < 97:
                RightClick(window, rnd);
                break;
            default:
                Log($"{_step} first page");
                window.Navigate(window.FirstPage);
                break;
        }
        await Settle();
    }

    private static IEnumerable<Visual> Roots(Window window)
    {
        if (window.Content is Visual root) yield return root;
        foreach (PresentationSource source in PresentationSource.CurrentSources)
            if (source.RootVisual is FrameworkElement { IsVisible: true } popup && popup.GetType().Name == "PopupRoot")
                yield return popup;
    }

    private static List<T> Find<T>(Window window, Func<T, bool>? keep = null) where T : FrameworkElement
    {
        var found = new List<T>();
        foreach (var root in Roots(window)) Walk(root, found, keep);
        return found;
    }

    private static void Walk<T>(DependencyObject node, List<T> found, Func<T, bool>? keep) where T : FrameworkElement
    {
        if (node is UIElement { IsVisible: false }) return;
        if (node is T t && t.IsEnabled && t.IsHitTestVisible && t.ActualWidth > 0 && t.ActualHeight > 0 && !Skipped.Contains(t.Name) && (keep is null || keep(t)))
            found.Add(t);
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i), found, keep);
    }

    private static string Describe(FrameworkElement e)
    {
        string text = e switch
        {
            ContentControl { Content: string s } => s,
            TextBox tb => "text box " + tb.Tag,
            _ => FirstText(e) ?? "",
        };
        if (text.Length == 0 && e.ToolTip is string tip) text = "tip: " + tip;
        if (text.Length > 50) text = text[..50];
        return $"{e.GetType().Name}{(e.Name.Length > 0 ? "#" + e.Name : "")} “{text}”";
    }

    private static string? FirstText(DependencyObject node)
    {
        if (node is TextBlock { Text.Length: > 0 } tb) return Theme.TextCase.Plain(tb.Text);
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            if (FirstText(VisualTreeHelper.GetChild(node, i)) is { } t) return t;
        return null;
    }

    private static void Interact(Window window, Random rnd)
    {
        var controls = Find<Control>(window, c => c is ButtonBase or ComboBox or Slider or TextBox);
        if (controls.Count == 0) return;
        var c = controls[rnd.Next(controls.Count)];
        Log($"{_step} {Describe(c)}");
        switch (c)
        {
            case ComboBox combo when combo.Items.Count > 0:
                combo.SelectedIndex = rnd.Next(combo.Items.Count);
                break;
            case Slider slider:
                slider.Value = slider.Minimum + rnd.NextDouble() * (slider.Maximum - slider.Minimum);
                break;
            case TextBox box:
                box.Focus();
                box.Text = Junk[rnd.Next(Junk.Length)];
                break;
            case ToggleButton toggle when UIElementAutomationPeer.CreatePeerForElement(toggle) is IToggleProvider t:
                t.Toggle();
                break;
            case ButtonBase button when UIElementAutomationPeer.CreatePeerForElement(button) is IInvokeProvider invoke:
                invoke.Invoke();
                if (rnd.Next(6) == 0) invoke.Invoke();
                break;
            case ButtonBase button:
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
                break;
        }
    }

    private static void Type(Window window, Random rnd)
    {
        var boxes = Find<TextBox>(window, b => !b.IsReadOnly);
        if (boxes.Count == 0) return;
        var box = boxes[rnd.Next(boxes.Count)];
        string text = Junk[rnd.Next(Junk.Length)];
        Log($"{_step} type “{(text.Length > 20 ? text[..20] + "…" : text)}” into {Describe(box)}");
        box.Focus();
        box.Text = text;
        if (rnd.Next(2) == 0) Key(window, System.Windows.Input.Key.Enter);
    }

    private static void RightClick(Window window, Random rnd)
    {
        var withMenus = Find<FrameworkElement>(window, e => e.ContextMenu is not null);
        if (withMenus.Count == 0) return;
        var e = withMenus[rnd.Next(withMenus.Count)];
        Log($"{_step} context menu on {Describe(e)}");
        e.ContextMenu!.PlacementTarget = e;
        e.ContextMenu.IsOpen = true;
    }

    private static void Scroll(Window window, Random rnd)
    {
        var viewers = Find<ScrollViewer>(window, s => s.ScrollableHeight > 0 || s.ScrollableWidth > 0);
        if (viewers.Count == 0) return;
        var v = viewers[rnd.Next(viewers.Count)];
        double to = rnd.NextDouble() * v.ScrollableHeight;
        Log($"{_step} scroll {Describe(v)} to {to:0}");
        v.ScrollToVerticalOffset(to);
    }

    private static void Resize(Window window, Random rnd)
    {
        if (rnd.Next(5) == 0)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            Log($"{_step} window {window.WindowState}");
            return;
        }
        window.WindowState = WindowState.Normal;
        window.Width = rnd.Next(960, 2561);
        window.Height = rnd.Next(600, 1441);
        Log($"{_step} window {window.Width}x{window.Height}");
    }

    private static void Key(Window window, Key key)
    {
        var target = Keyboard.FocusedElement as IInputElement ?? window;
        if (PresentationSource.FromVisual(window) is not { } source) return;
        Log($"{_step} key {key}");
        foreach (var routed in new[] { Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent })
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = routed });
    }

    private static int TickHandlers(AppServices app) =>
        typeof(AppServices).GetField("Tick", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(app) is Delegate d ? d.GetInvocationList().Length : 0;

    private static List<(Popup Popup, FrameworkElement Target)> Orphans()
    {
        var found = new List<(Popup, FrameworkElement)>();
        foreach (PresentationSource source in PresentationSource.CurrentSources)
            if (source.RootVisual is FrameworkElement { IsVisible: true } root && root.GetType().Name == "PopupRoot"
                && root.Parent is Popup { IsOpen: true, PlacementTarget: FrameworkElement target } popup && (!target.IsVisible || PresentationSource.FromVisual(target) is null))
                found.Add((popup, target));
        return found;
    }

    private static async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var frame = new TaskCompletionSource();
            void OnFrame(object? s, EventArgs e) { CompositionTarget.Rendering -= OnFrame; frame.TrySetResult(); }
            CompositionTarget.Rendering += OnFrame;
            await frame.Task;
        }
    }

    private static async Task CheckAsync(MainWindow window, AppServices app, int baseline)
    {
        var copy = ConfigStore.Clone(app.Config);
        string once = JsonSerializer.Serialize(ConfigStore.Normalize(copy), ConfigStore.JsonOptions);
        string twice = JsonSerializer.Serialize(ConfigStore.Normalize(copy), ConfigStore.JsonOptions);
        if (once != twice) Problem("config: Normalize twice differs from once");
        foreach (var d in ConfigReferences.Dangling(app.Config)) Problem("config: dangling " + d);

        if (Orphans().Count > 0)
        {
            await Frames(2);
            await Settle();
            foreach (var (popup, target) in Orphans())
                Problem($"a popup stayed open after its target went away: {(popup.Child is FrameworkElement child ? Describe(child) : "?")} on {Describe(target)}");
        }

        while (window.WindowState == WindowState.Minimized || !window.IsVisible) { window.WindowState = WindowState.Normal; window.Show(); await Settle(); }
        Key(window, System.Windows.Input.Key.Escape);
        window.Navigate(window.FirstPage);
        await Task.Delay(300);
        await Settle();
        int now = TickHandlers(app);
        if (now > baseline) Problem($"tick handlers on the first page grew from {baseline} to {now}");
        await Task.Delay(700);
        if (window.PageMove.Y != 0 && !window.PageMove.HasAnimatedProperties)
            Problem($"the page stayed {window.PageMove.Y:0} px off its place after the page animation");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        long heap = GC.GetTotalMemory(false) / 1048576;
        _heaps.Add(heap);
        Log($"{_step} check: tick handlers {now}, managed heap {heap} MB, working set {me.WorkingSet64 / 1048576} MB, handles {me.HandleCount}, threads {me.Threads.Count}");
        if (_heaps.Count >= 14 && _heaps.Skip(_heaps.Count - 10).Min() > 2 * _heaps.Skip(1).Take(3).Min() + 30)
            Problem($"managed heap keeps growing: {string.Join(" → ", _heaps.Skip(_heaps.Count - 10))} MB");
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GoodieBag.Views;

namespace GoodieBag.Services;

internal static class PerfProbe
{
    private static readonly List<(string Name, double Ms)> Marks = new();

    public static string? File { get; set; }

    public static void Mark(string name) => Marks.Add((name, (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds));

    public static async void Run(MainWindow window, string file)
    {
        var lines = new List<string>();
        void Put(string name, double value) => lines.Add($"{name} {value:0.0}");
        var me = Process.GetCurrentProcess();
        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = 0;
            window.Top = 0;
            window.Width = 1280;
            window.Height = 820;
            window.Show();
            Mark("shown");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Mark("idle");
            await Rendered(window);
            Put("startup_ms", (DateTime.Now - me.StartTime).TotalMilliseconds);
            foreach (var (name, ms) in Marks) Put($"startup_{name}_ms", ms);
            await Task.Delay(3000);
            await Idle("idle_main", window, lines);
            await InvalidElements("main", window, lines);
            Motion.SetWindowActive(false, force: true);
            await Task.Delay(2500);
            await Idle("idle_main_unfocused", window, lines);
            Motion.SetWindowActive(true, force: true);
            await Task.Delay(1500);
            var ui = AppServices.Current!.Config.Ui;
            ui.Animations = false;
            Controls.NavIcon.RefreshAll();
            window.Navigate("settings");
            window.Navigate("plugins");
            await Task.Delay(1500);
            await Idle("idle_main_still", window, lines);
            ui.Animations = true;
            Controls.NavIcon.RefreshAll();
            foreach (var pass in new[] { "first", "again" })
                foreach (var key in window.NavKeys.Where(k => k != "plugins").Append("plugins"))
                {
                    var opened = new TaskCompletionSource();
                    var input = LongestInputWait(window, opened.Task);
                    await Task.Delay(50);
                    var clock = Stopwatch.StartNew();
                    double firstFrame = 0;
                    void OnFirstFrame(object? s, EventArgs e) { CompositionTarget.Rendering -= OnFirstFrame; firstFrame = clock.Elapsed.TotalMilliseconds; }
                    CompositionTarget.Rendering += OnFirstFrame;
                    lines.Add($"# navigate {key} {pass} {DateTime.Now:HH:mm:ss.fff}");
                    window.Navigate(key);
                    await Rendered(window);
                    Put($"open_{pass}_{key}_first_frame_ms", firstFrame);
                    Put($"open_{pass}_{key}_ms", clock.Elapsed.TotalMilliseconds);
                    opened.SetResult();
                    Put($"open_{pass}_{key}_input_wait_ms", await input);
                }
            var original = Theme.ThemeManager.Current.Id;
            foreach (var theme in Theme.Themes.All.Where(t => t.Id != original).Append(Theme.ThemeManager.Current))
            {
                var clock = Stopwatch.StartNew();
                Theme.ThemeManager.Apply(theme.Id);
                Put($"theme_{theme.Id}_apply_ms", clock.Elapsed.TotalMilliseconds);
                await Rendered(window);
                Put($"theme_{theme.Id}_total_ms", clock.Elapsed.TotalMilliseconds);
                await Task.Delay(300);
            }
            window.Navigate("settings");
            await Rendered(window);
            await Task.Delay(1000);
            foreach (var theme in Theme.Themes.All.Where(t => t.Id != original).Append(Theme.ThemeManager.Current))
            {
                var clock = Stopwatch.StartNew();
                double firstFrame = 0;
                void OnFirstFrame(object? s, EventArgs e) { CompositionTarget.Rendering -= OnFirstFrame; firstFrame = clock.Elapsed.TotalMilliseconds; }
                CompositionTarget.Rendering += OnFirstFrame;
                AppServices.Current!.SwitchThemeUnsaved(theme.Id);
                Put($"theme_switch_{theme.Id}_ms", clock.Elapsed.TotalMilliseconds);
                await Rendered(window);
                Put($"theme_switch_{theme.Id}_first_frame_ms", firstFrame);
                Put($"theme_switch_{theme.Id}_shown_ms", clock.Elapsed.TotalMilliseconds);
                await Task.Delay(300);
            }
            if (Environment.GetCommandLineArgs().Contains("--soak", StringComparer.OrdinalIgnoreCase))
                await Soak(window, lines);
            window.Navigate("monitor");
            await Task.Delay(2000);
            await Idle("idle_monitor", window, lines);
            if (window.PageOf("monitor") is Views.Pages.HardwareMonitorPage monitor)
            {
                var sensors = monitor.Sensors;
                if (!sensors.Expanded) { sensors.Toggle(); await Rendered(window); await Task.Delay(1000); }
                for (int i = 0; i < 3; i++)
                {
                    var clock = Stopwatch.StartNew();
                    sensors.Toggle();
                    await Rendered(window);
                    Put($"sensors_fold_{i}_ms", clock.Elapsed.TotalMilliseconds);
                    await Task.Delay(500);
                    clock.Restart();
                    sensors.Toggle();
                    await Rendered(window);
                    Put($"sensors_unfold_{i}_ms", clock.Elapsed.TotalMilliseconds);
                    await Task.Delay(500);
                }
            }
            window.Navigate("modes");
            await Task.Delay(2000);
            await Idle("idle_modes", window, lines);
            window.Navigate("plugins");
            await Task.Delay(3000);
            await Idle("idle_main_settled", window, lines);
            await InvalidElements("main_settled", window, lines);
            Motion.SetWindowActive(false, force: true);
            await Task.Delay(2500);
            await Idle("idle_main_settled_unfocused", window, lines);
            Motion.SetWindowActive(true, force: true);
            ui.Animations = false;
            Controls.NavIcon.RefreshAll();
            window.Navigate("settings");
            window.Navigate("plugins");
            await Task.Delay(1500);
            await Idle("idle_main_settled_still", window, lines);
            ui.Animations = true;
            Controls.NavIcon.RefreshAll();
            window.Hide();
            await Task.Delay(4000);
            await Idle("idle_hidden", window, lines);
            window.TrimWhileHidden();
            await Task.Delay(2000);
            await Idle("idle_hidden_trimmed", window, lines);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            me.Refresh();
            Put("working_set_mb", me.WorkingSet64 / 1048576.0);
            Put("private_mb", me.PrivateMemorySize64 / 1048576.0);
            Put("managed_heap_mb", GC.GetTotalMemory(false) / 1048576.0);
            Put("threads", me.Threads.Count);
            Put("handles", me.HandleCount);
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "?").Order(StringComparer.Ordinal).ToList();
            Put("assemblies_loaded", loaded.Count);
            foreach (var name in loaded) lines.Add("# loaded " + name);
        }
        catch (Exception ex)
        {
            lines.Add("error " + ex.ToString().Replace(Environment.NewLine, " | "));
        }
        finally
        {
            System.IO.File.AppendAllLines(file, lines);
            Application.Current.Shutdown();
        }
    }

    private static async Task Soak(MainWindow window, List<string> lines)
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.FindIndex(args, a => a.Equals("--soak", StringComparison.OrdinalIgnoreCase));
        int rounds = at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int n) && n > 0 ? n : 8;
        var me = Process.GetCurrentProcess();
        var original = Theme.ThemeManager.Current.Id;
        for (int round = 1; round <= rounds; round++)
        {
            foreach (var key in window.NavKeys)
            {
                window.Navigate(key);
                await Rendered(window);
            }
            foreach (var theme in Theme.Themes.All.Where(t => t.Id != original).Append(Theme.ThemeManager.Current))
            {
                Theme.ThemeManager.Apply(theme.Id);
                await Rendered(window);
            }
            window.Navigate("plugins");
            await Task.Delay(500);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            me.Refresh();
            lines.Add($"soak_{round} heap_mb {GC.GetTotalMemory(false) / 1048576.0:0.00} handles {me.HandleCount} " +
                      $"gdi {GetGuiResources(me.Handle, 0)} user {GetGuiResources(me.Handle, 1)} private_mb {me.PrivateMemorySize64 / 1048576.0:0.0}");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr process, int flags);

    private static async Task Idle(string name, MainWindow window, List<string> lines)
    {
        var me = Process.GetCurrentProcess();
        var cpu = me.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        int collections = GC.CollectionCount(0);
        long allocated = GC.GetTotalAllocatedBytes();
        int frames = 0, layouts = 0;
        void OnFrame(object? s, EventArgs e) => frames++;
        void OnLayout(object? s, EventArgs e) => layouts++;
        window.LayoutUpdated += OnLayout;
        await Task.Delay(10_000);
        window.LayoutUpdated -= OnLayout;
        me.Refresh();
        lines.Add($"{name}_cpu_pct {(me.TotalProcessorTime - cpu).TotalMilliseconds / clock.Elapsed.TotalMilliseconds * 100:0.00}");
        lines.Add($"{name}_alloc_kb_per_s {(GC.GetTotalAllocatedBytes() - allocated) / 1024.0 / clock.Elapsed.TotalSeconds:0.0}");
        lines.Add($"{name}_gen0 {GC.CollectionCount(0) - collections}");
        lines.Add($"{name}_layouts_per_s {layouts / clock.Elapsed.TotalSeconds:0.0}");
        lines.Add($"{name}_working_set_mb {me.WorkingSet64 / 1048576.0:0.0}");
        using var sampler = new AllocationSampler();
        CompositionTarget.Rendering += OnFrame;
        clock.Restart();
        await Task.Delay(3000);
        CompositionTarget.Rendering -= OnFrame;
        lines.Add($"{name}_frames_per_s {frames / clock.Elapsed.TotalSeconds:0.0}");
        foreach (var (type, count) in sampler.Top(8))
            lines.Add($"# {name} alloc sample {count} {type}");
    }

    private sealed class AllocationSampler : System.Diagnostics.Tracing.EventListener
    {
        private readonly Dictionary<string, int> _counts = new();

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (e.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) != true || e.PayloadNames is null) return;
            int i = e.PayloadNames.IndexOf("TypeName");
            if (i < 0 || e.Payload?[i] is not string type) return;
            lock (_counts) _counts[type] = _counts.GetValueOrDefault(type) + 1;
        }

        public List<(string Type, int Count)> Top(int n)
        {
            lock (_counts) return _counts.OrderByDescending(p => p.Value).Take(n).Select(p => (p.Key, p.Value)).ToList();
        }
    }

    private static async Task InvalidElements(string name, Window window, List<string> lines)
    {
        var counts = new Dictionary<string, int>();
        void Walk(DependencyObject node, string path)
        {
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is UIElement { IsVisible: false }) continue;
                if (child is UIElement { IsMeasureValid: false } or UIElement { IsArrangeValid: false })
                {
                    var key = child.GetType().Name + (child is FrameworkElement { Name.Length: > 0 } fe ? "#" + fe.Name : "") + " in " + path;
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
                Walk(child, child is FrameworkElement { Name.Length: > 0 } named ? named.Name : path);
            }
        }
        void OnFrame(object? s, EventArgs e) => Walk(window, "window");
        CompositionTarget.Rendering += OnFrame;
        await Task.Delay(3000);
        CompositionTarget.Rendering -= OnFrame;
        foreach (var (key, count) in counts.OrderByDescending(p => p.Value).Take(10))
            lines.Add($"# {name} invalid {count} {key}");
    }

    private static async Task<double> LongestInputWait(Window window, Task until)
    {
        double longest = 0;
        using var done = new CancellationTokenSource();
        var watcher = Task.Run(async () =>
        {
            while (!done.IsCancellationRequested)
            {
                var wait = Stopwatch.StartNew();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
                longest = Math.Max(longest, wait.Elapsed.TotalMilliseconds);
                await Task.Delay(5);
            }
        });
        await until;
        await Task.Delay(1000);
        done.Cancel();
        await watcher;
        return longest;
    }

    private static async Task Rendered(Window window)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var frame = new TaskCompletionSource();
        void OnFrame(object? s, EventArgs e) { CompositionTarget.Rendering -= OnFrame; frame.TrySetResult(); }
        CompositionTarget.Rendering += OnFrame;
        await frame.Task;
    }
}

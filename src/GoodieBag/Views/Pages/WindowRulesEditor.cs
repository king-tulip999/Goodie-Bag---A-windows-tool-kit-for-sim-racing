using System.IO;
using System.Windows;
using System.Windows.Controls;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Display;
using GoodieBag.Modes;
using GoodieBag.Services;

namespace GoodieBag.Views.Pages;

internal sealed class WindowRulesEditor : StackPanel
{
    private readonly AppServices _app;
    private readonly StackPanel _rows = new();

    public WindowRulesEditor(AppServices app, IEnumerable<WindowRule> rules)
    {
        _app = app;
        Rules = rules.Select(r => r.Copy()).ToList();
        Children.Add(new TextBlock
        {
            Text = "Force a program's window to an exact size and spot, e.g. one game across triple screens, borderless. Applied when this mode is applied and whenever the program starts while it's active.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        });
        Children.Add(_rows);
        var add = Ui.LabeledButton("I.Plus", "Add window…", () => { }, "SoftButton");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += (_, _) => ShowPrograms(add, all: false, process =>
        {
            var screen = Bounds(Monitors());
            Rules.Add(new WindowRule { Name = Path.GetFileNameWithoutExtension(process), ProcessName = process, X = screen.X, Y = screen.Y, Width = screen.W, Height = screen.H });
            Fill();
        });
        Children.Add(add);
        Fill();
    }

    public List<WindowRule> Rules { get; }

    public string? Problem => Rules.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.ProcessName)) is { } missing
        ? $"Choose the program for the window “{missing.Label}”." : null;

    private void Fill()
    {
        _rows.Children.Clear();
        foreach (var rule in Rules)
            _rows.Children.Add(Row(rule));
        if (Rules.Count == 0)
            _rows.Children.Add(new TextBlock { Text = "None: windows stay where their programs put them.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 6) });
    }

    private Tile Row(WindowRule rule)
    {
        var stack = new StackPanel();

        var head = new DockPanel { LastChildFill = true };
        var remove = Ui.IconButton("I.Trash", "Remove this window", () => { Rules.Remove(rule); Fill(); });
        DockPanel.SetDock(remove, Dock.Right);
        head.Children.Add(remove);
        var test = Ui.LabeledButton("I.Play", "Try it now", () => _ = _app.WindowRules.ApplyAsync(rule, retry: false, watch: false), "GhostButton");
        test.Margin = new Thickness(12, 0, 8, 0);
        test.ToolTip = "Apply these values to the program's window now";
        DockPanel.SetDock(test, Dock.Right);
        head.Children.Add(test);
        var name = new TextBox { Text = rule.Name, Tag = "Name", Width = 170, Margin = new Thickness(0, 0, 12, 0) };
        name.TextChanged += (_, _) => rule.Name = name.Text.Trim();
        head.Children.Add(name);
        var program = new TextBox { Text = rule.ProcessName, Tag = "e.g. RRRE64.exe", Width = 160 };
        program.TextChanged += (_, _) => rule.ProcessName = program.Text.Trim();
        head.Children.Add(program);
        var pick = Ui.IconButton("I.AppWindow", "Pick from the programs running now", () => { });
        pick.Margin = new Thickness(8, 0, 4, 0);
        pick.Click += (_, _) => ShowPrograms(pick, all: false, p => { if (p.Length > 0) program.Text = p; else { program.Focus(); program.SelectAll(); } });
        head.Children.Add(pick);
        var all = Ui.IconButton("I.Search", "All processes (also ones without a window)", () => { });
        all.Click += (_, _) => ShowPrograms(all, all: true, p => program.Text = p);
        head.Children.Add(all);
        head.Children.Add(new FrameworkElement());
        stack.Children.Add(head);

        var width = new NumberBox(rule.Width ?? 1920, "px", 80) { Minimum = 100, Maximum = 32000 };
        var height = new NumberBox(rule.Height ?? 1080, "px", 80) { Minimum = 100, Maximum = 32000 };
        var x = new NumberBox(rule.X, "px", 80) { Minimum = -32000, Maximum = 32000 };
        var y = new NumberBox(rule.Y, "px", 80) { Minimum = -32000, Maximum = 32000 };
        var keep = new CheckBox { Content = "Keep its size", IsChecked = rule.Width is null, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 24, 8) };
        void SyncSize()
        {
            bool k = keep.IsChecked == true;
            width.IsEnabled = height.IsEnabled = !k;
            rule.Width = k ? null : (int)width.Value;
            rule.Height = k ? null : (int)height.Value;
        }
        keep.Click += (_, _) => SyncSize();
        width.ValueChanged += _ => SyncSize();
        height.ValueChanged += _ => SyncSize();
        x.ValueChanged += v => rule.X = (int)v;
        y.ValueChanged += v => rule.Y = (int)v;
        width.IsEnabled = height.IsEnabled = rule.Width is not null;

        var presets = new ComboBox { MinWidth = 150, Margin = new Thickness(0, 0, 24, 8) };
        presets.Items.Add(new Choice(null, "Pick preset"));
        var list = Presets().ToList();
        foreach (var preset in list) presets.Items.Add(preset.Choice);
        presets.SelectedIndex = 0;
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedIndex <= 0) return;
            var picked = list[presets.SelectedIndex - 1];
            var r = picked.Rect ?? (picked.Choice.Id == "current" ? WindowRuleService.CurrentRect(rule.ProcessName) : null);
            if (r is { } rect)
            {
                (x.Value, y.Value, width.Value, height.Value) = (rect.X, rect.Y, rect.W, rect.H);
                (rule.X, rule.Y) = (rect.X, rect.Y);
                keep.IsChecked = false;
                SyncSize();
            }
            else if (picked.Choice.Id == "current")
                _app.Notify("No window to read", $"Start {(rule.ProcessName.Length > 0 ? rule.ProcessName : "the program")} first, then try again.", NoticeKind.Info);
            presets.SelectedIndex = 0;
        };

        var place = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        place.Children.Add(presets);
        place.Children.Add(Pair("Size", width, "×", height));
        place.Children.Add(keep);
        place.Children.Add(Pair("At", x, ",", y));
        stack.Children.Add(place);

        var borders = new CheckBox { Content = "Remove borders", IsChecked = rule.RemoveBorders, ToolTip = "Strips the frame and title bar. Only for games without a borderless option of their own.", Margin = new Thickness(0, 0, 24, 8), VerticalAlignment = VerticalAlignment.Center };
        var titleBar = new CheckBox { Content = "Hide title bar", IsChecked = rule.HideTitleBar, IsEnabled = rule.RemoveBorders, ToolTip = "For Store / Xbox app games (e.g. Forza Horizon 4) that keep a title strip after removing borders: pushes it above the screen.", Margin = new Thickness(0, 0, 32, 8), VerticalAlignment = VerticalAlignment.Center };
        borders.Click += (_, _) => { rule.RemoveBorders = borders.IsChecked == true; titleBar.IsEnabled = rule.RemoveBorders; rule.HideTitleBar = rule.RemoveBorders && titleBar.IsChecked == true; };
        titleBar.Click += (_, _) => rule.HideTitleBar = rule.RemoveBorders && titleBar.IsChecked == true;
        var delay = new NumberBox(rule.DelayMs, "ms", 80) { Minimum = 0, Maximum = 120000, Step = 250 };
        delay.ValueChanged += v => rule.DelayMs = (int)v;
        delay.ToolTip = "Wait this long after the program starts. Goodie Bag also re-applies for about 35 s if the game moves the window.";
        var style = new WrapPanel { Margin = new Thickness(0, 4, 0, -8) };
        style.Children.Add(borders);
        style.Children.Add(titleBar);
        style.Children.Add(Pair("Delay after start", delay));
        stack.Children.Add(style);

        return new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(14, 12, 12, 12), Margin = new Thickness(0, 0, 0, 10), Child = stack };
    }

    private static StackPanel Pair(string label, FrameworkElement a, string? between = null, FrameworkElement? b = null, double left = 0)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(left, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        panel.Children.Add(a);
        if (b is not null)
        {
            panel.Children.Add(new TextBlock { Text = between, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(b);
        }
        return panel;
    }

    private static void ShowPrograms(FrameworkElement anchor, bool all, Action<string> picked)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxHeight = 420 };
        IEnumerable<(string Process, string? Title)> items = all
            ? System.Diagnostics.Process.GetProcesses().Select(p => { try { return p.ProcessName + ".exe"; } catch { return ""; } finally { p.Dispose(); } })
                .Where(n => n.Length > 4).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => (n, (string?)null))
            : WindowRuleService.ProgramsWithWindows().Select(w => (w.Process, (string?)w.Title));
        foreach (var (process, title) in items)
        {
            var item = new MenuItem { Header = title is null ? process : $"{process}  ·  {title}" };
            item.Click += (_, _) => picked(process);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "Nothing running with a window", IsEnabled = false });
        var manual = new MenuItem { Header = "Type the exe name instead" };
        manual.Click += (_, _) => picked("");
        if (!all) menu.Items.Add(manual);
        menu.IsOpen = true;
    }

    private static (int X, int Y, int W, int H) Bounds(List<MonitorInfo> active)
    {
        if (active.Count == 0) return (0, 0, 1920, 1080);
        int left = active.Min(m => m.X), top = active.Min(m => m.Y);
        return (left, top, active.Max(m => m.X + m.Width) - left, active.Max(m => m.Y + m.Height) - top);
    }

    private List<MonitorInfo> Monitors()
    {
        try { return _app.Displays.GetMonitors().Where(m => m.Active).ToList(); }
        catch { return new List<MonitorInfo>(); }
    }

    private IEnumerable<(Choice Choice, (int X, int Y, int W, int H)? Rect)> Presets()
    {
        var monitors = Monitors().OrderBy(m => m.X).ToList();
        var all = Bounds(monitors);
        if (monitors.Count > 1) yield return (new Choice("all", $"All displays · {all.W} × {all.H} at {all.X}, {all.Y}"), all);
        foreach (var m in monitors)
            yield return (new Choice("m" + m.Key, $"Display {m.Label}{(m.Primary ? " (main)" : "")} · {m.Width} × {m.Height} at {m.X}, {m.Y}"), (m.X, m.Y, m.Width, m.Height));
        yield return (new Choice("t1080", "Triple 1080p · 5760 × 1080 at -1920, 0"), (-1920, 0, 5760, 1080));
        yield return (new Choice("t1440", "Triple 1440p · 7680 × 1440 at -2560, 0"), (-2560, 0, 7680, 1440));
        yield return (new Choice("t4k", "Triple 4K · 11520 × 2160 at -3840, 0"), (-3840, 0, 11520, 2160));
        yield return (new Choice("current", "The program's window, where it is now"), null);
    }
}

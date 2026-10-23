using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.Theme;
using GoodieBag.Views;

namespace GoodieBag.Grr.Views;

internal static class GrrParts
{
    public const string Anchor = "grr.dashboard";

    public static string LongWhen(DateTime t) => LongDate(t) + " · " + Dates.Time(t);

    public static string LongDate(DateTime t) => Dates.ShortWithDay(t);

    public static string ShortWhen(DateTime t) => Dates.Weekday(t) + " " + Dates.Short(t) + " · " + Dates.Time(t);

    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago" : $"{(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")} ago";
    }

    public static string Points(double p) => p.ToString(p % 1 == 0 ? "#,0" : "#,0.#");

    public static string Series(GrrRace race, GrrLeagueData? data)
    {
        var text = GrrLeagues.Name(race.League);
        if (race.Round is { } round)
        {
            text += " · " + round;
            if (data?.Schedule is { Count: > 1 } s) text += $" of {s.Count}";
        }
        return text;
    }

    public static GrrRound? RoundOf(GrrRace race, GrrLeagueData? data) => data?.Schedule?.FirstOrDefault(r => r.Start == race.Start);

    public static TextBlock Eyebrow(string text, string? brush = null, double top = 0)
    {
        var t = TextFit.TipWhenTrimmed(new TextBlock { Text = text, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, top, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        if (brush is not null) t.Foreground = Ui.Res<Brush>(brush);
        return t;
    }

    public static TextBlock Caption(string text, double top = 0) =>
        TextFit.TipWhenTrimmed(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, top, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });

    public static TextBlock Wrapped(string text, double top = 0) =>
        new() { Text = text, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, top, 0, 0), TextWrapping = TextWrapping.Wrap };

    public static TextBlock Clock(double size, string brush = "Heading") => new()
    {
        FontFamily = Ui.Res<FontFamily>("MetricFont"), FontSize = size, FontWeight = size >= 20 ? FontWeights.Bold : FontWeights.SemiBold,
        Foreground = Ui.Res<Brush>(brush), Typography = { NumeralAlignment = FontNumeralAlignment.Tabular },
    };

    public static Border Tag(string text, bool live = false)
    {
        var tag = Ui.Chip(text, live ? "SageSoft" : "AccentSoft", live ? "SageDeep" : "AccentText");
        tag.Margin = new Thickness(0, 0, 8, 0);
        tag.VerticalAlignment = VerticalAlignment.Center;
        return tag;
    }

    public static Grid Hero(GrrRace race, GrrLeagueData? data, DateTime now, out TextBlock clock, double clockSize = 34)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new Border { Width = 3, Background = Ui.Res<Brush>("Accent"), Margin = new Thickness(0, 2, 14, 2), CornerRadius = ThemeManager.Round(1.5) });
        var body = new StackPanel();
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        bool live = race.Start <= now;
        var top = new DockPanel { LastChildFill = true };
        top.Children.Add(Tag(live ? "Racing now" : "Up next", live));
        top.Children.Add(Eyebrow(Series(race, data), "TextMuted"));
        ((FrameworkElement)top.Children[1]).VerticalAlignment = VerticalAlignment.Center;
        body.Children.Add(top);

        clock = Clock(clockSize, live ? "SageDeep" : "Heading");
        clock.Margin = new Thickness(0, 8, 0, 0);
        clock.Text = GrrLeagues.Countdown(GrrLeagues.Until(race.Start, now));
        body.Children.Add(clock);
        body.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = race.Track, Style = Ui.Res<Style>("H2"), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }));

        var when = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var whenText = Caption(LongWhen(race.Start));
        whenText.Margin = new Thickness(0, 0, 10, 0);
        whenText.VerticalAlignment = VerticalAlignment.Center;
        when.Children.Add(whenText);
        if (RoundOf(race, data) is { } round)
        {
            if (round.Format is { } f) when.Children.Add(Small(char.ToUpperInvariant(f[0]) + f[1..]));
            if (round.Laps is { } laps) when.Children.Add(Small(GoodieBag.SimRacing.Race.RaceFormat.Count(laps, "lap")));
        }
        body.Children.Add(when);
        return grid;
    }

    private static Border Small(string text)
    {
        var chip = Ui.Chip(text);
        chip.Margin = new Thickness(0, 0, 6, 0);
        return chip;
    }

    public static Button TimelineRow(string eyebrow, string track, string when, FrameworkElement? right, TimelineMark mark, bool first, bool last, Action? click, object? tip = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rail = new Grid();
        if (!(first && last))
        {
            var line = new Rectangle { Width = 1, Fill = Ui.Res<Brush>("LineStrong"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, first ? 12 : -6, 0, last ? 0 : -6) };
            if (last) { line.VerticalAlignment = VerticalAlignment.Top; line.Height = 18; }
            rail.Children.Add(line);
        }
        var dot = new Ellipse
        {
            Width = mark == TimelineMark.Next ? 11 : 9, Height = mark == TimelineMark.Next ? 11 : 9, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, mark == TimelineMark.Next ? 6 : 7, 0, 0), StrokeThickness = 2,
            Stroke = Ui.Res<Brush>(mark switch { TimelineMark.Next => "Accent", TimelineMark.Done => "TextFaint", _ => "LineStrong" }),
            Fill = Ui.Res<Brush>(mark switch { TimelineMark.Next => "Accent", TimelineMark.Done => "TextFaint", _ => "Surface" }),
        };
        rail.Children.Add(dot);
        grid.Children.Add(rail);

        var text = new StackPanel { Margin = new Thickness(4, 0, 10, 0) };
        text.Children.Add(Eyebrow(eyebrow, mark == TimelineMark.Next ? "AccentText" : null));
        text.Children.Add(TextFit.TipWhenTrimmed(new TextBlock
        {
            Text = track, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>(mark == TimelineMark.Done ? "TextMuted" : "Text"),
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
        }));
        text.Children.Add(Caption(when, 1));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
        }

        var button = new Button
        {
            Style = Ui.Res<Style>("QuietButton"), Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(4, 5, 8, 6), FontWeight = FontWeights.Normal, ToolTip = tip, IsHitTestVisible = click is not null,
            Focusable = click is not null,
        };
        if (click is not null) button.Click += (_, _) => click();
        return button;
    }

    public enum TimelineMark { Done, Next, Later }

    public static Button Compact(string icon, string label, Action click, string style = "Btn")
    {
        var b = Ui.LabeledButton(icon, label, click, style);
        b.Padding = new Thickness(9, 4, 10, 4);
        b.FontSize = 12;
        b.Margin = new Thickness(0, 0, 6, 6);
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    public static Button Link(string label, string url, string? tip = null, Action? before = null)
    {
        var b = Compact("I.External", label, () => { before?.Invoke(); Ui.OpenUrl(url); });
        b.ToolTip = tip ?? url;
        return b;
    }

    public static Button RefreshButton(AppServices app)
    {
        var icon = new Icon { Data = Ui.Res<Geometry>("I.Refresh"), Width = 15, Height = 15, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform() };
        var b = new Button { Style = Ui.Res<Style>("Btn"), Content = icon, Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(0, 0, 6, 6), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(b, "Refresh league data");
        ToolTipService.SetShowOnDisabled(b, true);
        b.Click += (_, _) => _ = app.Grr.RefreshNowAsync();
        SyncRefresh(b, app);
        return b;
    }

    public static void SyncRefresh(Button b, AppServices app)
    {
        var grr = app.Grr;
        b.IsEnabled = grr.CanRefresh;
        b.ToolTip = grr.Busy ? "Refreshing league data…" : grr.CanRefresh ? "Refresh league data" : "Refresh league data (just refreshed: again in a few seconds)";
        if (b.Content is not Icon { RenderTransform: RotateTransform turn }) return;
        bool spin = grr.Busy && Motion.Enabled;
        if (spin == (b.Tag is true)) return;
        b.Tag = spin;
        turn.BeginAnimation(RotateTransform.AngleProperty, spin
            ? new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever }
            : null);
    }

    public static Border Badge(string text, string family, string tip)
    {
        var badge = new Tile
        {
            Background = Ui.Res<Brush>(family + "Soft"), BorderBrush = Ui.Res<Brush>(family + "Deep"), BorderThickness = new Thickness(1),
            CornerRadius = ThemeManager.Round(4), Chamfer = true, Padding = new Thickness(7, 1, 7, 2), MinHeight = 21, MinWidth = 30,
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip,
            Child = new TextBlock
            {
                Text = text, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Ui.Res<Brush>(family + "Deep"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        System.Windows.Automation.AutomationProperties.SetName(badge, tip);
        return badge;
    }

    public static FrameworkElement Position(int p, bool mine, double size = 13, bool prefix = false, string? plain = null)
    {
        var text = Clock(size, p is >= 1 and <= 3 ? $"Podium{p}Fg" : plain ?? (mine ? "AccentText" : "TextFaint"));
        text.Text = prefix ? $"P{p}" : p.ToString();
        if (p is < 1 or > 3) return text;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        string tip = p switch { 1 => "1st: gold", 2 => "2nd: silver", _ => "3rd: bronze" };
        double k = size / 13;
        var pill = new Tile
        {
            MinWidth = 26 * k, Padding = new Thickness(5 * k, 1 * k, 5 * k, 2 * k), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = ThemeManager.Round(4 * Math.Min(k, 1.5)), Chamfer = true, BorderThickness = new Thickness(1.5),
            Background = Ui.Res<Brush>($"Podium{p}Bg"), BorderBrush = Ui.Res<Brush>($"Podium{p}Edge"), ToolTip = tip, Child = text,
        };
        System.Windows.Automation.AutomationProperties.SetName(pill, $"Position {p}, {tip}");
        return pill;
    }

    public static string FastestLapLabel(string? cls) => string.IsNullOrEmpty(cls) ? "FL" : $"FL - {cls}";

    public static object? RoundTip(IReadOnlyList<GrrClassResult> results, string? action)
    {
        if (results.Count == 0 && action is null) return null;
        var stack = new StackPanel { MaxWidth = 320 };
        TextBlock Line(string label, string? who, double top = 0)
        {
            var t = new TextBlock { Margin = new Thickness(0, top, 0, 0), TextWrapping = TextWrapping.Wrap };
            t.Inlines.Add(new System.Windows.Documents.Run(label + "  ") { Foreground = Ui.Res<Brush>("TextMuted") });
            t.Inlines.Add(new System.Windows.Documents.Run(who ?? "not posted") { FontWeight = FontWeights.SemiBold });
            return t;
        }
        foreach (var r in results)
        {
            if (r.Class.Length > 0) stack.Children.Add(new TextBlock { Text = r.Class, FontWeight = FontWeights.Bold, Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : 8, 0, 1) });
            stack.Children.Add(Line("Winner", r.Winner));
            stack.Children.Add(Line(FastestLapLabel(r.Class), r.FastestLap, 1));
        }
        if (action is not null) stack.Children.Add(new TextBlock { Text = action, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : 8, 0, 0) });
        return stack;
    }

    public static StackPanel Placeholder(params double[] widths)
    {
        var stack = new StackPanel();
        double[] heights = { 12, 30, 16, 12, 12, 12 };
        for (int i = 0; i < widths.Length; i++)
            stack.Children.Add(new Border
            {
                Height = heights[Math.Min(i, heights.Length - 1)], Width = widths[i], HorizontalAlignment = HorizontalAlignment.Left,
                Background = Ui.Res<Brush>("SurfaceAlt"), CornerRadius = ThemeManager.Round(3), Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0),
            });
        return stack;
    }

    public static StackPanel Problem(AppServices app, string text, string? detail)
    {
        var stack = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.Alert"), Width = 15, Height = 15, Foreground = Ui.Res<Brush>("WarnDeep"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Body"), VerticalAlignment = VerticalAlignment.Center });
        if (detail is not null) row.ToolTip = detail;
        stack.Children.Add(row);
        var retry = Compact("I.Refresh", "Retry", () => _ = app.Grr.RefreshNowAsync());
        retry.HorizontalAlignment = HorizontalAlignment.Left;
        retry.Margin = new Thickness(0, 10, 0, 0);
        retry.IsEnabled = app.Grr.CanRefresh;
        stack.Children.Add(retry);
        return stack;
    }

    public static void OpenDriverHistory(string? driver)
    {
        if (driver is not null) try { Clipboard.SetText(driver); } catch { }
        Ui.OpenUrl(GrrLeagues.DriverUrl);
    }

    public static string DriverHistoryTip(string? driver) => driver is null
        ? "Driver history on the GRR website"
        : $"Driver history on the GRR website; your name ({driver}) is copied, ready to paste in its search";

    public static async Task EditAsync(AppServices app, MainWindow window)
    {
        var s = app.Config.Grr;
        var picked = s.Leagues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Follow", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 4, 0, 6) });
        foreach (var league in GrrLeagues.All)
        {
            var id = league.Id;
            var box = new CheckBox { Content = league.Name, IsChecked = picked.Contains(id), Margin = new Thickness(0, 2, 0, 4) };
            box.Click += (_, _) => { if (box.IsChecked == true) picked.Add(id); else picked.Remove(id); };
            stack.Children.Add(box);
        }
        var name = new TextBox { Text = s.DriverName ?? "", Tag = "As it appears on the GRR website", Margin = new Thickness(0, 4, 0, 0) };
        stack.Children.Add(new TextBlock { Text = "Your driver name", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 14, 0, 0) });
        stack.Children.Add(name);
        stack.Children.Add(new TextBlock { Text = "Highlights you in the standings and results and shows your championship.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 6, 0, 0) });
        var leagueIds = new TextBox { Text = string.Join(", ", s.IracingLeagueIds), Tag = "e.g. 1234", Margin = new Thickness(0, 4, 0, 0) };
        stack.Children.Add(new TextBlock { Text = "iRacing league ID", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 14, 0, 0) });
        stack.Children.Add(leagueIds);
        stack.Children.Add(new TextBlock { Text = "The number in the league's iRacing page address. Session Stats counts those sessions as league races.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        var card = Ui.Dialog(window, "GRR League", "The leagues you race in and your name on the GRR website. Race times, standings and results come from the GRR website.",
            stack, Ui.DialogButtons(window, "Save", () =>
            {
                s.Leagues = s.Leagues.Where(picked.Contains).Concat(GrrLeagues.All.Select(l => l.Id).Where(id => picked.Contains(id) && !s.Leagues.Contains(id, StringComparer.OrdinalIgnoreCase))).ToList();
                s.DriverName = string.IsNullOrWhiteSpace(name.Text) ? null : name.Text.Trim();
                s.IracingLeagueIds = GrrSettings.ParseIds(leagueIds.Text);
                return true;
            }), width: 460);
        if (!await window.ShowModal(card)) return;
        app.Save();
        app.Grr.SettingsChanged();
    }
}

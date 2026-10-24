using GoodieBag.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.SessionStats;

namespace GoodieBag.Views.Pages;

internal sealed class SessionFilter
{
    public int Type;
    public string? Car, Track;
    public int Shown = 10;

    private static readonly string?[] Types = { null, "Practice", "Qualify", "Race" };

    public IEnumerable<SessionSummary> Apply(IEnumerable<SessionSummary> sessions) => sessions.Where(s =>
        (Types[Type] is not { } type || s.Type == type) && (Car is null || s.Car == Car) && (Track is null || s.Place == Track));
}

internal static class StatsSessions
{
    public static Tile Card(IReadOnlyList<SessionSummary> sessions, IReadOnlySet<long> traced, SessionFilter filter, Config.SessionStatsSettings traceSettings,
        Action changed, Action<SessionSummary> open, FrameworkElement settings)
    {
        var tools = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        void Tool(FrameworkElement element) { element.Margin = new Thickness(8, 2, 0, 2); element.VerticalAlignment = VerticalAlignment.Center; tools.Children.Add(element); }
        Tool(Ui.Segmented(new[] { "All", "Practice", "Qualifying", "Race" }, filter.Type, i => { filter.Type = i; filter.Shown = 10; changed(); }, "statsSessionType"));
        Tool(Pick("All cars", sessions.Select(s => s.Car), filter.Car, v => { filter.Car = v; filter.Shown = 10; changed(); }));
        Tool(Pick("All tracks", sessions.Select(s => s.Place), filter.Track, v => { filter.Track = v; filter.Shown = 10; changed(); }));
        Tool(settings);

        var list = filter.Apply(sessions).ToList();
        var body = new StackPanel();
        if (list.Count == 0) body.Children.Add(StatsSections.Empty("No sessions match in this range."));
        else
        {
            var table = new Grid();
            foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto })
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = w, MinWidth = w.IsStar ? 120 : 0 });
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] heads = { "Time", "Session", "Car and track", "Laps", "Best lap", "Inc", "Result", "" };
            for (int c = 0; c < heads.Length; c++)
            {
                Cell(table, 0, c, new TextBlock { Text = heads[c], Style = Ui.Res<Style>("Eyebrow") }, c is >= 3 and <= 6);
                if (c > 0 && heads[c].Length > 0) ColumnBar(table, 0, c, 16);
            }
            int row = 0;
            var now = DateTime.UtcNow;
            DateTime? day = null;
            foreach (var s in list.Take(filter.Shown))
            {
                var local = s.StartUtc.ToLocalTime();
                if (day != local.Date)
                {
                    day = local.Date;
                    row++;
                    table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var dayText = new TextBlock { Text = Dates.Weekday(local) + " " + Dates.Short(local), Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, row == 1 ? 6 : 16, 0, 4) };
                    dayText.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
                    Grid.SetRow(dayText, row);
                    Grid.SetColumnSpan(dayText, heads.Length);
                    table.Children.Add(dayText);
                }
                row++;
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var hit = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Open this session" };
                Grid.SetRow(hit, row);
                Grid.SetColumnSpan(hit, heads.Length);
                hit.MouseEnter += (_, _) => hit.SetResourceReference(Border.BackgroundProperty, "Hover");
                hit.MouseLeave += (_, _) => hit.Background = Brushes.Transparent;
                var session = s;
                hit.MouseLeftButtonUp += (_, e) => { e.Handled = true; open(session); };
                var rule = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
                rule.SetResourceReference(Border.BackgroundProperty, "Line");
                Grid.SetRow(rule, row);
                Grid.SetColumnSpan(rule, heads.Length);
                table.Children.Add(hit);
                table.Children.Add(rule);
                Cell(table, row, 0, Caption(Dates.Time(local)));
                Cell(table, row, 1, TypeMark(s.Type));
                var names = new StackPanel { Children = { Body(s.Car), Body(s.Place, "TextMuted", 12) } };
                Cell(table, row, 2, CarPictures.Beside(names, s.Car, 26, 10));
                Cell(table, row, 3, Caption(s.Laps.ToString(CultureInfo.CurrentCulture)), true);
                Cell(table, row, 4, Time(s.Best), true);
                Cell(table, row, 5, Caption(s.Incidents is { } inc ? inc + "x" : "–"), true);
                Cell(table, row, 6, Caption(Result(s)), true);
                bool hasTrace = traced.Contains(s.Id);
                var mark = new Icon
                {
                    Data = Ui.Res<Geometry>("I.Activity"), Width = 14, Height = 14,
                    ToolTip = hasTrace ? "Lap traces kept: lap analysis available" : "Times only: no lap analysis. " + NoTraceReason(s, traceSettings, now),
                };
                mark.SetResourceReference(Control.ForegroundProperty, hasTrace ? "AccentText" : "TextFaint");
                if (!hasTrace) mark.Opacity = 0.55;
                Cell(table, row, 7, mark);
            }
            body.Children.Add(table);
            if (list.Count > filter.Shown)
            {
                var more = Ui.LabeledButton("I.ChevronDown", $"Show more ({list.Count - filter.Shown} left)", () => { filter.Shown += 20; changed(); });
                more.Margin = new Thickness(0, 12, 0, 0);
                more.HorizontalAlignment = HorizontalAlignment.Left;
                body.Children.Add(more);
            }
        }
        var card = StatsSections.Section("Recent sessions", "I.Calendar", $"{list.Count:N0} session{(list.Count == 1 ? "" : "s")}", tools, body);
        Ui.Anchor(card, "stats.sessions");
        return card;
    }

    internal static string NoTraceReason(SessionSummary s, Config.SessionStatsSettings settings, DateTime nowUtc)
    {
        if (!settings.TraceRecord)
            return "Lap traces are turned off (the gear on Recent sessions), so this session's line and inputs weren't kept.";
        if (settings.TraceKeepDays > 0 && s.StartUtc < nowUtc.AddDays(-settings.TraceKeepDays))
            return $"Older than lap traces are kept for ({TraceSettings.DaysName(settings.TraceKeepDays)}, the gear on Recent sessions), so its line and inputs were let go.";
        return "Recorded before Goodie Bag kept lap traces, and iRacing's telemetry file (.ibt) for it wasn't found, so there's no line or inputs to analyze.";
    }

    internal static string TypeName(string type) => type switch { "Qualify" => "Qualifying", "" => "Other", _ => type };

    internal static string TypeBrush(string type) => type switch { "Practice" => "Slate", "Qualify" => "Gold", "Race" => "Accent", _ => "LineStrong" };

    internal static FrameworkElement TypeMark(string type)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var swatch = new Border { Width = 8, Height = 8, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        swatch.SetResourceReference(Border.BackgroundProperty, TypeBrush(type));
        row.Children.Add(swatch);
        row.Children.Add(new TextBlock { Text = TypeName(type), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static string Result(SessionSummary s)
    {
        if (s.Type != "Race" || s.FinishPos is not { } finish) return "–";
        return s.StartPos is { } start ? $"P{start} → P{finish}" : "P" + finish;
    }

    private static ComboBox Pick(string all, IEnumerable<string> values, string? selected, Action<string?> changed)
    {
        var combo = new SearchCombo { MinWidth = 170, MaxWidth = 280 };
        combo.Items.Add(new Choice(null, all));
        var list = values.Where(v => v.Length > 0).Distinct().ToList();
        if (selected is { Length: > 0 } && !list.Contains(selected)) list.Add(selected);
        foreach (var v in list.OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)) combo.Items.Add(new Choice(v, v));
        combo.SelectedItem = combo.Items.Cast<Choice>().FirstOrDefault(c => c.Id == selected) ?? combo.Items[0];
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice c && c.Id != selected) changed(c.Id); };
        return combo;
    }

    private static void Cell(Grid table, int row, int col, FrameworkElement element, bool right = false)
    {
        element.Margin = new Thickness(col == 0 ? 8 : 16, 7, col == 7 ? 8 : 0, 7);
        element.VerticalAlignment = VerticalAlignment.Center;
        element.IsHitTestVisible = element.ToolTip is not null;
        if (right) element.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        table.Children.Add(element);
    }

    internal static void ColumnBar(Grid table, int row, int col, double gap)
    {
        var bar = new Border { Width = 2, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(gap / 2 - 1, 0, 0, 0), IsHitTestVisible = false };
        bar.SetResourceReference(Border.BackgroundProperty, "LineStrong");
        Grid.SetRow(bar, row);
        Grid.SetColumn(bar, col);
        table.Children.Add(bar);
    }

    private static TextBlock Caption(string text) => new() { Text = text, Style = Ui.Res<Style>("Caption") };

    private static TextBlock Body(string text, string? brush = null, double size = 13)
    {
        var t = TextFit.TipWhenTrimmed(new TextBlock { Text = text, Style = Ui.Res<Style>("Body"), FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis });
        if (brush is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    internal static TextBlock Time(double? seconds, string brush = "Heading")
    {
        var t = new TextBlock { Text = seconds is { } s ? StatFormat.LapTime(s) : "–", FontWeight = FontWeights.SemiBold, FontSize = 13 };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "MetricFont");
        t.SetResourceReference(TextBlock.ForegroundProperty, seconds is null ? "TextFaint" : brush);
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }
}

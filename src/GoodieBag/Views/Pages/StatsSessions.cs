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
        Tool(Ui.Segmented(new[] { "All", "Practice", "Qualify", "Race" }, filter.Type, i => { filter.Type = i; filter.Shown = 10; changed(); }, "statsSessionType"));
        Tool(Pick("All cars", sessions.Select(s => s.Car), filter.Car, v => { filter.Car = v; filter.Shown = 10; changed(); }));
        Tool(Pick("All tracks", sessions.Select(s => s.Place), filter.Track, v => { filter.Track = v; filter.Shown = 10; changed(); }));
        Tool(settings);

        var list = filter.Apply(sessions).ToList();
        var body = new StackPanel();
        if (list.Count == 0) body.Children.Add(StatsSections.Empty("No sessions match in this range."));
        else
        {
            var table = new Grid();
            foreach (var w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1.2, GridUnitType.Star), new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto })
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = w, MinWidth = w.IsStar ? 80 : 0 });
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] heads = { "Date", "Session", "Car", "Track", "Laps", "Best lap", "Inc", "Result", "" };
            for (int c = 0; c < heads.Length; c++)
            {
                Cell(table, 0, c, new TextBlock { Text = heads[c], Style = Ui.Res<Style>("Eyebrow") }, c is >= 4 and <= 7);
                if (c > 0 && heads[c].Length > 0) ColumnBar(table, 0, c, 16);
            }
            int row = 0;
            bool timesOnly = false;
            var now = DateTime.UtcNow;
            foreach (var s in list.Take(filter.Shown))
            {
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
                var local = s.StartUtc.ToLocalTime();
                Cell(table, row, 0, Caption(Dates.ShortTime(local)));
                Cell(table, row, 1, Caption(TypeName(s.Type)));
                Cell(table, row, 2, Body(s.Car));
                Cell(table, row, 3, Body(s.Place, "TextMuted"));
                Cell(table, row, 4, Caption(s.Laps.ToString(CultureInfo.CurrentCulture)), true);
                Cell(table, row, 5, Time(s.Best), true);
                Cell(table, row, 6, Caption(s.Incidents is { } inc ? inc + "x" : "–"), true);
                Cell(table, row, 7, Caption(s.Type == "Race" && s.FinishPos is { } p ? "P" + p : "–"), true);
                if (traced.Contains(s.Id))
                {
                    var mark = new Icon { Data = Ui.Res<Geometry>("I.Activity"), Width = 14, Height = 14, ToolTip = "Lap traces kept: lap analysis available" };
                    mark.SetResourceReference(Control.ForegroundProperty, "AccentText");
                    Cell(table, row, 8, mark);
                }
                else
                {
                    timesOnly = true;
                    var label = Caption("Times only");
                    label.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
                    label.ToolTip = NoTraceReason(s, traceSettings, now);
                    Cell(table, row, 8, label);
                }
            }
            body.Children.Add(table);
            if (timesOnly)
            {
                var why = Ui.Text("Times only = no lap traces, so no lap analysis: usually a session from before Goodie Bag kept them. Hover it for why. Its lap times and stats still count.", "Caption");
                why.TextWrapping = TextWrapping.Wrap;
                var note = Ui.IconText("I.Info", why, "TextMuted");
                note.Margin = new Thickness(8, 12, 0, 0);
                body.Children.Add(note);
            }
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
        element.Margin = new Thickness(col == 0 ? 8 : 16, 8, col == 8 ? 8 : 0, 8);
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
            Margin = new Thickness(gap / 2 - 1, 0, 0, 0), Opacity = 0.55, IsHitTestVisible = false };
        bar.SetResourceReference(Border.BackgroundProperty, "Accent");
        Grid.SetRow(bar, row);
        Grid.SetColumn(bar, col);
        table.Children.Add(bar);
    }

    private static TextBlock Caption(string text) => new() { Text = text, Style = Ui.Res<Style>("Caption") };

    private static TextBlock Body(string text, string? brush = null)
    {
        var t = new TextBlock { Text = text, Style = Ui.Res<Style>("Body"), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
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

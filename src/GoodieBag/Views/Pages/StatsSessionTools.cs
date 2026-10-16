using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.SessionStats;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal static class StatsSessionTools
{
    public static Tile? Sectors(IReadOnlyList<LapRow> laps)
    {
        var lines = SectorReport.Make(laps);
        if (lines.Count == 0) return null;
        int? worst = SectorReport.MostToFind(lines);
        double maxLost = Math.Max(0.001, lines.Max(l => l.Lost));
        var table = new Grid();
        string[] heads = { "", "Best", "Lap", "Average", "Spread", "Left on the table", "" };
        foreach (var _ in heads) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions[^1].Width = new GridLength(1, GridUnitType.Star);
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < heads.Length; c++) Cell(table, 0, c, new TextBlock { Text = heads[c], Style = Ui.Res<Style>("Eyebrow") }, c is > 0 and < 6);
        var (coralSoft, coralDeep) = StatsUi.Hue("Coral");
        foreach (var line in lines)
        {
            int row = line.Index + 1;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool focus = worst == line.Index;
            var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
            rule.SetResourceReference(Border.BorderBrushProperty, "Line");
            if (focus) rule.SetResourceReference(Border.BackgroundProperty, coralSoft);
            Grid.SetRow(rule, row);
            Grid.SetColumnSpan(rule, heads.Length);
            table.Children.Add(rule);
            Cell(table, row, 0, new TextBlock { Text = $"S{line.Index + 1}", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold });
            var best = Number(line.Best.ToString("0.000", CultureInfo.CurrentCulture));
            best.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            Cell(table, row, 1, best, true);
            Cell(table, row, 2, Number(line.BestLap.ToString(CultureInfo.CurrentCulture), "Caption"), true);
            Cell(table, row, 3, Number(line.Average.ToString("0.000", CultureInfo.CurrentCulture)), true);
            Cell(table, row, 4, Number("±" + line.Spread.ToString("0.000", CultureInfo.CurrentCulture), "Caption"), true);
            var lost = Number(LapReplayView.Signed(line.Lost, "0.000"));
            if (focus) lost.SetResourceReference(TextBlock.ForegroundProperty, coralDeep);
            Cell(table, row, 5, lost, true);
            var bar = new Border { Height = 6, Width = 120 * line.Lost / maxLost, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 2 };
            bar.SetResourceReference(Border.BackgroundProperty, focus ? coralDeep : "LineStrong");
            Cell(table, row, 6, bar);
        }
        int totalRow = lines.Count + 1;
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var totalRule = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
        totalRule.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        Grid.SetRow(totalRule, totalRow);
        Grid.SetColumnSpan(totalRule, heads.Length);
        table.Children.Add(totalRule);
        Cell(table, totalRow, 0, new TextBlock { Text = "Lap", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold });
        var optimal = Number(StatFormat.LapTime(lines.Sum(l => l.Best)));
        optimal.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        optimal.ToolTip = "Optimal: your best sectors put together";
        Cell(table, totalRow, 1, optimal, true);
        Cell(table, totalRow, 3, Number(StatFormat.LapTime(lines.Sum(l => l.Average))), true);
        Cell(table, totalRow, 5, Number(LapReplayView.Signed(lines.Sum(l => l.Lost), "0.000")), true);

        string note = worst is { } w
            ? $"Most time to find in S{w + 1}: {lines[w].Lost.ToString("0.000", CultureInfo.CurrentCulture)} s a lap on average against your best there"
            : "Clean laps with sector times";
        var body = new StackPanel();
        var hint = new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        body.Children.Add(hint);
        body.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = table });
        var card = StatsSections.Section("Sectors", "I.Flag", $"{lines[0].Laps} clean laps", null, body);
        card.ToolTip = "Spread = how much the sector varies lap to lap; left on the table = the average less your best";
        return card;
    }

    public static FrameworkElement TyreRun(IReadOnlyList<TyreReading> readings)
    {
        var body = new StackPanel { ToolTip = "Measured by iRacing when the car stops in its pit stall: tread left on each tire (left / middle / right)" };
        var (sageSoft, sageDeep) = StatsUi.Hue("Sage");
        var (goldSoft, goldDeep) = StatsUi.Hue("Gold");
        var (coralSoft, coralDeep) = StatsUi.Hue("Coral");
        foreach (var r in readings)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, body.Children.Count > 0 ? 10 : 0, 0, 8) };
            var glyph = new Icon { Data = Ui.Res<Geometry>("I.Wheel"), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            glyph.SetResourceReference(Control.ForegroundProperty, "AccentText");
            head.Children.Add(glyph);
            head.Children.Add(new TextBlock
            {
                Text = $"Tires after lap {r.Lap}" + (r.Laps > 0 ? $" · {r.Laps} lap{(r.Laps == 1 ? "" : "s")} on this set" : ""),
                Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            });
            body.Children.Add(head);
            var grid = new UniformGrid { Columns = 2 };
            for (int c = 0; c < 4; c++)
            {
                double low = r.Lowest(c);
                var (soft, deep) = low >= 0.9 ? (sageSoft, sageDeep) : low >= 0.75 ? (goldSoft, goldDeep) : (coralSoft, coralDeep);
                var tile = new Tile { CornerRadius = ThemeManager.Round(4), Padding = new Thickness(14, 12, 14, 12), BorderThickness = new Thickness(1) };
                tile.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
                tile.SetResourceReference(Border.BorderBrushProperty, "Line");
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = TyreReading.Corners[c], Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                var big = new TextBlock { Text = $"{low * 100:0.0}%", Style = Ui.Res<Style>("Body"), FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
                big.SetResourceReference(TextBlock.ForegroundProperty, deep);
                big.ToolTip = "Tread left at its most worn point";
                stack.Children.Add(big);
                var zones = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
                string[] names = { "Left", "Middle", "Right" };
                for (int z = 0; z < 3; z++)
                {
                    double left = r.Left(c, z);
                    var fill = new Border { Height = 5, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                    fill.SetResourceReference(Border.BackgroundProperty, deep);
                    var track = new Border { Height = 5, Child = fill, Margin = new Thickness(0, 4, 0, 0) };
                    track.SetResourceReference(Border.BackgroundProperty, soft);
                    track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * Math.Clamp(left, 0, 1);
                    var cell = new StackPanel { ToolTip = $"{names[z]}: {left * 100:0.0}% left", Margin = new Thickness(0, 0, z < 2 ? 10 : 0, 0) };
                    cell.Children.Add(Number($"{left * 100:0.0}", "Caption"));
                    cell.Children.Add(track);
                    zones.Children.Add(cell);
                }
                stack.Children.Add(zones);
                if (r.UsedPerLap(c) is { } perLap)
                    stack.Children.Add(new TextBlock { Text = $"{perLap * 100:0.00}% a lap", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
                tile.Child = stack;
                grid.Children.Add(tile);
            }
            for (int i = 0; i < grid.Children.Count; i++)
                ((FrameworkElement)grid.Children[i]).Margin = new Thickness(0, 0, i % 2 == 0 ? 8 : 0, i < 2 ? 8 : 0);
            body.Children.Add(grid);
        }
        return body;
    }

    private static TextBlock Number(string text, string style = "Body")
    {
        var t = new TextBlock { Text = text, Style = Ui.Res<Style>(style) };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    private static void Cell(Grid table, int row, int col, FrameworkElement element, bool right = false)
    {
        element.Margin = new Thickness(col == 0 ? 8 : 20, 7, 0, 7);
        element.VerticalAlignment = VerticalAlignment.Center;
        if (right) element.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        table.Children.Add(element);
    }
}

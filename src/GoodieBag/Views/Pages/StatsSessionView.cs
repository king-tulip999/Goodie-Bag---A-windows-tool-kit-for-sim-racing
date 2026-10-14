using GoodieBag.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.SessionStats;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed record SessionDetail(SessionSummary Session, List<LapRow> Laps, HashSet<int> Traced, double? AllTimeBest, SessionConditions? Conditions = null);

internal static class StatsSessionView
{
    public const int MaxLaps = 4;

    public static FrameworkElement Build(SessionDetail d, bool imperial, string noTraceReason, HashSet<int> ticked, Action<IReadOnlyList<int>> analyze)
    {
        var s = d.Session;
        var page = new StackPanel();

        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = s.Car, Style = Ui.Res<Style>("H2"), TextWrapping = TextWrapping.Wrap });
        head.Children.Add(new TextBlock { Text = s.Place, Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var facts = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        FrameworkElement Fact(string label, string value, string? brush = null)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 28, 8) };
            var v = new TextBlock { Text = value, Style = Ui.Res<Style>("BigValue"), FontSize = 20 };
            if (brush is not null) v.SetResourceReference(TextBlock.ForegroundProperty, brush);
            cell.Children.Add(v);
            cell.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption") });
            facts.Children.Add(cell);
            return cell;
        }
        var laps = d.Laps;
        var cleanLaps = laps.Where(l => l.Clean).ToList();
        int sectors = laps.Max(l => (int?)(l.Sectors?.Count ?? 0)) ?? 0;
        var bestSector = Enumerable.Range(0, sectors).Select(k => laps.Where(l => l.Clean && l.Sectors?.Count == sectors).Select(l => (double?)l.Sectors![k]).Min()).ToArray();
        var local = s.StartUtc.ToLocalTime();
        Fact(StatsSessions.TypeName(s.Type), Dates.DateTime(local));
        if (d.Conditions is { SimStart.Length: > 0 } cond && SimTime(cond.SimStart) is { } simTime) Fact("In-sim", simTime);
        Fact("Laps", laps.Count == 0 ? s.Laps.ToString(CultureInfo.CurrentCulture)
            : $"{laps.Count} ({cleanLaps.Count} clean, {100.0 * cleanLaps.Count / laps.Count:0}%)");
        Fact("Fastest clean lap", s.Best is { } best ? StatFormat.LapTime(best) : "–", "AccentText");
        if (sectors > 0 && bestSector.All(b => b is not null))
        {
            double sum = bestSector.Sum(b => b!.Value);
            if (s.Best is { } fastest && fastest < sum) sum = fastest;
            var optimal = Fact("Optimal lap", StatFormat.LapTime(sum));
            optimal.ToolTip = "Your best clean sectors of the session put together";
        }
        if (cleanLaps.Count > 0) Fact("Average clean lap", StatFormat.LapTime(cleanLaps.Average(l => l.Time)));
        if (cleanLaps.Where(l => l.FuelUsed is not null).Select(l => l.FuelUsed!.Value).ToList() is { Count: > 0 } burned)
            Fact("Fuel per clean lap", StatFormat.Value(StatKind.Fuel, burned.Average(), imperial));
        Fact("Driving", StatFormat.Duration(s.DriveS));
        if (s.Type == "Race")
        {
            if (s.FinishPos is { } fin) Fact(s.StartPos is { } st ? $"Finish (started P{st})" : "Finish", "P" + fin);
            if (s.IrDelta is { } ir) Fact("iRating", StatFormat.Value(StatKind.Signed, ir, false), ir > 0 ? StatsUi.Hue("Sage").Deep : ir < 0 ? StatsUi.Hue("Coral").Deep : null);
            if (s.SrDelta is { } sr) Fact("Safety rating", StatFormat.Value(StatKind.Rating, sr, false));
        }
        if (s.Incidents is { } inc) Fact("Incidents", inc + "x");
        head.Children.Add(facts);
        if (ConditionsStrip(s, d.Conditions, imperial) is { } strip) head.Children.Add(strip);
        page.Children.Add(Card(head));

        if (laps.Count == 0)
        {
            page.Children.Add(Card(StatsSections.Empty("No complete laps in this session.")));
            return page;
        }

        var chartCard = Card(StatsSections.Chart("Lap times", "Every lap in order; hollow = not clean (an incident, the pit lane or off track)", new LapTimeChart(laps) { Height = 220 }));

        double? sessionBest = cleanLaps.Select(l => (double?)l.Time).Min();
        var bestSoFar = new double?[sectors];
        if (ticked.Count == 0 && laps.Where(l => d.Traced.Contains(l.Lap)).OrderByDescending(l => l.Clean).ThenBy(l => l.Time).FirstOrDefault() is { } first) ticked.Add(first.Lap);

        var analyzeButton = Ui.LabeledButton("I.Activity", "Analyze", () => analyze(ticked.OrderBy(x => x).ToList()), "PrimaryButton");
        void Sync() => analyzeButton.IsEnabled = ticked.Count is > 0 and <= MaxLaps;
        Sync();
        analyzeButton.ToolTip = d.Traced.Count == 0 ? noTraceReason : $"Compare up to {MaxLaps} ticked laps: line, inputs and time gained or lost";

        var table = new Grid();
        var cols = new List<(string Head, bool Right)> { ("", false), ("Lap", true), ("Time", true), ("Δ best", true) };
        for (int k = 0; k < sectors; k++) cols.Add(($"S{k + 1}", true));
        cols.Add(("Fuel", true));
        cols.Add(("Top speed", true));
        cols.Add(("", false));
        foreach (var _ in cols) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions[^1].Width = new GridLength(1, GridUnitType.Star);
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < cols.Count; c++) Cell(table, 0, c, new TextBlock { Text = cols[c].Head, Style = Ui.Res<Style>("Eyebrow") }, cols[c].Right);
        var (pbSoft, pbDeep) = StatsUi.Hue("Gold");
        var (sageSoft, sageDeep) = StatsUi.Hue("Sage");
        var (coralSoft, coralDeep) = StatsUi.Hue("Coral");
        int row = 0;
        int? shownRun = null;
        bool runs = laps.Select(l => l.Run ?? 1).Distinct().Count() > 1 || laps.Any(l => l.Setup is not null);
        foreach (var lap in laps)
        {
            if (runs && (lap.Run ?? 1) != shownRun)
            {
                shownRun = lap.Run ?? 1;
                row++;
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var inRun = laps.Where(l => (l.Run ?? 1) == shownRun).ToList();
                var parts = new List<string> { $"Run {shownRun}", $"{inRun.Count} lap{(inRun.Count == 1 ? "" : "s")}" };
                if (inRun.Where(l => l.Clean).ToList() is { Count: > 0 } clean) parts.Add($"average clean {StatFormat.LapTime(clean.Average(l => l.Time))}");
                var header = new DockPanel { Margin = new Thickness(8, row == 1 ? 10 : 18, 0, 6) };
                var setup = inRun.Select(l => l.Setup).LastOrDefault(x => x is not null);
                if (setup is not null)
                {
                    var file = new TextBlock { Text = setup, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), ToolTip = "The setup loaded" };
                    TextCase.Verbatim(file);
                    DockPanel.SetDock(file, Dock.Right);
                    header.Children.Add(file);
                }
                header.Children.Add(new TextBlock { Text = string.Join(" · ", parts), Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center });
                var runBand = new Tile { Child = header, CornerRadius = ThemeManager.Round(4) };
                runBand.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
                runBand.Margin = new Thickness(0, row == 1 ? 6 : 14, 0, 2);
                header.Margin = new Thickness(10, 7, 10, 7);
                Grid.SetRow(runBand, row);
                Grid.SetColumnSpan(runBand, cols.Count);
                table.Children.Add(runBand);
            }
            row++;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool isBest = lap.Clean && lap.Time == sessionBest;
            bool isPb = lap.Clean && d.AllTimeBest is { } pb && Math.Abs(lap.Time - pb) < 0.0005;
            var band = new Border();
            if (isPb) band.SetResourceReference(Border.BackgroundProperty, pbSoft);
            else if (isBest) band.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            var rule = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = band };
            rule.SetResourceReference(Border.BorderBrushProperty, "Line");
            Grid.SetRow(rule, row);
            Grid.SetColumnSpan(rule, cols.Count);
            table.Children.Add(rule);

            int c = 0;
            bool traced = d.Traced.Contains(lap.Lap);
            var tick = new CheckBox { IsChecked = ticked.Contains(lap.Lap), IsEnabled = traced, VerticalAlignment = VerticalAlignment.Center };
            var tickHost = new Border { Child = tick, Background = Brushes.Transparent, ToolTip = traced ? "Tick to analyze (up to 4)" : "No trace for this lap (not in a telemetry file, or not kept)" };
            int number = lap.Lap;
            tick.Click += (_, _) =>
            {
                if (tick.IsChecked == true)
                {
                    if (ticked.Count >= MaxLaps) { tick.IsChecked = false; return; }
                    ticked.Add(number);
                }
                else ticked.Remove(number);
                Sync();
            };
            Cell(table, row, c++, tickHost);
            Cell(table, row, c++, Caption(lap.Lap.ToString(CultureInfo.CurrentCulture)), true);
            var time = StatsSessions.Time(lap.Time, isPb ? pbDeep : isBest ? "AccentText" : lap.Clean ? "Heading" : "TextMuted");
            if (isPb) time.ToolTip = "Your all-time best in this car at this track";
            Cell(table, row, c++, time, true);
            Cell(table, row, c++, Caption(sessionBest is { } sb && !isBest ? StatFormat.Value(StatKind.LapDelta, lap.Time - sb, false) : "–"), true);
            for (int k = 0; k < sectors; k++)
            {
                double? sector = lap.Sectors is { } list && list.Count == sectors ? list[k] : null;
                var text = Caption(sector is { } v ? v.ToString("0.000", CultureInfo.CurrentCulture) : "–");
                System.Windows.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
                if (sector is { } sv && lap.Clean)
                {
                    if (bestSector[k] is { } bk && Math.Abs(sv - bk) < 0.0005)
                    {
                        text.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                        text.FontWeight = FontWeights.SemiBold;
                        text.ToolTip = "Best sector of the session";
                    }
                    else if (bestSoFar[k] is not { } before || sv < before)
                    {
                        text.SetResourceReference(TextBlock.ForegroundProperty, sageDeep);
                        text.ToolTip = "Faster than this sector on every clean lap before it";
                    }
                    if (bestSoFar[k] is not { } prev || sv < prev) bestSoFar[k] = sv;
                }
                Cell(table, row, c++, text, true);
            }
            Cell(table, row, c++, Caption(lap.FuelUsed is { } fuel ? StatFormat.Value(StatKind.Fuel, fuel, imperial) : "–"), true);
            Cell(table, row, c++, Caption(lap.TopSpeed is { } top ? StatFormat.Value(StatKind.Speed, top, imperial) : "–"), true);
            var flag = new WrapPanel();
            void Tag(string text, string soft, string deep, string? tip = null)
            {
                var chip = Ui.Chip(text, soft, deep);
                chip.Margin = new Thickness(0, 0, 4, 0);
                chip.ToolTip = tip;
                flag.Children.Add(chip);
            }
            if (isPb) Tag("All-time best", pbSoft, pbDeep, "Your all-time best in this car at this track");
            else if (isBest) Tag("Fastest", "AccentSoft", "AccentText", "The session's fastest clean lap");
            if (lap.Flags.HasFlag(LapFlags.Incident)) Tag(lap.Incidents is > 0 and var n ? $"Incident {n}x" : "Incident", coralSoft, coralDeep);
            if (lap.Flags.HasFlag(LapFlags.OffTrack)) Tag("Off track", coralSoft, coralDeep);
            if (lap.Flags.HasFlag(LapFlags.PitLane)) Tag(lap.Flags.HasFlag(LapFlags.Refuel) ? "Pit stop" : "Pit lane", "SurfaceAlt", "TextMuted", "Out lap, in lap or a stop: never a clean lap");
            if (!lap.Clean && lap.Flags == LapFlags.None) Tag("Not clean", coralSoft, coralDeep, "An incident, the pit lane or off track on this lap");
            Cell(table, row, c, flag);
        }
        var tableScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = table };
        FrameworkElement lapsBody = tableScroll;
        if (d.Traced.Count == 0)
        {
            var why = Ui.Text($"This session can't be analyzed. {noTraceReason} Its lap times, sectors and stats are all still here.", "Caption");
            why.TextWrapping = TextWrapping.Wrap;
            var note = new Tile { Child = Ui.IconText("I.Info", why, "TextMuted"), CornerRadius = ThemeManager.Round(4), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 8) };
            note.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            lapsBody = new StackPanel { Children = { note, tableScroll } };
        }
        var lapsCard = StatsSections.Section("Laps", "I.Flag", d.Traced.Count > 0 ? $"{d.Traced.Count} with a trace" : "No lap traces", analyzeButton, lapsBody);
        lapsCard.Margin = new Thickness(0, 0, 0, Ui.PanelSpacing);
        lapsCard.VerticalAlignment = VerticalAlignment.Top;
        chartCard.VerticalAlignment = VerticalAlignment.Top;
        page.Children.Add(SideBySide(lapsCard, chartCard));
        return page;
    }

    private const double ChartMinWidth = 420;

    private static Grid SideBySide(FrameworkElement laps, FrameworkElement chart)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.Children.Add(laps);
        row.Children.Add(chart);
        bool? wide = null;
        var follow = Sticky.Install(chart, row, () => wide == true, Ui.PanelSpacing);
        void Arrange(double width)
        {
            laps.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            bool two = width - laps.DesiredSize.Width - Ui.PanelSpacing >= ChartMinWidth;
            if (wide == two) return;
            wide = two;
            row.ColumnDefinitions[0].Width = two ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            row.ColumnDefinitions[1].Width = two ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetRow(chart, 0);
            Grid.SetColumn(chart, two ? 1 : 0);
            Grid.SetRow(laps, two ? 0 : 1);
            chart.Margin = new Thickness(two ? Ui.PanelSpacing : 0, 0, 0, Ui.PanelSpacing);
            follow();
        }
        Arrange(0);
        row.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) Arrange(e.NewSize.Width); };
        row.Loaded += (_, _) => Arrange(row.ActualWidth);
        return row;
    }

    private static string? SimTime(string text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? Dates.DateTime(t) : null;

    private static readonly string[] SkyNames = { "Clear", "Partly cloudy", "Mostly cloudy", "Overcast" };

    private static readonly string[] WetnessNames = { "", "Dry", "Mostly dry", "Very lightly wet", "Lightly wet", "Moderately wet", "Very wet", "Extremely wet" };

    private static readonly string[] Compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    private static FrameworkElement? ConditionsStrip(SessionSummary s, SessionConditions? c, bool imperial)
    {
        var strip = new WrapPanel();
        void Add(string icon, string label, string value, string tip)
        {
            var cell = new DockPanel { Margin = new Thickness(0, 0, 28, 12), MinWidth = 120, ToolTip = tip };
            var glyph = new Icon { Data = Ui.Res<Geometry>(icon), Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            glyph.SetResourceReference(Control.ForegroundProperty, "AccentText");
            cell.Children.Add(glyph);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var reading = new TextBlock { Text = value, Style = Ui.Res<Style>("Body"), FontSize = 15, FontWeight = FontWeights.SemiBold };
            reading.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
            text.Children.Add(reading);
            text.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption") });
            cell.Children.Add(text);
            strip.Children.Add(cell);
        }
        string type = s.Weather.Split(" · ")[0];
        if (c?.Skies is { } sky) Add(sky == 0 ? "I.Sun" : "I.Cloud", type.Length > 0 ? $"Skies · {type}" : "Skies", SkyNames[sky], "Skies when you first drove · weather type");
        else if (s.Weather.Length > 0) Add("I.Cloud", "Weather", s.Weather, "Weather");
        if (c is not null)
        {
            if (c.TrackTempMin is { } lo && c.TrackTempMax is { } hi)
                Add("I.Thermometer", "Track", Math.Round(hi - lo) >= 1 ? $"{StatFormat.Number(StatKind.Temp, lo, imperial)}–{StatFormat.Value(StatKind.Temp, hi, imperial)}" : StatFormat.Value(StatKind.Temp, hi, imperial),
                    "Track temperature while you drove (lowest–highest)");
            if (c.AirTemp is { } air) Add("I.Thermometer", "Air", StatFormat.Value(StatKind.Temp, air, imperial), "Air temperature when you first drove");
            if (c.WindKph is { } wind)
                Add("I.Wind", c.WindDir is { } dir ? "Wind from " + Compass[(int)Math.Round((dir % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) / (Math.PI / 4)) % 8] : "Wind",
                    StatFormat.Value(StatKind.Speed, wind, imperial), "Wind speed and where it comes from");
            if (c.Humidity is { } hum) Add("I.Droplet", "Humidity", $"{hum * 100:0}%", "Relative humidity");
            if (c.Pressure is { } pa) Add("I.Gauge", "Pressure", imperial ? $"{pa / 3386.389:0.00} inHg" : $"{pa / 100:0} hPa", "Air pressure");
            if (c.AirDensity is { } rho) Add("I.Layers", "Air density", imperial ? $"{rho * 0.062428:0.000} lb/ft³" : $"{rho:0.00} kg/m³", "Air density: less air, less grip and power");
            if (c.RainMax is > 0 and var rain) Add("I.CloudRain", "Most rain", $"{rain * 100:0}%", "The most rain while you drove");
            if (c.WetnessMax is > 1 and < 8 and var wet) Add("I.Droplet", "Wettest", WetnessNames[wet], "The wettest the track got while you drove");
            if (c.Rubber.Length > 0) Add("I.Wheel", "Rubber", c.Rubber, "Rubber on the racing line at the start");
        }
        if (strip.Children.Count == 0) return null;
        var block = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var rule = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 14) };
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        block.Children.Add(rule);
        block.Children.Add(strip);
        return block;
    }

    private static Tile Card(FrameworkElement content) => new()
    {
        Style = Ui.Res<Style>("Card"), Padding = new Thickness(24, 20, 24, 24), Child = content, Margin = new Thickness(0, 0, 0, Ui.PanelSpacing),
    };

    private static TextBlock Caption(string text) => new() { Text = text, Style = Ui.Res<Style>("Caption") };

    private static void Cell(Grid table, int row, int col, FrameworkElement element, bool right = false)
    {
        element.Margin = new Thickness(col == 0 ? 8 : 18, 7, 0, 7);
        element.VerticalAlignment = VerticalAlignment.Center;
        if (right) element.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        table.Children.Add(element);
    }

    private sealed class LapTimeChart : StatsCharts.HoverChart
    {
        private readonly List<LapRow> _laps;
        private readonly double _lo, _hi, _step;

        public LapTimeChart(List<LapRow> laps)
        {
            _laps = laps;
            var times = laps.Select(l => l.Time).OrderBy(t => t).ToList();
            double median = StatsCharts.Quantile(times, 0.5);
            (_lo, _hi, _step) = StatsCharts.LapScale(times[0], Math.Min(times[^1], median * 1.04));
        }

        private double X(int i) => Left + (ActualWidth - Left - Right) * (_laps.Count == 1 ? 0.5 : (double)i / (_laps.Count - 1));

        protected override int HitAt(Point p)
        {
            int best = -1;
            double near = 14;
            for (int i = 0; i < _laps.Count; i++)
                if (Math.Abs(X(i) - p.X) < near) { near = Math.Abs(X(i) - p.X); best = i; }
            return best;
        }

        protected override string TipOf(int i)
        {
            var l = _laps[i];
            return $"Lap {l.Lap} · {StatFormat.LapTime(l.Time)}{(l.Clean ? "" : " · not clean")}";
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth <= Left + Right || ActualHeight <= Top + Bottom) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            var y = LapAxis(dc, _lo, _hi, _step);
            var accent = Ui.Res<Brush>("AccentText");
            var line = new Pen(Ui.Res<Brush>("Accent"), 1.5) { LineJoin = PenLineJoin.Round };
            var hollow = new Pen(Ui.Res<Brush>("TextMuted"), 1.5);
            Point P(int i) => new(X(i), Math.Max(Top, y(Math.Min(_hi, _laps[i].Time))));
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                bool started = false;
                for (int i = 0; i < _laps.Count; i++)
                {
                    if (!_laps[i].Clean || _laps[i].Time > _hi) { started = false; continue; }
                    if (!started) { g.BeginFigure(P(i), false, false); started = true; }
                    else g.LineTo(P(i), true, true);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, line, geo);
            int step = Math.Max(1, (int)Math.Ceiling(_laps.Count / ((ActualWidth - Left - Right) / 34)));
            for (int i = 0; i < _laps.Count; i++)
            {
                var p = P(i);
                double r = i == Hover ? 5 : 3.5;
                if (_laps[i].Clean) dc.DrawEllipse(accent, null, p, r, r);
                else dc.DrawEllipse(Ui.Res<Brush>("Surface"), hollow, p, r, r);
                if (i % step == 0 || i == _laps.Count - 1)
                {
                    var label = Text(_laps[i].Lap.ToString(CultureInfo.CurrentCulture));
                    dc.DrawText(label, new Point(p.X - label.Width / 2, ActualHeight - Bottom + 6));
                }
            }
        }
    }
}

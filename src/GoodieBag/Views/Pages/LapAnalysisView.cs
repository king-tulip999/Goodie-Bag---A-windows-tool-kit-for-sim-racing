using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.SessionStats;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed record AnalysisData(SessionSummary Session, List<(int Lap, double Time, LapTrace Trace)> Laps, (int Lap, LapTrace Trace)? SessionBest,
    (string Label, LapTrace Trace)? TrackBest, LapTrace? Optimal, IReadOnlyList<double> SectorStarts, string Driver,
    IReadOnlyList<SharedLapInfo> Shared, (SharedLapInfo Info, LapTrace Trace)? SharedPicked, SimRacing.TrackMaps.TrackMap? Map);

internal sealed record AnalysisActions(Action<string> PickReference, Action<int> RemoveLap, Action LanesChanged, Action<int> ExportLap, Action ImportLap,
    Action<long> DeleteShared, Action Save);

internal enum MapColors { Laps, Speed, Delta }

internal sealed class AnalysisLine
{
    public required string Label { get; init; }
    public required LapTrace Trace { get; init; }
    public required string BrushKey { get; init; }
    public string Driver { get; init; } = "";
    public string Short { get; init; } = "";
    public int Lap { get; init; }
    public bool Reference { get; set; }
    public double[]? Delta { get; set; }
    public (double[] X, double[] Y)? Line { get; set; }
    public Dictionary<string, (double[]? A, double[]? B)> Lanes { get; } = new();
    public List<(double From, double To)> PushToPass { get; } = new();
    public (double Kph, double From)? Wind { get; set; }

    public bool BoostAt(double d) => PushToPass.Any(s => d >= s.From && d <= s.To);
}

internal sealed class AnalysisState
{
    public const double MinSpan = 60;
    public List<AnalysisLine> Lines { get; } = new();
    public AnalysisLine Reference { get; set; } = null!;
    public double Length { get; set; }
    public IReadOnlyList<double> SectorDistances { get; set; } = Array.Empty<double>();
    public bool Imperial { get; init; }
    public MapColors MapColors { get; set; }
    public double D0 { get; private set; }
    public double D1 { get; private set; }
    public double? Cursor { get; private set; }
    public (double From, double To)? Selecting { get; set; }

    public bool Zoomed => D0 > 0.5 || D1 < Length - 0.5;

    public event Action? ZoomChanged, CursorChanged;

    public void Zoom(double a, double b)
    {
        if (a > b) (a, b) = (b, a);
        if (b - a < MinSpan) { double mid = (a + b) / 2; a = mid - MinSpan / 2; b = mid + MinSpan / 2; }
        if (a < 0) { b -= a; a = 0; }
        if (b > Length) { a -= b - Length; b = Length; }
        D0 = Math.Max(0, a);
        D1 = Math.Min(Length, b);
        ZoomChanged?.Invoke();
    }

    public void ResetZoom() => Zoom(0, Length);

    public void SetCursor(double? d)
    {
        if (Cursor == d) return;
        Cursor = d;
        CursorChanged?.Invoke();
    }

    public void RaiseCursor() => CursorChanged?.Invoke();
}

internal static class LapAnalysisView
{
    public static readonly string[] LapBrushes = { "AccentText", "Tool2Deep", "Tool7Deep", "Tool3Deep" };

    private static readonly Dictionary<string, string> LaneNames = new()
    {
        ["delta"] = "Time delta", ["speed"] = "Speed", ["pedals"] = "Throttle / brake", ["steering"] = "Steering angle", ["gear"] = "Gear", ["rpm"] = "RPM", ["latg"] = "Lateral g",
    };

    public static FrameworkElement Build(AnalysisData data, Config.SessionStatsSettings settings, string reference, AnalysisActions actions, ReplayMemory memory,
        out AnalysisState state)
    {
        var s = state = Make(data, settings.Imperial, reference);
        var page = new StackPanel();
        Action<string> pickReference = actions.PickReference;
        Action lanesChanged = actions.LanesChanged;

        var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var line in s.Lines)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 14, 2), VerticalAlignment = VerticalAlignment.Center };
            var swatch = new Border { Width = 18, Height = 4, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, line.BrushKey);
            if (line.Reference) swatch.Opacity = 0.6;
            chip.Children.Add(swatch);
            chip.Children.Add(new TextBlock { Text = line.Label, Style = Ui.Res<Style>("Body"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            if (line.Reference) chip.Children.Add(new TextBlock { Text = "Reference", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            int lapNumber = data.Laps.FirstOrDefault(l => ReferenceEquals(l.Trace, line.Trace)).Lap;
            if (lapNumber > 0)
            {
                var export = Ui.IconButton("I.Save", "Export this lap (to share or compare)", () => actions.ExportLap(lapNumber), "QuietIconButton");
                export.Margin = new Thickness(2, 0, 0, 0);
                chip.Children.Add(export);
            }
            if (lapNumber > 0 && data.Laps.Count > 1)
            {
                var remove = Ui.IconButton("I.X", "Take this lap out", () => actions.RemoveLap(lapNumber), "QuietIconButton");
                remove.Margin = new Thickness(2, 0, 0, 0);
                chip.Children.Add(remove);
            }
            chips.Children.Add(chip);
        }

        var refChoices = new List<Choice>();
        if (data.SessionBest is { } sb) refChoices.Add(new Choice("session", $"Session best · {StatFormat.LapTime(sb.Trace.LapTime)}"));
        if (data.TrackBest is { } tb) refChoices.Add(new Choice("track", $"Your best here · {tb.Label}"));
        if (data.Optimal is { } opt) refChoices.Add(new Choice("optimal", $"Optimal · {StatFormat.LapTime(opt.LapTime)}"));
        foreach (var l in data.Laps) refChoices.Add(new Choice("lap:" + l.Lap, $"Lap {l.Lap} · {StatFormat.LapTime(l.Time)}"));
        foreach (var shared in data.Shared) refChoices.Add(new Choice("shared:" + shared.Id, SharedLabel(shared, data.Session.Car)));
        var refPick = Ui.Combo(refChoices, reference, c => { if (c.Id is { } id && id != reference) pickReference(id); }, 230);
        refPick.ToolTip = "What the delta is measured against";
        var import = Ui.LabeledButton("I.Folder", "Import lap", actions.ImportLap);
        import.ToolTip = "Open a lap someone exported (same track) to compare against";
        Button? deleteShared = null;
        if (data.SharedPicked is { } picked && reference == "shared:" + picked.Info.Id)
        {
            deleteShared = Ui.IconButton("I.Trash", "Remove this imported lap", () => actions.DeleteShared(picked.Info.Id), "QuietIconButton");
            deleteShared.Margin = new Thickness(4, 0, 0, 0);
        }

        var mapMode = Ui.Segmented(new[] { "Laps", "Speed", "Delta" }, (int)s.MapColors, i => { s.MapColors = (MapColors)i; s.RaiseCursor(); }, "lapMapColors",
            new[] { "Each lap in its color", "The first lap colored by speed: slow to fast", "The first lap colored by where it gains (or loses) time on the reference" });
        var reset = Ui.LabeledButton("I.Restore", "Reset zoom", s.ResetZoom);
        reset.IsEnabled = false;
        s.ZoomChanged += () => reset.IsEnabled = s.Zoomed;
        var lanesButton = Ui.LabeledButton("I.Sliders", "Lanes", () => { });
        lanesButton.Click += (_, _) => OpenLanes(lanesButton, settings, lanesChanged);

        var windy = s.Lines.Any(l => l.Wind is not null);
        var windBox = WindBox(s);
        windBox.Visibility = windy && settings.AnalysisWind ? Visibility.Visible : Visibility.Collapsed;
        var windButton = Ui.LabeledButton("I.Wind", "Wind", () => { });
        windButton.ToolTip = "Wind speed and direction on each lap, on the map";
        Ui.SetToggled(windButton, settings.AnalysisWind);
        windButton.Click += (_, _) =>
        {
            settings.AnalysisWind = !settings.AnalysisWind;
            Ui.SetToggled(windButton, settings.AnalysisWind);
            windBox.Visibility = settings.AnalysisWind ? Visibility.Visible : Visibility.Collapsed;
            actions.Save();
        };
        var refRow = Labeled("Reference", refPick);
        if (deleteShared is not null) refRow.Children.Add(deleteShared);
        var right = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var e in new FrameworkElement[] { refRow, import, mapMode, windButton, reset, lanesButton })
        {
            if (e == windButton && !windy) continue;
            e.Margin = new Thickness(10, 2, 0, 2);
            e.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(e);
        }
        var toolbar = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(20, 12, 20, 12), Margin = new Thickness(0, 0, 0, Ui.PanelSpacing), Child = Ui.SplitRow(chips, right, 16) };
        page.Children.Add(toolbar);

        var map = new LapMap(s) { Height = 460 };
        var mapCursor = new MapCursor(s, map) { IsHitTestVisible = false };
        var mapHost = new Grid { ClipToBounds = true, Children = { map, mapCursor, windBox } };
        var lanes = (settings.AnalysisLanes ?? Config.SessionStatsSettings.DefaultLanes.ToList()).Where(LaneNames.ContainsKey).ToList();
        if (lanes.Count == 0) lanes.Add("speed");
        var chart = new TraceChart(s, lanes);
        var chartCursor = new ChartCursor(s, chart) { IsHitTestVisible = false };
        var chartHost = new Grid { Children = { chart, chartCursor } };

        var mapCard = Card(StatsSections.Chart("Line", s.Lines.Any(l => l.Line is not null) ? "Where each lap drove; the wide band is the reference's" : null, mapHost));
        var chartCard = Card(StatsSections.Chart("Telemetry", "Drag across to zoom, double-click for the whole lap"
            + (s.Lines.Any(l => l.PushToPass.Count > 0) ? "; strips along the bottom of each lane = push-to-pass in use" : ""), chartHost));
        var telemetry = StatsLayout.Pair(mapCard, chartCard, 1100, 2, 3, Ui.PanelSpacing)!;
        page.Children.Add(telemetry);
        page.Children.Add(new LapReplayView(data, s, settings, memory, actions.Save));
        s.ResetZoom();
        return page;
    }

    private static FrameworkElement WindBox(AnalysisState s)
    {
        var rows = new Grid();
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            rows.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        rows.RowDefinitions.Add(new RowDefinition());
        rows.Children.Add(Cell(new TextBlock { Text = "Wind", Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) }, 0, 0, 4));
        int row = 1;
        foreach (var line in s.Lines)
        {
            if (line.Wind is not { } wind) continue;
            rows.RowDefinitions.Add(new RowDefinition());
            var swatch = new Border { Width = 4, Height = 16, Margin = new Thickness(0, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, line.BrushKey);
            if (line.Reference) swatch.Opacity = 0.6;
            double to = wind.From + Math.PI;
            var arrow = new Icon
            {
                Data = Geometry.Parse("M12 20V4M6 10l6-6 6 6"), Width = 16, Height = 16, StrokeThickness = 2.2, Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(to * 180 / Math.PI),
            };
            arrow.SetResourceReference(Icon.ForegroundProperty, line.BrushKey);
            double speed = s.Imperial ? wind.Kph / 1.609344 : wind.Kph;
            var value = new TextBlock { Text = $"{line.Short}  {speed:0} {(s.Imperial ? "mph" : "km/h")}", Style = Ui.Res<Style>("Body"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var point = new TextBlock { Text = SimRacing.Race.RaceFormat.Compass(to), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
            rows.Children.Add(Cell(swatch, row, 0));
            rows.Children.Add(Cell(arrow, row, 1));
            rows.Children.Add(Cell(value, row, 2));
            rows.Children.Add(Cell(point, row, 3));
            row++;
        }
        return new Tile
        {
            Style = Ui.Res<Style>("Card"), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 4, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Child = rows,
        };

        static UIElement Cell(FrameworkElement e, int row, int column, int span = 1)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, column);
            Grid.SetColumnSpan(e, span);
            return e;
        }
    }

    internal static string SharedLabel(SharedLapInfo lap, string car) =>
        $"{(lap.Driver.Length > 0 ? lap.Driver : "Imported")} · {StatFormat.LapTime(lap.LapTime)}{(string.Equals(lap.Car, car, StringComparison.OrdinalIgnoreCase) ? "" : " · " + lap.Car)}";

    private static StackPanel Labeled(string label, FrameworkElement control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(control);
        return row;
    }

    private static Tile Card(FrameworkElement content) => new()
    {
        Style = Ui.Res<Style>("Card"), Padding = new Thickness(20, 16, 20, 20), Child = content, Margin = new Thickness(0, 0, 0, Ui.PanelSpacing),
    };

    private static void OpenLanes(FrameworkElement anchor, Config.SessionStatsSettings settings, Action changed)
    {
        var content = new ContentControl();
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Custom, StaysOpen = false, AllowsTransparency = true,
            CustomPopupPlacementCallback = (size, target, _) => new[] { new CustomPopupPlacement(new Point(target.Width - size.Width, target.Height), PopupPrimaryAxis.Horizontal) },
            Child = new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 4, 0, 0), MaxWidth = 420, Child = content },
        };
        RenderOptions.SetClearTypeHint(popup.Child, ClearTypeHint.Enabled);
        Ui.FollowTarget(popup);
        void Fill()
        {
            var on = (settings.AnalysisLanes ?? Config.SessionStatsSettings.DefaultLanes.ToList()).ToList();
            void Save(List<string> lanes) { settings.AnalysisLanes = lanes; changed(); Fill(); }
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Shown · drag to reorder", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 6) });
            var grid = new ReorderGrid { Flow = true, MinCellWidth = 40, Gap = 6 };
            foreach (var id in on)
            {
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Tag = id };
                chip.Children.Add(Ui.Chip(LaneNames[id]));
                if (on.Count > 1)
                {
                    var lane = id;
                    var x = Ui.IconButton("I.X", "Hide this lane", () => Save(on.Where(l => l != lane).ToList()), "QuietIconButton");
                    chip.Children.Add(x);
                }
                grid.Children.Add(chip);
            }
            grid.Moved += (_, _) => Save(grid.Children.OfType<FrameworkElement>().Select(c => (string)c.Tag).ToList());
            stack.Children.Add(grid);
            var off = Config.SessionStatsSettings.Lanes.Where(l => !on.Contains(l)).ToList();
            if (off.Count > 0)
            {
                stack.Children.Add(new TextBlock { Text = "Add", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 12, 0, 6) });
                var add = new WrapPanel();
                foreach (var id in off)
                {
                    var lane = id;
                    var b = Ui.LabeledButton("I.Plus", LaneNames[id], () => Save(on.Append(lane).ToList()), "SoftButton");
                    b.Margin = new Thickness(0, 0, 6, 6);
                    add.Children.Add(b);
                }
                stack.Children.Add(add);
            }
            content.Content = stack;
        }
        Fill();
        popup.IsOpen = true;
    }

    internal static AnalysisState Make(AnalysisData data, bool imperial, string reference)
    {
        var s = new AnalysisState { Imperial = imperial };
        for (int i = 0; i < data.Laps.Count; i++)
            s.Lines.Add(new AnalysisLine
            {
                Label = $"Lap {data.Laps[i].Lap} · {StatFormat.LapTime(data.Laps[i].Time)}", Trace = data.Laps[i].Trace, BrushKey = LapBrushes[i % LapBrushes.Length],
                Driver = data.Driver, Short = $"Lap {data.Laps[i].Lap}", Lap = data.Laps[i].Lap,
            });
        AnalysisLine? refLine = null;
        if (reference.StartsWith("lap:") && int.TryParse(reference[4..], out int n) && data.Laps.FindIndex(l => l.Lap == n) is var at and >= 0) refLine = s.Lines[at];
        else
        {
            var (label, shortName, driver, trace) = reference switch
            {
                _ when reference.StartsWith("shared:") && data.SharedPicked is { } sp => (SharedLabel(sp.Info, data.Session.Car), "Imported", sp.Info.Driver, sp.Trace),
                "track" when data.TrackBest is { } tb => ($"Your best here · {tb.Label}", "Your best", data.Driver, tb.Trace),
                "optimal" when data.Optimal is { } o => ($"Optimal · {StatFormat.LapTime(o.LapTime)}", "Optimal", data.Driver, o),
                _ when data.SessionBest is { } sb => ($"Session best · lap {sb.Lap} · {StatFormat.LapTime(sb.Trace.LapTime)}", "Session best", data.Driver, sb.Trace),
                _ => ("", "", "", null!),
            };
            if (trace is not null)
            {
                refLine = s.Lines.FirstOrDefault(l => ReferenceEquals(l.Trace, trace));
                if (refLine is null) { refLine = new AnalysisLine { Label = label, Trace = trace, BrushKey = "TextMuted", Driver = driver, Short = shortName }; s.Lines.Add(refLine); }
            }
        }
        refLine ??= s.Lines[0];
        refLine.Reference = true;
        s.Reference = refLine;
        s.Length = refLine.Trace.LengthM;
        s.SectorDistances = data.SectorStarts.Where(p => p > 0).Select(p => p * s.Length).ToList();
        foreach (var line in s.Lines)
        {
            var t = line.Trace;
            if (!line.Reference) line.Delta = LapTrace.Delta(t, refLine.Trace);
            line.Line = t.LineIn(refLine.Trace);
            double speedK = imperial ? 2.236936 : 3.6;
            line.Lanes["delta"] = (line.Delta, null);
            line.Lanes["speed"] = (Scaled(t[TraceChannel.Speed], speedK), null);
            line.Lanes["pedals"] = (Scaled(t[TraceChannel.Throttle], 100), Scaled(t[TraceChannel.Brake], 100));
            line.Lanes["steering"] = (Scaled(t[TraceChannel.Steering], -180 / Math.PI), null);
            line.Lanes["gear"] = (t[TraceChannel.Gear], null);
            line.Lanes["rpm"] = (t[TraceChannel.Rpm], null);
            line.Lanes["latg"] = (Scaled(t[TraceChannel.LatAccel], 1 / 9.80665), null);
            line.PushToPass.AddRange(BoostSpans(t));
            line.Wind = t.WindOfLap();
        }
        s.Lines.Remove(refLine);
        s.Lines.Insert(0, refLine);
        return s;
    }

    private static double[]? Scaled(double[]? values, double k) => values?.Select(v => v * k).ToArray();

    internal static List<(double From, double To)> BoostSpans(LapTrace t)
    {
        var spans = new List<(double, double)>();
        if (t[TraceChannel.PushToPass] is not { } p) return spans;
        int start = -1;
        for (int i = 0; i <= p.Length; i++)
        {
            bool on = i < p.Length && p[i] > 0.5;
            if (on && start < 0) start = i;
            else if (!on && start >= 0) { spans.Add((t.Distance(start), t.Distance(i - 1))); start = -1; }
        }
        return spans;
    }

    internal static string LaneLabel(string id, bool imperial) => id switch
    {
        "delta" => "Time delta (seconds)", "speed" => imperial ? "Speed (mph)" : "Speed (km/h)", "pedals" => "Throttle / brake (%)", "steering" => "Steering angle (degrees)",
        _ => LaneNames.TryGetValue(id, out var n) ? n : id,
    };

    internal static string LaneHue(string lane) => lane switch
    {
        "delta" => "AccentText",
        "speed" => StatsUi.Hue("Slate").Deep,
        "pedals" => StatsUi.Hue("Sage").Deep,
        "steering" => StatsUi.Hue("Plum").Deep,
        "gear" => StatsUi.Hue("Gold").Deep,
        "rpm" => StatsUi.Hue("Coral").Deep,
        _ => StatsUi.Hue("Slate").Deep,
    };

    internal static string Format(string lane, double v) => lane switch
    {
        "delta" => (v < 0 ? "−" : "+") + Math.Abs(v).ToString("0.000", CultureInfo.CurrentCulture),
        "latg" => v.ToString("0.00", CultureInfo.CurrentCulture),
        "gear" => v < 0 ? "R" : v == 0 ? "N" : v.ToString("0", CultureInfo.CurrentCulture),
        _ => v.ToString("0", CultureInfo.CurrentCulture),
    };

    internal static string DistanceText(double m, bool imperial) =>
        imperial ? (m / 1609.344).ToString("0.00", CultureInfo.CurrentCulture) + " mi" : (m / 1000).ToString("0.00", CultureInfo.CurrentCulture) + " km";

    internal static SolidColorBrush Brush(string key) => Ui.Res<Brush>(key) as SolidColorBrush ?? Brushes.Gray;

    internal static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    internal static FormattedText Text(Visual v, string s, Brush brush, double size = 11, bool bold = false) =>
        new(ThemeManager.Case(s), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeManager.UiFace(bold: bold), size, brush, VisualTreeHelper.GetDpi(v).PixelsPerDip);
}

internal readonly record struct MapView(double MinX, double MaxY, double Scale, double Ox, double Oy)
{
    public Point At(double x, double y) => new(Ox + (x - MinX) * Scale, Oy + (MaxY - y) * Scale);
}

internal sealed class LapMap : FrameworkElement
{
    private const int Buckets = 16;
    private readonly AnalysisState _s;

    public LapMap(AnalysisState s)
    {
        _s = s;
        s.ZoomChanged += InvalidateVisual;
        s.CursorChanged += () => { if (_mode != s.MapColors) InvalidateVisual(); };
        ClipToBounds = true;
    }

    private MapColors _mode;

    public MapView? View
    {
        get
        {
            if (_s.Reference.Line is not { } r || ActualWidth < 40 || ActualHeight < 40) return null;
            var t = _s.Reference.Trace;
            int i0 = (int)(_s.D0 / t.StepM), i1 = Math.Min(t.Points - 1, (int)Math.Ceiling(_s.D1 / t.StepM));
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int i = i0; i <= i1; i++)
            {
                minX = Math.Min(minX, r.X[i]); maxX = Math.Max(maxX, r.X[i]);
                minY = Math.Min(minY, r.Y[i]); maxY = Math.Max(maxY, r.Y[i]);
            }
            double pad = 24, w = ActualWidth - 2 * pad, h = ActualHeight - 2 * pad;
            double scale = Math.Min(w / Math.Max(10, maxX - minX), h / Math.Max(10, maxY - minY));
            return new MapView(minX, maxY, scale, pad + (w - (maxX - minX) * scale) / 2, pad + (h - (maxY - minY) * scale) / 2);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (View is not { } view || _s.Reference.Line is not { } line) return;
        var p = e.GetPosition(this);
        var t = _s.Reference.Trace;
        int i0 = (int)(_s.D0 / t.StepM), i1 = Math.Min(t.Points - 1, (int)(_s.D1 / t.StepM));
        int best = -1;
        double near = 30 * 30;
        for (int i = i0; i <= i1; i++)
        {
            var q = view.At(line.X[i], line.Y[i]);
            double d = (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y);
            if (d < near) { near = d; best = i; }
        }
        _s.SetCursor(best >= 0 ? t.Distance(best) : null);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _s.SetCursor(null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        _mode = _s.MapColors;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_s.Reference.Line is not { } r || View is not { } view)
        {
            var none = LapAnalysisView.Text(this, "No position data for these laps", Ui.Res<Brush>("TextMuted"), 12);
            dc.DrawText(none, new Point((ActualWidth - none.Width) / 2, (ActualHeight - none.Height) / 2));
            return;
        }
        var t = _s.Reference.Trace;
        var track = new Pen(Ui.Res<Brush>("Line"), Math.Clamp(12 * view.Scale, 6, 46)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        track.Freeze();
        dc.DrawGeometry(null, track, Path(r.X, r.Y, 0, t.Points - 1, view));

        bool zoomed = _s.Zoomed;
        var colored = _s.MapColors == MapColors.Laps ? null : _s.Lines.FirstOrDefault(l => !l.Reference) ?? _s.Lines[0];
        foreach (var line in _s.Lines)
        {
            if (line.Line is not { } xy) continue;
            var lt = line.Trace;
            int a = (int)(_s.D0 / lt.StepM), b = Math.Min(lt.Points - 1, (int)Math.Ceiling(_s.D1 / lt.StepM));
            if (ReferenceEquals(line, colored) && Values(line) is { } values)
            {
                if (zoomed) dc.DrawGeometry(null, PenOf(line, 0.25), Path(xy.X, xy.Y, 0, lt.Points - 1, view));
                DrawBuckets(dc, xy, values, a, b, view);
                continue;
            }
            if (colored is not null && !line.Reference) continue;
            if (zoomed) dc.DrawGeometry(null, PenOf(line, 0.25), Path(xy.X, xy.Y, 0, lt.Points - 1, view));
            dc.DrawGeometry(null, PenOf(line, line.Reference ? 0.7 : 1), Path(xy.X, xy.Y, a, b, view));
        }
        foreach (double d in _s.SectorDistances.Prepend(0))
        {
            int i = Math.Min(t.Points - 1, (int)(d / t.StepM));
            if (d < _s.D0 - 1 || d > _s.D1 + 1) continue;
            var p = view.At(r.X[i], r.Y[i]);
            var label = LapAnalysisView.Text(this, d == 0 ? "S/F" : "S" + (_s.SectorDistances.ToList().IndexOf(d) + 2), Ui.Res<Brush>("TextMuted"), 10.5, bold: true);
            dc.DrawText(label, new Point(p.X + 8, p.Y - label.Height - 4));
            dc.DrawEllipse(Ui.Res<Brush>("TextMuted"), null, p, 2.5, 2.5);
        }
    }

    private double[]? Values(AnalysisLine line)
    {
        if (_s.MapColors == MapColors.Speed) return line.Trace[TraceChannel.Speed];
        if (line.Delta is not { } delta) return null;
        var slope = new double[line.Trace.Points];
        for (int i = 0; i < slope.Length; i++)
        {
            int a = Math.Max(0, i - 15), b = Math.Min(delta.Length - 1, i + 15);
            slope[i] = b > a && b < delta.Length ? (delta[b] - delta[a]) / (b - a) : 0;
        }
        return slope;
    }

    private void DrawBuckets(DrawingContext dc, (double[] X, double[] Y) xy, double[] values, int a, int b, MapView view)
    {
        Color[] colors;
        Func<double, int> bucket;
        if (_s.MapColors == MapColors.Speed)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            for (int i = a; i <= b; i++) { lo = Math.Min(lo, values[i]); hi = Math.Max(hi, values[i]); }
            Color slow = LapAnalysisView.Brush(StatsUi.Hue("Coral").Deep).Color, mid = LapAnalysisView.Brush(StatsUi.Hue("Gold").Deep).Color, fast = LapAnalysisView.Brush(StatsUi.Hue("Sage").Deep).Color;
            colors = Enumerable.Range(0, Buckets).Select(k => { double u = k / (Buckets - 1.0); return u < 0.5 ? LapAnalysisView.Mix(slow, mid, u * 2) : LapAnalysisView.Mix(mid, fast, u * 2 - 1); }).ToArray();
            double span = Math.Max(1e-6, hi - lo);
            bucket = v => (int)Math.Clamp((v - lo) / span * (Buckets - 1), 0, Buckets - 1);
        }
        else
        {
            Color gain = LapAnalysisView.Brush(StatsUi.Hue("Sage").Deep).Color, even = LapAnalysisView.Brush("TextFaint").Color, loss = LapAnalysisView.Brush(StatsUi.Hue("Coral").Deep).Color;
            colors = Enumerable.Range(0, Buckets).Select(k => { double u = k / (Buckets - 1.0); return u < 0.5 ? LapAnalysisView.Mix(gain, even, u * 2) : LapAnalysisView.Mix(even, loss, u * 2 - 1); }).ToArray();
            const double Full = 0.0015;
            bucket = v => (int)Math.Clamp((v / Full + 1) / 2 * (Buckets - 1), 0, Buckets - 1);
        }
        var geos = new StreamGeometry[Buckets];
        var ctx = new StreamGeometryContext?[Buckets];
        for (int i = a; i < b && i + 1 < values.Length; i++)
        {
            int k = bucket(values[i]);
            if (ctx[k] is null) { geos[k] = new StreamGeometry(); ctx[k] = geos[k].Open(); }
            ctx[k]!.BeginFigure(view.At(xy.X[i], xy.Y[i]), false, false);
            ctx[k]!.LineTo(view.At(xy.X[i + 1], xy.Y[i + 1]), true, false);
        }
        for (int k = 0; k < Buckets; k++)
        {
            if (ctx[k] is not { } c) continue;
            c.Close();
            geos[k].Freeze();
            var pen = new Pen(new SolidColorBrush(colors[k]), 3.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geos[k]);
        }
    }

    private static Pen PenOf(AnalysisLine line, double opacity)
    {
        var brush = LapAnalysisView.Brush(line.BrushKey).Clone();
        brush.Opacity = opacity;
        var pen = new Pen(brush, line.Reference ? 2 : 2.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (line.Reference) pen.DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0);
        pen.Freeze();
        return pen;
    }

    private static StreamGeometry Path(double[] xs, double[] ys, int a, int b, MapView view)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(view.At(xs[a], ys[a]), false, false);
            var last = view.At(xs[a], ys[a]);
            for (int i = a + 1; i <= b; i++)
            {
                var p = view.At(xs[i], ys[i]);
                if (Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) < 0.75 && i < b) continue;
                g.LineTo(p, true, true);
                last = p;
            }
        }
        geo.Freeze();
        return geo;
    }
}

internal sealed class MapCursor : FrameworkElement
{
    private readonly AnalysisState _s;
    private readonly LapMap _map;

    public MapCursor(AnalysisState s, LapMap map)
    {
        _s = s;
        _map = map;
        s.CursorChanged += InvalidateVisual;
        s.ZoomChanged += InvalidateVisual;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_s.Cursor is not { } d || _map.View is not { } view) return;
        var edge = Ui.Res<Brush>("Surface");
        foreach (var line in _s.Lines)
        {
            if (line.Line is not { } xy) continue;
            int i = Math.Clamp((int)Math.Round(d / line.Trace.StepM), 0, line.Trace.Points - 1);
            var p = view.At(xy.X[i], xy.Y[i]);
            dc.DrawEllipse(Ui.Res<Brush>(line.BrushKey), new Pen(edge, 2), p, 6, 6);
        }
    }
}

internal sealed class TraceChart : FrameworkElement
{
    public const double Left = 92, Right = 12, Axis = 26;
    private readonly AnalysisState _s;
    private Point? _press;
    private bool _dragging;

    public TraceChart(AnalysisState s, List<string> lanes)
    {
        _s = s;
        Lanes = lanes;
        Height = lanes.Sum(LaneHeight) + Axis;
        s.ZoomChanged += InvalidateVisual;
        ClipToBounds = true;
        Focusable = false;
    }

    public List<string> Lanes { get; }

    public AnalysisState State => _s;

    public const double BoostStrip = 4;

    public static double LaneHeight(string lane) => lane switch { "gear" => 72, "delta" => 118, "speed" => 138, "pedals" => 112, _ => 100 };

    public double PlotWidth => Math.Max(1, ActualWidth - Left - Right);

    public double X(double d) => Left + (d - _s.D0) / Math.Max(1, _s.D1 - _s.D0) * PlotWidth;

    public double DistanceAt(double x) => _s.D0 + Math.Clamp((x - Left) / PlotWidth, 0, 1) * (_s.D1 - _s.D0);

    public Dictionary<string, (double Top, double Height, double Lo, double Hi)> Layout { get; } = new();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2) { _s.ResetZoom(); e.Handled = true; return; }
        _press = e.GetPosition(this);
        _dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (_press is { } start && e.LeftButton == MouseButtonState.Pressed)
        {
            if (Math.Abs(p.X - start.X) > 4) _dragging = true;
            if (_dragging) _s.Selecting = (DistanceAt(start.X), DistanceAt(p.X));
        }
        _s.SetCursor(p.X >= Left && p.X <= ActualWidth - Right ? DistanceAt(p.X) : null);
        if (_dragging) _s.RaiseCursor();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging && _s.Selecting is { } sel && Math.Abs(sel.To - sel.From) >= 5) _s.Zoom(sel.From, sel.To);
        _press = null;
        _dragging = false;
        _s.Selecting = null;
        ReleaseMouseCapture();
        _s.RaiseCursor();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_press is null) _s.SetCursor(null);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        double at = DistanceAt(e.GetPosition(this).X), k = e.Delta > 0 ? 0.8 : 1.25;
        double span = Math.Min(_s.Length, (_s.D1 - _s.D0) * k);
        double share = (at - _s.D0) / Math.Max(1, _s.D1 - _s.D0);
        _s.Zoom(at - span * share, at - span * share + span);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        Layout.Clear();
        if (ActualWidth <= Left + Right + 20) return;
        var grid = new Pen(Ui.Res<Brush>("GridLine"), 1);
        grid.Freeze();
        var strong = new Pen(Ui.Res<Brush>("LineStrong"), 1);
        strong.Freeze();
        var muted = Ui.Res<Brush>("TextMuted");
        var boost = _s.Lines.Where(l => l.PushToPass.Count > 0).Select(l => (LapAnalysisView.Brush(l.BrushKey), l.PushToPass)).ToList();
        double top = 0;
        foreach (var lane in Lanes)
        {
            double h = LaneHeight(lane), plotTop = top + 26, plotH = h - 34;
            var (lo, hi) = Range(lane);
            Layout[lane] = (plotTop, plotH, lo, hi);
            double Y(double v) => plotTop + plotH * (hi - v) / Math.Max(1e-9, hi - lo);
            dc.DrawText(LapAnalysisView.Text(this, LapAnalysisView.LaneLabel(lane, _s.Imperial), Ui.Res<Brush>(LapAnalysisView.LaneHue(lane)), 12.5, bold: true), new Point(0, top + 1));
            var hiText = LapAnalysisView.Text(this, LapAnalysisView.Format(lane, hi), muted, 11.5);
            var loText = LapAnalysisView.Text(this, LapAnalysisView.Format(lane, lo), muted, 11.5);
            dc.DrawText(hiText, new Point(Left - 8 - hiText.Width, plotTop - hiText.Height / 2));
            dc.DrawText(loText, new Point(Left - 8 - loText.Width, plotTop + plotH - loText.Height / 2));
            dc.DrawLine(grid, new Point(Left, Math.Round(plotTop) + 0.5), new Point(ActualWidth - Right, Math.Round(plotTop) + 0.5));
            dc.DrawLine(grid, new Point(Left, Math.Round(plotTop + plotH) + 0.5), new Point(ActualWidth - Right, Math.Round(plotTop + plotH) + 0.5));
            if (lane is "delta" or "steering" or "latg" && lo < 0 && hi > 0) dc.DrawLine(strong, new Point(Left, Math.Round(Y(0)) + 0.5), new Point(ActualWidth - Right, Math.Round(Y(0)) + 0.5));

            dc.PushClip(new RectangleGeometry(new Rect(Left, plotTop - 2, PlotWidth, plotH + 4)));
            for (int k = 0; k < boost.Count; k++)
                foreach (var (from, to) in boost[k].PushToPass)
                {
                    double a = X(from), b = X(to);
                    if (b >= Left && a <= ActualWidth - Right) dc.DrawRectangle(boost[k].Item1, null, new Rect(a, plotTop + plotH - BoostStrip * (k + 1), Math.Max(1, b - a), BoostStrip - 1));
                }
            foreach (var line in _s.Lines)
            {
                if (!line.Lanes.TryGetValue(lane, out var series)) continue;
                if (series.A is { } a) dc.DrawGeometry(null, PenFor(line, false, lane), Decimated(a, line.Trace.StepM, Y, lane == "gear"));
                if (series.B is { } b) dc.DrawGeometry(null, PenFor(line, true, lane), Decimated(b, line.Trace.StepM, Y, false));
            }
            dc.Pop();
            top += h;
        }
        var sectorPen = new Pen(Ui.Res<Brush>("LineStrong"), 1) { DashStyle = new DashStyle(new[] { 2.0, 3.0 }, 0) };
        sectorPen.Freeze();
        for (int k = 0; k < _s.SectorDistances.Count; k++)
        {
            double d = _s.SectorDistances[k];
            if (d <= _s.D0 || d >= _s.D1) continue;
            double x = Math.Round(X(d)) + 0.5;
            dc.DrawLine(sectorPen, new Point(x, 14), new Point(x, top));
            dc.DrawText(LapAnalysisView.Text(this, "S" + (k + 2), muted, 11.5, bold: true), new Point(x + 4, 1));
        }
        dc.DrawLine(strong, new Point(Left, top + 0.5), new Point(ActualWidth - Right, top + 0.5));
        double span = _s.D1 - _s.D0;
        double unit = _s.Imperial ? 1609.344 : 1000;
        double step = new[] { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2 }.Select(v => v * unit).FirstOrDefault(v => span / v <= 8, 5 * unit);
        for (double d = Math.Ceiling(_s.D0 / step) * step; d <= _s.D1; d += step)
        {
            double x = X(d);
            dc.DrawLine(strong, new Point(x, top), new Point(x, top + 4));
            var label = LapAnalysisView.Text(this, LapAnalysisView.DistanceText(d, _s.Imperial), muted, 11.5);
            if (x - label.Width / 2 >= Left - 4 && x + label.Width / 2 <= ActualWidth) dc.DrawText(label, new Point(x - label.Width / 2, top + 6));
        }
    }

    private (double Lo, double Hi) Range(string lane)
    {
        if (lane == "pedals") return (0, 100);
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var line in _s.Lines)
        {
            if (!line.Lanes.TryGetValue(lane, out var series)) continue;
            foreach (var v in new[] { series.A, series.B })
            {
                if (v is null) continue;
                int a = (int)(_s.D0 / line.Trace.StepM), b = Math.Min(v.Length - 1, (int)Math.Ceiling(_s.D1 / line.Trace.StepM));
                for (int i = a; i <= b; i++) { lo = Math.Min(lo, v[i]); hi = Math.Max(hi, v[i]); }
            }
        }
        if (lo > hi) return (0, 1);
        if (lane is "delta" or "steering" or "latg")
        {
            double m = Math.Max(Math.Max(Math.Abs(lo), Math.Abs(hi)), lane == "delta" ? 0.05 : 0.1) * 1.08;
            return (-m, m);
        }
        if (lane == "gear") return (Math.Min(0, lo), Math.Max(lo + 1, hi));
        double pad = Math.Max(1e-6, (hi - lo) * 0.06);
        return (lo - pad, hi + pad);
    }

    private static Pen PenFor(AnalysisLine line, bool second, string lane)
    {
        var brush = LapAnalysisView.Brush(line.BrushKey);
        if (line.Reference || second) { brush = brush.Clone(); brush.Opacity = line.Reference && second ? 0.45 : 0.7; }
        var pen = new Pen(brush, line.Reference ? 1.4 : 1.6) { LineJoin = PenLineJoin.Round };
        if (second || (line.Reference && lane != "pedals")) pen.DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0);
        pen.Freeze();
        return pen;
    }

    private StreamGeometry Decimated(double[] v, double step, Func<double, double> y, bool stepped)
    {
        var geo = new StreamGeometry();
        int a = Math.Max(0, (int)(_s.D0 / step) - 1), b = Math.Min(v.Length - 1, (int)Math.Ceiling(_s.D1 / step) + 1);
        if (b <= a) { geo.Freeze(); return geo; }
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(X(a * step), y(v[a])), false, false);
            double last = v[a];
            int column = int.MinValue;
            double min = 0, max = 0, minX = 0, maxX = 0;
            void Flush()
            {
                if (column == int.MinValue) return;
                if (minX <= maxX) { g.LineTo(new Point(minX, y(min)), true, true); if (max != min) g.LineTo(new Point(maxX, y(max)), true, true); }
                else { g.LineTo(new Point(maxX, y(max)), true, true); if (max != min) g.LineTo(new Point(minX, y(min)), true, true); }
            }
            for (int i = a + 1; i <= b; i++)
            {
                double x = X(i * step), value = v[i];
                if (stepped && value != last) g.LineTo(new Point(x, y(last)), true, true);
                last = value;
                int col = (int)x;
                if (col != column)
                {
                    Flush();
                    column = col;
                    min = max = value;
                    minX = maxX = x;
                }
                else
                {
                    if (value < min) { min = value; minX = x; }
                    if (value > max) { max = value; maxX = x; }
                }
            }
            Flush();
        }
        geo.Freeze();
        return geo;
    }
}

internal sealed class ChartCursor : FrameworkElement
{
    private readonly AnalysisState _s;
    private readonly TraceChart _chart;

    public ChartCursor(AnalysisState s, TraceChart chart)
    {
        _s = s;
        _chart = chart;
        s.CursorChanged += InvalidateVisual;
        s.ZoomChanged += InvalidateVisual;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double bottom = _chart.ActualHeight - TraceChart.Axis;
        if (_s.Selecting is { } sel)
        {
            double a = _chart.X(Math.Min(sel.From, sel.To)), b = _chart.X(Math.Max(sel.From, sel.To));
            var fill = LapAnalysisView.Brush("Accent").Clone();
            fill.Opacity = 0.14;
            dc.DrawRectangle(fill, new Pen(Ui.Res<Brush>("Accent"), 1), new Rect(a, 0, Math.Max(1, b - a), bottom));
        }
        if (_s.Cursor is not { } d || d < _s.D0 || d > _s.D1) return;
        double x = Math.Round(_chart.X(d)) + 0.5;
        dc.DrawLine(new Pen(Ui.Res<Brush>("Heading"), 1), new Point(x, 0), new Point(x, bottom));
        var panel = Ui.Res<Brush>("Surface");
        foreach (var (lane, (top, height, lo, hi)) in _chart.Layout)
        {
            var parts = new List<FormattedText>();
            foreach (var line in _s.Lines)
            {
                if (!line.Lanes.TryGetValue(lane, out var series) || series.A is not { } values) continue;
                int i = Math.Clamp((int)Math.Round(d / line.Trace.StepM), 0, values.Length - 1);
                string text = LapAnalysisView.Format(lane, values[i]);
                if (series.B is { } b && i < b.Length) text += " / " + LapAnalysisView.Format(lane, b[i]);
                if (line.BoostAt(d)) text += "  P2P";
                parts.Add(LapAnalysisView.Text(this, text, Ui.Res<Brush>(line.BrushKey), 15.5, bold: true));
            }
            if (parts.Count == 0) continue;
            double w = parts.Sum(p => p.Width) + 14 * (parts.Count - 1) + 16, h = parts.Max(p => p.Height) + 4;
            double left = x + 8 + w > ActualWidth ? x - 8 - w : x + 8;
            dc.DrawRectangle(panel, new Pen(Ui.Res<Brush>("LineStrong"), 1), new Rect(left, top - h - 1, w, h));
            double at = left + 8;
            foreach (var p in parts) { dc.DrawText(p, new Point(at, top - h + 1)); at += p.Width + 14; }
        }
        var dist = LapAnalysisView.Text(this, LapAnalysisView.DistanceText(d, _s.Imperial), Ui.Res<Brush>("Heading"), 12, bold: true);
        double dl = Math.Clamp(x - dist.Width / 2 - 4, 0, Math.Max(0, ActualWidth - dist.Width - 8));
        dc.DrawRectangle(panel, null, new Rect(dl, bottom + 3, dist.Width + 8, dist.Height + 2));
        dc.DrawText(dist, new Point(dl + 4, bottom + 4));
    }
}

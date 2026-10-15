using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.SessionStats;

namespace GoodieBag.Views.Pages;

internal sealed class ReplayMemory
{
    public double Distance;
    public string? Follow;
}

internal sealed class ReplayCar
{
    public required AnalysisLine Line { get; init; }
    public required double[] X { get; init; }
    public required double[] Y { get; init; }
    public required double[] Headings { get; init; }
    public double Distance, Px, Py, Heading;
    public LapTrace Trace => Line.Trace;
}

internal sealed class LapReplayView : Grid
{
    private const double MinStage = 420, MaxStage = 900;
    private const long ReadoutMs = 66;
    private readonly AnalysisState _s;
    private readonly Config.SessionStatsSettings _settings;
    private readonly ReplayMemory _memory;
    private readonly Action _save;
    private readonly List<ReplayCar> _cars = new();
    private readonly List<ReplayCard> _cards = new();
    private readonly double[] _sectorStarts;
    private readonly ReplayStage _stage = null!;
    private readonly ReplayMiniMap _mini = null!;
    private readonly ReplayScrubber _scrubber = null!;
    private readonly Button _play = null!;
    private readonly TextBlock _clock = new(), _gapSeconds = new(), _gapMetres = new(), _sectorName = new(), _sectorGap = new();
    private readonly SectorTable? _table;
    private readonly Slider _zoom = null!;
    private readonly Tile _stageTile = null!;
    private readonly Tile _barTile = null!;
    private ScrollViewer? _page;
    private ReplayCar _follow = null!;
    private double _time;
    private bool _playing, _hooked;
    private TimeSpan? _lastFrame;
    private long _lastReadout;
    private int _gapSign = 2, _sectorSign = 2, _sector = -1;

    public LapReplayView(AnalysisData data, AnalysisState s, Config.SessionStatsSettings settings, ReplayMemory memory, Action save)
    {
        _s = s;
        _settings = settings;
        _memory = memory;
        _save = save;
        _sectorStarts = new[] { 0.0 }.Concat(s.SectorDistances).ToArray();
        foreach (var line in s.Lines)
        {
            if (line.Line is not { } xy) continue;
            _cars.Add(new ReplayCar { Line = line, X = xy.X, Y = xy.Y, Headings = LapReplay.Headings(xy.X, xy.Y, 6) });
        }
        if (_cars.Count == 0)
        {
            Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Child = Ui.Text("These laps have no position data, so they can't be replayed.", "Muted") });
            return;
        }
        _follow = _cars.FirstOrDefault(c => c.Line.Label == memory.Follow) ?? _cars.FirstOrDefault(c => !c.Line.Reference) ?? _cars[0];
        _time = _follow.Trace.TimeAt(memory.Distance);
        Focusable = true;
        FocusVisualStyle = null;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _stage = new ReplayStage(_cars, s, settings, () => _follow);
        if (data.Map is { } map)
        {
            var lines = _cars.Select(c => (c.X, c.Y, c.Trace.StepM, c.Trace.LengthM)).ToList();
            _ = PlaceMapAsync(map, lines);
        }
        _stage.ZoomStep += delta => SetZoom(_settings.ReplayZoom + delta);
        var overlay = new Grid { Margin = new Thickness(14) };

        var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Orientation = Orientation.Horizontal };
        foreach (var car in _cars.OrderBy(c => c.Line.Reference ? 1 : 0))
        {
            var card = new ReplayCard(car, settings.Imperial, () => Follow(car));
            card.Margin = new Thickness(8, 0, 0, 8);
            _cards.Add(card);
            cards.Children.Add(card);
        }
        var track = _cars.FirstOrDefault(c => c.Line.Reference) ?? _follow;
        _mini = new ReplayMiniMap((track.X, track.Y), track.Trace.StepM, _sectorStarts) { Width = 230, Height = 150 };
        _sectorName.Style = Ui.Res<Style>("Caption");
        _sectorGap.Style = Ui.Res<Style>("Body");
        _sectorGap.FontWeight = FontWeights.SemiBold;
        _sectorGap.HorizontalAlignment = HorizontalAlignment.Right;
        Typography.SetNumeralAlignment(_sectorGap, FontNumeralAlignment.Tabular);
        var miniFoot = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        miniFoot.Children.Add(_sectorName);
        miniFoot.Children.Add(_sectorGap);
        var miniCard = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(12, 10, 12, 10), HorizontalAlignment = HorizontalAlignment.Right, Child = new StackPanel { Children = { _mini, miniFoot } } };
        miniCard.ToolTip = "Time gained or lost on the reference since this sector began";
        var topRight = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(120, 0, 0, 0) };
        topRight.Children.Add(cards);
        topRight.Children.Add(miniCard);
        overlay.Children.Add(topRight);

        if (data.SectorStarts.Count > 1)
        {
            _table = new SectorTable(_cars, data.SectorStarts);
            overlay.Children.Add(new Tile
            {
                Style = Ui.Res<Style>("Card"), Padding = new Thickness(14, 10, 14, 10), Child = _table,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            });
        }

        _zoom = new Slider { Minimum = 0, Maximum = 1, Value = settings.ReplayZoom, Width = 150, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        _zoom.ValueChanged += (_, e) => SetZoom(e.NewValue);
        _zoom.ToolTip = "Zoom (the wheel over the track too); all the way out = the whole lap";
        var turn = Ui.LabeledButton("I.Rotate", "Turn with car", () => { });
        Ui.SetToggled(turn, settings.ReplayTurn);
        turn.Click += (_, _) => { settings.ReplayTurn = !settings.ReplayTurn; Ui.SetToggled(turn, settings.ReplayTurn); _stage.Update(); _save(); };
        turn.ToolTip = "Keep the followed car pointing up (off = north up)";
        turn.Margin = new Thickness(10, 0, 0, 0);
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal };
        zoomRow.Children.Add(Ui.IconButton("I.Minus", "Zoom out", () => SetZoom(_settings.ReplayZoom - 0.1), "QuietIconButton"));
        zoomRow.Children.Add(_zoom);
        zoomRow.Children.Add(Ui.IconButton("I.Plus", "Zoom in", () => SetZoom(_settings.ReplayZoom + 0.1), "QuietIconButton"));
        zoomRow.Children.Add(turn);
        overlay.Children.Add(new Tile
        {
            Style = Ui.Res<Style>("Card"), Padding = new Thickness(8, 6, 8, 6), Child = zoomRow,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
        });

        var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        foreach (var (label, key) in new[] { ("Throttle", StatsUi.Hue("Sage").Deep), ("Coasting", "Heading"), ("Brake", "Danger") })
        {
            var sw = new Border { Width = 14, Height = 3, Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            sw.SetResourceReference(Border.BackgroundProperty, key);
            legend.Children.Add(sw);
            legend.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
        }
        legend.ToolTip = "The followed lap's line; the other laps are dotted in their colors";
        overlay.Children.Add(legend);

        var stageHost = new Grid { ClipToBounds = true, Children = { _stage, overlay } };
        _stageTile = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Height = 560, Child = stageHost, Margin = new Thickness(0, 0, 0, 12) };
        var stageTile = _stageTile;
        Children.Add(stageTile);

        _scrubber = new ReplayScrubber(_follow.Trace.LapTime > 0 ? _sectorStarts.Skip(1).Select(d => _follow.Trace.TimeAt(d) / _follow.Trace.LapTime).ToArray() : Array.Empty<double>())
        {
            Height = 26, Margin = new Thickness(0, 0, 0, 10),
        };
        _scrubber.Seeking += share => { Pause(); Seek(share * _follow.Trace.LapTime); };

        _play = Ui.IconButton("I.Play", "Play / pause (Space)", Toggle, "PrimaryButton");
        _clock.Style = Ui.Res<Style>("H3");
        _clock.VerticalAlignment = VerticalAlignment.Center;
        _clock.Margin = new Thickness(14, 0, 0, 0);
        _clock.MinWidth = 150;
        Typography.SetNumeralAlignment(_clock, FontNumeralAlignment.Tabular);
        var speeds = LapReplay.Speeds.Select(v => new Choice(v.ToString(CultureInfo.InvariantCulture), v.ToString("0.##", CultureInfo.CurrentCulture) + "×")).ToList();
        var speed = Ui.Combo(speeds, settings.ReplaySpeed.ToString(CultureInfo.InvariantCulture), c =>
        {
            if (double.TryParse(c.Id, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) { _settings.ReplaySpeed = v; _save(); }
        }, 90);
        speed.ToolTip = "Playback speed";
        speed.Margin = new Thickness(10, 0, 0, 0);
        speed.VerticalAlignment = VerticalAlignment.Center;
        var left = new StackPanel { Orientation = Orientation.Horizontal, Children = { _play, _clock, speed } };

        foreach (var t in new[] { _gapSeconds, _gapMetres })
        {
            t.Style = Ui.Res<Style>("Body");
            t.FontWeight = FontWeights.SemiBold;
            t.VerticalAlignment = VerticalAlignment.Center;
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        }
        _gapSeconds.MinWidth = 64;
        _gapMetres.MinWidth = 64;
        _gapMetres.Margin = new Thickness(10, 0, 0, 0);
        var gapLabel = new TextBlock { Text = "Delta", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var gap = new StackPanel { Orientation = Orientation.Horizontal, Children = { gapLabel, _gapSeconds, _gapMetres } };
        gap.ToolTip = "Behind (+) or ahead (−) of the reference: in time at this spot, and in distance after the same time";
        var refSwitch = Ui.Switch(settings.ReplayShowReference, on => { _settings.ReplayShowReference = on; _stage.Update(); _save(); });
        var refRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        refRow.Children.Add(new TextBlock { Text = "Reference", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        refRow.Children.Add(refSwitch);
        refRow.ToolTip = "Show the reference lap's line and car";
        var sync = Ui.Segmented(new[] { "Distance", "Time" }, settings.ReplaySync == "time" ? 1 : 0, i =>
        {
            _settings.ReplaySync = i == 1 ? "time" : "distance";
            _save();
            Place(force: true);
        }, "replaySync", new[] { "Every car at the same spot: the gap shows in time", "Every car after the same time since the line: they race, the gap shows on track" });
        sync.Margin = new Thickness(18, 0, 0, 0);
        sync.VerticalAlignment = VerticalAlignment.Center;
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { gap, refRow, sync } };

        var bar = new StackPanel();
        bar.Children.Add(_scrubber);
        bar.Children.Add(Ui.SplitRow(left, right, 16));
        _barTile = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(20, 14, 20, 14), Child = bar };
        SetRow(_barTile, 1);
        Children.Add(_barTile);

        PreviewMouseDown += (_, e) => { if (e.OriginalSource is not TextBox && !IsKeyboardFocusWithin) Focus(); };
        PreviewKeyDown += OnKey;
        IsVisibleChanged += (_, _) => { if (!IsVisible) Pause(); };
        Unloaded += (_, _) =>
        {
            Pause();
            if (_page is not null) _page.SizeChanged -= FitStage;
            _page = null;
        };
        Loaded += (_, _) =>
        {
            _page = Ancestor<ScrollViewer>(this);
            if (_page is not null) _page.SizeChanged += FitStage;
            Dispatcher.InvokeAsync(() => FitStage(null, null), System.Windows.Threading.DispatcherPriority.Loaded);
            Place(force: true);
        };
        _stage.SizeChanged += (_, _) => _stage.Update();
        Place(force: true);
    }

    public TrackOutline? Outline { get; private set; }

    private async Task PlaceMapAsync(SimRacing.TrackMaps.TrackMap map, List<(double[] X, double[] Y, double Step, double Length)> lines)
    {
        TrackOutline? outline;
        try { outline = await Task.Run(() => TrackFit.Fit(map, lines)); }
        catch (Exception ex) { App.LogError(ex); return; }
        if (outline is null) return;
        Outline = outline;
        _stage.SetOutline(outline);
    }

    private static T? Ancestor<T>(DependencyObject from) where T : DependencyObject
    {
        for (var at = VisualTreeHelper.GetParent(from); at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is T found) return found;
        return null;
    }

    private void FitStage(object? sender, SizeChangedEventArgs? e)
    {
        if (_page is null || !_stageTile.IsLoaded) return;
        var toPage = _stageTile.TransformToAncestor(_page);
        double unit = Math.Max(0.1, toPage.Transform(new Point(0, 100)).Y - toPage.Transform(new Point(0, 0)).Y) / 100;
        double room = _page.ViewportHeight / unit - _barTile.ActualHeight - _stageTile.Margin.Bottom - 28;
        _stageTile.Height = Math.Clamp(room, MinStage, MaxStage);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1;
        switch (e.Key)
        {
            case Key.Space: Toggle(); break;
            case Key.Left: Seek(_time - step); break;
            case Key.Right: Seek(_time + step); break;
            case Key.Home: Seek(0); break;
            case Key.End: Seek(_follow.Trace.LapTime); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Toggle()
    {
        if (_playing) Pause();
        else Play();
    }

    public void Play()
    {
        if (_cars.Count == 0) return;
        if (_time >= _follow.Trace.LapTime - 0.01) _time = 0;
        _playing = true;
        ((Icon)_play.Content).Data = Ui.Res<Geometry>("I.Pause");
        if (!_hooked) { CompositionTarget.Rendering += OnFrame; _hooked = true; }
        _lastFrame = null;
    }

    public void Pause()
    {
        if (_hooked) { CompositionTarget.Rendering -= OnFrame; _hooked = false; }
        if (!_playing) return;
        _playing = false;
        ((Icon)_play.Content).Data = Ui.Res<Geometry>("I.Play");
        Place(force: true);
    }

    public void Seek(double time)
    {
        if (_cars.Count == 0) return;
        _time = Math.Clamp(time, 0, _follow.Trace.LapTime);
        Place(force: true);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs r) return;
        if (_lastFrame is not { } last) { _lastFrame = r.RenderingTime; return; }
        double dt = (r.RenderingTime - last).TotalSeconds;
        if (dt < 0.015) return;
        _lastFrame = r.RenderingTime;
        var (t, ended) = LapReplay.Advance(_time, Math.Min(dt, 0.1), _settings.ReplaySpeed, _follow.Trace.LapTime);
        _time = t;
        Place();
        if (ended) Pause();
    }

    private void Follow(ReplayCar car)
    {
        if (car == _follow) return;
        double d = _follow.Distance;
        _follow = car;
        _memory.Follow = car.Line.Label;
        _time = car.Trace.TimeAt(d);
        _scrubber.SetMarks(car.Trace.LapTime > 0 ? _sectorStarts.Skip(1).Select(x => car.Trace.TimeAt(x) / car.Trace.LapTime).ToArray() : Array.Empty<double>());
        Place(force: true);
    }

    private void SetZoom(double z)
    {
        z = Math.Clamp(z, 0, 1);
        if (Math.Abs(_zoom.Value - z) > 1e-9) { _zoom.Value = z; return; }
        _settings.ReplayZoom = z;
        _stage.Update();
        _save();
    }

    private void Place(bool force = false)
    {
        if (_cars.Count == 0) return;
        var sync = _settings.ReplaySync == "time" ? ReplaySync.Time : ReplaySync.Distance;
        foreach (var car in _cars)
        {
            var t = car.Trace;
            car.Distance = car == _follow ? _follow.Trace.DistanceAtTime(_time) : LapReplay.DistanceOf(t, _follow.Trace, _time, sync);
            car.Px = LapReplay.At(car.X, t.StepM, car.Distance);
            car.Py = LapReplay.At(car.Y, t.StepM, car.Distance);
            car.Heading = LapReplay.HeadingAt(car.Headings, t.StepM, car.Distance);
        }
        _memory.Distance = _follow.Distance;
        _stage.Update();
        _mini.Move(_follow.Px, _follow.Py);
        _scrubber.Value = _follow.Trace.LapTime > 0 ? _time / _follow.Trace.LapTime : 0;
        long now = Environment.TickCount64;
        if (!force && now - _lastReadout < ReadoutMs) return;
        _lastReadout = now;
        Readouts();
    }

    private void Readouts()
    {
        _clock.Text = StatFormat.LapTime(_time) + " / " + StatFormat.LapTime(_follow.Trace.LapTime);
        foreach (var card in _cards) card.Update(card.Car == _follow, _settings.ReplayShowReference || !card.Car.Line.Reference);
        var compared = _follow.Line.Reference ? _cars.FirstOrDefault(c => !c.Line.Reference) : _follow;
        int sector = LapReplay.SectorAt(_sectorStarts, _follow.Distance);
        if (sector != _sector)
        {
            _sector = sector;
            _mini.SetSector(sector);
            _table?.SetCurrent(sector);
            _sectorName.Text = _sectorStarts.Length > 1 ? $"Sector {sector + 1}" : "Lap";
        }
        if (compared is null)
        {
            _gapSeconds.Text = _gapMetres.Text = _sectorGap.Text = RaceFormatMissing;
            Tone(_gapSeconds, 0, ref _gapSign);
            Tone(_sectorGap, 0, ref _sectorSign);
            return;
        }
        var reference = _s.Reference.Trace;
        var (seconds, metres) = LapReplay.Gap(compared.Trace, reference, compared.Distance);
        _gapSeconds.Text = Signed(seconds, "0.000") + " s";
        _gapMetres.Text = _settings.Imperial ? Signed(metres * 3.28084, "0") + " ft" : Signed(metres, "0") + " m";
        Tone(_gapSeconds, seconds, ref _gapSign);
        _gapMetres.Foreground = _gapSeconds.Foreground;
        double sectorGap = LapReplay.SectorGap(compared.Trace, reference, _sectorStarts, compared.Distance);
        _sectorGap.Text = Signed(sectorGap, "0.000");
        Tone(_sectorGap, sectorGap, ref _sectorSign);
    }

    private const string RaceFormatMissing = GoodieBag.SimRacing.Race.RaceFormat.Missing;

    internal static string Signed(double v, string format) =>
        (v < 0 ? "−" : "+") + Math.Abs(v).ToString(format, CultureInfo.CurrentCulture);

    private static void Tone(TextBlock text, double value, ref int last)
    {
        int sign = Math.Abs(value) < 0.0005 ? 0 : Math.Sign(value);
        if (sign == last) return;
        last = sign;
        text.SetResourceReference(TextBlock.ForegroundProperty, sign > 0 ? StatsUi.Hue("Coral").Deep : sign < 0 ? StatsUi.Hue("Sage").Deep : "Heading");
    }
}

internal sealed class ReplayStage : FrameworkElement, IKeepsWheel
{
    public const double TrackWidth = 14;
    private const double MaxScale = 22;
    private const int Buckets = 16;
    private readonly List<ReplayCar> _cars;
    private readonly AnalysisState _s;
    private readonly Config.SessionStatsSettings _settings;
    private readonly Func<ReplayCar> _follow;
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _world = new();
    private readonly MatrixTransform _camera = new();
    private readonly DrawingVisual[] _carVisuals;
    private readonly MatrixTransform[] _carMatrix;
    private readonly StreamGeometry[] _paths;
    private StreamGeometry _ticks;
    private TrackOutline? _outline;
    private StreamGeometry? _surface, _pitFill, _pitLines, _start;
    private readonly double _midX, _midY, _spanX, _spanY;
    private double _builtScale = -1;
    private ReplayCar? _builtFollow;
    private bool _builtRef;

    public event Action<double>? ZoomStep;

    public ReplayStage(List<ReplayCar> cars, AnalysisState s, Config.SessionStatsSettings settings, Func<ReplayCar> follow)
    {
        _cars = cars;
        _s = s;
        _settings = settings;
        _follow = follow;
        ClipToBounds = true;
        _visuals = new VisualCollection(this) { _world };
        _world.Transform = _camera;
        _paths = cars.Select(c => Path(c.X, c.Y)).ToArray();
        _carVisuals = cars.Select(_ => new DrawingVisual()).ToArray();
        _carMatrix = cars.Select(_ => new MatrixTransform()).ToArray();
        for (int i = 0; i < cars.Count; i++)
        {
            _carVisuals[i].Transform = _carMatrix[i];
            _visuals.Add(_carVisuals[i]);
        }
        var track = Track;
        double minX = track.X.Min(), maxX = track.X.Max(), minY = track.Y.Min(), maxY = track.Y.Max();
        (_midX, _midY, _spanX, _spanY) = ((minX + maxX) / 2, (minY + maxY) / 2, Math.Max(50, maxX - minX), Math.Max(50, maxY - minY));
        _ticks = Ticks(track, _s.Reference.Trace.StepM, TrackWidth);
    }

    public void SetOutline(TrackOutline outline)
    {
        _outline = outline;
        _surface = Shapes(outline.Surface.Select(f => (f.X, f.Y, true)), FillRule.EvenOdd);
        _pitFill = Shapes(outline.Pit.Where(p => p.Filled), FillRule.Nonzero);
        _pitLines = Shapes(outline.Pit.Where(p => !p.Filled), FillRule.Nonzero);
        _start = null;
        if (outline.StartLine is { } sf)
        {
            _start = new StreamGeometry();
            using (var g = _start.Open()) { g.BeginFigure(new Point(sf.X1, sf.Y1), false, false); g.LineTo(new Point(sf.X2, sf.Y2), true, false); }
            _start.Freeze();
        }
        _ticks = Ticks(Track, _s.Reference.Trace.StepM, outline.Width);
        _builtScale = -1;
        Update();
    }

    private static StreamGeometry Shapes(IEnumerable<(double[] X, double[] Y, bool Closed)> figures, FillRule rule)
    {
        var geo = new StreamGeometry { FillRule = rule };
        using (var g = geo.Open())
            foreach (var (x, y, closed) in figures)
            {
                if (x.Length < 2) continue;
                g.BeginFigure(new Point(x[0], y[0]), closed, closed);
                for (int i = 1; i < x.Length; i++) g.LineTo(new Point(x[i], y[i]), true, true);
            }
        geo.Freeze();
        return geo;
    }

    private ReplayCar Track => _cars.FirstOrDefault(c => c.Line.Reference) ?? _cars[0];

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override void OnRender(DrawingContext dc) => dc.DrawRectangle(Ui.Res<Brush>("Bg"), null, new Rect(RenderSize));

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomStep?.Invoke(e.Delta > 0 ? 0.06 : -0.06);
        e.Handled = true;
    }

    private double Scale(double zoom)
    {
        double fit = Math.Min((ActualWidth - 60) / _spanX, (ActualHeight - 60) / _spanY);
        fit = Math.Max(0.01, fit);
        return fit * Math.Pow(Math.Max(1, MaxScale / fit), zoom);
    }

    public void Update()
    {
        if (ActualWidth < 20 || ActualHeight < 20) return;
        var follow = _follow();
        double zoom = _settings.ReplayZoom, scale = Scale(zoom);
        if (Math.Abs(scale / _builtScale - 1) > 0.004 || follow != _builtFollow || _settings.ReplayShowReference != _builtRef) Build(scale, follow);

        double k = Math.Clamp(zoom / 0.15, 0, 1);
        double cx = _midX + (follow.Px - _midX) * k, cy = _midY + (follow.Py - _midY) * k;
        double rot = _settings.ReplayTurn ? Math.IEEERemainder(follow.Heading - Math.PI / 2, Math.Tau) * k : 0;
        var m = Matrix.Identity;
        m.Translate(-cx, -cy);
        m.Scale(scale, -scale);
        m.Rotate(rot * 180 / Math.PI);
        m.Translate(ActualWidth / 2, ActualHeight * (0.5 + 0.08 * k));
        _camera.Matrix = m;

        for (int i = 0; i < _cars.Count; i++)
        {
            var car = _cars[i];
            bool shown = _settings.ReplayShowReference || !car.Line.Reference || car == follow;
            _carVisuals[i].Opacity = shown ? 1 : 0;
            var p = m.Transform(new Point(car.Px, car.Py));
            var cm = Matrix.Identity;
            cm.Rotate((rot - car.Heading + Math.PI / 2) * 180 / Math.PI);
            cm.Translate(p.X, p.Y);
            _carMatrix[i].Matrix = cm;
        }
    }

    private void Build(double scale, ReplayCar follow)
    {
        _builtScale = scale;
        _builtFollow = follow;
        _builtRef = _settings.ReplayShowReference;
        double px = 1 / scale;
        using (var dc = _world.RenderOpen())
        {
            if (_outline is not null)
            {
                var rim = new Pen(Ui.Res<Brush>("LineStrong"), 2 * px) { LineJoin = PenLineJoin.Round };
                rim.Freeze();
                var pitBrush = Ui.Res<Brush>("SurfaceAlt").Clone();
                pitBrush.Opacity = 0.55;
                pitBrush.Freeze();
                if (_pitFill is not null) dc.DrawGeometry(pitBrush, null, _pitFill);
                if (_pitLines is not null && _outline.PitWidth > 0)
                {
                    var pit = new Pen(pitBrush, _outline.PitWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    pit.Freeze();
                    dc.DrawGeometry(null, pit, _pitLines);
                }
                dc.DrawGeometry(Ui.Res<Brush>("SurfaceAlt"), rim, _surface);
                if (_start is not null)
                {
                    var sf = new Pen(Ui.Res<Brush>("Heading"), 3 * px);
                    sf.Freeze();
                    dc.DrawGeometry(null, sf, _start);
                }
            }
            else
            {
                int track = _cars.IndexOf(Track);
                var edge = new Pen(Ui.Res<Brush>("LineStrong"), TrackWidth + 2 * px) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                var surface = new Pen(Ui.Res<Brush>("SurfaceAlt"), TrackWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                edge.Freeze();
                surface.Freeze();
                dc.DrawGeometry(null, edge, _paths[track]);
                dc.DrawGeometry(null, surface, _paths[track]);
            }
            var tick = new Pen(Ui.Res<Brush>("TextMuted"), 2 * px);
            tick.Freeze();
            dc.DrawGeometry(null, tick, _ticks);
            for (int i = 0; i < _cars.Count; i++)
            {
                var car = _cars[i];
                if (car == follow || (car.Line.Reference && !_settings.ReplayShowReference)) continue;
                var pen = new Pen(Ui.Res<Brush>(car.Line.BrushKey), 2 * px) { DashStyle = new DashStyle(new[] { 1.0, 2.0 }, 0), DashCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                pen.Freeze();
                dc.DrawGeometry(null, pen, _paths[i]);
            }
            DrawPedals(dc, follow, 3.5 * px);
        }
        double carPx = Math.Clamp(4.6 * scale, 22, 84);
        for (int i = 0; i < _cars.Count; i++)
        {
            using var dc = _carVisuals[i].RenderOpen();
            CarShape.Draw(dc, Ui.Res<Brush>(_cars[i].Line.BrushKey), carPx, _cars[i] == follow);
        }
        int at = _cars.IndexOf(follow);
        _visuals.Remove(_carVisuals[at]);
        _visuals.Add(_carVisuals[at]);
    }

    private static void DrawPedals(DrawingContext dc, ReplayCar car, double width)
    {
        var t = car.Trace;
        if (t[TraceChannel.Throttle] is not { } throttle || t[TraceChannel.Brake] is not { } brake)
        {
            var plain = new Pen(Ui.Res<Brush>(car.Line.BrushKey), width) { LineJoin = PenLineJoin.Round };
            plain.Freeze();
            dc.DrawGeometry(null, plain, Path(car.X, car.Y));
            return;
        }
        Color brakeC = LapAnalysisView.Brush("Danger").Color, coast = LapAnalysisView.Brush("Heading").Color, gas = LapAnalysisView.Brush(StatsUi.Hue("Sage").Deep).Color;
        var geos = new StreamGeometry?[Buckets];
        var ctx = new StreamGeometryContext?[Buckets];
        int n = Math.Min(car.X.Length, throttle.Length);
        int run = -1;
        for (int i = 0; i + 1 < n; i++)
        {
            double u = (LapReplay.Pedal(throttle[i], brake[i]) + 1) / 2;
            int k = (int)Math.Round(u * (Buckets - 1));
            if (ctx[k] is null) { geos[k] = new StreamGeometry(); ctx[k] = geos[k]!.Open(); }
            if (k != run) ctx[k]!.BeginFigure(new Point(car.X[i], car.Y[i]), false, false);
            ctx[k]!.LineTo(new Point(car.X[i + 1], car.Y[i + 1]), true, true);
            run = k;
        }
        for (int k = 0; k < Buckets; k++)
        {
            if (ctx[k] is not { } c) continue;
            c.Close();
            geos[k]!.Freeze();
            double u = k / (Buckets - 1.0);
            var color = u < 0.5 ? LapAnalysisView.Mix(brakeC, coast, u * 2) : LapAnalysisView.Mix(coast, gas, u * 2 - 1);
            var pen = new Pen(new SolidColorBrush(color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geos[k]);
        }
    }

    private static StreamGeometry Path(double[] x, double[] y)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(x[0], y[0]), false, false);
            for (int i = 1; i < x.Length; i++) g.LineTo(new Point(x[i], y[i]), true, true);
        }
        geo.Freeze();
        return geo;
    }

    private StreamGeometry Ticks(ReplayCar track, double step, double width)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            foreach (double d in _s.SectorDistances.Prepend(0))
            {
                double x = LapReplay.At(track.X, step, d), y = LapReplay.At(track.Y, step, d), h = LapReplay.HeadingAt(track.Headings, step, d);
                double nx = -Math.Sin(h) * width / 2, ny = Math.Cos(h) * width / 2;
                g.BeginFigure(new Point(x - nx, y - ny), false, false);
                g.LineTo(new Point(x + nx, y + ny), true, false);
            }
        }
        geo.Freeze();
        return geo;
    }
}

internal static class CarShape
{
    private static readonly Geometry Body = Geometry.Parse(
        "M-0.06,-0.5 L0.06,-0.5 L0.17,-0.42 L0.2,-0.28 L0.2,-0.12 L0.22,0.02 L0.22,0.3 L0.2,0.42 L0.1,0.47 L-0.1,0.47 L-0.2,0.42 L-0.22,0.3 L-0.22,0.02 L-0.2,-0.12 L-0.2,-0.28 L-0.17,-0.42 Z");
    private static readonly Geometry Cabin = Geometry.Parse("M-0.12,-0.12 L0.12,-0.12 L0.14,0.02 L0.13,0.16 L-0.13,0.16 L-0.14,0.02 Z M-0.1,0.16 L-0.08,0.26 L0.08,0.26 L0.1,0.16");
    private static readonly Geometry Wing = Geometry.Parse("M-0.24,0.43 L0.24,0.43 L0.24,0.5 L-0.24,0.5 Z M-0.06,0.43 L-0.06,0.36 M0.06,0.43 L0.06,0.36");
    private static readonly Geometry Wheels = Geometry.Parse(
        "M-0.25,-0.36 h0.05 v0.15 h-0.05 Z M0.2,-0.36 h0.05 v0.15 h-0.05 Z M-0.26,0.14 h0.05 v0.16 h-0.05 Z M0.21,0.14 h0.05 v0.16 h-0.05 Z");

    static CarShape()
    {
        Body.Freeze();
        Cabin.Freeze();
        Wing.Freeze();
        Wheels.Freeze();
    }

    public static void Draw(DrawingContext dc, Brush brush, double length, bool strong)
    {
        dc.PushTransform(new ScaleTransform(length, length));
        double px = 1 / length;
        var fill = brush.Clone();
        fill.Opacity = strong ? 0.28 : 0.16;
        fill.Freeze();
        var pen = new Pen(brush, (strong ? 1.6 : 1.2) * px) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        var dark = new Pen(Ui.Res<Brush>("Bg"), 3.5 * px) { LineJoin = PenLineJoin.Round };
        dark.Freeze();
        dc.DrawGeometry(null, dark, Body);
        dc.DrawGeometry(fill, pen, Body);
        dc.DrawGeometry(null, pen, Cabin);
        dc.DrawGeometry(fill, pen, Wing);
        dc.DrawGeometry(brush, null, Wheels);
        dc.Pop();
    }
}

internal sealed class ReplayCard : Tile
{
    private readonly bool _imperial;
    private readonly ScaleTransform _throttle = new(0, 1), _brake = new(0, 1);
    private readonly TextBlock _speed = new(), _gear = new(), _rpm = new();
    private int _lastSpeed = int.MinValue, _lastGear = int.MinValue, _lastRpm = int.MinValue;
    private bool? _followed, _shown;

    public ReplayCar Car { get; }

    public ReplayCard(ReplayCar car, bool imperial, Action follow)
    {
        Car = car;
        _imperial = imperial;
        Style = Ui.Res<Style>("Card");
        Padding = new Thickness(12, 8, 12, 8);
        Width = 204;
        Cursor = Cursors.Hand;
        ToolTip = "Follow this car";
        MouseLeftButtonUp += (_, e) => { follow(); e.Handled = true; };

        var swatch = new Border { Width = 4, Margin = new Thickness(0, 0, 10, 0) };
        swatch.SetResourceReference(Border.BackgroundProperty, car.Line.BrushKey);
        var name = new TextBlock { Text = car.Line.Driver.Length > 0 ? car.Line.Driver : car.Line.Short, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        var sub = new TextBlock { Text = car.Line.Driver.Length > 0 ? car.Line.Short : "", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        if (car.Line.Reference) sub.Text = sub.Text.Length > 0 ? sub.Text + " · reference" : "Reference";
        sub.ToolTip = sub.Text;
        var time = new TextBlock { Text = StatFormat.LapTime(car.Trace.LapTime), Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top };
        Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        var head = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(swatch, Dock.Left);
        DockPanel.SetDock(time, Dock.Right);
        head.Children.Add(swatch);
        head.Children.Add(time);
        head.Children.Add(new StackPanel { Children = { name, sub } });

        var pedals = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        pedals.ColumnDefinitions.Add(new ColumnDefinition());
        pedals.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        pedals.ColumnDefinitions.Add(new ColumnDefinition());
        pedals.Children.Add(Bar("Throttle", StatsUi.Hue("Sage").Deep, _throttle));
        var brakeBar = Bar("Brake", "Danger", _brake);
        Grid.SetColumn(brakeBar, 2);
        pedals.Children.Add(brakeBar);

        var values = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
        values.Children.Add(Value(_speed, imperial ? "mph" : "km/h"));
        values.Children.Add(Value(_gear, "Gear"));
        values.Children.Add(Value(_rpm, "RPM"));
        Child = new StackPanel { Children = { head, pedals, values } };
    }

    private static FrameworkElement Bar(string label, string brush, ScaleTransform scale)
    {
        var fill = new Border { Height = 4, RenderTransform = scale, HorizontalAlignment = HorizontalAlignment.Stretch };
        fill.SetResourceReference(Border.BackgroundProperty, brush);
        var track = new Border { Height = 4, Child = fill, Margin = new Thickness(0, 4, 0, 0) };
        track.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        return new StackPanel { Children = { new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), FontSize = 11 }, track } };
    }

    private static FrameworkElement Value(TextBlock value, string label)
    {
        value.Style = Ui.Res<Style>("Body");
        value.FontWeight = FontWeights.SemiBold;
        value.Text = "–";
        Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        return new StackPanel { Children = { value, new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), FontSize = 11 } } };
    }

    public void Update(bool followed, bool shown)
    {
        if (_followed != followed)
        {
            _followed = followed;
            if (followed) SetResourceReference(BorderBrushProperty, "AccentText");
            else ClearValue(BorderBrushProperty);
        }
        if (_shown != shown) { _shown = shown; Opacity = shown ? 1 : 0.5; }
        var t = Car.Trace;
        double d = Car.Distance;
        _throttle.ScaleX = t.At(TraceChannel.Throttle, d) ?? 0;
        _brake.ScaleX = t.At(TraceChannel.Brake, d) ?? 0;
        int speed = t.At(TraceChannel.Speed, d) is { } v ? (int)Math.Round(v * (_imperial ? 2.236936 : 3.6)) : int.MinValue;
        if (speed != _lastSpeed) { _lastSpeed = speed; _speed.Text = speed == int.MinValue ? "–" : speed.ToString(CultureInfo.CurrentCulture); }
        int gear = t.At(TraceChannel.Gear, d) is { } g ? (int)Math.Round(g) : int.MinValue;
        if (gear != _lastGear) { _lastGear = gear; _gear.Text = gear == int.MinValue ? "–" : LapAnalysisView.Format("gear", gear); }
        int rpm = t.At(TraceChannel.Rpm, d) is { } r ? (int)Math.Round(r / 10) * 10 : int.MinValue;
        if (rpm != _lastRpm) { _lastRpm = rpm; _rpm.Text = rpm == int.MinValue ? "–" : rpm.ToString(CultureInfo.CurrentCulture); }
    }
}

internal sealed class ReplayMiniMap : FrameworkElement
{
    private const double Pad = 12;
    private readonly double[] _x, _y, _starts;
    private readonly double _step, _minX, _maxY, _spanX, _spanY;
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _dot = new();
    private readonly TranslateTransform _dotAt = new();
    private int _sector;
    private double _scale, _ox, _oy, _lastX, _lastY;

    public ReplayMiniMap((double[] X, double[] Y) line, double step, double[] starts)
    {
        (_x, _y) = line;
        _step = step;
        _starts = starts;
        _minX = _x.Min();
        _maxY = _y.Max();
        _spanX = Math.Max(10, _x.Max() - _minX);
        _spanY = Math.Max(10, _maxY - _y.Min());
        _visuals = new VisualCollection(this) { _dot };
        _dot.Transform = _dotAt;
        using (var dc = _dot.RenderOpen()) dc.DrawEllipse(Ui.Res<Brush>("AccentText"), new Pen(Ui.Res<Brush>("Surface"), 2), new Point(0, 0), 5, 5);
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    private Point At(double x, double y) => new(_ox + (x - _minX) * _scale, _oy + (_maxY - y) * _scale);

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Fit();
        InvalidateVisual();
    }

    private void Fit()
    {
        double w = ActualWidth - 2 * Pad, h = ActualHeight - 2 * Pad;
        _scale = Math.Max(0.001, Math.Min(w / _spanX, h / _spanY));
        _ox = Pad + (w - _spanX * _scale) / 2;
        _oy = Pad + (h - _spanY * _scale) / 2;
        Move(_lastX, _lastY);
    }

    public void SetSector(int sector)
    {
        _sector = sector;
        InvalidateVisual();
    }

    public void Move(double x, double y)
    {
        _lastX = x;
        _lastY = y;
        var p = At(x, y);
        _dotAt.X = p.X;
        _dotAt.Y = p.Y;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 2 * Pad + 10) return;
        Fit();
        var line = new Pen(Ui.Res<Brush>("LineStrong"), 2) { LineJoin = PenLineJoin.Round };
        line.Freeze();
        dc.DrawGeometry(null, line, Stretch(0, _x.Length - 1));
        if (_starts.Length > 1)
        {
            int a = (int)(_starts[_sector] / _step), b = _sector + 1 < _starts.Length ? (int)(_starts[_sector + 1] / _step) : _x.Length - 1;
            var lit = new Pen(Ui.Res<Brush>("AccentText"), 3) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            lit.Freeze();
            dc.DrawGeometry(null, lit, Stretch(Math.Clamp(a, 0, _x.Length - 1), Math.Clamp(b, 0, _x.Length - 1)));
            var edge = new Pen(Ui.Res<Brush>("LineStrong"), 1.5);
            edge.Freeze();
            for (int k = 0; k < _starts.Length; k++)
            {
                int i = Math.Clamp((int)(_starts[k] / _step), 0, _x.Length - 1);
                var p = At(_x[i], _y[i]);
                bool on = k == _sector;
                dc.DrawEllipse(Ui.Res<Brush>(on ? "AccentText" : "Surface"), edge, p, 8, 8);
                var n = LapAnalysisView.Text(this, (k + 1).ToString(CultureInfo.CurrentCulture), Ui.Res<Brush>(on ? "OnAccent" : "Heading"), 10, bold: true);
                dc.DrawText(n, new Point(p.X - n.Width / 2, p.Y - n.Height / 2));
            }
        }
    }

    private StreamGeometry Stretch(int a, int b)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(At(_x[a], _y[a]), false, false);
            for (int i = a + 1; i <= b; i++) g.LineTo(At(_x[i], _y[i]), true, true);
        }
        geo.Freeze();
        return geo;
    }
}

internal sealed class ReplayScrubber : FrameworkElement
{
    private const double Thumb = 7;
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _fill = new(), _thumb = new();
    private readonly ScaleTransform _fillScale = new(0, 1);
    private readonly TranslateTransform _thumbAt = new();
    private double[] _marks;
    private double _value;

    public event Action<double>? Seeking;

    public ReplayScrubber(double[] marks)
    {
        _marks = marks;
        _visuals = new VisualCollection(this) { _fill, _thumb };
        _fill.Transform = _fillScale;
        _thumb.Transform = _thumbAt;
        Cursor = Cursors.Hand;
        ToolTip = "Drag to move through the lap (← → step a second, Shift for five)";
        using var dc = _thumb.RenderOpen();
        dc.DrawEllipse(Ui.Res<Brush>("AccentText"), new Pen(Ui.Res<Brush>("Surface"), 2), new Point(0, 0), Thumb, Thumb);
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 1);
            Place();
        }
    }

    public void SetMarks(double[] marks)
    {
        _marks = marks;
        InvalidateVisual();
    }

    private double Left => Thumb;

    private double Width0 => Math.Max(1, ActualWidth - 2 * Thumb);

    private void Place()
    {
        _fillScale.ScaleX = _value;
        _thumbAt.X = Left + _value * Width0;
        _thumbAt.Y = ActualHeight / 2;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        using (var dc = _fill.RenderOpen()) dc.DrawRectangle(Ui.Res<Brush>("AccentText"), null, new Rect(Left, ActualHeight / 2 - 2, Width0, 4));
        _fillScale.CenterX = Left;
        Place();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double mid = ActualHeight / 2;
        dc.DrawRectangle(Ui.Res<Brush>("Line"), null, new Rect(Left, mid - 2, Width0, 4));
        var tick = new Pen(Ui.Res<Brush>("TextMuted"), 1.5);
        tick.Freeze();
        foreach (double m in _marks)
        {
            double x = Math.Round(Left + m * Width0) + 0.5;
            dc.DrawLine(tick, new Point(x, mid - 7), new Point(x, mid + 7));
        }
    }

    private void Seek(MouseEventArgs e) => Seeking?.Invoke(Math.Clamp((e.GetPosition(this).X - Left) / Width0, 0, 1));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        Seek(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) Seek(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }
}

internal sealed class SectorTable : Grid
{
    private readonly List<Border> _rows = new();
    private int _current = -1;

    public SectorTable(List<ReplayCar> cars, IReadOnlyList<double> startsPct)
    {
        var times = cars.Select(c => LapTrace.SectorTimes(c.Trace, startsPct)).ToList();
        int sectors = times[0].Length;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in cars) ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r <= sectors + 1; r++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Add(new TextBlock { Text = "Sectors", Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 14, 6) }, 0, 0);
        for (int c = 0; c < cars.Count; c++)
        {
            var swatch = new Border { Height = 3, Width = 34, Margin = new Thickness(12, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, cars[c].Line.BrushKey);
            swatch.ToolTip = cars[c].Line.Label;
            Add(swatch, 0, c + 1);
        }
        for (int k = 0; k <= sectors; k++)
        {
            int row = k + 1;
            bool total = k == sectors;
            var band = new Border { Margin = new Thickness(-6, 0, -6, 0) };
            SetRow(band, row);
            SetColumnSpan(band, cars.Count + 1);
            Children.Add(band);
            if (!total) _rows.Add(band);
            Add(new TextBlock { Text = total ? "Lap" : $"S{k + 1}", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 3, 14, 3) }, row, 0);
            double best = total ? cars.Min(c => c.Trace.LapTime) : times.Min(t => t[k]);
            for (int c = 0; c < cars.Count; c++)
            {
                double v = total ? cars[c].Trace.LapTime : times[c][k];
                var text = new TextBlock
                {
                    Text = total ? StatFormat.LapTime(v) : v.ToString("0.000", CultureInfo.CurrentCulture), Style = Ui.Res<Style>("Caption"),
                    HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 3, 0, 3),
                };
                Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
                if (cars.Count > 1 && Math.Abs(v - best) < 0.0005)
                {
                    text.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                    text.FontWeight = FontWeights.SemiBold;
                }
                Add(text, row, c + 1);
            }
        }
    }

    private void Add(UIElement e, int row, int col)
    {
        SetRow(e, row);
        SetColumn(e, col);
        Children.Add(e);
    }

    public void SetCurrent(int sector)
    {
        if (sector == _current) return;
        if (_current >= 0 && _current < _rows.Count) _rows[_current].Background = null;
        _current = sector;
        if (sector >= 0 && sector < _rows.Count) _rows[sector].SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.SimRacing;
using GoodieBag.SimRacing.IracingFiles;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class FovSection : StackPanel
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly Grid _top = new(), _bottom = new();
    private readonly StackPanel _iracing = new();
    private string? _iracingFolder;
    private double? _iracingFov;

    private void ReadIracing()
    {
        try
        {
            _iracingFolder = FovExchange.IracingFolder();
            _iracingFov = _iracingFolder is null ? null : FovExchange.CurrentIracingFov(_iracingFolder);
        }
        catch
        {
            _iracingFolder = null;
            _iracingFov = null;
        }
    }
    private readonly StackPanel _inputs = new(), _numbers = new(), _sims = new(), _compare = new(), _measure = new(), _warnings = new();
    private readonly RigDiagram _diagram = new() { MinHeight = 300 };
    private readonly Stack<FovSetup> _undo = new();
    private DateTime _lastUndoPush;
    private Button? _undoButton;
    private int _mode = 2;
    private readonly FrameworkElement _compareCard;
    private bool _bottomWide = true;

    private static readonly (string Label, Func<FovSetup> Make)[] Presets =
    {
        ("Triple 27\" 16:9 1440p · 70 cm", () => Setup("Triple 27\"", true, 27, 2560, 1440, 70, 20)),
        ("Triple 32\" 16:9 1440p · 80 cm", () => Setup("Triple 32\"", true, 32, 2560, 1440, 80, 22)),
        ("Triple 24\" 16:9 1080p · 60 cm", () => Setup("Triple 24\"", true, 24, 1920, 1080, 60, 18)),
        ("Triple 32\" 16:9 4K · 75 cm", () => Setup("Triple 32\" 4K", true, 32, 3840, 2160, 75, 20)),
        ("Triple 27\" with bezel-free kit · 70 cm", () => Setup("Triple 27\" bezel-free", true, 27, 2560, 1440, 70, -2)),
        ("Single 27\" 16:9 1440p · 65 cm", () => Setup("Single 27\"", false, 27, 2560, 1440, 65, 20)),
        ("Single 34\" 21:9 1500R · 70 cm", () => Setup("Single 34\" ultrawide", false, 34, 3440, 1440, 70, 20, 1500)),
        ("Single 49\" 32:9 1800R · 80 cm", () => Setup("Single 49\" super ultrawide", false, 49, 5120, 1440, 80, 20, 1800)),
        ("Single 55\" TV 4K · 110 cm", () => Setup("55\" TV", false, 55, 3840, 2160, 110, 30)),
    };

    private static readonly (int W, int H)[] Resolutions =
    {
        (1920, 1080), (2560, 1440), (3840, 2160), (1920, 1200), (2560, 1600), (2560, 1080), (3440, 1440), (3840, 1600), (5120, 1440), (5120, 2160),
    };

    private static readonly (string Id, string Label)[] Aspects =
    {
        ("auto", "From resolution"), ("16:9", "16:9"), ("16:10", "16:10"), ("21:9", "21:9 (nominal)"), ("32:9", "32:9"), ("4:3", "4:3"), ("5:4", "5:4"), ("custom", "Custom"),
    };

    private static readonly int[] Radii = { 800, 1000, 1500, 1800, 2300, 3000, 4000 };

    public FovSection(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        BuildTools();

        foreach (var w in new[] { new GridLength(400), new GridLength(16), new GridLength(1, GridUnitType.Star), new GridLength(16), new GridLength(250) })
            _top.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        for (int i = 0; i < 3; i++) _top.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var inputsCard = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(18, 14, 18, 14), Child = _inputs };
        var left = new StackPanel();
        left.Children.Add(inputsCard);
        var iracingCard = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(18, 14, 18, 16), Margin = new Thickness(0, 16, 0, 0), Child = _iracing };
        Ui.Anchor(iracingCard, "simracing.fov.iracing");
        left.Children.Add(iracingCard);
        _top.Children.Add(left);
        var diagramStack = new DockPanel();
        var diagramHead = Heading("Top-down view", "I.Target");
        DockPanel.SetDock(diagramHead, Dock.Top);
        diagramStack.Children.Add(diagramHead);
        DockPanel.SetDock(_warnings, Dock.Bottom);
        diagramStack.Children.Add(_warnings);
        diagramStack.Children.Add(_diagram);
        var diagramCard = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(18, 14, 18, 14), Child = diagramStack };
        Grid.SetColumn(diagramCard, 2);
        _top.Children.Add(diagramCard);
        Grid.SetColumn(_numbers, 4);
        _top.Children.Add(_numbers);
        Children.Add(_top);

        foreach (var w in new[] { new GridLength(1, GridUnitType.Star), new GridLength(16), new GridLength(1.25, GridUnitType.Star) })
            _bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        _bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _bottom.Margin = new Thickness(0, 16, 0, 0);
        var simsCard = Card("Enter in your sim", "I.Wheel", _sims);
        _bottom.Children.Add(simsCard);
        var right = new StackPanel();
        _compareCard = Card("Compare setups", "I.Layers", _compare);
        Ui.Anchor(_compareCard, "simracing.compare");
        right.Children.Add(_compareCard);
        var measureCard = Card("Measure your rig", "I.Ruler", _measure);
        measureCard.Margin = new Thickness(0, 16, 0, 0);
        right.Children.Add(measureCard);
        Grid.SetColumn(right, 2);
        _bottom.Children.Add(right);
        Children.Add(_bottom);

        SizeChanged += (_, _) => Layout(ActualWidth);
        ReadIracing();
        BuildInputs();
        Recalculate();
    }

    private SimRacingSettings S => _app.Config.SimRacing;
    private FovSetup C => S.Current;
    private bool Imperial => S.Imperial;

    private string LenUnit => Imperial ? "in" : "cm";
    private string BezelUnit => Imperial ? "in" : "mm";
    private double ToLen(double cm) => Imperial ? cm / 2.54 : cm;
    private double FromLen(double v) => Imperial ? v * 2.54 : v;
    private double ToBezel(double mm) => Imperial ? mm / 25.4 : mm;
    private double FromBezel(double v) => Imperial ? v * 25.4 : v;
    private string Len(double cm, int decimals = 1) => ToLen(cm).ToString("F" + decimals) + " " + LenUnit;

    private void Layout(double width)
    {
        int mode = width >= 1150 ? 2 : width >= 720 ? 1 : 0;
        bool bottomWide = width >= 900;
        if (mode == _mode && bottomWide == _bottomWide) return;
        bool numbersMoved = (mode == 2) != (_mode == 2);
        _mode = mode;
        _bottomWide = bottomWide;
        var cols = _top.ColumnDefinitions;
        cols[0].Width = mode == 2 ? new GridLength(400) : mode == 1 ? new GridLength(380) : new GridLength(1, GridUnitType.Star);
        cols[1].Width = new GridLength(mode > 0 ? 16 : 0);
        cols[2].Width = mode > 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        cols[3].Width = new GridLength(mode == 2 ? 16 : 0);
        cols[4].Width = mode == 2 ? new GridLength(250) : new GridLength(0);
        var diagram = (FrameworkElement)_top.Children[1];
        Grid.SetColumn(diagram, mode > 0 ? 2 : 0);
        Grid.SetRow(diagram, mode > 0 ? 0 : 2);
        diagram.Margin = new Thickness(0, mode > 0 ? 0 : 16, 0, 0);
        Grid.SetColumn(_numbers, mode == 2 ? 4 : 0);
        Grid.SetColumnSpan(_numbers, mode == 1 ? 3 : 1);
        Grid.SetRow(_numbers, mode == 2 ? 0 : 1);
        _numbers.Margin = new Thickness(0, mode == 2 ? 0 : 16, 0, 0);

        var b = _bottom.ColumnDefinitions;
        b[1].Width = new GridLength(bottomWide ? 16 : 0);
        b[2].Width = bottomWide ? new GridLength(1.25, GridUnitType.Star) : new GridLength(0);
        var right = (FrameworkElement)_bottom.Children[1];
        Grid.SetColumn(right, bottomWide ? 2 : 0);
        Grid.SetRow(right, bottomWide ? 0 : 1);
        right.Margin = new Thickness(0, bottomWide ? 0 : 16, 0, 0);
        ArrangeTools();
        if (numbersMoved) Recalculate();
    }

    private readonly StackPanel _tools = new();
    private readonly ComboBox _presets = new() { ToolTip = "Popular setups" };
    private readonly DockPanel _toolRow = new() { LastChildFill = false };

    private void BuildTools()
    {
        _presets.Items.Add("Popular setups…");
        foreach (var (label, _) in Presets) _presets.Items.Add(label);
        _presets.SelectedIndex = 0;
        _presets.SelectionChanged += (_, _) =>
        {
            if (_presets.SelectedIndex <= 0) return;
            var make = Presets[_presets.SelectedIndex - 1].Make;
            Change(() => S.Current = make(), rebuild: true);
            _presets.SelectedIndex = 0;
        };
        _tools.Children.Add(_presets);
        var units = Ui.Segmented(new[] { "cm", "in" }, Imperial ? 1 : 0, i => { S.Imperial = i == 1; _app.Save(); BuildInputs(); Recalculate(); }, "Units", new[] { "Centimetres", "Inches" });
        units.ToolTip = "Units for distances and sizes";
        DockPanel.SetDock(units, Dock.Left);
        _toolRow.Children.Add(units);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _undoButton = Ui.IconButton("I.Undo", "Undo last change", Undo);
        _undoButton.IsEnabled = false;
        buttons.Children.Add(_undoButton);
        buttons.Children.Add(Ui.IconButton("I.Refresh", "Reset to defaults", () => Change(() => S.Current = new FovSetup(), rebuild: true)));
        DockPanel.SetDock(buttons, Dock.Right);
        _toolRow.Children.Add(buttons);
        _tools.Children.Add(_toolRow);
        ArrangeTools();
    }

    private void ArrangeTools()
    {
        bool column = _mode == 2;
        _tools.Orientation = column ? Orientation.Vertical : Orientation.Horizontal;
        _tools.HorizontalAlignment = column ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        _tools.Margin = new Thickness(column ? 6 : 0, 0, 6, 12);
        _presets.Width = column ? double.NaN : 230;
        _toolRow.Margin = column ? new Thickness(0, 8, 0, 0) : new Thickness(8, 0, 0, 0);
        _toolRow.HorizontalAlignment = column ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        var units = (FrameworkElement)_toolRow.Children[0];
        units.Margin = new Thickness(0, 0, column ? 0 : 8, 0);
    }

    private void Change(Action mutate, bool rebuild = false)
    {
        if (DateTime.UtcNow - _lastUndoPush > TimeSpan.FromSeconds(1.2))
        {
            _undo.Push(C.Copy());
            if (_undo.Count > 50)
            {
                var keep = _undo.Reverse().Skip(1).ToList();
                _undo.Clear();
                foreach (var s in keep) _undo.Push(s);
            }
            _lastUndoPush = DateTime.UtcNow;
            if (_undoButton is not null) _undoButton.IsEnabled = true;
        }
        var color = C.Color;
        mutate();
        C.Color = color;
        _app.Save();
        if (rebuild) BuildInputs();
        Recalculate();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        var color = C.Color;
        S.Current = _undo.Pop();
        C.Color = color;
        _lastUndoPush = DateTime.MinValue;
        if (_undoButton is not null) _undoButton.IsEnabled = _undo.Count > 0;
        _app.Save();
        BuildInputs();
        Recalculate();
    }

    private void BuildInputs()
    {
        _inputs.Children.Clear();
        _inputs.Children.Add(Row("Screens", Ui.Segmented(new[] { "Single", "Triple" }, C.Triple ? 1 : 0, i => Change(() => C.Triple = i == 1, rebuild: true), "Layout",
            new[] { "One screen in front of you", "A center screen plus two side screens angled in" }),
            "Triple: a center screen straight ahead and two side screens angled in towards you."));

        _inputs.Children.Add(Section(C.Triple ? "Center screen" : "Screen"));
        ScreenEditor(C.Centre);

        int tripleFrom = _inputs.Children.Count;
        {
            _inputs.Children.Add(Section("Side screens"));
            _inputs.Children.Add(Row("Different size", Ui.Switch(C.MixedSides, v => Change(() => { C.MixedSides = v; if (v) C.Side = C.Centre.Copy(); }, rebuild: true)),
                "Mixed setups, e.g. a bigger center screen with smaller sides."));
            if (C.MixedSides)
                ScreenEditor(C.Side);
        }
        var tripleRows = Enumerable.Range(tripleFrom, _inputs.Children.Count - tripleFrom).Select(i => _inputs.Children[i]).ToList();

        _inputs.Children.Add(Section("Position"));
        var distance = new NumberBox(ToLen(C.DistanceCm), LenUnit, 64) { Minimum = ToLen(10), Maximum = ToLen(400), Step = Imperial ? 0.5 : 1, Decimals = Imperial ? 1 : 0 };
        distance.ValueChanged += v => Change(() => C.DistanceCm = FromLen(v));
        _inputs.Children.Add(Row("Eyes to screen", distance,
            "Sit in your driving position, helmet off. Measure from your eyes straight ahead to the surface of the center screen (not the bezel or the stand). The most important number: a few cm changes the FOV noticeably."));
        tripleFrom = _inputs.Children.Count;
        {
            var bezel = new NumberBox(ToBezel(C.BezelMm), BezelUnit, 64) { Minimum = ToBezel(-30), Maximum = ToBezel(100), Step = Imperial ? 0.05 : 1, Decimals = Imperial ? 2 : 0 };
            bezel.ValueChanged += v => Change(() => C.BezelMm = FromBezel(v));
            _inputs.Children.Add(Row("Bezel per seam", bezel,
                "The gap between the picture on one screen and the next, where they meet: both bezels together, last lit pixel to first lit pixel. Bezel-free kit (lenses / overlap): enter a negative value."));
            var angle = new StackPanel { Orientation = Orientation.Horizontal };
            angle.Children.Add(Ui.Segmented(new[] { "Ideal", "My rig" }, C.AutoAngle ? 0 : 1, i => Change(() => C.AutoAngle = i == 0, rebuild: true), "Angle",
                new[] { "Work out the angle that makes each side screen face your eyes square-on", "Enter the angle your side screens are turned in now" }));
            if (!C.AutoAngle)
            {
                var degrees = new NumberBox(C.SideAngleDeg, "°", 56) { Minimum = 0, Maximum = 120, Step = 0.5, Decimals = 1, Margin = new Thickness(8, 0, 0, 0) };
                degrees.ValueChanged += v => Change(() => C.SideAngleDeg = v);
                angle.Children.Add(degrees);
            }
            _inputs.Children.Add(Row("Side angle", angle,
                "Ideal turns each side screen to face your eyes square-on: the angle to set your rig to. My rig: how far your side screens are turned in from flat (0° = in line with the center)."));
        }
        tripleRows.AddRange(Enumerable.Range(tripleFrom, _inputs.Children.Count - tripleFrom).Select(i => _inputs.Children[i]));
        if (!C.Triple)
            foreach (var row in tripleRows)
            {
                row.IsEnabled = false;
                row.Opacity = 0.45;
                if (row is FrameworkElement fe)
                {
                    fe.ToolTip = "For triple screens: pick Triple above to use it.";
                    ToolTipService.SetShowOnDisabled(fe, true);
                }
            }
    }

    private void ScreenEditor(FovScreen screen)
    {
        bool measured = screen.SizeMode == ScreenSizeMode.Measured;
        var size = Ui.Segmented(new[] { "Diagonal", "Measured" }, measured ? 1 : 0,
            i => Change(() => screen.SizeMode = i == 1 ? ScreenSizeMode.Measured : ScreenSizeMode.Diagonal, rebuild: true), "Size",
            new[] { "The advertised size, e.g. 32 inches", "The lit picture's width and height, measured with a tape (most accurate)" });
        _inputs.Children.Add(Row("Size from", size, "Diagonal: the advertised size. Measured: the lit picture's width and height (most accurate)."));

        if (!measured)
        {
            var diagonal = new NumberBox(screen.DiagonalIn, "in", 56) { Minimum = 10, Maximum = 120, Step = 0.5, Decimals = 1 };
            diagonal.ValueChanged += v => Change(() => screen.DiagonalIn = v);
            var aspect = new ComboBox { MinWidth = 160, ToolTip = "Aspect ratio of the picture" };
            foreach (var (_, label) in Aspects) aspect.Items.Add(label);
            aspect.SelectedIndex = Math.Max(0, Array.FindIndex(Aspects, a => a.Id == screen.Aspect));
            aspect.SelectionChanged += (_, _) => Change(() => screen.Aspect = Aspects[aspect.SelectedIndex].Id, rebuild: true);
            _inputs.Children.Add(Row("Diagonal", diagonal, $"Corner to corner of the picture ({screen.DiagonalIn * 2.54:0.#} cm)."));
            _inputs.Children.Add(Row("Aspect", aspect,
                $"“From resolution” uses {FovCalculator.AspectOf(screen):0.000}:1; “21:9” panels are really 2.39:1 (3440×1440) or 2.37:1 (2560×1080)."));
            if (screen.Aspect == "custom")
            {
                var custom = new StackPanel { Orientation = Orientation.Horizontal };
                var aw = new NumberBox(screen.CustomAspectW, ":", 44) { Minimum = 1, Maximum = 100, Step = 1, Decimals = 1 };
                var ah = new NumberBox(screen.CustomAspectH, "", 44) { Minimum = 1, Maximum = 100, Step = 1, Decimals = 1 };
                aw.ValueChanged += v => Change(() => screen.CustomAspectW = v);
                ah.ValueChanged += v => Change(() => screen.CustomAspectH = v);
                custom.Children.Add(aw);
                custom.Children.Add(ah);
                _inputs.Children.Add(Row("Aspect", custom, "Width : height of the picture."));
            }
        }
        else
        {
            var width = new NumberBox(ToLen(screen.WidthCm), LenUnit, 56) { Minimum = ToLen(10), Maximum = ToLen(300), Step = 0.1, Decimals = 1 };
            var height = new NumberBox(ToLen(screen.HeightCm), LenUnit, 56) { Minimum = ToLen(5), Maximum = ToLen(200), Step = 0.1, Decimals = 1 };
            width.ValueChanged += v => Change(() => screen.WidthCm = FromLen(v));
            height.ValueChanged += v => Change(() => screen.HeightCm = FromLen(v));
            _inputs.Children.Add(Row("Picture width", width, "Left to right edge of the lit picture, not the bezel. On a curved screen, follow the curve with a soft tape."));
            _inputs.Children.Add(Row("Picture height", height, "Top to bottom of the lit picture."));
        }

        var resolution = new ComboBox { Width = 150 };
        foreach (var (w, h) in Resolutions) resolution.Items.Add($"{w} × {h}");
        resolution.Items.Add("Other…");
        int known = Array.FindIndex(Resolutions, r => r.W == screen.ResolutionW && r.H == screen.ResolutionH);
        resolution.SelectedIndex = known >= 0 ? known : Resolutions.Length;
        var resRow = new StackPanel { Orientation = Orientation.Horizontal };
        resRow.Children.Add(resolution);
        var typed = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), Visibility = known >= 0 ? Visibility.Collapsed : Visibility.Visible };
        var tw = new TextBox { Width = 56, Text = screen.ResolutionW.ToString(), VerticalContentAlignment = VerticalAlignment.Center };
        var th = new TextBox { Width = 56, Text = screen.ResolutionH.ToString(), VerticalContentAlignment = VerticalAlignment.Center };
        void Commit(TextBox box, int lo, int hi, Func<int> get, Action<int> set)
        {
            if (int.TryParse(box.Text.Trim(), out var v) && v >= lo && v <= hi) { if (v != get()) Change(() => set(v)); }
            else box.Text = get().ToString();
        }
        void Wire(TextBox box, int lo, int hi, Func<int> get, Action<int> set)
        {
            box.LostKeyboardFocus += (_, _) => Commit(box, lo, hi, get, set);
            box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(box, lo, hi, get, set); };
        }
        Wire(tw, 320, 15360, () => screen.ResolutionW, v => screen.ResolutionW = v);
        Wire(th, 200, 8640, () => screen.ResolutionH, v => screen.ResolutionH = v);
        typed.Children.Add(tw);
        typed.Children.Add(new TextBlock { Text = "×", Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Res<Brush>("TextMuted") });
        typed.Children.Add(th);
        resRow.Children.Add(typed);
        resolution.SelectionChanged += (_, _) =>
        {
            if (resolution.SelectedIndex < Resolutions.Length)
            {
                var (w, h) = Resolutions[resolution.SelectedIndex];
                typed.Visibility = Visibility.Collapsed;
                Change(() => { screen.ResolutionW = w; screen.ResolutionH = h; });
            }
            else typed.Visibility = Visibility.Visible;
        };
        _inputs.Children.Add(Row("Resolution", resRow, "Native resolution: sets the aspect ratio (when “From resolution”) and pixels per degree."));

        var curve = new StackPanel { Orientation = Orientation.Horizontal };
        curve.Children.Add(Ui.Switch(screen.Curved, v => Change(() => screen.Curved = v, rebuild: true)));
        if (screen.Curved)
        {
            var radius = new ComboBox { Width = 96, Margin = new Thickness(10, 0, 0, 0) };
            foreach (var r in Radii) radius.Items.Add($"{r}R");
            radius.Items.Add("Other");
            int index = Array.IndexOf(Radii, (int)screen.RadiusMm);
            radius.SelectedIndex = index >= 0 ? index : Radii.Length;
            var custom = new NumberBox(screen.RadiusMm, "mm", 56) { Minimum = 300, Maximum = 10000, Step = 50, Margin = new Thickness(8, 0, 0, 0) };
            custom.Visibility = index >= 0 ? Visibility.Collapsed : Visibility.Visible;
            custom.ValueChanged += v => Change(() => screen.RadiusMm = v);
            radius.SelectionChanged += (_, _) =>
            {
                if (radius.SelectedIndex < Radii.Length)
                {
                    custom.Visibility = Visibility.Collapsed;
                    Change(() => screen.RadiusMm = Radii[radius.SelectedIndex]);
                }
                else custom.Visibility = Visibility.Visible;
            };
            curve.Children.Add(radius);
            curve.Children.Add(custom);
        }
        _inputs.Children.Add(Row("Curved", curve, "The “R” rating on the box: 1500R means the curve is part of a circle with a 1.5 m radius. Smaller = more curved. You sit inside the curve."));
    }

    private void Recalculate()
    {
        var r = FovCalculator.Calculate(C);

        _numbers.Children.Clear();
        _numbers.Children.Add(_tools);
        var numbers = _mode == 2 ? (Panel)_numbers : new UniformGrid { Columns = 3, Margin = new Thickness(-6, 0, -6, 0) };
        numbers.Children.Add(Number("Horizontal FOV", $"{r.HorizontalFovTotal:0.0}°", r.Triple ? $"All three screens · center {r.HorizontalFovCentre:0.0}°" : "iRacing, AMS2, LFS, RaceRoom", "I.Eye", "SageSoft", "SageDeep"));
        numbers.Children.Add(Number("Vertical FOV", $"{r.VerticalFov:0.0}°", r.Triple && C.MixedSides ? $"Center · sides {r.VerticalFovSide:0.0}°" : "AC, ACC, LMU, rF2, AMS1, BeamNG", "I.Sliders", "SlateSoft", "SlateDeep"));
        numbers.Children.Add(r.Triple
            ? Number("Side angle", $"{r.SideAngle:0.0}°", C.AutoAngle ? $"Turned in from flat · {180 - r.SideAngle:0.0}° between screens" : $"Your angle · ideal is {r.OptimalSideAngle:0.0}°", "I.Rotate", "ApricotSoft", "ApricotDeep")
            : Number("Detail", $"{r.PixelsPerDegree:0.0}", "Pixels per degree (horizontal)", "I.Rotate", "ApricotSoft", "ApricotDeep"));
        if (numbers != _numbers) _numbers.Children.Add(numbers);

        _warnings.Children.Clear();
        if (r.Triple && r.OptimalUnreachable && C.AutoAngle)
            _warnings.Children.Add(Banner("ApricotSoft", "The side screens are too wide to face you square-on at this distance, so the angle is capped. Sit further back or use smaller side screens."));
        if (r.HorizontalFovTotal > 180)
            _warnings.Children.Add(Banner("AccentSoft", $"Your screens wrap past 180° around you ({r.HorizontalFovTotal:0}°). That's fine; some sims cap their FOV lower than this."));
        if (C.DistanceCm < 35)
            _warnings.Children.Add(Banner("ApricotSoft", "That's very close. Double-check the distance: it's from your eyes to the screen surface."));

        S.AssignCompareColors();
        var compared = S.Compare.Select(setup => (Result: FovCalculator.Calculate(setup), Color: CompareBrush(setup))).ToList();
        _diagram.Show(r, Imperial, compared, C.Color ?? SimRacingSettings.AccentColor);
        FillSims(r);
        FillCompare(r, compared.Select(c => c.Result).ToList());
        FillMeasure(r);
        FillIracing(r);
    }

    private void FillIracing(FovResult r)
    {
        _iracing.Children.Clear();
        _iracing.Children.Add(Heading("iRacing settings", "I.Wheel"));
        var folder = _iracingFolder;
        double target = FovExchange.IracingFov(r);
        string status = folder is null
            ? "iRacing's settings weren't found in Documents\\iRacing. Start iRacing once, or import a file you saved."
            : _iracingFov is { } now
                ? Math.Abs(now - target) < 0.15 ? $"iRacing is already set for this rig ({now:0.#}°)." : $"iRacing's FOV is {now:0.#}° now; for this rig it should be {target:0.#}°."
                : "Found iRacing's settings in Documents\\iRacing.";
        _iracing.Children.Add(new TextBlock { Text = status, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8),
            Foreground = Ui.Res<Brush>(folder is null ? "ApricotDeep" : "Text") });
        string[] steps =
        {
            "Import reads the screen size, distance, bezel, side angle and curve iRacing has now, so you start from your current setup.",
            "Adjust the rig above until it matches your seat and screens.",
            "Write puts this FOV and the triple-screen setup into iRacing. Close iRacing first; its files are backed up (iRacing Controller → Backups). Or save copies to a folder and copy them in yourself.",
        };
        for (int i = 0; i < steps.Length; i++)
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var n = new TextBlock { Text = $"{i + 1}.", Width = 18, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("AccentText") };
            DockPanel.SetDock(n, Dock.Left);
            line.Children.Add(n);
            line.Children.Add(new TextBlock { Text = steps[i], Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
            _iracing.Children.Add(line);
        }
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Button B(string icon, string label, Action run, string style, string tip)
        {
            var b = Ui.LabeledButton(icon, label, run, style);
            b.Margin = new Thickness(0, 0, 8, 8);
            b.ToolTip = tip;
            buttons.Children.Add(b);
            return b;
        }
        B("I.Folder", "Import from iRacing", ImportFromIracingFolder, "SoftButton", "Read rendererDX11Monitor.ini in Documents\\iRacing").IsEnabled = folder is not null;
        B("I.Folder", "Import a file…", ImportFromFile, "GhostButton", "Read a rendererDX11Monitor.ini you saved somewhere else");
        B("I.Download", "Write into iRacing", () => _ = ExportIntoIracing(), "PrimaryButton", $"FOV {target:0.#}° into app.ini and the screen setup into rendererDX11Monitor.ini (iRacing must be closed)").IsEnabled = folder is not null;
        B("I.Save", "Save copies…", ExportCopies, "GhostButton", "Updated app.ini and rendererDX11Monitor.ini plus _backup copies, in a folder you pick").IsEnabled = folder is not null;
        _iracing.Children.Add(buttons);
    }

    private void FillSims(FovResult r)
    {
        _sims.Children.Clear();
        var values = FovCalculator.SimValues(r);
        var picked = values.FirstOrDefault(v => v.Sim == S.Sim) ?? values[0];
        var head = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        head.Inlines.Add(new System.Windows.Documents.Run(picked.Degrees is { } d ? $"{d:0.0}°  " : picked.Axis + "  ") { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("AccentText") });
        head.Inlines.Add(new System.Windows.Documents.Run(picked.Degrees is null ? $"{picked.Sim}: {picked.Note}" : $"{picked.Axis.ToLowerInvariant()} FOV for {picked.Sim}. {picked.Note}") { Foreground = Ui.Res<Brush>("TextMuted") });
        _sims.Children.Add(head);

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-4, 0, -4, 0) };
        foreach (var v in values)
        {
            bool mine = v == picked;
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = v.Sim, FontWeight = mine ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }));
            var value = new TextBlock
            {
                Text = v.Degrees is { } deg ? $"{deg:0.0}° {(v.Axis == "Horizontal" ? "H" : "V")}" : "–",
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = Ui.Res<Brush>(v.OutOfRange ? "Danger" : v.Degrees is null ? "TextFaint" : "Text"),
            };
            Grid.SetColumn(value, 1);
            line.Children.Add(value);
            var row = new Tile
            {
                Padding = new Thickness(10, 5, 10, 5), CornerRadius = ThemeManager.Round(8), Margin = new Thickness(4, 1, 4, 1), Child = line, Cursor = System.Windows.Input.Cursors.Hand,
                Background = mine ? Ui.Res<Brush>("AccentSoft") : Brushes.Transparent,
                ToolTip = $"{v.Axis} FOV. {v.Note}" + (v.Extra is null ? "" : $" {v.Extra}.") + (v.OutOfRange ? $" Outside its {v.Range} range." : v.Range is null ? "" : $" Range {v.Range}."),
            };
            var sim = v.Sim;
            row.MouseLeftButtonUp += (_, _) => { S.Sim = sim; _app.Save(); FillSims(FovCalculator.Calculate(C)); };
            Motion.Pressable(row);
            grid.Children.Add(row);
        }
        _sims.Children.Add(grid);
        _sims.Children.Add(new TextBlock
        {
            Text = "H = horizontal, V = vertical. The calculated value is the right starting point; fine-tune by feel (around ±10%).",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        });
    }

    private void FillCompare(FovResult current, IReadOnlyList<FovResult> others)
    {
        _compare.Children.Clear();
        var table = new Grid();
        foreach (var w in new[] { 2.2, 1, 1, 1, 1.4, 0.8 })
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        int row = 0;
        void Cell(UIElement e, int col)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            table.Children.Add(e);
        }
        TextBlock T(string text, bool head = false) => new()
        {
            Text = text, VerticalAlignment = head ? VerticalAlignment.Bottom : VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 8, 4),
            Style = head ? Ui.Res<Style>("Eyebrow") : Ui.Res<Style>("Body"), ToolTip = head ? null : text, TextWrapping = head ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        table.RowDefinitions.Add(new RowDefinition());
        string[] heads = { "Setup", "H FOV", "V FOV", "Side angle", "Footprint", "px/°" };
        string?[] tips = { null, "Horizontal field of view", "Vertical field of view", null, "Width × depth of the screens seen from above", "Pixels per degree (horizontal): detail" };
        for (int i = 0; i < heads.Length; i++)
        {
            var head = T(heads[i], true);
            head.ToolTip = tips[i];
            Cell(head, i);
        }

        void AddRow(FovSetup setup, FovResult r, Brush color, Action remove, Action? edit)
        {
            row++;
            table.RowDefinitions.Add(new RowDefinition());
            var idle = edit is null ? Ui.Res<Brush>("AccentSoft") : Ui.Res<Brush>("Surface");
            var band = new Tile
            {
                Background = idle, CornerRadius = ThemeManager.Round(8), Margin = new Thickness(-10, 4, -4, 4),
                BorderThickness = new Thickness(1), BorderBrush = Ui.Res<Brush>(edit is null ? "Accent" : "LineStrong"),
                Cursor = edit is null ? null : System.Windows.Input.Cursors.Hand,
                ToolTip = edit is null ? "The setup you're editing" : "Click to edit this setup (the one you're editing now joins the comparison)",
            };
            if (edit is not null)
            {
                band.MouseEnter += (_, _) => band.Background = Ui.Res<Brush>("Hover");
                band.MouseLeave += (_, _) => band.Background = idle;
                band.MouseLeftButtonUp += (_, _) => edit();
                Motion.Pressable(band);
            }
            Grid.SetRow(band, row);
            Grid.SetColumnSpan(band, 7);
            table.Children.Add(band);
            int firstCell = table.Children.Count;
            var who = new DockPanel { Margin = new Thickness(20, 12, 8, 12) };
            var detail = (edit is null ? "Editing · " : "") + $"{(setup.Triple ? "3 × " : "")}{Describe(setup.Centre)} · {Len(setup.DistanceCm, 0)}";
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, ToolTip = $"{setup.Name}\n{detail}" };
            label.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = setup.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }));
            label.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = detail, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) }));
            who.Children.Add(label);
            Cell(who, 0);
            Cell(T($"{r.HorizontalFovTotal:0.0}°"), 1);
            Cell(T($"{r.VerticalFov:0.0}°"), 2);
            Cell(T(r.Triple ? $"{r.SideAngle:0.0}°" : "–"), 3);
            Cell(T($"{ToLen(r.FootprintWidthCm):0} × {ToLen(r.FootprintDepthCm):0} {LenUnit}"), 4);
            Cell(T($"{r.PixelsPerDegree:0.0}"), 5);
            for (int c = firstCell; c < table.Children.Count; c++) table.Children[c].IsHitTestVisible = false;
            Cell(ColorButton(setup, color), 0);
            Cell(Ui.IconButton("I.Trash", edit is not null ? "Delete this setup"
                : S.Compare.Count > 0 ? "Delete this setup (the next one in the list becomes the one you edit)" : "Delete this setup (back to a default rig)", remove), 6);
        }

        foreach (var i in S.ValidCompareRows())
        {
            if (i == -1)
            {
                AddRow(C, current, CompareBrush(C), () => _ = RemoveCurrentAsync(), null);
                continue;
            }
            if (i >= others.Count) continue;
            var setup = S.Compare[i];
            int slot = i;
            AddRow(setup, others[i], CompareBrush(setup), async () =>
            {
                if (!await _window.Confirm($"Delete “{setup.Name}”?", "It's removed from the comparison and from your saved setups.", "Delete", danger: true)) return;
                S.RemoveCompare(slot);
                _app.Save();
                Recalculate();
            }, () => MakeActive(slot));
        }
        _compare.Children.Add(table);

        var items = new List<(string, Action?)> { ("A copy of the setup being edited", () => AddCompare(C.Copy(), C.Name + " copy")), ("", null) };
        foreach (var (label, make) in Presets)
            items.Add((label, () => AddCompare(make(), null)));
        var add = Menu("I.Plus", "Compare with…", items.ToArray());
        add.IsEnabled = S.Compare.Count < SimRacingSettings.MaxCompare;
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 10, 0, 0);
        _compare.Children.Add(add);
        _compare.Children.Add(new TextBlock
        {
            Text = S.Compare.Count == 0
                ? "Add setups to see them in the top-down view in their own color, with their numbers next to yours. Tip: compare a copy of your rig, then change your rig."
                : $"Drawn in the top-down view in their color (click a color to change it, × to remove a setup). Click a row to edit that setup. Up to {SimRacingSettings.MaxCompare}.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        });
    }

    private Brush CompareBrush(FovSetup setup) => Ui.Res<Brush>(setup.Color ?? (setup == C ? SimRacingSettings.AccentColor : SimRacingSettings.CompareColors[0]));

    private FrameworkElement ColorButton(FovSetup setup, Brush color)
    {
        var ring = new Tile
        {
            Child = new Tile { Width = 12, Height = 12, CornerRadius = ThemeManager.Round(3), Background = color },
            Padding = new Thickness(2), Margin = new Thickness(-3, 0, 0, 0), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent, CornerRadius = ThemeManager.Round(5), Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Change this setup's color",
        };
        ring.MouseEnter += (_, _) => ring.BorderBrush = Ui.Res<Brush>("LineStrong");
        ring.MouseLeave += (_, _) => ring.BorderBrush = Brushes.Transparent;
        ring.MouseLeftButtonUp += (_, e) => { e.Handled = true; PickColor(ring, setup); };
        Motion.Pressable(ring);
        return ring;
    }

    private void PickColor(FrameworkElement anchor, FovSetup setup)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        Ui.FollowTarget(popup);
        var swatches = new WrapPanel { MaxWidth = 4 * 30 };
        var seen = new List<Color>();
        var everyone = S.Compare.Prepend(C).ToList();
        foreach (var key in SimRacingSettings.ColorChoices)
        {
            var color = ThemeManager.ColorOf(key, Colors.Transparent);
            bool mine = key == setup.Color;
            if (seen.Contains(color) && !mine) continue;
            seen.Add(color);
            var owner = everyone.FirstOrDefault(other => other != setup && other.Color == key);
            var choice = new Tile
            {
                Child = new Tile { Width = 18, Height = 18, CornerRadius = ThemeManager.Round(4), Background = Ui.Res<Brush>(key) },
                Padding = new Thickness(2), Margin = new Thickness(2), BorderThickness = new Thickness(2), CornerRadius = ThemeManager.Round(6),
                BorderBrush = mine ? Ui.Res<Brush>("Heading") : Brushes.Transparent, Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = mine ? "This setup's color" : owner is null ? null : $"Used by {owner.Name} (it takes this setup's color)",
            };
            if (!mine)
            {
                choice.MouseEnter += (_, _) => choice.BorderBrush = Ui.Res<Brush>("LineStrong");
                choice.MouseLeave += (_, _) => choice.BorderBrush = Brushes.Transparent;
            }
            var picked = key;
            choice.MouseLeftButtonUp += (_, _) =>
            {
                popup.IsOpen = false;
                if (mine) return;
                if (owner is not null) owner.Color = setup.Color;
                setup.Color = picked;
                _app.Save();
                Recalculate();
            };
            Motion.Pressable(choice);
            swatches.Children.Add(choice);
        }
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "COLOR", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(2, 0, 0, 6) });
        stack.Children.Add(swatches);
        popup.Child = new Tile
        {
            Style = Ui.Res<Style>("LiftedCard"),
            Padding = new Thickness(10),
            Margin = new Thickness(8, 4, 8, 16),
            Child = stack,
        };
        popup.IsOpen = true;
    }

    private void MakeActive(int slot)
    {
        S.MakeCurrent(slot);
        EditedAnother();
    }

    private async Task RemoveCurrentAsync()
    {
        if (!await _window.Confirm($"Delete “{C.Name}”?", S.Compare.Count > 0 ? "The next setup in the list becomes the one you edit." : "You're back to a default rig.", "Delete", danger: true)) return;
        S.RemoveCurrent();
        EditedAnother();
    }

    private void EditedAnother()
    {
        _undo.Clear();
        _lastUndoPush = DateTime.MinValue;
        if (_undoButton is not null) _undoButton.IsEnabled = false;
        _app.Save();
        BuildInputs();
        Recalculate();
    }

    private string Describe(FovScreen s) =>
        (s.SizeMode == ScreenSizeMode.Measured ? $"{Len(s.WidthCm, 0)} wide" : $"{s.DiagonalIn:0.#}\"") + (s.Curved ? $" {s.RadiusMm:0}R" : "");

    private void AddCompare(FovSetup setup, string? name)
    {
        if (S.Compare.Count >= SimRacingSettings.MaxCompare) return;
        if (name is not null) setup.Name = name;
        setup.Color = null;
        S.Compare.Add(setup);
        _app.Save();
        Recalculate();
    }

    private void FillMeasure(FovResult r)
    {
        _measure.Children.Clear();
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-8, 0, -8, 0) };
        void M(string label, string value, string? help)
        {
            var cell = new StackPanel { Margin = new Thickness(8, 4, 8, 4), ToolTip = help };
            cell.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis }));
            cell.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("AccentText") });
            grid.Children.Add(cell);
        }
        if (r.Triple)
        {
            M("Outer edge to outer edge", Len(r.TapeOuterEdgesCm), "Tape across the front, from the far edge of the left side screen's picture to the far edge of the right one's. Angle the side screens until it reads this.");
            M("Eyes to far edge of a side screen", Len(r.TapeEyeToSideEdgeCm), "From between your eyes (driving position) to the outer edge of a side screen's picture: a second check of the angle.");
            M("Rig width × depth", $"{ToLen(r.RigWidthCm):0} × {ToLen(r.RigDepthCm):0} {LenUnit}", "Outer bezel to outer bezel; front of the side screens to the deepest point of the center screen.");
        }
        else
        {
            M("Picture width", Len(r.Centre.WidthCm), "Across the lit picture" + (r.Centre.RadiusCm is null ? "." : ", following the curve."));
            M("Rig width", Len(r.RigWidthCm, 0), "Outer bezel to outer bezel.");
        }
        M("Footprint (estimate)", $"{ToLen(r.FootprintWidthCm):0} × {ToLen(r.FootprintDepthCm):0} {LenUnit}",
            r.Triple ? "Width plus ~1.75 cm of frame per side; depth plus ~8 cm of panel and ~4.5 cm for a triple stand." : "Width plus ~1.75 cm of frame per side; depth includes ~8 cm of panel.");
        string Size(ScreenSize s) => $"{ToLen(s.WidthCm):0.0} × {ToLen(s.HeightCm):0.0} {LenUnit} ({s.Aspect:0.00}:1)";
        M(r.Triple ? "Center picture" : "Picture", Size(r.Centre), r.Centre.RadiusCm is { } rad ? $"{rad * 10:0}R curve" : null);
        if (r.Triple && C.MixedSides) M("Side picture", Size(r.Side), null);
        M("Pixel density", $"{r.PixelsPerDegree:0.0} px / °", "Horizontal pixels per degree of view at the center. Higher = sharper detail at this distance.");
        _measure.Children.Add(grid);
    }

    private void ImportFromIracingFolder()
    {
        if (FovExchange.IracingFolder() is not { } folder)
        {
            _app.Notify("iRacing's settings weren't found", "Documents\\iRacing has no rendererDX11Monitor.ini yet. Start iRacing once, or import a file instead.", NoticeKind.Warning, important: true);
            return;
        }
        Import(Path.Combine(folder, IracingPaths.MonitorRenderer));
    }

    private void ImportFromFile()
    {
        if (!GoodieBag.Services.External.Allowed("open dialog")) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "iRacing monitor settings (rendererDX11Monitor.ini)|rendererDX11Monitor.ini|INI files (*.ini)|*.ini" };
        if (FovExchange.IracingFolder() is { } folder) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog() == true) Import(dialog.FileName);
    }

    private void Import(string monitorFile)
    {
        try
        {
            var rig = FovExchange.FromIracing(monitorFile, C);
            Change(() => S.Current = rig, rebuild: true);
            double? current = null;
            try { if (Path.GetDirectoryName(monitorFile) is { } folder) current = FovExchange.CurrentIracingFov(folder); }
            catch { }
            string fov = current is { } now ? $" iRacing's FOV is set to {now:0.#}°; for this rig it should be {FovExchange.IracingFov(FovCalculator.Calculate(C)):0.#}°." : "";
            _app.Notify("Imported from iRacing", "Screen width, distance, bezel, side angle and curve. iRacing doesn't store the screen height or resolution, so check those." + fov, NoticeKind.Success, important: true);
        }
        catch (Exception ex) { _app.Notify("Not imported", ex.Message, NoticeKind.Warning, important: true); }
    }

    private async Task ExportIntoIracing()
    {
        if (_app.Demo)
        {
            _app.Notify("Demo mode", "Nothing is written to iRacing in demo mode.", NoticeKind.Info);
            return;
        }
        if (FovExchange.IracingFolder() is not { } folder)
        {
            _app.Notify("iRacing's settings weren't found", "Documents\\iRacing has no rendererDX11Monitor.ini yet. Start iRacing once first.", NoticeKind.Warning, important: true);
            return;
        }
        var r = FovCalculator.Calculate(C);
        string values = string.Join(", ", FovExchange.IracingValues(C, r).Select(v => $"{v.Key} {v.Value}"));
        if (!await _window.Confirm("Write this rig into iRacing?",
                $"FOV {FovExchange.IracingFov(r):0.#}° in app.ini, and in rendererDX11Monitor.ini [MonitorSetup]: {values}.\n\niRacing's files are backed up first (iRacing Controller → Backups).", "Write"))
            return;
        try
        {
            int backup = FovExchange.WriteIracing(folder, C, r);
            var saved = _app.Config.SimRacing.IracingController.SavedLayout;
            if (saved.Count > 0)
            {
                foreach (var (key, value) in FovExchange.IracingValues(C, r)) saved["MonitorSetup|" + key] = value;
                _app.Save();
            }
            _app.Notify("Written to iRacing", $"iRacing uses it the next time it starts. Backup #{backup} has the files from before.", NoticeKind.Success);
            ReadIracing();
            Recalculate();
        }
        catch (Exception ex) { _app.Notify("Not written", ex.Message, NoticeKind.Warning, important: true); }
    }

    private void ExportCopies()
    {
        if (_app.Demo)
        {
            _app.Notify("Demo mode", "Nothing is saved in demo mode.", NoticeKind.Info);
            return;
        }
        var source = FovExchange.IracingFolder();
        if (source is null)
        {
            _app.Notify("iRacing's settings weren't found", "The updated files are made from your own app.ini and rendererDX11Monitor.ini in Documents\\iRacing. Start iRacing once first.", NoticeKind.Warning, important: true);
            return;
        }
        if (!GoodieBag.Services.External.Allowed("folder dialog")) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Folder for the updated iRacing files" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var files = FovExchange.SaveCopies(source, dialog.FolderName, C, FovCalculator.Calculate(C));
            _app.Notify("iRacing files saved", $"{string.Join(" and ", files)} (plus a _backup copy of each). Close iRacing, then copy them into Documents\\iRacing.", NoticeKind.Success, important: true);
        }
        catch (Exception ex) { _app.Notify("Not saved", ex.Message, NoticeKind.Warning, important: true); }
    }

    private static FovSetup Setup(string name, bool triple, double diagonal, int w, int h, double distance, double bezel, double? radius = null) => new()
    {
        Name = name,
        Triple = triple,
        Centre = new FovScreen { DiagonalIn = diagonal, ResolutionW = w, ResolutionH = h, Curved = radius is not null, RadiusMm = radius ?? 1000 },
        DistanceCm = distance,
        BezelMm = bezel,
    };

    private static Button Menu(string icon, string label, (string Text, Action? Run)[] items)
    {
        var button = Ui.LabeledButton(icon, label, () => { }, "SoftButton");
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        foreach (var (text, run) in items)
        {
            if (run is null) { menu.Items.Add(new Separator()); continue; }
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }
        button.Click += (_, _) => menu.IsOpen = true;
        return button;
    }

    private static Grid Row(string label, UIElement control, string? help)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var name = new TextBlock { Foreground = Ui.Res<Brush>("TextMuted"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0), Background = Brushes.Transparent, ToolTip = help };
        name.Inlines.Add(new System.Windows.Documents.Run(label));
        if (help is not null)
            name.Inlines.Add(new System.Windows.Documents.InlineUIContainer(new Icon { Data = Ui.Res<Geometry>("I.Info"), Width = 12, Height = 12, Margin = new Thickness(5, 0, 0, 0), Foreground = Ui.Res<Brush>("TextFaint") }) { BaselineAlignment = BaselineAlignment.Center });
        grid.Children.Add(name);
        if (control is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Left; fe.VerticalAlignment = VerticalAlignment.Center; }
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static TextBlock Section(string title) => new() { Text = title, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 12, 0, 2) };

    private static StackPanel Heading(string title, string icon)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(new Tile
        {
            Style = Ui.Res<Style>("IconBadge"), Width = 28, Height = 28, CornerRadius = ThemeManager.Round(8),
            Child = new Icon { Data = Ui.Res<Geometry>(icon), Width = 15, Height = 15 },
        });
        head.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("H3"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return head;
    }

    private static Tile Card(string title, string icon, UIElement body)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(title, icon));
        stack.Children.Add(body);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(18, 14, 18, 14), Child = stack };
    }

    private static Tile Number(string title, string value, string note, string icon, string soft, string deep)
    {
        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Tile
        {
            Width = 24, Height = 24, CornerRadius = ThemeManager.Round(7), Background = Ui.Res<Brush>(soft),
            Child = new Icon { Data = Ui.Res<Geometry>(icon), Width = 13, Height = 13, Foreground = Ui.Res<Brush>(deep) },
        });
        head.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(head);
        stack.Children.Add(new TextBlock { Text = value, FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>(deep), Margin = new Thickness(0, 4, 0, 0) });
        stack.Children.Add(new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(6, 0, 6, 12), Child = stack };
    }

    private static Tile Banner(string soft, string text)
    {
        var banner = Ui.Notice(soft.Replace("Soft", ""), soft == "AccentSoft" ? "I.Info" : "I.Alert", null, text);
        banner.Margin = new Thickness(0, 8, 0, 0);
        return banner;
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("compare", () =>
        {
            S.Sim = "iRacing";
            S.Compare.Clear();
            S.CompareRows.Clear();
            S.Compare.Add(Presets[1].Make());
            S.Compare.Add(Presets[2].Make());
            Recalculate();
            _compareCard.BringIntoView();
            return Task.CompletedTask;
        });
        yield return ("single-curved", () =>
        {
            S.Current = Presets[6].Make();
            BuildInputs();
            Recalculate();
            return Task.CompletedTask;
        });
    }
}

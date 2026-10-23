using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using GoodieBag.Services;
using GoodieBag.Views;

namespace GoodieBag.Controls;

public sealed class NavIcon : Viewbox
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(NavIcon), new PropertyMetadata(null, (d, _) => ((NavIcon)d).Build()));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(NavIcon), new PropertyMetadata(false, (d, _) => ((NavIcon)d).Update()));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(NavIcon), new PropertyMetadata(Brushes.Gray));

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private const double Stroke = 1.8;

    public const string CarGlyph = "car:";

    public static bool IsPicture(string? glyph) => glyph?.StartsWith(CarGlyph, StringComparison.Ordinal) == true;

    private readonly Canvas _canvas = new() { Width = 24, Height = 24 };
    private readonly List<(IAnimatable Target, DependencyProperty Property, double Wrap)> _targets = new();
    private readonly List<(IAnimatable Target, DependencyProperty Property, double Still, double Running)> _switches = new();
    private ParallelTimeline? _timeline;
    private TimeSpan _period;
    private Action? _started, _stopped;
    private ClockGroup? _clock;
    private Window? _window;
    private bool _loaded;
    private SpeedRamp? _ramp;
    private bool _wound;

    private static readonly TimeSpan WindDownTime = TimeSpan.FromSeconds(0.9), WindUpTime = TimeSpan.FromSeconds(0.7);

    private static event Action? RefreshRequested;

    public static void RefreshAll() => RefreshRequested?.Invoke();

    public NavIcon()
    {
        Stretch = Stretch.Uniform;
        Child = _canvas;
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => Update();
        Loaded += (_, _) =>
        {
            _loaded = true;
            RefreshRequested -= Update;
            RefreshRequested += Update;
            Hook(Window.GetWindow(this));
            Update();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            RefreshRequested -= Update;
            Hook(null);
            Update();
        };
    }

    private void Hook(Window? window)
    {
        if (ReferenceEquals(window, _window)) return;
        if (_window is not null) _window.StateChanged -= OnWindowStateChanged;
        _window = window;
        if (_window is not null) _window.StateChanged += OnWindowStateChanged;
        Views.Motion.WindowActiveChanged -= Update;
        if (_window is not null) Views.Motion.WindowActiveChanged += Update;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => Update();

    private bool OnScreen => _loaded && IsVisible && _window?.WindowState != WindowState.Minimized;

    private void Update()
    {
        if (_capturing) return;
        bool run = IsActive && _timeline is not null && Motion.Enabled && OnScreen;
        if (!run)
        {
            StopRamp();
            _wound = false;
            if (_clock is not null)
                Stop(settle: !IsActive && Motion.Enabled && OnScreen);
            return;
        }
        if (!Motion.WindowActive)
        {
            if (_clock is not null && !_wound) WindDown();
            else _wound = true;
            return;
        }
        if (_clock is null)
        {
            Start();
            if (_wound) WindUp(SpeedRamp.Slowest);
            return;
        }
        if (_wound) WindUp(_clock.Controller!.SpeedRatio);
    }

    private void StopRamp()
    {
        _ramp?.Stop();
        _ramp = null;
    }

    private void WindDown()
    {
        StopRamp();
        _wound = true;
        var controller = _clock!.Controller!;
        _ramp = new SpeedRamp(controller, controller.SpeedRatio, () => SpeedRamp.Slowest, WindDownTime, null, () =>
        {
            _ramp = null;
            Stop(settle: true);
        });
    }

    private void WindUp(double from)
    {
        StopRamp();
        _wound = false;
        var controller = _clock!.Controller!;
        controller.SpeedRatio = Math.Max(SpeedRamp.Slowest, from);
        _ramp = new SpeedRamp(controller, from, () => 1, WindUpTime, null, () => _ramp = null);
    }

    private static readonly Duration IntroTime = new(TimeSpan.FromSeconds(0.3));

    private static readonly Duration SettleTime = new(TimeSpan.FromSeconds(0.22));

    private const double Hold = double.PositiveInfinity;

    private void Start()
    {
        var clock = _timeline!.CreateClock();
        for (int i = 0; i < _targets.Count; i++)
            _targets[i].Target.ApplyAnimationClock(_targets[i].Property, (AnimationClock)clock.Children[i]);
        foreach (var (target, property, still, running) in _switches)
        {
            ((DependencyObject)target).SetValue(property, running);
            target.BeginAnimation(property, _capturing ? null
                : new DoubleAnimation(still, running, IntroTime) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop });
        }
        _started?.Invoke();
        _clock = clock;
    }

    private void Stop(bool settle = false)
    {
        if (_clock is null) return;
        var now = new object?[_targets.Count];
        if (settle)
            for (int i = 0; i < _targets.Count; i++)
                now[i] = ((DependencyObject)_targets[i].Target).GetValue(_targets[i].Property);
        _clock.Controller?.Stop();
        foreach (var (target, property, _) in _targets)
            target.ApplyAnimationClock(property, null);
        foreach (var (target, property, still, running) in _switches)
        {
            ((DependencyObject)target).SetValue(property, still);
            target.BeginAnimation(property, settle
                ? new DoubleAnimation(running, still, SettleTime) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop }
                : null);
        }
        if (settle)
            for (int i = 0; i < _targets.Count; i++)
                SettleFrom(_targets[i].Target, _targets[i].Property, _targets[i].Wrap, now[i]);
        _stopped?.Invoke();
        _clock = null;
    }

    private static void SettleFrom(IAnimatable target, DependencyProperty property, double wrap, object? now)
    {
        object rest = ((DependencyObject)target).GetValue(property);
        AnimationTimeline? ease = null;
        if (now is double from && rest is double to && !double.IsNaN(from))
        {
            if (double.IsPositiveInfinity(wrap)) to = from;
            else if (wrap > 0) to += Math.Ceiling((from - to) / wrap - 1e-6) * wrap;
            else if (wrap < 0) to += Math.Floor((from - to) / -wrap + 1e-6) * -wrap;
            if (Math.Abs(from - to) > 0.001 || double.IsPositiveInfinity(wrap))
                ease = new DoubleAnimation(from, to, SettleTime) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop };
        }
        else if (now is Point a && rest is Point b && a != b)
            ease = new PointAnimation(a, b, SettleTime) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop };
        if (ease is not null)
            target.BeginAnimation(property, ease);
    }

    private bool _capturing;

    internal TimeSpan? LoopDuration => _timeline is null ? null : _period;

    internal ClockController? BeginCapture()
    {
        if (_timeline is null) return null;
        Stop();
        _capturing = true;
        Start();
        return _clock?.Controller;
    }

    internal void EndCapture()
    {
        _capturing = false;
        Stop();
        Update();
    }

    private void Build()
    {
        Stop();
        _canvas.Children.Clear();
        _targets.Clear();
        _switches.Clear();
        _timeline = null;
        _started = _stopped = null;
        if (IsPicture(Glyph))
        {
            var image = new Image { Source = CarPictures.Of(Glyph![CarGlyph.Length..]), Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Child = image;
            Update();
            return;
        }
        Child = _canvas;
        switch (Glyph)
        {
            case "I.Home": House(); break;
            case "I.Layers": Layers(); break;
            case "I.Monitor": Pong(); break;
            case "I.Zap": Lightning(); break;
            case "I.HeartPulse": HeartPulse(); break;
            case "I.RaceOverlay": RaceOverlay(); break;
            case "I.Candles": Candles(); break;
            case "I.Compass": Compass(); break;
            case "F.GrrLogo": GrrLogo(Res(Glyph)); break;
            case "I.Overlay": Overlay(); break;
            case "I.Fan": Fan(); break;
            case "I.FormulaWheel": Wheel(); break;
            case "I.Gears": Gears(); break;
            case "I.Tune": Sliders(); break;
            case "I.Stats": Bars(); break;
            case "I.Activity": Pulse(); break;
            case "I.Cpu": Chip(); break;
            case "I.Triple": Screens(); break;
            case "I.Shield": Shield(); break;
            case "I.Folder": Folder(); break;
            case "I.Checkered": Flag(); break;
            case "I.Palette": Palette(); break;
            case "I.Keyboard": Keyboard(); break;
            case { } key when key.StartsWith("F.", StringComparison.Ordinal): Draw(Res(key), fill: true); break;
            case { } key: Draw(Res(key)); break;
        }
        Update();
    }

    private Geometry? Res(string key) => TryFindResource(key) as Geometry;

    private static readonly Dictionary<string, Geometry> Parsed = new();

    private static Geometry G(string data)
    {
        if (!Parsed.TryGetValue(data, out var geometry))
        {
            geometry = Geometry.Parse(data);
            if (geometry.CanFreeze) geometry.Freeze();
            Parsed[data] = geometry;
        }
        return geometry;
    }

    private Path Draw(Geometry? data, Transform? transform = null, bool fill = false, double stroke = Stroke)
    {
        var path = new Path
        {
            Data = data,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        if (transform is not null) path.RenderTransform = transform;
        var color = new Binding(nameof(Foreground)) { Source = this };
        if (fill)
            path.SetBinding(Shape.FillProperty, color);
        else
        {
            path.StrokeThickness = stroke;
            path.SetBinding(Shape.StrokeProperty, color);
        }
        _canvas.Children.Add(path);
        return path;
    }

    private void Loop(double seconds)
    {
        _period = TimeSpan.FromSeconds(seconds);
        _timeline = new ParallelTimeline { Duration = new Duration(_period), RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(_timeline, Showcase.ShowcaseRunner.Active ? PcClass.FrameRate : PcClass.SoftwareRendering ? 20 : 30);
    }

    private void Animate(IAnimatable target, DependencyProperty property, AnimationTimeline animation, double wrap = 0)
    {
        animation.Duration = new Duration(_period);
        _timeline!.Children.Add(animation);
        _targets.Add((target, property, wrap));
    }

    private void Switch(IAnimatable target, DependencyProperty property, double still, double running)
    {
        ((DependencyObject)target).SetValue(property, still);
        _switches.Add((target, property, still, running));
    }

    private static KeyTime At(double seconds) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));

    private static IEasingFunction Ease(EasingMode mode)
    {
        var ease = new SineEase { EasingMode = mode };
        ease.Freeze();
        return ease;
    }

    private static readonly IEasingFunction InOut = Ease(EasingMode.EaseInOut), EaseOut = Ease(EasingMode.EaseOut);

    private static DoubleAnimationUsingKeyFrames Keys(IEasingFunction? ease, params (double T, double V)[] keys)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        foreach (var (t, v) in keys)
            animation.KeyFrames.Add(ease is null ? new LinearDoubleKeyFrame(v, At(t)) : new EasingDoubleKeyFrame(v, At(t), ease));
        return animation;
    }

    private DoubleAnimationUsingKeyFrames Sampled(double phase, Func<double, double> f, int samples = 24)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        double period = _period.TotalSeconds;
        for (int k = 0; k <= samples; k++)
        {
            double u = (double)k / samples;
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(f((u + phase) % 1.0), At(u * period)));
        }
        return animation;
    }

    private PointAnimationUsingKeyFrames SampledPoints(Func<double, Point> f, int samples = 48)
    {
        var animation = new PointAnimationUsingKeyFrames();
        double period = _period.TotalSeconds;
        for (int k = 0; k <= samples; k++)
        {
            double u = (double)k / samples;
            animation.KeyFrames.Add(new LinearPointKeyFrame(f(u % 1.0), At(u * period)));
        }
        return animation;
    }

    private static double Eased(double t, params (double T, double V)[] keys)
    {
        if (t <= keys[0].T) return keys[0].V;
        for (int i = 0; i < keys.Length - 1; i++)
            if (t <= keys[i + 1].T)
            {
                double u = (t - keys[i].T) / (keys[i + 1].T - keys[i].T);
                return keys[i].V + (keys[i + 1].V - keys[i].V) * (0.5 - 0.5 * Math.Cos(Math.PI * u));
            }
        return keys[^1].V;
    }

    private DoubleAnimationUsingKeyFrames Over(Func<double, double> f) => Sampled(0, u => f(u * _period.TotalSeconds), 72);

    private PointAnimationUsingKeyFrames Over(Func<double, Point> f) => SampledPoints(u => f(u * _period.TotalSeconds), 72);

    private void House()
    {
        const double Period = 3.0, Land = 0.46;
        Loop(Period);
        var squash = new ScaleTransform(1, 1, 12, 21);
        var lift = new TranslateTransform();
        var roofLag = new TranslateTransform();
        var walls = new TransformGroup { Children = { squash, lift } };
        var roof = new TransformGroup { Children = { squash, lift, roofLag } };

        var glowShape = new EllipseGeometry(new Point(12, 11), 4.6, 4.6);
        glowShape.Freeze();
        var glow = Accent(glowShape, walls);
        var fade = new RadialGradientBrush();
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0, 0, 0), 0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(90, 0, 0, 0), 0.4));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        fade.Freeze();
        glow.OpacityMask = fade;
        glow.Opacity = 0;
        var pane = new RectangleGeometry(new Rect(10.4, 9.4, 3.2, 3.2));
        pane.Freeze();
        var light = Accent(pane, walls);
        light.Opacity = 0;

        foreach (double side in new[] { -1.0, 1.0 })
        {
            const double DustStart = 0.47, DustLife = 0.55;
            var at = new Point(side < 0 ? 3.6 : 20.4, 20.6);
            var dust = new EllipseGeometry(at, 1, 1);
            dust.Freeze();
            double D(double u) => Math.Min(1, ((u * Period - DustStart) % Period + Period) % Period / DustLife);
            double Out(double u) => 1 - (1 - D(u)) * (1 - D(u));
            var grow = new ScaleTransform(0.6, 0.6, at.X, at.Y);
            var spread = new TranslateTransform();
            var path = Draw(dust, new TransformGroup { Children = { grow, spread } }, fill: true);
            path.Opacity = 0;
            double s = side;
            Animate(spread, TranslateTransform.XProperty, Sampled(0, u => s * 2.0 * Out(u), 144), Hold);
            Animate(spread, TranslateTransform.YProperty, Sampled(0, u => -0.7 * Out(u), 144), Hold);
            Animate(grow, ScaleTransform.ScaleXProperty, Sampled(0, u => 0.6 + 0.7 * Out(u), 144), Hold);
            Animate(grow, ScaleTransform.ScaleYProperty, Sampled(0, u => 0.6 + 0.7 * Out(u), 144), Hold);
            Animate(path, UIElement.OpacityProperty, Sampled(0, u =>
            {
                double p = D(u);
                return 0.85 * (p < 0.08 ? p / 0.08 : p < 0.35 ? 1 : (1 - p) / 0.65);
            }, 144));
        }

        Draw(G("M4 11.8V20a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-8.2M10 21v-4.5a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1V21"), walls);
        Draw(G("M10.4 9.4h3.2v3.2h-3.2z"), walls, stroke: 1.4);
        Draw(G("M2 13.5 12 5l10 8.5M16 8.4V5h2.5v5.5"), roof);

        Animate(squash, ScaleTransform.ScaleYProperty, Keys(InOut, (0, 1), (0.12, 0.95), (0.3, 1.06), (Land, 1), (0.52, 0.84), (0.61, 1.07), (0.71, 0.97), (0.81, 1.01), (0.9, 1), (Period, 1)));
        Animate(squash, ScaleTransform.ScaleXProperty, Keys(InOut, (0, 1), (0.12, 1.03), (0.3, 0.97), (Land, 1), (0.52, 1.1), (0.61, 0.96), (0.71, 1.02), (0.81, 0.995), (0.9, 1), (Period, 1)));
        Animate(lift, TranslateTransform.YProperty, Keys(InOut, (0, 0), (0.12, 0), (0.3, -1.3), (Land, 0), (Period, 0)));
        Animate(roofLag, TranslateTransform.YProperty, Keys(InOut, (0, 0), (0.3, 0.15), (Land, -0.6), (0.56, -0.4), (0.68, 0.35), (0.8, -0.08), (0.9, 0), (Period, 0)));
        Animate(light, UIElement.OpacityProperty, Keys(null, (0, 0), (Land, 0), (0.54, 1), (1.2, 1), (2.1, 0), (Period, 0)));
        Animate(glow, UIElement.OpacityProperty, Keys(null, (0, 0), (Land, 0), (0.56, 1), (0.8, 0.6), (1.2, 0.5), (2.1, 0), (Period, 0)));

        var origin = new Point(17.25, 3.4);
        var puff = new EllipseGeometry(origin, 1.15, 1.15);
        puff.Freeze();
        foreach (double start in new[] { 0.58, 0.8, 1.02, 1.24 })
        {
            const double Life = 1.6;
            double s0 = start;
            double P(double u) => Math.Min(1, ((u * Period - s0) % Period + Period) % Period / Life);
            var grow = new ScaleTransform(1, 1, origin.X, origin.Y);
            var drift = new TranslateTransform();
            var path = Draw(puff, new TransformGroup { Children = { grow, drift } }, fill: true);
            path.Opacity = 0;
            Animate(drift, TranslateTransform.XProperty, Sampled(0, u => 3.6 * P(u), 72), Hold);
            Animate(drift, TranslateTransform.YProperty, Sampled(0, u => -0.9 * Math.Sin(Math.PI * Math.Min(1, 1.6 * P(u))) + 0.2 * P(u), 72), Hold);
            Animate(grow, ScaleTransform.ScaleXProperty, Sampled(0, u => 0.75 + 1.35 * P(u), 72), Hold);
            Animate(grow, ScaleTransform.ScaleYProperty, Sampled(0, u => 0.75 + 1.35 * P(u), 72), Hold);
            Animate(path, UIElement.OpacityProperty, Sampled(0, u =>
            {
                double p = P(u);
                return 0.9 * (p < 0.08 ? p / 0.08 : p < 0.55 ? 1 : (1 - p) / 0.45);
            }, 72));
        }
    }

    private Path Accent(Geometry data, Transform transform)
    {
        var path = new Path { Data = data, RenderTransform = transform };
        path.SetResourceReference(Shape.FillProperty, "Accent");
        _canvas.Children.Add(path);
        return path;
    }

    private void Layers()
    {
        const double Period = 2.0;
        Loop(Period);
        var parts = new[]
        {
            (Data: "M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z", Sink: 2.6, Lift: 1.2, Bounce: 0.5, Delay: 0.0),
            (Data: "M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12", Sink: 1.5, Lift: 0.8, Bounce: 0.35, Delay: 0.06),
            (Data: "M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17", Sink: 0.5, Lift: 0.45, Bounce: 0.15, Delay: 0.12),
        };
        var moves = parts.Select(_ => new TranslateTransform()).ToArray();

        var top = G(parts[0].Data);
        var glow = new Path
        {
            Data = top, RenderTransform = moves[0], StrokeThickness = 3.6, Opacity = 0,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        glow.SetResourceReference(Shape.StrokeProperty, "Accent");
        _canvas.Children.Add(glow);
        var white = new Path { Data = top, RenderTransform = moves[0], Fill = Brushes.White, Opacity = 0 };
        _canvas.Children.Add(white);
        Animate(white, UIElement.OpacityProperty, Keys(null, (0, 0), (1.3, 0), (1.37, 0.95), (1.45, 0.7), (1.6, 0), (Period, 0)));
        Animate(glow, UIElement.OpacityProperty, Keys(null, (0, 0), (1.3, 0), (1.38, 0.75), (1.5, 0.35), (1.64, 0), (Period, 0)));

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var move = moves[i];
            Draw(G(part.Data), move);
            double d = part.Delay;
            Animate(move, TranslateTransform.YProperty, Keys(InOut,
                (0, 0), (0.05 + d, 0), (0.6 + d, part.Sink), (0.8 + d, -part.Lift), (0.98 + d, part.Bounce),
                (1.12 + d, -0.35 * part.Bounce), (1.26 + d, 0), (Period, 0)));
        }
    }

    private void Pong()
    {
        Draw(Res("I.Monitor"));
        Loop(3.2);
        var left = new TranslateTransform();
        var right = new TranslateTransform();
        var ball = new TranslateTransform();
        Draw(G("M5.6 8.5v3"), left, stroke: 1.5);
        Draw(G("M18.4 8.5v3"), right, stroke: 1.5);
        var dot = new EllipseGeometry(new Point(12, 10), 0.85, 0.85);
        dot.Freeze();
        Draw(dot, ball, fill: true);
        Animate(ball, TranslateTransform.XProperty, Keys(null, (0, 0), (0.4, 4.9), (1.2, -4.9), (2.0, 4.9), (2.8, -4.9), (3.2, 0)));
        Animate(ball, TranslateTransform.YProperty, Keys(null, (0, 0), (0.2667, 3.4), (0.8, -3.4), (1.3333, 3.4), (1.8667, -3.4), (2.4, 3.4), (2.9333, -3.4), (3.2, 0)));
        Animate(left, TranslateTransform.YProperty, Sampled(0, u => 2.404 * Math.Sin(2 * Math.PI * u), 48));
        Animate(right, TranslateTransform.YProperty, Keys(InOut, (0, 0), (0.4, 1.7), (2.0, -1.7), (3.2, 0)));
    }

    private void Lightning()
    {
        const double Period = 2.4;
        Loop(Period);
        foreach (var (angle, delay) in new (double, double)[] { (-55, 0), (-30, 0.02), (-78, 0.03), (125, 0), (150, 0.02), (102, 0.03) })
        {
            double ux = Math.Cos(angle * Math.PI / 180), uy = Math.Sin(angle * Math.PI / 180), s = 0.58 + delay;
            var ray = new LineGeometry(new Point(12 + ux * 7.4, 12 + uy * 7.4), new Point(12 + ux * 9.2, 12 + uy * 9.2));
            ray.Freeze();
            var fly = new TranslateTransform();
            var path = Draw(ray, fly, stroke: 1.5);
            path.Opacity = 0;
            Animate(fly, TranslateTransform.XProperty, Keys(null, (0, 0), (s, 0), (s + 0.25, 2.2 * ux), (Period, 2.2 * ux)), Hold);
            Animate(fly, TranslateTransform.YProperty, Keys(null, (0, 0), (s, 0), (s + 0.25, 2.2 * uy), (Period, 2.2 * uy)), Hold);
            Animate(path, UIElement.OpacityProperty, Keys(null, (0, 0), (s - 0.001, 0), (s, 1), (s + 0.1, 0.9), (s + 0.25, 0), (Period, 0)));
        }
        var pop = new ScaleTransform(1, 1, 12, 12);
        var jolt = new TranslateTransform();
        var bolt = new TransformGroup { Children = { pop, jolt } };
        var branches = new[]
        {
            (Data: "F1M7.96 6.40 5.96 5.88 5.31 4.00 3.24 3.63 2.10 1.70 2.96 3.97 4.69 4.60 5.04 6.72 7.44 7.80ZM5.22 5.92 3.95 7.17 2.50 7.10 4.05 7.63 5.78 6.68Z",
             Flicker: new (double, double)[] { (0, 0), (0.57, 0), (0.58, 1), (0.64, 0.35), (0.68, 1), (0.74, 0.3), (0.78, 0.8), (0.88, 0), (Period, 0) }),
            (Data: "F1M16.04 17.60 18.04 18.12 18.69 20.00 20.76 20.37 21.90 22.30 21.04 20.03 19.31 19.40 18.96 17.28 16.56 16.20ZM18.78 18.08 20.05 16.83 21.50 16.90 19.95 16.37 18.22 17.32Z",
             Flicker: new (double, double)[] { (0, 0), (0.57, 0), (0.58, 1), (0.64, 0.5), (0.68, 0.85), (0.74, 0.2), (0.78, 0.6), (0.88, 0), (Period, 0) }),
        };
        foreach (var branch in branches)
        {
            var path = Draw(G(branch.Data), bolt, fill: true);
            path.Opacity = 0;
            Animate(path, UIElement.OpacityProperty, Keys(null, branch.Flicker));
        }
        var flash = Draw(Res("I.Zap"), bolt, fill: true);
        flash.Opacity = 0;
        var outline = Draw(Res("I.Zap"), bolt);
        var size = new (double, double)[] { (0, 1), (0.3, 1), (0.38, 1.05), (0.44, 0.96), (0.48, 1.0), (0.53, 0.94), (0.58, 1.11), (0.64, 1.03), (0.68, 1.08), (0.74, 1.0), (0.78, 1.07), (0.86, 0.91), (0.98, 0.95), (1.4, 1), (Period, 1) };
        Animate(pop, ScaleTransform.ScaleXProperty, Keys(null, size));
        Animate(pop, ScaleTransform.ScaleYProperty, Keys(null, size));
        Animate(jolt, TranslateTransform.XProperty, Keys(null, (0, 0), (0.3, 0), (0.38, 0.3), (0.44, -0.15), (0.53, 0), (0.58, -0.4), (0.64, 0.4), (0.68, -0.3), (0.74, 0.2), (0.78, -0.1), (0.86, 0), (Period, 0)));
        Animate(jolt, TranslateTransform.YProperty, Keys(null, (0, 0), (0.3, 0), (0.38, -0.2), (0.44, 0.1), (0.53, 0), (0.58, 0.05), (0.64, -0.25), (0.68, 0.15), (0.74, -0.1), (0.86, 0), (Period, 0)));
        Animate(flash, UIElement.OpacityProperty, Keys(null, (0, 0), (0.3, 0), (0.38, 0.85), (0.44, 0.1), (0.48, 0.45), (0.53, 0), (0.58, 1), (0.64, 0.4), (0.68, 0.9), (0.74, 0.2), (0.78, 0.7), (0.86, 0.1), (0.98, 0), (Period, 0)));
        Animate(outline, UIElement.OpacityProperty, Keys(null, (0, 1), (0.38, 1), (0.44, 0.45), (0.48, 0.8), (0.53, 0.4), (0.58, 1), (0.64, 0.7), (0.68, 1), (0.74, 0.6), (0.78, 1), (0.86, 0.85), (0.98, 1), (Period, 1)));
    }

    private void Bars()
    {
        const double Period = 2.8;
        Loop(Period);
        Draw(G("M3 3v16a2 2 0 0 0 2 2h16"));
        foreach (var (x, top, delay) in new[] { (8.0, 14.0, 0.0), (13.0, 5.0, 0.14), (18.0, 9.0, 0.28) })
        {
            var bar = new LineGeometry(new Point(x, 17), new Point(x, top));
            bar.Freeze();
            var grow = new ScaleTransform(1, 1, x, 17);
            Draw(bar, grow);
            Animate(grow, ScaleTransform.ScaleYProperty, Keys(InOut, (0, 1), (0.2 + delay, 1), (0.55 + delay, 0.3), (1.0 + delay, 1.12), (1.25 + delay, 1), (Period, 1)));
        }
    }

    private const string HeartShape = "M2 9.5a5.5 5.5 0 0 1 9.591-3.676 0.56 0.56 0 0 0 0.818 0A5.49 5.49 0 0 1 22 9.5c0 2.29-1.5 4-3 5.5l-5.492 5.313a2 2 0 0 1-3 0.019L5 15c-1.5-1.5-3-3.2-3-5.5";

    private const string PulseShape = "M3.22 13H9.5l0.5-1 2 4.5 2-7 1.5 3.5h5.27";

    private void HeartPulse()
    {
        const double Period = 2.0, Repeat = 22, Tail = 9, Start = 3.22, End = 20.77;
        Loop(Period);
        var beat = new ScaleTransform(1, 1, 12, 12.5);
        Draw(G(HeartShape), beat);
        var full = Draw(G(PulseShape), beat);
        Switch(full, UIElement.OpacityProperty, 1, 0.32);
        var sweep = new TranslateTransform();
        var mask = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(Start - Repeat, 0), EndPoint = new Point(Start, 0),
            SpreadMethod = GradientSpreadMethod.Repeat, Transform = sweep,
        };
        Color off = Color.FromArgb(0, 0, 0, 0), on = Color.FromArgb(255, 0, 0, 0);
        mask.GradientStops.Add(new GradientStop(off, 0));
        mask.GradientStops.Add(new GradientStop(off, 1 - Tail / Repeat));
        mask.GradientStops.Add(new GradientStop(on, 1 - 0.6 / Repeat));
        mask.GradientStops.Add(new GradientStop(on, 1));
        var lit = Draw(G(PulseShape), beat);
        lit.OpacityMask = mask;
        Switch(lit, UIElement.OpacityProperty, 0, 1);
        Animate(sweep, TranslateTransform.XProperty, Keys(null, (0, 0), (Period, Repeat)), Hold);
        var dot = new EllipseGeometry(new Point(Start, 13), 1.3, 1.3);
        dot.Freeze();
        var head = new TranslateTransform();
        var pop = new ScaleTransform(1, 1, Start, 13);
        var headDot = Draw(dot, new TransformGroup { Children = { pop, head, beat } }, fill: true);
        headDot.Opacity = 0;
        Switch(pop, ScaleTransform.ScaleXProperty, 0.1, 1);
        Switch(pop, ScaleTransform.ScaleYProperty, 0.1, 1);
        var headX = new List<(double T, double V)>();
        var headY = new List<(double T, double V)>();
        foreach (var (x, y) in new (double X, double Y)[] { (Start, 13), (9.5, 13), (10, 12), (12, 16.5), (14, 9.5), (15.5, 13), (End, 13) })
        {
            double t = (x - Start) / Repeat * Period;
            headX.Add((t, x - Start));
            headY.Add((t, y - 13));
        }
        headX.Add((Period - 0.02, End - Start));
        headX.Add((Period - 0.01, 0));
        headX.Add((Period, 0));
        headY.Add((Period, 0));
        Animate(head, TranslateTransform.XProperty, Keys(null, headX.ToArray()), Hold);
        Animate(head, TranslateTransform.YProperty, Keys(null, headY.ToArray()), Hold);
        double end = (End - Start) / Repeat * Period;
        Animate(headDot, UIElement.OpacityProperty, Keys(null, (0, 1), (end, 1), (end + 0.05, 0), (Period - 0.005, 0), (Period, 1)));
        var thump = new (double, double)[] { (0, 1), (0.76, 1), (0.84, 1.13), (0.94, 0.97), (1.04, 1.08), (1.2, 1), (Period, 1) };
        Animate(beat, ScaleTransform.ScaleXProperty, Keys(InOut, thump));
        Animate(beat, ScaleTransform.ScaleYProperty, Keys(InOut, thump));
    }

    private void Pulse()
    {
        const double Period = 2.2, Repeat = 30, Tail = 9;
        Loop(Period);
        var full = Draw(Res("I.Activity"));
        Switch(full, UIElement.OpacityProperty, 1, 0.3);
        var sweep = new TranslateTransform();
        var mask = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(2 - Repeat, 0), EndPoint = new Point(2, 0),
            SpreadMethod = GradientSpreadMethod.Repeat, Transform = sweep,
        };
        Color off = Color.FromArgb(0, 0, 0, 0), on = Color.FromArgb(255, 0, 0, 0);
        mask.GradientStops.Add(new GradientStop(off, 0));
        mask.GradientStops.Add(new GradientStop(off, 1 - Tail / Repeat));
        mask.GradientStops.Add(new GradientStop(on, 1 - 0.6 / Repeat));
        mask.GradientStops.Add(new GradientStop(on, 1));
        var lit = Draw(Res("I.Activity"));
        lit.OpacityMask = mask;
        Switch(lit, UIElement.OpacityProperty, 0, 1);
        Animate(sweep, TranslateTransform.XProperty, Keys(null, (0, 0), (Period, Repeat)), Hold);
    }

    private void Chip()
    {
        const double Period = 2.4;
        Loop(Period);
        Draw(G("M6 4h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM9 9h6v6H9z"));
        var core = Draw(G("M9 9h6v6H9z"), fill: true);
        core.Opacity = 0;
        Animate(core, UIElement.OpacityProperty, Keys(InOut, (0, 0), (0.15, 0.6), (0.6, 0), (Period, 0)));
        string[] pins = { "M9 2v2", "M15 2v2", "M20 9h2", "M20 15h2", "M15 20v2", "M9 20v2", "M2 15h2", "M2 9h2" };
        for (int i = 0; i < pins.Length; i++)
        {
            var pin = Draw(G(pins[i]));
            double phase = (1 - (double)i / pins.Length) % 1;
            Animate(pin, UIElement.OpacityProperty, Sampled(phase, u => u < 0.25 ? 1 : 0.3 + 0.7 * Math.Max(0, (u - 0.85) / 0.15), 48));
        }
    }

    private void Screens()
    {
        const double Period = 3.2;
        Loop(Period);
        var pop = new ScaleTransform(1, 1, 12, 12);
        Draw(G("M9 5.5h6v11H9zM10.5 19.5h3"), pop);
        var popKeys = new (double, double)[] { (0, 1), (2.0, 1), (2.18, 1.06), (2.45, 1), (Period, 1) };
        Animate(pop, ScaleTransform.ScaleXProperty, Keys(InOut, popKeys));
        Animate(pop, ScaleTransform.ScaleYProperty, Keys(InOut, popKeys));
        var fold = new (double T, double V)[] { (0, 0), (0.3, 0), (1.0, 95), (1.6, 95), (2.1, -12), (2.35, 5), (2.55, -2), (2.7, 0), (Period, 0) };
        foreach (double side in new[] { -1.0, 1.0 })
        {
            double hinge = side < 0 ? 7.5 : 16.5, s = side;
            var top = new LineSegment(new Point(hinge + side * 6, 7), true);
            var bottom = new LineSegment(new Point(hinge + side * 6, 16), true);
            var figure = new PathFigure { StartPoint = new Point(hinge, 7), IsClosed = true, Segments = { top, bottom, new LineSegment(new Point(hinge, 16), true) } };
            Draw(new PathGeometry { Figures = { figure } });
            Point Outer(double t, double edge)
            {
                double angle = Eased(t, fold) * Math.PI / 180;
                return new Point(hinge + s * 6 * Math.Cos(angle), 11.5 + edge * 4.5 * (1 + 0.22 * Math.Abs(Math.Sin(angle))));
            }
            Animate(top, LineSegment.PointProperty, Over(t => Outer(t, -1)));
            Animate(bottom, LineSegment.PointProperty, Over(t => Outer(t, 1)));
        }
    }

    private void Shield()
    {
        const double Period = 2.6;
        Loop(Period);
        var spread = new ScaleTransform(1, 1, 12, 12);
        var ring = Draw(Res("I.Shield"), spread, stroke: 1.2);
        ring.Opacity = 0;
        Animate(spread, ScaleTransform.ScaleXProperty, Keys(EaseOut, (0, 1), (0.3, 1), (1.2, 1.4), (Period, 1.4)), Hold);
        Animate(spread, ScaleTransform.ScaleYProperty, Keys(EaseOut, (0, 1), (0.3, 1), (1.2, 1.4), (Period, 1.4)), Hold);
        Animate(ring, UIElement.OpacityProperty, Keys(null, (0, 0), (0.3, 0.8), (1.2, 0), (Period, 0)));
        var pop = new ScaleTransform(1, 1, 12, 12);
        Draw(Res("I.Shield"), pop);
        Animate(pop, ScaleTransform.ScaleXProperty, Keys(InOut, (0, 1), (0.15, 0.9), (0.32, 1.08), (0.5, 0.98), (0.65, 1), (Period, 1)));
        Animate(pop, ScaleTransform.ScaleYProperty, Keys(InOut, (0, 1), (0.15, 0.9), (0.32, 1.08), (0.5, 0.98), (0.65, 1), (Period, 1)));
    }

    private void Folder()
    {
        const double Period = 3.0;
        Loop(Period);
        var dip = new ScaleTransform(1, 1, 12, 20);
        Draw(Res("I.Folder"), dip);
        var landings = new List<(double, double)> { (0, 1) };
        foreach (var (y, width, start) in new[] { (10.5, 10.0, 0.1), (13.5, 10.0, 0.4), (16.5, 6.0, 0.7) })
        {
            var line = new LineGeometry(new Point(7, y), new Point(7 + width, y));
            line.Freeze();
            var fall = new TranslateTransform();
            var file = Draw(line, new TransformGroup { Children = { fall, dip } });
            file.Opacity = 0;
            double land = start + 0.3;
            Animate(fall, TranslateTransform.YProperty, Keys(Ease(EasingMode.EaseIn), (0, -8), (start, -8), (land, 0), (Period, 0)), Hold);
            Animate(file, UIElement.OpacityProperty, Keys(null, (0, 0), (start, 0), (start + 0.12, 1), (2.3, 1), (2.7, 0), (Period, 0)));
            landings.Add((land, 1));
            landings.Add((land + 0.08, 0.95));
            landings.Add((land + 0.22, 1));
        }
        landings.Add((Period, 1));
        Animate(dip, ScaleTransform.ScaleYProperty, Keys(InOut, landings.ToArray()));
    }

    private void Flag()
    {
        const double Period = 1.8;
        Loop(Period);
        Draw(G("M4 22V15"));
        var wave = new SkewTransform(0, 0, 4, 9);
        var squeeze = new ScaleTransform(1, 1, 4, 9);
        Draw(G("M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1zM4 9s1-1 4-1 5 2 8 2 4-1 4-1M8 2v12M12 3v12M16 4v12"), new TransformGroup { Children = { wave, squeeze } });
        Animate(wave, SkewTransform.AngleYProperty, Sampled(0, u => 6 * Math.Sin(2 * Math.PI * u), 48));
        Animate(squeeze, ScaleTransform.ScaleXProperty, Sampled(0, u => 1 - 0.05 * (1 - Math.Cos(4 * Math.PI * u)) / 2, 48));
    }

    private void Palette()
    {
        const double Period = 2.4;
        Loop(Period);
        var rock = new RotateTransform(0, 12, 12);
        Draw(G("M12 22a1 1 0 0 1 0-20 10 9 0 0 1 10 9 5 5 0 0 1-5 5h-2.25a1.75 1.75 0 0 0-1.4 2.8l.3.4a1.75 1.75 0 0 1-1.4 2.8z"), rock);
        Animate(rock, RotateTransform.AngleProperty, Keys(InOut, (0, 0), (0.5, -7), (1.1, 5), (1.6, 0), (Period, 0)));
        foreach (var (x, y, start) in new[] { (6.5, 12.5, 0.2), (8.5, 7.5, 0.45), (13.5, 6.5, 0.7), (17.5, 10.5, 0.95) })
        {
            var pop = new ScaleTransform(1, 1, x, y);
            var dot = new EllipseGeometry(new Point(x, y), 1.1, 1.1);
            dot.Freeze();
            Draw(dot, new TransformGroup { Children = { pop, rock } }, fill: true);
            Animate(pop, ScaleTransform.ScaleXProperty, Keys(InOut, (0, 1), (start, 1), (start + 0.15, 1.8), (start + 0.4, 1), (Period, 1)));
            Animate(pop, ScaleTransform.ScaleYProperty, Keys(InOut, (0, 1), (start, 1), (start + 0.15, 1.8), (start + 0.4, 1), (Period, 1)));
        }
    }

    private void Keyboard()
    {
        const double Period = 2.4, Space = 1.3;
        Loop(Period);
        var settle = new TranslateTransform();
        Draw(G("M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z"), settle);
        static (double, double)[] Press(double at, double down, double up = 0) => new[] { (0, up), (at, up), (at + 0.06, down), (at + 0.22, up), (Period, up) };
        var keys = new[] { ("M6 8h.01", 6.0, 8.0, 0.1), ("M14 8h.01", 14.0, 8.0, 0.24), ("M12 12h.01", 12.0, 12.0, 0.38), ("M18 8h.01", 18.0, 8.0, 0.55), ("M8 12h.01", 8.0, 12.0, 0.7), ("M10 8h.01", 10.0, 8.0, 0.84), ("M16 12h.01", 16.0, 12.0, 1.0) };
        foreach (var (data, x, y, at) in keys)
        {
            var ring = new EllipseGeometry(new Point(x, y), 0.8, 0.8);
            var wave = Draw(ring, stroke: 1);
            wave.Opacity = 0;
            Animate(ring, EllipseGeometry.RadiusXProperty, Keys(EaseOut, (0, 0.8), (at, 0.8), (at + 0.38, 3), (Period, 3)), Hold);
            Animate(ring, EllipseGeometry.RadiusYProperty, Keys(EaseOut, (0, 0.8), (at, 0.8), (at + 0.38, 3), (Period, 3)), Hold);
            Animate(wave, UIElement.OpacityProperty, Keys(null, (0, 0), (at, 0), (at + 0.01, 0.7), (at + 0.38, 0), (Period, 0)));
            var squash = new ScaleTransform(1, 1, x, y);
            var sink = new TranslateTransform();
            Draw(G(data), new TransformGroup { Children = { squash, sink } });
            Animate(sink, TranslateTransform.YProperty, Keys(InOut, Press(at, 0.9)));
            Animate(squash, ScaleTransform.ScaleXProperty, Keys(InOut, Press(at, 0.5, 1)));
            Animate(squash, ScaleTransform.ScaleYProperty, Keys(InOut, Press(at, 0.5, 1)));
        }
        var bar = new ScaleTransform(1, 1, 12, 16);
        var drop = new TranslateTransform();
        Draw(G("M7 16h10"), new TransformGroup { Children = { bar, drop } });
        Animate(drop, TranslateTransform.YProperty, Keys(InOut, Press(Space, 0.9)));
        Animate(bar, ScaleTransform.ScaleXProperty, Keys(InOut, Press(Space, 0.88, 1)));
        Animate(settle, TranslateTransform.YProperty, Keys(InOut, Press(Space, 0.35)));
    }

    private void RaceOverlay()
    {
        const double Period = 4.0, Laps = 4;
        Loop(Period);
        Draw(G("M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z"));
        var road = Draw(G("M5.5 20l5.7-6.8M18.5 20l-5.7-6.8"), stroke: 1.5);
        road.Opacity = 0.55;
        for (int i = 0; i < 3; i++)
        {
            double phase = i / 3.0;
            double P(double u) => (u * Laps + phase) % 1;
            static double Top(double p) => 13.6 + 5.4 * p * p;
            static double Bottom(double p) => Math.Min(19.2, Top(p) + 0.5 + 1.8 * p);
            var dash = new LineGeometry(new Point(12, Top(phase)), new Point(12, Bottom(phase)));
            var path = Draw(dash);
            Animate(dash, LineGeometry.StartPointProperty, SampledPoints(u => new Point(12, Top(P(u))), 128));
            Animate(dash, LineGeometry.EndPointProperty, SampledPoints(u => new Point(12, Bottom(P(u))), 128));
            Animate(path, Shape.StrokeThicknessProperty, Sampled(0, u => 1 + 1.1 * P(u), 128));
            Animate(path, UIElement.OpacityProperty, Sampled(0, u =>
            {
                double p = P(u);
                return p < 0.05 || p > 0.92 ? 0 : Math.Min(1, p * 3) * (p < 0.8 ? 1 : (0.92 - p) / 0.12);
            }, 128));
        }
        double[] slots = { 7.4, 9.6, 11.8 };
        var swap = new (double T, double V)[] { (0, 0), (1.6, 0), (2.0, 1), (3.4, 1), (3.8, 0), (Period, 0) };
        for (int i = 0; i < 3; i++)
        {
            var place = new TranslateTransform(0, slots[i]);
            var row = Draw(G("M5 0h.01M7.2 0h3.3"), place, stroke: 1.6);
            row.Opacity = i == 1 ? 1 : 0.5;
            if (i == 2) continue;
            double from = slots[i], to = slots[1 - i];
            Animate(place, TranslateTransform.YProperty, Keys(InOut, swap.Select(k => (k.T, from + (to - from) * k.V)).ToArray()));
        }
    }

    private void Candles()
    {
        const double Period = 3.2;
        Loop(Period);
        Draw(G("M3 3v16a2 2 0 0 0 2 2h16"));
        var candles = new[]
        {
            (X: 9.0, Up: 4.0, Down: 2.0,
             Top: new (double T, double V)[] { (0, 9), (0.8, 10.5), (1.6, 7.5), (2.4, 9.5), (Period, 9) },
             Bottom: new (double T, double V)[] { (0, 15), (0.8, 16), (1.6, 14), (2.4, 15.5), (Period, 15) }),
            (X: 17.0, Up: 2.0, Down: 3.0,
             Top: new (double T, double V)[] { (0, 5), (0.8, 6.5), (1.6, 4), (2.4, 7), (Period, 5) },
             Bottom: new (double T, double V)[] { (0, 13), (0.8, 12), (1.6, 13.5), (2.4, 11.5), (Period, 13) }),
        };
        foreach (var c in candles)
        {
            double x = c.X, top0 = c.Top[0].V, height0 = c.Bottom[0].V - top0, up = c.Up, down = c.Down;
            var top = c.Top;
            var bottom = c.Bottom;
            var high = new LineGeometry(new Point(x, top0 - up), new Point(x, top0));
            var low = new LineGeometry(new Point(x, top0 + height0), new Point(x, top0 + height0 + down));
            Draw(high);
            Draw(low);
            Animate(high, LineGeometry.StartPointProperty, Over(t => new Point(x, Eased(t, top) - up)));
            Animate(high, LineGeometry.EndPointProperty, Over(t => new Point(x, Eased(t, top))));
            Animate(low, LineGeometry.StartPointProperty, Over(t => new Point(x, Eased(t, bottom))));
            Animate(low, LineGeometry.EndPointProperty, Over(t => new Point(x, Eased(t, bottom) + down)));
            var body = new RectangleGeometry(new Rect(x - 2, top0, 4, height0), 1, 1);
            body.Freeze();
            var stretch = new ScaleTransform(1, 1, x, top0);
            var move = new TranslateTransform();
            var shape = new TransformGroup { Children = { stretch, move } };
            var fill = Draw(body, shape, fill: true);
            fill.Opacity = 0;
            Draw(body, shape);
            Animate(stretch, ScaleTransform.ScaleYProperty, Over(t => (Eased(t, bottom) - Eased(t, top)) / height0));
            Animate(move, TranslateTransform.YProperty, Over(t => Eased(t, top) - top0));
            Animate(fill, UIElement.OpacityProperty, Over(t => Math.Clamp((Eased(t - 0.05, top) - Eased(t, top)) * 6, 0, 0.45)));
        }
    }

    private void Compass()
    {
        const double Period = 3.2, Reach = 10, From = 134.5, Sweep = 89;
        Loop(Period);
        var drawn = new (double T, double V)[] { (0.5, 0), (1.9, 1) };
        double Drawn(double t) => Math.Clamp(Eased(t, drawn), 0, 1);
        double dash = Reach * Sweep * Math.PI / 180 / Stroke;
        var arc = Draw(G("M4.865 12A10 10 0 0 0 19.136 12"));
        arc.StrokeDashArray = new DoubleCollection { dash, dash * 1.1 };
        arc.StrokeDashCap = PenLineCap.Round;
        Animate(arc, Shape.StrokeDashOffsetProperty, Over(t => dash * (1 - Drawn(t))));
        Animate(arc, UIElement.OpacityProperty, Over(t => Drawn(t) <= 0 ? 0 : Eased(t, (0, 1), (2.2, 1), (2.8, 0), (Period, 0))));
        Point Tip(double t)
        {
            double angle = (From - Sweep * Drawn(t)) * Math.PI / 180;
            return new Point(Reach * Math.Cos(angle), Reach * Math.Sin(angle));
        }
        var pencil = new EllipseGeometry(new Point(12, 5), 1, 1);
        pencil.Freeze();
        var trace = new TranslateTransform();
        var tip = Draw(pencil, trace, fill: true);
        tip.Opacity = 0;
        Animate(trace, TranslateTransform.XProperty, Over(t => Tip(t).X), Hold);
        Animate(trace, TranslateTransform.YProperty, Over(t => Tip(t).Y), Hold);
        Animate(tip, UIElement.OpacityProperty, Over(t => t > 0.5 && t < 1.9 ? 1 : 0));
        var rock = new RotateTransform(0, 12, 5);
        var spread = new (double T, double V)[] { (0, 0), (0.3, 5), (0.45, 3.5), (2.7, 3.5), (3.0, 0), (Period, 0) };
        foreach (var (data, side) in new[] { ("M11.02 6.74 3 21", 1.0), ("M12.99 6.74 21 21", -1.0) })
        {
            var open = new RotateTransform(0, 12, 5);
            Draw(G(data), new TransformGroup { Children = { open, rock } });
            Animate(open, RotateTransform.AngleProperty, Keys(InOut, spread.Select(k => (k.T, side * k.V)).ToArray()));
        }
        var hinge = new EllipseGeometry(new Point(12, 5), 2, 2);
        hinge.Freeze();
        Draw(hinge, rock);
        Animate(rock, RotateTransform.AngleProperty, Keys(InOut, (0, 0), (0.5, -14), (1.9, 14), (2.6, 0), (Period, 0)));
    }

    private void GrrLogo(Geometry? logo)
    {
        if (logo is null) return;
        const double Period = 3.6, Baseline = 15.1, Rushes = 9;
        Loop(Period);
        var rush = new (double T, double V)[] { (0, 0), (0.45, 0), (0.62, 1), (1.15, 1), (1.45, 0), (Period, 0) };
        foreach (double y in new[] { 6.4, 17.6 })
            for (int i = 0; i < 3; i++)
            {
                double phase = i / 3.0 + (y > 10 ? 0.17 : 0), row = y;
                double Left(double u) => 26 - 32 * ((u * Rushes + phase) % 1);
                var streak = new LineGeometry(new Point(0, y), new Point(0, y));
                var path = Draw(streak, stroke: 1.2);
                path.Opacity = 0;
                Animate(streak, LineGeometry.StartPointProperty, SampledPoints(u => new Point(Math.Clamp(Left(u), 0, 24), row), 216));
                Animate(streak, LineGeometry.EndPointProperty, SampledPoints(u => new Point(Math.Clamp(Left(u) + 3.2, 0, 24), row), 216));
                Animate(path, UIElement.OpacityProperty, Sampled(0, u =>
                {
                    double x = Left(u);
                    return x < 0.3 || x > 23.7 ? 0 : 0.75 * Eased(u * Period, rush);
                }, 216));
            }
        var slide = new TranslateTransform();
        var flat = PathGeometry.CreateFromGeometry(logo);
        double[] delays = { 0, 0.05, 0.1, 0.05 };
        for (int i = 0; i < flat.Figures.Count; i++)
        {
            double d = delays[Math.Min(i, delays.Length - 1)];
            var letter = new PathGeometry(new[] { flat.Figures[i].Clone() }) { FillRule = flat.FillRule };
            letter.Freeze();
            var lean = new SkewTransform(0, 0, 0, Baseline);
            Draw(letter, new TransformGroup { Children = { lean, slide } }, fill: true);
            Animate(lean, SkewTransform.AngleXProperty, Keys(InOut, (0, 0), (0.3 + d, 0), (0.5 + d, 6), (0.75 + d, -14), (1.0 + d, 4), (1.2 + d, -1), (1.35 + d, 0), (Period, 0)));
        }
        Animate(slide, TranslateTransform.XProperty, Keys(InOut, (0, 0), (0.3, 0), (0.5, -0.9), (0.75, 1.0), (0.95, -0.3), (1.15, 0.1), (1.3, 0), (Period, 0)));
        var pass = new TranslateTransform(-8, 0);
        var band = PathGeometry.CreateFromGeometry(G("M0 8.5h2.6l-2.4 7h-2.6z"));
        band.Transform = pass;
        _canvas.Children.Add(new Path { Data = band, Fill = Brushes.White, Opacity = 0.85, Clip = logo });
        Animate(pass, TranslateTransform.XProperty, Keys(null, (0, -8), (1.5, -8), (2.1, 28), (Period, 28)), Hold);
    }

    private void Overlay()
    {
        Draw(G("M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z"));
        var move = new TranslateTransform();
        Draw(G("M6.5 7.5h6v4h-6z"), move);
        Loop(3.6);
        Animate(move, TranslateTransform.XProperty, Keys(InOut, (0, 0), (0.55, 0), (0.9, 5), (1.45, 5), (1.8, 5), (2.35, 5), (2.7, 0), (3.25, 0), (3.6, 0)));
        Animate(move, TranslateTransform.YProperty, Keys(InOut, (0, 0), (0.55, 0), (0.9, 0), (1.45, 0), (1.8, 5), (2.35, 5), (2.7, 5), (3.25, 5), (3.6, 0)));
    }

    private void Fan()
    {
        var turn = new RotateTransform(0, 12, 12);
        Draw(Res("I.Fan"), turn);
        const double Period = 3.0;
        Loop(Period);
        double boost = (FanTurns / Period - FanSlow) / (5.0 / 16);
        static double Sin6(double u)
        {
            double x = Math.PI * u;
            return (5 * x / 16 - 15 * Math.Sin(2 * x) / 64 + 3 * Math.Sin(4 * x) / 64 - Math.Sin(6 * x) / 192) / Math.PI;
        }
        var spin = new DoubleAnimationUsingKeyFrames();
        const int Samples = 96;
        for (int k = 0; k <= Samples; k++)
        {
            double u = (double)k / Samples;
            spin.KeyFrames.Add(new LinearDoubleKeyFrame(360 * Period * (FanSlow * u + boost * Sin6(u)), At(u * Period)));
        }
        Animate(turn, RotateTransform.AngleProperty, spin, 90);
    }

    private const double FanSlow = 0.5;

    private const double FanTurns = 2.75;

    private void Wheel()
    {
        var fit = new ScaleTransform(WheelFit, WheelFit, 12, 12);
        fit.Freeze();
        var turn = new RotateTransform(0, 12, 12);
        Draw(Res("I.FormulaWheel"), new TransformGroup { Children = { fit, turn } });
        Loop(5.4);
        Animate(turn, RotateTransform.AngleProperty, Keys(InOut,
            (0, 0), (0.1, 0), (0.6, -45), (0.8, -45), (1.65, 100), (1.9, 100), (2.8, -75), (3.05, -75), (3.6, -20), (3.8, -20),
            (4.35, 30), (4.5, 30), (5.0, 0), (5.4, 0)));
    }

    private const double WheelFit = 0.91;

    private void Gears()
    {
        Loop(4.0);
        const double Teeth = 3;
        var gears = new[]
        {
            (Data: "M14.07 7.3A6.5 6.5 0 0 1 14.07 7.96L12.23 8.58A4.75 4.75 0 0 1 11.9 9.6L13.02 11.18A6.5 6.5 0 0 1 12.64 11.71L10.79 11.13A4.75 4.75 0 0 1 9.92 11.76L9.9 13.7A6.5 6.5 0 0 1 9.28 13.9L8.12 12.35A4.75 4.75 0 0 1 7.04 12.35L5.88 13.9A6.5 6.5 0 0 1 5.26 13.7L5.24 11.76A4.75 4.75 0 0 1 4.37 11.13L2.52 11.71A6.5 6.5 0 0 1 2.14 11.18L3.26 9.6A4.75 4.75 0 0 1 2.93 8.58L1.09 7.96A6.5 6.5 0 0 1 1.09 7.3L2.93 6.68A4.75 4.75 0 0 1 3.26 5.66L2.14 4.08A6.5 6.5 0 0 1 2.52 3.55L4.37 4.13A4.75 4.75 0 0 1 5.24 3.5L5.26 1.56A6.5 6.5 0 0 1 5.88 1.36L7.04 2.91A4.75 4.75 0 0 1 8.12 2.91L9.28 1.36A6.5 6.5 0 0 1 9.9 1.56L9.92 3.5A4.75 4.75 0 0 1 10.79 4.13L12.64 3.55A6.5 6.5 0 0 1 13.02 4.08L11.9 5.66A4.75 4.75 0 0 1 12.23 6.68Z",
             Hole: "M9.08 7.63a1.5 1.5 0 1 1-3 0a1.5 1.5 0 1 1 3 0z", X: 7.58, Y: 7.63, N: 10, Turn: 1),
            (Data: "M12.29 9.52A5.4 5.4 0 0 1 12.5 8.87L14.44 8.9A3.65 3.65 0 0 1 15.11 8.12L14.77 6.21A5.4 5.4 0 0 1 15.38 5.9L16.72 7.3A3.65 3.65 0 0 1 17.75 7.22L18.86 5.63A5.4 5.4 0 0 1 19.51 5.84L19.48 7.78A3.65 3.65 0 0 1 20.26 8.45L22.17 8.11A5.4 5.4 0 0 1 22.48 8.72L21.08 10.06A3.65 3.65 0 0 1 21.16 11.09L22.75 12.2A5.4 5.4 0 0 1 22.54 12.85L20.6 12.82A3.65 3.65 0 0 1 19.93 13.6L20.27 15.51A5.4 5.4 0 0 1 19.66 15.82L18.32 14.42A3.65 3.65 0 0 1 17.29 14.5L16.18 16.09A5.4 5.4 0 0 1 15.53 15.88L15.56 13.94A3.65 3.65 0 0 1 14.78 13.27L12.87 13.61A5.4 5.4 0 0 1 12.56 13L13.96 11.66A3.65 3.65 0 0 1 13.88 10.63Z",
             Hole: "M18.77 10.86a1.25 1.25 0 1 1-2.5 0a1.25 1.25 0 1 1 2.5 0z", X: 17.52, Y: 10.86, N: 8, Turn: -1),
            (Data: "M11.26 21.32A4.3 4.3 0 0 1 10.84 20.73L12.11 19.26A2.55 2.55 0 0 1 12.02 18.31L10.52 17.09A4.3 4.3 0 0 1 10.83 16.43L12.73 16.79A2.55 2.55 0 0 1 13.51 16.24L13.83 14.33A4.3 4.3 0 0 1 14.55 14.27L15.19 16.1A2.55 2.55 0 0 1 16.05 16.5L17.86 15.82A4.3 4.3 0 0 1 18.28 16.41L17.01 17.88A2.55 2.55 0 0 1 17.1 18.83L18.6 20.05A4.3 4.3 0 0 1 18.29 20.71L16.39 20.35A2.55 2.55 0 0 1 15.61 20.9L15.29 22.81A4.3 4.3 0 0 1 14.57 22.87L13.93 21.04A2.55 2.55 0 0 1 13.07 20.64Z",
             Hole: "M15.46 18.57a0.9 0.9 0 1 1-1.8 0a0.9 0.9 0 1 1 1.8 0z", X: 14.56, Y: 18.57, N: 6, Turn: 1),
        };
        foreach (var gear in gears)
        {
            var turn = new RotateTransform(0, gear.X, gear.Y);
            Draw(G(gear.Data), turn, stroke: 1.4);
            Draw(G(gear.Hole), stroke: 1.2);
            Animate(turn, RotateTransform.AngleProperty, new DoubleAnimation(0, gear.Turn * 360 * Teeth / gear.N, _period), gear.Turn * 360.0 / gear.N);
        }
    }

    private void Sliders()
    {
        Loop(4.0);
        const double Middle = 12, Reach = 5.5, Gap = 2.6;
        var tracks = new[]
        {
            (Y: 6.0, At: 8.0, Turns: 1, Phase: Math.Asin((8.0 - Middle) / Reach)),
            (Y: 12.0, At: 15.0, Turns: 1, Phase: Math.PI - Math.Asin((15.0 - Middle) / Reach)),
            (Y: 18.0, At: 11.0, Turns: 2, Phase: Math.Asin((11.0 - Middle) / Reach)),
        };
        foreach (var track in tracks)
        {
            double y = track.Y, x = track.At, phase = track.Phase;
            int turns = track.Turns;
            var left = new LineGeometry(new Point(3, y), new Point(x - Gap, y));
            var right = new LineGeometry(new Point(x + Gap, y), new Point(21, y));
            var knob = new EllipseGeometry(new Point(x, y), 2, 2);
            Draw(left);
            Draw(right);
            Draw(knob);
            double KnobX(double p) => Middle + Reach * Math.Sin(2 * Math.PI * turns * p + phase);
            Animate(left, LineGeometry.EndPointProperty, SampledPoints(p => new Point(KnobX(p) - Gap, y)));
            Animate(right, LineGeometry.StartPointProperty, SampledPoints(p => new Point(KnobX(p) + Gap, y)));
            Animate(knob, EllipseGeometry.CenterProperty, SampledPoints(p => new Point(KnobX(p), y)));
        }
    }
}

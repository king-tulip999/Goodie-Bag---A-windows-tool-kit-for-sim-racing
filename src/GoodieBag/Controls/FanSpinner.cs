using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GoodieBag.Controls;

public sealed class FanSpinner : Grid
{
    private readonly RotateTransform _target;
    private readonly Icon _rotor;
    private readonly Icon? _body, _drop;
    private readonly Brush? _onBrush, _offBrush;
    private readonly Icon[]? _trails;
    private readonly RotateTransform[]? _trailOffsets;
    private readonly System.Windows.Shapes.Ellipse? _disc;
    private Clock? _clock;
    private double? _rpm;
    private double _percent;
    private double _speed;
    private double _blur;
    private bool _animate;
    private bool? _running;
    private Window? _window;
    private SpeedRamp? _ramp;
    private bool _wound;

    private static readonly TimeSpan WindDownTime = TimeSpan.FromSeconds(1.2), WindUpTime = TimeSpan.FromSeconds(0.8);

    private bool IsPump => _drop is not null;

    private static readonly double[] TrailOpacity = { 0.5, 0.32, 0.18 };
    private const double TrailSpread = 10;

    public FanSpinner(Geometry fanIcon, Brush color, double size = 22) : this(fanIcon, color, size, trails: true) { }

    private FanSpinner(Geometry rotor, Brush color, double size, bool trails)
    {
        Width = size;
        Height = size;
        _target = new RotateTransform();
        if (trails)
        {
            _disc = new System.Windows.Shapes.Ellipse { Fill = color, Width = size * 0.82, Height = size * 0.82, Opacity = 0, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            Children.Add(_disc);
            _trails = new Icon[TrailOpacity.Length];
            _trailOffsets = new RotateTransform[TrailOpacity.Length];
            for (int i = TrailOpacity.Length - 1; i >= 0; i--)
            {
                _trailOffsets[i] = new RotateTransform();
                _trails[i] = new Icon
                {
                    Data = rotor, Foreground = color, Width = size, Height = size, StrokeThickness = 1.9, Opacity = 0, Visibility = Visibility.Collapsed,
                    RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new TransformGroup { Children = { _trailOffsets[i], _target } },
                    CacheMode = Cached(),
                };
                Children.Add(_trails[i]);
            }
        }
        _rotor = new Icon { Data = rotor, Foreground = color, Width = size, Height = size, StrokeThickness = 1.9, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _target, CacheMode = Cached() };
        Children.Add(_rotor);
        IsVisibleChanged += (_, _) => Update();
        Loaded += (_, _) => Hook(Window.GetWindow(this));
        Unloaded += (_, _) => Hook(null);
    }

    private static BitmapCache Cached() => new() { RenderAtScale = 2 };

    private FanSpinner(Geometry body, Geometry impeller, Geometry drop, Brush running, Brush off, double size)
        : this(impeller, off, size, trails: false)
    {
        _onBrush = running;
        _offBrush = off;
        _rotor.RenderTransformOrigin = new Point(10.0 / 24, 13.0 / 24);
        _body = new Icon { Data = body, Foreground = off, Width = size, Height = size, StrokeThickness = 1.9 };
        _drop = new Icon { Data = drop, Foreground = off, Width = size, Height = size, StrokeThickness = 1.9, Visibility = Visibility.Hidden };
        Children.Insert(0, _body);
        Children.Add(_drop);
    }

    public static FanSpinner Pump(Brush running, Brush off, double size = 22)
    {
        var res = Application.Current;
        return new FanSpinner((Geometry)res.FindResource("I.PumpBody"), (Geometry)res.FindResource("I.PumpImpeller"),
            (Geometry)res.FindResource("I.PumpDrop"), running, off, size);
    }

    private void Hook(Window? window)
    {
        if (ReferenceEquals(window, _window)) return;
        if (_window is not null) _window.StateChanged -= OnWindowStateChanged;
        _window = window;
        if (_window is not null) _window.StateChanged += OnWindowStateChanged;
        Views.Motion.WindowActiveChanged -= Update;
        if (_window is not null) Views.Motion.WindowActiveChanged += Update;
        Update();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => Update();

    private bool OnScreen => IsVisible && _window?.WindowState != WindowState.Minimized;

    public void SetRpm(double? rpm, bool animate) => SetSpeed(null, rpm, 0, animate);

    public void SetSpeed(double? percent, double? rpm, double maxRpm, bool animate)
    {
        _rpm = rpm;
        _percent = SpinPercent(percent, rpm, maxRpm);
        _animate = animate;
        Update();
    }

    private void DropClock()
    {
        StopRamp();
        _wound = false;
        if (_clock is null) return;
        _clock.Controller?.Stop();
        _target.ApplyAnimationClock(RotateTransform.AngleProperty, null);
        _drop?.ApplyAnimationClock(UIElement.OpacityProperty, null);
        _clock = null;
    }

    private void StopRamp()
    {
        _ramp?.Stop();
        _ramp = null;
    }

    private double TargetSpeed() => IsPump
        ? _rpm is { } r && r >= 50 ? 1 / PumpPeriod(r) : 0
        : Spin(_percent).TurnsPerSecond;

    private double TargetBlur() => IsPump ? 0 : Spin(_percent).Blur;

    private void Update()
    {
        if (IsPump) PumpLook();
        if (!OnScreen)
        {
            DropClock();
            return;
        }
        double target = TargetSpeed();
        if (!_animate || !Views.Motion.Enabled || target <= 0)
        {
            StopRamp();
            _wound = false;
            if (IsPump) DropClock();
            else _clock?.Controller?.Pause();
            ApplyBlur(0);
            return;
        }
        if (!Views.Motion.WindowActive)
        {
            if (_clock is null) { _wound = true; ApplyBlur(0); }
            else if (!_wound) WindDown();
            return;
        }
        if (_clock is null)
        {
            CreateClock(_wound ? SpeedRamp.Slowest : target);
            if (!_wound)
            {
                _speed = target;
                ApplyBlur(TargetBlur());
                return;
            }
        }
        if (_wound)
        {
            WindUp();
            return;
        }
        if (_ramp is not null) return;
        if (Math.Abs(target - _speed) > _speed * 0.04 + 0.02)
        {
            _clock!.Controller!.SpeedRatio = target;
            _speed = target;
        }
        _clock!.Controller!.Resume();
        ApplyBlur(TargetBlur());
    }

    private void CreateClock(double speed)
    {
        if (IsPump)
        {
            var group = new ParallelTimeline { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(group, IconFrameRate);
            group.Children.Add(new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)));
            group.Children.Add(new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.5)) { AutoReverse = true });
            var clock = group.CreateClock();
            _target.ApplyAnimationClock(RotateTransform.AngleProperty, (AnimationClock)clock.Children[0]);
            _drop!.ApplyAnimationClock(UIElement.OpacityProperty, (AnimationClock)clock.Children[1]);
            _clock = clock;
        }
        else
        {
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(spin, IconFrameRate);
            var clock = spin.CreateClock();
            _target.ApplyAnimationClock(RotateTransform.AngleProperty, clock);
            _clock = clock;
        }
        _clock.Controller!.SpeedRatio = speed;
    }

    private void WindDown()
    {
        StopRamp();
        _wound = true;
        var controller = _clock!.Controller!;
        double blur = _blur;
        _ramp = new SpeedRamp(controller, controller.SpeedRatio, () => SpeedRamp.Slowest, WindDownTime,
            e => ApplyBlur(blur * (1 - e)),
            () =>
            {
                _ramp = null;
                controller.Pause();
                ApplyBlur(0);
            });
    }

    private void WindUp()
    {
        StopRamp();
        _wound = false;
        var controller = _clock!.Controller!;
        double from = controller.SpeedRatio;
        controller.Resume();
        _ramp = new SpeedRamp(controller, from, TargetSpeed, WindUpTime,
            e => ApplyBlur(TargetBlur() * e),
            () =>
            {
                _ramp = null;
                _speed = TargetSpeed();
                ApplyBlur(TargetBlur());
            });
    }

    private const int IconFrameRate = 30;

    private void ApplyBlur(double blur)
    {
        if (_trails is null || _trailOffsets is null || _disc is null) return;
        if (Math.Abs(blur - _blur) < 0.03 && (blur > 0) == (_blur > 0)) return;
        _blur = blur;
        var shown = blur > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _trails.Length; i++)
        {
            _trails[i].Visibility = shown;
            _trails[i].Opacity = TrailOpacity[i] * blur;
            _trailOffsets[i].Angle = -(i + 1) * TrailSpread * blur;
        }
        double top = Math.Clamp((blur - 0.6) / 0.4, 0, 1);
        _disc.Visibility = top > 0 ? Visibility.Visible : Visibility.Collapsed;
        _disc.Opacity = 0.22 * top;
        _rotor.Opacity = 1 - 0.25 * top;
    }

    internal static double SpinPercent(double? percent, double? rpm, double maxRpm)
    {
        double value = percent is { } p ? p
            : rpm is { } r && r >= 50 ? r / (maxRpm > 0 ? maxRpm : 3000) * 100
            : 0;
        return double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 100);
    }

    internal static (double TurnsPerSecond, double Blur) Spin(double percent)
    {
        if (double.IsNaN(percent) || percent < 1) return (0, 0);
        double p = Math.Min(percent, 100) / 100;
        double turns = 0.25 + 6.75 * Math.Pow(p, 1.6);
        double b = Math.Clamp((p - 0.5) / 0.5, 0, 1);
        return (turns, b * b * (3 - 2 * b));
    }

    private void PumpLook()
    {
        bool running = _rpm is { } r && r >= 50;
        if (_running == running) return;
        _running = running;
        var brush = (running ? _onBrush : _offBrush)!;
        _rotor.Foreground = brush;
        _body!.Foreground = brush;
        _drop!.Foreground = brush;
        _drop.Visibility = running ? Visibility.Visible : Visibility.Hidden;
    }

    internal static double PumpPeriod(double rpm) => Math.Clamp(2.6 - rpm / 1800, 0.8, 2.6);
}

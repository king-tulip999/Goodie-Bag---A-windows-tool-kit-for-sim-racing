using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using GoodieBag.Theme;

namespace GoodieBag.Controls;

public sealed class LoadingSpinner : Grid
{
    private const string Circuit = "M6 19 L17 19 C19 19 20 18 20 16 L20 9 C20 6 18 4 15 4 C12 4 11 6 11 8 C11 10 9 11 7 11 C5 11 4 12 4 14 L4 17 C4 18.5 5 19 6 19 Z";

    private readonly RotateTransform _rotate = new();
    private readonly Path? _head, _tail;
    private readonly double _lap;
    private AnimationClock? _clock;
    private Window? _window;
    private bool _spinning;

    public LoadingSpinner(double size = 40)
    {
        Width = Height = size;
        if (ThemeManager.Current.RacingDecor)
        {
            double t = Math.Max(2.5, size / 14);
            var shape = Geometry.Parse(Circuit).Clone();
            double length = Length(shape) * size / 24;
            shape.Transform = new ScaleTransform(size / 24, size / 24);
            shape.Freeze();
            _lap = length / t;
            double head = Math.Min(length * 0.16, 14) / t, tail = length * 0.22 / t;
            Children.Add(Track(shape, t, "MeterTrack"));
            _tail = Track(shape, t, "Accent");
            _tail.Opacity = 0.35;
            _tail.StrokeDashArray = new DoubleCollection { 0, _lap - tail, tail, 0 };
            Children.Add(_tail);
            _head = Track(shape, t, "Accent");
            _head.StrokeDashArray = new DoubleCollection { head, _lap - head };
            Children.Add(_head);
        }
        else
        {
            double stroke = Math.Max(3, size / 10), r = (size - stroke) / 2, c = size / 2;
            Children.Add(new Ellipse { Stroke = (Brush)Application.Current.Resources["MeterTrack"], StrokeThickness = stroke });
            var arc = new PathGeometry(new[]
            {
                new PathFigure(new Point(c, c - r), new[] { new ArcSegment(new Point(c - r, c), new Size(r, r), 0, true, SweepDirection.Clockwise, true) }, false),
            });
            Children.Add(new Path
            {
                Data = arc, Stroke = (Brush)Application.Current.Resources["Accent"], StrokeThickness = stroke,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = _rotate, RenderTransformOrigin = new Point(0.5, 0.5),
            });
        }
        IsVisibleChanged += (_, _) => Update();
        Loaded += (_, _) => Hook(Window.GetWindow(this));
        Unloaded += (_, _) => Hook(null);
    }

    private static Path Track(Geometry shape, double thickness, string brush) => new()
    {
        Data = shape, StrokeThickness = thickness, Stroke = (Brush)Application.Current.Resources[brush],
        StrokeLineJoin = PenLineJoin.Round, StrokeDashCap = PenLineCap.Flat,
    };

    private static double Length(Geometry shape)
    {
        double total = 0;
        foreach (var figure in shape.GetFlattenedPathGeometry().Figures)
        {
            var last = figure.StartPoint;
            foreach (var segment in figure.Segments)
            {
                IEnumerable<Point> points = segment switch
                {
                    PolyLineSegment poly => poly.Points,
                    LineSegment line => new[] { line.Point },
                    _ => Array.Empty<Point>(),
                };
                foreach (var p in points)
                {
                    total += (p - last).Length;
                    last = p;
                }
            }
            if (figure.IsClosed) total += (figure.StartPoint - last).Length;
        }
        return total;
    }

    private void Hook(Window? window)
    {
        if (ReferenceEquals(window, _window)) return;
        if (_window is not null) _window.StateChanged -= OnWindowStateChanged;
        _window = window;
        if (_window is not null) _window.StateChanged += OnWindowStateChanged;
        Update();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        bool on = IsVisible && _window?.WindowState != WindowState.Minimized && (Services.AppServices.Current?.Animations ?? true);
        if (on == _spinning) return;
        _spinning = on;
        Animate(on);
    }

    private void Animate(bool on)
    {
        if (_head is null || _tail is null)
        {
            _rotate.BeginAnimation(RotateTransform.AngleProperty, on
                ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever }
                : null);
            return;
        }

        if (_clock is not null)
        {
            _clock.Controller?.Stop();
            _head.ApplyAnimationClock(Shape.StrokeDashOffsetProperty, null);
            _tail.ApplyAnimationClock(Shape.StrokeDashOffsetProperty, null);
            _clock = null;
        }
        if (!on || !(Services.AppServices.Current?.Animations ?? true))
            return;
        _clock = new DoubleAnimation(0, -_lap, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever }.CreateClock();
        _head.ApplyAnimationClock(Shape.StrokeDashOffsetProperty, _clock);
        _tail.ApplyAnimationClock(Shape.StrokeDashOffsetProperty, _clock);
    }
}

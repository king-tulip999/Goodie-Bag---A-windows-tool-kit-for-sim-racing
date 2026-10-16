using System.Windows;
using System.Windows.Media;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal static class Dividers
{
    private static Color _accent;
    private static (Brush Halo, Brush Core, Brush Fade)? _brushes, _upright;

    public static void Draw(DrawingContext dc, double left, double right, double y)
    {
        if (right - left < 40 || Brushes(vertical: false) is not var (halo, core, fade)) return;
        dc.PushOpacityMask(fade);
        dc.DrawRectangle(halo, null, new Rect(left, y - 5, right - left, 11));
        dc.DrawRectangle(core, null, new Rect(left, y, right - left, 1));
        dc.Pop();
    }

    public static void DrawVertical(DrawingContext dc, double x, double top, double bottom)
    {
        if (bottom - top < 40 || Brushes(vertical: true) is not var (halo, core, fade)) return;
        dc.PushOpacityMask(fade);
        dc.DrawRectangle(halo, null, new Rect(x - 5, top, 11, bottom - top));
        dc.DrawRectangle(core, null, new Rect(x, top, 1, bottom - top));
        dc.Pop();
    }

    private static ThemeSpec? _spec;

    private static (Brush, Brush, Brush)? Brushes(bool vertical)
    {
        if (Ui.Res<Brush>("Accent") is not SolidColorBrush { Color: var accent }) return null;
        if (_accent != accent || !ReferenceEquals(_spec, ThemeManager.Current))
        {
            _accent = accent;
            _spec = ThemeManager.Current;
            _brushes = _upright = null;
        }
        return vertical ? _upright ??= Make(accent, true) : _brushes ??= Make(accent, false);
    }

    private static (Brush, Brush, Brush) Make(Color accent, bool vertical)
    {
        var spec = ThemeManager.Current;
        double glow = spec.GlowStrength * (spec.Dark ? 0.22 : 0.16), line = spec.Dark ? 0.7 : 0.85;
        Color A(double alpha) => Color.FromArgb((byte)(255 * alpha), accent.R, accent.G, accent.B);
        Point across = vertical ? new Point(1, 0) : new Point(0, 1), along = vertical ? new Point(0, 1) : new Point(1, 0);
        var halo = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = across };
        halo.GradientStops.Add(new GradientStop(A(0), 0));
        halo.GradientStops.Add(new GradientStop(A(glow), 0.5));
        halo.GradientStops.Add(new GradientStop(A(0), 1));
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = along };
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.18));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.82));
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        var core = new SolidColorBrush(A(line));
        halo.Freeze();
        fade.Freeze();
        core.Freeze();
        return (halo, core, fade);
    }
}

internal sealed class DividerLine : FrameworkElement
{
    public DividerLine()
    {
        IsHitTestVisible = false;
        Height = 11;
    }

    protected override void OnRender(DrawingContext dc) => Dividers.Draw(dc, 0, ActualWidth, Math.Floor(ActualHeight / 2));
}

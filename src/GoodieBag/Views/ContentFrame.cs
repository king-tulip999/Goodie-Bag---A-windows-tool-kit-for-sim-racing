using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GoodieBag.Views;

internal sealed class ContentFrame : Decorator
{
    private readonly ScaleTransform _scale = new();
    private double _designWidth = double.PositiveInfinity;
    private bool _centered;

    public double DesignWidth
    {
        get => _designWidth;
        set { if (_designWidth.Equals(value)) return; _designWidth = value; InvalidateMeasure(); }
    }

    public bool Centered
    {
        get => _centered;
        set { if (_centered == value) return; _centered = value; InvalidateArrange(); }
    }

    public double Scale => _scale.ScaleX;

    internal static (double Scale, double Width) Fit(double available, double design, double minScale)
    {
        if (double.IsInfinity(available) || double.IsInfinity(design) || design <= 0) return (1, available);
        double scale = Math.Clamp(available / design, Math.Clamp(minScale, 0.5, 1), 1);
        return (scale, Math.Min(design, available / scale));
    }

    protected override Size MeasureOverride(Size available)
    {
        if (Child is not FrameworkElement child) return default;
        var (scale, width) = Fit(available.Width, _designWidth, 1);
        if (Math.Abs(_scale.ScaleX - scale) > 0.0005) _scale.ScaleX = _scale.ScaleY = scale;
        if (!ReferenceEquals(child.LayoutTransform, _scale)) child.LayoutTransform = _scale;
        child.Measure(new Size(double.IsInfinity(width) ? width : width * scale, available.Height));
        double w = double.IsInfinity(available.Width) ? child.DesiredSize.Width : available.Width;
        return new Size(w, child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Child is not FrameworkElement child) return final;
        var (scale, width) = Fit(final.Width, _designWidth, 1);
        double w = Math.Min(final.Width, width * scale);
        double x = _centered ? Math.Max(0, (final.Width - w) / 2) : 0;
        child.Arrange(new Rect(x, 0, w, final.Height));
        return final;
    }
}

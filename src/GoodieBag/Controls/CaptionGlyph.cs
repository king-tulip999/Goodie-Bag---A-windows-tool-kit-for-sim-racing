using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace GoodieBag.Controls;

public enum CaptionKind { Minimize, Maximize, Restore, Close }

public sealed class CaptionGlyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(CaptionKind), typeof(CaptionGlyph), new FrameworkPropertyMetadata(CaptionKind.Minimize, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(CaptionGlyph), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public CaptionKind Kind
    {
        get => (CaptionKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private Point _placed = new(double.NaN, double.NaN);

    public CaptionGlyph()
    {
        Width = Height = 16;
        IsHitTestVisible = false;
        LayoutUpdated += (_, _) =>
        {
            if (DeviceTransform() is { } m && new Point(m.OffsetX, m.OffsetY) != _placed) InvalidateVisual();
        };
    }

    private Matrix? DeviceTransform()
    {
        if (PresentationSource.FromVisual(this) is not { CompositionTarget: { } target } source || source.RootVisual is not Visual root) return null;
        try { return TransformToAncestor(root) is MatrixTransform t ? t.Matrix * target.TransformToDevice : null; }
        catch (InvalidOperationException) { return null; }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        InvalidateVisual();
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        InvalidateVisual();
    }

    internal static (int Size, int Line) Metrics(double deviceScale)
    {
        return (Math.Max(8, (int)Math.Round(10 * deviceScale)), Math.Max(1, (int)Math.Round(deviceScale)));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (DeviceTransform() is not { HasInverse: true } toDevice || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target) return;
        _placed = new Point(toDevice.OffsetX, toDevice.OffsetY);
        var (size, line) = Metrics(target.TransformToDevice.M11);
        var centre = toDevice.Transform(new Point(ActualWidth / 2, ActualHeight / 2));
        double left = Math.Round(centre.X - size / 2.0), top = Math.Round(centre.Y - size / 2.0);
        var back = toDevice;
        back.Invert();
        dc.PushTransform(new MatrixTransform(back));
        var pen = new Pen(Foreground, line);
        pen.Freeze();
        double h = line / 2.0;
        switch (Kind)
        {
            case CaptionKind.Minimize:
                double y = top + Math.Floor((size - line) / 2.0) + h;
                dc.DrawLine(pen, new Point(left, y), new Point(left + size, y));
                break;
            case CaptionKind.Maximize:
                dc.DrawRectangle(null, pen, new Rect(left + h, top + h, size - line, size - line));
                break;
            case CaptionKind.Restore:
                int inset = Math.Max(2, (int)Math.Round(size * 0.2));
                dc.DrawRectangle(null, pen, new Rect(left + h, top + inset + h, size - inset - line, size - inset - line));
                var corner = new StreamGeometry();
                using (var g = corner.Open())
                {
                    g.BeginFigure(new Point(left + inset + h, top + inset), false, false);
                    g.LineTo(new Point(left + inset + h, top + h), true, false);
                    g.LineTo(new Point(left + size - h, top + h), true, false);
                    g.LineTo(new Point(left + size - h, top + size - inset - h), true, false);
                    g.LineTo(new Point(left + size - inset, top + size - inset - h), true, false);
                }
                corner.Freeze();
                dc.DrawGeometry(null, pen, corner);
                break;
            case CaptionKind.Close:
                var cross = new Pen(Foreground, line * 1.15) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
                cross.Freeze();
                dc.DrawLine(cross, new Point(left, top), new Point(left + size, top + size));
                dc.DrawLine(cross, new Point(left + size, top), new Point(left, top + size));
                break;
        }
        dc.Pop();
    }
}

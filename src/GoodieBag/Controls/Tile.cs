using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Theme;

namespace GoodieBag.Controls;

public class Tile : Border
{
    public static readonly DependencyProperty FramedProperty = DependencyProperty.RegisterAttached(
        "Framed", typeof(bool), typeof(Tile), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static bool GetFramed(DependencyObject d) => (bool)d.GetValue(FramedProperty);

    public static void SetFramed(DependencyObject d, bool value) => d.SetValue(FramedProperty, value);

    public static readonly DependencyProperty ChamferProperty = DependencyProperty.RegisterAttached(
        "Chamfer", typeof(bool), typeof(Tile), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static bool GetChamfer(DependencyObject d) => (bool)d.GetValue(ChamferProperty);

    public static void SetChamfer(DependencyObject d, bool value) => d.SetValue(ChamferProperty, value);

    public bool Chamfer
    {
        get => GetChamfer(this);
        set => SetChamfer(this, value);
    }

    public static readonly DependencyProperty OutsetProperty = DependencyProperty.RegisterAttached(
        "Outset", typeof(double), typeof(Tile), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static double GetOutset(DependencyObject d) => (double)d.GetValue(OutsetProperty);

    public static void SetOutset(DependencyObject d, double value) => d.SetValue(OutsetProperty, value);

    public bool CardOutline { get; set; }

    public static Geometry Shape(Rect bounds, CornerRadius radius)
    {
        double r = Math.Min(radius.TopLeft, Math.Min(bounds.Width, bounds.Height) / 2);
        var g = new RectangleGeometry(bounds, r, r);
        g.Freeze();
        return g;
    }

    public static Geometry CardShape(Rect bounds, double radius)
    {
        double chamfer = ThemeManager.Current.CardChamfer;
        double cut = ChamferFor(bounds.Width, bounds.Height, chamfer);
        if (cut > 0)
            return Build(ctx => AddOctagon(ctx, bounds, new Thickness(0), cut, stroked: true));
        return Shape(bounds, chamfer > 0 ? new CornerRadius(0) : ThemeManager.Round(radius));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var spec = ThemeManager.Current;
        double w = ActualWidth, h = ActualHeight;
        double cut = w <= 0 || h <= 0 ? 0
            : GetFramed(this) || CardOutline ? ChamferFor(w, h, spec.CardChamfer)
            : GetChamfer(this) ? ControlCut(w, h, spec.ControlChamfer, GetOutset(this))
            : 0;
        if (cut > 0 || IsSquareWithEdge) DrawChamfered(dc, cut);
        else base.OnRender(dc);
    }

    private bool IsSquareWithEdge
    {
        get
        {
            var r = CornerRadius;
            var e = BorderThickness;
            return r.TopLeft == 0 && r.TopRight == 0 && r.BottomLeft == 0 && r.BottomRight == 0 && BorderBrush is not null
                && (e.Left > 0 || e.Top > 0 || e.Right > 0 || e.Bottom > 0);
        }
    }

    private const double Sqrt2 = 1.4142135623730951;

    private static double ChamferFor(double w, double h, double chamfer) => chamfer > 0 && Math.Min(w, h) >= chamfer * 3 ? chamfer : 0;

    internal static double ControlCut(double w, double h, double chamfer, double outset = 0)
    {
        double inner = Math.Min(w, h) - 2 * outset;
        if (chamfer <= 0 || inner <= 0) return 0;
        double cut = Math.Min(chamfer, inner / 3);
        return cut < 0.75 ? 0 : cut + outset * (2 - Sqrt2);
    }

    public static Geometry Chamfered(Rect bounds, double cut, double inset = 0) =>
        Build(ctx => AddOctagon(ctx, bounds, new Thickness(inset), cut));

    public static Geometry ChamferedRing(Rect bounds, double cut, double width) =>
        Build(ctx =>
        {
            AddOctagon(ctx, bounds, new Thickness(0), cut);
            AddOctagon(ctx, bounds, new Thickness(width), cut);
        });

    private sealed class Outline
    {
        public required (double W, double H, double Cut, Thickness Edge, Vector Inset) Key { get; init; }
        public Geometry? Ring { get; init; }
        public required Geometry Fill { get; init; }
    }

    private Outline? _outline;

    private void DrawChamfered(DrawingContext dc, double cut)
    {
        double w = ActualWidth, h = ActualHeight;
        var (e, inset) = DeviceEdge(BorderThickness);
        var key = (w, h, cut, e, inset);
        if (_outline is null || !_outline.Key.Equals(key))
            _outline = BuildOutline(key);
        double x0 = inset.X, y0 = inset.Y, x1 = w - inset.X, y1 = h - inset.Y;
        dc.PushGuidelineSet(new GuidelineSet(new[] { x0, x0 + e.Left, x1 - e.Right, x1 }, new[] { y0, y0 + e.Top, y1 - e.Bottom, y1 }));
        if (_outline.Ring is { } ring && BorderBrush is { } border)
            dc.DrawGeometry(border, null, ring);
        if (Background is { } background)
            dc.DrawGeometry(background, null, _outline.Fill);
        dc.Pop();
    }

    private bool _awaitingSource;

    private (Thickness Edge, Vector Inset) DeviceEdge(Thickness edge)
    {
        if (PresentationSource.FromVisual(this) is not { RootVisual: { } root, CompositionTarget: { } target })
        {
            if (!_awaitingSource)
            {
                _awaitingSource = true;
                PresentationSource.AddSourceChangedHandler(this, OnSourceChanged);
            }
            return (edge, default);
        }
        var toRoot = Matrix.Identity;
        DependencyObject? at = this;
        while (at is Visual v && !ReferenceEquals(v, root))
        {
            var t = v is FrameworkElement fe ? fe.LayoutTransform : VisualTreeHelper.GetTransform(v);
            if (t is not null && !t.Value.IsIdentity) toRoot.Append(t.Value);
            at = VisualTreeHelper.GetParent(v);
        }
        if (!ReferenceEquals(at, root)) return (edge, default);
        var unit = new Vector(toRoot.M11 + toRoot.M21, toRoot.M12 + toRoot.M22);
        var device = Services.ScreenshotRunner.Active ? Matrix.Identity : target.TransformToDevice;
        double sx = Math.Abs(unit.X) * device.M11, sy = Math.Abs(unit.Y) * device.M22;
        if (sx < 0.01 || sy < 0.01) return (edge, default);
        static double Whole(double v, double scale) => v <= 0 ? 0 : Math.Max(1, Math.Round(v * scale)) / scale;
        return (new Thickness(Whole(edge.Left, sx), Whole(edge.Top, sy), Whole(edge.Right, sx), Whole(edge.Bottom, sy)), new Vector(0.5 / sx, 0.5 / sy));
    }

    private void OnSourceChanged(object sender, SourceChangedEventArgs e)
    {
        if (e.NewSource is null) return;
        PresentationSource.RemoveSourceChangedHandler(this, OnSourceChanged);
        _awaitingSource = false;
        InvalidateVisual();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }

    private static Outline BuildOutline((double W, double H, double Cut, Thickness Edge, Vector Inset) key)
    {
        var outer = new Rect(key.Inset.X, key.Inset.Y, Math.Max(0, key.W - 2 * key.Inset.X), Math.Max(0, key.H - 2 * key.Inset.Y));
        var edge = key.Edge;
        bool hasEdge = edge.Left > 0 || edge.Top > 0 || edge.Right > 0 || edge.Bottom > 0;
        var fill = Build(ctx => AddOctagon(ctx, outer, edge, key.Cut));
        var ring = hasEdge
            ? Build(ctx =>
            {
                AddOctagon(ctx, outer, new Thickness(0), key.Cut);
                AddOctagon(ctx, outer, edge, key.Cut);
            })
            : null;
        return new Outline { Key = key, Ring = ring, Fill = fill };
    }

    private static StreamGeometry Build(Action<StreamGeometryContext> draw, FillRule rule = FillRule.EvenOdd)
    {
        var g = new StreamGeometry { FillRule = rule };
        using (var ctx = g.Open())
            draw(ctx);
        g.Freeze();
        return g;
    }

    private static void AddOctagon(StreamGeometryContext ctx, Rect b, Thickness inset, double cut, bool stroked = false)
    {
        double l = b.Left + inset.Left, t = b.Top + inset.Top, r = b.Right - inset.Right, bt = b.Bottom - inset.Bottom;
        if (r - l <= 0 || bt - t <= 0) return;
        double max = Math.Min(r - l, bt - t) / 2;
        double Inner(double a, double c) => Math.Clamp(cut + Math.Max(a, c) * Sqrt2 - a - c, 0, max);
        double tl = Inner(inset.Left, inset.Top), tr = Inner(inset.Right, inset.Top), br = Inner(inset.Right, inset.Bottom), bl = Inner(inset.Left, inset.Bottom);
        ctx.BeginFigure(new Point(l + tl, t), true, true);
        ctx.LineTo(new Point(r - tr, t), stroked, false);
        ctx.LineTo(new Point(r, t + tr), stroked, false);
        ctx.LineTo(new Point(r, bt - br), stroked, false);
        ctx.LineTo(new Point(r - br, bt), stroked, false);
        ctx.LineTo(new Point(l + bl, bt), stroked, false);
        ctx.LineTo(new Point(l, bt - bl), stroked, false);
        ctx.LineTo(new Point(l, t + tl), stroked, false);
    }
}

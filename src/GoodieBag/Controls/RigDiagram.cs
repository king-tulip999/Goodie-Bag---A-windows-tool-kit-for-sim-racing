using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GoodieBag.SimRacing;
using GoodieBag.Theme;

namespace GoodieBag.Controls;

internal sealed class RigDiagram : FrameworkElement
{
    private FovResult? _result;
    private bool _imperial;
    private IReadOnlyList<(FovResult Result, Brush Color)> _compare = Array.Empty<(FovResult, Brush)>();
    private string _colorKey = "Accent";

    public void Show(FovResult result, bool imperial, IReadOnlyList<(FovResult Result, Brush Color)>? compare = null, string colorKey = "Accent")
    {
        _result = result;
        _imperial = imperial;
        _compare = compare ?? Array.Empty<(FovResult, Brush)>();
        _colorKey = colorKey;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (_result is not { } r || w < 60 || h < 60)
            return;

        var accent = ThemeManager.Brush(_colorKey, Brushes.SlateBlue);
        var accentColor = ThemeManager.ColorOf(_colorKey, Colors.SlateBlue);
        var text = ThemeManager.Brush("Text", Brushes.Black);
        var muted = ThemeManager.Brush("TextMuted", Brushes.Gray);
        var faint = ThemeManager.Brush("TextFaint", Brushes.Gray);
        var line = ThemeManager.Brush("LineStrong", Brushes.LightGray);
        var peach = ThemeManager.Brush("ApricotDeep", Brushes.DarkOrange);

        var points = r.Screens.Concat(_compare.SelectMany(c => c.Result.Screens)).SelectMany(s => s).Append(new V2(0, 0)).ToList();
        double halfWidth = Math.Max(r.FootprintWidthCm / 2, points.Max(p => Math.Abs(p.X)));
        double back = r.Screens.SelectMany(x => x).Max(p => p.Y) + (r.FootprintDepthCm - r.RigDepthCm);
        double far = Math.Max(back, points.Max(p => p.Y)) + 3;
        double near = Math.Min(-12, points.Min(p => p.Y));
        double worldW = halfWidth * 2 + 30, worldH = far - near + 28;
        double scale = Math.Min(w / worldW, h / worldH);
        double cx = w / 2, top = (h - (far - near) * scale) / 2 + 6;
        Point P(V2 v) => new(cx + v.X * scale, top + (far - v.Y) * scale);

        var eye = P(new V2(0, 0));

        var left = new V2(-r.RightEdge.X, r.RightEdge.Y);
        var wedge = new StreamGeometry();
        using (var g = wedge.Open())
        {
            g.BeginFigure(eye, true, true);
            g.LineTo(P(left), true, false);
            foreach (var screen in r.Screens.Count == 3 ? new[] { r.Screens[2], r.Screens[0], r.Screens[1] } : new[] { r.Screens[0] })
                foreach (var p in screen)
                    g.LineTo(P(p), true, false);
            g.LineTo(P(r.RightEdge), true, false);
        }
        wedge.Freeze();
        var wedgeFill = new SolidColorBrush(Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B));
        dc.DrawGeometry(wedgeFill, null, wedge);
        var rayPen = new Pen(accent, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
        dc.DrawLine(rayPen, eye, P(r.RightEdge));
        dc.DrawLine(rayPen, eye, P(left));

        foreach (var (other, color) in _compare)
        {
            var dash = new Pen(color, 1.2) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            dc.DrawLine(dash, eye, P(other.RightEdge));
            dc.DrawLine(dash, eye, P(new V2(-other.RightEdge.X, other.RightEdge.Y)));
            var pen = new Pen(color, Math.Max(2.5, 4 * scale / 4)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            foreach (var screen in other.Screens)
                DrawPolyline(dc, pen, screen.Select(P).ToList());
        }

        double fw = r.FootprintWidthCm / 2;
        var footprint = new Rect(P(new V2(-fw, back)), P(new V2(fw, back - r.FootprintDepthCm)));
        dc.DrawGeometry(null, new Pen(line, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) }, Tile.Shape(footprint, ThemeManager.Round(4)));

        var screenPen = new Pen(text, Math.Max(3, 5 * scale / 4)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        foreach (var screen in r.Screens)
            DrawPolyline(dc, screenPen, screen.Select(P).ToList());

        if (r.Triple && r.SideAngle > 0.5 && r.Screens[1].Count > 1)
        {
            var hinge = P(r.Screens[1][0]);
            double radius = 22;
            double a = r.SideAngle * Math.PI / 180;
            var start = new Point(hinge.X + radius, hinge.Y);
            var end = new Point(hinge.X + radius * Math.Cos(a), hinge.Y + radius * Math.Sin(a));
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(start, false, false);
                g.ArcTo(end, new Size(radius, radius), 0, r.SideAngle > 180, SweepDirection.Clockwise, true, false);
            }
            arc.Freeze();
            dc.DrawLine(new Pen(faint, 1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) }, hinge, new Point(hinge.X + radius + 10, hinge.Y));
            dc.DrawGeometry(null, new Pen(peach, 1.6), arc);
            Label(dc, $"{r.SideAngle:0.0}°", new Point(hinge.X + radius + 6, hinge.Y + 6), peach, 11.5, bold: true);
        }

        dc.DrawEllipse(ThemeManager.Brush("Surface", Brushes.White), new Pen(text, 1.6), eye, 9, 9);
        dc.DrawEllipse(text, null, new Point(eye.X - 3.2, eye.Y - 1.5), 1.6, 1.6);
        dc.DrawEllipse(text, null, new Point(eye.X + 3.2, eye.Y - 1.5), 1.6, 1.6);

        Label(dc, $"{r.HorizontalFovTotal:0.0}° horizontal", new Point(eye.X, eye.Y + 14), accent, 12, bold: true, centre: true);
        string unit = _imperial ? "in" : "cm";
        double k = _imperial ? 1 / 2.54 : 1;
        Label(dc, $"Dotted box: ≈ {r.FootprintWidthCm * k:0} × {r.FootprintDepthCm * k:0} {unit} footprint", new Point(w - 2, 0), muted, 11, alignRight: true);
    }

    private static void DrawPolyline(DrawingContext dc, Pen pen, IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], false, false);
            g.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void Label(DrawingContext dc, string text, Point at, Brush brush, double size, bool bold = false, bool centre = false, bool alignRight = false)
    {
        var ft = new FormattedText(ThemeManager.Case(text), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            ThemeManager.UiFace(bold),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double x = centre ? at.X - ft.Width / 2 : alignRight ? at.X - ft.Width : at.X;
        dc.DrawText(ft, new Point(x, at.Y));
    }
}

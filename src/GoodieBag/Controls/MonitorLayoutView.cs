using System.Globalization;
using System.Windows;
using System.Windows.Media;

using GoodieBag.Theme;

namespace GoodieBag.Controls;

public sealed record LayoutTile(double X, double Y, double Width, double Height, string Label, string Detail, bool Primary);

public sealed class MonitorLayoutView : FrameworkElement
{
    private IReadOnlyList<LayoutTile> _tiles = Array.Empty<LayoutTile>();
    private static Typeface LabelFace => ThemeManager.DisplayFace;
    private static Typeface DetailFace => ThemeManager.UiFace();

    private static Brush TileFill => ThemeManager.Brush("TileRaised", Brushes.WhiteSmoke);
    private static Brush TileStroke => ThemeManager.Brush("TileEdge", Brushes.LightGray);
    private static Brush PrimaryFill => ThemeManager.Brush("AccentSoft", Brushes.Plum);
    private static Brush PrimaryStroke => ThemeManager.Brush("Accent", Brushes.SlateBlue);
    private static Brush TextBrush => ThemeManager.Brush("Text", Brushes.Black);
    private static Brush DetailBrush => ThemeManager.Brush("TextMuted", Brushes.Gray);
    private Pen? _tilePen, _primaryPen;

    public void SetTiles(IReadOnlyList<LayoutTile> tiles)
    {
        _tiles = tiles;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (_tiles.Count == 0 || w <= 0 || h <= 0)
            return;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var t in _tiles)
        {
            minX = Math.Min(minX, t.X);
            minY = Math.Min(minY, t.Y);
            maxX = Math.Max(maxX, t.X + t.Width);
            maxY = Math.Max(maxY, t.Y + t.Height);
        }
        if (maxX - minX <= 0 || maxY - minY <= 0 || w <= 8 || h <= 8)
            return;
        double scale = Math.Min((w - 8) / (maxX - minX), (h - 8) / (maxY - minY));
        double ox = (w - (maxX - minX) * scale) / 2, oy = (h - (maxY - minY) * scale) / 2;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _tilePen = Frozen(_tilePen, TileStroke, 1);
        _primaryPen = Frozen(_primaryPen, PrimaryStroke, 1.6);

        foreach (var t in _tiles)
        {
            var r = new Rect(ox + (t.X - minX) * scale + 2, oy + (t.Y - minY) * scale + 2, Math.Max(0, t.Width * scale - 4), Math.Max(0, t.Height * scale - 4));
            if (r.Width <= 2 || r.Height <= 2)
                continue;
            dc.DrawGeometry(t.Primary ? PrimaryFill : TileFill, t.Primary ? _primaryPen : _tilePen, Tile.Shape(r, ThemeManager.Round(6)));

            double size = Math.Clamp(r.Height * 0.28, 9, 22);
            var label = new FormattedText(ThemeManager.Case(t.Label), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, size, TextBrush, dpi);
            double total = label.Height;
            FormattedText? detail = null;
            if (!string.IsNullOrEmpty(t.Detail) && r.Height > 44)
            {
                detail = new FormattedText(ThemeManager.Case(t.Detail), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, DetailFace, Math.Clamp(size * 0.55, 9, 12), DetailBrush, dpi);
                if (detail.Width > r.Width - 6)
                    detail = null;
                else
                    total += detail.Height + 1;
            }
            double ty = r.Y + (r.Height - total) / 2;
            dc.DrawText(label, new Point(r.X + (r.Width - label.Width) / 2, ty));
            if (detail is not null)
                dc.DrawText(detail, new Point(r.X + (r.Width - detail.Width) / 2, ty + label.Height + 1));
        }
    }

    private static Pen Frozen(Pen? cached, Brush brush, double thickness)
    {
        if (cached is not null && cached.Brush == brush)
            return cached;
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}

using System.Windows;
using System.Windows.Controls;
using GoodieBag.Controls;

namespace GoodieBag.Views;

internal sealed class CardFace : Panel
{
    private const double IconGap = 14, BarGap = 10, BarHeight = 2;
    private readonly StackPanel _titles;
    private readonly TextBlock _title;
    private readonly NavIcon _icon;
    private readonly Border _bar;
    private double _iconSize = -1, _fontSize = -1, _barWidth;

    public CardFace(StackPanel titles, TextBlock title, NavIcon icon, Border bar)
    {
        (_titles, _title, _icon, _bar) = (titles, title, icon, bar);
        Children.Add(icon);
        Children.Add(titles);
        Children.Add(bar);
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 300 : Math.Max(1, available.Width);
        double h = double.IsInfinity(available.Height) ? w * 0.55 : Math.Max(1, available.Height);
        double font = Math.Round(Math.Clamp(Math.Min(w * 0.047, h * 0.09), 12, 24) * 2) / 2;
        if (font != _fontSize) { _fontSize = font; _title.FontSize = font; }
        _titles.Measure(new Size(w, double.PositiveInfinity));
        double rest = h - _titles.DesiredSize.Height - IconGap - BarGap - BarHeight;
        double icon = Math.Round(Math.Clamp(Math.Min(rest * 0.82, w * 0.34), 24, 220));
        if (icon != _iconSize) { _iconSize = icon; _icon.Width = _icon.Height = icon; }
        _icon.Measure(new Size(icon, icon));
        _barWidth = Math.Round(Math.Clamp(w * 0.1, 20, 64));
        _bar.Measure(new Size(_barWidth, BarHeight));
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double w = final.Width, titleH = _titles.DesiredSize.Height, icon = _iconSize;
        double block = icon + IconGap + titleH + BarGap + BarHeight;
        double top = Math.Max(0, (final.Height - block) / 2);
        _icon.Arrange(new Rect((w - icon) / 2, top, icon, icon));
        top += icon + IconGap;
        _titles.Arrange(new Rect(0, top, w, titleH));
        top += titleH + BarGap;
        _bar.Arrange(new Rect((w - _barWidth) / 2, top, _barWidth, BarHeight));
        return final;
    }
}

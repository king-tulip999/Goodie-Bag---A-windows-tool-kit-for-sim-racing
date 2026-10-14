using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using GoodieBag.Theme;

namespace GoodieBag.Controls;

public sealed class ArrangeTile
{
    public required string Key { get; init; }
    public required string Number { get; init; }
    public required string Name { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Primary { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class ArrangementCanvas : Canvas
{
    private readonly Dictionary<string, Border> _visuals = new();
    private List<ArrangeTile> _tiles = new();
    private string? _selected;
    private Border? _dragging;
    private Point _dragOffset;
    private double _scale = 0.05, _ox, _oy, _minX, _minY;

    public event Action<string>? Selected;

    public event Action<string>? Moved;

    public bool Animate { get; set; } = true;

    public ArrangementCanvas()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        SizeChanged += (_, _) => Layout(animate: false);
    }

    public void SetTiles(List<ArrangeTile> tiles, string? selected)
    {
        _tiles = tiles;
        _selected = selected ?? tiles.FirstOrDefault(t => t.Primary)?.Key ?? tiles.FirstOrDefault()?.Key;
        _dragging = null;
        Children.Clear();
        _visuals.Clear();
        foreach (var tile in tiles)
        {
            var border = BuildVisual(tile);
            _visuals[tile.Key] = border;
            Children.Add(border);
        }
        Layout(animate: false);
    }

    public void Select(string key)
    {
        _selected = key;
        foreach (var t in _tiles)
            Paint(_visuals[t.Key], t);
    }

    public void Refresh(bool animate = true)
    {
        foreach (var t in _tiles)
        {
            if (_visuals.TryGetValue(t.Key, out var v))
                Fill(v, t);
        }
        Layout(animate);
    }

    private void Layout(bool animate)
    {
        if (_tiles.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        _minX = _tiles.Min(t => t.X);
        _minY = _tiles.Min(t => t.Y);
        double maxX = _tiles.Max(t => t.X + t.Width), maxY = _tiles.Max(t => t.Y + t.Height);
        double pad = 40;
        _scale = Math.Min((ActualWidth - pad * 2) / Math.Max(1, maxX - _minX), (ActualHeight - pad * 2) / Math.Max(1, maxY - _minY));
        _scale = Math.Min(_scale, 0.12);
        _ox = (ActualWidth - (maxX - _minX) * _scale) / 2;
        _oy = (ActualHeight - (maxY - _minY) * _scale) / 2;

        foreach (var t in _tiles)
        {
            var v = _visuals[t.Key];
            v.Width = Math.Max(20, t.Width * _scale - 4);
            v.Height = Math.Max(16, t.Height * _scale - 4);
            double left = _ox + (t.X - _minX) * _scale + 2, top = _oy + (t.Y - _minY) * _scale + 2;
            if (animate && Animate)
            {
                double fromLeft = GetLeft(v), fromTop = GetTop(v);
                SetLeft(v, left);
                SetTop(v, top);
                v.BeginAnimation(LeftProperty, new DoubleAnimation(double.IsNaN(fromLeft) ? left : fromLeft, left, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ease });
                v.BeginAnimation(TopProperty, new DoubleAnimation(double.IsNaN(fromTop) ? top : fromTop, top, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ease });
            }
            else
            {
                v.BeginAnimation(LeftProperty, null);
                v.BeginAnimation(TopProperty, null);
                SetLeft(v, left);
                SetTop(v, top);
            }
            Paint(v, t);
        }
    }

    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    private Border BuildVisual(ArrangeTile tile)
    {
        var border = new Tile
        {
            CornerRadius = ThemeManager.Round(10),
            BorderThickness = new Thickness(1.5),
            Tag = tile.Key,
            Child = new Grid(),
        };
        Fill(border, tile);
        border.MouseLeftButtonDown += OnDown;
        border.MouseMove += OnMove;
        border.MouseLeftButtonUp += OnUp;
        border.LostMouseCapture += OnLostCapture;
        border.MouseEnter += (_, _) => { if (_dragging is null) border.Opacity = 0.92; };
        border.MouseLeave += (_, _) => border.Opacity = 1;
        return border;
    }

    private static void Fill(Border border, ArrangeTile tile)
    {
        var grid = (Grid)border.Child;
        grid.Children.Clear();
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = tile.Number,
            FontSize = 30,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = ThemeManager.DisplayFace.FontFamily,
        });
        stack.Children.Add(new TextBlock { Text = tile.Name, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 6, 0) });
        stack.Children.Add(new TextBlock { Text = tile.Detail, FontSize = 11, Opacity = 0.65, HorizontalAlignment = HorizontalAlignment.Center });
        grid.Children.Add(stack);
        if (tile.Primary)
        {
            grid.Children.Add(new Tile
            {
                CornerRadius = ThemeManager.Round(6),
                Background = ThemeManager.Brush("Accent", Brushes.SlateBlue),
                Padding = new Thickness(6, 1, 6, 2),
                Margin = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = "Main", FontSize = 10, Foreground = ThemeManager.Brush("OnAccent", Brushes.Black), FontWeight = FontWeights.SemiBold },
            });
        }
    }

    private void Paint(Border border, ArrangeTile tile)
    {
        bool selected = tile.Key == _selected;
        border.Background = ThemeManager.Brush(selected ? "AccentSoft" : "TileRaised", Brushes.White);
        border.BorderBrush = ThemeManager.Brush(selected ? "Accent" : "LineStrong", Brushes.Gray);
        border.BorderThickness = new Thickness(selected ? 2 : 1.5);
        TextElement.SetForeground(border, ThemeManager.Brush(selected ? "AccentText" : "Text", Brushes.Black));
        Panel.SetZIndex(border, selected ? 2 : 1);
    }

    private Point _downAt;

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var border = (Border)sender;
        string key = (string)border.Tag;
        if (_selected != key)
        {
            Select(key);
            Selected?.Invoke(key);
        }
        if (_tiles.Count < 2)
            return;

        _dragging = border;
        _dragOffset = e.GetPosition(border);
        _downAt = e.GetPosition(this);
        double left = GetLeft(border), top = GetTop(border);
        border.BeginAnimation(LeftProperty, null);
        border.BeginAnimation(TopProperty, null);
        SetLeft(border, left);
        SetTop(border, top);
        border.CaptureMouse();
        Panel.SetZIndex(border, 10);
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragging != sender || e.LeftButton != MouseButtonState.Pressed)
            return;
        var p = e.GetPosition(this);
        SetLeft(_dragging, p.X - _dragOffset.X);
        SetTop(_dragging, p.Y - _dragOffset.Y);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging != sender)
            return;
        var border = _dragging;
        _dragging = null;
        border.ReleaseMouseCapture();

        if ((e.GetPosition(this) - _downAt).Length < 4) { Layout(animate: false); return; }
        var tile = _tiles.First(t => t.Key == (string)border.Tag);
        double x = (GetLeft(border) - 2 - _ox) / _scale + _minX;
        double y = (GetTop(border) - 2 - _oy) / _scale + _minY;
        var (sx, sy) = Snap(tile, x, y);
        bool changed = Math.Abs(sx - tile.X) > 0.5 || Math.Abs(sy - tile.Y) > 0.5;
        tile.X = sx;
        tile.Y = sy;
        Layout(animate: true);
        if (changed)
            Moved?.Invoke(tile.Key);
    }

    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_dragging != sender)
            return;
        _dragging = null;
        Layout(animate: true);
    }

    private (double X, double Y) Snap(ArrangeTile moving, double x, double y)
    {
        var others = _tiles.Where(t => t != moving).ToList();
        if (others.Count == 0)
            return (x, y);

        double w = moving.Width, h = moving.Height;
        double alignThreshold = Math.Max(w, h) * 0.08;
        var candidates = new List<(double X, double Y)>();

        foreach (var o in others)
        {
            double cy = Math.Clamp(y, o.Y - h + 1, o.Y + o.Height - 1);
            double cx = Math.Clamp(x, o.X - w + 1, o.X + o.Width - 1);
            cy = AlignTo(cy, new[] { o.Y, o.Y + o.Height - h, o.Y + (o.Height - h) / 2 }, alignThreshold);
            cx = AlignTo(cx, new[] { o.X, o.X + o.Width - w, o.X + (o.Width - w) / 2 }, alignThreshold);

            candidates.Add((o.X + o.Width, cy));
            candidates.Add((o.X - w, cy));
            candidates.Add((cx, o.Y + o.Height));
            candidates.Add((cx, o.Y - h));
        }

        var best = candidates
            .Where(c => others.All(o => !Overlaps(c.X, c.Y, w, h, o)))
            .OrderBy(c => (c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y))
            .Select(c => ((double, double)?)c)
            .FirstOrDefault();
        return best ?? (moving.X, moving.Y);
    }

    private static double AlignTo(double value, double[] targets, double threshold)
    {
        foreach (var t in targets)
            if (Math.Abs(value - t) <= threshold)
                return t;
        return value;
    }

    private static bool Overlaps(double x, double y, double w, double h, ArrangeTile o) =>
        x < o.X + o.Width - 0.5 && x + w > o.X + 0.5 && y < o.Y + o.Height - 0.5 && y + h > o.Y + 0.5;
}

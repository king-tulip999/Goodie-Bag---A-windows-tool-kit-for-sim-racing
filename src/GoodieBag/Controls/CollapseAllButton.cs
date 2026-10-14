using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GoodieBag.Theme;
using GoodieBag.Views;

namespace GoodieBag.Controls;

internal sealed class CollapseAllButton : Button
{
    private readonly Icon _chevron = new() { Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center };
    private readonly RotateTransform _turn = new(180);
    private readonly ScaleTransform _squash = new(1, 1);
    private readonly TextBlock _label = new() { Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _countText = new() { FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _count;
    private bool? _anyOpen;

    public event Action<bool>? Requested;

    public CollapseAllButton()
    {
        Style = Ui.Res<Style>("GhostButton");
        ClickGuard.SetAllowRepeat(this, true);
        _chevron.Data = Ui.Res<Geometry>("I.ChevronDown");
        _chevron.RenderTransformOrigin = new Point(0.5, 0.5);
        _chevron.RenderTransform = new TransformGroup { Children = { _squash, _turn } };
        _countText.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        _count = new Border
        {
            CornerRadius = ThemeManager.Round(8), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Child = _countText, RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        _count.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(_chevron);
        content.Children.Add(_label);
        content.Children.Add(_count);
        Content = content;
        Click += (_, _) => Requested?.Invoke(_anyOpen != false);
        SetOpen(1, 1, animate: false);
    }

    public void SetOpen(int open, int total, bool animate = true)
    {
        bool anyOpen = open > 0;
        _countText.Text = $"{open} open";
        _count.Visibility = anyOpen && total > 1 ? Visibility.Visible : Visibility.Collapsed;
        ToolTip = anyOpen ? $"Fold every panel ({open} of {total} open)" : "Open every panel";
        if (_anyOpen == anyOpen)
        {
            if (animate && anyOpen) Pop(_count);
            return;
        }
        _anyOpen = anyOpen;
        _label.Text = ThemeManager.Case(anyOpen ? "Collapse all" : "Expand all");
        double angle = anyOpen ? 180 : 0;
        if (!animate || !Motion.Enabled)
        {
            _turn.BeginAnimation(RotateTransform.AngleProperty, null);
            _turn.Angle = angle;
            return;
        }
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
        _turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        var squash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(380) };
        squash.KeyFrames.Add(new EasingDoubleKeyFrame(anyOpen ? 1.25 : 0.45, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), Motion.Ease));
        squash.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)), new ElasticEase { Oscillations = 1, Springiness = 4 }));
        _squash.BeginAnimation(ScaleTransform.ScaleYProperty, squash);
        if (anyOpen) Pop(_count);
    }

    private static void Pop(FrameworkElement element)
    {
        if (!Motion.Enabled || element.RenderTransform is not ScaleTransform scale) return;
        var pop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(260) };
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)), Motion.Ease));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), Motion.Ease));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }
}

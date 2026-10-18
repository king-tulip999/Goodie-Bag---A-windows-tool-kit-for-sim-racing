using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GoodieBag.Controls;

internal sealed class NumberBox : Grid
{
    private readonly TextBox _box = new() { TextAlignment = TextAlignment.Right, MinWidth = 64, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _unit = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private double _value;
    private bool _updating;
    private double? _typed;
    private readonly DispatcherTimer _pause = new() { Interval = TimeSpan.FromMilliseconds(TypingPauseMs) };

    public const int TypingPauseMs = 800;

    public double Minimum { get; init; }
    public double Maximum { get; init; } = 1000;
    public double Step { get; init; } = 1;
    public int Decimals { get; init; }

    public event Action<double>? ValueChanged;

    public NumberBox(double value, string unit, double width = 92)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _box.MinWidth = width;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var minus = StepButton("I.Minus", "Smaller", -1);
        var plus = StepButton("I.Plus", "Larger", +1);
        _box.Margin = new Thickness(6, 0, 0, 0);
        _unit.Text = unit;
        if (unit.Length == 0) _unit.Visibility = Visibility.Collapsed;
        plus.Margin = new Thickness(6, 0, 0, 0);
        _unit.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        Grid.SetColumn(_box, 1);
        Grid.SetColumn(_unit, 2);
        Grid.SetColumn(plus, 3);
        Children.Add(minus);
        Children.Add(_box);
        Children.Add(_unit);
        Children.Add(plus);

        _value = value;
        Loaded += (_, _) =>
        {
            FitToRange();
            ShowValue();
        };
        _pause.Tick += (_, _) => CommitTyped();
        Unloaded += (_, _) => CommitTyped();
        _box.TextChanged += (_, _) => OnTyped();
        _box.LostKeyboardFocus += (_, _) =>
        {
            CommitTyped();
            ShowValue();
        };
        _box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down)
            {
                Nudge(e.Key == Key.Up ? 1 : -1, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                CommitTyped();
                ShowValue();
                _box.SelectAll();
            }
        };
        _box.PreviewMouseWheel += (_, e) =>
        {
            if (!_box.IsKeyboardFocusWithin)
                return;
            Nudge(Math.Sign(e.Delta), 1);
            e.Handled = true;
        };
        _box.GotKeyboardFocus += (_, _) => _box.SelectAll();
    }

    public double Value
    {
        get => _typed is { } t ? Math.Clamp(Precise(t, Decimals, Step), Minimum, Maximum) : _value;
        set
        {
            _typed = null;
            _pause.Stop();
            _value = value;
            ShowValue();
        }
    }

    private Button StepButton(string icon, string tooltip, int direction)
    {
        var button = new Button
        {
            Width = 34, Height = 34, Padding = new Thickness(0), ToolTip = tooltip + " (or scroll / arrow keys)",
            Content = new Icon { Data = (Geometry)Application.Current.FindResource(icon), Width = 14, Height = 14 },
        };
        button.SetResourceReference(StyleProperty, "SoftButton");
        ClickGuard.SetAllowRepeat(button, true);
        button.Click += (_, _) => Nudge(direction, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1);
        return button;
    }

    private void Nudge(int direction, int multiplier)
    {
        CommitTyped();
        double next = Math.Round((_value + direction * Step * multiplier) / Step) * Step;
        Set(next, show: true);
    }

    private void OnTyped()
    {
        if (_updating)
            return;
        string text = _box.Text.Trim().Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= Minimum && v <= Maximum)
        {
            _box.ClearValue(Control.BorderBrushProperty);
            _typed = v;
        }
        else
        {
            _box.SetResourceReference(Control.BorderBrushProperty, "Danger");
            _typed = null;
        }
        _pause.Stop();
        if (_typed is not null) _pause.Start();
    }

    private void CommitTyped()
    {
        _pause.Stop();
        if (_typed is not { } v) return;
        _typed = null;
        Set(v, show: false);
    }

    private void Set(double value, bool show)
    {
        value = Math.Clamp(Precise(value, Decimals, Step), Minimum, Maximum);
        bool changed = Math.Abs(value - _value) > 1e-9;
        _value = value;
        if (show)
            ShowValue();
        if (changed)
            ValueChanged?.Invoke(value);
    }

    internal static double Precise(double value, int decimals, double step)
    {
        int stepDecimals = step > 0 && step < 1e15 ? (decimal.GetBits((decimal)step)[3] >> 16) & 0xFF : 0;
        return Math.Round(value, Math.Clamp(Math.Max(decimals, stepDecimals), 0, 15), MidpointRounding.AwayFromZero);
    }

    private void FitToRange()
    {
        double widest = new[] { Minimum, Maximum }.Max(v =>
            new FormattedText(v.ToString("F" + Decimals, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(_box.FontFamily, _box.FontStyle, _box.FontWeight, _box.FontStretch), _box.FontSize, Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace);
        _box.MinWidth = Math.Max(_box.MinWidth, Math.Ceiling(widest + _box.Padding.Left + _box.Padding.Right + _box.BorderThickness.Left + _box.BorderThickness.Right + 8));
    }

    private void ShowValue()
    {
        _updating = true;
        _box.Text = _value.ToString("F" + Decimals, CultureInfo.InvariantCulture);
        _box.ClearValue(Control.BorderBrushProperty);
        _updating = false;
    }
}

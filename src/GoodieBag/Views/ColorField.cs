using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GoodieBag.Theme;
using WinForms = System.Windows.Forms;

namespace GoodieBag.Views;

internal static class ColorField
{
    public static FrameworkElement Build(Func<string> get, Func<string, bool> set, string? help = null, double boxWidth = 96)
    {
        var chip = new Border { Width = 22, Height = 18, CornerRadius = ThemeManager.Round(3), BorderThickness = new Thickness(1), BorderBrush = Ui.Res<Brush>("LineStrong") };
        var swatch = new Button
        {
            Width = 34, Height = 30, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Pick a color", Style = Ui.Res<Style>("Btn"),
            Content = chip,
        };
        var box = new TextBox { Text = get(), Width = boxWidth, MaxLength = 7, FontFamily = Ui.Res<FontFamily>("MonoFont"), VerticalAlignment = VerticalAlignment.Center, ToolTip = help };
        void Show() => chip.Background = CustomPalette.Parse(get()) is { } c ? new SolidColorBrush(c) : Brushes.Transparent;
        void Commit(string text)
        {
            if (CustomPalette.Parse(text.Trim()) is { } v)
            {
                string hex = $"#{v.R:X2}{v.G:X2}{v.B:X2}";
                if (!string.Equals(hex, get(), StringComparison.OrdinalIgnoreCase)) set(hex);
            }
            box.Text = get();
            Show();
        }
        box.LostKeyboardFocus += (_, _) => Commit(box.Text);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(box.Text); };
        swatch.Click += (_, _) =>
        {
            var now = CustomPalette.Parse(get()) ?? Colors.Black;
            using var dialog = new WinForms.ColorDialog { Color = System.Drawing.Color.FromArgb(now.R, now.G, now.B), FullOpen = true, AnyColor = true };
            var owner = Window.GetWindow(swatch) is { } w ? new Owner(new WindowInteropHelper(w).Handle) : null;
            if (dialog.ShowDialog(owner) == WinForms.DialogResult.OK)
                Commit($"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
        };
        Show();
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(swatch);
        line.Children.Add(box);
        return line;
    }

    private sealed class Owner(IntPtr handle) : WinForms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}

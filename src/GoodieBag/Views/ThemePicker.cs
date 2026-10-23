using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal sealed class ThemePicker : UniformGrid
{
    public ThemePicker(AppTheme selected, Action<AppTheme> pick, int columns = 3, double height = 64, Action<string?>? oled = null)
    {
        Columns = columns;
        foreach (var theme in Themes.All)
        {
            Action<FrameworkElement>? menu = theme.Id == AppTheme.Custom && oled is not null ? anchor => ThemeMenu.OpenOledMenu(anchor, oled) : null;
            Children.Add(Swatch(theme, theme.Id == selected, pick, height, menu));
        }
    }

    private static Button Swatch(ThemeSpec theme, bool selected, Action<AppTheme> pick, double height, Action<FrameworkElement>? menu)
    {
        var c = ThemeManager.Resolve(theme);
        Brush Of(string key) => new SolidColorBrush(c[key]);

        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var key in new[] { "Accent", "ActiveModeBg", "Sage" })
            dots.Children.Add(new Rectangle { Width = 10, Height = 10, RadiusX = theme.ChipRadius > 6 ? 5 : 2, RadiusY = theme.ChipRadius > 6 ? 5 : 2, Fill = Of(key), Margin = new Thickness(4, 0, 0, 0) });

        var name = TextFit.TipWhenTrimmed(new TextBlock
        {
            Text = theme.Caps ? theme.Name.ToUpperInvariant() : theme.Name,
            FontFamily = ThemeManager.Family(theme.DisplayFont), FontWeight = theme.HeadingWeight, FontStyle = theme.HeadingStyle, FontSize = 14,
            Foreground = Of("Heading"), VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        FrameworkElement label = theme.Link is { } link && Logo(link.Logo) is { } logo
            ? new Image
            {
                Source = logo,
                Stretch = Stretch.Uniform, Height = Math.Clamp(height * 0.34, 16, 26), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                SnapsToDevicePixels = true,
            }
            : name;
        if (label is Image) RenderOptions.SetBitmapScalingMode(label, BitmapScalingMode.HighQuality);
        var card = new Border
        {
            Background = Of("Surface"), BorderBrush = Of(theme.TileEdge ? "TileEdge" : "Line"), BorderThickness = theme.TileEdge ? new Thickness(0, 0, 0, 2) : new Thickness(1),
            CornerRadius = new CornerRadius(8 * theme.Radius),
            Child = new Grid { Children = { label, dots } },
        };
        var face = new Grid();
        face.Children.Add(card);
        if (menu is not null)
            face.Children.Add(new Icon
            {
                Data = Ui.Res<Geometry>("I.ChevronDown"), Width = 11, Height = 11, Foreground = Of("Heading"), StrokeThickness = 2.2,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, selected ? 18 : 8, 8),
            });
        if (selected)
            face.Children.Add(new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Ui.Res<Brush>("Accent"),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -6, -6),
                Child = new Icon { Data = Ui.Res<Geometry>("I.Check"), Width = 11, Height = 11, StrokeThickness = 3, Foreground = Ui.Res<Brush>("OnAccent") },
            });

        var button = new Button
        {
            Style = Ui.Res<Style>("Btn"), Height = height, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(5),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = Of("Bg"), BorderBrush = selected ? Ui.Res<Brush>("Accent") : Ui.Res<Brush>("LineStrong"),
            BorderThickness = new Thickness(selected ? 2 : 1), Content = face, ToolTip = theme.Name,
        };
        button.MouseEnter += (_, _) => button.BorderBrush = Ui.Res<Brush>("Accent");
        button.MouseLeave += (_, _) => button.BorderBrush = selected ? Ui.Res<Brush>("Accent") : Ui.Res<Brush>("LineStrong");
        AutomationProperties(button, theme.Link?.Name ?? theme.Name, selected);
        if (menu is not null) button.ToolTip = theme.Name + ": pick its colors";
        button.Click += (_, _) =>
        {
            if (menu is not null) menu(button);
            else if (!selected) pick(theme.Id);
        };
        return button;
    }

    internal static ImageSource? Logo(Uri uri)
    {
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            App.LogError(ex);
            return null;
        }
    }

    private static void AutomationProperties(Button b, string name, bool selected) =>
        System.Windows.Automation.AutomationProperties.SetName(b, name + (selected ? " (selected)" : ""));
}

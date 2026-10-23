using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GoodieBag.SimRacing;

namespace GoodieBag.Views;

internal static class CarPictures
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Of(string key)
    {
        if (Cache.TryGetValue(key, out var known)) return known;
        ImageSource? source = null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/GoodieBag;component/Assets/Cars/{key}.png");
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            source = image;
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException or FileFormatException)
        {
        }
        Cache[key] = source;
        return source;
    }

    public static ImageSource? For(string? car) => CarArt.KeyOf(car) is { } key ? Of(key) : null;

    public static Image? Picture(string? car, double height, double maxWidth = double.PositiveInfinity)
    {
        if (For(car) is not { } source) return null;
        var image = new Image
        {
            Source = source, Height = height, MaxWidth = maxWidth, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    public const double SlotAspect = 3.0;

    public static Image? Slotted(string? car, double height)
    {
        if (Picture(car, height) is not { } picture) return null;
        picture.Width = height * SlotAspect;
        return picture;
    }

    public static FrameworkElement Beside(FrameworkElement content, string? car, double height, double gap = 10, bool slot = true)
    {
        if ((slot ? Slotted(car, height) : Picture(car, height, height * 4)) is not { } picture) return content;
        picture.Margin = new Thickness(0, 0, gap, 0);
        content.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(picture, Dock.Left);
        row.Children.Add(picture);
        row.Children.Add(content);
        return row;
    }
}

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GoodieBag.Services;

internal static class ProgramIcons
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[]? ids, uint count, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    public static ImageSource? Of(string? exe, int px = 64)
    {
        if (exe is null || !File.Exists(exe)) return null;
        var handles = new IntPtr[1];
        try
        {
            if (PrivateExtractIcons(exe, 0, px, px, handles, null, 1, 0) == 0 || handles[0] == IntPtr.Zero) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(px, px));
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handles[0] != IntPtr.Zero) DestroyIcon(handles[0]);
        }
    }

    public static async Task<ImageSource?> GetAsync(Func<string?> exe, string site)
    {
        var own = await Task.Run(() => { try { return Of(exe()); } catch { return null; } });
        return own ?? await SiteIcons.GetAsync(site);
    }
}

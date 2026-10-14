using System.IO;
using System.Runtime.InteropServices;

namespace GoodieBag.Display;

internal static class Wallpapers
{
    public static Dictionary<string, string> Current()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IDesktopWallpaper? wallpaper = null;
        try
        {
            wallpaper = Create();
            uint count = wallpaper.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    string id = wallpaper.GetMonitorDevicePathAt(i);
                    string path = wallpaper.GetWallpaper(id);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        result[id] = path;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        finally
        {
            Release(wallpaper);
        }
        return result;
    }

    public static List<string> Apply(IReadOnlyDictionary<string, string> wallpapers)
    {
        var problems = new List<string>();
        IDesktopWallpaper wallpaper;
        try { wallpaper = Create(); }
        catch (Exception ex) { problems.Add(ex.Message); return problems; }

        try
        {
            var monitors = new List<string>();
            try
            {
                uint count = wallpaper.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                    monitors.Add(wallpaper.GetMonitorDevicePathAt(i));
            }
            catch (Exception ex)
            {
                problems.Add(ex.Message);
                return problems;
            }

            foreach (var (monitor, file) in wallpapers)
            {
                if (!File.Exists(file))
                {
                    problems.Add($"{Path.GetFileName(file)} no longer exists");
                    continue;
                }
                var id = monitors.FirstOrDefault(m => string.Equals(m, monitor, StringComparison.OrdinalIgnoreCase));
                if (id is null)
                    continue;
                try { wallpaper.SetWallpaper(id, file); }
                catch (Exception ex) { problems.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
            }
            return problems;
        }
        finally
        {
            Release(wallpaper);
        }
    }

    private static IDesktopWallpaper Create() => (IDesktopWallpaper)new DesktopWallpaperClass();

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
            Marshal.ReleaseComObject(com);
    }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);

        uint GetMonitorDevicePathCount();
    }
}

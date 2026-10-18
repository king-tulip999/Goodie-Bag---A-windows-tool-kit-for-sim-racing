using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GoodieBag.Services;

internal static class ShotDiff
{
    private const int Tolerance = 24;
    private const double ChangedShare = 0.002;

    internal sealed record Result(string Name, string State, double Share, Int32Rect Box);

    public static int Run(string baseline, string current, string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<Result>();
        var currentFiles = Directory.Exists(current)
            ? Directory.EnumerateFiles(current, "*.png", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(current, f)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseFiles = Directory.Exists(baseline)
            ? Directory.EnumerateFiles(baseline, "*.png", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(baseline, f)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ignored = Ignored();
        foreach (var name in currentFiles.Union(baseFiles).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (ignored.Any(r => r.IsMatch(name.Replace(Path.DirectorySeparatorChar, '/')))) continue;
            if (!baseFiles.Contains(name)) { results.Add(new Result(name, "new", 1, default)); continue; }
            if (!currentFiles.Contains(name)) { results.Add(new Result(name, "gone", 1, default)); continue; }
            var a = Load(Path.Combine(baseline, name));
            var b = Load(Path.Combine(current, name));
            if (a.Width != b.Width || a.Height != b.Height)
            {
                results.Add(new Result(name, $"size {a.Width}x{a.Height} -> {b.Width}x{b.Height}", 1, default));
                continue;
            }
            var (share, box, diff) = Compare(a, b);
            if (share < ChangedShare) continue;
            results.Add(new Result(name, "changed", share, box));
            Save(diff, b.Width, b.Height, Path.Combine(output, name.Replace(Path.DirectorySeparatorChar, '_')));
        }
        File.WriteAllLines(Path.Combine(output, "changed.txt"),
            results.Count == 0 ? new[] { "no changes" } : results.Select(r => r.State == "changed"
                ? $"{r.Name}: {r.Share:P1} of pixels, around x {r.Box.X}–{r.Box.X + r.Box.Width}, y {r.Box.Y}–{r.Box.Y + r.Box.Height}"
                : $"{r.Name}: {r.State}"));
        return results.Count;
    }

    private static List<System.Text.RegularExpressions.Regex> Ignored()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string file = Path.Combine(dir.FullName, "devtools", "tools", "shot-diff.ignore.txt");
            if (!File.Exists(file)) continue;
            return File.ReadAllLines(file).Select(l => l.Split((char)9)[0].Trim()).Where(p => p.Length > 0 && !p.StartsWith('#'))
                .Select(p => new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(p).Replace(@"\*", ".*") + "$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                .ToList();
        }
        return new();
    }

    private sealed record Pixels(int Width, int Height, byte[] Data);

    private static Pixels Load(string file)
    {
        using var stream = File.OpenRead(file);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var data = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(data, bgra.PixelWidth * 4, 0);
        return new Pixels(bgra.PixelWidth, bgra.PixelHeight, data);
    }

    private static (double Share, Int32Rect Box, byte[] Diff) Compare(Pixels a, Pixels b) => Compare(a.Width, a.Height, a.Data, b.Data);

    internal static (double Share, Int32Rect Box, byte[] Diff) Compare(int width, int height, byte[] a, byte[] b)
    {
        var diff = new byte[a.Length];
        int changed = 0, minX = width, minY = height, maxX = -1, maxY = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                int d = Math.Max(Math.Abs(a[i] - b[i]), Math.Max(Math.Abs(a[i + 1] - b[i + 1]), Math.Abs(a[i + 2] - b[i + 2])));
                if (d > Tolerance)
                {
                    changed++;
                    diff[i] = 0; diff[i + 1] = 0; diff[i + 2] = 255; diff[i + 3] = 255;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
                else
                {
                    diff[i] = (byte)(b[i] / 3); diff[i + 1] = (byte)(b[i + 1] / 3); diff[i + 2] = (byte)(b[i + 2] / 3); diff[i + 3] = 255;
                }
            }
        var box = maxX < 0 ? default : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        return ((double)changed / (width * height), box, diff);
    }

    private static void Save(byte[] data, int width, int height, string file)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, data, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}

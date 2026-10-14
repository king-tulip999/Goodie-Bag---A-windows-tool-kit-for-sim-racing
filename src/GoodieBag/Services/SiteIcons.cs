using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GoodieBag.Config;

namespace GoodieBag.Services;

internal static class SiteIcons
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Dictionary<string, (Task<ImageSource?> Task, DateTime Started)> Loading = new();
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
    private static string Folder => Path.Combine(ConfigStore.Folder, "Icons");

    public static Task<ImageSource?> GetAsync(string site)
    {
        var uri = new Uri(site);
        lock (Loading)
        {
            if (Loading.TryGetValue(uri.Host, out var known)
                && !(known.Task.IsCompleted && (known.Task.IsFaulted || known.Task.Result is null) && DateTime.UtcNow - known.Started > RetryAfter))
                return known.Task;
            var task = LoadAsync(uri);
            Loading[uri.Host] = (task, DateTime.UtcNow);
            return task;
        }
    }

    private static async Task<ImageSource?> LoadAsync(Uri site)
    {
        var file = Path.Combine(Folder, site.Host + ".ico");
        try
        {
            byte[]? bytes = File.Exists(file) ? await File.ReadAllBytesAsync(file) : null;
            if (bytes is not null && Decode(bytes) is null) bytes = null;
            if (bytes is null)
            {
                foreach (var candidate in await CandidatesAsync(site))
                {
                    try
                    {
                        using var response = await Http.GetAsync(candidate);
                        if (!response.IsSuccessStatusCode) continue;
                        var data = await response.Content.ReadAsByteArrayAsync();
                        if (Decode(data) is null) continue;
                        bytes = data;
                        if (!ConfigStore.ReadOnly)
                        {
                            Directory.CreateDirectory(Folder);
                            var temp = file + ".tmp";
                            await File.WriteAllBytesAsync(temp, data);
                            File.Move(temp, file, overwrite: true);
                        }
                        break;
                    }
                    catch { }
                }
            }
            return bytes is null ? null : Decode(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<Uri>> CandidatesAsync(Uri site)
    {
        var list = new List<Uri>();
        try
        {
            var html = await Http.GetStringAsync(site);
            foreach (Match link in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var tag = link.Value;
                if (!Regex.IsMatch(tag, @"rel\s*=\s*[""'][^""']*icon", RegexOptions.IgnoreCase)) continue;
                var href = Regex.Match(tag, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (!href.Success || href.Groups[1].Value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
                var size = Regex.Match(tag, @"sizes\s*=\s*[""'](\d+)", RegexOptions.IgnoreCase);
                list.Add(new Uri(site, System.Net.WebUtility.HtmlDecode(href.Groups[1].Value)));
                if (size.Success && int.TryParse(size.Groups[1].Value, out int px) && px >= 64) list.Insert(0, list[^1]);
            }
        }
        catch { }
        foreach (var path in new[] { "/favicon.ico", "/apple-touch-icon.png", "/favicon-32x32.png", "/favicon.png", "/logo192.png", "/android-chrome-192x192.png" })
            list.Add(new Uri(site, path));
        return list.Distinct().ToList();
    }

    private static ImageSource? Decode(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }
}

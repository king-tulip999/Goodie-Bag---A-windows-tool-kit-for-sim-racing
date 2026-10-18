using System.IO;
using System.IO.Compression;

namespace GoodieBag.Services;

internal static class Quotes
{
    private static readonly Lazy<(byte[] Data, int[] Starts)> All = new(Load);
    private static int _last = -1;

    public static void Warm() => _ = Task.Run(() => All.Value);

    public static (string Text, string Author)? Next()
    {
        var (data, starts) = All.Value;
        int count = starts.Length - 1;
        if (count <= 0) return null;
        int i = ScreenshotRunner.Active ? 0 : Random.Shared.Next(count);
        if (i == _last && count > 1 && !ScreenshotRunner.Active) i = (i + 1 + Random.Shared.Next(count - 1)) % count;
        _last = i;
        var line = System.Text.Encoding.UTF8.GetString(data, starts[i], starts[i + 1] - starts[i]).TrimEnd('\r', '\n');
        int tab = line.IndexOf('\t');
        return tab < 0 ? (line, "") : (line[..tab], line[(tab + 1)..]);
    }

    private static (byte[] Data, int[] Starts) Load()
    {
        try
        {
            using var stream = typeof(Quotes).Assembly.GetManifestResourceStream("Quotes.txt.gz");
            if (stream is null) return (Array.Empty<byte>(), new[] { 0 });
            using var unzip = new GZipStream(stream, CompressionMode.Decompress);
            using var buffer = new MemoryStream();
            unzip.CopyTo(buffer);
            var data = buffer.ToArray();
            var starts = new List<int>();
            for (int at = 0; at < data.Length;)
            {
                int end = Array.IndexOf(data, (byte)'\n', at);
                if (end < 0) end = data.Length;
                if (end > at && !(end == at + 1 && data[at] == '\r')) starts.Add(at);
                at = end + 1;
            }
            starts.Add(data.Length);
            return (data, starts.ToArray());
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return (Array.Empty<byte>(), new[] { 0 });
        }
    }
}

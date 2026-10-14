using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoodieBag.SessionStats;

internal sealed record SharedLap(string Driver, string Car, string Track, string Config, IReadOnlyList<double>? Sectors, DateTime RecordedUtc, LapTrace Trace)
{
    public double LapTime => Trace.LapTime;
}

internal sealed record SharedLapInfo(long Id, string Driver, string Car, string Track, string Config, double LapTime, DateTime RecordedUtc);

internal static class LapFile
{
    public const string Extension = ".gblap";
    public const string Filter = "Goodie Bag lap (*.gblap)|*.gblap";
    private const int Format = 1;
    private const long MaxBytes = 32 << 20;

    private sealed class Header
    {
        [JsonPropertyName("goodieBagLap")] public int Format { get; set; }
        [JsonPropertyName("driver")] public string? Driver { get; set; }
        [JsonPropertyName("car")] public string? Car { get; set; }
        [JsonPropertyName("track")] public string? Track { get; set; }
        [JsonPropertyName("config")] public string? Config { get; set; }
        [JsonPropertyName("lapTime")] public double LapTime { get; set; }
        [JsonPropertyName("sectors")] public List<double>? Sectors { get; set; }
        [JsonPropertyName("recordedUtc")] public DateTime RecordedUtc { get; set; }
    }

    public static void Write(Stream output, SharedLap lap)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var header = new Header
        {
            Format = Format, Driver = lap.Driver, Car = lap.Car, Track = lap.Track, Config = lap.Config, LapTime = lap.LapTime,
            Sectors = lap.Sectors?.ToList(), RecordedUtc = DateTime.SpecifyKind(lap.RecordedUtc, DateTimeKind.Utc),
        };
        using (var s = zip.CreateEntry("lap.json", CompressionLevel.Optimal).Open())
            JsonSerializer.Serialize(s, header, new JsonSerializerOptions { WriteIndented = true });
        using (var s = zip.CreateEntry("trace.bin", CompressionLevel.NoCompression).Open())
            s.Write(lap.Trace.Encode());
    }

    public static SharedLap Read(Stream input)
    {
        if (input.CanSeek && input.Length > MaxBytes) throw new InvalidDataException("The file is too large to be a lap.");
        ZipArchive zip;
        try { zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { throw new InvalidDataException("This isn't a Goodie Bag lap file."); }
        using (zip)
        {
            if (zip.GetEntry("lap.json") is not { } info || zip.GetEntry("trace.bin") is not { } data || data.Length > MaxBytes)
                throw new InvalidDataException("This isn't a Goodie Bag lap file.");
            Header? header;
            try
            {
                using var s = info.Open();
                header = JsonSerializer.Deserialize<Header>(s);
            }
            catch (JsonException) { throw new InvalidDataException("This isn't a Goodie Bag lap file."); }
            if (header is null || header.Format <= 0) throw new InvalidDataException("This isn't a Goodie Bag lap file.");
            if (header.Format > Format) throw new InvalidDataException("This lap was saved by a newer Goodie Bag. Update to open it.");
            if (string.IsNullOrWhiteSpace(header.Track) || string.IsNullOrWhiteSpace(header.Car)) throw new InvalidDataException("The file doesn't say which car and track the lap is from.");
            LapTrace trace;
            try
            {
                using var s = data.Open();
                using var buffer = new MemoryStream();
                s.CopyTo(buffer);
                trace = LapTrace.Decode(buffer.ToArray());
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) { throw new InvalidDataException("The lap's telemetry is damaged."); }
            if (trace[TraceChannel.Time] is null || trace.LapTime <= 0) throw new InvalidDataException("The lap's telemetry is damaged.");
            var sectors = header.Sectors is { Count: > 1 } list && list.All(v => v > 0 && double.IsFinite(v)) ? list : null;
            return new SharedLap((header.Driver ?? "").Trim(), header.Car.Trim(), header.Track.Trim(), (header.Config ?? "").Trim(), sectors,
                DateTime.SpecifyKind(header.RecordedUtc, DateTimeKind.Utc), trace);
        }
    }

    public static string FileName(SharedLap lap)
    {
        string time = StatFormat.LapTime(lap.LapTime).Replace(':', '.');
        string name = string.Join(" ", new[] { lap.Driver, lap.Car, lap.Track, time }.Where(p => p.Length > 0));
        var bad = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => bad.Contains(c) ? ' ' : c).ToArray()).Trim();
        return (name.Length > 0 ? name : "lap") + Extension;
    }
}

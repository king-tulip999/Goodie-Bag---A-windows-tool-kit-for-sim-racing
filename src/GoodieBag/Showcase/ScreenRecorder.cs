using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace GoodieBag.Showcase;

internal sealed class ScreenRecorder
{
    public const int Fps = 60;

    private readonly Process _process;
    private readonly Stopwatch _clock;
    private (double At, long Frame)? _first;

    private ScreenRecorder(Process process, Stopwatch clock)
    {
        _process = process;
        _clock = clock;
    }

    public static ScreenRecorder Start(string ffmpeg, string output, Stopwatch clock)
    {
        string grab = $"vsrc_amf=monitor_index=0:framerate={Fps}";
        var info = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[]
        {
            "-hide_banner", "-y", "-f", "lavfi", "-i", grab,
            "-c:v", "h264_amf", "-quality", "quality", "-rc", "cqp", "-qp_i", "12", "-qp_p", "14",
            "-progress", "pipe:1", "-stats_period", "0.05", "-nostats", output,
        }) info.ArgumentList.Add(arg);
        var process = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg didn't start");
        var recorder = new ScreenRecorder(process, clock);
        process.OutputDataReceived += (_, e) => recorder.OnProgress(e.Data);
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) lock (recorder.Errors) recorder.Errors.Add(line); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return recorder;
    }

    public readonly List<string> Errors = new();

    public bool Running => !_process.HasExited && _first is not null;

    private void OnProgress(string? line)
    {
        if (line is null || _first is not null || !line.StartsWith("frame=", StringComparison.Ordinal)) return;
        if (long.TryParse(line.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out long frame) && frame > 0)
            _first = (_clock.Elapsed.TotalSeconds, frame);
    }

    public double VideoTime(double clockSeconds) => _first is { } f ? clockSeconds - f.At + f.Frame / (double)Fps : clockSeconds;

    public static (bool Ok, string Report) CheckFrames(string ffprobe, string file, double from, double length)
    {
        var info = new ProcessStartInfo(ffprobe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "packet=pts_time:stream=width,height", "-of", "csv=p=0", file })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("ffprobe didn't start");
        var times = new List<double>();
        string size = "";
        while (process.StandardOutput.ReadLine() is { } line)
        {
            if (line.Contains(',')) size = line.Replace(',', '×');
            else if (double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out double t) && t >= from && t <= from + length) times.Add(t);
        }
        process.WaitForExit();
        times.Sort();
        double frame = 1.0 / Fps, longest = 0;
        int gaps = 0, crowded = 0;
        for (int i = 1; i < times.Count; i++)
        {
            double step = times[i] - times[i - 1];
            if (step > frame * 1.5) { gaps++; longest = Math.Max(longest, step); }
            else if (step < frame * 0.5) crowded++;
        }
        double fps = times.Count > 1 ? (times.Count - 1) / (times[^1] - times[0]) : 0;
        bool ok = gaps == 0 && crowded == 0 && Math.Abs(fps - Fps) < 0.05;
        return (ok, string.Format(CultureInfo.InvariantCulture, "capture {0}: {1} frames in {2:0.00} s = {3:0.000} fps, {4} missed{5}, {6} crowded",
            size, times.Count, length, fps, gaps, gaps > 0 ? $" (longest gap {longest * 1000:0} ms)" : "", crowded));
    }

    public async Task StopAsync()
    {
        if (_process.HasExited) return;
        try
        {
            await _process.StandardInput.WriteAsync('q');
            await _process.StandardInput.FlushAsync();
        }
        catch (IOException) { }
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await _process.WaitForExitAsync(wait.Token); }
        catch (OperationCanceledException) { _process.Kill(); }
    }
}

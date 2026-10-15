using GoodieBag.Config;
using GoodieBag.Hardware;

namespace GoodieBag.Fans;

internal sealed record CalibrationStep(int Percent, int? Rpm, string Status, double Progress);

internal sealed class CalibrationOptions
{
    public bool TestStop { get; set; }

    public int Floor { get; set; } = 20;
}

internal sealed class FanCalibrator
{
    public const float AbortTempC = 80;

    private readonly HardwareService _hardware;
    private readonly bool _demo;
    private bool _stopTest;

    public FanCalibrator(HardwareService hardware, bool demo)
    {
        _hardware = hardware;
        _demo = demo;
    }

    public async Task<FanCalibration> RunAsync(string controlId, CalibrationOptions options, IProgress<CalibrationStep> progress, CancellationToken cancel)
    {
        _stopTest = options.TestStop;
        var steps = new List<int> { 100, 80, 60, 45, 35, 30, 25, 20 };
        if (options.TestStop)
            steps.AddRange(new[] { 15, 10, 5, 0 });
        steps = steps.Where(s => s >= Math.Min(options.Floor, options.TestStop ? 0 : options.Floor)).Distinct().ToList();
        if (options.Floor >= 60)
            steps = steps.Where(s => s >= options.Floor).ToList();

        var points = new List<CalibrationPoint>();
        var spreads = new List<double>();
        int done = 0;
        int total = steps.Count + (options.TestStop ? 3 : 0);

        try
        {
            foreach (int percent in steps)
            {
                cancel.ThrowIfCancellationRequested();
                progress.Report(new CalibrationStep(percent, CurrentRpm(controlId), $"Testing {percent}%…", (double)done / total));
                var (rpm, spread) = await MeasureAsync(controlId, percent, progress, done, total, cancel);
                if (percent == 100 && rpm == 0)
                    throw new CalibrationAbortedException("The fan reported 0 RPM at full speed (no speed signal on that header?), so nothing was saved.");
                points.Add(new CalibrationPoint { Percent = percent, Rpm = rpm });
                if (rpm > 0) spreads.Add(spread);
                done++;
                progress.Report(new CalibrationStep(percent, rpm, $"{percent}% → {rpm:N0} RPM", (double)done / total));
            }

            if (!points.Any(p => p.Rpm > 0))
                throw new CalibrationAbortedException("The fan never reported a speed above 0 RPM (no speed signal on that header?), so nothing was saved.");
            int? startPercent = null;
            int? stopPercent = options.TestStop ? points.Where(p => p.Rpm == 0 && points.Any(q => q.Percent > p.Percent && q.Rpm > 0))
                .Select(p => (int?)p.Percent).DefaultIfEmpty(null).Max() : null;
            if (options.TestStop && stopPercent is not null)
            {
                for (int percent = stopPercent.Value + 5; percent <= 60; percent += 5)
                {
                    cancel.ThrowIfCancellationRequested();
                    var (rpm, _) = await MeasureAsync(controlId, percent, progress, done, total, cancel);
                    progress.Report(new CalibrationStep(percent, rpm, $"Restart test: {percent}% → {rpm:N0} RPM", (double)Math.Min(total - 1, ++done) / total));
                    if (rpm > 0)
                    {
                        startPercent = percent;
                        break;
                    }
                }
            }

            int max = points.Where(p => p.Percent == 100).Select(p => p.Rpm).FirstOrDefault();
            var spinning = points.Where(p => p.Rpm > 0).ToList();
            int minStable = spinning.Count == 0 ? 100 : spinning.Min(p => p.Percent);
            int low = spinning.Where(p => p.Percent < 100).OrderBy(p => p.Percent).Select(p => p.Rpm).DefaultIfEmpty(max).First();

            progress.Report(new CalibrationStep(100, CurrentRpm(controlId), "Done", 1));
            return new FanCalibration
            {
                Date = DateTime.Now,
                Points = points.OrderByDescending(p => p.Percent).ToList(),
                MaxRpm = max,
                MinStablePercent = minStable,
                StopPercent = stopPercent,
                StartPercent = startPercent,
                RespondsToControl = max > 0 && low < max * 0.85,
                StableReadings = spreads.Count == 0 || spreads.Average() < 0.08,
            };
        }
        finally
        {
            _hardware.ClearOverride(controlId);
        }
    }

    private async Task<(int Rpm, double Spread)> MeasureAsync(string controlId, int percent, IProgress<CalibrationStep> progress,
        int done, int total, CancellationToken cancel)
    {
        const int Window = 4;
        _hardware.SetOverride(controlId, percent);
        var samples = new List<int>();
        int interval = _demo ? 150 : SampleMs, minimum = _demo ? 2 : 1500 / SampleMs, maximum = _demo ? 8 : 12_000 / SampleMs;
        for (int i = 0; i < maximum; i++)
        {
            await Task.Delay(interval, cancel);
            Guard();
            int rpm = CurrentRpm(controlId) ?? throw new CalibrationAbortedException("The fan's speed reading went away (sensors stopped or rescanned), so nothing was saved.");
            samples.Add(rpm);
            progress.Report(new CalibrationStep(percent, rpm, $"Testing {percent}%…", (double)done / total));
            if (i + 1 >= minimum && Settled(samples, Window)) break;
        }

        var tail = samples.TakeLast(Window).ToList();
        int median = tail.OrderBy(x => x).ElementAt(tail.Count / 2);
        double spread = median > 0 ? (tail.Max() - tail.Min()) / (double)median : 0;
        return (median, spread);
    }

    private const int SampleMs = Config.PerformanceSettings.CalibrationLoopMs;

    internal static bool Settled(IReadOnlyList<int> samples, int window)
    {
        if (samples.Count < window) return false;
        var last = samples.Skip(samples.Count - window).ToList();
        double band = Math.Max(25, last.Average() * 0.025);
        return last.Max() - last.Min() <= band && Math.Abs(last[^1] - last[0]) <= band * 0.6;
    }

    private int? CurrentRpm(string controlId)
    {
        var rpm = _hardware.Catalog?.Fans.FirstOrDefault(f => f.ControlId == controlId)?.Rpm;
        return rpm is { } r ? (int)Math.Round(r) : null;
    }

    private void Guard()
    {
        var snapshot = _hardware.Latest;
        if (_hardware.Catalog?.Emergency == true)
            throw new CalibrationAbortedException("The emergency temperature failsafe tripped, so all fans went to full speed.", safety: true);
        if (_stopTest && snapshot is { CpuTemp: null })
            throw new CalibrationAbortedException("The CPU temperature can't be read, so the stop test can't watch it. Calibrate without the stop test.", safety: true);
        if ((snapshot?.CpuTemp ?? 0) >= AbortTempC || (snapshot?.GpuTemp ?? 0) >= AbortTempC)
            throw new CalibrationAbortedException($"The CPU or GPU passed {AbortTempC:0} °C. Calibrate while the PC is idle.", safety: true);
    }
}

internal sealed class CalibrationAbortedException : Exception
{
    public bool Safety { get; }

    public CalibrationAbortedException(string message, bool safety = false) : base(message) => Safety = safety;
}

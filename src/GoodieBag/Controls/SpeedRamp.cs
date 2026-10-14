using System.Diagnostics;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GoodieBag.Controls;

internal sealed class SpeedRamp
{
    public const double Slowest = 0.02;

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();

    public SpeedRamp(ClockController controller, double from, Func<double> to, TimeSpan duration, Action<double>? step, Action done)
    {
        from = Math.Max(Slowest, from);
        _timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, _elapsed.Elapsed.TotalSeconds / duration.TotalSeconds);
            double eased = 1 - (1 - t) * (1 - t);
            controller.SpeedRatio = Math.Max(Slowest, from + (to() - from) * eased);
            step?.Invoke(eased);
            if (t < 1) return;
            _timer.Stop();
            done();
        };
        _timer.Start();
    }

    public void Stop() => _timer.Stop();
}

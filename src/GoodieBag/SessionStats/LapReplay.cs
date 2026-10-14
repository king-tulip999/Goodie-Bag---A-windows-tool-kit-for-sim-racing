namespace GoodieBag.SessionStats;

internal enum ReplaySync { Distance, Time }

internal static class LapReplay
{
    public static readonly double[] Speeds = { 0.25, 0.5, 1, 2, 4 };

    public static double DistanceOf(LapTrace lap, LapTrace followed, double time, ReplaySync sync) =>
        sync == ReplaySync.Distance ? Math.Min(lap.LengthM, followed.DistanceAtTime(time)) : lap.DistanceAtTime(time);

    public static (double Seconds, double Metres) Gap(LapTrace lap, LapTrace reference, double distance)
    {
        double d = Math.Clamp(distance, 0, Math.Min(lap.LengthM, reference.LengthM));
        double lapTime = lap.TimeAt(d);
        return (lapTime - reference.TimeAt(d), reference.DistanceAtTime(lapTime) - d);
    }

    public static int SectorAt(IReadOnlyList<double> starts, double distance)
    {
        int k = 0;
        while (k + 1 < starts.Count && distance >= starts[k + 1]) k++;
        return k;
    }

    public static double SectorGap(LapTrace lap, LapTrace reference, IReadOnlyList<double> starts, double distance)
    {
        double from = starts.Count > 0 ? starts[SectorAt(starts, distance)] : 0;
        return Gap(lap, reference, distance).Seconds - Gap(lap, reference, from).Seconds;
    }

    public static double At(double[] values, double step, double distance)
    {
        double at = Math.Clamp(distance / step, 0, values.Length - 1);
        int i = (int)at;
        if (i >= values.Length - 1) return values[^1];
        return values[i] + (values[i + 1] - values[i]) * (at - i);
    }

    public static double[] Headings(double[] x, double[] y, int span)
    {
        int n = x.Length;
        var h = new double[n];
        for (int i = 0; i < n; i++)
        {
            int a = Math.Max(0, i - span), b = Math.Min(n - 1, i + span);
            h[i] = b > a ? Math.Atan2(y[b] - y[a], x[b] - x[a]) : i > 0 ? h[i - 1] : 0;
        }
        return h;
    }

    public static double HeadingAt(double[] headings, double step, double distance)
    {
        double at = Math.Clamp(distance / step, 0, headings.Length - 1);
        int i = (int)at;
        if (i >= headings.Length - 1) return headings[^1];
        return headings[i] + Math.IEEERemainder(headings[i + 1] - headings[i], Math.Tau) * (at - i);
    }

    public static double Pedal(double throttle, double brake) =>
        brake > 0.02 ? -Math.Clamp(brake, 0, 1) : Math.Clamp(throttle, 0, 1);

    public static (double Time, bool Ended) Advance(double time, double seconds, double speed, double lapTime)
    {
        double next = time + seconds * speed;
        return next >= lapTime ? (lapTime, true) : (Math.Max(0, next), false);
    }
}

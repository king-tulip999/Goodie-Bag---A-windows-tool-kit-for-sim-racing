using System.Globalization;

namespace GoodieBag.SessionStats;

internal enum ReplaySync { Distance, Time }

internal enum NoteKind { Brake, Throttle, MinSpeed }

internal sealed record DriveNote(double Distance, NoteKind Kind, double Amount, bool ReferenceFirst)
{
    public string Title => Kind switch { NoteKind.Brake => "Braking", NoteKind.Throttle => "Throttle", _ => "Minimum speed" };

    public string Text(bool imperial)
    {
        if (Kind == NoteKind.MinSpeed)
        {
            double v = imperial ? Amount * 2.236936 : Amount * 3.6;
            string speed = v.ToString(v < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + (imperial ? " mph" : " km/h");
            return ReferenceFirst ? $"{speed} slower than the reference." : $"{speed} faster than the reference.";
        }
        double d = imperial ? Amount * 3.28084 : Amount;
        string gap = d.ToString(d < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + (imperial ? " ft" : " m");
        return Kind == NoteKind.Brake
            ? (ReferenceFirst ? $"Reference braked {gap} earlier than you." : $"You braked {gap} earlier than the reference.")
            : (ReferenceFirst ? $"Reference got on the throttle {gap} earlier than you." : $"You got on the throttle {gap} earlier than the reference.");
    }
}

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

    public static List<double> Onsets(double[] values, double step, double on, double off, double quietM)
    {
        var found = new List<double>();
        double quiet = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < off) { quiet += step; continue; }
            if (values[i] >= on && quiet >= quietM && i > 0) found.Add(i * step);
            if (values[i] >= on) quiet = 0;
        }
        return found;
    }

    public static List<DriveNote> Notes(double[]? brake, double[]? throttle, double step, double length,
        double[]? refBrake, double[]? refThrottle, double refStep, double refLength, int most = 6,
        double[]? speed = null, double[]? refSpeed = null)
    {
        var notes = new List<DriveNote>();
        if (length <= 0 || refLength <= 0) return notes;
        void Compare(double[]? mine, double[]? theirs, NoteKind kind, double on, double off, double quiet, double least)
        {
            if (mine is null || theirs is null) return;
            var you = Onsets(mine, step, on, off, quiet).Select(d => d * refLength / length).ToList();
            var found = new List<DriveNote>();
            foreach (double r in Onsets(theirs, refStep, on, off, quiet))
            {
                if (you.Count == 0) break;
                double y = you.MinBy(d => Math.Abs(d - r));
                double gap = y - r;
                if (Math.Abs(gap) < least || Math.Abs(gap) > 80) continue;
                found.Add(new DriveNote(Math.Min(r, y), kind, Math.Abs(gap), gap > 0));
            }
            notes.AddRange(found.OrderByDescending(n => n.Amount).Take(most));
        }
        Compare(brake, refBrake, NoteKind.Brake, 0.15, 0.05, 30, 3);
        Compare(throttle, refThrottle, NoteKind.Throttle, 0.5, 0.2, 20, 5);
        if (speed is not null && refSpeed is not null)
            notes.AddRange(Corners(refSpeed, refStep)
                .Select(c =>
                {
                    double at = c * length / refLength;
                    int from = Math.Max(0, (int)((at - 60) / step)), to = Math.Min(speed.Length - 1, (int)((at + 60) / step));
                    if (to <= from) return null;
                    double mine = double.MaxValue;
                    for (int i = from; i <= to; i++) mine = Math.Min(mine, speed[i]);
                    double gap = mine - refSpeed[(int)Math.Round(c / refStep)];
                    return Math.Abs(gap) < 0.55 ? null : new DriveNote(c, NoteKind.MinSpeed, Math.Abs(gap), gap < 0);
                })
                .OfType<DriveNote>().OrderByDescending(n => n.Amount).Take(most));
        return notes.OrderBy(n => n.Distance).ToList();
    }

    public static List<double> Corners(double[] speed, double step, double spanM = 150, double dropMs = 2.5)
    {
        var found = new List<double>();
        int span = Math.Max(1, (int)(spanM / step));
        for (int i = 1; i + 1 < speed.Length; i++)
        {
            int a = Math.Max(0, i - span), b = Math.Min(speed.Length - 1, i + span);
            double low = double.MaxValue, before = 0, after = 0;
            for (int j = a; j <= b; j++) low = Math.Min(low, speed[j]);
            if (speed[i] > low) continue;
            for (int j = a; j < i; j++) before = Math.Max(before, speed[j]);
            for (int j = i + 1; j <= b; j++) after = Math.Max(after, speed[j]);
            if (before - speed[i] < dropMs || after - speed[i] < dropMs) continue;
            if (found.Count > 0 && i * step - found[^1] < spanM) continue;
            found.Add(i * step);
        }
        return found;
    }

    public static double Pedal(double throttle, double brake) =>
        brake > 0.02 ? -Math.Clamp(brake, 0, 1) : Math.Clamp(throttle, 0, 1);

    public static (double Time, bool Ended) Advance(double time, double seconds, double speed, double lapTime)
    {
        double next = time + seconds * speed;
        return next >= lapTime ? (lapTime, true) : (Math.Max(0, next), false);
    }
}

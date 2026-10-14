using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Race;

internal enum FlagKind
{
    None, Disqualified, Black, Meatball, Red, Caution, Yellow, Debris, Blue, White, Checkered, OneLapToGreen, StartReady, StartSet, Green,
}

internal static class RaceControl
{
    public static List<FlagKind> Active(RaceFlags f)
    {
        var list = new List<FlagKind>();
        foreach (var (kind, on) in Order)
            if (on(f)) list.Add(kind);
        return list;
    }

    public static FlagKind Top(RaceFlags f)
    {
        foreach (var (kind, on) in Order)
            if (on(f)) return kind;
        return FlagKind.None;
    }

    private static readonly (FlagKind Kind, Func<RaceFlags, bool> On)[] Order =
    {
        (FlagKind.Disqualified, f => f.HasFlag(RaceFlags.Disqualify)),
        (FlagKind.Black, f => f.HasFlag(RaceFlags.Black) && !f.HasFlag(RaceFlags.Furled)),
        (FlagKind.Meatball, f => f.HasFlag(RaceFlags.Repair)),
        (FlagKind.Red, f => f.HasFlag(RaceFlags.Red)),
        (FlagKind.Caution, f => (f & (RaceFlags.Caution | RaceFlags.CautionWaving)) != 0),
        (FlagKind.Yellow, f => (f & (RaceFlags.Yellow | RaceFlags.YellowWaving)) != 0),
        (FlagKind.Debris, f => f.HasFlag(RaceFlags.Debris)),
        (FlagKind.Blue, f => f.HasFlag(RaceFlags.Blue)),
        (FlagKind.White, f => f.HasFlag(RaceFlags.White)),
        (FlagKind.Checkered, f => f.HasFlag(RaceFlags.Checkered)),
        (FlagKind.OneLapToGreen, f => f.HasFlag(RaceFlags.OneLapToGreen)),
        (FlagKind.StartReady, f => f.HasFlag(RaceFlags.StartReady)),
        (FlagKind.StartSet, f => f.HasFlag(RaceFlags.StartSet)),
        (FlagKind.Green, f => (f & (RaceFlags.Green | RaceFlags.GreenHeld | RaceFlags.StartGo)) != 0),
    };

    public static string Label(FlagKind k) => k switch
    {
        FlagKind.Disqualified => "Disqualified",
        FlagKind.Black => "Black flag",
        FlagKind.Meatball => "Repair",
        FlagKind.Red => "Red flag",
        FlagKind.Caution => "Caution",
        FlagKind.Yellow => "Yellow",
        FlagKind.Debris => "Debris",
        FlagKind.Blue => "Blue flag",
        FlagKind.White => "Last lap",
        FlagKind.Checkered => "Checkered",
        FlagKind.OneLapToGreen => "1 lap to green",
        FlagKind.StartReady => "Get ready",
        FlagKind.StartSet => "Set",
        FlagKind.Green => "Green",
        _ => "",
    };

    public static string Meaning(FlagKind k) => k switch
    {
        FlagKind.Disqualified => "You've been disqualified from the session.",
        FlagKind.Black => "Penalty: serve it in the pit lane.",
        FlagKind.Meatball => "Damage: pit for repairs.",
        FlagKind.Red => "Session stopped.",
        FlagKind.Caution => "Full-course caution: no overtaking.",
        FlagKind.Yellow => "Danger ahead: slow down, no overtaking.",
        FlagKind.Debris => "Debris on track.",
        FlagKind.Blue => "Faster car behind: let it by.",
        FlagKind.White => "One lap to go.",
        FlagKind.Checkered => "Session over.",
        FlagKind.OneLapToGreen => "Restart next time by.",
        FlagKind.StartReady => "Start coming up.",
        FlagKind.StartSet => "Lights on.",
        FlagKind.Green => "Racing.",
        _ => "",
    };

    public static AlertLevel Level(FlagKind k) => k switch
    {
        FlagKind.Disqualified or FlagKind.Black or FlagKind.Red or FlagKind.Meatball => AlertLevel.Critical,
        FlagKind.Caution or FlagKind.Yellow or FlagKind.Debris => AlertLevel.Warning,
        FlagKind.Green => AlertLevel.Good,
        _ => AlertLevel.Info,
    };

    public static string? PitStatus(int? status) => status switch
    {
        null or 0 => null,
        1 => "Service in progress",
        2 => "Service complete",
        100 => "Too far left",
        101 => "Too far right",
        102 => "Too far forward",
        103 => "Too far back",
        104 => "Bad angle",
        105 => "Can't repair that",
        _ => "Pit status " + status,
    };

    public static string SessionPhaseLabel(SessionPhase p) => p switch
    {
        SessionPhase.GetInCar => "Get in car",
        SessionPhase.Warmup => "Warm-up",
        SessionPhase.ParadeLaps => "Formation",
        SessionPhase.Racing => "Racing",
        SessionPhase.Checkered => "Checkered",
        SessionPhase.CoolDown => "Cool-down",
        _ => "",
    };
}

internal static class Proximity
{
    public static bool LeftOccupied(CarSides s) => s is CarSides.Left or CarSides.LeftRight or CarSides.TwoLeft;
    public static bool RightOccupied(CarSides s) => s is CarSides.Right or CarSides.LeftRight or CarSides.TwoRight;
}

internal enum SpotterCall { None, CarLeft, CarRight, ThreeWide, TwoLeft, TwoRight, ClearLeft, ClearRight, AllClear }

internal sealed class Spotter
{
    private CarSides _last = CarSides.Off;

    public void Reset() => _last = CarSides.Off;

    public SpotterCall Next(CarSides now)
    {
        var before = _last;
        _last = now;
        if (now == before || now == CarSides.Off) return SpotterCall.None;
        bool l0 = Proximity.LeftOccupied(before), r0 = Proximity.RightOccupied(before);
        bool l1 = Proximity.LeftOccupied(now), r1 = Proximity.RightOccupied(now);
        if (l1 && r1 && !(l0 && r0)) return SpotterCall.ThreeWide;
        if (now == CarSides.TwoLeft && before != CarSides.TwoLeft) return SpotterCall.TwoLeft;
        if (now == CarSides.TwoRight && before != CarSides.TwoRight) return SpotterCall.TwoRight;
        if (l1 && !l0) return SpotterCall.CarLeft;
        if (r1 && !r0) return SpotterCall.CarRight;
        if (!l1 && !r1 && (l0 || r0)) return l0 && r0 ? SpotterCall.AllClear : l0 ? SpotterCall.ClearLeft : SpotterCall.ClearRight;
        if (l0 && !l1) return SpotterCall.ClearLeft;
        if (r0 && !r1) return SpotterCall.ClearRight;
        return SpotterCall.None;
    }

    public static string Text(SpotterCall c) => c switch
    {
        SpotterCall.CarLeft => "Car left",
        SpotterCall.CarRight => "Car right",
        SpotterCall.ThreeWide => "Three wide",
        SpotterCall.TwoLeft => "Two cars left",
        SpotterCall.TwoRight => "Two cars right",
        SpotterCall.ClearLeft => "Clear left",
        SpotterCall.ClearRight => "Clear right",
        SpotterCall.AllClear => "Clear all round",
        _ => "",
    };
}

internal sealed class GapHistory
{
    public const int Samples = 24;
    private readonly double[,] _gaps = new double[TelemetryFrame.MaxCars, Samples];
    private readonly double[,] _times = new double[TelemetryFrame.MaxCars, Samples];
    private readonly int[] _count = new int[TelemetryFrame.MaxCars], _head = new int[TelemetryFrame.MaxCars];
    private double _lastSample = double.NegativeInfinity;

    public void Clear()
    {
        Array.Clear(_count);
        Array.Clear(_head);
        _lastSample = double.NegativeInfinity;
    }

    public void Sample(TelemetryFrame f, double now)
    {
        if (now - _lastSample < 0.5) return;
        _lastSample = now;
        int player = f.PlayerCarIdx;
        if ((uint)player >= TelemetryFrame.MaxCars) return;
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            if (i == player || !f.CarActive(i) || FieldOrder.GapSeconds(f, player, i) is not { } gap) { _count[i] = 0; continue; }
            if (_count[i] > 0 && Math.Sign(gap) != Math.Sign(Last(i)) && Math.Abs(gap - Last(i)) > 5) _count[i] = 0;
            _gaps[i, _head[i]] = gap;
            _times[i, _head[i]] = now;
            _head[i] = (_head[i] + 1) % Samples;
            _count[i] = Math.Min(Samples, _count[i] + 1);
        }
    }

    private double Last(int car) => _gaps[car, (_head[car] + Samples - 1) % Samples];

    public double? Trend(int car, double window = 8)
    {
        if ((uint)car >= TelemetryFrame.MaxCars || _count[car] < 2) return null;
        int newest = (_head[car] + Samples - 1) % Samples;
        double tNew = _times[car, newest], gNew = Math.Abs(_gaps[car, newest]);
        for (int k = _count[car] - 1; k >= 1; k--)
        {
            int idx = (newest - k + Samples) % Samples;
            double dt = tNew - _times[car, idx];
            if (dt > window + 0.01) continue;
            if (dt < 3) return null;
            return (gNew - Math.Abs(_gaps[car, idx])) / dt;
        }
        return null;
    }

    public double? TimeToCatch(int car, double gap)
    {
        if (Trend(car) is not { } t || t >= -0.005) return null;
        return Math.Abs(gap) / -t;
    }
}

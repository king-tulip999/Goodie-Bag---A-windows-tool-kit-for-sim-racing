using GoodieBag.Config;

namespace GoodieBag.SimRacing;

internal readonly record struct V2(double X, double Y)
{
    public static V2 operator +(V2 a, V2 b) => new(a.X + b.X, a.Y + b.Y);
    public static V2 operator -(V2 a, V2 b) => new(a.X - b.X, a.Y - b.Y);
    public static V2 operator *(V2 a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Dot(V2 b) => X * b.X + Y * b.Y;
}

internal sealed record ScreenSize(double WidthCm, double HeightCm, double Aspect, double ChordCm, double? RadiusCm);

internal sealed record FovResult
{
    public required ScreenSize Centre { get; init; }
    public required ScreenSize Side { get; init; }
    public required bool Triple { get; init; }

    public required double VerticalFov { get; init; }

    public required double HorizontalFovCentre { get; init; }

    public required double HorizontalFovTotal { get; init; }

    public required double VerticalFovSide { get; init; }

    public required double SideAngle { get; init; }

    public required double OptimalSideAngle { get; init; }

    public required bool OptimalUnreachable { get; init; }

    public required double TapeOuterEdgesCm { get; init; }

    public required double TapeEyeToSideEdgeCm { get; init; }

    public required double RigWidthCm { get; init; }

    public required double RigDepthCm { get; init; }

    public required double FootprintWidthCm { get; init; }
    public required double FootprintDepthCm { get; init; }

    public required double PixelsPerDegree { get; init; }

    public required IReadOnlyList<IReadOnlyList<V2>> Screens { get; init; }

    public required V2 RightEdge { get; init; }
}

internal static class FovCalculator
{
    public const double FrameCm = 1.75, PanelDepthCm = 8, StandDepthCm = 4.5;

    private const double Deg = 180 / Math.PI;

    private static double AtLeast(double value, double min) => double.IsFinite(value) ? Math.Max(min, value) : min;

    public static double AspectOf(FovScreen s) => s.Aspect switch
    {
        "16:9" => 16.0 / 9,
        "16:10" => 16.0 / 10,
        "21:9" => 21.0 / 9,
        "32:9" => 32.0 / 9,
        "4:3" => 4.0 / 3,
        "5:4" => 5.0 / 4,
        "custom" when s.CustomAspectW > 0 && s.CustomAspectH > 0 && double.IsFinite(s.CustomAspectW / s.CustomAspectH) => s.CustomAspectW / s.CustomAspectH,
        _ when s.ResolutionW > 0 && s.ResolutionH > 0 => (double)s.ResolutionW / s.ResolutionH,
        _ => 16.0 / 9,
    };

    public static ScreenSize SizeOf(FovScreen s)
    {
        double w, h, aspect;
        if (s.SizeMode == ScreenSizeMode.Measured && s.WidthCm > 0 && s.HeightCm > 0 && double.IsFinite(s.WidthCm) && double.IsFinite(s.HeightCm))
        {
            w = s.WidthCm;
            h = s.HeightCm;
            aspect = w / h;
        }
        else
        {
            aspect = AspectOf(s);
            double diagonalCm = AtLeast(s.DiagonalIn, 1) * 2.54;
            w = diagonalCm * aspect / Math.Sqrt(aspect * aspect + 1);
            h = w / aspect;
        }

        double? radius = s.Curved && s.RadiusMm > 0 && double.IsFinite(s.RadiusMm) ? s.RadiusMm / 10 : null;
        if (radius is { } r && w / (2 * r) > Math.PI / 2)
            radius = w / Math.PI;
        double chord = radius is { } rr ? 2 * rr * Math.Sin(w / (2 * rr)) : w;
        return new ScreenSize(w, h, aspect, chord, radius);
    }

    public static FovResult Calculate(FovSetup setup)
    {
        var centre = SizeOf(setup.Centre);
        var side = setup.MixedSides ? SizeOf(setup.Side) : centre;
        var sideSpec = setup.MixedSides ? setup.Side : setup.Centre;
        double d = AtLeast(setup.DistanceCm, 1);
        double gap = double.IsFinite(setup.BezelMm) ? setup.BezelMm / 10 : 0;

        var centrePoints = ArcPoints(centre, d);
        var edge = centrePoints[^1];
        var tangent = centre.RadiusCm is { } rc ? new V2(Math.Cos(centre.WidthCm / (2 * rc)), -Math.Sin(centre.WidthCm / (2 * rc))) : new V2(1, 0);

        double vFov = 2 * Math.Atan(centre.HeightCm / 2 / d) * Deg;
        double vFovSide = 2 * Math.Atan(side.HeightCm / 2 / d) * Deg;
        double hFovCentre = 2 * Math.Atan2(edge.X, edge.Y) * Deg;
        double ppd = setup.Centre.ResolutionW > 0 && hFovCentre > 0 ? setup.Centre.ResolutionW / hFovCentre : 0;

        if (!setup.Triple)
        {
            double width = 2 * edge.X + gap;
            return new FovResult
            {
                Centre = centre, Side = side, Triple = false,
                VerticalFov = vFov, VerticalFovSide = vFov,
                HorizontalFovCentre = hFovCentre, HorizontalFovTotal = hFovCentre,
                SideAngle = 0, OptimalSideAngle = 0, OptimalUnreachable = false,
                TapeOuterEdgesCm = 2 * edge.X, TapeEyeToSideEdgeCm = edge.Length,
                RigWidthCm = width, RigDepthCm = d - edge.Y,
                FootprintWidthCm = width + 2 * FrameCm, FootprintDepthCm = d - edge.Y + PanelDepthCm,
                PixelsPerDegree = ppd,
                Screens = new[] { Mirror(centrePoints) },
                RightEdge = edge,
            };
        }

        var hinge = edge + tangent * (gap / 2);
        double k = gap / 2 + side.ChordCm / 2;
        double hingeLength = hinge.Length;
        bool unreachable = k > hingeLength;
        double optimal = (Math.Acos(Math.Clamp(-k / hingeLength, -1, 1)) - Math.Atan2(hinge.Y, hinge.X)) * Deg;
        optimal = Math.Clamp(optimal, 0, 180);

        double theta = Math.Clamp(setup.AutoAngle || !double.IsFinite(setup.SideAngleDeg) ? optimal : setup.SideAngleDeg, 0, 180);
        double t = theta / Deg;
        var u = new V2(Math.Cos(t), -Math.Sin(t));
        var sideStart = hinge + u * (gap / 2);
        var sideEnd = sideStart + u * side.ChordCm;
        var sidePoints = SidePoints(side, sideStart, u);

        double hFovTotal = 2 * Math.Atan2(sideEnd.X, sideEnd.Y) * Deg;
        var outer = sideEnd + u * (gap / 2);
        double rigWidth = 2 * Math.Max(outer.X, hinge.X);
        double nearestY = Math.Min(outer.Y, Math.Min(sideStart.Y, edge.Y));
        double rigDepth = d - nearestY;

        return new FovResult
        {
            Centre = centre, Side = side, Triple = true,
            VerticalFov = vFov, VerticalFovSide = vFovSide,
            HorizontalFovCentre = hFovCentre, HorizontalFovTotal = hFovTotal,
            SideAngle = theta, OptimalSideAngle = optimal, OptimalUnreachable = unreachable,
            TapeOuterEdgesCm = 2 * sideEnd.X, TapeEyeToSideEdgeCm = sideEnd.Length,
            RigWidthCm = rigWidth, RigDepthCm = rigDepth,
            FootprintWidthCm = rigWidth + 2 * FrameCm, FootprintDepthCm = rigDepth + PanelDepthCm + StandDepthCm,
            PixelsPerDegree = ppd,
            Screens = new[] { Mirror(centrePoints), sidePoints, sidePoints.Select(p => new V2(-p.X, p.Y)).Reverse().ToList() },
            RightEdge = sideEnd,
        };
    }

    private static List<V2> ArcPoints(ScreenSize s, double d)
    {
        if (s.RadiusCm is not { } r)
            return new List<V2> { new(0, d), new(s.WidthCm / 2, d) };
        double alpha = s.WidthCm / (2 * r);
        var points = new List<V2>();
        for (int i = 0; i <= 16; i++)
        {
            double a = alpha * i / 16;
            points.Add(new V2(r * Math.Sin(a), d - r * (1 - Math.Cos(a))));
        }
        return points;
    }

    private static List<V2> SidePoints(ScreenSize s, V2 start, V2 u)
    {
        if (s.RadiusCm is not { } r)
            return new List<V2> { start, start + u * s.ChordCm };
        var normal = new V2(-u.Y, u.X);
        double alpha = s.WidthCm / (2 * r);
        double sagitta = r * (1 - Math.Cos(alpha));
        var points = new List<V2>();
        for (int i = 0; i <= 16; i++)
        {
            double a = -alpha + 2 * alpha * i / 16;
            double along = s.ChordCm / 2 + r * Math.Sin(a);
            double bow = r * Math.Cos(a) - r + sagitta;
            points.Add(start + u * along + normal * bow);
        }
        return points;
    }

    private static List<V2> Mirror(List<V2> rightHalf) =>
        rightHalf.Skip(1).Reverse().Select(p => new V2(-p.X, p.Y)).Concat(rightHalf).ToList();

    internal sealed record SimValue(string Sim, string Axis, double? Degrees, string Note, string? Range, bool OutOfRange, string? Extra = null);

    public static IReadOnlyList<SimValue> SimValues(FovResult r)
    {
        double v = r.VerticalFov, hc = r.HorizontalFovCentre, ht = r.HorizontalFovTotal;
        bool tri = r.Triple;

        SimValue Vertical(string sim, string note, double? min = null, double? max = null, string? extra = null) =>
            new(sim, "Vertical", v, note, min is null ? null : $"{min:0}–{max:0}°", min is not null && (v < min || v > max), extra);
        SimValue Horizontal(string sim, double value, string note, double? min = null, double? max = null, string? extra = null) =>
            new(sim, "Horizontal", value, note, min is null ? null : $"{min:0}–{max:0}°", min is not null && (value < min || value > max), extra);

        return new List<SimValue>
        {
            Horizontal("iRacing", ht, tri ? "Total across all three screens, not the center one. Also enter the side angle and bezel in its triple-screen setup." : "Horizontal FOV of your screen."),
            Vertical("Assetto Corsa", tri ? "Vertical FOV; on triples the screen geometry (angle, bezel) drives the rest." : "Vertical FOV."),
            Vertical("Assetto Corsa Competizione", "Set per car in View Settings.", 10, 90),
            Vertical("Assetto Corsa EVO", "Early access; the default of 55 suggests a vertical value."),
            Vertical("Le Mans Ultimate", "Vertical FOV (default 49). With Multi-View on the slider is locked and geometry decides.", 10, 100),
            Vertical("rFactor 2", "Vertical FOV. Multi-View overrides the FOV value entirely."),
            Vertical("Automobilista 1", tri ? "With MultiView on this is the center screen's value." : "Vertical FOV."),
            Vertical("BeamNG.drive", "Often listed as horizontal; it's vertical (default 65).", 10, 120),
            Horizontal("Automobilista 2", tri ? hc : hc, tri ? "With triple-screen mode on, enter the center screen's value." : "Horizontal FOV.",
                extra: tri ? $"Triple mode off (one stretched image): {ht:0.0}°" : null),
            Horizontal("Project CARS 2", hc, tri ? "Same as AMS2: center screen with triple-screen mode on." : "Horizontal FOV.",
                extra: tri ? $"Triple mode off: {ht:0.0}°" : null),
            Horizontal("RaceRoom", hc, "Degrees in versions since 2021 (older guides use a 0.5–1.3 multiplier).",
                extra: tri ? $"Across all three: {ht:0.0}°" : null),
            Horizontal("Live for Speed", hc, tri ? "Once side screens are configured, the number is your center screen only." : "Horizontal FOV (default 90).", 30, 160),
            new("Gran Turismo 7", "None", null, "No FOV setting, only seat height and depth.", null, false),
            new("F1 (Codemasters / EA)", "Slider", null, "Unitless slider, roughly −20 to +20 around a fixed base.", null, false),
            new("EA Sports WRC", "Slider", null, "Unitless 0–100 slider.", null, false),
            new("DiRT Rally 2.0", "Slider", null, "Unitless slider in game; degrees only by editing its XML.", null, false),
            new("Forza Motorsport / Horizon", "Unknown", null, "25–65 slider; which axis has never been stated.", null, false),
        };
    }
}

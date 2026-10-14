using GoodieBag.SimRacing.FuelEngine;

namespace GoodieBag.SimRacing.Race;

internal sealed record FuelPlan(double PerLap, double LapsOfFuel, double? LapsToGo, double? ToFinish, double? Surplus, int? Stops,
    double? AddAtNextStop, double? WindowOpensIn, double? WindowClosesIn)
{
    public bool CanFinish => Surplus is >= 0;

    public double? Expected { get; init; }

    public double StatisticalReserve { get; init; }

    public double Margin { get; init; }

    public double? AtFlag { get; init; }

    public double? CurrentLap { get; init; }
    public bool CurrentLapFromProfile { get; init; }

    public bool LapsToGoForecast { get; init; }

    public EstimateReliability Reliability { get; init; }

    public string? Warning { get; init; }
}

internal static class FuelMath
{
    public const double AddStep = 0.1;

    public static FuelPlan? Plan(double fuel, double? perLap, double? lapsToGo, double? tank, double reserve, double? toFinish = null)
    {
        if (perLap is not > 0.01) return null;
        double rate = perLap.Value;
        double lapsOfFuel = Math.Max(0, fuel / rate);
        if (lapsToGo is not { } togo)
            return new FuelPlan(rate, lapsOfFuel, null, null, null, null, null, null, lapsOfFuel);
        double need = toFinish ?? togo * rate + reserve;
        double surplus = fuel - need;
        int? stops = null;
        double? add = null, opens = null;
        if (surplus >= 0) stops = 0;
        else if (tank is > 0)
        {
            double short_ = -surplus;
            stops = (int)Math.Ceiling(short_ / tank.Value - 1e-9);
            add = Math.Min(FuelStrategyCalculator.RoundUpToIncrement(short_, AddStep), tank.Value);
            double tankLaps = (stops.Value * tank.Value - reserve) / rate;
            opens = Math.Max(0, togo - tankLaps);
        }
        return new FuelPlan(rate, lapsOfFuel, togo, need, surplus, stops, add, opens, lapsOfFuel);
    }
}

internal static class FuelPlanner
{
    public static FuelPlan? Make(double fuel, FuelConsumptionEstimate estimate, (double Laps, bool Forecast)? lapsToGo, double lapProgress,
        FuelSectorProfileEstimate? currentLap, double? tank, double marginLaps, SafetyMode mode, double extraLiters = 0, double extraShare = 0)
    {
        if (!estimate.HasEstimate || !double.IsFinite(fuel) || fuel < 0) return null;
        double rate = estimate.MeanLitersPerLap;
        double margin = Math.Max(0, rate * marginLaps + Math.Max(0, extraLiters) + (lapsToGo is { } lt ? rate * Math.Max(0, lt.Laps) * Math.Max(0, extraShare) : 0));
        string? warning = Warning(estimate);
        if (lapsToGo is not { } togo)
            return FuelMath.Plan(fuel, rate, null, tank, margin)! with { Reliability = estimate.Reliability, Warning = warning, Margin = margin };

        double laps = Math.Max(0, togo.Laps);
        double partial = laps <= 0 ? 0 : laps - (Math.Ceiling(laps) - 1);
        bool useProfile = currentLap is not null && laps > 0 && Math.Abs(partial - (1 - Math.Clamp(lapProgress, 0, 1))) < 0.01;
        var result = FuelStrategyCalculator.Calculate(new FuelStrategyRequest
        {
            CurrentFuelLiters = fuel,
            LapsRemaining = laps,
            CurrentLapFuelRemainingLiters = useProfile ? currentLap!.ExpectedFuelRemainingLiters : null,
            Consumption = estimate,
            ExplicitReserveLiters = margin,
            SafetyMode = mode,
            UsableTankCapacityLiters = tank is > 0 ? tank : null,
            FuelAddIncrementLiters = FuelMath.AddStep,
        });
        double reserve = result.StatisticalUncertaintyReserveLiters + result.ExplicitReserveLiters;
        var plan = FuelMath.Plan(fuel, rate, laps, tank, reserve, result.RecommendedFuelToFinishLiters)!;
        return plan with
        {
            Expected = result.ExpectedFuelToFinishLiters,
            StatisticalReserve = result.StatisticalUncertaintyReserveLiters,
            Margin = result.ExplicitReserveLiters,
            AtFlag = result.ProjectedFuelAtFinishLiters,
            CurrentLap = laps <= 0 ? 0 : useProfile ? currentLap!.ExpectedFuelRemainingLiters : partial * rate,
            CurrentLapFromProfile = useProfile,
            LapsToGoForecast = togo.Forecast,
            Reliability = estimate.Reliability,
            Warning = warning,
        };
    }

    internal static string? Warning(FuelConsumptionEstimate e) => e.Reliability switch
    {
        EstimateReliability.Unavailable => "No clean laps measured yet.",
        EstimateReliability.Learning => $"Learning: {e.UsedLapCount} clean lap{(e.UsedLapCount == 1 ? "" : "s")} so far. Treat the plan as a rough guide." + (e.HasStatisticalBound ? "" : " No safety reserve yet (needs 2+ clean laps); only your margin is added."),
        EstimateReliability.Low => $"Low confidence: {e.UsedLapCount} clean laps, the figures may still move.",
        _ => null,
    };

    public static SafetyMode Mode(Config.FuelSafety safety) => safety switch
    {
        Config.FuelSafety.Aggressive => SafetyMode.Aggressive,
        Config.FuelSafety.Conservative => SafetyMode.Conservative,
        _ => SafetyMode.Balanced,
    };
}

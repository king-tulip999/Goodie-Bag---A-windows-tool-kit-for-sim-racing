namespace GoodieBag.SimRacing.FuelEngine;

public static class FuelStrategyCalculator
{
    public static FuelStrategyResult Calculate(FuelStrategyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        var estimate = request.Consumption;
        double laps = request.LapsRemaining;
        double fullLapsAfterCurrent = laps <= 0 ? 0 : Math.Ceiling(laps) - 1;
        double currentLapFraction = laps <= 0 ? 0 : laps - fullLapsAfterCurrent;
        double estimatedCurrentLapFuel = laps <= 0 ? 0 : request.CurrentLapFuelRemainingLiters ?? currentLapFraction * estimate.MeanLitersPerLap;
        double expected = fullLapsAfterCurrent * estimate.MeanLitersPerLap + estimatedCurrentLapFuel;
        bool hasBound = estimate.ConservativeLitersPerLap is > 0 && estimate.StandardDeviationLitersPerLap is not null;
        double uncertaintyReserve = 0;

        if (hasBound && laps > 0)
        {
            double s2 = Math.Pow(estimate.StandardDeviationLitersPerLap!.Value, 2);
            double variance = request.CurrentLapFuelRemainingLiters is not null
                ? fullLapsAfterCurrent * s2 + fullLapsAfterCurrent * fullLapsAfterCurrent * s2 / Math.Max(1, estimate.EffectiveSampleSize)
                : (fullLapsAfterCurrent + currentLapFraction * currentLapFraction) * s2
                    + laps * laps * s2 / Math.Max(1, estimate.EffectiveSampleSize);
            double multiplier = request.SafetyMode switch
            {
                SafetyMode.Aggressive => 1.28,
                SafetyMode.Balanced => 1.645,
                SafetyMode.Conservative => 2.326,
                _ => throw new ArgumentOutOfRangeException(nameof(request.SafetyMode)),
            };
            uncertaintyReserve = multiplier * Math.Sqrt(Math.Max(0, variance));
            double perLapGuard = Math.Max(0, estimate.ConservativeLitersPerLap!.Value - estimate.MeanLitersPerLap);
            double futureLapCount = fullLapsAfterCurrent + (request.CurrentLapFuelRemainingLiters is null ? currentLapFraction : 0);
            uncertaintyReserve = Math.Max(uncertaintyReserve, Math.Min(futureLapCount, Math.Sqrt(futureLapCount)) * perLapGuard);
            uncertaintyReserve = Math.Max(uncertaintyReserve, MinimumReserveFraction(request) * expected);
        }

        double explicitReserve = request.ExplicitReserveLiters;
        double recommended = expected + uncertaintyReserve + explicitReserve;
        double projected = request.CurrentFuelLiters - expected;
        double deficit = Math.Max(0, recommended - request.CurrentFuelLiters);
        double add = RoundUpToIncrement(deficit, request.FuelAddIncrementLiters);
        if (request.UsableTankCapacityLiters is { } capacity) add = Math.Min(add, Math.Max(0, capacity - request.CurrentFuelLiters));
        double? lapsOfFuel = estimate.MeanLitersPerLap > 0 ? request.CurrentFuelLiters / estimate.MeanLitersPerLap : null;
        bool canFinish = request.CurrentFuelLiters + 1e-9 >= recommended;
        string partialNote = request.CurrentLapFuelRemainingLiters is null && laps > 0
            ? " Current-lap fuel is approximated proportionally from lap progress; fuel burn is not uniform around the circuit, so this is less precise than a validated sector profile."
            : request.CurrentLapFuelRemainingLiters is not null ? " Current-lap fuel uses the supplied remaining-current-lap estimate; its uncertainty is not independently modeled." : string.Empty;
        string explanation = (hasBound
            ? $"Expected burn is {expected:0.00} L. Statistical reserve is {uncertaintyReserve:0.00} L at the {request.SafetyMode.ToString().ToLowerInvariant()} planning level (at least {MinimumReserveFraction(request):P1} of the expected burn); explicit reserve is {explicitReserve:0.00} L. The bound is a model-based estimate, not a guarantee."
            : $"Expected burn is {expected:0.00} L. A statistical reserve cannot be estimated reliably from the current sample; only the explicit {explicitReserve:0.00} L reserve is included. Treat the recommendation as low-confidence.") + partialNote;

        return new(expected, uncertaintyReserve, explicitReserve, recommended, projected, deficit, add, lapsOfFuel,
            canFinish, hasBound, estimate.Reliability, explanation);
    }

    public static double MinimumReserveFraction(FuelStrategyRequest request) => request.MinimumReserveFraction ?? request.SafetyMode switch
    {
        SafetyMode.Aggressive => 0.010,
        SafetyMode.Conservative => 0.020,
        _ => 0.015,
    };

    public static double RoundUpToIncrement(double quantity, double increment)
    {
        if (!double.IsFinite(quantity) || quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (!double.IsFinite(increment) || increment <= 0) throw new ArgumentOutOfRangeException(nameof(increment));
        if (quantity == 0) return 0;
        return Math.Ceiling((quantity - 1e-10) / increment) * increment;
    }

    private static void Validate(FuelStrategyRequest request)
    {
        if (!double.IsFinite(request.CurrentFuelLiters) || request.CurrentFuelLiters < 0) throw new ArgumentOutOfRangeException(nameof(request.CurrentFuelLiters));
        if (!double.IsFinite(request.LapsRemaining) || request.LapsRemaining < 0) throw new ArgumentOutOfRangeException(nameof(request.LapsRemaining));
        if (request.CurrentLapFuelRemainingLiters is { } partial && (!double.IsFinite(partial) || partial < 0)) throw new ArgumentOutOfRangeException(nameof(request.CurrentLapFuelRemainingLiters));
        if (!double.IsFinite(request.ExplicitReserveLiters) || request.ExplicitReserveLiters < 0) throw new ArgumentOutOfRangeException(nameof(request.ExplicitReserveLiters));
        if (!double.IsFinite(request.FuelAddIncrementLiters) || request.FuelAddIncrementLiters <= 0) throw new ArgumentOutOfRangeException(nameof(request.FuelAddIncrementLiters));
        if (request.MinimumReserveFraction is { } floor && (!double.IsFinite(floor) || floor < 0 || floor > 0.5)) throw new ArgumentOutOfRangeException(nameof(request.MinimumReserveFraction));
        if (request.UsableTankCapacityLiters is { } capacity && (!double.IsFinite(capacity) || capacity <= 0)) throw new ArgumentOutOfRangeException(nameof(request.UsableTankCapacityLiters));
        if (!request.Consumption.HasEstimate) throw new ArgumentException("A valid fuel consumption estimate is required.", nameof(request.Consumption));
        if (request.Consumption.StandardDeviationLitersPerLap is { } sd && (!double.IsFinite(sd) || sd < 0)) throw new ArgumentException("Consumption standard deviation must be finite and nonnegative.", nameof(request.Consumption));
        if (request.Consumption.ConservativeLitersPerLap is { } upper && (!double.IsFinite(upper) || upper < request.Consumption.MeanLitersPerLap)) throw new ArgumentException("The conservative burn bound must be finite and at least the mean.", nameof(request.Consumption));
        if (!double.IsFinite(request.Consumption.EffectiveSampleSize) || request.Consumption.EffectiveSampleSize < 0) throw new ArgumentException("Effective sample size must be finite and nonnegative.", nameof(request.Consumption));
    }
}

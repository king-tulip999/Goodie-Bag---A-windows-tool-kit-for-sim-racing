namespace GoodieBag.SimRacing.FuelEngine;

[Flags]
public enum FuelLapFlags
{
    None = 0,
    PitRoad = 1 << 0,
    Refueled = 1 << 1,
    Caution = 1 << 2,
    OutLap = 1 << 3,
    InLap = 1 << 4,
    FormationLap = 1 << 5,
    OffTrackOrIncident = 1 << 6,
    InvalidTelemetry = 1 << 7,
    Incomplete = 1 << 8,
    TowOrReset = 1 << 9,
}

public sealed record FuelLapSample(
    int LapNumber,
    double FuelUsedLiters,
    double? LapTimeSeconds = null,
    FuelLapFlags Flags = FuelLapFlags.None,
    DateTimeOffset? CompletedAt = null)
{
    public bool IsEligible => Flags == FuelLapFlags.None
        && double.IsFinite(FuelUsedLiters)
        && FuelUsedLiters > 0
        && (LapTimeSeconds is null || (double.IsFinite(LapTimeSeconds.Value) && LapTimeSeconds.Value > 0));
}

public enum EstimateReliability { Unavailable, Learning, Low, Moderate, High }

public sealed record FuelEstimatorOptions
{
    public int WindowSize { get; init; } = 20;
    public double WeightHalfLifeLaps { get; init; } = 6;
    public double MinimumRobustScaleLiters { get; init; } = 0.03;
    public double OutlierThreshold { get; init; } = 3.5;
    public double OneSidedConfidence { get; init; } = 0.95;
}

public sealed record FuelConsumptionEstimate(
    double MeanLitersPerLap,
    double? StandardDeviationLitersPerLap,
    double? ConservativeLitersPerLap,
    int EligibleLapCount,
    int UsedLapCount,
    int RejectedOutlierCount,
    double EffectiveSampleSize,
    double CoefficientOfVariation,
    double? RecentTrendLitersPerLap,
    EstimateReliability Reliability,
    string Explanation)
{
    public bool HasEstimate => MeanLitersPerLap > 0 && double.IsFinite(MeanLitersPerLap);
    public bool HasStatisticalBound => ConservativeLitersPerLap is > 0;
}

public enum SafetyMode { Aggressive, Balanced, Conservative }

public sealed record FuelStrategyRequest
{
    public required double CurrentFuelLiters { get; init; }
    public required double LapsRemaining { get; init; }
    public double? CurrentLapFuelRemainingLiters { get; init; }
    public required FuelConsumptionEstimate Consumption { get; init; }
    public double ExplicitReserveLiters { get; init; }
    public SafetyMode SafetyMode { get; init; } = SafetyMode.Balanced;
    public double? UsableTankCapacityLiters { get; init; }
    public double FuelAddIncrementLiters { get; init; } = 0.1;
    public double? MinimumReserveFraction { get; init; }
}

public sealed record FuelStrategyResult(
    double ExpectedFuelToFinishLiters,
    double StatisticalUncertaintyReserveLiters,
    double ExplicitReserveLiters,
    double RecommendedFuelToFinishLiters,
    double ProjectedFuelAtFinishLiters,
    double FuelDeficitLiters,
    double RecommendedFuelToAddLiters,
    double? EstimatedLapsOfFuel,
    bool CanFinishWithRecommendedReserve,
    bool StatisticalBoundAvailable,
    EstimateReliability Reliability,
    string Explanation);

public sealed record TimedRaceInput
{
    public required double SessionTimeRemainingSeconds { get; init; }
    public required double LeaderLapProgress { get; init; }
    public required double LeaderLapTimeSeconds { get; init; }
    public required double PlayerLapProgress { get; init; }
    public required double PlayerLapTimeSeconds { get; init; }
    public bool WhiteFlagObserved { get; init; }
    public bool CheckeredFlagObserved { get; init; }
    public bool LeaderFinished { get; init; }
}

public sealed record TimedRaceEstimate(
    double LapsRemainingExpected,
    double? LapsRemainingLower,
    double? LapsRemainingUpper,
    double EstimatedSecondsUntilLeaderWhiteFlag,
    bool IsAuthoritative,
    string Explanation);

public sealed record FuelTelemetrySample
{
    public required string SessionKey { get; init; }
    public required string CarKey { get; init; }
    public required string TrackKey { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required int LapCompleted { get; init; }
    public required double LapDistPct { get; init; }
    public required double FuelLevelLiters { get; init; }
    public bool OnPitRoad { get; init; }
    public bool UnderCaution { get; init; }
    public bool IsFormationLap { get; init; }
    public bool OffTrackOrIncident { get; init; }
    public bool IsOnTrack { get; init; }
}

public sealed record FuelTrackerUpdate(
    IReadOnlyList<FuelLapSample> NewLaps,
    bool WasReset,
    bool RefuelDetected,
    string Status);

public sealed record FuelSectorLapSample(
    int LapNumber,
    IReadOnlyList<double> FuelAtBoundariesLiters,
    FuelLapFlags Flags = FuelLapFlags.None,
    DateTimeOffset? CompletedAt = null);

public sealed record FuelSectorProfileOptions
{
    public int WindowSize { get; init; } = 20;
    public int MinimumSamplesPerSector { get; init; } = 3;
    public double WeightHalfLifeLaps { get; init; } = 6;
    public double MinimumRobustScaleLiters { get; init; } = 0.02;
    public double OutlierThreshold { get; init; } = 3.5;
    public SafetyMode SafetyMode { get; init; } = SafetyMode.Balanced;
}

public sealed record FuelSectorProfileEstimate(
    double ExpectedFuelRemainingLiters,
    double ConservativeFuelRemainingLiters,
    int CurrentSectorIndex,
    int SectorCount,
    int CleanLapCount,
    int MinimumSamplesInAnySector,
    double? StandardDeviationOfRemainingFuelLiters,
    EstimateReliability Reliability,
    string Explanation);

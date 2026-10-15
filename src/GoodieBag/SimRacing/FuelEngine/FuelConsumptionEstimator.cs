namespace GoodieBag.SimRacing.FuelEngine;

public static class FuelConsumptionEstimator
{
    private const double MadToSigma = 1.4826;

    public static FuelConsumptionEstimate Estimate(IEnumerable<FuelLapSample> samples, FuelEstimatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        options ??= new FuelEstimatorOptions();
        ValidateOptions(options);

        var eligible = samples.Where(s => s is not null && s.IsEligible)
            .TakeLast(options.WindowSize)
            .ToArray();
        if (eligible.Length == 0)
            return new(0, null, null, 0, 0, 0, 0, 0, null, EstimateReliability.Unavailable,
                "No valid clean-green lap samples are available. Complete clean laps to establish a measured baseline.");

        var values = eligible.Select(s => s.FuelUsedLiters).ToArray();
        double median = Median(values);
        double mad = Median(values.Select(v => Math.Abs(v - median)).ToArray());
        double robustScale = Math.Max(options.MinimumRobustScaleLiters, mad * MadToSigma);
        double threshold = options.OutlierThreshold * robustScale;

        var accepted = new List<(FuelLapSample Sample, double Value, double Weight)>();
        int rejected = 0;
        for (int i = 0; i < eligible.Length; i++)
        {
            double value = values[i];
            if (eligible.Length >= 5 && Math.Abs(value - median) > threshold)
            {
                rejected++;
                continue;
            }
            int age = eligible.Length - 1 - i;
            double weight = Math.Exp(-Math.Log(2) * age / options.WeightHalfLifeLaps);
            accepted.Add((eligible[i], value, weight));
        }

        if (accepted.Count == 0)
        {
            accepted.Add((eligible[^1], eligible[^1].FuelUsedLiters, 1));
            rejected = eligible.Length - 1;
        }

        double weightSum = accepted.Sum(x => x.Weight);
        double weightedMean = accepted.Sum(x => x.Value * x.Weight) / weightSum;
        double squaredWeightSum = accepted.Sum(x => x.Weight * x.Weight);
        double effectiveN = weightSum * weightSum / squaredWeightSum;
        double varianceDenominator = weightSum - squaredWeightSum / weightSum;
        double variance = varianceDenominator > 1e-12
            ? accepted.Sum(x => x.Weight * Math.Pow(x.Value - weightedMean, 2)) / varianceDenominator
            : 0;
        double standardDeviation = Math.Sqrt(Math.Max(0, variance));
        double cv = weightedMean > 0 ? standardDeviation / weightedMean : 0;

        double? conservative = null;
        if (accepted.Count >= 2 && effectiveN > 1.0001)
        {
            double t = OneSidedTCritical(options.OneSidedConfidence, Math.Max(1, (int)Math.Floor(effectiveN) - 1));
            conservative = Math.Max(weightedMean, weightedMean + t * standardDeviation * Math.Sqrt(1 + 1 / effectiveN));
        }

        double? trend = EstimateTheilSenTrend(accepted.Select(x => x.Sample).ToArray());
        EstimateReliability reliability = DetermineReliability(accepted.Count, effectiveN, cv, trend, weightedMean);
        string explanation = BuildExplanation(eligible.Length, accepted.Count, rejected, effectiveN, cv, trend, conservative);

        return new(weightedMean, accepted.Count >= 2 ? standardDeviation : null, conservative,
            eligible.Length, accepted.Count, rejected, effectiveN, cv, trend, reliability, explanation);
    }

    private static void ValidateOptions(FuelEstimatorOptions options)
    {
        if (options.WindowSize < 1) throw new ArgumentOutOfRangeException(nameof(options.WindowSize));
        if (!double.IsFinite(options.WeightHalfLifeLaps) || options.WeightHalfLifeLaps <= 0) throw new ArgumentOutOfRangeException(nameof(options.WeightHalfLifeLaps));
        if (!double.IsFinite(options.MinimumRobustScaleLiters) || options.MinimumRobustScaleLiters <= 0) throw new ArgumentOutOfRangeException(nameof(options.MinimumRobustScaleLiters));
        if (!double.IsFinite(options.OutlierThreshold) || options.OutlierThreshold < 1) throw new ArgumentOutOfRangeException(nameof(options.OutlierThreshold));
        if (options.OneSidedConfidence is not (0.90 or 0.95 or 0.99)) throw new ArgumentOutOfRangeException(nameof(options.OneSidedConfidence));
    }

    private static EstimateReliability DetermineReliability(int n, double effectiveN, double cv, double? trend, double mean)
    {
        if (n < 3 || effectiveN < 2) return EstimateReliability.Learning;
        if (n < 5 || effectiveN < 3) return EstimateReliability.Low;
        if (n >= 10 && effectiveN >= 6 && cv <= 0.08 && (trend is null || Math.Abs(trend.Value) <= mean * 0.015)) return EstimateReliability.High;
        return EstimateReliability.Moderate;
    }

    private static string BuildExplanation(int eligible, int used, int rejected, double effectiveN, double cv, double? trend, double? upper)
    {
        var parts = new List<string> { $"Using {used} of {eligible} eligible recent clean-green laps (effective sample size {effectiveN:0.0})." };
        if (rejected > 0) parts.Add($"Rejected {rejected} isolated statistical outlier(s); inspect the lap log if conditions changed.");
        parts.Add($"Observed coefficient of variation: {cv:P1}.");
        if (trend is { } t) parts.Add($"Recent robust trend: {t:+0.000;-0.000;0.000} L/lap per lap index; trend is shown diagnostically and is not extrapolated indefinitely.");
        parts.Add(upper is null
            ? "Not enough independent samples to estimate a statistical prediction bound."
            : "The conservative per-lap figure is a model-based one-sided prediction bound, not a guarantee of future consumption.");
        return string.Join(" ", parts);
    }

    private static double? EstimateTheilSenTrend(IReadOnlyList<FuelLapSample> samples)
    {
        if (samples.Count < 3) return null;
        var slopes = new List<double>();
        for (int i = 0; i < samples.Count; i++)
        for (int j = i + 1; j < samples.Count; j++)
        {
            int deltaSample = j - i;
            slopes.Add((samples[j].FuelUsedLiters - samples[i].FuelUsedLiters) / deltaSample);
        }
        return slopes.Count == 0 ? null : Median(slopes.ToArray());
    }

    internal static double Median(double[] values)
    {
        if (values.Length == 0) return double.NaN;
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
    }

    private static double OneSidedTCritical(double confidence, int degreesOfFreedom)
    {
        if (confidence == 0.90) return TTable(degreesOfFreedom, 0.90);
        if (confidence == 0.95) return TTable(degreesOfFreedom, 0.95);
        return TTable(degreesOfFreedom, 0.99);
    }

    private static double TTable(int df, double confidence)
    {
        double[] row = confidence switch
        {
            0.90 => new[] { 3.078, 1.886, 1.638, 1.533, 1.476, 1.440, 1.415, 1.397, 1.383, 1.372, 1.325, 1.282 },
            0.95 => new[] { 6.314, 2.920, 2.353, 2.132, 2.015, 1.943, 1.895, 1.860, 1.833, 1.812, 1.725, 1.645 },
            _ => new[] { 31.821, 6.965, 4.541, 3.747, 3.365, 3.143, 2.998, 2.896, 2.821, 2.764, 2.528, 2.326 },
        };
        if (df <= 1) return row[0];
        if (df <= 10) return row[df - 1];
        if (df >= 20) return row[10];
        return row[9] + (row[10] - row[9]) * (df - 10) / 10.0;
    }
}

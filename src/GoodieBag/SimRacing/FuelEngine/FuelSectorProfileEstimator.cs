namespace GoodieBag.SimRacing.FuelEngine;

public static class FuelSectorProfileEstimator
{
    private const double MadToSigma = 1.4826;

    public static FuelSectorProfileEstimate? EstimateRemainingFuel(
        IEnumerable<FuelSectorLapSample> samples,
        double currentLapProgress,
        FuelSectorProfileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (!double.IsFinite(currentLapProgress) || currentLapProgress < 0 || currentLapProgress > 1)
            throw new ArgumentOutOfRangeException(nameof(currentLapProgress));
        options ??= new FuelSectorProfileOptions();
        ValidateOptions(options);
        if (currentLapProgress >= 1) return new(0, 0, 0, 0, 0, 0, 0, EstimateReliability.High, "At the finish-line boundary; no current-lap fuel remains.");

        var valid = samples.Where(IsValidCleanLap).TakeLast(options.WindowSize).ToArray();
        if (valid.Length == 0) return null;
        int boundaryCount = valid[0].FuelAtBoundariesLiters.Count;
        if (valid.Any(s => s.FuelAtBoundariesLiters.Count != boundaryCount))
            valid = valid.Where(s => s.FuelAtBoundariesLiters.Count == boundaryCount).ToArray();
        if (valid.Length == 0 || boundaryCount < 3) return null;

        int sectors = boundaryCount - 1;
        int currentSector = Math.Min(sectors - 1, (int)Math.Floor(currentLapProgress * sectors));
        double sectorPosition = currentLapProgress * sectors - currentSector;
        double[] coefficients = new double[sectors];
        coefficients[currentSector] = 1 - sectorPosition;
        for (int i = currentSector + 1; i < sectors; i++) coefficients[i] = 1;

        double totalMean = 0, totalVariance = 0;
        int minimumSamples = int.MaxValue;
        var sectorDetails = new List<string>(sectors);
        for (int sector = currentSector; sector < sectors; sector++)
        {
            double coefficient = coefficients[sector];
            if (coefficient <= 0) continue;
            var burns = valid.Select(s => s.FuelAtBoundariesLiters[sector] - s.FuelAtBoundariesLiters[sector + 1]).ToArray();
            var estimate = EstimateSector(burns, options);
            if (estimate is null || estimate.Value.Count < options.MinimumSamplesPerSector) return null;
            minimumSamples = Math.Min(minimumSamples, estimate.Value.Count);
            totalMean += coefficient * estimate.Value.Mean;
            totalVariance += coefficient * coefficient * estimate.Value.Variance * (1 + 1 / estimate.Value.EffectiveN);
            sectorDetails.Add($"S{sector + 1}: {coefficient:P0} of sector, {estimate.Value.Mean:0.000} L mean ({estimate.Value.Count} samples)");
        }

        double standardDeviation = Math.Sqrt(Math.Max(0, totalVariance));
        double multiplier = options.SafetyMode switch
        {
            SafetyMode.Aggressive => 1.28,
            SafetyMode.Balanced => 1.645,
            SafetyMode.Conservative => 2.326,
            _ => throw new ArgumentOutOfRangeException(nameof(options.SafetyMode)),
        };
        double upper = totalMean + multiplier * standardDeviation;
        var reliability = minimumSamples >= 10 && standardDeviation <= Math.Max(0.05, totalMean * 0.08)
            ? EstimateReliability.High
            : minimumSamples >= 5 ? EstimateReliability.Moderate : EstimateReliability.Low;
        string explanation = $"Estimated current-lap remainder from {valid.Length} clean lap profiles across {sectors} sectors. "
            + $"Expected {totalMean:0.00} L; conservative model bound {upper:0.00} L ({options.SafetyMode.ToString().ToLowerInvariant()}). "
            + "Sector errors are treated as independent for the variance estimate; this is approximate and should be calibrated against replayed telemetry. "
            + string.Join("; ", sectorDetails);
        return new(totalMean, upper, currentSector, sectors, valid.Length, minimumSamples, standardDeviation, reliability, explanation);
    }

    private static (double Mean, double Variance, double EffectiveN, int Count)? EstimateSector(double[] values, FuelSectorProfileOptions options)
    {
        if (values.Length == 0 || values.Any(v => !double.IsFinite(v) || v < -0.02)) return null;
        values = values.Select(v => Math.Max(0, v)).ToArray();
        double median = Median(values);
        double mad = Median(values.Select(v => Math.Abs(v - median)).ToArray());
        double threshold = options.OutlierThreshold * Math.Max(options.MinimumRobustScaleLiters, mad * MadToSigma);
        var accepted = new List<(double Value, double Weight)>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values.Length >= 5 && Math.Abs(values[i] - median) > threshold) continue;
            int age = values.Length - 1 - i;
            accepted.Add((values[i], Math.Exp(-Math.Log(2) * age / options.WeightHalfLifeLaps)));
        }
        if (accepted.Count < options.MinimumSamplesPerSector) return null;
        double sumW = accepted.Sum(x => x.Weight);
        double mean = accepted.Sum(x => x.Value * x.Weight) / sumW;
        double sumW2 = accepted.Sum(x => x.Weight * x.Weight);
        double nEff = sumW * sumW / sumW2;
        double denom = sumW - sumW2 / sumW;
        double variance = denom > 1e-12 ? accepted.Sum(x => x.Weight * Math.Pow(x.Value - mean, 2)) / denom : 0;
        return (mean, variance, nEff, accepted.Count);
    }

    private static bool IsValidCleanLap(FuelSectorLapSample sample)
    {
        if (sample is null || sample.Flags != FuelLapFlags.None || sample.FuelAtBoundariesLiters is null || sample.FuelAtBoundariesLiters.Count < 3)
            return false;
        var levels = sample.FuelAtBoundariesLiters;
        for (int i = 0; i < levels.Count; i++)
        {
            if (!double.IsFinite(levels[i]) || levels[i] < 0) return false;
            if (i > 0 && levels[i] > levels[i - 1] + 0.02) return false;
        }
        return levels[0] - levels[^1] > 0.01;
    }

    private static void ValidateOptions(FuelSectorProfileOptions options)
    {
        if (options.WindowSize < 1) throw new ArgumentOutOfRangeException(nameof(options.WindowSize));
        if (options.MinimumSamplesPerSector < 2) throw new ArgumentOutOfRangeException(nameof(options.MinimumSamplesPerSector));
        if (!double.IsFinite(options.WeightHalfLifeLaps) || options.WeightHalfLifeLaps <= 0) throw new ArgumentOutOfRangeException(nameof(options.WeightHalfLifeLaps));
        if (!double.IsFinite(options.MinimumRobustScaleLiters) || options.MinimumRobustScaleLiters <= 0) throw new ArgumentOutOfRangeException(nameof(options.MinimumRobustScaleLiters));
        if (!double.IsFinite(options.OutlierThreshold) || options.OutlierThreshold < 1) throw new ArgumentOutOfRangeException(nameof(options.OutlierThreshold));
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        int mid = values.Length / 2;
        return values.Length % 2 == 0 ? (values[mid - 1] + values[mid]) / 2 : values[mid];
    }
}

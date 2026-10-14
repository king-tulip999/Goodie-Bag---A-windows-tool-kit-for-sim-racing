using System.IO;
using System.Text;
using GoodieBag.SimRacing.FuelEngine;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.Services;

internal static class FuelReplay
{
    public const string Arg = "--fuel-replay";

    public static void Run(App app, string folder, string report)
    {
        var text = new StringBuilder();
        try
        {
            Replay(folder, text);
        }
        catch (Exception ex)
        {
            text.AppendLine("error " + ex);
        }
        try { File.WriteAllText(report, text.ToString()); }
        catch { }
        app.Shutdown();
    }

    internal sealed class Totals
    {
        public readonly List<double> LapErrors = new(), LapPctErrors = new(), Stint5 = new(), Stint10 = new(), HalfProfile = new(), HalfShare = new();
        public int LapUnder, LapBoundScored, LapWithinBound, Stint5Short, Stint5ShortWithReserve, Stint10Short, Stint10ShortWithReserve;
        public int Files, Laps, CleanLaps;
    }

    internal static void Replay(string folder, StringBuilder text)
    {
        var files = Directory.GetFiles(folder, "*.ibt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        text.AppendLine($"# fuel replay of {files.Count} file(s) in {folder}");
        text.AppendLine("# file | laps recorded | clean | unit | next-lap MAE | stints scored");
        var all = new Totals();
        foreach (var path in files)
        {
            using var ibt = IbtFile.Open(path, out var error);
            if (ibt is null) { text.AppendLine($"skip {Path.GetFileName(path)}: {error}"); continue; }
            var session = SessionInfo.Parse(ibt.SessionInfoYaml);
            var decoder = new FrameDecoder();
            decoder.Bind(ibt.Vars);
            var frame = new TelemetryFrame();
            var model = new FuelModel();
            string identity = $"{session.TrackId}|{session.TrackConfig}|{session.Player?.CarPath}";
            foreach (var record in ibt.Records())
            {
                decoder.Decode(record, frame);
                frame.Status = SimStatus.Connected;
                frame.Session = session;
                model.Update(frame, identity, 10);
            }
            string unit = ibt.Vars.TryGetValue("FuelLevel", out var v) ? v.Unit : "?";
            var score = new Totals();
            Score(model, score);
            all.Files++;
            all.Laps += model.Laps.Count;
            all.CleanLaps += model.Laps.Count(l => l.IsEligible);
            Merge(score, all);
            text.AppendLine($"{Path.GetFileName(path)} | {model.Laps.Count} | {model.Laps.Count(l => l.IsEligible)} | {unit} | "
                + (score.LapErrors.Count > 0 ? $"{score.LapErrors.Average(Math.Abs):0.000} L over {score.LapErrors.Count}" : "–")
                + $" | {score.Stint5.Count} × 5, {score.Stint10.Count} × 10" + (model.EnergyUnits ? " | kWh car: not modelled" : ""));
        }

        text.AppendLine();
        text.AppendLine($"files {all.Files}, laps recorded {all.Laps}, clean laps {all.CleanLaps}");
        if (all.LapErrors.Count == 0) { text.AppendLine("nothing to score (no file had two or more clean laps)"); return; }
        text.AppendLine($"next_lap_scored {all.LapErrors.Count}");
        text.AppendLine($"next_lap_mae_l {all.LapErrors.Average(Math.Abs):0.0000}");
        text.AppendLine($"next_lap_bias_l {all.LapErrors.Average():+0.0000;-0.0000}   (actual − estimate; positive = the lap used more)");
        text.AppendLine($"next_lap_mape_pct {all.LapPctErrors.Average(Math.Abs):0.00}");
        text.AppendLine($"next_lap_used_more_than_estimate_pct {100.0 * all.LapUnder / all.LapErrors.Count:0.0}");
        if (all.LapBoundScored > 0)
            text.AppendLine($"next_lap_within_conservative_bound_pct {100.0 * all.LapWithinBound / all.LapBoundScored:0.0}   (model target 95; over {all.LapBoundScored} laps)");
        Stint(text, "stint5", all.Stint5, all.Stint5Short, all.Stint5ShortWithReserve);
        Stint(text, "stint10", all.Stint10, all.Stint10Short, all.Stint10ShortWithReserve);
        if (all.HalfProfile.Count > 0)
        {
            text.AppendLine($"half_lap_scored {all.HalfProfile.Count}");
            text.AppendLine($"half_lap_profile_mae_l {all.HalfProfile.Average(Math.Abs):0.0000}   bias {all.HalfProfile.Average():+0.0000;-0.0000}");
            text.AppendLine($"half_lap_share_mae_l {all.HalfShare.Average(Math.Abs):0.0000}   bias {all.HalfShare.Average():+0.0000;-0.0000}   (half a lap × the average rate)");
        }
    }

    private static void Stint(StringBuilder text, string name, List<double> errors, int shortExpected, int shortReserve)
    {
        if (errors.Count == 0) return;
        text.AppendLine($"{name}_scored {errors.Count}");
        text.AppendLine($"{name}_mae_l {errors.Average(Math.Abs):0.000}   bias {errors.Average():+0.000;-0.000}");
        text.AppendLine($"{name}_expected_short_pct {100.0 * shortExpected / errors.Count:0.0}   (used more than expected)");
        text.AppendLine($"{name}_with_balanced_reserve_short_pct {100.0 * shortReserve / errors.Count:0.0}   (used more than expected + reserve)");
    }

    private static void Merge(Totals from, Totals into)
    {
        into.LapErrors.AddRange(from.LapErrors); into.LapPctErrors.AddRange(from.LapPctErrors);
        into.Stint5.AddRange(from.Stint5); into.Stint10.AddRange(from.Stint10);
        into.HalfProfile.AddRange(from.HalfProfile); into.HalfShare.AddRange(from.HalfShare);
        into.LapUnder += from.LapUnder; into.LapBoundScored += from.LapBoundScored; into.LapWithinBound += from.LapWithinBound;
        into.Stint5Short += from.Stint5Short; into.Stint5ShortWithReserve += from.Stint5ShortWithReserve;
        into.Stint10Short += from.Stint10Short; into.Stint10ShortWithReserve += from.Stint10ShortWithReserve;
    }

    internal static void Score(FuelModel model, Totals t)
    {
        var laps = model.Laps;
        var options = new FuelEstimatorOptions { WindowSize = 10 };
        for (int k = 1; k < laps.Count; k++)
        {
            if (!laps[k].IsEligible) continue;
            var before = FuelConsumptionEstimator.Estimate(laps.Take(k), options);
            if (!before.HasEstimate) continue;
            double actual = laps[k].FuelUsedLiters, error = actual - before.MeanLitersPerLap;
            t.LapErrors.Add(error);
            t.LapPctErrors.Add(100 * error / before.MeanLitersPerLap);
            if (error > 0) t.LapUnder++;
            if (before.ConservativeLitersPerLap is { } bound)
            {
                t.LapBoundScored++;
                if (actual <= bound) t.LapWithinBound++;
            }
            foreach (int n in new[] { 5, 10 })
            {
                if (k + n > laps.Count || !laps.Skip(k).Take(n).All(l => l.IsEligible)) continue;
                double used = laps.Skip(k).Take(n).Sum(l => l.FuelUsedLiters);
                var plan = FuelStrategyCalculator.Calculate(new FuelStrategyRequest { CurrentFuelLiters = 1000, LapsRemaining = n, Consumption = before, SafetyMode = SafetyMode.Balanced });
                double stintError = used - plan.ExpectedFuelToFinishLiters;
                bool shortExpected = stintError > 0, shortReserve = used > plan.ExpectedFuelToFinishLiters + plan.StatisticalUncertaintyReserveLiters;
                if (n == 5) { t.Stint5.Add(stintError); if (shortExpected) t.Stint5Short++; if (shortReserve) t.Stint5ShortWithReserve++; }
                else { t.Stint10.Add(stintError); if (shortExpected) t.Stint10Short++; if (shortReserve) t.Stint10ShortWithReserve++; }
            }
        }
        var sectorLaps = model.SectorLaps;
        for (int k = 1; k < sectorLaps.Count; k++)
        {
            var lap = sectorLaps[k];
            if (lap.Flags != FuelLapFlags.None || lap.FuelAtBoundariesLiters.Count != FuelModel.Sectors + 1) continue;
            var earlier = sectorLaps.Take(k).ToList();
            var profile = FuelSectorProfileEstimator.EstimateRemainingFuel(earlier, 0.5);
            var clean = laps.Where(l => l.IsEligible && l.CompletedAt < lap.CompletedAt).ToList();
            if (profile is null || clean.Count == 0) continue;
            double actualHalf = lap.FuelAtBoundariesLiters[FuelModel.Sectors / 2] - lap.FuelAtBoundariesLiters[^1];
            double rate = FuelConsumptionEstimator.Estimate(clean, options).MeanLitersPerLap;
            t.HalfProfile.Add(actualHalf - profile.ExpectedFuelRemainingLiters);
            t.HalfShare.Add(actualHalf - 0.5 * rate);
        }
    }
}

namespace GoodieBag.SimRacing.FuelEngine;

public static class RaceDistanceEstimator
{
    public static TimedRaceEstimate EstimateTimedRace(TimedRaceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateTimedInput(input);
        if (input.CheckeredFlagObserved)
            return new(0, 0, 0, 0, true, "iRacing's checkered flag is authoritative: race finished.");

        if (input.WhiteFlagObserved)
        {
            double last = input.PlayerLapProgress >= 1 ? 0 : 1 - input.PlayerLapProgress;
            return new(last, last, last, 0, true,
                "White flag observed from iRacing: the player is on the final lap and finishes at the next line crossing.");
        }

        double leaderToLine = (1 - input.LeaderLapProgress) * input.LeaderLapTimeSeconds;
        double afterFirstCrossing = input.SessionTimeRemainingSeconds - leaderToLine;
        int moreLeaderLaps = afterFirstCrossing > 0 ? (int)Math.Ceiling(afterFirstCrossing / input.LeaderLapTimeSeconds - 1e-9) : 0;
        double checkeredIn = leaderToLine + moreLeaderLaps * input.LeaderLapTimeSeconds;
        double whiteFlagIn = Math.Max(0, checkeredIn - input.LeaderLapTimeSeconds);

        double playerProgressAtCheckered = input.PlayerLapProgress + checkeredIn / input.PlayerLapTimeSeconds;
        double finishBoundary = Math.Max(Math.Ceiling(playerProgressAtCheckered - 1e-9), input.PlayerLapProgress > 0 ? 1 : 0);
        double lapsRemaining = Math.Max(0, finishBoundary - input.PlayerLapProgress);
        return new(lapsRemaining, Math.Max(0, lapsRemaining - 1), lapsRemaining + 1, whiteFlagIn, false,
            $"Forecast only: at the leader's recent pace the checkered flag falls in about {checkeredIn:0} s (white flag in about {whiteFlagIn:0} s); "
            + "the player finishes at their first crossing after it, at their own recent pace. Replaced by iRacing's flags as soon as they show.");
    }

    public static double LapLimitedLapsRemaining(int targetCompletedLaps, int playerCompletedLaps, double playerLapProgress)
    {
        if (targetCompletedLaps < 0) throw new ArgumentOutOfRangeException(nameof(targetCompletedLaps));
        if (playerCompletedLaps < 0) throw new ArgumentOutOfRangeException(nameof(playerCompletedLaps));
        ValidateProgress(playerLapProgress, nameof(playerLapProgress));
        return Math.Max(0, targetCompletedLaps - playerCompletedLaps - playerLapProgress);
    }

    public static double LapsUntilFinishAfterSeconds(double secondsUntilLeaderFinish, double playerLapProgress, double playerLapTimeSeconds)
    {
        if (!double.IsFinite(secondsUntilLeaderFinish) || secondsUntilLeaderFinish < 0) throw new ArgumentOutOfRangeException(nameof(secondsUntilLeaderFinish));
        ValidateProgress(playerLapProgress, nameof(playerLapProgress));
        if (!double.IsFinite(playerLapTimeSeconds) || playerLapTimeSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(playerLapTimeSeconds));
        double progressAtFinish = playerLapProgress + secondsUntilLeaderFinish / playerLapTimeSeconds;
        return Math.Max(0, Math.Floor(progressAtFinish + 1e-10) + 1 - playerLapProgress);
    }

    private static void ValidateTimedInput(TimedRaceInput input)
    {
        if (!double.IsFinite(input.SessionTimeRemainingSeconds)) throw new ArgumentOutOfRangeException(nameof(input.SessionTimeRemainingSeconds));
        ValidateProgress(input.LeaderLapProgress, nameof(input.LeaderLapProgress));
        ValidateProgress(input.PlayerLapProgress, nameof(input.PlayerLapProgress));
        if (!double.IsFinite(input.LeaderLapTimeSeconds) || input.LeaderLapTimeSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(input.LeaderLapTimeSeconds));
        if (!double.IsFinite(input.PlayerLapTimeSeconds) || input.PlayerLapTimeSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(input.PlayerLapTimeSeconds));
    }

    private static void ValidateProgress(double progress, string name)
    {
        if (!double.IsFinite(progress) || progress < 0 || progress > 1) throw new ArgumentOutOfRangeException(name, "Lap progress must be in [0, 1].");
    }
}

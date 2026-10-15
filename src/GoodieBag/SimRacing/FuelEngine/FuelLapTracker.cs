namespace GoodieBag.SimRacing.FuelEngine;

public sealed class FuelLapTracker
{
    public const int MaximumStoredLaps = 300;
    private readonly List<FuelLapSample> _laps = new();
    private FuelTelemetrySample? _previous;
    private string? _sessionKey, _carKey, _trackKey;
    private double _fuelAtStart, _lapMinimumFuel;
    private bool _startedMidLap, _lapTouchedPit, _lapTouchedCaution, _lapFormation, _lapTouchedIncident, _lapRefueled;
    private DateTimeOffset _lastTimestamp;
    private readonly TimeSpan _maximumFrameGap;
    private readonly double _refuelThresholdLiters;

    public FuelLapTracker(TimeSpan? maximumFrameGap = null, double refuelThresholdLiters = 0.2)
    {
        _maximumFrameGap = maximumFrameGap ?? TimeSpan.FromSeconds(3);
        if (_maximumFrameGap <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumFrameGap));
        if (!double.IsFinite(refuelThresholdLiters) || refuelThresholdLiters <= 0) throw new ArgumentOutOfRangeException(nameof(refuelThresholdLiters));
        _refuelThresholdLiters = refuelThresholdLiters;
    }

    public IReadOnlyList<FuelLapSample> Laps => _laps;

    public void Reset(bool clearHistory = true)
    {
        _previous = null;
        _sessionKey = _carKey = _trackKey = null;
        _fuelAtStart = _lapMinimumFuel = 0;
        _startedMidLap = _lapTouchedPit = _lapTouchedCaution = _lapFormation = _lapTouchedIncident = _lapRefueled = false;
        _lastTimestamp = default;
        if (clearHistory) _laps.Clear();
    }

    public FuelTrackerUpdate Update(FuelTelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ValidateSample(sample);
        IReadOnlyList<FuelLapSample> created = Array.Empty<FuelLapSample>();
        bool reset = false, refueled = false;

        bool identityChanged = _sessionKey is not null &&
            (!StringComparer.Ordinal.Equals(_sessionKey, sample.SessionKey)
             || !StringComparer.Ordinal.Equals(_carKey, sample.CarKey)
             || !StringComparer.Ordinal.Equals(_trackKey, sample.TrackKey));
        if (identityChanged)
        {
            Reset(clearHistory: true);
            reset = true;
        }
        _sessionKey = sample.SessionKey;
        _carKey = sample.CarKey;
        _trackKey = sample.TrackKey;

        if (_previous is null)
        {
            BeginLap(sample, startedMidLap: sample.LapDistPct > 0.03);
            _previous = sample;
            _lastTimestamp = sample.Timestamp;
            return new(created, reset, false, "Baseline established; waiting for a complete lap.");
        }

        if (sample.Timestamp == _lastTimestamp
            && sample.LapCompleted == _previous.LapCompleted
            && sample.LapDistPct.Equals(_previous.LapDistPct)
            && sample.FuelLevelLiters.Equals(_previous.FuelLevelLiters))
            return new(created, reset, false, "Duplicate telemetry frame ignored.");

        if (sample.Timestamp <= _lastTimestamp || sample.Timestamp - _lastTimestamp > _maximumFrameGap)
        {
            BeginLap(sample, startedMidLap: true);
            _previous = sample;
            _lastTimestamp = sample.Timestamp;
            return new(created, reset, false, "Telemetry timestamp discontinuity or stale gap; current lap discarded and baseline reset.");
        }

        double elapsedSeconds = (sample.Timestamp - _lastTimestamp).TotalSeconds;
        double observedFuelDrop = _previous.FuelLevelLiters - sample.FuelLevelLiters;
        double plausibleFuelDrop = Math.Max(1.0, elapsedSeconds * 0.25);
        if (observedFuelDrop > plausibleFuelDrop)
        {
            BeginLap(sample, startedMidLap: true);
            _previous = sample;
            _lastTimestamp = sample.Timestamp;
            return new(created, reset, false, "Abrupt fuel-level drop detected; likely telemetry/session discontinuity, current lap discarded.");
        }

        _lapMinimumFuel = Math.Min(_lapMinimumFuel, sample.FuelLevelLiters);
        if (!_lapRefueled && sample.FuelLevelLiters > _lapMinimumFuel + _refuelThresholdLiters)
        {
            refueled = true;
            _lapRefueled = true;
        }
        if (sample.OnPitRoad || !sample.IsOnTrack) _lapTouchedPit = true;
        if (sample.UnderCaution) _lapTouchedCaution = true;
        if (sample.IsFormationLap) _lapFormation = true;
        if (sample.OffTrackOrIncident) _lapTouchedIncident = true;

        if (sample.LapCompleted < _previous.LapCompleted || sample.LapCompleted > _previous.LapCompleted + 1)
        {
            BeginLap(sample, startedMidLap: true);
            _previous = sample;
            _lastTimestamp = sample.Timestamp;
            return new(created, reset, refueled, "Lap counter reset/jump detected; discontinuous lap discarded.");
        }

        if (sample.LapCompleted == _previous.LapCompleted + 1)
        {
            if (!_startedMidLap)
            {
                double used = _fuelAtStart - sample.FuelLevelLiters;
                var flags = ComposeFlags();
                if (refueled || _lapRefueled) flags |= FuelLapFlags.Refueled;
                if (double.IsFinite(used) && used > 0.01 && used < 100.0)
                {
                    var lapTime = (sample.Timestamp - _lapBoundaryStartTime).TotalSeconds;
                    var lap = new FuelLapSample(sample.LapCompleted, used, lapTime > 0 ? lapTime : null, flags, sample.Timestamp);
                    created = new[] { lap };
                    _laps.Add(lap);
                    if (_laps.Count > MaximumStoredLaps) _laps.RemoveAt(0);
                }
            }
            BeginLap(sample, startedMidLap: false);
        }

        _previous = sample;
        _lastTimestamp = sample.Timestamp;
        return new(created, reset, refueled, created.Count == 0 ? "Telemetry accepted; no new valid lap completed." : "Completed lap fuel measurement recorded.");
    }

    private DateTimeOffset _lapBoundaryStartTime;

    private FuelLapFlags ComposeFlags()
    {
        FuelLapFlags flags = FuelLapFlags.None;
        if (_lapTouchedPit) flags |= FuelLapFlags.PitRoad;
        if (_lapTouchedCaution) flags |= FuelLapFlags.Caution;
        if (_lapFormation) flags |= FuelLapFlags.FormationLap;
        if (_lapTouchedIncident) flags |= FuelLapFlags.OffTrackOrIncident;
        if (_lapRefueled) flags |= FuelLapFlags.Refueled;
        return flags;
    }

    private void BeginLap(FuelTelemetrySample sample, bool startedMidLap)
    {
        _fuelAtStart = _lapMinimumFuel = sample.FuelLevelLiters;
        _startedMidLap = startedMidLap;
        _lapTouchedPit = sample.OnPitRoad || !sample.IsOnTrack;
        _lapTouchedCaution = sample.UnderCaution;
        _lapFormation = sample.IsFormationLap;
        _lapTouchedIncident = sample.OffTrackOrIncident;
        _lapRefueled = false;
        _lapBoundaryStartTime = sample.Timestamp;
    }

    private static void ValidateSample(FuelTelemetrySample sample)
    {
        if (string.IsNullOrWhiteSpace(sample.SessionKey)) throw new ArgumentException("SessionKey is required.", nameof(sample));
        if (string.IsNullOrWhiteSpace(sample.CarKey)) throw new ArgumentException("CarKey is required.", nameof(sample));
        if (string.IsNullOrWhiteSpace(sample.TrackKey)) throw new ArgumentException("TrackKey is required.", nameof(sample));
        if (sample.LapCompleted < 0) throw new ArgumentOutOfRangeException(nameof(sample.LapCompleted));
        if (!double.IsFinite(sample.LapDistPct) || sample.LapDistPct < 0 || sample.LapDistPct > 1) throw new ArgumentOutOfRangeException(nameof(sample.LapDistPct));
        if (!double.IsFinite(sample.FuelLevelLiters) || sample.FuelLevelLiters < 0) throw new ArgumentOutOfRangeException(nameof(sample.FuelLevelLiters));
    }
}

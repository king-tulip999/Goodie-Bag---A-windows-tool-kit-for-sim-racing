using GoodieBag.SimRacing.FuelEngine;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Race;

internal sealed class FuelModel
{
    public const int Sectors = 10;
    private const int MaxSectorLaps = 40;
    private static readonly TimeSpan PairWindow = TimeSpan.FromSeconds(1);

    private readonly FuelLapTracker _tracker = new();
    private readonly FuelSectorSampler _sampler = new(Sectors);
    private readonly List<FuelSectorLapSample> _sectorLaps = new();
    private string _identity = "";
    private double _lastTime = double.NaN;
    private int? _lastIncidents;
    private int _window = -1, _lapsSeen = -1;
    private IReadOnlyDictionary<string, IrsdkVar>? _unitsFrom;
    private (double[] Boundaries, DateTimeOffset At)? _pendingProfile;
    private (FuelLapSample Lap, DateTimeOffset At)? _pendingLap;

    public IReadOnlyList<FuelLapSample> Laps => _tracker.Laps;

    public IReadOnlyList<FuelSectorLapSample> SectorLaps => _sectorLaps;

    public FuelConsumptionEstimate Estimate { get; private set; } = FuelConsumptionEstimator.Estimate(Array.Empty<FuelLapSample>());

    public bool EnergyUnits { get; private set; }

    public void Clear()
    {
        _tracker.Reset();
        _sampler.Reset();
        _sectorLaps.Clear();
        _pendingProfile = null;
        _pendingLap = null;
        _lastTime = double.NaN;
        _lastIncidents = null;
        _lapsSeen = -1;
        Estimate = FuelConsumptionEstimator.Estimate(Array.Empty<FuelLapSample>());
    }

    public void Update(TelemetryFrame f, string carTrack, int window)
    {
        if (!ReferenceEquals(f.Vars, _unitsFrom))
        {
            _unitsFrom = f.Vars;
            EnergyUnits = f.Vars.TryGetValue("FuelLevel", out var v) && v.Unit.Contains("kWh", StringComparison.OrdinalIgnoreCase);
        }
        if (carTrack != _identity)
        {
            Clear();
            _identity = carTrack;
        }
        if (!EnergyUnits && Sample(f) is { } sample)
        {
            if (!double.IsNaN(_lastTime) && (sample.Timestamp - DateTimeOffset.UnixEpoch).TotalSeconds - _lastTime > 3)
                _sampler.Reset();
            _lastTime = (sample.Timestamp - DateTimeOffset.UnixEpoch).TotalSeconds;
            var update = _tracker.Update(sample);
            var boundaries = _sampler.Update(sample.LapCompleted, sample.LapDistPct, sample.FuelLevelLiters);
            if (boundaries is not null) _pendingProfile = (boundaries, sample.Timestamp);
            if (update.NewLaps.Count == 1) _pendingLap = (update.NewLaps[0], sample.Timestamp);
            PairProfileWithLap(sample.Timestamp);
        }
        if (_tracker.Laps.Count != _lapsSeen || window != _window)
        {
            _lapsSeen = _tracker.Laps.Count;
            _window = window;
            Estimate = FuelConsumptionEstimator.Estimate(_tracker.Laps, new FuelEstimatorOptions { WindowSize = Math.Max(1, window) });
        }
    }

    private void PairProfileWithLap(DateTimeOffset now)
    {
        if (_pendingProfile is { } stale && now - stale.At > PairWindow) _pendingProfile = null;
        if (_pendingLap is { } staleLap && now - staleLap.At > PairWindow) _pendingLap = null;
        if (_pendingProfile is not { } profile || _pendingLap is not { } lap) return;
        _sectorLaps.Add(new FuelSectorLapSample(lap.Lap.LapNumber, profile.Boundaries, lap.Lap.Flags, now));
        if (_sectorLaps.Count > MaxSectorLaps) _sectorLaps.RemoveAt(0);
        _pendingProfile = null;
        _pendingLap = null;
    }

    private FuelTelemetrySample? Sample(TelemetryFrame f)
    {
        if (f.IsReplayPlaying || f.SessionTime is not { } time || f.Fuel is not { } fuel || !double.IsFinite(fuel) || fuel < 0
            || f.LapCompleted is not >= 0 || f.LapDistPct is not { } pct || !double.IsFinite(pct) || pct < -0.01 || pct > 1.01)
            return null;
        bool incident = f.Incidents is { } inc && _lastIncidents is { } before && inc > before;
        if (f.Incidents is { } now) _lastIncidents = now;
        bool parade = f.Phase == SessionPhase.ParadeLaps || f.SessionKind == SessionKind.Race && f.Phase < SessionPhase.Racing;
        return new FuelTelemetrySample
        {
            SessionKey = _identity,
            CarKey = _identity,
            TrackKey = _identity,
            Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(time),
            LapCompleted = f.LapCompleted.Value,
            LapDistPct = Math.Clamp(pct, 0, 1),
            FuelLevelLiters = fuel,
            OnPitRoad = f.OnPitRoad,
            UnderCaution = (f.Flags & (RaceFlags.Caution | RaceFlags.CautionWaving)) != 0,
            IsFormationLap = parade,
            OffTrackOrIncident = incident,
            IsOnTrack = f.IsOnTrack,
        };
    }

    public FuelSectorProfileEstimate? CurrentLap(double lapProgress, SafetyMode mode) =>
        _sectorLaps.Count == 0 || !double.IsFinite(lapProgress)
            ? null
            : FuelSectorProfileEstimator.EstimateRemainingFuel(_sectorLaps, Math.Clamp(lapProgress, 0, 1), new FuelSectorProfileOptions { SafetyMode = mode });
}

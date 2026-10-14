namespace GoodieBag.SimRacing.Race;

internal sealed record LapRecord(int Lap, double Time, bool Clean, bool PitLap, bool Caution, int IncidentsGained, double? FuelUsed,
    IReadOnlyList<double?> Sectors);

internal sealed record LapStats(int Count, double Best, double Average, double Median, double Spread, double StdDev);

internal static class LapMath
{
    public static LapStats? Stats(IReadOnlyList<LapRecord> laps, int window)
    {
        var use = laps.Skip(Math.Max(0, laps.Count - Math.Max(1, window))).Where(l => l.Clean && !l.PitLap && !l.Caution).Select(l => l.Time).ToList();
        if (use.Count == 0) return null;
        double median = Median(use);
        use = use.Where(t => t <= median * 1.07).ToList();
        if (use.Count == 0) return null;
        double avg = use.Average();
        double sd = Math.Sqrt(use.Sum(t => (t - avg) * (t - avg)) / use.Count);
        return new LapStats(use.Count, use.Min(), avg, Median(use), use.Max() - use.Min(), sd);
    }

    public static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n == 0 ? 0 : n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
    }

    public static double?[] BestSectors(IReadOnlyList<LapRecord> laps, int sectors)
    {
        var best = new double?[sectors];
        foreach (var lap in laps.Where(l => l.Clean && !l.PitLap))
            for (int i = 0; i < Math.Min(sectors, lap.Sectors.Count); i++)
                if (lap.Sectors[i] is { } t && (best[i] is null || t < best[i])) best[i] = t;
        return best;
    }

    public static double? Theoretical(double?[] bestSectors) =>
        bestSectors.Length > 0 && bestSectors.All(s => s is not null) ? bestSectors.Sum(s => s!.Value) : null;

    public static int SectorAt(IReadOnlyList<double> starts, double pct)
    {
        int s = 0;
        for (int i = 0; i < starts.Count; i++)
            if (pct >= starts[i]) s = i;
        return s;
    }
}

internal sealed class LapTracker
{
    public const int MaxLaps = 300;
    private readonly List<LapRecord> _laps = new();
    private IReadOnlyList<double> _sectorStarts = new[] { 0.0 };
    private double?[] _current = new double?[1];
    private int _lap = -1, _sector, _incidentsAtStart, _pendingLap = -1;
    private double _sectorStartTime, _lastLapTime, _prevPct = -1, _prevT;
    private bool _pit, _caution;
    private double? _fuelAtStart, _pendingFuel;
    private double?[] _pendingSectors = Array.Empty<double?>();
    private bool _pendingPit, _pendingCaution;
    private int _pendingIncidents;
    private long _pendingSince;

    public IReadOnlyList<LapRecord> Laps => _laps;

    public IReadOnlyList<double?> CurrentSectors => _current;

    public int CurrentSector => _sector;

    public int SectorCount => _sectorStarts.Count;

    public event Action<LapRecord>? LapCompleted;

    public void Clear()
    {
        _laps.Clear();
        _lap = -1;
        _pendingLap = -1;
    }

    public void SetSectors(IReadOnlyList<double> starts)
    {
        var s = starts.Where(x => x >= 0 && x < 1).Distinct().OrderBy(x => x).ToList();
        if (s.Count == 0 || s[0] > 0.001) s.Insert(0, 0);
        if (s.SequenceEqual(_sectorStarts)) return;
        _sectorStarts = s;
        _current = new double?[s.Count];
        for (int i = 0; i < _laps.Count; i++) _laps[i] = _laps[i] with { Sectors = Array.Empty<double?>() };
    }

    public void Update(double now, int? lapCompleted, double? pct, double? lapTime, double? lastLap, int? incidents, double? fuel, bool onPitRoad, bool caution)
    {
        if (lapCompleted is not { } lap || pct is not { } p || lapTime is not { } t) return;
        if (onPitRoad) _pit = true;
        if (caution) _caution = true;
        if (_lap < 0 || lap < _lap || lap > _lap + 1)
        {
            Begin(lap, incidents ?? 0, fuel);
            _partial = p > 0.03;
            _lastLapTime = lastLap ?? 0;
            _prevPct = p;
            _prevT = t;
            return;
        }
        if (lap == _lap + 1)
        {
            _pendingLap = lap;
            _pendingPartial = _partial;
            _pendingSectors = (double?[])_current.Clone();
            _pendingPit = _pit;
            _pendingCaution = _caution;
            _pendingIncidents = Math.Max(0, (incidents ?? 0) - _incidentsAtStart);
            _pendingFuel = _fuelAtStart is { } f0 && fuel is { } f1 && f0 > f1 ? f0 - f1 : null;
            _pendingSince = (long)(now * 1000);
            Begin(lap, incidents ?? 0, fuel);
        }
        else
        {
            int sector = LapMath.SectorAt(_sectorStarts, p);
            if (sector == _sector + 1)
            {
                double line = _sectorStarts[sector], cross = t;
                if (_prevPct >= 0 && _prevPct < line && p > _prevPct && t >= _prevT)
                    cross = _prevT + (t - _prevT) * (line - _prevPct) / (p - _prevPct);
                _current[_sector] = cross - _sectorStartTime;
                _sectorStartTime = cross;
                _sector = sector;
            }
            else if (sector != _sector && sector != 0) _sector = sector;
        }
        _prevPct = p;
        _prevT = t;
        if (_pendingLap >= 0 && lastLap is { } ll && ll > 0 && (Math.Abs(ll - _lastLapTime) > 1e-6 || now * 1000 - _pendingSince > 3000))
        {
            if (Math.Abs(ll - _lastLapTime) > 1e-6 && !_pendingPartial)
            {
                var sectors = _pendingSectors;
                int last = sectors.Length - 1;
                if (sectors[last] is null && sectors.Take(last).All(s => s is not null))
                {
                    double rest = ll - sectors.Take(last).Sum(s => s!.Value);
                    if (rest > 0) sectors[last] = rest;
                }
                var record = new LapRecord(_pendingLap, ll, _pendingIncidents == 0 && !_pendingPit, _pendingPit, _pendingCaution, _pendingIncidents, _pendingFuel, sectors);
                _laps.Add(record);
                if (_laps.Count > MaxLaps) _laps.RemoveAt(0);
                LapCompleted?.Invoke(record);
            }
            _lastLapTime = ll;
            _pendingLap = -1;
        }
    }

    private bool _partial, _pendingPartial;

    private void Begin(int lap, int incidents, double? fuel)
    {
        _partial = false;
        _lap = lap;
        _sector = 0;
        _sectorStartTime = 0;
        _incidentsAtStart = incidents;
        _fuelAtStart = fuel;
        _pit = false;
        _caution = false;
        _current = new double?[_sectorStarts.Count];
    }
}

namespace GoodieBag.SimRacing.FuelEngine;

public sealed class FuelSectorSampler
{
    private readonly double?[] _boundaries;
    private int _lap = -1;
    private double _previousProgress, _previousFuel;
    private bool _hasPrevious;

    public FuelSectorSampler(int sectorCount = 10)
    {
        if (sectorCount < 2) throw new ArgumentOutOfRangeException(nameof(sectorCount));
        SectorCount = sectorCount;
        _boundaries = new double?[sectorCount + 1];
    }

    public int SectorCount { get; }

    public void Reset()
    {
        _hasPrevious = false;
        _lap = -1;
        Array.Clear(_boundaries);
    }

    public double[]? Update(int lapCompleted, double lapProgress, double fuel)
    {
        if (lapCompleted < 0 || !double.IsFinite(lapProgress) || lapProgress < 0 || lapProgress > 1 || !double.IsFinite(fuel) || fuel < 0)
        {
            Reset();
            return null;
        }
        if (!_hasPrevious)
        {
            Start(lapCompleted, lapProgress, fuel, atLine: lapProgress <= 1e-3 ? fuel : null);
            return null;
        }

        double[]? completed = null;
        if (lapCompleted == _lap)
        {
            if (lapProgress < _previousProgress)
            {
                Start(lapCompleted, lapProgress, fuel, atLine: null);
                return null;
            }
            Fill(_previousProgress, lapProgress, _previousFuel, fuel, lapProgress - _previousProgress);
        }
        else if (lapCompleted == _lap + 1)
        {
            double total = 1 - _previousProgress + lapProgress;
            double atLine = total > 1e-9 ? _previousFuel + (fuel - _previousFuel) * (1 - _previousProgress) / total : fuel;
            Fill(_previousProgress, 1, _previousFuel, atLine, 1 - _previousProgress);
            _boundaries[SectorCount] = atLine;
            if (_boundaries.All(b => b.HasValue)) completed = _boundaries.Select(b => b!.Value).ToArray();
            Start(lapCompleted, lapProgress, fuel, atLine);
            if (lapProgress > 0) Fill(0, lapProgress, atLine, fuel, lapProgress);
            return completed;
        }
        else
        {
            Start(lapCompleted, lapProgress, fuel, atLine: null);
            return null;
        }
        _previousProgress = lapProgress;
        _previousFuel = fuel;
        return completed;
    }

    private void Start(int lap, double progress, double fuel, double? atLine)
    {
        Array.Clear(_boundaries);
        _boundaries[0] = atLine;
        _lap = lap;
        _previousProgress = progress;
        _previousFuel = fuel;
        _hasPrevious = true;
    }

    private void Fill(double from, double to, double fuelFrom, double fuelTo, double span)
    {
        for (int k = 1; k < SectorCount; k++)
        {
            double at = (double)k / SectorCount;
            if (at <= from || at > to) continue;
            double t = span > 1e-12 ? (at - from) / span : 1;
            _boundaries[k] = fuelFrom + (fuelTo - fuelFrom) * t;
        }
    }
}

namespace GoodieBag.Overlay;

internal sealed class RunningAverage
{
    private double _sum;
    private long _count;

    public void Add(float? value)
    {
        if (value is not { } v || !float.IsFinite(v)) return;
        _sum += v;
        _count++;
    }

    public float? Value => _count > 0 ? (float)(_sum / _count) : null;

    public void Reset()
    {
        _sum = 0;
        _count = 0;
    }
}

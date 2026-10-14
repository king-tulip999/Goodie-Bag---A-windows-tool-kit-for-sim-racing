namespace GoodieBag.SimRacing.Telemetry;

internal sealed class TripleBuffer<T> where T : class
{
    private const int Fresh = 4;
    private readonly T[] _items;
    private int _back = 0, _front = 2;
    private int _shared = 1;

    public TripleBuffer(Func<T> create) => _items = new[] { create(), create(), create() };

    public T Back => _items[_back];

    public T Front => _items[_front];

    public void Publish() => _back = Interlocked.Exchange(ref _shared, _back | Fresh) & 3;

    public bool TryTake()
    {
        if ((Volatile.Read(ref _shared) & Fresh) == 0) return false;
        _front = Interlocked.Exchange(ref _shared, _front) & 3;
        return true;
    }
}

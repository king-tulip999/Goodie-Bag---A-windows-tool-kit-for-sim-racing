namespace GoodieBag.Services;

internal sealed class LoopGuard
{
    private readonly Queue<long> _recent = new();
    private readonly int _limit;
    private readonly TimeSpan _window;

    public LoopGuard(int limit, TimeSpan window)
    {
        _limit = limit;
        _window = window;
    }

    public bool Tripped { get; private set; }

    public bool Allow()
    {
        if (Tripped)
            return false;
        long now = Environment.TickCount64;
        while (_recent.Count > 0 && now - _recent.Peek() > _window.TotalMilliseconds)
            _recent.Dequeue();
        if (_recent.Count >= _limit)
        {
            Tripped = true;
            return false;
        }
        _recent.Enqueue(now);
        return true;
    }

    public void Reset()
    {
        Tripped = false;
        _recent.Clear();
    }
}

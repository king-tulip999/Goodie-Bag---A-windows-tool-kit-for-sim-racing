namespace GoodieBag.Services;

internal static class SingleInstance
{
    private const string MutexName = @"Local\GoodieBag.SingleInstance";
    private const string ShowEventName = @"Local\GoodieBag.ShowWindow";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        return true;
    }

    public static void SignalExisting()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(ShowEventName);
            ev.Set();
        }
        catch
        {
        }
    }

    public static void Listen(Action show)
    {
        var showEvent = _showEvent ??= new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (showEvent.WaitOne())
                show();
        })
        { IsBackground = true, Name = "Goodie Bag single instance" };
        thread.Start();
    }

    public static void Release()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
    }
}

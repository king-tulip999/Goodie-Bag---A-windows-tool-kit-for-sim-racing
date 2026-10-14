using System.Runtime.InteropServices;

namespace GoodieBag.Hardware;

internal sealed class DiskActivity : IDisposable
{
    private IntPtr _query, _read, _write, _idle;
    private bool _failed;

    public (float? ReadBps, float? WriteBps, float? ActivePercent) Read()
    {
        if (_failed)
            return (null, null, null);
        try
        {
            if (_query == IntPtr.Zero)
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0
                    || PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", IntPtr.Zero, out _read) != 0
                    || PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out _write) != 0
                    || PdhAddEnglishCounter(_query, @"\PhysicalDisk(_Total)\% Idle Time", IntPtr.Zero, out _idle) != 0)
                {
                    _failed = true;
                    Dispose();
                    return (null, null, null);
                }
                PdhCollectQueryData(_query);
                return (null, null, null);
            }
            if (PdhCollectQueryData(_query) != 0)
                return (null, null, null);
            float? idle = Value(_idle);
            return (Value(_read), Value(_write), idle is { } i ? Math.Clamp(100 - i, 0, 100) : null);
        }
        catch
        {
            _failed = true;
            Dispose();
            return (null, null, null);
        }
    }

    private static float? Value(IntPtr counter) =>
        PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var v) == 0 && v.CStatus == 0 ? (float)v.DoubleValue : null;

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
            PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    private const uint PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000;

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}

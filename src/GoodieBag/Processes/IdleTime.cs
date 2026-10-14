using System.Runtime.InteropServices;

namespace GoodieBag.Processes;

internal static class IdleTime
{
    public static TimeSpan Current
    {
        get
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info))
                return TimeSpan.Zero;
            uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
            return TimeSpan.FromMilliseconds(idleMs);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
}

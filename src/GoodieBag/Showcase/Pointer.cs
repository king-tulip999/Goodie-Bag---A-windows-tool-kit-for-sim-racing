using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace GoodieBag.Showcase;

internal static class Pointer
{
    public static Point Position => GetCursorPos(out var p) ? new Point(p.X, p.Y) : new Point();

    public static double Scale { get; set; } = 1;

    public static Task Glide(Point to, double ms = 0)
    {
        var from = Position;
        double distance = (to - from).Length;
        if (ms <= 0) ms = Math.Clamp(260 + distance / Scale * 0.16, 300, 620);
        var normal = distance > 1 ? new Vector(-(to.Y - from.Y) / distance, (to.X - from.X) / distance) : new Vector();
        double bow = Math.Min(40 * Scale, distance * 0.06);
        return Task.Factory.StartNew(() =>
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / ms);
                double e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
                var at = from + (to - from) * e + normal * (bow * Math.Sin(Math.PI * e));
                SetCursorPos((int)Math.Round(at.X), (int)Math.Round(at.Y));
                if (t >= 1) return;
                Thread.Sleep(4);
            }
        }, TaskCreationOptions.LongRunning);
    }

    public static async Task Click()
    {
        Send(MOUSEEVENTF_LEFTDOWN);
        await Task.Delay(70);
        Send(MOUSEEVENTF_LEFTUP);
        await Task.Delay(30);
    }

    public static async Task Drag(Point to, double ms)
    {
        var from = Position;
        Send(MOUSEEVENTF_LEFTDOWN);
        await Task.Delay(120);
        var way = to - from;
        if (way.Length > 1) way.Normalize();
        await Glide(from + way * 10 * Scale, 120);
        await Glide(to, ms);
        await Task.Delay(160);
        Send(MOUSEEVENTF_LEFTUP);
        await Task.Delay(40);
    }

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;

    private static void Send(uint flags)
    {
        var input = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = flags } };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}

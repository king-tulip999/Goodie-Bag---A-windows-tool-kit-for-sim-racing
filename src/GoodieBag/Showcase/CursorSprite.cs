using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using GoodieBag.Overlay;

namespace GoodieBag.Showcase;

internal sealed class CursorSprite : IDisposable
{
    private const double FadeMs = 350;
    private readonly Thread _thread;
    private readonly float _scale;
    private volatile bool _stop, _visible = true;

    public CursorSprite(float scale)
    {
        _scale = scale;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Showcase cursor", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public bool Visible { set => _visible = value; }

    private void Loop()
    {
        int margin = (int)Math.Ceiling(2 * _scale);
        var size = new Size((int)Math.Ceiling(13 * _scale) + 2 * margin, (int)Math.Ceiling(21 * _scale) + 2 * margin);
        using var window = new OverlayWindow();
        var g = window.Surface(size);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(margin, margin);
        g.ScaleTransform(_scale, _scale);
        using (var arrow = new GraphicsPath())
        {
            arrow.AddPolygon(new PointF[] { new(0, 0), new(0, 16.5f), new(4.1f, 12.8f), new(6.9f, 19.2f), new(9.4f, 18.1f), new(6.7f, 11.8f), new(12, 11.8f) });
            using var fill = new SolidBrush(Color.White);
            using var edge = new Pen(Color.Black, 1.15f) { LineJoin = LineJoin.Round };
            g.FillPath(fill, arrow);
            g.DrawPath(edge, arrow);
        }
        window.Show();
        var clock = Stopwatch.StartNew();
        double alpha = 1, last = 0;
        var shown = new Point(int.MinValue, 0);
        byte shownAlpha = 0;
        while (!_stop)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            alpha = Math.Clamp(alpha + (now - last) / FadeMs * (_visible ? 1 : -1), 0, 1);
            last = now;
            GetCursorPos(out var p);
            var at = new Point(p.X - margin, p.Y - margin);
            byte a = (byte)Math.Round(255 * alpha);
            if (at != shown || a != shownAlpha)
            {
                window.Present(at, a);
                (shown, shownAlpha) = (at, a);
            }
            SetWindowPos(window.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x200);
            System.Windows.Forms.Application.DoEvents();
            DwmFlush();
        }
        window.Hide();
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(1000);
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}

using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;

namespace GoodieBag.Overlay;

internal class OverlayWindow : Form
{
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GWL_EXSTYLE = -20;

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Goodie Bag overlay";
    }

    private bool _clickThrough = true;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            if (_clickThrough == value) return;
            _clickThrough = value;
            if (!IsHandleCreated) return;
            long style = GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64();
            style = value ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
            SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(style));
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            if (_clickThrough) cp.ExStyle |= WS_EX_TRANSPARENT;
            return cp;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        const int WM_DPICHANGED = 0x02E0;
        if (m.Msg == WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }
        base.WndProc(ref m);
    }

    private IntPtr _memDc, _dib, _oldObject, _bits;
    private readonly PresentGuard _guard = new();
    private Bitmap? _surface;
    private Graphics? _graphics;
    private Size _surfaceSize;

    public Graphics Surface(Size size)
    {
        if (_graphics is not null && size == _surfaceSize)
            return _graphics;
        ReleaseSurface();
        IntPtr screenDc = GetDC(IntPtr.Zero);
        try
        {
            var info = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = size.Width,
                biHeight = -size.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            _memDc = CreateCompatibleDC(screenDc);
            _dib = CreateDIBSection(screenDc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (_memDc == IntPtr.Zero || _dib == IntPtr.Zero || bits == IntPtr.Zero)
                throw new OutOfMemoryException("The overlay's drawing surface couldn't be created.");
            _oldObject = SelectObject(_memDc, _dib);
            _bits = bits;
            _surface = new Bitmap(size.Width, size.Height, size.Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits);
            _graphics = Graphics.FromImage(_surface);
            _surfaceSize = size;
            return _graphics;
        }
        catch
        {
            ReleaseSurface();
            throw;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void ReleaseSurface()
    {
        _graphics?.Dispose();
        _graphics = null;
        _surface?.Dispose();
        _surface = null;
        if (_memDc != IntPtr.Zero && _oldObject != IntPtr.Zero)
            SelectObject(_memDc, _oldObject);
        if (_dib != IntPtr.Zero)
            DeleteObject(_dib);
        if (_memDc != IntPtr.Zero)
            DeleteDC(_memDc);
        (_memDc, _dib, _oldObject, _bits) = (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        _guard.Release();
        _surfaceSize = Size.Empty;
    }

    public void Present(Point location, byte opacity = 255)
    {
        if (_graphics is null)
            return;
        _graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
        if (_guard.Same(_bits, _surfaceSize.Width * _surfaceSize.Height * 4, location, opacity)) return;
        IntPtr screenDc = GetDC(IntPtr.Zero);
        try
        {
            var size = new SIZE { cx = _surfaceSize.Width, cy = _surfaceSize.Height };
            var source = new POINT();
            var destination = new POINT { x = location.X, y = location.Y };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = opacity, AlphaFormat = 1 };
            if (!UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, _memDc, ref source, 0, ref blend, 2)) _guard.Reset();
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Repaint() => _guard.Reset();

    protected override void OnHandleCreated(EventArgs e)
    {
        _guard.Reset();
        base.OnHandleCreated(e);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        _guard.Reset();
        base.OnVisibleChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ReleaseSurface();
        base.Dispose(disposing);
    }

    public void KeepOnTop()
    {
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}

using System.Runtime.InteropServices;
using Microsoft.Win32;
using GoodieBag.Config;
using GoodieBag.Hardware;
using GoodieBag.Network;
using System.Drawing;
using System.Windows.Forms;

namespace GoodieBag.Overlay;

internal sealed class OverlayController : IDisposable
{
    private readonly OverlaySettings _settings;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly OverlayRenderer _renderer = new();
    private readonly OverlayRenderer _previewRenderer = new() { Hits = new() };
    private readonly SynchronizationContext _ui;
    private readonly HardwareService _hardware;
    private readonly FrameRateService _frames;
    private readonly NetworkMonitor _network;
    private readonly Func<IReadOnlyList<(string Name, float? Rpm)>> _fans;
    private bool _holdingNetwork;
    private OverlayWindow? _window;
    private Func<System.Drawing.Size, Graphics>? _surface;
    private bool _fpsErrorReported, _drawErrorLogged;
    private int _ticks;

    public event Action<string, string>? Problem;

    public OverlayController(OverlaySettings settings, HardwareService hardware, FrameRateService frames, NetworkMonitor network,
        Func<IReadOnlyList<(string Name, float? Rpm)>> fans)
    {
        _settings = settings;
        _hardware = hardware;
        _frames = frames;
        _network = network;
        _fans = fans;
        _ui = SynchronizationContext.Current ?? new System.Windows.Forms.WindowsFormsSynchronizationContext();
        _timer.Tick += (_, _) => Tick();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Refresh(); }
    }
    private bool _enabled;

    public bool DemoMode { get; init; }

    private bool _running;

    public bool Showing => _running;

    public void SetVisible(bool visible)
    {
        _settings.Visible = visible;
        Refresh();
    }

    private void Refresh()
    {
        bool show = _enabled && _settings.Visible && (!DemoMode || Showcase.ShowcaseRunner.Active);
        if (show == _running)
            return;
        _running = show;
        if (show)
        {
            if (_window is null)
            {
                var window = new OverlayWindow();
                _window = window;
                _surface = window.Surface;
            }
            _hardware.OverlayIntervalMs = Math.Clamp(_settings.UpdateIntervalMs, 100, 5000);
            _hardware.OverlayActive = true;
            _hardware.Acquire("Overlay");
            _frames.Acquire("Overlay");
            _hardware.Nudge();
            _window.Show();
            ApplySettings();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _window?.Hide();
            _window?.ReleaseSurface();
            _hardware.OverlayActive = false;
            _hardware.Release("Overlay");
            _frames.Release("Overlay");
            HoldNetwork(false);
        }
    }

    private void HoldNetwork(bool hold)
    {
        if (hold == _holdingNetwork)
            return;
        _holdingNetwork = hold;
        if (hold) _network.Acquire("Overlay");
        else _network.Release("Overlay");
    }

    public void ApplySettings()
    {
        _renderer.InvalidateColors();
        _previewRenderer.InvalidateColors();
        _timer.Interval = Math.Clamp(_settings.UpdateIntervalMs, 100, 5000);
        _hardware.OverlayIntervalMs = _timer.Interval;
        if (_running)
        {
            var layout = _settings.Current;
            HoldNetwork(layout.Shows(OverlayMetric.Ping) || layout.Shows(OverlayMetric.Network));
            Tick();
        }
    }

    public static IEnumerable<(string Id, string Label)> AvailableMonitors()
    {
        yield return ("primary", "Primary monitor");
        int n = 1;
        foreach (var screen in Screen.AllScreens.OrderBy(s => s.Bounds.X))
        {
            string where = screen.Primary ? ", primary" : "";
            yield return (screen.DeviceName, $"Monitor {n++} ({screen.Bounds.Width}×{screen.Bounds.Height}{where})");
        }
    }

    public static (double W, double H) MonitorSize(OverlayLayout layout)
    {
        try
        {
            var screen = TargetScreen(layout);
            float scale = DpiScale(screen);
            return (screen.Bounds.Width / scale, screen.Bounds.Height / scale);
        }
        catch { return (1920, 1080); }
    }

    internal static (double Fx, double Fy) FractionOf(OverlayLayout layout, double monitorW, double monitorH, double width, double height)
    {
        double freeW = Math.Max(0, monitorW - width), freeH = Math.Max(0, monitorH - height);
        double fx = freeW > 0 ? Math.Clamp(layout.OffsetX / freeW, 0, 1) : 0, fy = freeH > 0 ? Math.Clamp(layout.OffsetY / freeH, 0, 1) : 0;
        bool right = layout.Corner is OverlayCorner.TopRight or OverlayCorner.BottomRight;
        bool bottom = layout.Corner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight;
        return (right ? 1 - fx : fx, bottom ? 1 - fy : fy);
    }

    public IReadOnlyList<OverlayHit> PreviewHits => _previewRenderer.Hits!;

    public Bitmap RenderPreview(OverlayLayout layout, FrameStats? frames, double[] frameTimes)
    {
        string? fpsNote = _frames.Error is not null && frames is null ? "needs admin" : null;
        return _previewRenderer.Render(layout, new OverlayData(_hardware.Latest, frames, fpsNote, _fans(), _network.Latest, frameTimes), 1f);
    }

    private void Tick()
    {
        if (_window is null || !_running)
            return;

        try
        {
            var layout = _settings.Current;
            string? fpsNote = null;
            FrameStats? stats = null;
            int pid = ForegroundProcessId();
            if (_frames.Error is { } error)
            {
                fpsNote = "unavailable";
                if (!_fpsErrorReported)
                {
                    _fpsErrorReported = true;
                    Problem?.Invoke("FPS counter unavailable", error);
                }
            }
            else
            {
                stats = _frames.GetStats(pid);
            }
            if (stats is null && DemoMode && Showcase.ShowcaseRunner.Active) stats = Showcase.ShowcaseRunner.SampleFrames();

            var screen = TargetScreen(layout);
            float scale = DpiScale(screen);
            var times = layout.ShowGraph && stats is not null ? _frames.GetFrameTimes(pid, 120) : Array.Empty<double>();
            var data = new OverlayData(_hardware.Latest, stats, fpsNote, layout.Shows(OverlayMetric.Fans) ? _fans() : Array.Empty<(string, float?)>(), _network.Latest, times);

            var size = _renderer.Render(layout, data, scale, _surface!);
            _window.Present(Position(layout, screen, size, scale), (byte)Math.Round(layout.Opacity * 2.55));

            if (++_ticks % 4 == 0)
                _window.KeepOnTop();
        }
        catch (Exception ex)
        {
            if (!_drawErrorLogged) { _drawErrorLogged = true; App.LogError(ex); }
        }
    }

    private static Screen TargetScreen(OverlayLayout layout) =>
        layout.Monitor == "primary"
            ? Screen.PrimaryScreen ?? Screen.AllScreens[0]
            : Screen.AllScreens.FirstOrDefault(s => s.DeviceName == layout.Monitor) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];

    private static Point Position(OverlayLayout layout, Screen screen, Size size, float scale)
    {
        var b = screen.Bounds;
        int dx = (int)(layout.OffsetX * scale), dy = (int)(layout.OffsetY * scale);
        return layout.Corner switch
        {
            OverlayCorner.TopRight => new Point(b.Right - size.Width - dx, b.Top + dy),
            OverlayCorner.BottomLeft => new Point(b.Left + dx, b.Bottom - size.Height - dy),
            OverlayCorner.BottomRight => new Point(b.Right - size.Width - dx, b.Bottom - size.Height - dy),
            _ => new Point(b.Left + dx, b.Top + dy),
        };
    }

    public void InvalidateColors()
    {
        _renderer.InvalidateColors();
        _previewRenderer.InvalidateColors();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _ui.Post(_ => { if (_running) Tick(); }, null);

    private static float DpiScale(Screen screen)
    {
        try
        {
            var center = new POINT { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 };
            IntPtr monitor = MonitorFromPoint(center, 2);
            if (GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0)
                return dpi / 96f;
        }
        catch { }
        return 1f;
    }

    private static int ForegroundProcessId()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return -1;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return (int)pid;
    }

    public void Dispose()
    {
        Enabled = false;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _timer.Dispose();
        _window?.Dispose();
        _renderer.Dispose();
        _previewRenderer.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}

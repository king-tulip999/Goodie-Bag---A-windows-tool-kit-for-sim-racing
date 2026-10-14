using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using GoodieBag.Overlay;

namespace GoodieBag.SimRacing.Hud;

internal sealed class HudWindow : OverlayWindow
{
    private enum Drag { None, Move, Right, Bottom, Corner }

    private Drag _drag;
    private Point _start;
    private RectangleF _startRect;

    public HudWindow(string title) => Text = title;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RectangleF Rect { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Editing { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Locked { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Grip { get; set; } = 14;

    public event Action<HudWindow, RectangleF, bool>? Dragging;

    public event Action<HudWindow>? Dropped;

    public event Action<HudWindow>? Picked;

    public event Action<HudWindow, PointF>? Clicked;

    private Drag ZoneAt(Point p)
    {
        if (Locked) return Drag.None;
        float w = Rect.Width, h = Rect.Height, g = Grip;
        bool right = p.X >= w - g * 0.5f, bottom = p.Y >= h - g * 0.5f;
        if (p.X >= w - g && p.Y >= h - g) return Drag.Corner;
        if (right) return Drag.Right;
        if (bottom) return Drag.Bottom;
        return Drag.Move;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (!Editing)
        {
            Clicked?.Invoke(this, new PointF(e.X, e.Y));
            return;
        }
        Picked?.Invoke(this);
        _drag = ZoneAt(e.Location);
        if (_drag == Drag.None) return;
        _start = Control.MousePosition;
        _startRect = Rect;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Editing) { Cursor = Cursors.Hand; return; }
        if (_drag == Drag.None)
        {
            Cursor = Locked ? Cursors.No : ZoneAt(e.Location) switch
            {
                Drag.Corner => Cursors.SizeNWSE, Drag.Right => Cursors.SizeWE, Drag.Bottom => Cursors.SizeNS, _ => Cursors.Default,
            };
            return;
        }
        var now = Control.MousePosition;
        float dx = now.X - _start.X, dy = now.Y - _start.Y;
        var r = _startRect;
        var proposed = _drag switch
        {
            Drag.Move => new RectangleF(r.X + dx, r.Y + dy, r.Width, r.Height),
            Drag.Right => new RectangleF(r.X, r.Y, r.Width + dx, r.Height),
            Drag.Bottom => new RectangleF(r.X, r.Y, r.Width, r.Height + dy),
            _ => new RectangleF(r.X, r.Y, r.Width + dx, r.Height + dy),
        };
        Dragging?.Invoke(this, proposed, _drag != Drag.Move);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag == Drag.None) return;
        _drag = Drag.None;
        Capture = false;
        Dropped?.Invoke(this);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (_drag != Drag.None && !Capture)
        {
            _drag = Drag.None;
            Dropped?.Invoke(this);
        }
    }

    public static bool Free => (Control.ModifierKeys & Keys.Alt) != 0;
}

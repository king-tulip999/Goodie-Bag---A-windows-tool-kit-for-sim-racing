using System.Drawing;

namespace GoodieBag.SimRacing.Hud;

internal sealed class HudToolbar
{
    private static readonly (string Id, string Label)[] Buttons = { ("undo", "Undo"), ("redo", "Redo"), ("snap", "Snap"), ("cancel", "Cancel"), ("done", "Done") };
    private const float Height = 40, Pad = 6, Gap = 6, Title = 150;

    private readonly HudWindow _window = new("Goodie Bag · iRacing Overlay editing") { ClickThrough = false };
    private readonly HudPainter _painter = new();
    private readonly List<(string Id, RectangleF Box)> _boxes = new();
    private float _scale = 1;

    public HudToolbar() => _window.Clicked += (_, at) => Click(new PointF(at.X / _scale, at.Y / _scale));

    public event Action<string>? Command;

    private bool _closed;

    public void Draw(HudHost host)
    {
        if (_closed) return;
        var monitor = HudGeometry.MonitorFor("primary", host.Monitors);
        _scale = monitor.Scale;
        _boxes.Clear();
        float x = Pad + Title;
        foreach (var (id, label) in Buttons)
        {
            float w = Math.Max(64, _painter.Measure(label, _painter.Bold) + 26);
            _boxes.Add((id, new RectangleF(x, Pad, w, Height - Pad * 2)));
            x += w + Gap;
        }
        float width = x - Gap + Pad;
        var rect = new RectangleF(monitor.Bounds.Left + (monitor.Bounds.Width - width * _scale) / 2, monitor.Bounds.Top + 24 * _scale, width * _scale, Height * _scale);
        _window.Rect = rect;
        var g = _window.Surface(new Size((int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height)));
        var look = host.Settings.Appearance;
        var p = _painter;
        p.Begin(g, _scale, new SizeF(width, Height), HudPalette.Of(look), look);
        p.Fill(p.Bounds, HudPalette.Alpha(p.P.Surface, 245), 8);
        p.Outline(new RectangleF(0.5f, 0.5f, width - 1, Height - 1), p.P.Accent, 1.5f, 8);
        p.Text("Arranging the iRacing Overlay", p.SmallBold, p.P.AccentText, new RectangleF(Pad + 6, 0, Title - 10, Height), HAlign.Left, VAlign.Center);
        foreach (var (id, box) in _boxes)
        {
            bool enabled = id switch { "undo" => host.CanUndo, "redo" => host.CanRedo, _ => true };
            bool primary = id == "done";
            bool on = id == "snap" && host.Settings.Snap;
            var fill = primary ? p.P.Accent : on ? p.P.AccentSoft : p.P.SurfaceAlt;
            p.Fill(box, fill, 5);
            p.Outline(box, primary ? p.P.Accent : p.P.LineStrong, 1, 5);
            string label = id == "snap" ? (host.Settings.Snap ? "Snap on" : "Snap off") : Buttons.First(b => b.Id == id).Label;
            var color = primary ? p.P.OnAccent : enabled ? p.P.Text : p.P.Faint;
            p.Text(label, p.Bold, color, box, HAlign.Center, VAlign.Center);
        }
        if (!_window.Visible) _window.Show();
        _window.Present(new Point((int)rect.X, (int)rect.Y));
        _window.KeepOnTop();
    }

    private void Click(PointF at)
    {
        foreach (var (id, box) in _boxes)
            if (box.Contains(at)) { Command?.Invoke(id); return; }
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _window.Hide();
        _window.Dispose();
        _painter.Dispose();
    }
}

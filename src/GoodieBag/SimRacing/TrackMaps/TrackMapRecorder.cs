using System.Drawing;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.TrackMaps;

internal sealed class TrackMapRecorder
{
    private readonly List<(double Pct, double X, double Y)> _points = new();
    private double _x, _y, _lastTime = double.NaN, _lastPct = double.NaN;
    private bool _recording;

    public double Progress => _recording && _points.Count > 0 ? _points[^1].Pct : 0;

    public bool Recording => _recording;

    public List<PointF>? Feed(TelemetryFrame f)
    {
        if (!f.Driving || f.LapDistPct is not { } pct || f.VelocityX is not { } vx || f.VelocityY is not { } vy || f.Yaw is not { } yaw || f.SessionTime is not { } now)
        {
            Abort();
            return null;
        }
        bool clean = !f.OnPitRoad && f.PlayerSurface is null or TrackSurface.OnTrack;
        double dt = double.IsNaN(_lastTime) ? 0 : now - _lastTime;
        bool crossed = !double.IsNaN(_lastPct) && _lastPct > 0.9 && pct < 0.1;
        bool jumped = !double.IsNaN(_lastPct) && !crossed && (pct < _lastPct - 0.005 || pct - _lastPct > 0.05 || dt > 1 || dt < 0);
        _lastTime = now;
        _lastPct = pct;
        if (!clean || jumped) { Abort(keepClock: true); return null; }
        if (crossed)
        {
            List<PointF>? lap = _recording && _points.Count > 200 && _points[^1].Pct > 0.97 ? Close() : null;
            Start(pct);
            return lap;
        }
        if (!_recording) return null;
        double c = Math.Cos(yaw), s = Math.Sin(yaw);
        _x += (vx * c - vy * s) * dt;
        _y += (vx * s + vy * c) * dt;
        if (pct > _points[^1].Pct) _points.Add((pct, _x, _y));
        return null;
    }

    private void Start(double pct)
    {
        _recording = true;
        _x = _y = 0;
        _points.Clear();
        _points.Add((pct, 0, 0));
    }

    private void Abort(bool keepClock = false)
    {
        _recording = false;
        _points.Clear();
        if (!keepClock) { _lastTime = double.NaN; _lastPct = double.NaN; }
    }

    private List<PointF>? Close()
    {
        double length = 0;
        var cumulative = new double[_points.Count];
        for (int i = 1; i < _points.Count; i++)
        {
            length += Math.Sqrt(Math.Pow(_points[i].X - _points[i - 1].X, 2) + Math.Pow(_points[i].Y - _points[i - 1].Y, 2));
            cumulative[i] = length;
        }
        double gapX = _points[^1].X - _points[0].X, gapY = _points[^1].Y - _points[0].Y;
        if (length < 300 || Math.Sqrt(gapX * gapX + gapY * gapY) > length * 0.05) return null;
        var line = new List<PointF>(_points.Count);
        for (int i = 0; i < _points.Count; i++)
        {
            double t = cumulative[i] / length;
            line.Add(new PointF((float)(_points[i].X - gapX * t), (float)-(_points[i].Y - gapY * t)));
        }
        return line;
    }
}

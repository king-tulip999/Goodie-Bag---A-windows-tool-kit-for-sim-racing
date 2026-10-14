using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;
using GoodieBag.SimRacing.TrackMaps;

namespace GoodieBag.SimRacing.Hud.Widgets;

internal sealed class TrackMapWidget : HudWidget
{
    public const string SampleKey = "523";

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("labels", "Car labels", "pos", new HudChoice("none", "None"), new HudChoice("pos", "Position"), new HudChoice("cpos", "Class position"), new HudChoice("num", "Car number")) with { Group = HudOptionGroup.Show },
        HudOption.Set("show", "On the map", "pit,sf,sectors", "Optional details",
            new HudChoice("pit", "Pit lane"), new HudChoice("sf", "Start / finish line and direction"), new HudChoice("sectors", "Sector lines"),
            new HudChoice("turns", "Turn numbers", "Where the map has them (iRacing's own maps)"), new HudChoice("pitcars", "Cars in the pit lane", "Dimmed")),
        HudOption.Bool("classColors", "Class colors", true, "Cars in their class color; off = every other car in one color"),
        HudOption.Choice("classes", "Classes", "all", new HudChoice("all", "Every class"), new HudChoice("mine", "My class only")) with { Group = HudOptionGroup.Filter },
        HudOption.Classes("hideClasses", "Hide classes", "Classes left off the map."),
        HudOption.Number("thickness", "Track thickness", 100, 50, 250, 10, "%"),
        HudOption.Number("carSize", "Car size", 100, 60, 200, 10, "%"),
        HudOption.Bool("turn", "Turn to fit", true, "A track longer than it is wide is turned a quarter to fill a wide widget (and the other way round)"),
        HudOption.Bool("follow", "Follow my car", false, "The map centered on you and zoomed in"),
        HudOption.Number("zoom", "Zoom", 300, 125, 800, 25, "%", "How close the map is while following your car (100 % = the whole track)"),
        HudOption.Bool("rotate", "Turn with my car", false, "The map turns so the way you're driving points up"),
        HudOption.Bool("record", "Learn missing maps", true, "With no map for this track: draw one from your next clean lap and keep it for next time"),
    };

    private (int? Id, string Name, bool Sample)? _for;
    private Task<TrackMap?>? _loading;
    private TrackMap? _map;
    private readonly TrackMapRecorder _recorder = new();
    private bool _saving;
    private volatile bool _saved;
    private PointF[] _loop = Array.Empty<PointF>();

    private (TrackMap Map, SizeF Size, bool Turned, float Thickness)? _built;
    private readonly List<(GraphicsPath Path, float Width)> _pit = new();
    private PointF _offset;
    private float _scale = 1;
    private bool _turned;
    private RectangleF _mapBounds;
    private readonly Matrix _viewMatrix = new();
    private Matrix? _view;
    private float _viewScale = 1;
    private readonly PointF[] _one = new PointF[1];

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Track map")) return;
        var f = v.Frame;
        var o = v.Options;
        p.Panel();
        var area = p.Bounds.Inflated(-8 * p.Density, -8 * p.Density);
        Follow(f);
        if (_map is null)
        {
            Missing(p, v, area);
            return;
        }
        var map = _map;
        var show = o.Columns("show");
        bool Has(string id) { for (int i = 0; i < show.Count; i++) if (show[i] == id) return true; return false; }
        float thickness = (float)(o.Number("thickness") / 100);
        bool rotate = o.Bool("rotate");
        Build(map, area, o.Bool("turn") && !rotate, thickness);
        View(v, map, area, o.Bool("follow"), rotate);
        float grow = Math.Min(_viewScale, 2.5f) / _viewScale;
        var state = p.G.Save();
        if (_view is not null)
        {
            p.G.SetClip(area);
            p.G.MultiplyTransform(_view);
        }

        var track = HudPalette.Mix(p.PanelColor, p.P.Text, p.P.Dark ? 0.30 : 0.22);
        var edge = HudPalette.Mix(p.PanelColor, p.P.Text, p.P.Dark ? 0.55 : 0.45);
        if (Has("pit"))
        {
            var pitColor = HudPalette.Mix(p.PanelColor, p.P.Text, p.P.Dark ? 0.2 : 0.16);
            foreach (var (path, width) in _pit)
                if (width <= 0) p.G.FillPath(p.Brush(pitColor), path);
                else p.G.DrawPath(p.Pen(pitColor, width * grow), path);
        }
        float trackWidth = Math.Max(map.Width * _scale, 5 * p.Density) * thickness * grow;
        DrawLoop(p, edge, trackWidth + 2.5f * p.Density / _viewScale, track, trackWidth);

        if (Has("sectors"))
            foreach (var s in f.Session.Sectors)
                if (s.StartPct > 0.001) Tick(p, map, s.StartPct, trackWidth * 1.5f, HudPalette.Alpha(p.P.AccentText, 200), 1.5f);
        if (Has("sf"))
        {
            Tick(p, map, 0, trackWidth * 2.2f, p.P.Heading, Math.Max(2, trackWidth * 0.3f));
            Arrow(p, map, trackWidth, p.P.Heading);
        }
        p.G.Restore(state);
        state = p.G.Save();
        if (_view is not null) p.G.SetClip(area);
        if (Has("turns"))
            foreach (var t in map.Turns)
                p.Text(t.Text, p.Tiny, p.P.Muted, CenteredBox(Screen(Place(t.At)), 28, 14), HAlign.Center, VAlign.Center, false);

        Cars(p, v, map, Has("pitcars"), area);
        p.G.Restore(state);
        if (f.Simulated && _for?.Sample == true && area.Height > 150)
            p.Text("Sample track", p.Tiny, p.P.Faint, new RectangleF(area.X, area.Bottom - p.LineHeight(p.Tiny), area.Width, p.LineHeight(p.Tiny)), HAlign.Right, VAlign.Bottom);
    }

    private void View(HudView v, TrackMap map, RectangleF area, bool follow, bool rotate)
    {
        _view = null;
        _viewScale = 1;
        if (!follow && !rotate) return;
        var f = v.Frame;
        int car = (uint)f.PlayerCarIdx < TelemetryFrame.MaxCars && f.CarLapDistPct[f.PlayerCarIdx] >= 0 ? f.PlayerCarIdx
            : (uint)f.CamCarIdx < TelemetryFrame.MaxCars && f.CarLapDistPct[f.CamCarIdx] >= 0 ? f.CamCarIdx : -1;
        var centre = new PointF(area.X + area.Width / 2, area.Y + area.Height / 2);
        double pct = car >= 0 ? f.CarLapDistPct[car] : -1;
        PointF pivot = centre;
        if (follow && pct >= 0) pivot = Place(map.At(pct));
        else if (!follow)
        {
            var fitted = new SizeF((_turned ? _mapBounds.Height : _mapBounds.Width) * _scale, (_turned ? _mapBounds.Width : _mapBounds.Height) * _scale);
            pivot = new PointF(_offset.X + fitted.Width / 2, _offset.Y + fitted.Height / 2);
        }
        float angle = 0;
        if (rotate && pct >= 0)
        {
            var d = Turn(map.Heading(pct));
            angle = -90 - (float)(Math.Atan2(d.Y, d.X) * 180 / Math.PI);
        }
        float scale;
        if (follow && pct >= 0) scale = (float)(v.Options.Number("zoom") / 100);
        else
        {
            var fitted = new SizeF((_turned ? _mapBounds.Height : _mapBounds.Width) * _scale, (_turned ? _mapBounds.Width : _mapBounds.Height) * _scale);
            scale = rotate ? Math.Min(area.Width, area.Height) / Math.Max(1, (float)Math.Sqrt(fitted.Width * fitted.Width + fitted.Height * fitted.Height)) : 1;
        }
        var m = _viewMatrix;
        m.Reset();
        m.Translate(-pivot.X, -pivot.Y, MatrixOrder.Append);
        if (angle != 0) m.Rotate(angle, MatrixOrder.Append);
        m.Scale(scale, scale, MatrixOrder.Append);
        m.Translate(centre.X, follow && rotate && pct >= 0 ? area.Y + area.Height * 0.62f : centre.Y, MatrixOrder.Append);
        _view = m;
        _viewScale = scale;
    }

    private PointF Screen(PointF placed)
    {
        if (_view is null) return placed;
        _one[0] = placed;
        _view.TransformPoints(_one);
        return _one[0];
    }

    private void Follow(TelemetryFrame f)
    {
        if (_saved) { _saved = false; _saving = false; _for = null; }
        bool sample = f.Simulated && f.Session.TrackId is null or 0;
        var want = (sample ? null : f.Session.TrackId, sample ? "" : f.Session.TrackName, sample);
        if (_for != want)
        {
            _for = want;
            _map = null;
            _built = null;
            _loading = sample ? TrackMapLibrary.ByKey(SampleKey) : TrackMapLibrary.For(want.Item1, want.Item2);
            ((IAsyncResult)_loading).AsyncWaitHandle.WaitOne(250);
        }
        if (_map is null && _loading is { IsCompletedSuccessfully: true } done) _map = done.Result;
    }

    private void Missing(HudPainter p, HudView v, RectangleF area)
    {
        var f = v.Frame;
        if (_loading is { IsCompleted: false })
        {
            p.Text("Loading the map…", p.Body, p.P.Muted, area, HAlign.Center, VAlign.Center);
            return;
        }
        string track = f.Session.TrackDisplayName + (f.Session.TrackConfig.Length > 0 ? " · " + f.Session.TrackConfig : "");
        string status;
        if (!v.Options.Bool("record") || f.Simulated) status = "No map for this track configuration.";
        else
        {
            var line = _recorder.Feed(f);
            if (line is not null && !_saving) Save(f, line);
            status = _saving ? "Saving the map…"
                : _recorder.Recording ? $"No map for this configuration yet: drawing it from this lap ({_recorder.Progress * 100:0} %)."
                : "No map for this configuration yet: drive a clean lap (no pit lane, no off-track) and it's drawn from it.";
        }
        var title = area.Top(p.LineHeight(p.Bold) + 4, out var rest);
        p.Text(track, p.Bold, p.P.Heading, title, HAlign.Center, VAlign.Bottom);
        p.Text(status, p.Small, p.P.Muted, rest.Inflated(-6, 0), HAlign.Center, VAlign.Top, false);
    }

    private void Save(TelemetryFrame f, List<PointF> line)
    {
        _saving = true;
        int? id = f.Session.TrackId is > 0 and var i ? i : null;
        string name = f.Session.TrackName;
        Task.Run(() =>
        {
            try
            {
                var result = TrackMapImport.FromPath(TrackMapLibrary.LearnedKey(id, name), id, name, TrackMapSource.Learned, line, null);
                if (result.Map is { } map) TrackMapLibrary.SaveLearned(map);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.LogError(ex); }
        }).ContinueWith(_ => _saved = true, TaskScheduler.Default);
    }

    private void Build(TrackMap map, RectangleF area, bool allowTurn, float thickness)
    {
        if (_built is { } b && ReferenceEquals(b.Map, map) && b.Size == area.Size && b.Turned == allowTurn && b.Thickness == thickness) return;
        _built = (map, area.Size, allowTurn, thickness);
        _mapBounds = map.Bounds;
        bool mapWide = _mapBounds.Width >= _mapBounds.Height, areaWide = area.Width >= area.Height;
        _turned = allowTurn && mapWide != areaWide && Math.Max(_mapBounds.Width, _mapBounds.Height) > Math.Min(_mapBounds.Width, _mapBounds.Height) * 1.15f;
        float w = _turned ? _mapBounds.Height : _mapBounds.Width, h = _turned ? _mapBounds.Width : _mapBounds.Height;
        _scale = Math.Min(area.Width / Math.Max(1, w), area.Height / Math.Max(1, h));
        _offset = new PointF(area.X + (area.Width - w * _scale) / 2, area.Y + (area.Height - h * _scale) / 2);

        if (_loop.Length != map.Centerline.Length + 1) _loop = new PointF[map.Centerline.Length + 1];
        for (int i = 0; i < map.Centerline.Length; i++) _loop[i] = Place(map.Centerline[i]);
        _loop[^1] = _loop[0];

        foreach (var (path, _) in _pit) path.Dispose();
        _pit.Clear();
        foreach (var shape in map.Pit)
        {
            var path = new GraphicsPath(FillMode.Winding);
            foreach (var fig in shape.Figures)
            {
                var pts = fig.Points.Select(Place).ToArray();
                if (pts.Length < 2) continue;
                if (shape.Filled && fig.Closed && pts.Length >= 3) path.AddPolygon(pts);
                else path.AddLines(pts);
                path.StartFigure();
            }
            _pit.Add((path, shape.Filled ? 0 : Math.Max(1.5f, shape.StrokeWidth * _scale)));
        }
    }

    private PointF Place(PointF m)
    {
        float x = m.X - _mapBounds.X, y = m.Y - _mapBounds.Y;
        if (_turned) (x, y) = (_mapBounds.Height - y, x);
        return new PointF(_offset.X + x * _scale, _offset.Y + y * _scale);
    }

    private PointF Turn(PointF d) => _turned ? new PointF(-d.Y, d.X) : d;

    private void DrawLoop(HudPainter p, Color edge, float edgeWidth, Color fill, float width)
    {
        using var outer = new Pen(edge, Math.Max(2, edgeWidth)) { LineJoin = LineJoin.Round };
        using var inner = new Pen(fill, Math.Max(1.5f, width)) { LineJoin = LineJoin.Round };
        p.G.DrawLines(outer, _loop);
        p.G.DrawLines(inner, _loop);
    }

    private void Tick(HudPainter p, TrackMap map, double pct, float length, Color color, float width)
    {
        var at = Place(map.At(pct));
        var d = Turn(map.Heading(pct));
        var n = new PointF(-d.Y * length / 2, d.X * length / 2);
        p.Line(at.X - n.X, at.Y - n.Y, at.X + n.X, at.Y + n.Y, color, width);
    }

    private void Arrow(HudPainter p, TrackMap map, float trackWidth, Color color)
    {
        double ahead = Math.Min(0.03, 60 / Math.Max(1, map.Length));
        var at = Place(map.At(ahead));
        var d = Turn(map.Heading(ahead));
        var n = new PointF(-d.Y, d.X);
        float s = Math.Max(4, trackWidth * 0.75f);
        var tip = new PointF(at.X + d.X * s, at.Y + d.Y * s);
        var left = new PointF(at.X - d.X * s * 0.5f + n.X * s * 0.7f, at.Y - d.Y * s * 0.5f + n.Y * s * 0.7f);
        var right = new PointF(at.X - d.X * s * 0.5f - n.X * s * 0.7f, at.Y - d.Y * s * 0.5f - n.Y * s * 0.7f);
        p.G.FillPolygon(p.Brush(color), new[] { tip, left, right });
    }

    private static RectangleF CenteredBox(PointF c, float w, float h) => new(c.X - w / 2, c.Y - h / 2, w, h);

    private void Cars(HudPainter p, HudView v, TrackMap map, bool pitCars, RectangleF area)
    {
        var f = v.Frame;
        var o = v.Options;
        string labels = o.Choice("labels");
        bool classColors = o.Bool("classColors");
        float r = Math.Clamp(Math.Min(area.Width, area.Height) / 30, 4.5f, 11) * (float)(o.Number("carSize") / 100) * p.Density;
        int me = f.PlayerCarIdx, selected = v.Race.SelectedCar;
        var font = r >= 6.5f ? p.Font(Math.Clamp(r * 1.1f, 7.5f, 12), true) : null;
        for (int pass = 0; pass < 2; pass++)
            for (int car = 0; car < TelemetryFrame.MaxCars; car++)
            {
                bool player = car == me;
                if (player != (pass == 1)) continue;
                double pct = f.CarLapDistPct[car];
                if (pct < 0 || f.CarSurface[car] == TrackSurface.NotInWorld) continue;
                if (!f.Session.ByCar.TryGetValue(car, out var d) || d.IsSpectator || d.IsPaceCar) continue;
                if (!player && !v.ClassShown(car)) continue;
                bool inPits = f.CarOnPitRoad[car];
                if (inPits && !pitCars && !player) continue;
                var at = Screen(Place(map.At(pct)));
                float radius = player ? r * 1.25f : r;
                var fill = player ? p.P.Accent : classColors ? p.ClassColor(f.Session, d.ClassId) : HudPalette.Mix(p.P.Text, p.PanelColor, 0.15);
                if (inPits) fill = HudPalette.Mix(fill, p.PanelColor, 0.5);
                if (player) p.Dot(at.X, at.Y, radius + 3, HudPalette.Alpha(p.P.Accent, 90));
                p.Dot(at.X, at.Y, radius, fill);
                p.G.DrawEllipse(p.Pen(HudPalette.Alpha(p.PanelColor, 220), 1.2f), at.X - radius, at.Y - radius, radius * 2, radius * 2);
                if (car == selected && !player) p.G.DrawEllipse(p.Pen(p.P.AccentText, 2), at.X - radius - 3, at.Y - radius - 3, radius * 2 + 6, radius * 2 + 6);
                if (font is null || labels == "none") continue;
                string text = labels switch
                {
                    "num" => d.CarNumber,
                    "cpos" => (v.Race.StandingOf(car)?.ClassPosition ?? f.CarClassPosition[car]) is > 0 and var cp ? cp.ToString() : "",
                    _ => f.CarPosition[car] > 0 ? f.CarPosition[car].ToString() : "",
                };
                if (text.Length > 0)
                    p.Text(text, font, player ? p.P.OnAccent : HudPalette.On(fill), CenteredBox(at, radius * 3, radius * 2), HAlign.Center, VAlign.Center, false);
            }
    }
}

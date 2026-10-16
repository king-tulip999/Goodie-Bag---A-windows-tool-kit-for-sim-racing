using System.Drawing;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Hud.Widgets;

internal sealed class TrafficWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Number("window", "Within", 6, 1, 30, 0.5, "s"),
        HudOption.Int("max", "Cars at most", 5, 1, 12),
        HudOption.Bool("sameClass", "Include my class", false),
    };

    private readonly List<(int Car, double Gap)> _cars = new();

    public override bool HasContent(HudView v) => Gather(v) > 0;

    private int Gather(HudView v)
    {
        var f = v.Frame;
        int me = f.PlayerCarIdx;
        _cars.Clear();
        if ((uint)me >= TelemetryFrame.MaxCars) return 0;
        double window = v.Options.Number("window");
        bool same = v.Options.Bool("sameClass");
        int myClass = FieldOrder.ClassOf(f, me);
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            if (i == me || !f.CarActive(i) || f.CarOnPitRoad[i] || !same && FieldOrder.ClassOf(f, i) == myClass) continue;
            if (FieldOrder.GapSeconds(f, me, i, v.Race.Profile) is { } g && Math.Abs(g) <= window) _cars.Add((i, g));
        }
        _cars.Sort((a, b) => Math.Abs(a.Gap).CompareTo(Math.Abs(b.Gap)));
        return _cars.Count;
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Traffic")) return;
        var f = v.Frame;
        int me = f.PlayerCarIdx;
        bool blue = f.Flags.HasFlag(RaceFlags.Blue);
        var area = p.Header("Traffic", blue ? "BLUE FLAG" : null, blue ? HudPalette.FlagBlue : null);
        if ((uint)me >= TelemetryFrame.MaxCars) return;
        bool same = v.Options.Bool("sameClass");
        double? myLap = FieldOrder.LapTimeOf(f, me);
        Gather(v);
        if (_cars.Count == 0)
        {
            p.Text(f.Session.NumCarClasses > 1 || same ? "No traffic nearby" : "Single-class session", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        float rowH = p.LineHeight(p.Body) * 1.4f * p.Density, y = area.Y;
        foreach (var (car, gap) in _cars.Take(v.Options.Int("max")))
        {
            if (y + rowH > area.Bottom + 1) break;
            var d = f.Session.ByCar.GetValueOrDefault(car);
            var row = new RectangleF(area.X, y, area.Width, rowH - 1);
            bool faster = FieldOrder.LapTimeOf(f, car) is { } lap && myLap is { } mine && lap < mine * 0.98;
            double? trend = v.Race.Gaps.Trend(car);
            bool closing = trend is < -0.01;
            if (faster && gap < 0 && closing) p.Fill(row, HudPalette.Alpha(p.P.Warn, 50), 3);
            var cls = row.Left(46, out var rest);
            p.Pill(new RectangleF(cls.X, cls.Y + rowH * 0.16f, cls.Width - 6, rowH * 0.66f), d?.ClassName ?? "", p.ClassColor(f.Session, d?.ClassId ?? 0), p.SmallBold);
            var gapBox = rest.Right(64, out rest);
            p.Text((gap > 0 ? "▲ " : "▼ ") + RaceFormat.Abs(gap), p.Bold, gap > 0 ? p.P.Text : faster ? p.P.WarnText : p.P.Text, gapBox, HAlign.Right);
            var info = rest.Right(80, out rest);
            string meet = closing && v.Race.Gaps.TimeToCatch(car, gap) is { } t && t < 120 ? $"~{t:0} s" : trend is > 0.01 ? "opening" : "";
            p.Text(meet, p.Small, p.P.Muted, info, HAlign.Right);
            p.Text(d?.Name ?? $"Car {car}", p.Body, p.P.Text, rest.Inflated(-4, 0), HAlign.Left);
            y += rowH;
        }
    }
}

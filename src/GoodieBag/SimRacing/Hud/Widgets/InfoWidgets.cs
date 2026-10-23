using System.Drawing;
using GoodieBag.Config;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Hud.Widgets;

internal sealed class SessionWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Columns("fields", "Shows", "remain,lapsleft,lap,pos,cpos",
            new HudChoice("remain", "Time left"), new HudChoice("lapsleft", "Laps left", "Timed races: estimated from the pace"), new HudChoice("elapsed", "Time elapsed"),
            new HudChoice("lap", "Lap"), new HudChoice("pos", "Position"), new HudChoice("cpos", "Position in class"), new HudChoice("phase", "Phase"), new HudChoice("track", "Track"),
            new HudChoice("temps", "Air / track temperature"), new HudChoice("inc", "Incidents"), new HudChoice("clock", "Time of day"),
            new HudChoice("finish", "Estimated finish (time of day)")),
        HudOption.Bool("progress", "Progress bar", true),
    };

    public override void Draw(HudPainter p, HudView v)
    {
        var f = v.Frame;
        if (f.Status != SimStatus.Connected)
        {
            var a = p.Header("Session");
            p.Text(f.Status == SimStatus.Waiting ? "iRacing is running · no session yet" : "Waiting for iRacing", p.Body, p.P.Muted, a, HAlign.Center, VAlign.Center);
            return;
        }
        var session = f.CurrentSession;
        var area = p.Header(session?.Name is { Length: > 0 } n ? n : v.Race.SessionLabel, f.Session.TrackDisplayName);
        int me = f.PlayerCarIdx;
        var cells = new List<(string, string)>();
        var ids = new List<string>();
        foreach (var id in v.Options.Columns("fields"))
        {
            int before = cells.Count;
            switch (id)
            {
                case "remain": cells.Add(("TIME LEFT", Strips.TimeLeft(v) ?? RaceFormat.Missing)); break;
                case "lapsleft": cells.Add(("LAPS LEFT", Strips.LapsLeft(v) ?? RaceFormat.Missing)); break;
                case "elapsed": cells.Add(("ELAPSED", RaceFormat.Clock(f.SessionTime))); break;
                case "lap": cells.Add(("LAP", f.Lap is { } l ? (session?.Laps is > 0 and var total ? $"{l} / {total}" : l.ToString()) : RaceFormat.Missing)); break;
                case "pos": cells.Add(("POSITION", (uint)me < TelemetryFrame.MaxCars && f.CarPosition[me] > 0 ? RaceFormat.Pos(f.CarPosition[me]) : RaceFormat.Missing)); break;
                case "cpos":
                    cells.Add(("In " + (f.Session.Player?.ClassName is { Length: > 0 } cn ? cn : "class"),
                        v.Race.StandingOf(me)?.ClassPosition is > 0 and var cp ? RaceFormat.Pos(cp) : RaceFormat.Missing));
                    break;
                case "phase": cells.Add(("PHASE", RaceControl.SessionPhaseLabel(f.Phase) is { Length: > 0 } ph ? ph : RaceFormat.Missing)); break;
                case "track": cells.Add(("TRACK", f.Session.TrackConfig.Length > 0 ? f.Session.TrackConfig : f.Session.TrackDisplayName)); break;
                case "temps": cells.Add(("AIR / TRACK", $"{RaceFormat.Temp(f.AirTemp, v.Units)} / {RaceFormat.Temp(f.TrackTemp, v.Units)}")); break;
                case "inc": cells.Add(("INCIDENTS", f.Incidents is { } i ? $"{i}x" + (f.Session.IncidentLimit is { } lim ? $" / {lim}x" : "") : RaceFormat.Missing)); break;
                case "clock": cells.Add(("TIME", RaceFormat.TimeOfDay(DateTime.Now))); break;
                case "finish":
                    double? togo = v.Race.Fuel?.LapsToGo ?? (f.SessionKind == SessionKind.Race ? v.Race.LapsToGo(f) : null);
                    double? left = session?.Laps is > 0 ? (togo is { } lt && LapMath.Stats(v.Race.Laps.Laps, 5)?.Average is { } avg ? lt * avg : null) : f.SessionTimeRemain;
                    cells.Add(("FINISH ~", left is { } s ? RaceFormat.TimeOfDay(DateTime.Now.AddSeconds(s)) : RaceFormat.Missing));
                    break;
            }
            if (cells.Count > before) ids.Add(id);
        }
        bool progress = v.Options.Bool("progress");
        float barH = progress ? 6 * p.Density + 6 : 0;
        var body = new RectangleF(area.X, area.Y, area.Width, area.Height - barH);
        if (cells.Count > 0)
        {
            var (heroLabel, heroValue) = cells[0];
            bool wide = body.Width >= 240 * p.Density && cells.Count > 1;
            var heroFont = body.Height >= p.LineHeight(p.Huge) + p.LineHeight(p.Label) + 3 ? p.Huge : p.Big;
            float heroH = Math.Min(body.Height, p.LineHeight(heroFont) + p.LineHeight(p.Label) + 3);
            var heroBox = wide ? body.Left(Math.Min(body.Width * 0.36f, 150 * p.Density), out var rest) : body.Top(heroH, out rest);
            p.ValueStat(new RectangleF(heroBox.X, heroBox.Y + Math.Max(0, (heroBox.Height - heroH) / 2), heroBox.Width, heroH), heroLabel, heroValue, p.P.Heading, heroFont);
            p.Item("fields", ids[0], heroBox);
            if (wide)
            {
                p.Divider(rest.X + 2, rest.Y + 4, rest.Bottom - 4);
                rest = new RectangleF(rest.X + 10 * p.Density, rest.Y, rest.Width - 10 * p.Density, rest.Height);
            }
            const float MinBox = 72;
            float gap = 4 * p.Density, ch = p.BoxedStatHeight() + gap;
            int others = cells.Count - 1;
            int rowsN = Math.Max(0, (int)((rest.Height + gap) / ch));
            int maxCols = Math.Max(1, (int)((rest.Width + gap) / (MinBox * p.Density + gap)));
            int cols = rowsN == 0 ? 1 : Math.Clamp((others + rowsN - 1) / rowsN, 1, maxCols);
            int fits = Math.Min(others, rowsN * cols);
            int rowsUsed = (fits + cols - 1) / cols;
            if (rowsUsed > 0) rest = new RectangleF(rest.X, rest.Y + Math.Max(0, (rest.Height + gap - rowsUsed * ch) / 2), rest.Width, rest.Height);
            float cw = (rest.Width + gap) / cols;
            for (int k = 0; k < others; k++)
            {
                if (k >= fits) { p.Overflow?.Add(("fields", ids[k + 1])); continue; }
                var box = new RectangleF(rest.X + k % cols * cw, rest.Y + k / cols * ch, cw - gap, ch - gap);
                p.BoxedStat(box, cells[k + 1].Item1, cells[k + 1].Item2);
                p.Item("fields", ids[k + 1], box);
            }
        }
        if (progress)
        {
            p.Region("progress", new RectangleF(area.X, area.Bottom - barH, area.Width, barH));
            double? done = session?.Laps is > 0 and var total2 && f.LapCompleted is { } lc ? (lc + (f.LapDistPct ?? 0)) / total2
                : f.SessionTimeTotal is > 0 && f.SessionTimeRemain is { } rem ? 1 - rem / f.SessionTimeTotal.Value : null;
            if (done is { } d) p.Bar(new RectangleF(area.X, area.Bottom - barH + 4, area.Width, barH - 4), d, p.P.Accent, p.Solid(p.Raised(0.10), 130));
        }
    }
}

internal sealed class AlertsWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Int("max", "Alerts at once", 3, 1, 8),
        HudOption.Bool("big", "Large text", false),
        HudOption.Bool("history", "Recent when quiet", false),
    };

    public override bool HasContent(HudView v) =>
        v.Race.Alerts.Visible(v.Now).Count > 0 || v.Options.Bool("history") && v.Race.Alerts.History.Count > 0;

    public override void Draw(HudPainter p, HudView v)
    {
        var list = v.Race.Alerts.Visible(v.Now);
        bool history = list.Count == 0 && v.Options.Bool("history") && v.Race.Alerts.History.Count > 0;
        if (list.Count == 0 && !history) return;
        p.Panel();
        var b = p.Bounds;
        float pad = 6 * p.Density;
        var area = new RectangleF(pad, pad, b.Width - pad * 2, b.Height - pad * 2);
        bool big = v.Options.Bool("big");
        var title = big ? p.Big : p.Bold;
        float rowH = p.LineHeight(title) + p.LineHeight(p.Small) + 10 * p.Density;
        int max = v.Options.Int("max");
        if (history)
        {
            p.Text("RECENT", p.Tiny, p.P.Faint, area.Top(p.LineHeight(p.Tiny) + 2, out area), HAlign.Left);
            var h = v.Race.Alerts.History;
            for (int i = h.Count - 1, n = 0; i >= 0 && n < max; i--, n++)
                Row(p, area.Top(rowH, out area), h[i], title, dim: true);
            return;
        }
        for (int i = 0; i < Math.Min(max, list.Count); i++)
        {
            if (area.Height < rowH * 0.6f) break;
            Row(p, area.Top(rowH, out area), list[i], title, dim: false);
            area = new RectangleF(area.X, area.Y + 4, area.Width, area.Height - 4);
        }
    }

    private static void Row(HudPainter p, RectangleF r, HudAlert a, Font title, bool dim)
    {
        var (edge, text, icon) = a.Level switch
        {
            AlertLevel.Critical => (p.P.Danger, p.P.DangerText, "✖"),
            AlertLevel.Warning => (p.P.Warn, p.P.WarnText, "▲"),
            AlertLevel.Good => (p.P.Good, p.P.GoodText, "✔"),
            _ => (p.P.Accent, p.P.Text, "●"),
        };
        if (dim) text = p.P.Muted;
        p.Soft(r, edge, dim ? 45 : 80);
        var iconBox = new RectangleF(r.X + 8, r.Y, p.LineHeight(title), r.Height);
        p.Text(icon, title, edge, iconBox, HAlign.Center, VAlign.Center, false);
        var body = new RectangleF(iconBox.Right + 6, r.Y + 3, r.Width - iconBox.Width - 18, r.Height - 6);
        bool detail = a.Detail is { Length: > 0 };
        p.Text(a.Title, title, text, detail ? body.Top(p.LineHeight(title) + 1, out _) : body, HAlign.Left, detail ? VAlign.Top : VAlign.Center);
        if (detail) p.Text(a.Detail!, p.Small, dim ? p.P.Faint : p.P.Muted, new RectangleF(body.X, body.Y + p.LineHeight(title) + 1, body.Width, p.LineHeight(p.Small) + 2), HAlign.Left, VAlign.Top);
    }
}

internal sealed class TyresWidget : HudWidget
{
    private static double? Lowest(double? a, double? b, double? c)
    {
        double? low = a;
        if (b is { } y && (low is null || y < low)) low = y;
        if (c is { } z && (low is null || z < low)) low = z;
        return low;
    }

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Bool("pressure", "Pressures", true),
        HudOption.Bool("temps", "Temperatures", true),
        HudOption.Bool("wear", "Wear", true),
        HudOption.Int("low", "Cold below", 70, 20, 120, "°C"),
        HudOption.Int("high", "Hot above", 105, 60, 160, "°C"),
    };

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Tires")) return;
        var f = v.Frame;
        var area = p.Header("Tires", "from the pit box");
        if (f.TyreColdPressure.All(x => x is null) && f.TyreTempMiddle.All(x => x is null) && f.TyreWearMiddle.All(x => x is null))
        {
            p.Text("This car doesn't report tire data", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        string[] names = { "LF", "RF", "LR", "RR" };
        float w = (area.Width - 8) / 2, h = (area.Height - 6) / 2;
        int low = v.Options.Int("low"), high = v.Options.Int("high");
        bool pressure = v.Options.Bool("pressure"), temps = v.Options.Bool("temps"), wearOn = v.Options.Bool("wear");
        float headH = p.LineHeight(p.SmallBold) + 2, tempH = p.LineHeight(p.Small) + 6, wearH = p.LineHeight(p.Label) + 2, between = 5 * p.Density;
        for (int i = 0; i < 4; i++)
        {
            var box = new RectangleF(area.X + i % 2 * (w + 8), area.Y + i / 2 * (h + 6), w, h);
            p.Tile(box);
            var inner = box.Inflated(-6 * p.Density, -4 * p.Density);
            double? wear = Lowest(f.TyreWearInner[i], f.TyreWearMiddle[i], f.TyreWearOuter[i]);
            bool hasWear = wearOn && wear is { } w0 && double.IsFinite(w0);
            bool showTemps = temps && inner.Height >= headH + tempH;
            bool showWear = hasWear && inner.Height >= headH + (showTemps ? tempH + between : 0) + wearH;
            if (!showTemps && !showWear)
            {
                bool stack = pressure && inner.Height >= headH + p.LineHeight(p.Small);
                float total = stack ? headH + p.LineHeight(p.Small) : headH;
                float y0 = inner.Y + (inner.Height - total) / 2;
                if (stack)
                {
                    p.Text(names[i], p.SmallBold, p.P.AccentText, new RectangleF(inner.X, y0, inner.Width, headH), HAlign.Center);
                    p.Text(RaceFormat.Pressure(f.TyreColdPressure[i], v.Units), p.Small, p.P.Text, new RectangleF(inner.X, y0 + headH, inner.Width, p.LineHeight(p.Small)), HAlign.Center);
                }
                else
                {
                    string both = pressure ? $"{names[i]}  {RaceFormat.Pressure(f.TyreColdPressure[i], v.Units)}" : names[i];
                    p.Text(both, p.SmallBold, p.P.AccentText, new RectangleF(inner.X, y0, inner.Width, headH), HAlign.Center);
                }
                continue;
            }
            float used = headH + (showTemps ? tempH : 0) + (showWear ? between + wearH : 0);
            inner = new RectangleF(inner.X, inner.Y + Math.Max(0, (inner.Height - used) / 2), inner.Width, used);
            var head = inner.Top(headH, out inner);
            p.Text(names[i], p.SmallBold, p.P.AccentText, head, HAlign.Left);
            if (pressure) p.Text(RaceFormat.Pressure(f.TyreColdPressure[i], v.Units), p.Small, p.P.Text, head, HAlign.Right);
            if (showTemps)
            {
                double?[] t = i % 2 == 0 ? new[] { f.TyreTempOuter[i], f.TyreTempMiddle[i], f.TyreTempInner[i] } : new[] { f.TyreTempInner[i], f.TyreTempMiddle[i], f.TyreTempOuter[i] };
                var row = inner.Top(tempH, out inner);
                float gap = 3 * p.Density, cw = (row.Width + gap) / 3;
                for (int k = 0; k < 3; k++)
                {
                    var cell = new RectangleF(row.X + k * cw, row.Y + 1, cw - gap, row.Height - 2);
                    var c = t[k] is not { } temp ? HudPalette.Alpha(p.P.LineStrong, 80) : temp < low ? p.P.Slate : temp > high ? p.P.Danger : p.P.Good;
                    p.Chip(cell, c);
                    p.Text(t[k] is { } tt ? RaceFormat.Temp(tt, v.Units).TrimEnd('C', 'F', '°') : RaceFormat.Missing, p.Small, p.P.Heading, cell, HAlign.Center, VAlign.Center);
                }
            }
            if (showWear)
            {
                double wv = wear!.Value;
                var line = new RectangleF(inner.X, inner.Y + between, inner.Width, wearH);
                string pct = $"{wv * 100:0}%";
                float pw = p.Measure("100%", p.Label) + 6 * p.Density;
                p.Bar(new RectangleF(line.X, line.Y + line.Height / 2 - 2.5f, line.Width - pw, 5), wv, wv < 0.5 ? p.P.Danger : wv < 0.75 ? p.P.Warn : p.P.Good);
                p.Text(pct, p.Label, p.LabelColor, new RectangleF(line.Right - pw, line.Y, pw, line.Height), HAlign.Right, VAlign.Center, false);
            }
        }
    }
}

internal sealed class WeatherWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("temp", "Temperatures in", "units", new HudChoice("units", "Overlay units"), new HudChoice("c", "°C"), new HudChoice("f", "°F"), new HudChoice("both", "°C and °F")),
        HudOption.Bool("wind", "Wind", true, "A pointer in a dotted dial showing where the wind blows, the speed under it: red for a headwind, amber across, green behind"),
        HudOption.Bool("turn", "Pointer turns with the car", true, "On: up = straight ahead. Off: up = north."),
    };

    private readonly (string Label, string Value)[] _cells = new (string, string)[3];

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Weather")) return;
        var f = v.Frame;
        var u = v.Units;
        float d = p.Density;
        string temp = v.Options.Choice("temp");
        bool fahrenheit = temp == "f" || temp == "units" && u == HudUnits.Imperial;
        string Temp(double? c) => temp == "both" && c is { } x && double.IsFinite(x)
            ? $"{x:0}° / {x * 9 / 5 + 32:0}°"
            : RaceFormat.Temp(c, fahrenheit ? HudUnits.Imperial : HudUnits.Metric);
        var bounds = p.Bounds;
        float pill = p.PillHeight + 4 * d;
        var body = bounds;
        if (bounds.Height >= pill + p.PieceGap + 44 * d)
        {
            var hr = new RectangleF(bounds.X, bounds.Y, bounds.Width, pill);
            Sky(p, v, hr);
            body = new RectangleF(bounds.X, hr.Bottom + p.PieceGap, bounds.Width, bounds.Height - pill - p.PieceGap);
        }
        p.Panel(body);
        var area = body.Inflated(-10 * d, -6 * d);
        if (v.Options.Bool("wind") && area.Width >= 230 * d) Wind(p, v, area.Right(Math.Min(area.Width * 0.47f, area.Height + 96 * d), out area));
        _cells[0] = ("AIR", Temp(f.AirTemp));
        _cells[1] = ("TRACK", Temp(f.TrackTemp));
        _cells[2] = ("HUMIDITY", f.Humidity is { } h ? $"{h * 100:0}%" : RaceFormat.Missing);
        float cw = area.Width / _cells.Length;
        var font = p.Big;
        foreach (var (_, value) in _cells)
            if (p.Measure(value, font) > cw - 4) { font = p.Bold; break; }
        float vh = p.LineHeight(font), lh = p.LineHeight(p.Label);
        float top = area.Y + (area.Height - vh - lh) / 2;
        for (int i = 0; i < _cells.Length; i++)
        {
            float x = area.X + cw * i;
            p.Text(_cells[i].Value, font, p.P.Heading, new RectangleF(x, top, cw, vh), HAlign.Left, VAlign.Center);
            p.Text(_cells[i].Label, p.Label, p.LabelColor, new RectangleF(x, top + vh, cw, lh), HAlign.Left, VAlign.Top, false);
        }
    }

    private static void Sky(HudPainter p, HudView v, RectangleF r)
    {
        var f = v.Frame;
        float d = p.Density;
        p.Panel(r);
        var inner = r.Inflated(-10 * d, 0);
        float s = inner.Height * 0.7f;
        var icon = new RectangleF(inner.X, inner.Y + (inner.Height - s) / 2, s * 1.2f, s);
        string sky = f.Session.Skies;
        Icon(p, icon, sky, f.Precipitation is > 0);
        bool wet = f.Precipitation is > 0 || f.TrackWetness is > 1 || f.DeclaredWet == true;
        string state = (f.DeclaredWet == true ? "DECLARED WET · " : "") + RaceFormat.Wetness(f.TrackWetness).ToUpperInvariant() + (f.Precipitation is { } rain ? $" · RAIN {rain * 100:0}%" : "");
        string skyText = sky.Length > 0 ? sky : RaceFormat.Missing;
        float skyX = icon.Right + 8 * d;
        float stateW = Math.Min(p.Measure(state, p.Label) + 4, inner.Right - skyX - p.Measure(skyText, p.Bold) - 12 * d);
        if (stateW < 40 * d) stateW = 0;
        else p.Text(state, p.Label, wet ? p.P.WarnText : p.P.Muted, new RectangleF(inner.Right - stateW, inner.Y, stateW, inner.Height), HAlign.Right, VAlign.Center);
        p.Text(skyText, p.Bold, p.P.Heading, new RectangleF(skyX, inner.Y, Math.Max(0, inner.Right - stateW - skyX), inner.Height), HAlign.Left, VAlign.Center);
    }

    private static void Icon(HudPainter p, RectangleF r, string sky, bool raining)
    {
        float s = r.Height;
        bool clear = sky.Contains("clear", StringComparison.OrdinalIgnoreCase);
        bool sun = clear || sky.Contains("partly", StringComparison.OrdinalIgnoreCase);
        if (sun) p.G.FillEllipse(p.Brush(p.P.WarnText), clear ? r.X + s * 0.15f : r.X + s * 0.55f, clear ? r.Y + s * 0.1f : r.Y, clear ? s * 0.8f : s * 0.6f, clear ? s * 0.8f : s * 0.6f);
        if (clear) return;
        var c = raining ? p.P.Muted : p.P.Heading;
        p.G.FillEllipse(p.Brush(c), r.X, r.Y + s * 0.42f, s * 0.55f, s * 0.55f);
        p.G.FillEllipse(p.Brush(c), r.X + s * 0.25f, r.Y + s * 0.22f, s * 0.62f, s * 0.62f);
        p.G.FillEllipse(p.Brush(c), r.X + s * 0.62f, r.Y + s * 0.45f, s * 0.52f, s * 0.52f);
        p.G.FillRectangle(p.Brush(c), r.X + s * 0.27f, r.Y + s * 0.6f, s * 0.62f, s * 0.37f);
        if (!raining) return;
        var pen = p.Pen(p.P.AccentText, Math.Max(1.2f, s * 0.08f));
        for (int k = 0; k < 3; k++)
        {
            float x = r.X + s * (0.3f + k * 0.25f);
            p.G.DrawLine(pen, x, r.Bottom + s * 0.02f, x - s * 0.08f, r.Bottom + s * 0.2f);
        }
    }

    private static void Wind(HudPainter p, HudView v, RectangleF r)
    {
        var f = v.Frame;
        float d = p.Density;
        p.Divider(r.X, r.Y + 4 * d, r.Bottom - 4 * d);
        float size = Math.Min(r.Height + 6 * d, 74 * d);
        var ring = new RectangleF(r.X + 8 * d, r.Y + (r.Height - size) / 2, size, size);
        double? toward = f.WindDir is { } from ? from + Math.PI : null;
        double heading = v.Options.Bool("turn") && f.YawNorth is { } yaw ? yaw : 0;
        WindArrow.Draw(p, ring, f.WindSpeed, toward, heading, "headtail", v.Units, false);
        var text = new RectangleF(ring.Right + 8 * d, r.Y, r.Right - ring.Right - 8 * d, r.Height);
        var (word, color) = (toward is { } t ? WindArrow.Relation(f.WindSpeed, t, heading) : WindRelation.Calm) switch
        {
            WindRelation.Head => ("HEADWIND", p.P.DangerText),
            WindRelation.Cross => ("CROSSWIND", p.P.WarnText),
            WindRelation.Tail => ("TAILWIND", p.P.GoodText),
            _ => ("CALM", p.P.Muted),
        };
        float wh = p.LineHeight(p.SmallBold), lh = p.LineHeight(p.Label);
        float top = text.Y + (text.Height - wh - lh - 2) / 2;
        p.Text(word, p.SmallBold, color, new RectangleF(text.X, top, text.Width, wh), HAlign.Left, VAlign.Center);
        p.Text(RaceFormat.SpeedUnit(v.Units).ToUpperInvariant() + " · " + RaceFormat.Compass(toward), p.Label, p.LabelColor, new RectangleF(text.X, top + wh + 2, text.Width, lh), HAlign.Left, VAlign.Top, false);
    }
}

internal enum WindRelation { Calm, Head, Cross, Tail }

internal static class WindArrow
{
    public const double CalmBelow = 0.5;

    public static WindRelation Relation(double? speed, double towardRad, double headingRad)
    {
        if (speed is not > CalmBelow) return WindRelation.Calm;
        double a = Math.IEEERemainder(towardRad - headingRad, Math.PI * 2);
        double deg = Math.Abs(a) * 180 / Math.PI;
        return deg <= 60 ? WindRelation.Tail : deg >= 120 ? WindRelation.Head : WindRelation.Cross;
    }

    public static Color ColorOf(HudPainter p, WindRelation r, string colors) => colors == "accent"
        ? (r == WindRelation.Calm ? p.P.Muted : p.P.AccentText)
        : r switch { WindRelation.Head => p.P.DangerText, WindRelation.Cross => p.P.WarnText, WindRelation.Tail => p.P.GoodText, _ => p.P.Muted };

    private static readonly PointF[] Pointer = new PointF[4];
    private static readonly (float Along, float Across)[] PointerShape = { (0.55f, 0f), (-0.52f, 0.43f), (-0.29f, 0f), (-0.52f, -0.43f) };
    private static readonly (float X, float Y)[] ManyDots = Circle(36), FewDots = Circle(24);

    private static (float X, float Y)[] Circle(int count) => Enumerable.Range(0, count)
        .Select(i => ((float)Math.Sin(i * Math.PI * 2 / count), (float)-Math.Cos(i * Math.PI * 2 / count))).ToArray();

    public static void Draw(HudPainter p, RectangleF box, double? speed, double? towardRad, double headingRad, string colors, HudUnits units, bool showUnit, string speedUnit = "units")
    {
        var relation = towardRad is { } t ? Relation(speed, t, headingRad) : WindRelation.Calm;
        var color = ColorOf(p, relation, colors);
        double? shown = speed is not { } s ? null : speedUnit switch
        {
            "kmh" => s * 3.6, "mph" => s * 2.23694, "ms" => s,
            _ => RaceFormat.SpeedValue(s, units),
        };
        string unit = speedUnit switch { "kmh" => "km/h", "mph" => "mph", "ms" => "m/s", _ => RaceFormat.SpeedUnit(units) };
        var font = p.Font(MathF.Max(10, Math.Min(box.Width, box.Height) * 0.2f), false);
        string text = shown is { } x ? x.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : RaceFormat.Missing;
        float lh = p.LineHeight(font), uh = showUnit ? p.LineHeight(p.Tiny) : 0;
        float size = Math.Max(8, Math.Min(box.Width, box.Height - lh - uh));
        float top = box.Y + (box.Height - size - lh - uh) / 2;
        float cx = box.X + box.Width / 2, cy = top + size / 2;
        float dot = Math.Max(1f, size * 0.017f), r = size / 2 - dot;
        var dialColor = HudPalette.Alpha(p.P.Muted, 170);
        var dial = p.Brush(dialColor);
        foreach (var (dx, dy) in size < 70 ? FewDots : ManyDots)
            p.G.FillEllipse(dial, cx + dx * r - dot, cy + dy * r - dot, dot * 2, dot * 2);
        var tick = p.Pen(dialColor, Math.Max(1.5f, r * 0.05f));
        p.G.DrawLine(tick, cx, cy - r, cx, cy - r * 0.8f);
        p.G.DrawLine(tick, cx, cy + r, cx, cy + r * 0.8f);
        p.G.DrawLine(tick, cx - r, cy, cx - r * 0.8f, cy);
        p.G.DrawLine(tick, cx + r, cy, cx + r * 0.8f, cy);
        if (towardRad is { } toward)
        {
            double bearing = toward - headingRad;
            float ux = (float)Math.Sin(bearing), uy = (float)-Math.Cos(bearing);
            for (int i = 0; i < Pointer.Length; i++)
            {
                var (along, across) = PointerShape[i];
                Pointer[i] = new PointF(cx + (ux * along - uy * across) * r, cy + (uy * along + ux * across) * r);
            }
            p.G.FillPolygon(p.Brush(color), Pointer);
        }
        var numberBox = new RectangleF(box.X, top + size, box.Width, lh);
        p.Text(text, font, color, numberBox, HAlign.Center, VAlign.Center, false);
        if (showUnit) p.Text(unit.ToUpperInvariant(), p.Tiny, p.LabelColor, new RectangleF(box.X, numberBox.Bottom - 2, box.Width, uh), HAlign.Center, VAlign.Center, false);
    }
}

internal sealed class WindWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Bool("turn", "Turns with the car", true, "On: the pointer shows where the wind blows as you drive (down = a headwind). Off: up = north."),
        HudOption.Choice("colors", "Colors", "headtail", new HudChoice("headtail", "By headwind / crosswind / tailwind", "Red at the nose, amber across, green behind"), new HudChoice("accent", "The accent color")),
        HudOption.Choice("speedUnit", "Speed in", "units", new HudChoice("units", "Overlay units"), new HudChoice("kmh", "km/h"), new HudChoice("mph", "mph"), new HudChoice("ms", "m/s")),
        HudOption.Bool("unit", "Unit label", false),
        HudOption.Bool("title", "Title", false, "“Wind” over the dial"),
    };

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v)) return;
        var f = v.Frame;
        var o = v.Options;
        if (!o.Bool("title")) p.Panel();
        var area = o.Bool("title") ? p.Header("Wind") : p.Bounds.Inflated(-6 * p.Density, -6 * p.Density);
        bool turn = o.Bool("turn");
        double? toward = f.WindDir is { } from ? from + Math.PI : null;
        if (turn && f.YawNorth is null) toward = null;
        WindArrow.Draw(p, area, f.WindSpeed, toward, turn ? f.YawNorth ?? 0 : 0, o.Choice("colors"), v.Units, o.Bool("unit"), o.Choice("speedUnit"));
    }
}

internal sealed class LapsWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Int("count", "Laps listed", 8, 3, 30),
        HudOption.Int("window", "Laps in the statistics", 10, 3, 50),
        HudOption.Bool("fuel", "Fuel used", true),
        HudOption.Bool("stats", "Statistics", true),
    };

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Laps")) return;
        var laps = v.Race.Laps.Laps;
        var stats = LapMath.Stats(laps, v.Options.Int("window"));
        var area = p.Header("Laps", laps.Count == 0 ? "none yet" : $"{laps.Count} this session");
        if (v.Options.Bool("stats") && stats is not null && area.Height > p.StripHeight * 3)
        {
            var foot = new RectangleF(area.X, area.Bottom - p.StripHeight, area.Width, p.StripHeight);
            area = new RectangleF(area.X, area.Y, area.Width, area.Height - foot.Height - 4);
            p.Strip(foot, new (string, string, Color?)[] { ("AVG", RaceFormat.LapTime(stats.Average), null), ("SPREAD", $"{stats.Spread:0.00} s", null), ("σ", $"{stats.StdDev:0.000}", null) });
        }
        if (laps.Count == 0) { p.Text("Laps appear here as you complete them", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center); return; }
        double? best = laps.Where(l => l.Clean).Select(l => (double?)l.Time).Min();
        float bandH = p.LineHeight(p.Bold) + 10 * p.Density;
        if (best is { } top && area.Height > bandH * 3)
        {
            var band = area.Top(bandH, out area);
            area = new RectangleF(area.X, area.Y + 4, area.Width, area.Height - 4);
            p.Soft(band, p.P.Fastest, 90);
            var inner = band.Inflated(-7 * p.Density, 0);
            double? optimal = LapMath.Theoretical(LapMath.BestSectors(laps, v.Race.Laps.SectorCount));
            bool both = optimal is not null && inner.Width > 220 * p.Density;
            void Pair(RectangleF r, string label, double time)
            {
                string l = p.LabelText(label), t = RaceFormat.LapTime(time);
                float lw = p.Measure(l, p.Label) + 6, tw = p.Measure(t, p.Bold);
                float x = r.X + Math.Max(0, (r.Width - lw - tw) / 2);
                p.Text(l, p.Label, p.P.Heading, new RectangleF(x, r.Y, lw, r.Height), HAlign.Left, VAlign.Center, false);
                p.Text(t, p.Bold, p.P.Heading, new RectangleF(x + lw, r.Y, tw + 2, r.Height), HAlign.Left, VAlign.Center, false);
            }
            if (both)
            {
                var left = inner.Left(inner.Width / 2, out var right);
                Pair(left, "Best", top);
                p.Line(right.X, band.Y + 5, right.X, band.Bottom - 5, HudPalette.Alpha(p.P.Heading, 70));
                Pair(right, "Optimal", optimal!.Value);
            }
            else Pair(inner, "Best", top);
        }
        float rowH = p.LineHeight(p.Body) * 1.3f * p.Density;
        bool fuel = v.Options.Bool("fuel");
        float headH = p.LineHeight(p.Tiny) + 4;
        bool headings = area.Height > headH + rowH * 2;
        float numW = 34 * p.Density, timeW = 74 * p.Density, deltaW = 56 * p.Density, fuelW = 52 * p.Density;
        float y = area.Y;
        if (headings)
        {
            var h = new RectangleF(area.X, y, area.Width, headH);
            p.Text("LAP", p.Tiny, p.P.Faint, h.Left(numW, out var hr), HAlign.Center, VAlign.Center);
            p.Text("TIME", p.Tiny, p.P.Faint, hr.Left(timeW, out hr), HAlign.Right, VAlign.Center);
            p.Text("Δ BEST", p.Tiny, p.P.Faint, hr.Left(deltaW, out hr), HAlign.Right, VAlign.Center);
            if (fuel && hr.Width >= fuelW + 12 * p.Density) p.Text("FUEL", p.Tiny, p.P.Faint, hr.Right(fuelW, out _), HAlign.Right, VAlign.Center);
            y += headH;
        }
        int n = Math.Min(v.Options.Int("count"), Math.Max(1, (int)((area.Bottom - y) / rowH)));
        float radius = p.CornerRadius(2);
        for (int i = laps.Count - 1; i >= Math.Max(0, laps.Count - n); i--)
        {
            var lap = laps[i];
            var row = new RectangleF(area.X, y, area.Width, rowH - 2);
            bool isBest = lap.Clean && best is { } bb0 && Math.Abs(lap.Time - bb0) < 0.0005;
            p.Fill(row, p.Solid(isBest ? HudPalette.Mix(p.Raised(0.06), p.P.Fastest, 0.18) : p.Raised(p.P.Dark ? 0.06 : 0.045), 110), radius);
            var num = row.Left(numW, out var rest);
            p.Fill(num, p.Solid(p.Sunken(0.38), 120), radius);
            p.Text(lap.Lap.ToString(), p.SmallBold, p.P.Muted, num, HAlign.Center, VAlign.Center);
            var time = rest.Left(timeW, out rest);
            p.Text(RaceFormat.LapTime(lap.Time), p.Bold, isBest ? p.P.Fastest : lap.Clean ? p.P.Heading : p.P.WarnText, time, HAlign.Right);
            var delta = rest.Left(deltaW, out rest);
            if (best is { } bb && !isBest)
            {
                double off = lap.Time - bb;
                var dc = off <= 0.25 ? p.P.GoodText : off <= 0.75 ? p.P.Text : p.P.WarnText;
                p.Text(RaceFormat.Gap(off, 2), p.Small, dc, delta, HAlign.Right);
            }
            if (fuel && lap.FuelUsed is { } fu && rest.Width >= fuelW + 12 * p.Density) { var fb = rest.Right(fuelW, out rest); p.Text(RaceFormat.Fuel(fu, v.Units), p.Small, p.P.Muted, fb.Inflated(-4, 0), HAlign.Right); }
            var marks = new List<string>(3);
            if (lap.PitLap) marks.Add("PIT");
            if (lap.IncidentsGained > 0) marks.Add($"{lap.IncidentsGained}x");
            if (lap.Caution) marks.Add("YEL");
            if (marks.Count > 0) p.Text(string.Join(" ", marks), p.Tiny, p.P.WarnText, rest.Inflated(-6, 0), HAlign.Left);
            y += rowH;
        }
    }
}

internal sealed class EventsWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[] { HudOption.Int("count", "Events listed", 8, 3, 30) };

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Race events")) return;
        var events = v.Race.Events;
        var area = p.Header("Race events");
        if (events.Count == 0) { p.Text("Nothing yet", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center); return; }
        float rowH = p.LineHeight(p.Small) * 1.55f * p.Density, y = area.Y;
        float timeW = p.Measure("00:00:00", p.Small) + 8 * p.Density;
        for (int i = events.Count - 1, n = 0; i >= 0 && n < v.Options.Int("count"); i--, n++)
        {
            if (y + rowH > area.Bottom + 1) break;
            if (n > 0) p.AccentRule(area.X + 2, area.Right - 2, MathF.Round(y) + 0.5f);
            var e = events[i];
            var row = new RectangleF(area.X, y, area.Width, rowH);
            var time = row.Left(timeW, out var rest);
            p.Text(RaceFormat.Clock(e.SessionTime), p.Small, p.P.Muted, time.Inflated(-2, 0), HAlign.Left);
            var c = e.Level switch { AlertLevel.Critical => p.P.Danger, AlertLevel.Warning => p.P.Warn, AlertLevel.Good => p.P.Good, _ => p.P.Accent };
            p.Dot(rest.X + 4, rest.Y + rowH / 2, 3.5f, c);
            p.Text(e.Text, p.Small, p.P.Text, new RectangleF(rest.X + 13, rest.Y, rest.Width - 13, rowH), HAlign.Left);
            y += rowH;
        }
    }
}

internal sealed class BlockWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        new HudOption("source", "Value", HudOptionKind.Choice, "fuelLaps") { Choices = HudChannels.All.Select(c => new HudChoice(c.Id, c.Label, HudChannels.KindText(c.Kind))).ToArray() },
        HudOption.Int("decimals", "Decimals", -1, -1, 4, help: "−1 = the value's own."),
        HudOption.Choice("visual", "Show as", "number", new HudChoice("number", "Number"), new HudChoice("bar", "Number + bar"), new HudChoice("graph", "Number + graph")),
        HudOption.Number("min", "Bar / graph from", 0, -100000, 100000, 1),
        HudOption.Number("max", "Bar / graph to", 100, -100000, 100000, 1),
        HudOption.Number("warn", "Warning at", 0, -100000, 100000, 0.1, help: "0 with critical 0 = no coloring."),
        HudOption.Number("crit", "Critical at", 0, -100000, 100000, 0.1),
        HudOption.Bool("above", "Warn when above (else below)", true),
    };

    private readonly float[] _history = new float[90];
    private int _head, _count;
    private double _lastSample;
    private PointF[] _points = Array.Empty<PointF>();

    public override void Draw(HudPainter p, HudView v)
    {
        var o = v.Options;
        var channel = HudChannels.Find(o.Choice("source")) ?? HudChannels.All[0];
        p.Panel();
        var b = p.Bounds;
        float pad = 6 * p.Density;
        var area = new RectangleF(pad, pad * 0.6f, b.Width - pad * 2, b.Height - pad * 1.2f);
        string label = channel.Label;
        double? raw = v.Frame.Status == SimStatus.Connected ? channel.Read(v) : null;
        double? value = raw is { } r ? HudChannels.Convert(r, channel.Unit, v.Units) : null;
        string unit = HudChannels.UnitText(channel.Unit, v.Units);
        int decimals = o.Int("decimals") is >= 0 and var d ? d : channel.Decimals;
        string text = value is not { } x ? RaceFormat.Missing
            : channel.Unit == ChannelUnit.LapTime ? (channel.Id is "timeLeft" or "stint" ? RaceFormat.Clock(x) : RaceFormat.LapTime(x))
            : x.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture);
        double warn = o.Number("warn"), crit = o.Number("crit");
        bool above = o.Bool("above");
        bool Past(double t) => t != 0 && value is { } vv && (above ? vv >= t : vv <= t);
        var color = Past(crit) ? p.P.DangerText : Past(warn) ? p.P.WarnText : p.P.Heading;
        string labelText = p.LabelText(p.Shortened(label, p.Label, area.Width));
        var head = area.Top(p.LineHeight(p.Label) + 3, out var body);
        p.Text(labelText, p.Label, p.LabelColor, head, HAlign.Center, VAlign.Center);
        string visual = o.Choice("visual");
        float minV = (float)o.Number("min"), maxV = (float)o.Number("max");
        double frac = value is { } fv && maxV > minV ? (fv - minV) / (maxV - minV) : 0;
        var lower = RectangleF.Empty;
        var main = visual == "number" ? body : body.Top(Math.Max(body.Height * 0.62f, Math.Min(body.Height, p.LineHeight(p.Big))), out lower);
        string shownUnit = unit.Length > 0 && channel.Unit != ChannelUnit.LapTime ? unit : "";
        float unitW = shownUnit.Length > 0 ? p.Measure(shownUnit, p.SmallBold) + 4 : 0;
        var font = p.Fitting(text, main.Width - unitW, main.Height + 4, p.Huge, p.Font(26f, true), p.Big, p.Bold);
        p.Number(text, shownUnit, font, color, main, HAlign.Center, VAlign.Center);
        if (visual == "bar") p.Bar(new RectangleF(lower.X + 4, lower.Y + lower.Height / 2 - 3, lower.Width - 8, 6), frac, Past(crit) ? p.P.Danger : Past(warn) ? p.P.Warn : p.P.Accent);
        if (visual == "graph")
        {
            if (v.Now - _lastSample >= 0.25 && value is { } gv)
            {
                _lastSample = v.Now;
                _history[_head] = (float)gv;
                _head = (_head + 1) % _history.Length;
                _count = Math.Min(_history.Length, _count + 1);
            }
            if (_count > 1)
            {
                if (_points.Length != _count) _points = new PointF[_count];
                for (int i = 0; i < _count; i++)
                {
                    float val = _history[(_head - _count + i + _history.Length) % _history.Length];
                    float t = maxV > minV ? Math.Clamp((val - minV) / (maxV - minV), 0, 1) : 0.5f;
                    _points[i] = new PointF(lower.X + lower.Width * i / (_history.Length - 1), lower.Bottom - t * lower.Height);
                }
                p.G.DrawLines(p.Pen(p.P.Accent, 1.5f), _points);
            }
        }
    }
}

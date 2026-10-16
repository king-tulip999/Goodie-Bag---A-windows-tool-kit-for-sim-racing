using System.Drawing;
using GoodieBag.SimRacing.FuelEngine;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Hud.Widgets;

internal sealed class TimingWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("reference", "Delta to", "best", new HudChoice("best", "My best lap"), new HudChoice("session", "Session best"), new HudChoice("optimal", "My optimal lap", "Best sectors combined")),
        HudOption.Bool("bar", "Delta bar", true),
        HudOption.Number("range", "Bar range", 1.0, 0.2, 5, 0.1, "s"),
        HudOption.Columns("fields", "Times", "current,last,best,predicted",
            new HudChoice("current", "Current lap"), new HudChoice("last", "Last lap"), new HudChoice("best", "My best"), new HudChoice("predicted", "Predicted lap"),
            new HudChoice("sessionBest", "Session best (all)"), new HudChoice("classBest", "Best in my class"), new HudChoice("theoretical", "Optimal lap (best sectors)")),
        HudOption.Bool("sectors", "Sectors", true),
        HudOption.Choice("split", "Sector split", "track", new HudChoice("track", "The track's sectors"), new HudChoice("6", "6 equal sectors"),
            new HudChoice("8", "8 equal sectors"), new HudChoice("10", "10 equal sectors"), new HudChoice("12", "12 equal sectors"))
            with { Help = "Equal sectors time every widget's sectors and the optimal lap from then on (the most any Lap timing widget asks for)" },
    };

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Lap timing")) return;
        var f = v.Frame;
        p.Panel();
        var b = p.Bounds;
        float pad = 8 * p.Density;
        var area = new RectangleF(pad, pad * 0.7f, b.Width - pad * 2, b.Height - pad * 1.4f);
        string reference = v.Options.Choice("reference");
        var (delta, ok, refLap) = reference switch
        {
            "session" => (f.DeltaSessionBest, f.DeltaSessionBestOk, FieldOrder.FastestLap(f)?.Time),
            "optimal" => (f.DeltaOptimal, f.DeltaOptimalOk, LapMath.Theoretical(LapMath.BestSectors(v.Race.Laps.Laps, v.Race.Laps.SectorCount))),
            _ => (f.DeltaBest, f.DeltaBestOk, f.LapBest),
        };
        double? d = ok ? delta : null;
        var color = d is null ? p.P.Muted : d < 0 ? p.P.GoodText : p.P.DangerText;
        string refLabel = reference switch { "session" => "Δ SESSION BEST", "optimal" => "Δ OPTIMAL", _ => "Δ MY BEST" };
        var fields = v.Options.Columns("fields");
        bool bar = v.Options.Bool("bar");
        bool sectors = v.Options.Bool("sectors") && v.Race.Laps.SectorCount > 1;
        float barH = bar ? 7 * p.Density + 6 : 0, sectorH = sectors ? p.LineHeight(p.Small) + 10 : 0;
        bool wide = area.Width >= 300 * p.Density && fields.Count > 0;
        float topH = Math.Max(p.LineHeight(p.Big) + p.LineHeight(p.Label) + 10, area.Height - barH - sectorH - (wide ? 0 : p.BoxedStatHeight() + 4));
        var top = area.Top(Math.Min(topH, area.Height), out var rest);
        var deltaBlock = wide ? top.Left(top.Width * 0.42f, out var times) : top.Top(Math.Min(top.Height, p.LineHeight(p.Huge) + p.LineHeight(p.Label) + 10), out times);
        if (!wide) times = rest.Top(Math.Min(rest.Height, p.BoxedStatHeight()), out rest);
        var tint = d is null ? p.Raised(0.05) : HudPalette.Mix(p.Raised(0.05), d < 0 ? p.P.Good : p.P.Danger, 0.22);
        if (d is null) p.Fill(deltaBlock, p.Solid(tint, 130)); else p.Soft(deltaBlock, d < 0 ? p.P.Good : p.P.Danger, 75);
        var inner = deltaBlock.Inflated(-8 * p.Density, -4 * p.Density);
        var label = inner.Top(p.LineHeight(p.Label) + 1, out var number);
        p.Text(p.LabelText(refLabel), p.Label, p.LabelColor, label, HAlign.Left, VAlign.Top);
        var deltaFont = number.Height >= p.LineHeight(p.Huge) * 0.9f ? p.Huge : p.Big;
        p.Text(d is null ? RaceFormat.Missing : RaceFormat.Delta(d), deltaFont, color, number, HAlign.Left, VAlign.Center, false);
        float gap = 4 * p.Density, cellH = p.BoxedStatHeight();
        times = RectangleF.FromLTRB(times.Left + (wide ? 8 * p.Density : 0), times.Top, times.Right + gap, times.Bottom);
        int cols = wide ? 2 : Math.Max(1, (int)(times.Width / (74 * p.Density)));
        int rowsN = wide ? Math.Max(1, (int)((times.Height + gap) / (cellH + gap))) : 1;
        int fit = times.Height < cellH - 1 ? 0 : Math.Min(fields.Count, cols * rowsN);
        int rowsUsed = Math.Max(1, (fit + cols - 1) / cols);
        float cw = times.Width / Math.Max(1, Math.Min(cols, fit)), ch = wide ? (times.Height + gap) / rowsUsed : times.Height + gap;
        for (int i = 0; i < fit; i++)
        {
            var box = new RectangleF(times.X + i % cols * cw, times.Y + i / cols * ch, cw - gap, ch - gap);
            var (name, value, valueColor) = fields[i] switch
            {
                "current" => ("LAP", RaceFormat.LapTime(f.LapCurrent, 1), (Color?)null),
                "last" => ("LAST", RaceFormat.LapTime(f.LapLast), LastColor(p, v)),
                "best" => ("BEST", RaceFormat.LapTime(f.LapBest), null),
                "predicted" => ("PREDICTED", d is { } dd && refLap is { } rl ? RaceFormat.LapTime(rl + dd) : RaceFormat.Missing, p.P.AccentText),
                "sessionBest" => ("SESSION", RaceFormat.LapTime(FieldOrder.FastestLap(f)?.Time), p.P.Fastest),
                "classBest" => ((f.Session.Player?.ClassName ?? "CLASS").ToUpperInvariant(), RaceFormat.LapTime(FieldOrder.FastestLap(f, f.Session.Player?.ClassId)?.Time), p.P.Fastest),
                "theoretical" => ("OPTIMAL", RaceFormat.LapTime(LapMath.Theoretical(LapMath.BestSectors(v.Race.Laps.Laps, v.Race.Laps.SectorCount))), (Color?)null),
                _ => ("", "", null),
            };
            if (name.Length > 0) p.BoxedStat(box, name, value, valueColor);
            p.Item("fields", fields[i], box);
        }
        for (int i = fit; i < fields.Count; i++) p.Overflow?.Add(("fields", fields[i]));
        if (bar && rest.Height >= 7 * p.Density + 4)
        {
            var barBox = rest.Top(barH, out rest);
            var track = new RectangleF(barBox.X, barBox.Y + 4, barBox.Width, 7 * p.Density);
            p.Fill(track, p.Solid(p.Raised(0.10), 130), 3);
            float mid = track.X + track.Width / 2;
            if (d is { } dv)
            {
                float len = (float)(Math.Clamp(dv / v.Options.Number("range"), -1, 1) * track.Width / 2);
                var fill = len < 0 ? new RectangleF(mid + len, track.Y, -len, track.Height) : new RectangleF(mid, track.Y, len, track.Height);
                p.Fill(fill, d < 0 ? p.P.Good : p.P.Danger, 3);
            }
            p.Line(mid, track.Y - 2, mid, track.Bottom + 2, p.P.Heading, 1.5f);
        }
        if (sectors && rest.Height > p.LineHeight(p.Small) + 6) Sectors(p, v, rest);
    }

    private static Color LastColor(HudPainter p, HudView v)
    {
        var laps = v.Race.Laps.Laps;
        if (laps.Count == 0) return p.P.Heading;
        var last = laps[^1];
        return !last.Clean ? p.P.WarnText : v.Frame.LapBest is { } best && Math.Abs(last.Time - best) < 0.0005 ? p.P.Fastest : p.P.Heading;
    }

    private static void Sectors(HudPainter p, HudView v, RectangleF r)
    {
        var tracker = v.Race.Laps;
        int n = tracker.SectorCount;
        if (n <= 1) return;
        var best = LapMath.BestSectors(tracker.Laps, n);
        var previous = tracker.Laps.Count > 0 ? tracker.Laps[^1].Sectors : null;
        float gap = n > 8 ? 2 : 4;
        float w = (r.Width + gap) / n, h = Math.Min(r.Height, p.LineHeight(p.Small) + 8);
        string format = w - gap >= p.Measure("00.00", p.SmallBold) + 6 ? "0.00" : "0.0";
        bool labels = w - gap >= p.Measure("00.0", p.SmallBold) + 4;
        for (int i = 0; i < n; i++)
        {
            var box = new RectangleF(r.X + i * w, r.Y + 2, w - gap, h);
            double? t = i < tracker.CurrentSectors.Count ? tracker.CurrentSectors[i] : null;
            Color fill = p.Solid(p.Raised(0.07), 130), text = p.P.Muted;
            string label = labels ? $"S{i + 1}" : "";
            if (t is { } st)
            {
                if (labels) label = st.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
                bool pb = best[i] is null || st <= best[i]!.Value + 0.0005;
                bool faster = previous is not null && i < previous.Count && previous[i] is { } ps && st < ps;
                fill = pb ? p.P.Fastest : faster ? p.P.Good : p.P.Warn;
                text = p.P.Heading;
                p.Chip(box, fill);
                if (label.Length > 0) p.Text(label, p.SmallBold, text, box, HAlign.Center, VAlign.Center);
                continue;
            }
            else if (i == tracker.CurrentSector) { fill = HudPalette.Alpha(p.P.AccentSoft, 200); text = p.P.AccentText; }
            p.Fill(box, fill, p.CornerRadius(3));
            if (label.Length > 0) p.Text(label, p.SmallBold, text, box, HAlign.Center, VAlign.Center);
        }
    }
}

internal sealed class FuelWidget : HudWidget
{
    private static readonly HudChoice[] Figures =
    {
        new("level", "Fuel in the tank"), new("perlap", "Per lap (average)"), new("last", "Last lap"),
        new("max", "Most on one lap"), new("laps", "Laps it lasts"), new("time", "Time it lasts"),
        new("tofinish", "Needed to the flag"), new("surplus", "Spare / short at the flag"), new("atflag", "Left at the flag (expected)"),
        new("add", "Add at the next stop"), new("stops", "Stops left"), new("window", "Pit window"), new("togo", "Laps to go"),
    };

    private static readonly HudChoice[] Parts =
    {
        new("header", "Laps to go, next pit, stops", "A line of key figures (Header shows)"),
        new("gauge", "Tank gauge", "Fuel in the tank, the laps it lasts and the tank's size over a bar in the fuel status color"),
        new("grid", "Consumption grid", "A row per rate (this lap, average, most…) with what it means: per lap, laps, refuel, left at the flag"),
        new("plan", "Plan state", "No stop, window open, pit needed… as a colored band"),
        new("figures", "Big figures", "Large numbers (Big figures shows)"),
        new("table", "Last / average / most table", "Columns of rates, rows of per lap, laps and refuel (Table columns / rows)"),
    };

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Columns("parts", "Sections, top to bottom", "header,gauge,grid", Parts)
            with { Group = HudOptionGroup.Parts, Help = "The blocks the widget is built of; what each one shows is picked under What to show" },
        HudOption.Columns("head", "Header shows", "togo,nextpit,stops",
            new HudChoice("togo", "Laps to go", "Colored by how sure the estimate is"), new HudChoice("nextpit", "Next pit lap", "The lap the pit window opens (one stop then reaches the flag)"),
            new HudChoice("pitby", "Pit by lap", "The last lap before the tank runs dry"), new HudChoice("stops", "Stops left"),
            new HudChoice("add", "Add at the next stop"), new HudChoice("level", "Fuel in the tank")),
        HudOption.Columns("gridRows", "Grid rows", "curr,avg,max,min",
            new HudChoice("curr", "This lap", "Projected from what this lap has used so far"), new HudChoice("avg", "Average", "The rate the plan uses (clean, green laps)"),
            new HudChoice("avgN", "Average of the last laps", "Plain average of your last clean laps (Laps in the average)"),
            new HudChoice("max", "Most on one lap"), new HudChoice("min", "Least on one lap"), new HudChoice("last", "Last lap")),
        HudOption.Columns("gridCols", "Grid columns", "use,laps,refuel",
            new HudChoice("use", "Per lap"), new HudChoice("laps", "Laps it lasts"), new HudChoice("refuel", "Refuel to the flag", "What to add, in total, to reach the flag at that rate (with the plan's reserve)"),
            new HudChoice("finish", "Left at the flag", "Fuel in the tank at the flag at that rate, without stopping (negative = short)")),
        HudOption.Int("avgLaps", "Laps in the average", 5, 3, 10),
        HudOption.Bool("statusEdge", "Border in the fuel status color", true, "Green: enough to the flag. Amber: a stop to come. Red: under the low-fuel alert's laps"),
        HudOption.Bool("title", "Title row", false, "“Fuel” with how many clean laps the estimate has"),
        HudOption.Columns("top", "Big figures", "level,laps,togo", Figures),
        HudOption.Columns("cols", "Table columns", "last,avg,max",
            new HudChoice("last", "Last lap"), new HudChoice("avg", "Average", "The rate the plan uses (clean, green laps)"), new HudChoice("max", "Most on one lap")),
        HudOption.Columns("rows", "Table rows", "perlap,laps,refuel",
            new HudChoice("perlap", "Per lap"), new HudChoice("laps", "Laps it lasts"), new HudChoice("refuel", "Refuel to the flag", "What to add, in total, to reach the flag at that rate (with the plan's reserve)")),
    };

    private readonly List<(string Id, string Label, string Value, Color? Color)> _top = new();

    private enum Status { Safe, Caution, Danger }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Fuel")) return;
        var f = v.Frame;
        var r = v.Race;
        var plan = r.Fuel;
        var estimate = r.FuelData.Estimate;
        int measured = estimate.UsedLapCount;
        float pad = 7 * p.Density, partPad = 6 * p.Density, gap = partPad * 2 + p.PieceGap;
        var bounds = p.Bounds;
        float top = 0;
        if (v.Options.Bool("title"))
        {
            string sub = measured == 0 ? "measuring…" : $"{measured} clean lap{(measured == 1 ? "" : "s")} · {Confidence(estimate.Reliability)}";
            p.TitleBar(new RectangleF(0, 0, bounds.Width, p.PillHeight), "Fuel", r.FuelData.EnergyUnits ? "" : sub);
            top = p.PillHeight + p.PieceGap;
        }
        if (f.Fuel is not { } level || r.FuelData.EnergyUnits)
        {
            var body = new RectangleF(0, top, bounds.Width, bounds.Height - top);
            p.Panel(body);
            p.Text(r.FuelData.EnergyUnits ? "This car reports energy (kWh), not fuel" : "This car doesn't report fuel", p.Small, p.P.Faint, body.Inflated(-pad, -pad), HAlign.Center, VAlign.Center);
            return;
        }
        double lowLaps = r.Alerts.Threshold("fuel.low");
        var status = StatusOf(plan, lowLaps);
        Color? edge = v.Options.Bool("statusEdge") ? HudPalette.Alpha(status switch { Status.Danger => p.P.Danger, Status.Caution => p.P.Warn, _ => p.P.Good }, 170) : null;
        var area = new RectangleF(pad, top + partPad, bounds.Width - pad * 2, bounds.Height - top - partPad * 2);
        var parts = v.Options.Columns("parts");
        for (int i = 0; i < parts.Count; i++)
        {
            float need = Height(parts[i], p, v);
            if (need <= 0) continue;
            if (area.Height < need - 0.5f)
            {
                for (int k = i; k < parts.Count; k++) p.Overflow?.Add(("parts", parts[k]));
                break;
            }
            float h = i == parts.Count - 1 && parts[i] is "grid" or "table" ? area.Height : Math.Min(area.Height, Natural(parts[i], p, v));
            var box = area.Top(h, out area);
            p.Item("parts", parts[i], box);
            var piece = new RectangleF(0, box.Y - partPad, bounds.Width, box.Height + partPad * 2);
            p.Panel(piece);
            if (edge is { } c) p.Outline(piece.Inflated(-1, -1), c, 2, p.CornerRadius(4));
            switch (parts[i])
            {
                case "header": HeaderLine(p, v, box, level, plan); break;
                case "gauge": Gauge(p, v, box, level, plan, status); break;
                case "grid": Grid(p, v, box, level, plan); break;
                case "plan": PlanBand(p, v, box, plan, measured); break;
                case "figures": Figures_(p, v, box, level, plan, lowLaps); break;
                case "table": TablePart(p, v, box, level, plan); break;
            }
            area = new RectangleF(area.X, area.Y + gap, area.Width, Math.Max(0, area.Height - gap));
        }
    }

    private static Status StatusOf(FuelPlan? plan, double lowLaps)
    {
        if (plan is null) return Status.Safe;
        if (plan.LapsOfFuel < lowLaps) return Status.Danger;
        if (plan.LapsToGo is null) return plan.LapsOfFuel < lowLaps * 2 ? Status.Caution : Status.Safe;
        return plan.Stops == 0 ? Status.Safe : Status.Caution;
    }

    private float Natural(string part, HudPainter p, HudView v) => part switch
    {
        "grid" => p.LineHeight(p.Label) + 4 + v.Options.Columns("gridRows").Count * (p.LineHeight(p.Body) + 4 * p.Density),
        "table" => p.LineHeight(p.Label) + 4 + v.Options.Columns("rows").Count * (p.LineHeight(p.Bold) + 5 * p.Density + 2),
        _ => Height(part, p, v),
    };

    private float Height(string part, HudPainter p, HudView v) => part switch
    {
        "header" => p.LineHeight(p.Label) + p.LineHeight(p.Bold) + 4 * p.Density,
        "gauge" => p.LineHeight(p.SmallBold) + 2 + 6 * p.Density,
        "grid" => v.Options.Columns("gridRows").Count == 0 || v.Options.Columns("gridCols").Count == 0 ? 0
            : p.LineHeight(p.Label) + 4 + Math.Min(2, v.Options.Columns("gridRows").Count) * (p.LineHeight(p.Body) + 4 * p.Density),
        "plan" => p.LineHeight(p.Bold) + 8 * p.Density,
        "figures" => v.Options.Columns("top").Count > 0 ? p.ValueStatHeight() : 0,
        "table" => v.Options.Columns("cols").Count == 0 || v.Options.Columns("rows").Count == 0 ? 0
            : p.LineHeight(p.Label) + 4 + (p.LineHeight(p.Bold) + 5 * p.Density + 2),
        _ => 0,
    };

    private static void HeaderLine(HudPainter p, HudView v, RectangleF box, double level, FuelPlan? plan)
    {
        var f = v.Frame;
        var u = v.Units;
        var ids = v.Options.Columns("head");
        if (ids.Count == 0) return;
        float cw = box.Width / ids.Count;
        for (int i = 0; i < ids.Count; i++)
        {
            var cell = new RectangleF(box.X + i * cw, box.Y, cw, box.Height);
            var align = i == 0 ? HAlign.Left : i == ids.Count - 1 ? HAlign.Right : HAlign.Center;
            Color color = p.P.Heading;
            string label, value;
            bool dot = false;
            switch (ids[i])
            {
                case "togo":
                    label = "TO GO";
                    double? togo = plan?.LapsToGo ?? (f.SessionKind == SessionKind.Race ? v.Race.LapsToGo(f) : null);
                    value = togo is { } t ? $"{Math.Ceiling(t):0} LAPS" : RaceFormat.Missing;
                    if (togo is not null && plan is not null)
                    {
                        color = plan.Reliability switch { EstimateReliability.High => p.P.GoodText, EstimateReliability.Moderate => p.P.WarnText, _ => p.P.DangerText };
                        if (plan.Reliability == EstimateReliability.Moderate || plan.LapsToGoForecast) value = "~" + value;
                        dot = true;
                    }
                    break;
                case "nextpit":
                    label = "NEXT PIT";
                    value = plan?.Stops switch
                    {
                        0 => "None",
                        > 0 when plan.WindowOpensIn is { } o && f.Lap is { } lap => o <= 0 ? "Now" : "L" + (lap + (int)Math.Ceiling(o)),
                        _ => RaceFormat.Missing,
                    };
                    if (plan is { Stops: > 0, WindowOpensIn: <= 0 }) color = p.P.GoodText;
                    break;
                case "pitby":
                    label = "PIT BY";
                    value = plan is not null && f.Lap is { } now ? "L" + (now + (int)Math.Floor(plan.LapsOfFuel)) : RaceFormat.Missing;
                    break;
                case "stops":
                    label = "STOPS";
                    value = plan?.Stops is { } st ? st.ToString() : RaceFormat.Missing;
                    break;
                case "add":
                    label = "ADD";
                    value = plan?.Stops is 0 ? "–" : plan?.AddAtNextStop is { } add ? RaceFormat.Fuel(add, u) : RaceFormat.Missing;
                    break;
                default:
                    label = "TANK";
                    value = RaceFormat.Fuel(level, u);
                    break;
            }
            p.Item("head", ids[i], cell);
            float lh = p.LineHeight(p.Label);
            p.Text(p.LabelText(label), p.Label, p.LabelColor, new RectangleF(cell.X, cell.Y, cell.Width, lh), align, VAlign.Top);
            var valueBox = new RectangleF(cell.X, cell.Y + lh + 1, cell.Width, cell.Height - lh - 1);
            if (dot)
            {
                float d = 3 * p.Density, w = p.Measure(value, p.Bold) + d * 2 + 4;
                float x = align switch { HAlign.Right => valueBox.Right - w, HAlign.Center => valueBox.X + (valueBox.Width - w) / 2, _ => valueBox.X };
                p.Dot(x + d, valueBox.Y + valueBox.Height / 2, d, color);
                p.Text(value, p.Bold, color, new RectangleF(x + d * 2 + 4, valueBox.Y, valueBox.Right - x - d * 2 - 4, valueBox.Height), HAlign.Left, VAlign.Center);
            }
            else p.Number(value, p.Bold, color, valueBox, align, VAlign.Center);
        }
    }

    private static void Gauge(HudPainter p, HudView v, RectangleF box, double level, FuelPlan? plan, Status status)
    {
        var u = v.Units;
        var r = v.Race;
        float lh = p.LineHeight(p.SmallBold);
        var line = new RectangleF(box.X, box.Y, box.Width, lh);
        p.Text("TANK", p.Label, p.LabelColor, line, HAlign.Left, VAlign.Center, false);
        string middle = RaceFormat.Fuel(level, u) + (plan is not null ? $" · {plan.LapsOfFuel:0.0} laps" : "");
        p.Text(middle, p.SmallBold, p.P.Heading, line, HAlign.Center, VAlign.Center);
        if (r.Tank is { } cap) p.Text($"{RaceFormat.Fuel(cap, u)} CAP", p.Label, p.LabelColor, line, HAlign.Right, VAlign.Center, false);
        var track = new RectangleF(box.X, line.Bottom + 2, box.Width, 6 * p.Density);
        if (r.Tank is not { } tank || tank <= 0) return;
        var fill = status switch { Status.Danger => p.P.Danger, Status.Caution => p.P.Warn, _ => p.P.Good };
        p.Bar(track, level / tank, fill, HudPalette.Alpha(p.P.LineStrong, 120));
        if (plan?.ToFinish is { } need && need > 0)
        {
            float x = track.X + (float)Math.Clamp(need / tank, 0, 1) * track.Width;
            p.Line(x, track.Y - 2, x, track.Bottom + 2, need <= level ? p.P.Heading : p.P.DangerText, 2);
        }
    }

    private static (string Label, double? Rate) RowRate(string id, HudView v, FuelPlan? plan)
    {
        var r = v.Race;
        var laps = r.FuelData.Laps;
        switch (id)
        {
            case "curr":
                double? used = r.FuelThisLap;
                double? rate = used is { } u && v.Frame.LapDistPct is { } pct && pct - r.FuelThisLapFrom is var driven and > 0.1 && u > 0 ? u / driven : null;
                return ("CURR", rate ?? (laps.Count > 0 && laps[^1].FuelUsedLiters > 0 ? laps[^1].FuelUsedLiters : null));
            case "avg": return ("AVG", plan?.PerLap ?? r.FuelPerLap);
            case "avgN":
                int n = v.Options.Int("avgLaps");
                double sum = 0;
                int count = 0;
                for (int i = laps.Count - 1; i >= 0 && count < n; i--)
                    if (laps[i].IsEligible) { sum += laps[i].FuelUsedLiters; count++; }
                return ($"AVG {n}", count > 0 ? sum / count : null);
            case "max": return ("MAX", MostOnOneLap(laps));
            case "min":
                double? least = null;
                foreach (var l in laps) if (l.IsEligible && (least is null || l.FuelUsedLiters < least)) least = l.FuelUsedLiters;
                return ("MIN", least);
            case "last": return ("LAST", laps.Count > 0 && laps[^1].FuelUsedLiters > 0 ? laps[^1].FuelUsedLiters : null);
            default: return (id, null);
        }
    }

    private static void Grid(HudPainter p, HudView v, RectangleF box, double level, FuelPlan? plan)
    {
        var u = v.Units;
        var rows = v.Options.Columns("gridRows");
        var cols = v.Options.Columns("gridCols");
        if (rows.Count == 0 || cols.Count == 0) return;
        float headH = p.LineHeight(p.Label) + 4, rowH = p.LineHeight(p.Body) + 4 * p.Density;
        int fit = Math.Clamp((int)((box.Height - headH) / rowH), 0, rows.Count);
        for (int i = fit; i < rows.Count; i++) p.Overflow?.Add(("gridRows", rows[i]));
        if (fit == 0) return;
        double reserve = plan is { ToFinish: { } tf, LapsToGo: { } tg } ? Math.Max(0, tf - tg * plan.PerLap) : 0;
        double? togo = plan?.LapsToGo ?? (v.Frame.SessionKind == SessionKind.Race ? v.Race.LapsToGo(v.Frame) : null);
        float labelW = Math.Min(box.Width * 0.26f, p.Measure("AVG 10", p.Label) + 14 * p.Density);
        float cw = (box.Width - labelW) / cols.Count;
        p.Fill(new RectangleF(box.X, box.Y, box.Width, headH), p.BandColor(), p.CornerRadius(2));
        p.Text(RaceFormat.FuelUnit(u), p.Label, p.LabelColor, new RectangleF(box.X + 6 * p.Density, box.Y, labelW, headH), HAlign.Left, VAlign.Center, false);
        for (int c = 0; c < cols.Count; c++)
        {
            string name = cols[c] switch { "use" => "Per lap", "laps" => "Laps", "refuel" => "Refuel", _ => "Finish" };
            var hb = new RectangleF(box.X + labelW + c * cw, box.Y, cw, headH);
            p.Text(p.LabelText(name), p.Label, p.LabelColor, hb, HAlign.Center, VAlign.Center, false);
            p.Item("gridCols", cols[c], hb);
        }
        float y = box.Y + headH;
        for (int i = 0; i < fit; i++)
        {
            var (label, rate) = RowRate(rows[i], v, plan);
            bool plan_ = rows[i] == "avg";
            var row = new RectangleF(box.X, y, box.Width, rowH);
            var fill = HudPalette.Mix(p.P.Surface, p.P.Bg, i % 2 == 1 ? 0.85 : 0.35);
            if (plan_) fill = HudPalette.Mix(fill, p.P.Accent, 0.22);
            p.Fill(row, p.Solid(fill, 185), p.CornerRadius(2));
            p.Text(label, p.Label, plan_ ? p.P.AccentText : p.LabelColor, new RectangleF(row.X + 6 * p.Density, row.Y, labelW, row.Height), HAlign.Left, VAlign.Center, false);
            p.Item("gridRows", rows[i], new RectangleF(row.X, row.Y, labelW, row.Height));
            for (int c = 0; c < cols.Count; c++)
            {
                string text = RaceFormat.Missing;
                Color color = plan_ ? p.P.Heading : p.P.Text;
                if (rate is { } x && x > 0)
                    switch (cols[c])
                    {
                        case "use": text = FuelNumber(x, u); break;
                        case "laps": text = (level / x).ToString("0.0"); break;
                        case "refuel":
                            if (togo is { } t)
                            {
                                double add = Math.Max(0, t * x + reserve - level);
                                text = FuelNumber(add, u);
                                color = add <= 0 ? p.P.GoodText : color;
                            }
                            break;
                        case "finish":
                            if (togo is { } t2)
                            {
                                double left = level - t2 * x;
                                text = (left < 0 ? "−" : "") + FuelNumber(Math.Abs(left), u);
                                color = left < 0 ? p.P.DangerText : p.P.GoodText;
                            }
                            break;
                    }
                p.Text(text, plan_ ? p.Bold : p.Body, text == RaceFormat.Missing ? p.P.Faint : color, new RectangleF(box.X + labelW + c * cw, row.Y, cw, row.Height), HAlign.Center, VAlign.Center);
            }
            y += rowH;
        }
    }

    private static void PlanBand(HudPainter p, HudView v, RectangleF band, FuelPlan? plan, int measured)
    {
        var (fill, label, value) = State(p, v, plan, measured);
        p.Fill(band, fill, p.CornerRadius(4));
        var on = HudPalette.On(fill);
        var inner = band.Inflated(-8 * p.Density, 0);
        float lw = Math.Min(inner.Width * 0.6f, p.Measure(p.LabelText(label), p.Label) + 8);
        p.Text(p.LabelText(label), p.Label, HudPalette.Alpha(on, 215), inner.Left(lw, out var right), HAlign.Left, VAlign.Center);
        p.Number(value, p.Bold, on, right, HAlign.Right, VAlign.Center);
    }

    private void Figures_(HudPainter p, HudView v, RectangleF row, double level, FuelPlan? plan, double lowLaps)
    {
        _top.Clear();
        foreach (var id in v.Options.Columns("top"))
            if (Cell(id, p, v, level, plan) is { } c) _top.Add(c);
        if (_top.Count == 0) return;
        int n = Math.Max(1, Math.Min(_top.Count, (int)(row.Width / (78 * p.Density))));
        float cw = row.Width / n;
        for (int i = 0; i < n; i++)
        {
            var (id, label, value, color) = _top[i];
            if (id == "laps" && plan is { } pl) color = pl.LapsOfFuel < lowLaps ? p.P.DangerText : pl.LapsOfFuel < lowLaps * 2 ? p.P.WarnText : color;
            if (i > 0) p.Divider(row.X + i * cw, row.Y + 4, row.Bottom - 4);
            p.ValueStat(new RectangleF(row.X + i * cw + 4, row.Y, cw - 8, row.Height), label, value, color, p.Big, HAlign.Center);
            p.Item("top", id, new RectangleF(row.X + i * cw, row.Y, cw, row.Height));
        }
        for (int i = n; i < _top.Count; i++) p.Overflow?.Add(("top", _top[i].Id));
    }

    private static void TablePart(HudPainter p, HudView v, RectangleF r, double level, FuelPlan? plan)
    {
        var cols = v.Options.Columns("cols");
        var rows = v.Options.Columns("rows");
        float rowH = p.LineHeight(p.Bold) + 5 * p.Density, headH = p.LineHeight(p.Label) + 4;
        int rowsFit = Math.Clamp((int)((r.Height - headH + 2) / (rowH + 2)), 0, rows.Count);
        for (int i = rowsFit; i < rows.Count; i++) p.Overflow?.Add(("rows", rows[i]));
        if (rowsFit > 0) Table(p, v, new RectangleF(r.X, r.Y, r.Width, headH + rowsFit * (rowH + 2) - 2), cols, rows, rowsFit, rowH, headH, level, plan);
    }

    private static void Table(HudPainter p, HudView v, RectangleF r, IReadOnlyList<string> cols, IReadOnlyList<string> rows, int rowsFit,
        float rowH, float headH, double level, FuelPlan? plan)
    {
        var u = v.Units;
        var data = v.Race.FuelData;
        double? Rate(string col) => col switch
        {
            "last" => data.Laps.Count > 0 && data.Laps[^1].FuelUsedLiters > 0 ? data.Laps[^1].FuelUsedLiters : null,
            "avg" => plan?.PerLap ?? v.Race.FuelPerLap,
            "max" => MostOnOneLap(data.Laps),
            _ => null,
        };
        double reserve = plan is { ToFinish: { } tf, LapsToGo: { } tg } ? Math.Max(0, tf - tg * plan.PerLap) : 0;
        double? togo = plan?.LapsToGo ?? (v.Frame.SessionKind == SessionKind.Race ? v.Race.LapsToGo(v.Frame) : null);
        float widest = 0;
        for (int i = 0; i < rowsFit; i++) widest = Math.Max(widest, p.Measure(p.LabelText(RowLabel(rows[i])), p.Label));
        float labelW = Math.Min(r.Width * 0.34f, widest + 14 * p.Density);
        float cw = (r.Width - labelW) / cols.Count;
        int avgAt = -1;
        for (int c = 0; c < cols.Count; c++) if (cols[c] == "avg") avgAt = c;
        if (avgAt >= 0)
            p.Fill(new RectangleF(r.X + labelW + avgAt * cw + 1, r.Y, cw - 2, r.Height), p.Solid(HudPalette.Mix(p.Raised(0.06), p.P.Accent, 0.16), 120), p.CornerRadius(3));
        for (int c = 0; c < cols.Count; c++)
        {
            string name = cols[c] switch { "last" => "Last", "avg" => "Avg", "max" => "Max", _ => "" };
            p.Text(p.LabelText(name), p.Label, c == avgAt ? p.P.AccentText : p.LabelColor, new RectangleF(r.X + labelW + c * cw, r.Y, cw, headH), HAlign.Center, VAlign.Center, false);
            p.Item("cols", cols[c], new RectangleF(r.X + labelW + c * cw, r.Y, cw, headH));
        }
        p.Text(RaceFormat.FuelUnit(u), p.Label, p.LabelColor, new RectangleF(r.X + 7 * p.Density, r.Y, labelW, headH), HAlign.Left, VAlign.Center, false);
        float y = r.Y + headH;
        for (int i = 0; i < rowsFit; i++)
        {
            var row = new RectangleF(r.X, y, r.Width, rowH);
            p.Fill(row, p.Solid(p.Raised(p.P.Dark ? 0.06 : 0.045), 90), p.CornerRadius(2));
            p.Text(p.LabelText(RowLabel(rows[i])), p.Label, p.LabelColor, new RectangleF(row.X + 7 * p.Density, row.Y, labelW - 7 * p.Density, row.Height), HAlign.Left, VAlign.Center);
            p.Item("rows", rows[i], new RectangleF(row.X, row.Y, labelW, row.Height));
            for (int c = 0; c < cols.Count; c++)
            {
                double? rate = Rate(cols[c]);
                string text = RaceFormat.Missing;
                Color color = c == avgAt ? p.P.Heading : p.P.Text;
                if (rate is { } x && x > 0)
                {
                    switch (rows[i])
                    {
                        case "perlap": text = FuelNumber(x, u); break;
                        case "laps":
                            double laps = level / x;
                            text = laps.ToString("0.0");
                            break;
                        case "refuel":
                            if (togo is { } t)
                            {
                                double add = Math.Max(0, t * x + reserve - level);
                                text = FuelNumber(add, u);
                                if (add <= 0) color = p.P.GoodText;
                            }
                            break;
                    }
                }
                p.Text(text, c == avgAt ? p.Bold : p.Body, color, new RectangleF(r.X + labelW + c * cw, row.Y, cw, row.Height), HAlign.Center, VAlign.Center);
            }
            y += rowH + 2;
        }
    }

    private static double? MostOnOneLap(IReadOnlyList<FuelLapSample> laps)
    {
        double? most = null;
        for (int i = 0; i < laps.Count; i++)
            if (laps[i].IsEligible && (most is null || laps[i].FuelUsedLiters > most)) most = laps[i].FuelUsedLiters;
        return most;
    }

    private static string RowLabel(string id) => id switch { "perlap" => "Per lap", "laps" => "Laps", "refuel" => "Refuel", _ => id };

    private static string FuelNumber(double litres, Config.HudUnits u)
    {
        double x = RaceFormat.FuelValue(litres, u);
        return x.ToString(u == Config.HudUnits.Imperial || Math.Abs(x) < 10 ? "0.00" : "0.0", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static (Color Fill, string Label, string Value) State(HudPainter p, HudView v, FuelPlan? plan, int measured)
    {
        var u = v.Units;
        var neutral = p.Raised(0.12);
        if (plan is null) return (neutral, "MEASURING", measured == 0 ? "After 1 clean lap" : "…");
        if (plan.Reliability is EstimateReliability.Learning or EstimateReliability.Low && plan.LapsToGo is not null)
            return (p.P.Warn, "LOW CONFIDENCE", $"{measured} clean lap{(measured == 1 ? "" : "s")}");
        if (plan.LapsToGo is null) return (neutral, "PER LAP", RaceFormat.Fuel(plan.PerLap, u));
        if (plan.Stops == 0) return (p.P.Good, "NO STOP · EST.", "+" + RaceFormat.Fuel(plan.Surplus ?? 0, u));
        if (plan.Stops is not { } stops) return (p.P.Danger, "SHORT · EST.", RaceFormat.Fuel(-(plan.Surplus ?? 0), u));
        string add = plan.AddAtNextStop is { } a ? "Add " + RaceFormat.Fuel(a, u) : "Pit";
        string many = stops > 1 ? $" · {stops} STOPS" : "";
        return plan.WindowOpensIn is <= 0
            ? (p.P.Good, "WINDOW OPEN" + many, add)
            : (p.P.Warn, (plan.WindowOpensIn is { } o ? $"WINDOW IN {o:0.0} LAPS" : "PIT NEEDED") + many, add);
    }

    internal static string Confidence(EstimateReliability r) => r switch
    {
        EstimateReliability.High => "settled",
        EstimateReliability.Moderate => "fair",
        EstimateReliability.Low => "low confidence",
        _ => "learning",
    };

    private static (string Id, string Label, string Value, Color? Color)? Cell(string id, HudPainter p, HudView v, double level, FuelPlan? plan)
    {
        var r = v.Race;
        var f = v.Frame;
        var u = v.Units;
        switch (id)
        {
            case "level": return (id, "TANK", RaceFormat.Fuel(level, u), null);
            case "perlap": return (id, "PER LAP", r.FuelPerLap is { } pl ? RaceFormat.Fuel(pl, u) : RaceFormat.Missing, null);
            case "last": return (id, "LAST LAP", r.FuelData.Laps.Count > 0 ? RaceFormat.Fuel(r.FuelData.Laps[^1].FuelUsedLiters, u) : RaceFormat.Missing, null);
            case "max": return (id, "MOST", MostOnOneLap(r.FuelData.Laps) is { } mx ? RaceFormat.Fuel(mx, u) : RaceFormat.Missing, null);
            case "atflag":
                return (id, "AT THE FLAG", plan?.AtFlag is { } at ? (at < 0 ? "−" : "") + RaceFormat.Fuel(Math.Abs(at), u) : RaceFormat.Missing,
                    plan?.AtFlag is < 0 ? p.P.DangerText : null);
            case "laps": return (id, "LAPS LEFT", plan is null ? RaceFormat.Missing : plan.LapsOfFuel.ToString("0.0"), null);
            case "time":
                double? pace = r.Laps.Laps.Count > 0 ? LapMath.Stats(r.Laps.Laps, 5)?.Average : f.LapBest;
                return (id, "TIME LEFT", plan is not null && pace is { } pc ? RaceFormat.Clock(plan.LapsOfFuel * pc) : RaceFormat.Missing, null);
            case "togo": return (id, "LAPS TO GO", plan?.LapsToGo is { } tg ? tg.ToString("0.0") : RaceFormat.Missing, null);
            case "tofinish": return (id, "TO FINISH", plan?.ToFinish is { } tf ? RaceFormat.Fuel(tf, u) : RaceFormat.Missing, null);
            case "surplus":
                return (id, plan?.Surplus is < 0 ? "SHORT" : "SPARE", plan?.Surplus is { } s ? RaceFormat.Fuel(Math.Abs(s), u) : RaceFormat.Missing,
                    plan?.Surplus is < 0 ? p.P.DangerText : plan?.Surplus is >= 0 ? p.P.GoodText : null);
            case "add":
                return (id, "ADD", plan?.Stops is 0 ? "No stop" : plan?.AddAtNextStop is { } add ? RaceFormat.Fuel(add, u) : RaceFormat.Missing, plan?.Stops is 0 ? p.P.GoodText : null);
            case "stops": return (id, "STOPS", plan?.Stops is { } st ? st.ToString() : RaceFormat.Missing, null);
            case "window":
                string win = plan?.Stops switch
                {
                    0 => "Not needed",
                    > 0 when plan.WindowOpensIn is { } o => o <= 0 ? "Open" : $"In {o:0.0} laps",
                    _ => RaceFormat.Missing,
                };
                return (id, "PIT WINDOW", win, plan?.WindowOpensIn is <= 0 && plan.Stops > 0 ? p.P.GoodText : null);
            default: return null;
        }
    }
}

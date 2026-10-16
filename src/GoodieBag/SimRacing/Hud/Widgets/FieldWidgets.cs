using System.Drawing;
using System.Globalization;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Hud.Widgets;

internal readonly record struct Cell(string Text, Color Color, bool Bold = false, Color? Pill = null)
{
    public static Cell Empty(HudPalette pal) => new(RaceFormat.Missing, pal.Faint);
}

internal sealed record Column(string Id, string Header, string Template, HAlign Align, Func<int, Cell> Value, bool Flex = false)
{
    public Action<int, List<(string Text, Color Fill, Color Fore, FlagKind Flag)>>? Marks { get; init; }

    public Action<HudPainter, int, RectangleF, bool>? Draw { get; init; }
}

internal readonly record struct RowStyle(bool PositionBlock = true, bool NumberFill = true, bool NumberEdge = true)
{
    public static RowStyle Of(HudView v)
    {
        bool B(string key, bool fallback) => v.Def.Option(key) is not null ? v.Options.Bool(key) : fallback;
        return new RowStyle(B("posBlock", true), B("numFill", true), B("numEdge", true));
    }
}

internal static class HudTable
{
    public const float Gap = 6, ClassEdge = 3;

    private static readonly List<(string Text, Color Fill, Color Fore, FlagKind Flag)> _marks = new();

    public const string FastestMark = "FL";

    public static float FastestRoom(HudPainter p, HudView v, float rowScale) =>
        v.Def.Option("marks") is not null && v.Options.Bool("marks") ? p.LineHeight(p.Body) * rowScale * p.Density + 2 : 0;

    private const float BlockPad = 10;

    private const float StripeLead = 2 + ClassEdge + 5;

    private static bool IsPosition(Column c) => c.Id is "pos" or "cpos";

    public static List<(Column Col, float Width)> Fit(HudPainter p, IReadOnlyList<Column> columns, float width, Font font, string key = "columns")
    {
        var bold = Bold(p, font);
        var natural = columns.Select(c => c.Flex
            ? (c.Template.Length > 0 ? p.Measure(c.Template, font) : 0) + (c.Marks is not null ? p.Measure(FastestMark, p.Tiny) + 12 : 0)
            : Math.Max(p.Measure(c.Template, IsPosition(c) ? bold : font), p.Measure(c.Header, p.Tiny)) + 2).ToList();
        var (kept, widths) = Layout(natural, columns.Select(c => c.Flex).ToList(), columns.Select(IsPosition).ToList(), width, columns.Select(c => c.Id == "gap").ToList());
        for (int i = 0; i < columns.Count; i++)
            if (!kept.Contains(i)) p.Overflow?.Add((key, columns[i].Id));
        return kept.Select(i => (columns[i], widths[i])).ToList();
    }

    internal static (List<int> Kept, float[] Widths) Layout(IReadOnlyList<float> natural, IReadOnlyList<bool> flex, IReadOnlyList<bool> position, float width, IReadOnlyList<bool>? keep = null)
    {
        const float NameMin = 60;
        var kept = Enumerable.Range(0, natural.Count).ToList();
        float Lead() => StripeLead + (kept.Count > 0 && position[kept[0]] ? BlockPad : 0);
        int Flexes() => kept.Count(i => flex[i]);
        float FlexMin() => kept.Where(i => flex[i]).Sum(i => Math.Max(NameMin, natural[i]));
        float Fixed() => kept.Sum(i => flex[i] ? 0 : natural[i]) + Gap * Math.Max(0, kept.Count - 1);
        while (kept.Count > 1 && Lead() + Fixed() + FlexMin() > width)
        {
            int drop = kept.FindLastIndex(i => !flex[i] && keep?[i] != true);
            if (drop < 0) drop = kept.FindLastIndex(i => !flex[i]);
            if (drop < 0) break;
            kept.RemoveAt(drop);
        }
        float rest = width - Lead() - Fixed();
        var widths = new float[natural.Count];
        int flexes = Flexes();
        foreach (int i in kept)
            widths[i] = flex[i] ? Math.Max(20, rest / flexes) : natural[i] + (flexes == 0 && rest > 0 ? rest / kept.Count : 0);
        return (kept, widths);
    }

    private static Font Bold(HudPainter p, Font font) => p.BoldOf(font);

    public static void Header(HudPainter p, List<(Column Col, float Width)> cols, RectangleF row)
    {
        float x = row.X;
        int start = 0;
        if (cols.Count > 0 && IsPosition(cols[0].Col))
        {
            p.Text(cols[0].Col.Header, p.Tiny, p.P.Faint, new RectangleF(x, row.Y, cols[0].Width + BlockPad, row.Height), HAlign.Center, VAlign.Center);
            x += cols[0].Width + BlockPad;
            start = 1;
        }
        x += StripeLead;
        for (int i = start; i < cols.Count; i++)
        {
            var (col, w) = cols[i];
            p.Text(col.Header, p.Tiny, p.P.Faint, new RectangleF(x, row.Y, w, row.Height), col.Align, VAlign.Center);
            x += w + Gap;
        }
    }

    public static void Row(HudPainter p, List<(Column Col, float Width)> cols, RectangleF row, int car, Color classColor, int emphasis, bool dim, Font font, Color? tint = null, bool alternate = false, RowStyle style = default)
    {
        var pal = p.P;
        var fill = HudPalette.Mix(pal.Surface, pal.Bg, alternate ? 0.85 : 0.35);
        if (emphasis == 1) fill = HudPalette.Mix(fill, pal.Player, 0.26);
        p.Fill(row, p.Solid(fill, 185));
        Color? over = emphasis == 1 ? pal.PlayerText : tint;
        Color Shade(Color c)
        {
            if (over is { } o && (c == pal.Heading || c == pal.Text || c == pal.Muted)) c = o;
            return dim ? HudPalette.Alpha(c, 150) : c;
        }
        var bold = Bold(p, font);
        float x = row.X;
        int start = 0;
        if (cols.Count > 0 && IsPosition(cols[0].Col))
        {
            var (pc, pw) = cols[0];
            var block = new RectangleF(row.X, row.Y, pw + BlockPad, row.Height);
            var cell = pc.Value(car);
            Color? bg = cell.Pill ?? (emphasis == 1 && style.PositionBlock ? pal.Player : null);
            if (bg is { } b) p.Fill(block, dim ? HudPalette.Alpha(b, 150) : b);
            p.Text(cell.Text, bold, bg is { } on ? HudPalette.On(on) : Shade(pal.Heading), block, HAlign.Center, VAlign.Center, false);
            x = block.Right;
            start = 1;
        }
        bool numbered = false;
        foreach (var (c, _) in cols) if (c.Id == "num") numbered = true;
        if (!numbered || !(style.NumberFill || style.NumberEdge)) p.Fill(new RectangleF(x + 2, row.Y + 3, ClassEdge, row.Height - 6), Shade(classColor));
        x += StripeLead;
        if (emphasis == 2) p.Outline(new RectangleF(row.X + 0.75f, row.Y + 0.75f, row.Width - 1.5f, row.Height - 1.5f), pal.Accent, 1.5f);
        for (int i = start; i < cols.Count; i++)
        {
            var (col, w) = cols[i];
            var box = new RectangleF(x, row.Y, w, row.Height);
            if (col.Draw is { } draw) { draw(p, car, box, dim); x += w + Gap; continue; }
            var cell = col.Value(car);
            if (col.Id == "num" && cell.Text != RaceFormat.Missing && (style.NumberFill || style.NumberEdge))
            {
                var tag = new RectangleF(box.X - 4, row.Y, w + 8, row.Height);
                if (style.NumberFill) p.Fade(tag, classColor, dim ? 45 : 80);
                if (style.NumberEdge) p.Fill(new RectangleF(tag.X, tag.Y, 2, tag.Height), dim ? HudPalette.Alpha(classColor, 150) : classColor);
                p.Text(cell.Text, font, Shade(pal.Muted), new RectangleF(tag.X + 3, tag.Y, tag.Width - 3, tag.Height), HAlign.Center, VAlign.Center, false);
                x += w + Gap;
                continue;
            }
            if (cell.Pill is { } pill && cell.Text.Length > 0)
            {
                float pw = Math.Min(w, p.Measure(cell.Text, p.SmallBold) + 8);
                float px = col.Align == HAlign.Right ? box.Right - pw : col.Align == HAlign.Center ? box.X + (w - pw) / 2 : box.X;
                p.Pill(new RectangleF(px, row.Y + row.Height * 0.16f, pw, row.Height * 0.68f), cell.Text, dim ? HudPalette.Alpha(pill, 150) : pill, p.SmallBold);
                x += w + Gap;
                continue;
            }
            if (cell.Bold && cell.Color == pal.Fastest && cell.Text.Length > 0)
            {
                float tw = Math.Min(w + 4, p.Measure(cell.Text, bold) + 8);
                float tx = col.Align == HAlign.Right ? box.Right - tw + 3 : col.Align == HAlign.Center ? box.X + (w - tw) / 2 : box.X - 3;
                p.Fill(new RectangleF(tx, row.Y + 2, tw, row.Height - 4), HudPalette.Alpha(pal.Fastest, 70), 2);
            }
            if (col.Marks is { } marks)
            {
                _marks.Clear();
                marks(car, _marks);
                float mx = box.Right, mh = Math.Min(row.Height - 4, p.LineHeight(p.Tiny) + 5);
                for (int m = _marks.Count - 1; m >= 0; m--)
                {
                    var (text, tagFill, fore, flag) = _marks[m];
                    if (text == FastestMark)
                    {
                        float side = row.Height - 2;
                        var outside = new RectangleF(p.Bounds.Right + 3, row.Y + 1, side, side);
                        p.Fill(outside, dim ? HudPalette.Alpha(tagFill, 150) : tagFill, 2);
                        Stopwatch(p, outside, fore);
                        continue;
                    }
                    float mw = flag != FlagKind.None ? mh * 1.5f : p.Measure(text, p.Tiny) + 9;
                    if (mx - mw < box.X + 40) break;
                    mx -= mw;
                    var tag = new RectangleF(mx, row.Y + (row.Height - mh) / 2, mw, mh);
                    if (flag != FlagKind.None) HudFlags.Draw(p, tag, flag);
                    else
                    {
                        p.Fill(tag, dim ? HudPalette.Alpha(tagFill, 150) : tagFill, 2);
                        p.Text(text, p.Tiny, fore, tag, HAlign.Center, VAlign.Center, false);
                    }
                    mx -= 3;
                }
                box = new RectangleF(box.X, box.Y, mx - box.X, box.Height);
            }
            p.Text(cell.Text, cell.Bold ? bold : font, Shade(cell.Color), box, col.Align, VAlign.Center);
            x += w + Gap;
        }
    }

    private static void Stopwatch(HudPainter p, RectangleF tag, Color c)
    {
        float s = tag.Height * 0.62f, cx = tag.X + tag.Width / 2, cy = tag.Y + tag.Height / 2 + s * 0.08f, r = s / 2;
        var pen = p.Pen(c, Math.Max(1.2f, s * 0.13f));
        p.G.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
        p.G.DrawLine(pen, cx, cy - r, cx, cy - r - s * 0.16f);
        p.G.DrawLine(pen, cx - s * 0.16f, cy - r - s * 0.2f, cx + s * 0.16f, cy - r - s * 0.2f);
        p.G.DrawLine(pen, cx + r * 0.72f, cy - r * 0.72f, cx + r * 0.95f, cy - r * 0.95f);
        p.G.DrawLine(pen, cx, cy, cx + r * 0.42f, cy - r * 0.42f);
    }

    public static void Badge(HudPainter p, RectangleF box, string license, int? irating, string format, bool dim)
    {
        float h = Math.Min(box.Height - 4, p.LineHeight(p.Tiny) + 6), y = box.Y + (box.Height - h) / 2, x = box.X;
        int alpha = dim ? 150 : 255;
        if (format != "rating" && license.Length > 0)
        {
            string text = RaceFormat.License(license);
            float w = p.Measure(text, p.Tiny) + 10;
            var r = new RectangleF(x, y, w, h);
            p.Fill(r, HudPalette.Alpha(Color.White, dim ? 16 : 26), 2);
            p.Text(text, p.Tiny, HudPalette.Alpha(Color.White, alpha), r, HAlign.Center, VAlign.Center, false);
            x = r.Right + 4;
        }
        if (format != "license" && irating is > 0)
        {
            string text = RaceFormat.IRating(irating);
            float w = p.Measure(text, p.Tiny) + 10;
            var r = new RectangleF(x, y, Math.Min(w, box.Right - x), h);
            p.Fill(r, HudPalette.Alpha(Color.White, dim ? 16 : 26), 2);
            p.Text(text, p.Tiny, HudPalette.Alpha(Color.White, alpha), r, HAlign.Center, VAlign.Center, false);
        }
    }

    public static void FlagEdge(HudPainter p, TelemetryFrame f, float width)
    {
        var flags = f.Flags;
        Color? c = (flags & RaceFlags.Red) != 0 ? HudPalette.FlagRed
            : (flags & (RaceFlags.Yellow | RaceFlags.YellowWaving | RaceFlags.Caution | RaceFlags.CautionWaving)) != 0 ? HudPalette.FlagYellow
            : (flags & RaceFlags.Blue) != 0 ? HudPalette.FlagBlue
            : (flags & (RaceFlags.Checkered | RaceFlags.White)) != 0 ? HudPalette.FlagWhite
            : null;
        if (c is not { } color) return;
        var b = p.Bounds;
        p.Outline(new RectangleF(b.X + width / 2, b.Y + width / 2, b.Width - width, b.Height - width), color, width, p.CornerRadius(4));
    }

    public static void Track(HudPainter p, List<(Column Col, float Width)> cols, RectangleF table, string key = "columns")
    {
        if (p.Items is null) return;
        float x = table.X;
        int start = 0;
        if (cols.Count > 0 && IsPosition(cols[0].Col))
        {
            p.Item(key, cols[0].Col.Id, new RectangleF(x, table.Y, cols[0].Width + BlockPad, table.Height));
            x += cols[0].Width + BlockPad;
            start = 1;
        }
        x += StripeLead;
        for (int i = start; i < cols.Count; i++)
        {
            var (col, w) = cols[i];
            p.Item(key, col.Id, new RectangleF(x - Gap / 2, table.Y, w + Gap, table.Height));
            x += w + Gap;
        }
    }
}

internal static class FieldColumns
{
    public static readonly HudChoice Pos = new("pos", "Position (overall)");
    public static readonly HudChoice ClassPos = new("cpos", "Position in class");
    public static readonly HudChoice Change = new("change", "Places gained / lost", "Since the start of the race");
    public static readonly HudChoice Number = new("num", "Car number");
    public static readonly HudChoice Name = new("name", "Driver");
    public static readonly HudChoice Team = new("team", "Team");
    public static readonly HudChoice IRatingChange = new("irchange", "iRating gain / loss (est.)", "Races: what each driver would gain or lose if it finished in this order (an estimate of iRacing's rating)");
    public static readonly HudChoice Last = new("last", "Last lap");
    public static readonly HudChoice Best = new("best", "Best lap");
    public static readonly HudChoice PaceDelta = new("delta", "Last lap vs yours", "+ = slower than you");
    public static readonly HudChoice Laps = new("laps", "Laps ahead / behind you");
    public static readonly HudChoice Pit = new("pit", "Pit lane / stops");
    public static readonly HudChoice Stint = new("stint", "Laps since pit stop");
    public static readonly HudChoice OutLap = new("out", "Out lap", "OUT while a driver is on the lap after leaving the pits");
    public static readonly HudChoice Incidents = new("inc", "Incidents", "As published in the session information (updated by iRacing every so often)");
    public static readonly HudChoice Trend = new("trend", "Gap trend", "Closing or pulling away, from the last seconds");
    public static readonly HudChoice Badge = new("badge", "License and iRating badge", "The license class and safety rating, the iRating beside it");
    public static readonly HudChoice AvgLap = new("avglap", "Average lap", "Of a driver's last few laps (Laps in the average)");
    public static readonly HudChoice LapsDone = new("lapsdone", "Laps completed");

    public static readonly HudOption NameFormat = HudOption.Choice("nameFormat", "Names", "full",
        new HudChoice("full", "Full name"), new HudChoice("initial", "First initial and last name"), new HudChoice("last", "Last name only"), new HudChoice("first", "First name and last initial"));
    public static readonly HudOption NoDigits = HudOption.Bool("noDigits", "Leave numbers out of names", false, "Some drivers have a number after their name (“Max Verstappen2”)");
    public static readonly HudOption TimeFormat = HudOption.Choice("timeFormat", "Lap times", "full",
        new HudChoice("full", "1:42.123"), new HudChoice("mixed", "1:42.1"), new HudChoice("minutes", "1:42"),
        new HudChoice("sfull", "42.123"), new HudChoice("smixed", "42.1"), new HudChoice("s", "42"));
    public static readonly HudOption Decimals = HudOption.Int("decimals", "Gap decimals", 1, 0, 3);
    public static readonly HudOption BadgeFormat = HudOption.Choice("badgeFormat", "Badge shows", "both",
        new HudChoice("both", "License and iRating"), new HudChoice("license", "License only"), new HudChoice("rating", "iRating only"));
    public static readonly HudOption PitShow = HudOption.Choice("pitShow", "Pit column after a stop", "lap",
        new HudChoice("lap", "Lap of the last stop (L12)"), new HudChoice("since", "Laps since the last stop"), new HudChoice("stops", "Stops made (2×)"));
    public static readonly HudOption PitTime = HudOption.Bool("pitTime", "Time in the pit lane", true, "Seconds in the pit lane beside PIT, counting while a car is in it");
    public static readonly HudOption AvgLaps = HudOption.Int("avgLaps", "Laps in the average", 5, 3, 10);
    public static readonly HudOption PositionBlock = HudOption.Bool("posBlock", "Your position on a colored block", true);
    public static readonly HudOption NumberFill = HudOption.Bool("numFill", "Class color fading behind the number", true);
    public static readonly HudOption NumberEdge = HudOption.Bool("numEdge", "Class color edge by the number", true);
    public static readonly HudOption FlagEdgeOption = HudOption.Bool("flagEdge", "Flag color around the widget", false, "A border in the color of a yellow, red, blue, white or checkered flag while it's out");
    public static readonly HudOption FlagWidth = HudOption.Int("flagWidth", "Flag border width", 4, 1, 10, "px");

    public static readonly HudOption[] Shared =
    {
        NameFormat, NoDigits, TimeFormat, Decimals, BadgeFormat, PitShow, PitTime, AvgLaps,
        PositionBlock, NumberFill, NumberEdge, FlagEdgeOption, FlagWidth,
    };

    private static void Marks(HudPalette pal, TelemetryFrame f, int car, List<(string, Color, Color, FlagKind)> list)
    {
        int? cls = f.Session.NumCarClasses > 1 && f.Session.ByCar.TryGetValue(car, out var d) ? d.ClassId : null;
        if (f.CarBestLap[car] > 0 && FieldOrder.FastestLap(f, cls) is { } fl && fl.Car == car) list.Add((HudTable.FastestMark, pal.Fastest, Color.White, FlagKind.None));
        var flags = f.CarFlags[car];
        if ((flags & RaceFlags.Disqualify) != 0) list.Add(("", default, default, FlagKind.Disqualified));
        else if ((flags & RaceFlags.Black) != 0) list.Add(("", default, default, FlagKind.Black));
        if ((flags & RaceFlags.Repair) != 0) list.Add(("", default, default, FlagKind.Meatball));
        if ((flags & RaceFlags.Blue) != 0) list.Add(("", default, default, FlagKind.Blue));
    }

    public static readonly HudOption MarksOption = HudOption.Bool("marks", "Fastest lap and flags by the name", true,
        "FL on the session's fastest lap (per class in multiclass), and the flag when a driver has a black, repair (meatball), disqualified or blue flag");

    public static Column? Make(string id, HudPainter p, HudView v, Func<int, Cell> gap, string gapHeader)
    {
        var f = v.Frame;
        var r = v.Race;
        var pal = p.P;
        var None = Cell.Empty(pal);
        DriverEntry? D(int car) => f.Session.ByCar.TryGetValue(car, out var d) ? d : null;
        Color Text(int car) => car == f.PlayerCarIdx ? pal.Heading : pal.Text;
        bool multi = f.Session.NumCarClasses > 1;
        string Choice(HudOption o) => v.Def.Option(o.Key) is not null ? v.Options.Choice(o.Key) : o.Default;
        bool Flag(HudOption o) => v.Def.Option(o.Key) is not null ? v.Options.Bool(o.Key) : o.Default == "true";
        int Int(HudOption o) => v.Def.Option(o.Key) is not null ? v.Options.Int(o.Key) : int.Parse(o.Default, CultureInfo.InvariantCulture);
        string times = Choice(TimeFormat);
        string Time(double? t) => RaceFormat.LapTime(t, times);
        switch (id)
        {
            case "pos":
                return new Column(id, "P", "00", HAlign.Right, car => new Cell(f.CarPosition[car] > 0 ? f.CarPosition[car].ToString() : RaceFormat.Missing, Text(car), car == f.PlayerCarIdx,
                    Pill: f.CarSurface[car] == TrackSurface.OffTrack && f.CarActive(car) ? HudPalette.FlagYellow : null));
            case "cpos":
                return new Column(id, multi ? "CL" : "P", "P00", HAlign.Center, car =>
                {
                    int cp = r.StandingOf(car)?.ClassPosition ?? f.CarClassPosition[car];
                    return new Cell(cp > 0 ? cp.ToString() : RaceFormat.Missing, Text(car),
                        Pill: f.CarSurface[car] == TrackSurface.OffTrack && f.CarActive(car) ? HudPalette.FlagYellow : null);
                });
            case "change":
                return new Column(id, "+/−", "▲00", HAlign.Right, car => r.PositionsGained(car) switch
                {
                    > 0 and var g => new Cell("▲" + g, pal.GoodText),
                    < 0 and var l => new Cell("▼" + -l, pal.DangerText),
                    0 => new Cell("–", pal.Faint),
                    _ => None,
                });
            case "num": return new Column(id, "#", "#000", HAlign.Center, car => D(car) is { CarNumber.Length: > 0 } d ? new Cell("#" + d.CarNumber, pal.Muted) : None);
            case "name":
                string nameFormat = Choice(NameFormat);
                bool noDigits = Flag(NoDigits);
                return new Column(id, "DRIVER", nameFormat == "last" ? "Lastname" : "Driver Lastname", HAlign.Left, car =>
                {
                    var d = D(car);
                    string name = d is null ? $"Car {car}" : d.Name.Length > 0 ? RaceFormat.Name(d.Name, nameFormat, noDigits) : d.TeamName;
                    return new Cell(name, Text(car), car == f.PlayerCarIdx);
                }, Flex: true) { Marks = v.Def.Option("marks") is not null && v.Options.Bool("marks") ? (car, list) => Marks(pal, f, car, list) : null };
            case "team": return new Column(id, "TEAM", "Team Name Here", HAlign.Left, car => D(car) is { TeamName.Length: > 0 } d ? new Cell(d.TeamName, pal.Muted) : None);
            case "badge":
                string badgeFormat = Choice(BadgeFormat);
                return new Column(id, "LICENSE", badgeFormat switch { "license" => "A 4.99", "rating" => "0.0k", _ => "A 4.99 0.0k" }, HAlign.Left,
                    car => D(car) is { } d ? new Cell(RaceFormat.License(d.License), pal.Text) : None)
                {
                    Draw = (painter, car, box, dim) => { if (D(car) is { } d) HudTable.Badge(painter, box, d.License, d.IRating, badgeFormat, dim); },
                };
            case "avglap":
                int avgLaps = Int(AvgLaps);
                return new Column(id, "AVG", "0:00.000", HAlign.Right, car => new Cell(Time(r.PaceOf(car, avgLaps)), pal.Muted));
            case "lapsdone": return new Column(id, "LAPS", "000", HAlign.Right, car => new Cell(f.CarLapCompleted[car] > 0 ? f.CarLapCompleted[car].ToString(CultureInfo.InvariantCulture) : RaceFormat.Missing, pal.Muted));
            case "irchange":
                return new Column(id, "iR ±", "+000", HAlign.Right, car => r.IRatingChangeOf(car) is not { } c ? None
                    : Math.Round(c) == 0 ? new Cell("0", pal.Muted) : new Cell((c > 0 ? "+" : "−") + Math.Abs(Math.Round(c)), c > 0 ? pal.GoodText : pal.DangerText));
            case "last":
                return new Column(id, "LAST", Time(83.456), HAlign.Right, car =>
                    new Cell(Time(f.CarLastLap[car] > 0 ? f.CarLastLap[car] : null), Text(car)));
            case "best":
                return new Column(id, "BEST", Time(83.456), HAlign.Right, car =>
                {
                    double t = f.CarBestLap[car];
                    bool fastest = t > 0 && FieldOrder.FastestLap(f, D(car)?.ClassId) is { } fl && fl.Car == car;
                    return new Cell(Time(t > 0 ? t : null), fastest ? pal.Fastest : Text(car), fastest);
                });
            case "delta":
                return new Column(id, "Δ LAST", "+0.00", HAlign.Right, car =>
                {
                    int me = f.PlayerCarIdx;
                    if (car == me || (uint)me >= TelemetryFrame.MaxCars || f.CarLastLap[car] <= 0 || f.CarLastLap[me] <= 0) return None;
                    double d = f.CarLastLap[car] - f.CarLastLap[me];
                    return new Cell(RaceFormat.Gap(d, 2), d > 0 ? pal.GoodText : pal.DangerText);
                });
            case "pit":
                string pitShow = Choice(PitShow);
                bool pitTime = Flag(PitTime);
                return new Column(id, "PIT", pitTime ? "PIT 00" : "L00", HAlign.Center, car =>
                {
                    if (f.CarOnPitRoad[car])
                        return new Cell(pitTime && r.PitLaneTime(car) is { } inLane ? $"PIT {inLane:0}" : "PIT", pal.Text, Pill: pal.Warn);
                    if (r.OnOutLap(car)) return new Cell("OUT", pal.Text, Pill: pal.Slate);
                    return pitShow switch
                    {
                        "stops" => r.StopsOf(car) is > 0 and var n ? new Cell(n + "×", pal.Muted) : None,
                        "since" => r.LastPitLap(car) is not null ? new Cell(r.StintLapsOf(car) + " L", pal.Muted) : None,
                        _ => r.LastPitLap(car) is { } lap ? new Cell("L" + lap.ToString(CultureInfo.InvariantCulture), pal.Muted) : None,
                    };
                });
            case "stint": return new Column(id, "STINT", "00 L", HAlign.Right, car => new Cell(r.StintLapsOf(car) + " L", pal.Muted));
            case "out": return new Column(id, "", "OUT", HAlign.Center, car => r.OnOutLap(car) ? new Cell("OUT", pal.Text, Pill: pal.Slate) : None);
            case "inc": return new Column(id, "INC", "00x", HAlign.Right, car => D(car)?.Incidents is { } i ? new Cell(i + "x", pal.Muted) : None);
            case "trend":
                return new Column(id, "", "▼", HAlign.Center, car =>
                {
                    if (car == f.PlayerCarIdx || r.Gaps.Trend(car) is not { } t || Math.Abs(t) < 0.01) return None;
                    bool ahead = FieldOrder.GapSeconds(f, f.PlayerCarIdx, car, r.Profile) is > 0;
                    return t < 0 ? new Cell("▼", ahead ? pal.GoodText : pal.WarnText) : new Cell("▲", pal.Faint);
                });
            case "gap": return new Column(id, gapHeader, "+0:00.0", HAlign.Right, gap);
            case "laps":
                return new Column(id, "LAP", "+0L", HAlign.Right, car =>
                {
                    int me = f.PlayerCarIdx;
                    if (car == me || (uint)me >= TelemetryFrame.MaxCars || FieldOrder.Distance(f, car) is not { } dc || FieldOrder.Distance(f, me) is not { } dm) return None;
                    int diff = (int)Math.Round(dc - dm - FieldOrder.Wrap(f.CarLapDistPct[car], f.CarLapDistPct[me]));
                    return diff == 0 ? None : new Cell((diff > 0 ? "+" : "−") + Math.Abs(diff) + "L", diff > 0 ? pal.DangerText : pal.Slate);
                });
        }
        return null;
    }
}

internal sealed class RelativeWidget : HudWidget
{
    private readonly List<(int Car, RectangleF Row)> _hits = new();

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Int("ahead", "Cars ahead", 4, 0, 10),
        HudOption.Int("behind", "Cars behind", 4, 0, 10),
        HudOption.Columns("columns", "Columns", "cpos,num,name,pit,badge,gap",
            FieldColumns.Pos, FieldColumns.ClassPos, FieldColumns.Change, FieldColumns.Number, FieldColumns.Name, FieldColumns.Team,
            FieldColumns.Badge, FieldColumns.IRatingChange, FieldColumns.Last, FieldColumns.Best, FieldColumns.AvgLap,
            FieldColumns.PaceDelta, FieldColumns.Laps, FieldColumns.LapsDone, FieldColumns.Pit, FieldColumns.OutLap, FieldColumns.Stint, FieldColumns.Incidents, FieldColumns.Trend,
            new HudChoice("gap", "Gap (seconds)")),
        HudOption.Choice("classes", "Classes", "all", new HudChoice("all", "Every class"), new HudChoice("mine", "My class only")) with { Group = HudOptionGroup.Filter },
        HudOption.Classes("hideClasses", "Hide classes", "Classes left out of the list (they're still counted)."),
        HudOption.Choice("hidePits", "Cars in the pits", "false", new HudChoice("false", "Show them"),
            new HudChoice("stall", "Hide cars parked in their pit stall", "Cars driving down the pit lane still show"), new HudChoice("true", "Hide every car in the pit lane")) with { Group = HudOptionGroup.Filter },
        HudOption.Number("close", "Close gap", 1.0, 0, 5, 0.1, "s", "Same-class cars within this gap are highlighted (battles)."),
        HudOption.Bool("lapColors", "Color cars lapping / being lapped", true, "Red: lapping you. Blue: a lap or more behind you"),
        FieldColumns.MarksOption,
        HudOption.Columns("header", "Header", "session,remain,inc,bias", Strips.Header),
        HudOption.Columns("footer", "Footer", "lap,clock,wetness,air,track", Strips.Footer),
        HudOption.Bool("showHeader", "Column headings", false),
        HudOption.Bool("highlightMe", "Highlight my row", true, "Your row tinted amber, your position in a block"),
        HudOption.Bool("stripes", "Row shading", true, "Every other row a shade darker"),
        HudFlags.BannerOption,
    }.Concat(FieldColumns.Shared).ToArray();

    private PointF _inset;

    public override void Draw(HudPainter p, HudView v)
    {
        string banner = v.Options.Choice("flagBanner");
        var (l, t, r, b) = HudFlags.Room(banner);
        float spare = HudTable.FastestRoom(p, v, 1.42f);
        r += spare;
        _inset = new PointF(l, t);
        p.Inset(l, t, r, b);
        try { DrawTable(p, v); }
        finally
        {
            var outer = p.EndInset();
            var kind = v.Frame.Status == SimStatus.Connected ? HudFlags.BannerKind(v.Frame.Flags) : FlagKind.None;
            if (kind == FlagKind.None && p.IsPreview) kind = FlagKind.Yellow;
            HudFlags.DrawBanner(p, outer, kind, banner, spare);
        }
    }

    private void DrawTable(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Relative")) return;
        var f = v.Frame;
        var o = v.Options;
        _hits.Clear();
        float pad = 6 * p.Density, sep = p.PieceGap;
        var header = o.Columns("header");
        var footer = o.Columns("footer");
        float pill = p.PillHeight;
        float minRows = p.LineHeight(p.Body) * 1.42f * p.Density * 3;
        var fbox = p.Bounds;
        if (footer.Count > 0 && fbox.Height - pad * 2 - (pill + sep) * (header.Count > 0 ? 2 : 1) < minRows) footer = Array.Empty<string>();
        if (header.Count > 0 && fbox.Height - pad * 2 - pill - sep < minRows) header = Array.Empty<string>();
        if (header.Count > 0)
        {
            var hr = new RectangleF(fbox.X, fbox.Y, fbox.Width, pill);
            p.Panel(hr);
            Strips.HeaderLine(p, v, header, hr);
            fbox = new RectangleF(fbox.X, hr.Bottom + sep, fbox.Width, fbox.Height - pill - sep);
        }
        if (footer.Count > 0)
        {
            var fr = new RectangleF(fbox.X, fbox.Bottom - pill, fbox.Width, pill);
            p.Panel(fr);
            Strips.FooterLine(p, v, footer, fr);
            fbox = new RectangleF(fbox.X, fbox.Y, fbox.Width, fbox.Height - pill - sep);
        }
        p.Panel(fbox);
        var area = fbox.Inflated(-pad, -pad);
        var font = p.Body;
        float rowH = p.LineHeight(font) * 1.42f * p.Density;
        bool headings = o.Bool("showHeader");
        float headH = headings ? p.LineHeight(p.Tiny) + 4 : 0;
        int fit = Math.Max(1, (int)((area.Height - headH) / rowH));
        int ahead = o.Int("ahead"), behind = o.Int("behind");
        while (ahead + behind + 1 > fit && (ahead > 0 || behind > 0))
        {
            if (ahead >= behind && ahead > 0) ahead--;
            else behind--;
        }
        string pits = o.Choice("hidePits");
        int player = f.PlayerCarIdx;
        var rows = FieldOrder.Relative(f, ahead, behind, pits == "true",
            car => v.ClassShown(car) && !(pits == "stall" && car != player && f.CarSurface[car] == TrackSurface.InPitStall), v.Race.Profile);
        double close = o.Number("close");
        int me = f.PlayerCarIdx;
        var cols = new List<Column>();
        foreach (var id in o.Columns("columns"))
            if (FieldColumns.Make(id, p, v, car => GapCell(p, v, rows, car, close), "GAP") is { } c) cols.Add(c);
        var fitted = HudTable.Fit(p, cols, area.Width, font);
        var style = RowStyle.Of(v);
        float y = area.Y;
        if (headings) { HudTable.Header(p, fitted, new RectangleF(area.X, y, area.Width, headH)); y += headH; }
        int missingAbove = ahead - rows.Count(r => r.TrackDistance > 0);
        y += Math.Max(0, missingAbove) * rowH;
        bool lapColors = o.Bool("lapColors") && f.SessionKind == SessionKind.Race;
        bool highlight = o.Bool("highlightMe"), stripes = o.Bool("stripes");
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var box = new RectangleF(area.X, y, area.Width, rowH - p.RowGap);
            int emphasis = row.IsPlayer && highlight ? 1 : row.CarIdx == v.Race.SelectedCar ? 2 : 0;
            bool dim = f.CarOnPitRoad[row.CarIdx] && !row.IsPlayer;
            var cls = p.ClassColor(f.Session, FieldOrder.ClassOf(f, row.CarIdx));
            Color? tint = lapColors && row.LapDiff != 0 && !row.IsPlayer
                ? (row.LapDiff > 0 ? p.P.Lapping : p.P.Lapped) : null;
            HudTable.Row(p, fitted, box, row.CarIdx, cls, emphasis, dim, font, tint, stripes && i % 2 == 1, style);
            _hits.Add((row.CarIdx, box));
            y += rowH;
        }
        HudTable.Track(p, fitted, RectangleF.FromLTRB(area.X, area.Y, area.Right, Math.Max(y, area.Y + rowH)));
        if (rows.Count == 1 && ahead + behind > 0 && y + rowH <= area.Bottom + 1)
            p.Text("No other cars near you", p.Small, p.P.Faint, new RectangleF(area.X, y, area.Width, rowH), HAlign.Center);
        if (o.Bool("flagEdge")) HudTable.FlagEdge(p, f, o.Int("flagWidth"));
    }

    private static Cell GapCell(HudPainter p, HudView v, List<RelativeRow> rows, int car, double close)
    {
        var f = v.Frame;
        var None = Cell.Empty(p.P);
        if (car == f.PlayerCarIdx) return None;
        RelativeRow? row = null;
        foreach (var r in rows) if (r.CarIdx == car) { row = r; break; }
        if (row?.Gap is not { } gap) return new Cell(RaceFormat.Missing, p.P.Faint);
        bool sameClass = FieldOrder.ClassOf(f, car) == FieldOrder.ClassOf(f, f.PlayerCarIdx);
        bool battle = sameClass && Math.Abs(gap) <= close && row.Value.LapDiff == 0;
        return new Cell(RaceFormat.Abs(gap, v.Options.Int("decimals")), battle ? p.P.WarnText : p.P.Heading, true);
    }

    public static string Remaining(HudView v) => Strips.Remaining(v);

    public override bool Click(PointF at, HudView v)
    {
        at = new PointF(at.X - _inset.X, at.Y - _inset.Y);
        foreach (var (car, row) in _hits)
            if (row.Contains(at))
            {
                v.Race.SelectedCar = v.Race.SelectedCar == car || car == v.Frame.PlayerCarIdx ? -1 : car;
                return true;
            }
        return false;
    }
}

internal sealed class StandingsWidget : HudWidget
{
    private readonly List<(int Car, RectangleF Row)> _hits = new();

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Columns("columns", "Columns", "pos,num,name,pit,badge,irchange,gap,best,last",
            FieldColumns.Pos, FieldColumns.ClassPos, FieldColumns.Change, FieldColumns.Number, FieldColumns.Name, FieldColumns.Team,
            FieldColumns.Badge, FieldColumns.IRatingChange, new HudChoice("gap", "Gap"), new HudChoice("int", "Interval (car ahead)"),
            new HudChoice("gapme", "Gap to you"), FieldColumns.LapsDone, FieldColumns.Last, FieldColumns.Best, FieldColumns.AvgLap, FieldColumns.PaceDelta,
            FieldColumns.Pit, FieldColumns.OutLap, FieldColumns.Stint, FieldColumns.Incidents),
        HudOption.Bool("group", "Group by class", true, "Multiclass: one block per class, positions within the class."),
        HudOption.Choice("classOrder", "Class order", "position", new HudChoice("position", "By their leader's position"), new HudChoice("fastest", "Fastest class first")),
        HudOption.Set("classInfo", "Class headings show", "cars,sof", null,
            new HudChoice("cars", "Cars in the class"), new HudChoice("sof", "Strength of field"), new HudChoice("laps", "Laps the leader will do", "Timed races: from the class leader's recent pace")),
        HudOption.Choice("divider", "Break in the list", "line", new HudChoice("line", "A line"), new HudChoice("accent", "An accent line"), new HudChoice("none", "Nothing"))
            with { Help = "Where cars are left out (the list doesn't fit): between the leaders and the cars around you" },
        HudOption.Bool("title", "Title row", true, "Session name and time or laps left"),
        HudOption.Bool("progress", "Session progress bar", false, "Under the title row"),
        HudOption.Choice("gapTo", "Gap to", "leader", new HudChoice("leader", "Leader (class leader when grouped)"), new HudChoice("interval", "Car ahead"), new HudChoice("player", "You")),
        HudOption.Choice("classes", "Classes", "all", new HudChoice("all", "Every class"), new HudChoice("mine", "My class only")) with { Group = HudOptionGroup.Filter },
        HudOption.Classes("hideClasses", "Hide classes", "Classes left out of the list."),
        HudOption.Int("top", "Leaders always shown", 3, 1, 20, help: "When the list doesn't fit: this many from the top of each class, then the cars around you."),
        HudOption.Int("otherRows", "Cars per other class", 0, 0, 30, help: "Multiclass: at most this many cars of each class but yours (0 = as many as fit)."),
        FieldColumns.MarksOption,
        HudOption.Bool("retired", "Show cars that left", true, "Cars no longer in the session stay in the list, dimmed.") with { Group = HudOptionGroup.Filter },
        HudOption.Bool("showHeader", "Column headings", true),
        HudOption.Columns("header", "Header", "", Strips.Header),
        HudOption.Columns("footer", "Footer", "inc,sof,fastest", Strips.Footer),
        HudOption.Bool("highlightMe", "Highlight my row", true, "Your row tinted amber, your position in a block"),
        HudOption.Bool("stripes", "Row shading", true, "Every other row a shade darker"),
    }.Concat(FieldColumns.Shared).ToArray();

    public override void Draw(HudPainter p, HudView v)
    {
        p.Inset(0, 0, HudTable.FastestRoom(p, v, 1.38f), 0);
        try { DrawStandings(p, v); }
        finally { p.EndInset(); }
    }

    private void DrawStandings(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Standings")) return;
        var f = v.Frame;
        var o = v.Options;
        _hits.Clear();
        float pad = 6 * p.Density, sep = p.PieceGap, pill = p.PillHeight;
        float minRows = p.LineHeight(p.Body) * 1.38f * p.Density * 4;
        var box = p.Bounds;
        bool Room() => box.Height - pill - sep - pad * 2 > minRows;
        if (o.Bool("title"))
        {
            var tr = new RectangleF(box.X, box.Y, box.Width, pill);
            p.TitleBar(tr, v.Race.SessionLabel, RelativeWidget.Remaining(v));
            if (o.Bool("progress") && Progress(f) is { } done)
                p.Bar(new RectangleF(tr.X + 8 * p.Density, tr.Bottom - 5 * p.Density, tr.Width - 16 * p.Density, 3 * p.Density), done, p.P.Accent, HudPalette.Alpha(p.P.LineStrong, 90));
            box = new RectangleF(box.X, tr.Bottom + sep, box.Width, box.Height - pill - sep);
        }
        var headerItems = o.Columns("header");
        if (headerItems.Count > 0 && Room())
        {
            var hr = new RectangleF(box.X, box.Y, box.Width, pill);
            p.Panel(hr);
            Strips.HeaderLine(p, v, headerItems, hr);
            box = new RectangleF(box.X, hr.Bottom + sep, box.Width, box.Height - pill - sep);
        }
        var footerItems = o.Columns("footer");
        if (footerItems.Count > 0 && Room())
        {
            var fr = new RectangleF(box.X, box.Bottom - pill, box.Width, pill);
            p.Panel(fr);
            Strips.FooterLine(p, v, footerItems, fr);
            box = new RectangleF(box.X, box.Y, box.Width, box.Height - pill - sep);
        }
        bool split = o.Bool("group") && f.Session.NumCarClasses > 1;
        if (!split) p.Panel(box);
        var area = split ? new RectangleF(box.X + pad, box.Y, box.Width - pad * 2, box.Height) : box.Inflated(-pad, -pad);
        bool race = f.SessionKind == SessionKind.Race;
        bool multi = f.Session.NumCarClasses > 1;
        bool group = o.Bool("group") && multi;
        string gapTo = o.Choice("gapTo");
        int me = f.PlayerCarIdx;
        var standings = v.Race.Standings.Where(s => v.ClassShown(s.CarIdx) && (o.Bool("retired") || f.CarActive(s.CarIdx))).ToList();
        var None = Cell.Empty(p.P);

        Cell Gap(int car)
        {
            var s = v.Race.StandingOf(car);
            if (s is not { } st) return None;
            int decimals = o.Int("decimals");
            double? g;
            int laps;
            if (gapTo == "player")
            {
                if (car == me || v.Race.StandingOf(me) is null) return None;
                bool aheadOfMe = IndexOf(v, car) < IndexOf(v, me);
                var (gg, l) = aheadOfMe ? FieldOrder.Behind(f, me, car, race) : FieldOrder.Behind(f, car, me, race);
                g = gg is { } x ? (aheadOfMe ? -x : x) : null;
                if (l > 0) return new Cell((aheadOfMe ? "−" : "+") + l + "L", p.P.Slate);
                laps = 0;
            }
            else if (gapTo == "interval") { g = group ? st.ClassInterval : st.Interval; laps = 0; }
            else { g = group ? st.GapToClassLeader : st.GapToLeader; laps = group ? st.ClassLapsDown : st.LapsDown; }
            bool leader = gapTo == "leader" && (group ? st.ClassPosition == 1 : st.Position == 1);
            if (leader) return new Cell(race ? "Leader" : RaceFormat.LapTime(f.CarBestLap[car] > 0 ? f.CarBestLap[car] : null), p.P.Muted);
            if (laps > 0 && gapTo != "interval") return new Cell($"+{laps}L", p.P.Slate);
            return new Cell(g is null ? RaceFormat.Missing : gapTo == "player" ? RaceFormat.Gap(g, Math.Max(1, decimals)) : "+" + RaceFormat.Abs(g, decimals), p.P.Text);
        }

        var cols = new List<Column>();
        foreach (var id in o.Columns("columns"))
        {
            Column? c = id switch
            {
                "int" => new Column(id, "INT", "+00.0", HAlign.Right, car => v.Race.StandingOf(car) is { } s && (group ? s.ClassInterval : s.Interval) is { } i
                    ? new Cell("+" + RaceFormat.Abs(i, o.Int("decimals")), p.P.Muted) : None),
                "gapme" => new Column(id, "YOU", "+00.0", HAlign.Right, car => car == me || (uint)me >= TelemetryFrame.MaxCars ? None
                    : FieldOrder.GapSeconds(f, me, car, v.Race.Profile) is { } g2 ? new Cell(RaceFormat.Gap(-g2), p.P.Muted) : new Cell(RaceFormat.Missing, p.P.Faint)),
                _ => FieldColumns.Make(id, p, v, Gap, gapTo == "interval" ? "INT" : gapTo == "player" ? "YOU" : "GAP"),
            };
            if (c is not null) cols.Add(c);
        }
        var font = p.Body;
        var fitted = HudTable.Fit(p, cols, area.Width, font);
        var style = RowStyle.Of(v);
        float rowH = p.LineHeight(font) * 1.38f * p.Density;
        float y = area.Y;
        float headH = o.Bool("showHeader") ? p.LineHeight(p.Tiny) + 4 : 0;
        if (headH > 0 && !group)
        {
            HudTable.Header(p, fitted, new RectangleF(area.X, y, area.Width, headH));
            y += headH;
        }
        float blockGap = p.PieceGap, blockPad = 5 * p.Density;
        bool fastestFirst = o.Choice("classOrder") == "fastest";
        var blocks = group
            ? standings.GroupBy(s => FieldOrder.ClassOf(f, s.CarIdx))
                .OrderBy(g => fastestFirst ? f.Session.ByCar.GetValueOrDefault(g.First().CarIdx)?.ClassEstLapTime ?? double.MaxValue : 0)
                .ThenBy(g => g.Min(s => s.Position > 0 ? s.Position : 999)).Select(g => g.ToList()).ToList()
            : new List<List<StandingRow>> { standings };
        float classHead = group ? p.LineHeight(p.SmallBold) + 9 * p.Density : 0;
        float perBlock = group ? classHead + blockGap + blockPad * 2 : 0;
        int otherRows = o.Int("otherRows");
        var pickFrom = group && otherRows > 0
            ? blocks.Select(b => b.Any(s => s.CarIdx == me) ? b : b.Take(otherRows).ToList()).ToList()
            : blocks;
        float breaks = o.Choice("divider") == "none" ? 0 : BreakRoom(p) * blocks.Count;
        int fit = Math.Max(1, (int)((area.Bottom - y - perBlock * blocks.Count - (group ? headH : 0) - breaks) / rowH));
        var shown = Pick(pickFrom, fit, o.Int("top"), me);
        if (group)
        {
            int empty = shown.Count(s => s.Count == 0);
            if (empty > 0) shown = Pick(pickFrom, Math.Max(1, (int)((area.Bottom - y - perBlock * (blocks.Count - empty) - (group ? headH : 0) - breaks) / rowH)), o.Int("top"), me);
        }
        bool highlight = o.Bool("highlightMe"), stripes = o.Bool("stripes");
        int drawn = 0;
        bool firstBlock = true;
        for (int b = 0; b < blocks.Count; b++)
        {
            if (shown[b].Count == 0) continue;
            if (group)
            {
                var first = blocks[b][0];
                var d = f.Session.ByCar.GetValueOrDefault(first.CarIdx);
                var cc = p.ClassColor(f.Session, d?.ClassId ?? 0);
                float rowsH = (firstBlock ? headH : 0);
                int lastI = -1;
                foreach (var (_, idx) in shown[b])
                {
                    if (lastI >= 0 && idx > lastI + 1 && o.Choice("divider") != "none") rowsH += BreakRoom(p);
                    rowsH += rowH;
                    lastI = idx;
                }
                rowsH = Math.Min(rowsH, area.Bottom - y - classHead - blockPad * 2);
                p.Panel(new RectangleF(box.X, y, box.Width, classHead + rowsH + blockPad * 2));
                y += blockPad;
                var hr = new RectangleF(area.X, y + 2, area.Width, classHead - 4);
                float radius = p.CornerRadius(2);
                string name = (d?.ClassName is { Length: > 0 } cn ? cn : "Class").ToUpperInvariant();
                var tag = new RectangleF(hr.X, hr.Y, Math.Min(hr.Width * 0.6f, p.Measure(name, p.SmallBold) + 16), hr.Height);
                var fade = new RectangleF(tag.X, tag.Y, tag.Width * 2.2f, tag.Height);
                using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(fade, HudPalette.Alpha(cc, 95), HudPalette.Alpha(cc, 0), System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                    p.G.FillRectangle(lg, p.Snap(fade));
                p.Fill(new RectangleF(tag.X, tag.Y, 3, tag.Height), cc);
                p.Text(name, p.SmallBold, p.P.Heading, new RectangleF(tag.X + 3, tag.Y, tag.Width - 3, tag.Height), HAlign.Center, VAlign.Center);
                p.Text(ClassInfo(v, blocks[b]), p.Small, p.P.Text, new RectangleF(tag.Right + 6, hr.Y, hr.Right - tag.Right - 12, hr.Height), HAlign.Right, VAlign.Center);
                y += classHead;
                if (firstBlock && headH > 0)
                {
                    HudTable.Header(p, fitted, new RectangleF(area.X, y, area.Width, headH));
                    y += headH;
                }
                firstBlock = false;
            }
            int last = -1;
            for (int i = 0; i < shown[b].Count; i++)
            {
                var (row, index) = shown[b][i];
                if (last >= 0 && index > last + 1 && o.Choice("divider") is var divider && divider != "none")
                {
                    float room = BreakRoom(p);
                    if (y + room + rowH > area.Bottom + 1) break;
                    var color = divider == "accent" ? p.P.Accent : p.P.LineStrong;
                    float h = divider == "accent" ? 2 * p.Density : Math.Max(1, p.Density);
                    float inset = divider == "accent" ? 0 : 12 * p.Density;
                    p.Line(area.X + inset, y + room / 2, area.Right - inset, y + room / 2, color, h);
                    y += room;
                }
                last = index;
                if (y + rowH > area.Bottom + 1) break;
                var rowBox = new RectangleF(area.X, y, area.Width, rowH - p.RowGap);
                int car = row.CarIdx;
                int emphasis = car == me && highlight ? 1 : car == v.Race.SelectedCar ? 2 : 0;
                bool dim = !f.CarActive(car) || f.CarOnPitRoad[car];
                Color? tint = null;
                if (race && car != me && v.Race.StandingOf(me) is { } mine && FieldOrder.ClassOf(f, car) == FieldOrder.ClassOf(f, me))
                    tint = row.LapsDown < mine.LapsDown ? p.P.Lapping : row.LapsDown > mine.LapsDown ? p.P.Lapped : null;
                HudTable.Row(p, fitted, rowBox, car, p.ClassColor(f.Session, FieldOrder.ClassOf(f, car)), emphasis, dim, font, tint, stripes && drawn++ % 2 == 1, style);
                _hits.Add((car, rowBox));
                y += rowH;
            }
            if (group) y += blockPad + blockGap;
        }
        HudTable.Track(p, fitted, RectangleF.FromLTRB(area.X, area.Y, area.Right, Math.Max(y, area.Y + rowH)));
        if (o.Bool("flagEdge")) HudTable.FlagEdge(p, f, o.Int("flagWidth"));
    }

    private static double? Progress(TelemetryFrame f)
    {
        if (f.CurrentSession?.Laps is > 0 && f.SessionLapsTotal is > 0 and var total && f.SessionLapsRemain is { } left) return Math.Clamp(1 - (double)left / total, 0, 1);
        if (f.CurrentSession?.TimeSeconds is not null && f.SessionTimeTotal is > 0 and var all && f.SessionTimeRemain is { } rest) return Math.Clamp(1 - rest / all, 0, 1);
        return null;
    }

    private static string ClassInfo(HudView v, List<StandingRow> block)
    {
        var f = v.Frame;
        var parts = new List<string>(3);
        var show = v.Options.Columns("classInfo");
        int classId = FieldOrder.ClassOf(f, block[0].CarIdx);
        foreach (var id in show)
            switch (id)
            {
                case "cars": parts.Add($"{block.Count} car{(block.Count == 1 ? "" : "s")}"); break;
                case "sof": if (Strips.StrengthOfField(f.Session, classId) is { } sof) parts.Add("SOF " + RaceFormat.IRating(sof)); break;
                case "laps":
                    int leader = block[0].CarIdx;
                    if (f.CurrentSession?.Laps is not > 0 && f.SessionTimeRemain is { } left && v.Race.PaceOf(leader) is { } pace && pace > 0)
                        parts.Add($"~{f.CarLapCompleted[leader] + Math.Ceiling(left / pace) + 1:0} laps");
                    break;
            }
        return string.Join(" · ", parts);
    }

    private static int IndexOf(HudView v, int car)
    {
        var list = v.Race.Standings;
        for (int i = 0; i < list.Count; i++) if (list[i].CarIdx == car) return i;
        return int.MaxValue;
    }

    private static float BreakRoom(HudPainter p) => 7 * p.Density;

    public static List<List<(StandingRow Row, int Index)>> Pick(List<List<StandingRow>> blocks, int fit, int top, int player)
    {
        var result = blocks.Select(_ => new List<(StandingRow, int)>()).ToList();
        int total = blocks.Sum(b => b.Count);
        if (total <= fit)
        {
            for (int b = 0; b < blocks.Count; b++) result[b].AddRange(blocks[b].Select((r, i) => (r, i)));
            return result;
        }
        int playerBlock = blocks.FindIndex(b => b.Any(r => r.CarIdx == player));
        var shares = new int[blocks.Count];
        int left = fit;
        if (playerBlock >= 0)
        {
            int count = blocks[playerBlock].Count;
            shares[playerBlock] = Math.Min(left, Math.Min(count, Math.Max(top + 3, (int)Math.Round((double)fit * count / total))));
            left -= shares[playerBlock];
        }
        int others = blocks.Where((_, b) => b != playerBlock).Sum(b => b.Count);
        int rest = fit - (playerBlock >= 0 ? shares[playerBlock] : 0);
        for (int b = 0; b < blocks.Count && left > 0; b++)
        {
            if (b == playerBlock) continue;
            int want = others == 0 ? 0 : Math.Max(Math.Min(top, blocks[b].Count), (int)Math.Floor((double)rest * blocks[b].Count / others));
            shares[b] = Math.Min(left, Math.Min(blocks[b].Count, want));
            left -= shares[b];
        }
        for (int b = 0; b < blocks.Count; b++)
        {
            var block = blocks[b];
            int share = shares[b];
            var take = new SortedSet<int>();
            for (int i = 0; i < Math.Min(top, block.Count) && take.Count < share; i++) take.Add(i);
            if (b == playerBlock)
            {
                int at = block.FindIndex(r => r.CarIdx == player);
                if (share > 0 && take.Count >= share && !take.Contains(at)) take.Remove(take.Max);
                for (int d = 0; take.Count < share && (at - d >= 0 || at + d < block.Count); d++)
                {
                    if (at - d >= 0) take.Add(at - d);
                    if (take.Count < share && at + d < block.Count) take.Add(at + d);
                }
            }
            for (int i = 0; take.Count < share && i < block.Count; i++) take.Add(i);
            foreach (int i in take) result[b].Add((block[i], i));
        }
        return result;
    }
    public override bool Click(PointF at, HudView v)
    {
        foreach (var (car, row) in _hits)
            if (row.Contains(at))
            {
                v.Race.SelectedCar = v.Race.SelectedCar == car || car == v.Frame.PlayerCarIdx ? -1 : car;
                return true;
            }
        return false;
    }
}

internal static class Strips
{
    public static readonly HudChoice[] Items =
    {
        new("session", "Session"), new("remain", "Time left"), new("lapsleft", "Laps left", "Timed races: estimated from the pace"), new("pos", "Your position"), new("cpos", "Your class position"),
        new("leader", "Gap to the leader"), new("ahead", "Nearest ahead"), new("behind", "Nearest behind"),
        new("lap", "Your lap"), new("last", "Your last lap"), new("best", "Your best lap"), new("delta", "Delta to your best", "Live, iRacing's own"),
        new("fastest", "Fastest lap"), new("classFastest", "Fastest lap in your class"),
        new("fuel", "Laps of fuel"), new("inc", "Your incidents"),
        new("sof", "Strength of field", "The field's iRating, as iRacing works it out"), new("csof", "Your class's strength of field"), new("cars", "Cars (in your class / all)"),
        new("air", "Air temperature"), new("track", "Track temperature"), new("wind", "Wind (speed and where it blows)"), new("humidity", "Humidity"), new("wetness", "Track wetness"),
        new("clock", "Clock (this PC)"),
        new("bias", "Brake bias"), new("trackname", "Track"), new("fuellevel", "Fuel in the tank"), new("rain", "Rain"),
        new("classpos", "Your place in class (P3 / 14)"),
    };

    public static readonly HudChoice[] Header = Items, Footer = Items;

    public static void HeaderLine(HudPainter p, HudView v, IReadOnlyList<string> fields, RectangleF r) => Line(p, v, fields, r, "header");

    public static void FooterLine(HudPainter p, HudView v, IReadOnlyList<string> fields, RectangleF r) => Line(p, v, fields, r, "footer");

    private static void Line(HudPainter p, HudView v, IReadOnlyList<string> fields, RectangleF r, string key)
    {
        var f = v.Frame;
        var u = v.Units;
        var items = new List<(string, string, Color?)>(fields.Count);
        var ids = new List<string>(fields.Count);
        int me = f.PlayerCarIdx;
        bool mine = (uint)me < TelemetryFrame.MaxCars;
        string missing = RaceFormat.Missing;
        foreach (var id in fields)
        {
            int before = items.Count;
            switch (id)
            {
                case "session": items.Add(("", v.Race.SessionLabel, p.P.AccentText)); break;
                case "remain": items.Add(("TIME LEFT", TimeLeft(v) ?? missing, null)); break;
                case "lapsleft": items.Add(("LAPS LEFT", LapsLeft(v) ?? missing, null)); break;
                case "pos": items.Add(("POS", mine && f.CarPosition[me] > 0 ? RaceFormat.Pos(f.CarPosition[me]) : missing, null)); break;
                case "cpos":
                    items.Add(((f.Session.Player?.ClassName is { Length: > 0 } cn ? cn : "CLASS").ToUpperInvariant(),
                        mine && v.Race.StandingOf(me) is { ClassPosition: > 0 } s ? "P" + s.ClassPosition : missing, null));
                    break;
                case "leader":
                    var standing = mine ? v.Race.StandingOf(me) : null;
                    items.Add(("LEADER", standing is not { } st ? missing : st.Position == 1 ? "You" : st.LapsDown > 0 ? $"+{st.LapsDown}L" : RaceFormat.Gap(st.GapToLeader), null));
                    break;
                case "ahead":
                case "behind":
                    var rows = FieldOrder.Relative(f, id == "ahead" ? 1 : 0, id == "behind" ? 1 : 0, profile: v.Race.Profile);
                    var near = rows.FirstOrDefault(x => !x.IsPlayer);
                    string arrow = id == "ahead" ? "▲" : "▼";
                    items.Add(!near.IsPlayer && near.Gap is { } g && rows.Count > 1 ? (arrow, $"{RaceFormat.Abs(g)} {Short(f, near.CarIdx)}", null) : (arrow, missing, p.P.Faint));
                    break;
                case "lap":
                    items.Add(("LAP", f.Lap is > 0 and var lap ? lap.ToString(CultureInfo.InvariantCulture) + (f.SessionLapsTotal is { } total && f.CurrentSession?.Laps is > 0 ? $" / {total}" : "") : missing, null));
                    break;
                case "last": items.Add(("LAST", f.LapLast is { } last ? RaceFormat.LapTime(last) : missing, null)); break;
                case "best": items.Add(("BEST", f.LapBest is { } best ? RaceFormat.LapTime(best) : missing, null)); break;
                case "delta":
                    items.Add(f.DeltaBestOk && f.DeltaBest is { } d
                        ? ("Δ", RaceFormat.Delta(d), d < 0 ? p.P.GoodText : d > 0 ? p.P.DangerText : null)
                        : ("Δ", missing, p.P.Faint));
                    break;
                case "fastest":
                    items.Add(FieldOrder.FastestLap(f) is { } fl ? ("FL", $"{RaceFormat.LapTime(fl.Time)} {Short(f, fl.Car)}", p.P.Fastest) : ("FL", missing, p.P.Faint));
                    break;
                case "classFastest":
                    string classFl = (f.Session.Player?.ClassName is { Length: > 0 } pcn ? pcn.ToUpperInvariant() : "CLASS") + " FL";
                    items.Add(f.Session.Player is { } pl && FieldOrder.FastestLap(f, pl.ClassId) is { } cfl ? (classFl, RaceFormat.LapTime(cfl.Time), p.P.Fastest) : (classFl, missing, p.P.Faint));
                    break;
                case "fuel": items.Add(("FUEL", v.Race.Fuel is { } plan ? $"{plan.LapsOfFuel:0.0} laps" : missing, null)); break;
                case "inc":
                    if (f.Incidents is { } inc)
                    {
                        bool close = f.Session.IncidentLimit is { } l && inc >= l - v.Race.Alerts.Threshold("incidents.limit");
                        items.Add(("INC", $"{inc}x" + (f.Session.IncidentLimit is { } lim ? $" / {lim}x" : ""), close ? p.P.DangerText : null));
                    }
                    else items.Add(("INC", missing, p.P.Faint));
                    break;
                case "sof": items.Add(("SOF", RaceFormat.IRating(StrengthOfField(f.Session, null)), null)); break;
                case "csof":
                    items.Add(((f.Session.Player?.ClassName is { Length: > 0 } scn ? scn.ToUpperInvariant() + " " : "") + "SOF",
                        f.Session.Player is { } sp ? RaceFormat.IRating(StrengthOfField(f.Session, sp.ClassId)) : missing, null));
                    break;
                case "cars":
                    int all = 0, same = 0, myClass = f.Session.Player?.ClassId ?? int.MinValue;
                    foreach (var dr in f.Session.Drivers)
                        if (dr.Races) { all++; if (dr.ClassId == myClass) same++; }
                    items.Add(("CARS", all == 0 ? missing : f.Session.NumCarClasses > 1 && same > 0 ? $"{same} / {all}" : all.ToString(CultureInfo.InvariantCulture), null));
                    break;
                case "air": items.Add(("AIR", f.AirTemp is { } air ? RaceFormat.Temp(air, u) : missing, null)); break;
                case "track": items.Add(("TRACK", f.TrackTemp is { } tt ? RaceFormat.Temp(tt, u) : missing, null)); break;
                case "wind":
                    double? to = f.WindDir is { } from ? from + Math.PI : null;
                    items.Add(("WIND", f.WindSpeed is { } ws ? $"{RaceFormat.SpeedValue(ws, u):0} {RaceFormat.SpeedUnit(u)} {RaceFormat.Compass(to)}" : missing, null));
                    break;
                case "humidity": items.Add(("HUMIDITY", f.Humidity is { } h ? $"{h * 100:0}%" : missing, null)); break;
                case "wetness": items.Add(("SURFACE", f.TrackWetness is { } w ? RaceFormat.Wetness(w) : missing, f.TrackWetness is > 1 ? p.P.WarnText : null)); break;
                case "clock": items.Add(("", RaceFormat.TimeOfDay(DateTime.Now), null)); break;
                case "bias": items.Add(("BB", f.BrakeBias is { } bb ? $"{bb:0.0}%" : missing, null)); break;
                case "trackname": items.Add(("", f.Session.TrackDisplayName is { Length: > 0 } tn ? tn : f.Session.TrackName is { Length: > 0 } tn2 ? tn2 : missing, null)); break;
                case "fuellevel": items.Add(("FUEL", f.Fuel is { } inTank ? RaceFormat.Fuel(inTank, u) : missing, null)); break;
                case "rain": items.Add(("RAIN", f.Precipitation is { } pr ? $"{pr * 100:0}%" : missing, f.Precipitation is > 0 ? p.P.AccentText : null)); break;
                case "classpos":
                    int inClass = 0, myCls = f.Session.Player?.ClassId ?? int.MinValue;
                    foreach (var dr in f.Session.Drivers) if (dr.Races && dr.ClassId == myCls) inClass++;
                    items.Add(("CLASS", mine && v.Race.StandingOf(me) is { ClassPosition: > 0 } cs ? $"P{cs.ClassPosition} / {inClass}" : missing, null));
                    break;
            }
            if (items.Count > before) ids.Add(id);
        }
        p.Strip(r, items, key, ids);
    }

    internal static int? StrengthOfField(SessionInfo session, int? classId)
    {
        const double K = 1600 / 0.69314718055994531;
        double sum = 0;
        int n = 0;
        foreach (var d in session.Drivers)
        {
            if (!d.Races || d.IRating is not > 0 || classId is { } c && d.ClassId != c) continue;
            sum += Math.Exp(-d.IRating.Value / K);
            n++;
        }
        return n == 0 ? null : (int)Math.Round(K * Math.Log(n / sum));
    }

    public static string Remaining(HudView v)
    {
        var f = v.Frame;
        if (f.CurrentSession?.Laps is > 0 && f.SessionLapsRemain is { } laps) return laps <= 0 ? "Final lap" : $"{laps} laps left";
        if (f.SessionTimeRemain is { } t && f.CurrentSession?.TimeSeconds is not null) return RaceFormat.Clock(t) + " left";
        return f.SessionTime is { } el ? RaceFormat.Clock(el) : "";
    }

    public static string? TimeLeft(HudView v)
    {
        var f = v.Frame;
        if (f.CurrentSession is not { } session) return null;
        if (session.TimeSeconds is not null && f.SessionTimeRemain is { } t) return RaceFormat.Clock(t);
        return session.Laps is > 0 ? null : "Unlimited";
    }

    public static string? LapsLeft(HudView v)
    {
        var f = v.Frame;
        if (f.CurrentSession?.Laps is > 0 && f.SessionLapsRemain is { } laps) return laps <= 0 ? "Final" : laps.ToString(CultureInfo.InvariantCulture);
        return v.Race.LapsToGo(f) is { } togo ? "~" + Math.Ceiling(togo).ToString(CultureInfo.InvariantCulture) : null;
    }

    public static string Short(TelemetryFrame f, int car) =>
        f.Session.ByCar.TryGetValue(car, out var d) ? (d.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? d.Name) : "";
}

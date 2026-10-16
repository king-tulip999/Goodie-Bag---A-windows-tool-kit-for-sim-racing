using System.Drawing;
using System.Drawing.Drawing2D;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Hud;

internal static class HudFlags
{
    public static bool HasPicture(FlagKind k) => k is FlagKind.Disqualified or FlagKind.Black or FlagKind.Meatball or FlagKind.Red
        or FlagKind.Caution or FlagKind.Yellow or FlagKind.Debris or FlagKind.Blue or FlagKind.White or FlagKind.Checkered or FlagKind.Green;

    private static readonly Color Stripe = Color.FromArgb(0xFF, 0xE0, 0x00), Meatball = Color.FromArgb(0xF7, 0x8A, 0x1D);

    public static void Draw(HudPainter p, RectangleF r, FlagKind kind)
    {
        r = p.Snap(r);
        var g = p.G;
        switch (kind)
        {
            case FlagKind.Green: p.Fill(r, HudPalette.FlagGreen); break;
            case FlagKind.Yellow or FlagKind.Caution: p.Fill(r, HudPalette.FlagYellow); break;
            case FlagKind.Red: p.Fill(r, HudPalette.FlagRed); break;
            case FlagKind.White: p.Fill(r, HudPalette.FlagWhite); break;
            case FlagKind.Black: p.Fill(r, HudPalette.FlagBlack); break;
            case FlagKind.Meatball:
            {
                p.Fill(r, HudPalette.FlagBlack);
                float d = r.Height * 0.62f;
                p.Dot(r.X + r.Width / 2, r.Y + r.Height / 2, d / 2, Meatball);
                break;
            }
            case FlagKind.Blue or FlagKind.Disqualified:
            {
                p.Fill(r, kind == FlagKind.Blue ? HudPalette.FlagBlue : HudPalette.FlagBlack);
                var state = g.Save();
                g.SetClip(r);
                float w = r.Height * (kind == FlagKind.Blue ? 0.26f : 0.18f);
                var pen = p.Pen(kind == FlagKind.Blue ? Stripe : HudPalette.FlagWhite, w);
                g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Top);
                if (kind == FlagKind.Disqualified) g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom);
                g.Restore(state);
                break;
            }
            case FlagKind.Debris:
            {
                const int stripes = 6;
                float sw = r.Width / stripes;
                for (int i = 0; i < stripes; i++)
                    p.Fill(new RectangleF(r.X + i * sw, r.Y, sw + (i < stripes - 1 ? 0.5f : 0), r.Height), i % 2 == 0 ? HudPalette.FlagYellow : HudPalette.FlagRed);
                break;
            }
            case FlagKind.Checkered:
                Checks(p, r, Math.Max(2, (int)Math.Round(r.Height / 4)));
                break;
            default: return;
        }
        bool dark = kind is FlagKind.Black or FlagKind.Meatball or FlagKind.Disqualified;
        p.Outline(r, dark ? HudPalette.Alpha(HudPalette.FlagWhite, 120) : HudPalette.Alpha(HudPalette.FlagBlack, 150), 1);
    }

    private static void Checks(HudPainter p, RectangleF r, int rows)
    {
        p.Fill(r, HudPalette.FlagWhite);
        float cell = r.Height / rows;
        int cols = Math.Max(1, (int)Math.Round(r.Width / cell));
        float cw = r.Width / cols;
        for (int y = 0; y < rows; y++)
            for (int x = y % 2; x < cols; x += 2)
                p.Fill(new RectangleF(r.X + x * cw, r.Y + y * cell, cw, cell), HudPalette.FlagBlack);
    }

    public static FlagKind BannerKind(RaceFlags flags)
    {
        var k = RaceControl.Top(flags);
        if (k == FlagKind.Green && !flags.HasFlag(RaceFlags.StartGo)) return FlagKind.None;
        return HasPicture(k) ? k : FlagKind.None;
    }

    public static readonly HudOption BannerOption = HudOption.Choice("flagBanner", "Flag banner", "off",
        new HudChoice("off", "Off"), new HudChoice("top", "On")) with
    {
        Help = "A bar above the widget with the flag and its name while a flag is out. Room for it is kept, so the table never moves.",
    };

    private const float Band = 24, BandGap = 5;

    public const string OldSide = "side";

    public static readonly (float L, float T, float R, float B) OldSideRoom = (3, 3, Band, 3);

    public static (float L, float T, float R, float B) Room(string where) => where switch
    {
        "top" => (0, Band + BandGap, 0, 0),
        _ => (0, 0, 0, 0),
    };

    private static (Color Fill, Color Text, string Label) Look(FlagKind k) => k switch
    {
        FlagKind.Disqualified => (HudPalette.FlagBlack, HudPalette.FlagWhite, "DISQUALIFIED"),
        FlagKind.Black => (HudPalette.FlagBlack, HudPalette.FlagWhite, "BLACK FLAG"),
        FlagKind.Meatball => (HudPalette.FlagBlack, Meatball, "REPAIR FLAG"),
        FlagKind.Red => (HudPalette.FlagRed, Color.White, "RED FLAG"),
        FlagKind.Caution => (HudPalette.FlagYellow, HudPalette.FlagBlack, "FULL COURSE YELLOW"),
        FlagKind.Yellow => (HudPalette.FlagYellow, HudPalette.FlagBlack, "YELLOW FLAG"),
        FlagKind.Debris => (HudPalette.FlagYellow, HudPalette.FlagBlack, "DEBRIS FLAG"),
        FlagKind.Blue => (HudPalette.FlagBlue, Color.White, "BLUE FLAG"),
        FlagKind.White => (HudPalette.FlagWhite, HudPalette.FlagBlack, "WHITE FLAG"),
        FlagKind.Checkered => (HudPalette.FlagWhite, HudPalette.FlagBlack, "CHECKERED FLAG"),
        _ => (HudPalette.FlagGreen, Color.White, "GREEN FLAG"),
    };

    public static void DrawBanner(HudPainter p, RectangleF outer, FlagKind kind, string where, float rightSpare = 0)
    {
        if (kind == FlagKind.None || where != "top") return;
        var (fill, text, label) = Look(kind);
        outer = p.Snap(outer);
        var band = p.Snap(RectangleF.FromLTRB(outer.Left, outer.Top, outer.Right - rightSpare, outer.Top + Band));
        p.Fill(band, fill, p.CornerRadius(4));
        var g = p.G;
        var state = g.Save();
        g.SetClip(band, CombineMode.Intersect);
        Ends(p, band, kind);
        var font = p.Font(12.5f, true);
        float flagH = band.Height * 0.56f, flagW = flagH * 1.5f, gap = 7;
        float textW = Math.Min(p.Measure(label, font), band.Width - flagW - gap - 16);
        float x = band.X + (band.Width - flagW - gap - textW) / 2;
        Draw(p, new RectangleF(x, band.Y + (band.Height - flagH) / 2, flagW, flagH), kind);
        p.Text(label, font, text, new RectangleF(x + flagW + gap, band.Y, textW + 2, band.Height), HAlign.Left, VAlign.Center);
        g.Restore(state);
    }

    private static void Ends(HudPainter p, RectangleF band, FlagKind kind)
    {
        float len = Math.Min(band.Width * 0.22f, 90);
        if (kind == FlagKind.Checkered)
        {
            int rows = 3;
            Checks(p, new RectangleF(band.X, band.Y, len, band.Height), rows);
            Checks(p, new RectangleF(band.Right - len, band.Y, len, band.Height), rows);
            return;
        }
        if (kind is not (FlagKind.Yellow or FlagKind.Caution or FlagKind.Debris)) return;
        var g = p.G;
        var color = kind == FlagKind.Debris ? HudPalette.Alpha(HudPalette.FlagRed, 200) : HudPalette.Alpha(HudPalette.FlagBlack, 70);
        var pen = p.Pen(color, 4);
        for (int end = 0; end < 2; end++)
        {
            var part = new RectangleF(end == 0 ? band.X : band.Right - len, band.Y, len, band.Height);
            var state = g.Save();
            g.SetClip(part, CombineMode.Intersect);
            for (float sx = part.X - band.Height; sx < part.Right; sx += 10)
                g.DrawLine(pen, sx, part.Bottom, sx + band.Height, part.Y);
            g.Restore(state);
        }
    }
}

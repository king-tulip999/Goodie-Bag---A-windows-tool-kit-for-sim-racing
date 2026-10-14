using System.Drawing;

namespace GoodieBag.SimRacing.Hud;

internal static class HudItemDrag
{
    public static bool Vertical(IReadOnlyList<(string Id, RectangleF Box)> drawn)
    {
        if (drawn.Count < 2) return false;
        float minX = drawn.Min(d => Center(d.Box).X), maxX = drawn.Max(d => Center(d.Box).X);
        float minY = drawn.Min(d => Center(d.Box).Y), maxY = drawn.Max(d => Center(d.Box).Y);
        return maxY - minY > maxX - minX;
    }

    public static string? Before(IReadOnlyList<(string Id, RectangleF Box)> drawn, string dragged, PointF at)
    {
        bool vertical = Vertical(drawn);
        foreach (var (id, box) in Ordered(drawn))
        {
            if (id == dragged) continue;
            bool sameLine = vertical || at.Y >= box.Top && at.Y < box.Bottom;
            if (vertical ? at.Y < Center(box).Y : at.Y < box.Top || sameLine && at.X < Center(box).X) return id;
        }
        return null;
    }

    public static List<string> Move(IReadOnlyList<string> order, string dragged, string? before, string? lastDrawn)
    {
        var list = order.Where(x => x != dragged).ToList();
        int at = before is not null ? list.IndexOf(before)
            : lastDrawn is not null && lastDrawn != dragged && list.IndexOf(lastDrawn) is >= 0 and var last ? last + 1
            : list.Count;
        list.Insert(at < 0 ? list.Count : at, dragged);
        return list;
    }

    private static IEnumerable<(string Id, RectangleF Box)> Ordered(IReadOnlyList<(string Id, RectangleF Box)> drawn) =>
        Vertical(drawn) ? drawn.OrderBy(d => d.Box.Top) : drawn.OrderBy(d => Math.Round(d.Box.Top)).ThenBy(d => d.Box.Left);

    private static PointF Center(RectangleF r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
}

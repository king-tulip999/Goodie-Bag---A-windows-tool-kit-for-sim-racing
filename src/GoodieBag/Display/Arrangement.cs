namespace GoodieBag.Display;

internal static class Arrangement
{
    public static (int X, int Y)[] Resize(IReadOnlyList<(int X, int Y, int W, int H)> rects, int changed, int newW, int newH)
    {
        var old = rects[changed];
        int dw = newW - old.W, dh = newH - old.H;
        var result = new (int X, int Y)[rects.Count];
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            result[i] = (r.X, r.Y);
            if (i == changed) continue;
            if (r.X >= old.X + old.W) result[i].X += dw;
            if (r.Y >= old.Y + old.H) result[i].Y += dh;
        }
        return result;
    }
}

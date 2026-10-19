namespace GoodieBag.WindowProfiles;

internal sealed record WindowPreset(string Id, string Group, string Name, int X, int Y, int Width, int Height)
{
    public string Text => $"{Name} · {Width} × {Height} at {X}, {Y}";
}

internal sealed record ScreenArea(string Name, int X, int Y, int Width, int Height, bool Primary);

internal static class WindowPresets
{
    public const string Yours = "Your displays";
    public const string Triples = "Triple screens";
    public const string Sizes = "Window sizes on the main display";

    private static readonly (string Id, string Name, int Single, int Height)[] TripleSets =
    {
        ("triple1080", "Triple 1080p", 1920, 1080),
        ("triple1440", "Triple 1440p", 2560, 1440),
        ("triple4k", "Triple 4K", 3840, 2160),
    };

    private static readonly (string Name, int Width, int Height)[] CommonSizes =
    {
        ("720p", 1280, 720),
        ("900p", 1600, 900),
        ("1080p", 1920, 1080),
        ("Ultrawide 1080p", 2560, 1080),
        ("1440p", 2560, 1440),
        ("Ultrawide 1440p", 3440, 1440),
        ("4K", 3840, 2160),
    };

    public static List<WindowPreset> For(IReadOnlyList<ScreenArea> screens)
    {
        var list = new List<WindowPreset>();
        var ordered = screens.OrderBy(s => s.X).ThenBy(s => s.Y).ToList();
        if (ordered.Count > 1)
        {
            var all = Span(ordered);
            list.Add(new WindowPreset("all", Yours, "Across all displays", all.X, all.Y, all.Width, all.Height));
        }
        foreach (var s in ordered)
            list.Add(new WindowPreset("screen:" + s.Name, Yours, s.Primary ? $"{s.Name} (main)" : s.Name, s.X, s.Y, s.Width, s.Height));
        foreach (var (id, name, single, height) in TripleSets)
            list.Add(new WindowPreset(id, Triples, name, -single, 0, single * 3, height));
        var main = ordered.FirstOrDefault(s => s.Primary) ?? ordered.FirstOrDefault();
        if (main is not null)
            foreach (var (name, w, h) in CommonSizes.Where(c => c.Width <= main.Width && c.Height <= main.Height))
                list.Add(new WindowPreset("size:" + w + "x" + h, Sizes, name + " centered", main.X + (main.Width - w) / 2, main.Y + (main.Height - h) / 2, w, h));
        return list;
    }

    public static ScreenArea Span(IReadOnlyList<ScreenArea> screens)
    {
        int left = screens.Min(s => s.X), top = screens.Min(s => s.Y);
        return new ScreenArea("all", left, top, screens.Max(s => s.X + s.Width) - left, screens.Max(s => s.Y + s.Height) - top, false);
    }
}

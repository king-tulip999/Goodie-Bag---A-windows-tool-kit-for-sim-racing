using GoodieBag.Config;

namespace GoodieBag.SimRacing.Hud;

internal static class HudPresets
{
    private static HudWidgetConfig W(string kind, HudAnchor anchor, double x, double y, double w, double h, bool enabled = true, (string Key, string Value)[]? options = null)
    {
        var config = new HudWidgetConfig { Id = HudWidgetConfig.NewId(kind), Kind = kind, Enabled = enabled, Anchor = anchor, X = x, Y = y, W = w, H = h };
        foreach (var (k, v) in options ?? Array.Empty<(string, string)>()) config.Options[k] = v;
        return config;
    }

    public const string DefaultName = "Default";

    public static List<HudLayout> DefaultLayouts() => new()
    {
        new HudLayout
        {
            Name = DefaultName,
            Widgets =
            {
                W("standings", HudAnchor.TopLeft, 0, 20, 560, 540, options: new[] { ("columns", "cpos,badge,avglap,change,num,name"), ("footer", "remain,classFastest,sof,lap"), ("header", "session,pos,classFastest,csof"), ("classInfo", "cars,sof,laps"), ("classOrder", "fastest") }),
                W("timing", HudAnchor.BottomRight, -452, -8, 380, 140, options: new[] { ("fields", "last,current,best,predicted") }),
                W("fuel", HudAnchor.BottomRight, 0, -8, 440, 280, options: new[] { ("statusEdge", "false"), ("parts", "header,gauge,grid,plan,figures"), ("head", "togo,nextpit,stops,pitby,add"), ("gridCols", "use,laps,refuel,finish"), ("top", "level,laps,togo,perlap") }),
                W("relative", HudAnchor.BottomLeft, 20, -28, 500, 300, options: new[] { ("columns", "cpos,pos,gap,name,out,num"), ("header", "session,remain,pos,last,best"), ("footer", "lap,clock,wetness,track,air") }),
            },
        },
    };

    public static List<HudLayoutRule> DefaultRules() => new();

    public const string CustomName = "Custom";

    public static string? Fork(RaceHudSettings settings) => settings.MarkCustomized(IsDefault, () => DefaultLayouts()[0]);

    public static bool IsDefault(HudLayout layout)
    {
        var preset = DefaultLayouts()[0].Widgets;
        if (layout.Widgets.Count != preset.Count) return false;
        for (int i = 0; i < preset.Count; i++)
        {
            var a = layout.Widgets[i];
            var b = preset[i];
            b.Clamp();
            if (!string.Equals(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase) || a.Enabled != b.Enabled || a.Anchor != b.Anchor
                || !string.Equals(a.Monitor, b.Monitor, StringComparison.OrdinalIgnoreCase)
                || Math.Abs(a.X - b.X) > 0.05 || Math.Abs(a.Y - b.Y) > 0.05 || Math.Abs(a.W - b.W) > 0.05 || Math.Abs(a.H - b.H) > 0.05
                || a.Locked != b.Locked || a.Opacity != b.Opacity || a.UpdateHz != b.UpdateHz || a.HideInReplay != b.HideInReplay
                || a.OnlyWhenNeeded != b.OnlyWhenNeeded || a.OnlyOnTrack != b.OnlyOnTrack || a.Background != b.Background
                || !a.Sessions.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(b.Sessions.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
                || !SameOptions(a, b))
                return false;
        }
        return true;
    }

    private static bool SameOptions(HudWidgetConfig a, HudWidgetConfig b)
    {
        var defs = HudCatalog.Find(b.Kind)?.Options ?? Array.Empty<HudOption>();
        Dictionary<string, string> Set(HudWidgetConfig w) => w.Options
            .Where(o => defs.FirstOrDefault(d => string.Equals(d.Key, o.Key, StringComparison.OrdinalIgnoreCase)) is not { } d || !string.Equals(d.Default, o.Value, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
        var sa = Set(a);
        var sb = Set(b);
        return sa.Count == sb.Count && sa.All(o => sb.TryGetValue(o.Key, out var v) && string.Equals(v, o.Value, StringComparison.OrdinalIgnoreCase));
    }
}

using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class BlockEditor
{
    private static bool Colored(HudOptions o) => o.Number("warn") != 0 || o.Number("crit") != 0;

    private static IReadOnlyDictionary<string, string> Start(HudOptions o) =>
        Colored(o) ? V() : V(("warn", Math.Round((o.Number("min") + o.Number("max")) / 2, 1)));

    private static readonly MergedSetting Coloring = new("coloring", "Color when", new[] { "above", "warn", "crit" }, new MergedChoice[]
    {
        new("Off", V(("warn", 0), ("crit", 0)), "The number keeps its color"),
        new("Above", V(("above", true)), "Amber past Warning, red past Critical, going up") { Also = Start },
        new("Below", V(("above", false)), "Amber past Warning, red past Critical, going down") { Also = Start },
    });

    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("value", "Value", HudTab.Layout, true, O("source"), O("decimals") with { InlineHelp = true }, O("visual")),
            new("range", "Bar & graph", HudTab.Layout, true, O("min") with { ShowIf = o => o.Choice("visual") != "number" }, O("max") with { ShowIf = o => o.Choice("visual") != "number" }),
            new("coloring", "Coloring", HudTab.Layout, false, Coloring, O("warn") with { ShowIf = Colored }, O("crit") with { ShowIf = Colored }),
        },
        PresetKeeps = new[] { "source", "decimals", "min", "max", "warn", "crit", "above" },
        Presets = new HudPreset[]
        {
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("visual", "graph"))),
        },
    };
}

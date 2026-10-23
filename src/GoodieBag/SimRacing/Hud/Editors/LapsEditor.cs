using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class LapsEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("count"), O("fuel"), O("stats"), O("window") with { ShowIf = o => o.Bool("stats") }) },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("count", 5), ("fuel", false), ("stats", false))),
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("count", 12))),
        },
    };
}

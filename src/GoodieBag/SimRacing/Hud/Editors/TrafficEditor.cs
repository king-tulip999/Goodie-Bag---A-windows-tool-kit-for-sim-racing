using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class TrafficEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("window"), O("max"), O("sameClass")) },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("max", 3))),
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("max", 8), ("sameClass", true))),
        },
    };
}

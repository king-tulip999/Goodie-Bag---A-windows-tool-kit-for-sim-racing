using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class EventsEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("count")) },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("count", 5))),
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("count", 15))),
        },
    };
}

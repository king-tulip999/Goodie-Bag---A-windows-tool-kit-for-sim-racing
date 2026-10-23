using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class AlertsEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("max"), O("big"), O("history")) },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("max", 1))),
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("max", 5), ("history", true))),
        },
    };
}

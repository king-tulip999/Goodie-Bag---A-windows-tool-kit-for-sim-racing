using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class TyresEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("shows", "Shows", HudTab.Layout, true, new TogglesSetting("shows", "Each corner shows", new[] { "pressure", "temps", "wear" })),
            new("limits", "Temperature limits", HudTab.Layout, false, O("low") with { ShowIf = o => o.Bool("temps") }, O("high") with { ShowIf = o => o.Bool("temps") }),
        },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("temps", false), ("wear", false))),
            new(HudPreset.Standard, V()),
        },
    };
}

using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class WeatherEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[] { new("main", null, HudTab.Layout, true, O("temp"), O("wind"), O("turn") with { ShowIf = o => o.Bool("wind") }) },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("wind", false))),
            new(HudPreset.Standard, V()),
        },
    };
}

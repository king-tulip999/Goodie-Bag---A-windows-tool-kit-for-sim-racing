using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class SessionEditor
{
    public static readonly HudEditorDef Def = new()
    {
        ContentTitle = "Shows",
        Groups = new HudGroup[]
        {
            new("main", null, HudTab.Layout, true, O("progress")),
            new("fields", "Shows", HudTab.Content, true, new PickerSetting("fields", new HudCategory[]
            {
                new("Time", "remain", "lapsleft", "elapsed", "clock", "finish"),
                new("Race", "lap", "pos", "cpos", "phase", "inc"),
                new("Track", "track", "temps"),
            })),
        },
        Regions = new Dictionary<string, string> { ["progress"] = "progress" },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("fields", "remain,pos"), ("progress", false))),
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("fields", "remain,lapsleft,elapsed,lap,pos,cpos,phase,track,temps,inc,clock,finish"))),
        },
    };
}

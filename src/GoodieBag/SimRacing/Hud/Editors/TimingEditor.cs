using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class TimingEditor
{
    public static readonly HudEditorDef Def = new()
    {
        ContentTitle = "Times",
        Groups = new HudGroup[]
        {
            new("delta", "Delta", HudTab.Layout, true, O("reference"), O("bar"), O("range") with { ShowIf = o => o.Bool("bar") }),
            new("sectors", "Sectors", HudTab.Layout, true, O("sectors"), O("split")),
            new("times", "Times", HudTab.Content, true, new PickerSetting("fields", new HudCategory[]
            {
                new("Your laps", "current", "last", "best", "predicted", "theoretical"),
                new("Field", "sessionBest", "classBest"),
            })),
        },
        Regions = new Dictionary<string, string> { ["delta"] = "reference", ["bar"] = "bar", ["sectors"] = "sectors" },
        PresetKeeps = new[] { "split" },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("fields", "current,best"), ("sectors", false))),
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("fields", "current,last,best,predicted,theoretical,sessionBest,classBest"))),
        },
    };
}

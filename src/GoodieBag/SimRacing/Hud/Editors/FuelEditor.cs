using static GoodieBag.SimRacing.Hud.Editors.FieldSettings;
using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class FuelEditor
{
    public static readonly HudEditorDef Def = new()
    {
        ContentTitle = "Sections",
        Groups = new HudGroup[]
        {
            new("look", "Look", HudTab.Layout, true, O("title"), O("statusEdge")),
            new("rates", "Rates", HudTab.Layout, true, O("avgLaps") with { ShowIf = o => Has("parts", "grid")(o) && Has("gridRows", "avgN")(o) }),
            new("parts", "Sections", HudTab.Content, true, new PickerSetting("parts", NoCategories)),
            new("shows", "In the sections", HudTab.Content, true,
                new PickerSetting("head", NoCategories) { ShowIf = Has("parts", "header") },
                new PickerSetting("gridRows", NoCategories) { ShowIf = Has("parts", "grid") },
                new PickerSetting("gridCols", NoCategories) { ShowIf = Has("parts", "grid") },
                new PickerSetting("top", new HudCategory[]
                {
                    new("Tank", "level", "laps", "time"),
                    new("Rates", "perlap", "last", "max"),
                    new("To the flag", "togo", "tofinish", "surplus", "atflag"),
                    new("Stops", "add", "stops", "window"),
                }) { ShowIf = Has("parts", "figures") },
                new PickerSetting("cols", NoCategories) { ShowIf = Has("parts", "table") },
                new PickerSetting("rows", NoCategories) { ShowIf = Has("parts", "table") }),
        },
        Regions = new Dictionary<string, string> { ["title"] = "title" },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("parts", "header,gauge"), ("head", "togo,nextpit"))),
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("parts", "header,gauge,grid,plan,figures,table"), ("head", "togo,nextpit,pitby,stops,add,level"),
                ("gridRows", "curr,avg,avgN,max,min,last"), ("gridCols", "use,laps,refuel,finish"), ("title", true))),
        },
    };
}

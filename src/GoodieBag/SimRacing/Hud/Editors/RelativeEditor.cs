using static GoodieBag.SimRacing.Hud.Editors.FieldSettings;
using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class RelativeEditor
{
    public static readonly HudEditorDef Def = new()
    {
        Groups = new HudGroup[]
        {
            new("layout", "Layout", HudTab.Layout, true, O("ahead"), O("behind"), O("showHeader"), O("stripes")),
            new("rows", "Rows", HudTab.Layout, true, MyRow, O("lapColors"), O("close"), O("nameFormat"), O("noDigits"),
                O("badgeFormat") with { ShowIf = Has("columns", "badge") }),
            new("timing", "Timing", HudTab.Layout, false, O("timeFormat"), O("decimals"), O("avgLaps") with { ShowIf = Has("columns", "avglap") }),
            new("pit", "Pit", HudTab.Layout, false, O("pitShow") with { ShowIf = Has("columns", "pit") }, O("pitTime") with { ShowIf = Has("columns", "pit") }),
            new("colors", "Colors & flags", HudTab.Layout, false, ClassStyle, FlagBorder, O("flagBanner"), O("marks")),
            new("columns", "Columns", HudTab.Content, true, new PickerSetting("columns", ColumnCategories)),
            new("infobars", "Info bars", HudTab.Content, true, InfoBars),
            new("cars", "Which cars", HudTab.Content, true, new ClassFilterSetting(), O("hidePits")),
        },
        Regions = new Dictionary<string, string>
        {
            ["headings"] = "showHeader",
            ["myrow"] = "myRow",
            ["flag"] = "flagBanner",
            ["rows"] = "ahead",
        },
        PresetKeeps = new[] { "flagBanner" },
        Presets = new HudPreset[]
        {
            new(HudPreset.Minimal, V(("columns", "cpos,num,name,gap"), ("header", ""), ("footer", ""))),
            new(HudPreset.Standard, V()),
            new(HudPreset.Everything, V(("columns", "pos,cpos,change,num,name,team,badge,irchange,last,best,avglap,delta,laps,lapsdone,pit,out,stint,inc,trend,gap"),
                ("header", "session,remain,lapsleft,pos,cpos,inc,bias"), ("footer", "lap,last,best,clock,wetness,air,track"), ("showHeader", true))),
        },
    };
}

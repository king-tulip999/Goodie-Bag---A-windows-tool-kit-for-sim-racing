using static GoodieBag.SimRacing.Hud.Editors.HudEditorDef;

namespace GoodieBag.SimRacing.Hud.Editors;

internal static class FieldSettings
{
    public static readonly HudCategory[] ColumnCategories =
    {
        new("Position", "pos", "cpos", "change"),
        new("Driver", "num", "name", "team", "badge", "irchange"),
        new("Gaps", "gap", "int", "gapme", "laps", "trend"),
        new("Lap times", "last", "best", "avglap", "delta"),
        new("Race", "lapsdone", "inc"),
        new("Pit", "pit", "out", "stint"),
    };

    public static readonly HudCategory[] StripCategories =
    {
        new("Session", "session", "remain", "lapsleft", "lap", "clock", "trackname", "cars"),
        new("Position", "pos", "cpos", "classpos", "leader", "ahead", "behind"),
        new("Lap times", "last", "best", "delta", "fastest", "classFastest"),
        new("Car & fuel", "fuel", "fuellevel", "bias", "inc"),
        new("Field", "sof", "csof"),
        new("Weather", "air", "track", "wind", "humidity", "wetness", "rain"),
    };

    public static Func<HudOptions, bool> Has(string key, string id) => o => o.Columns(key).Contains(id);

    public static readonly LanesSetting InfoBars = new("infobars", "Info bars", new[] { ("header", "Top"), ("footer", "Bottom") }, StripCategories);

    public static readonly MergedSetting ClassStyle = new("classStyle", "Class color by number", new[] { "numFill", "numEdge" }, new MergedChoice[]
    {
        new("Edge", V(("numFill", false), ("numEdge", true)), "A thin class-color edge by the car number"),
        new("Fade", V(("numFill", true), ("numEdge", false)), "Class color fading behind the car number"),
        new("Both", V(("numFill", true), ("numEdge", true)), "Fade and edge"),
        new("Stripe", V(("numFill", false), ("numEdge", false)), "A thin class-color stripe at the start of the row; the number plain"),
    });

    public static readonly MergedSetting MyRow = new("myRow", "My row", new[] { "highlightMe", "posBlock" }, new MergedChoice[]
    {
        new("Off", V(("highlightMe", false)), "Your row looks like the others"),
        new("Tint", V(("highlightMe", true), ("posBlock", false)), "Your row tinted amber"),
        new("Tint + block", V(("highlightMe", true), ("posBlock", true)), "Your row tinted amber, your position on an amber block"),
    });

    public static readonly MergedSetting FlagBorder = new("flagBorder", "Flag border", new[] { "flagEdge", "flagWidth" }, new MergedChoice[]
    {
        new("Off", V(("flagEdge", false))),
        new("2 px", V(("flagEdge", true), ("flagWidth", 2))),
        new("4 px", V(("flagEdge", true), ("flagWidth", 4))),
        new("6 px", V(("flagEdge", true), ("flagWidth", 6))),
    })
    {
        Extra = o => o.Bool("flagEdge") ? new MergedChoice($"{o.Int("flagWidth")} px", V(("flagEdge", true), ("flagWidth", o.Int("flagWidth")))) : null,
        Help = "A border in the flag's color while a yellow, red, blue, white or checkered flag is out",
    };

    public static readonly HudCategory[] NoCategories = Array.Empty<HudCategory>();
}

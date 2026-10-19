namespace GoodieBag.Views;

internal static class Hues
{
    public static readonly (string Soft, string Deep) Accent = ("AccentSoft", "AccentText");

    private static readonly (string Soft, string Deep)[] Pairs =
        { Accent, ("SlateSoft", "SlateDeep"), ("SageSoft", "SageDeep"), ("CoralSoft", "CoralDeep"), ("PlumSoft", "PlumDeep"), ("GoldSoft", "GoldDeep") };

    public static (string Soft, string Deep) At(int i) => Pairs[((i % Pairs.Length) + Pairs.Length) % Pairs.Length];

    private static readonly Dictionary<string, (string Soft, string Deep)> Tools = new()
    {
        [Plugins.PluginIds.WindowProfiles] = ("Tool1Soft", "Tool1Deep"), [Plugins.PluginIds.Displays] = ("Tool2Soft", "Tool2Deep"),
        [Plugins.PluginIds.HardwareMonitor] = ("Tool4Soft", "Tool4Deep"),
        [Plugins.PluginIds.FanControl] = ("Tool5Soft", "Tool5Deep"), [Plugins.PluginIds.SimRacing] = ("Tool6Soft", "Tool6Deep"),
        [Plugins.PluginIds.SessionStats] = ("Tool7Soft", "Tool7Deep"),
    };

    public static (string Soft, string Deep) Tool(string pluginId, (string Soft, string Deep) fallback) => Tools.GetValueOrDefault(pluginId, fallback);

    public static (string Soft, string Deep) Named(string name) => name switch
    {
        "Slate" => Pairs[1], "Sage" => Pairs[2], "Coral" => Pairs[3], "Plum" => Pairs[4], "Gold" => Pairs[5], _ => Accent,
    };
}

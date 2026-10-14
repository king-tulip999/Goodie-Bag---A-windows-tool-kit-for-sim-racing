using GoodieBag.Config;
using GoodieBag.Plugins;

namespace GoodieBag.Services;

internal enum HotkeyOwner { Mode, DisplayProfile, Plugin, Window, Iracing }

internal sealed record HotkeyEntry(string Id, IReadOnlyList<string> Path, string Label, string? Note, HotkeyOwner Owner,
    string? PluginId, string Page, object ClashKey, Func<string?> Get, Action<string?> Set, bool Global = true,
    string? Default = null, string? Locked = null, string? Cluster = null);

internal static class HotkeyCatalog
{
    public const string WindowGroup = "Goodie Bag";
    public const string IracingGroup = "iRacing (in the sim)";
    public const string SearchId = "window.search";
    public const string ShowWindowId = "window.show";

    public static List<HotkeyEntry> Build(AppConfig config, IEnumerable<(string PluginId, string PluginName, string Page, PluginHotkey Hotkey)> pluginHotkeys,
        IReadOnlyList<SimRacing.IracingAction>? iracing = null, Action<string, string?>? setIracing = null)
    {
        var list = new List<HotkeyEntry>();

        foreach (var mode in config.Modes)
        {
            var m = mode;
            list.Add(new HotkeyEntry("mode:" + m.Name, new[] { "Modes", "Apply a mode" }, m.Name, m.OneOff ? "one-off" : null, HotkeyOwner.Mode,
                PluginIds.Modes, "modes", m, () => m.Hotkey, hk => m.Hotkey = hk));
        }

        foreach (var profile in config.Displays.Profiles)
        {
            var p = profile;
            list.Add(new HotkeyEntry("display:" + p.Name, new[] { "Displays", "Apply a display profile" }, p.Name, null, HotkeyOwner.DisplayProfile,
                PluginIds.Displays, "displays", p, () => p.Hotkey, hk => p.Hotkey = hk));
        }

        var ids = new HashSet<string>(list.Select(e => e.Id), StringComparer.Ordinal);
        foreach (var (pluginId, pluginName, page, hotkey) in pluginHotkeys)
        {
            if (!ids.Add(hotkey.Id))
                continue;
            var path = hotkey.Group is null ? new[] { pluginName } : new[] { pluginName, hotkey.Group };
            list.Add(new HotkeyEntry(hotkey.Id, path, hotkey.Label, null, HotkeyOwner.Plugin, pluginId, page, hotkey.Id, hotkey.Get, hotkey.Set, Default: hotkey.Default, Cluster: hotkey.Group));
        }

        if (iracing is not null && setIracing is not null)
        {
            foreach (var action in iracing)
            {
                var a = action;
                string? locked = a.Binding.OnController ? "Set on a wheel or controller in iRacing" : null;
                list.Add(new HotkeyEntry("iracing:" + a.Binding.Name, new[] { IracingGroup, a.Group }, a.Label, null, HotkeyOwner.Iracing,
                    PluginIds.SimRacing, "simracing", "iracing:" + a.Binding.Name, () => a.Binding.OnController ? "Wheel / controller" : a.Binding.Key?.ToString(),
                    hk => setIracing(a.Binding.Name, hk), Global: false, Default: a.Default?.ToString(), Locked: locked, Cluster: a.Cluster));
            }
        }

        var ui = config.Ui;
        list.Add(new HotkeyEntry(ShowWindowId, new[] { WindowGroup }, "Show / hide Goodie Bag", "from any program", HotkeyOwner.Window,
            null, "settings", ShowWindowId, () => config.Hotkeys.ShowWindow, hk => config.Hotkeys.ShowWindow = hk));
        list.Add(new HotkeyEntry(SearchId, new[] { WindowGroup }, "Search settings", "in this window", HotkeyOwner.Window,
            null, "settings", SearchId, () => ui.SearchKey, hk => ui.SearchKey = hk, Global: false, Default: "Ctrl+F"));
        return list;
    }

    public static string CategoryKey(string name) => "group:" + name;

    public static bool IsCategoryKey(string? key) =>
        key is not null && key.StartsWith("group:", StringComparison.Ordinal) && key.Length > 6 && key.IndexOf('/', 6) < 0;

    public static List<T> Ordered<T>(IEnumerable<T> items, Func<T, string> key, IReadOnlyList<string> order)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < order.Count; i++)
            rank.TryAdd(order[i], i);
        return items.Select((item, i) => (item, i, r: rank.TryGetValue(key(item), out var r) ? r : int.MaxValue))
            .OrderBy(x => x.r).ThenBy(x => x.i).Select(x => x.item).ToList();
    }

    public static T? Step<T>(IReadOnlyList<T> items, T? current, int delta) where T : class
    {
        if (items.Count == 0)
            return null;
        int at = current is null ? -1 : items.ToList().IndexOf(current);
        if (at < 0)
            return delta > 0 ? items[0] : items[^1];
        return items[((at + delta) % items.Count + items.Count) % items.Count];
    }

    public static void SaveOrder(List<string> order, IReadOnlyList<string> siblings)
    {
        int at = order.FindIndex(k => siblings.Contains(k));
        order.RemoveAll(k => siblings.Contains(k));
        if (at < 0 || at > order.Count) at = order.Count;
        order.InsertRange(at, siblings);
    }
}

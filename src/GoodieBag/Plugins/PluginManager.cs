using GoodieBag.Config;
using GoodieBag.Services;

namespace GoodieBag.Plugins;

internal sealed class PluginManager
{
    private readonly AppServices _app;
    private readonly AppConfig _config;

    public IReadOnlyList<Plugin> All { get; }

    public event Action? Changed;

    public PluginManager(AppServices app, AppConfig config)
    {
        _app = app;
        _config = config;
        All = typeof(Plugin).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Plugin)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (Plugin)Activator.CreateInstance(t)!)
            .OrderBy(p => p.Order).ThenBy(p => p.Name).ToList();
    }

    public IEnumerable<Plugin> Active => All.Where(p => p.IsActive);

    public Plugin? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    public bool IsEnabled(Plugin plugin) => PluginStates.IsEnabled(_config.Plugins, plugin.Id, plugin.EnabledByDefault);

    public bool IsActive(string id) => Find(id)?.IsActive == true;

    public (Plugin Plugin, PluginPage Page)? PageOwner(string key)
    {
        foreach (var plugin in All)
            foreach (var page in plugin.Pages)
                if (page.Key == key)
                    return (plugin, page);
        return null;
    }

    public void StartEnabled()
    {
        foreach (var plugin in All.Where(IsEnabled))
            TryStart(plugin);
        Changed?.Invoke();
    }

    public void SetEnabled(Plugin plugin, bool enabled)
    {
        _config.Plugins[plugin.Id] = enabled;
        _app.Save();
        StartOrStop(plugin, enabled);
        _app.Hotkeys.RegisterAll();
        Changed?.Invoke();
    }

    public void SetEnabled(Func<Plugin, bool> wanted)
    {
        var changed = PluginStates.Apply(_config.Plugins, All.Select(p => (p.Id, p.EnabledByDefault)), id => Find(id) is { } p && wanted(p));
        if (changed.Count == 0)
            return;
        _app.Save();
        foreach (var id in changed)
            if (Find(id) is { } plugin)
                StartOrStop(plugin, _config.Plugins[id]);
        _app.Hotkeys.RegisterAll();
        Changed?.Invoke();
    }

    public void RaiseChanged()
    {
        _app.Hotkeys.RegisterAll();
        Changed?.Invoke();
    }

    private void StartOrStop(Plugin plugin, bool enabled)
    {
        if (enabled)
            TryStart(plugin);
        else
        {
            try { plugin.Stop(_app); }
            catch (Exception ex) { App.LogError(ex); }
        }
    }

    internal static List<Plugin> InNavOrder(IEnumerable<Plugin> plugins, IReadOnlyList<string> navOrder)
    {
        int Rank(Plugin plugin)
        {
            int best = int.MaxValue;
            foreach (var key in Keys(plugin))
                for (int i = 0; i < navOrder.Count && i < best; i++)
                    if (navOrder[i] == key) { best = i; break; }
            return best;
        }
        return plugins.OrderBy(Rank).ToList();
    }

    internal static List<string> PageKeys(IEnumerable<Plugin> plugins) => plugins.SelectMany(Keys).ToList();

    private static IEnumerable<string> Keys(Plugin plugin) =>
        plugin.Pages.Count > 0 ? plugin.Pages.Select(p => p.Key) : new[] { PluginKeyPrefix + plugin.Id };

    public const string PluginKeyPrefix = "plugin:";

    public void StopAll()
    {
        foreach (var plugin in All.Reverse())
        {
            try { plugin.Stop(_app); }
            catch (Exception ex) { App.LogError(ex); }
        }
    }

    private void TryStart(Plugin plugin)
    {
        try
        {
            plugin.Start(_app);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            try { plugin.Stop(_app); } catch { }
            _app.Notify($"{plugin.Name} couldn't start", ex.Message, NoticeKind.Error, important: true);
        }
    }
}

internal static class PluginStates
{
    public static bool IsEnabled(IReadOnlyDictionary<string, bool> stored, string id, bool byDefault) =>
        stored.TryGetValue(id, out var on) ? on : byDefault;

    public static List<string> Apply(Dictionary<string, bool> stored, IEnumerable<(string Id, bool Default)> plugins, Func<string, bool> wanted)
    {
        var changed = new List<string>();
        foreach (var (id, byDefault) in plugins)
        {
            bool on = wanted(id);
            if (IsEnabled(stored, id, byDefault) == on)
                continue;
            stored[id] = on;
            changed.Add(id);
        }
        return changed;
    }
}

using GoodieBag.Hotkeys;

namespace GoodieBag.Services;

internal sealed class HotkeyService : IDisposable
{
    private readonly AppServices _app;
    private HotkeyManager? _manager;
    private int _pauseCount;

    public HotkeyService(AppServices app)
    {
        _app = app;
    }

    public void RegisterAll()
    {
        if (_app.Demo || _pauseCount > 0)
            return;

        _manager ??= new HotkeyManager();
        _manager.UnregisterAll();
        var failures = new List<string>();

        void Register(string? hotkey, Action action, string label)
        {
            if (!string.IsNullOrWhiteSpace(hotkey) && !_manager!.TryRegister(hotkey, action, out var error))
                failures.Add($"{label}: {Hotkey.Display(hotkey)} {error}");
        }

        var config = _app.Config;
        bool displays = _app.Plugins.IsActive(Plugins.PluginIds.Displays), modes = _app.Plugins.IsActive(Plugins.PluginIds.Modes);
        foreach (var profile in displays ? config.Displays.Profiles : new List<Config.DisplayProfile>())
        {
            var target = profile;
            Register(profile.Hotkey, () => _ = _app.DisplayProfiles.ApplyAsync(target), "Display profile " + profile.Name);
        }
        foreach (var hotkey in PluginHotkeys())
            Register(hotkey.Get(), hotkey.Invoke, hotkey.Label);
        foreach (var mode in modes ? config.Modes : new List<Config.Mode>())
        {
            var target = mode;
            Register(mode.Hotkey, () => _ = _app.Modes.ApplyAsync(target), mode.Name);
        }
        Register(config.Hotkeys.ShowWindow, _app.RequestToggleWindow, "Show / hide Goodie Bag");

        var failed = string.Join("\n", failures);
        if (failed.Length > 0 && failed != _lastFailures)
            _app.Notify("Some keybinds didn't register", failed, NoticeKind.Warning, important: true);
        _lastFailures = failed;
    }

    private string _lastFailures = "";

    public void Pause()
    {
        if (_pauseCount++ == 0)
            _manager?.UnregisterAll();
    }

    public void Resume()
    {
        if (_pauseCount > 0 && --_pauseCount == 0)
            RegisterAll();
    }

    public string? Clash(string? hotkey, object? except)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
            return null;
        var config = _app.Config;
        string shown = Hotkey.Display(hotkey);
        if (config.Displays.Profiles.FirstOrDefault(p => !ReferenceEquals(p, except) && Hotkey.SameKey(p.Hotkey, hotkey)) is { } profile)
            return $"{shown} is already used by the display profile “{profile.Name}”.";
        foreach (var plugin in PluginHotkeys())
        {
            if (!Equals(except, plugin.Id) && Hotkey.SameKey(hotkey, plugin.Get()))
                return $"{shown} is already used for “{plugin.Label}”.";
        }
        if (config.Modes.FirstOrDefault(p => !ReferenceEquals(p, except) && Hotkey.SameKey(p.Hotkey, hotkey)) is { } clash)
            return $"{shown} is already used by the mode “{clash.Name}”.";
        if (!Equals(except, HotkeyCatalog.SearchId) && (Hotkey.SameKey(hotkey, config.Ui.SearchKey) || Hotkey.SameKey(hotkey, "Ctrl+K")))
            return $"{shown} is already used for “Search settings” in the Goodie Bag window.";
        if (!Equals(except, HotkeyCatalog.ShowWindowId) && Hotkey.SameKey(hotkey, config.Hotkeys.ShowWindow))
            return $"{shown} is already used for “Show / hide Goodie Bag”.";
        return null;
    }

    public List<HotkeyEntry> Catalog() => HotkeyCatalog.Build(_app.Config,
        _app.Plugins.Active.SelectMany(p => p.Hotkeys(_app).Select(h => (p.Id, p.Name, p.Pages.FirstOrDefault()?.Key ?? "plugins", h))),
        Plugins.BuiltIn.SimRacingPlugin.FeatureOn(_app, "iracing") ? _app.IracingKeys.Actions() : null, SetIracing);

    private void SetIracing(string action, string? hotkey)
    {
        SimRacing.IracingFiles.IracingKey? key = null;
        if (!string.IsNullOrWhiteSpace(hotkey))
            key = SimRacing.IracingFiles.IracingKey.FromHotkey(hotkey)
                ?? throw new InvalidOperationException(hotkey.Contains("NumPad", StringComparison.OrdinalIgnoreCase)
                    ? "Turn Num Lock off, then press the number-pad key again."
                    : $"{Hotkey.Display(hotkey)} can't be set from Goodie Bag (use letters, numbers, F1–F12, arrows, Page Up / Down, Home, End, Insert, Delete, Space, [ ] = - with Ctrl / Alt / Shift); set that key in iRacing.");
        _app.IracingKeys.Set(action, key);
    }

    public string? GlobalUsing(string? hotkey, object? except) =>
        Catalog().FirstOrDefault(e => e.Global && e.Owner != HotkeyOwner.Iracing && !Equals(e.ClashKey, except) && IsAvailable(e)
            && SimRacing.IracingFiles.IracingKey.FromHotkey(e.Get()) is { } mine && SimRacing.IracingFiles.IracingKey.FromHotkey(hotkey) is { } theirs && mine.SameAs(theirs))?.Label;

    public bool IsAvailable(HotkeyEntry entry) => entry.PluginId is null || _app.Plugins.IsActive(entry.PluginId);

    public string? Change(HotkeyEntry entry, string? hotkey)
    {
        if (entry.Locked is { } locked)
            return locked + ".";
        if (entry.Owner == HotkeyOwner.Iracing)
        {
            try { entry.Set(string.IsNullOrWhiteSpace(hotkey) ? null : hotkey); }
            catch (InvalidOperationException ex) { return ex.Message; }
            if (GlobalUsing(hotkey, entry.ClashKey) is { } mine)
                _app.Notify("Saved, but Goodie Bag uses those keys too",
                    $"“{mine}” in Goodie Bag has the same keys and takes them first, so iRacing won't see them while Goodie Bag runs.", NoticeKind.Warning);
            return null;
        }
        if (Clash(hotkey, entry.ClashKey) is { } clash)
            return clash;
        entry.Set(string.IsNullOrWhiteSpace(hotkey) ? null : hotkey);
        switch (entry.Owner)
        {
            case HotkeyOwner.Mode: _app.RaiseModesChanged(); break;
            case HotkeyOwner.DisplayProfile: _app.DisplayProfiles.RaiseChanged(); break;
            default: _app.Save(); RegisterAll(); break;
        }
        return null;
    }

    public IEnumerable<Plugins.PluginHotkey> PluginHotkeys() => _app.Plugins.Active.SelectMany(p => p.Hotkeys(_app));

    public void Dispose() => _manager?.Dispose();
}

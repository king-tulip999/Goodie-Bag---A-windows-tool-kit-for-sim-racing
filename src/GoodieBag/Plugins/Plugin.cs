using System.Windows;
using GoodieBag.Services;
using GoodieBag.Views;
using WinForms = System.Windows.Forms;

namespace GoodieBag.Plugins;

internal static class PluginIds
{
    public const string Overlay = "overlay";
    public const string FanControl = "fan-control";
    public const string HardwareMonitor = "hardware-monitor";
    public const string SimRacing = "sim-racing";
    public const string Modes = "modes";
    public const string Displays = "displays";
    public const string SessionStats = "session-stats";
}

internal enum PageSensors
{
    None,
    Basic,
    Full,
}

internal sealed record PluginPage(string Key, string Title, string Icon, Func<AppServices, MainWindow, FrameworkElement> Create,
    PageSensors Sensors = PageSensors.None);

internal sealed record PluginHotkey(string Id, string Label, Func<string?> Get, Action<string?> Set, Action Invoke, string? Group = null, string? Default = null);

internal abstract class Plugin
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }

    public abstract string Icon { get; }

    public virtual (string Soft, string Deep) Colors => ("AccentSoft", "AccentText");

    public virtual bool EnabledByDefault => true;

    public virtual int Order => 100;

    public abstract string Reads { get; }

    public abstract string BackgroundWork { get; }

    public abstract string Changes { get; }

    public virtual IReadOnlyList<PluginPage> Pages => Array.Empty<PluginPage>();

    public virtual IEnumerable<Automations.PluginTrigger> Triggers(AppServices app) => Array.Empty<Automations.PluginTrigger>();

    public virtual IEnumerable<SearchEntry> SearchEntries => Array.Empty<SearchEntry>();

    public virtual IEnumerable<PluginHotkey> Hotkeys(AppServices app) => Array.Empty<PluginHotkey>();

    public virtual void AddTrayItems(WinForms.ToolStripItemCollection items, AppServices app) { }

    public virtual string? Status(AppServices app) => null;

    public virtual IReadOnlyCollection<int>? IracingLeagueIds(AppServices app) => null;

    public bool IsActive { get; private set; }

    internal void Start(AppServices app)
    {
        if (IsActive)
            return;
        IsActive = true;
        OnStart(app);
    }

    internal void Stop(AppServices app)
    {
        if (!IsActive)
            return;
        IsActive = false;
        OnStop(app);
    }

    protected abstract void OnStart(AppServices app);

    protected abstract void OnStop(AppServices app);
}

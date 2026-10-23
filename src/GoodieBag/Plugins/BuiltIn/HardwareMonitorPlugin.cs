using GoodieBag.Services;
using GoodieBag.Views.Pages;

namespace GoodieBag.Plugins.BuiltIn;

internal sealed class HardwareMonitorPlugin : Plugin
{
    private const string Consumer = "Hardware Monitor";
    private AppServices? _app;

    public override string Id => PluginIds.HardwareMonitor;
    public override string Name => "Hardware Monitor";
    public override string Description => "Performance panels (live readings, two-minute history, FPS and frame times) and every sensor your PC exposes with min / max.";
    public override string Icon => "I.HeartPulse";
    public override (string Soft, string Deep) Colors => ("SlateSoft", "SlateDeep");
    public override int Order => 50;
    public override string Reads =>
        "CPU, GPU, RAM and motherboard sensors. The game's frame rate only while its page or the overlay shows it; storage, network and frame-time detail only while its page is open.";
    public override string BackgroundWork =>
        "Always while it's on: reads sensors at the sensor update rate (Settings → Performance) and keeps two minutes of history for the charts.";
    public override string Changes => "Nothing. It only reads.";

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage("monitor", "Hardware Monitor", "I.HeartPulse", (app, window) => new HardwareMonitorPage(app, window), PageSensors.Basic, HardwareMonitorPage.SectionsFor),
    };


    public override string? Status(AppServices app)
    {
        var tree = app.Hardware.Tree;
        if (!app.Hardware.Ready) return "Starting sensors…";
        if (app.Hardware.OpenError is { } e) return "Sensors unavailable: " + e;
        return tree is not null ? $"{tree.Devices.Sum(d => d.AllSensors.Count())} sensors" : "Reading sensors";
    }

    protected override void OnStart(AppServices app)
    {
        _app = app;
        app.Hardware.Acquire(Consumer);
        app.Tick += Sample;
    }

    protected override void OnStop(AppServices app)
    {
        app.Tick -= Sample;
        app.Hardware.Release(Consumer);
        app.History.Clear();
    }

    private void Sample() => _app?.History.Sample(_app);
}

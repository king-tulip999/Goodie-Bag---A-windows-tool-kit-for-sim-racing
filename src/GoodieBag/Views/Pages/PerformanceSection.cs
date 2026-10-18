using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Hardware;
using GoodieBag.Network;
using GoodieBag.Overlay;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class PerformanceSection : UserControl, IPage
{
    private const int FrameCount = 300;
    private const string Consumer = "Performance page";
    private const double ChartSeconds = 120;

    private readonly AppServices _app;
    private readonly DashboardGrid _grid = new();
    private readonly DispatcherTimer _frameTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _refresh = new(DispatcherPriority.Background);
    private readonly double?[] _frameBuffer = new double?[FrameCount];
    private readonly double?[] _reading = new double?[MetricHistory.Keys.Length];
    private readonly Dictionary<string, List<double?>> _samples = MetricHistory.Keys.ToDictionary(k => k, _ => new List<double?>());
    private readonly Dictionary<string, Action<Frame>> _updates = new();
    private LineChart? _frameChart;
    private int? _framePid;
    private bool _shown;

    private sealed record Frame(HardwareSnapshot? Hw, NetworkSnapshot? Net, SensorTree? Tree, (int ProcessId, string Name, FrameStats Stats)? Game);

    public FrameworkElement Tools { get; private set; } = null!;

    private int IntervalMs => _app.Config.Performance.IntervalMs;
    private readonly ContentControl _rateHost = new() { Focusable = false };
    private readonly Action? _openSettings;
    private int SampleCapacity => (int)(ChartSeconds * 1000 / IntervalMs);

    public PerformanceSection(AppServices app, Action? openSettings = null)
    {
        _app = app;
        _openSettings = openSettings;
        Content = _grid;
        _frameTimer.Tick += (_, _) => DrawFrameTimes();
        _refresh.Tick += (_, _) =>
        {
            if (_refresh.Interval.TotalMilliseconds != IntervalMs) ApplyRate();
            Refresh(sample: true);
        };
        Build();
    }

    public void OnShown()
    {
        _shown = true;
        _app.Hardware.Acquire(Consumer, fullTree: true);
        _app.Network.Acquire(Consumer);
        _app.Frames.Acquire(Consumer);
        SeedSamples();
        ApplyRate();
        Refresh(sample: false);
        _refresh.Interval = TimeSpan.FromMilliseconds(Math.Min(250, IntervalMs));
        _refresh.Start();
        FillRate();
        _app.PerformanceChanged += OnPerformanceChanged;
    }

    private void OnPerformanceChanged()
    {
        FillRate();
        if (!_shown) return;
        SeedSamples();
        ApplyRate();
        Refresh(sample: false);
    }

    public void OnHidden()
    {
        _shown = false;
        _refresh.Stop();
        _frameTimer.Stop();
        _app.PerformanceChanged -= OnPerformanceChanged;
        _app.Network.Release(Consumer);
        _app.Frames.Release(Consumer);
        _app.Hardware.Release(Consumer);
    }

    private void ApplyRate()
    {
        _refresh.Interval = TimeSpan.FromMilliseconds(IntervalMs);
    }

    private void SeedSamples()
    {
        int per = IntervalMs;
        foreach (var key in MetricHistory.Keys)
        {
            var history = _app.History[key];
            var list = _samples[key];
            list.Clear();
            for (double t = 0; t < history.Count * 1000; t += per)
                list.Add(history[Math.Min(history.Count - 1, (int)(t / 1000))]);
            while (list.Count > SampleCapacity) list.RemoveAt(0);
        }
    }

    private void Sample()
    {
        MetricHistory.Read(_app, _reading);
        for (int i = 0; i < _reading.Length; i++)
        {
            var list = _samples[MetricHistory.Keys[i]];
            list.Add(_reading[i]);
            while (list.Count > SampleCapacity) list.RemoveAt(0);
        }
    }

    private void DrawFrameTimes()
    {
        if (_frameChart is null || (_framePid is null && !_app.Demo))
            return;
        var times = _framePid is { } pid ? _app.Frames.GetFrameTimes(pid, FrameCount) : FrameRateService.SampleFrameTimes(FrameCount);
        for (int i = 0; i < FrameCount; i++)
        {
            int src = times.Length - FrameCount + i;
            _frameBuffer[i] = src >= 0 ? times[src] : null;
        }
        _frameChart.Series[0].Values = _frameBuffer;
        _frameChart.Refresh();
    }

    private void Build()
    {
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        Tools = tools;
        Ui.Anchor(_rateHost, "performance.rate");
        tools.Children.Add(_rateHost);
        FillRate();

        AddPanel("cpu", CpuPanel);
        AddPanel("gpu", GpuPanel);
        AddPanel("memory", MemoryPanel);
        AddPanel("network", NetworkPanel);
        AddPanel("storage", StoragePanel);
        AddPanel("fans", FansPanel);
        AddPanel("motherboard", MotherboardPanel);
        AddPanel("fps", FpsPanel);
        Ui.Anchor(_grid, "performance.live");
    }

    private void FillRate()
    {
        var p = _app.Config.Performance;
        int selected = Array.IndexOf(PerformanceSettings.Presets, p.Rate);
        var segmented = Ui.Segmented(PerformanceSettings.Presets.Select(PerformanceSettings.Name).ToArray(), selected,
            i => _app.SetPerformance(PerformanceSettings.Presets[i]), "Rate",
            PerformanceSettings.Presets.Select(r => (string?)($"{PerformanceSettings.Name(r)}: every {PerformanceSettings.Describe(PerformanceSettings.IntervalFor(r, p.CustomMs))}" + (r switch
            {
                SensorRate.Low => ". The lightest on your PC; good for leaving this page open all day.",
                SensorRate.Fast => ". Smoothest charts; uses a little more CPU.",
                _ => ". The normal rate.",
            }))).ToList());

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };
        var icon = new Icon { Data = Ui.Res<Geometry>("I.Refresh"), Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        icon.SetResourceReference(Icon.ForegroundProperty, "AccentText");
        label.Children.Add(icon);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = "Update rate", FontWeight = FontWeights.SemiBold, FontSize = 13 });
        texts.Children.Add(new TextBlock
        {
            Text = (p.Rate == SensorRate.Custom ? "Custom · " : "") + "every " + PerformanceSettings.Describe(p.IntervalMs),
            Style = Ui.Res<Style>("Caption"), FontSize = 11.5,
        });
        label.Children.Add(texts);
        row.Children.Add(label);
        row.Children.Add(segmented);
        if (_openSettings is not null)
        {
            var more = Ui.IconButton("I.Sliders", "Settings → Performance: the same rate for all of Goodie Bag, custom intervals and background usage", _openSettings);
            more.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(more);
        }
        var frame = new Border { Child = row };
        frame.ToolTip = "How often these cards and every sensor reading in Goodie Bag update (the same setting as Settings → Performance)";
        _rateHost.Content = frame;
    }

    private void AddPanel(string id, Func<PanelSize, (FrameworkElement View, Action<Frame> Update)> build)
    {
        var (view, update) = build(PanelSize.Large);
        _updates[id] = update;
        _grid.Children.Add(view);
    }

    private static readonly SensorTree NotRead = new(Array.Empty<DeviceNode>());

    private (FrameworkElement, Action<Frame>) CpuPanel(PanelSize size)
    {
        var now = new TextBlock();
        var (stats, values) = Stats(("Load", "%"), ("Temp", "°C"), ("Clock", "GHz"));
        var parts = new List<UIElement> { stats };
        Reading? power = null;
        Sparkline? spark = null;
        LineChart? chart = null;
        CoreBars? cores = null;
        Details? details = null;
        if (size >= PanelSize.Medium)
        {
            power = new Reading("Power draw", "How much power the CPU is using, in watts. Higher under load; the label says how busy the CPU is.");
            var left = new StackPanel { Children = { power.Root } };
            cores = new CoreBars();
            left.Children.Add(new TextBlock { Text = "Each core", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 4, 0, 4) });
            left.Children.Add(cores.Root);
            if (size == PanelSize.Medium) left.Children.Add(Spark(out spark, "SlateDeep", "Load, last two minutes"));
            details = new Details();
            parts.Add(Side(left, details.Root, size));
        }
        if (size == PanelSize.Large)
            parts.Add(ChartBlock(out chart, ("Load", "Accent", "%", 0, 100), ("Temperature", "SlateDeep", "°", 20, 100)));
        var view = Card("CPU", "I.Cpu", "SlateSoft", "SlateDeep", now, parts);
        SensorTree? seen = NotRead;
        DeviceNode? cpu = null;
        var cpuSensors = new List<SensorReading>();
        var coreLoads = new List<float?>();
        string? clockText = null, packageText = null, hottestText = null, voltageText = null, busText = null, threadsText = null;
        return (view, f =>
        {
            var hw = f.Hw;
            TextCase.Set(values[0], F(hw?.CpuLoad));
            TextCase.Set(values[1], F(hw?.CpuTemp));
            TextCase.Set(values[2], hw?.CpuClockMhz is { } mhz and > 0 ? $"{mhz / 1000:0.00}" : "–");
            TextCase.Set(now, Verdict(hw?.CpuLoad, "Relaxed", "Boosting").Text);
            power?.Busy(hw?.CpuPower, $"{hw?.CpuPower:0}", "W", hw?.CpuLoad, "Idle", "Working hard");
            if (spark is not null) { spark.Capacity = SampleCapacity; spark.SetValues(_samples["cpuLoad"]); }
            if (chart is not null) Plot(chart, _samples["cpuLoad"], _samples["cpuTemp"]);
            if (!ReferenceEquals(f.Tree, seen))
            {
                seen = f.Tree;
                cpu = null;
                coreLoads.Clear();
                if (f.Tree is { } tree && tree.FirstOfKind(DeviceKind.Cpu) is { } found)
                {
                    cpu = found;
                    cpuSensors.Clear();
                    found.CollectSensors(cpuSensors);
                    var clocks = new List<SensorReading>();
                    foreach (var s in cpuSensors)
                    {
                        if (s.Kind == SensorKind.Load && s.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
                            coreLoads.Add(s.Value);
                        else if (s.Kind == SensorKind.Clock && s.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase) && s.Value > 0)
                            clocks.Add(s);
                    }
                    clockText = clocks.Count == 0 ? null
                        : $"{SensorFormat.Format(SensorKind.Clock, clocks.Average(c => c.Value!.Value))} / {SensorFormat.Format(SensorKind.Clock, clocks.Max(c => c.Value!.Value))}";
                    packageText = V(SensorLookup.Find(found, SensorKind.Power, "Package", "CPU Package"));
                    hottestText = V(SensorLookup.Find(found, SensorKind.Temperature, "Core Max", "Core (Tctl/Tdie)"));
                    voltageText = V(SensorLookup.Find(found, SensorKind.Voltage, "Core (SVI2 TFN)", "Core (SVI3 TFN)", "CPU Core", "Core", "Vcore") ?? SensorLookup.FindAny(tree, DeviceKind.SuperIO, SensorKind.Voltage, "Vcore", "CPU Core"));
                    busText = V(SensorLookup.Find(found, SensorKind.Clock, "Bus Speed"));
                    threadsText = clocks.Count == 0 ? null : coreLoads.Count.ToString();
                }
            }
            int shown = size == PanelSize.Large ? CpuDetails.Length : 5;
            if (cpu is not null)
            {
                cores?.Show(coreLoads);
                details?.Show(shown, Pair(CpuDetails, clockText, packageText, hottestText, Peak(_samples["cpuLoad"], "0'%'"),
                    Peak(_samples["cpuTemp"], "0 °C"), voltageText, busText, threadsText));
            }
            else
            {
                cores?.Show(Array.Empty<float?>());
                details?.Waiting(CpuDetails[..shown]);
            }
        });
    }

    private (FrameworkElement, Action<Frame>) GpuPanel(PanelSize size)
    {
        var now = new TextBlock();
        var (stats, values) = Stats(("Load", "%"), ("Temp", "°C"), ("Clock", "MHz"));
        var parts = new List<UIElement> { stats };
        Reading? hotSpot = null, power = null, fan = null, vram = null;
        LineChart? chart = null;
        Details? details = null;
        if (size >= PanelSize.Medium)
        {
            hotSpot = new Reading("Hot spot", "The hottest point on the graphics chip. Above 105 °C it slows itself down.");
            power = new Reading("Power draw", "How much power the graphics card is using, in watts. A few hundred watts in games is normal for a big card; the label says how busy it is.");
            fan = new Reading("Fan", "How fast the graphics card's fans are spinning.");
            vram = new Reading("Video memory", "Graphics memory in use. Running out makes games stutter or lower their textures.");
            details = new Details();
            parts.Add(Side(new StackPanel { Children = { hotSpot.Root, power.Root, fan.Root, vram.Root } }, details.Root, size));
        }
        if (size == PanelSize.Large)
            parts.Add(ChartBlock(out chart, ("Load", "CoralDeep", "%", 0, 100), ("Temperature", "SlateDeep", "°", 20, 100)));
        var view = Card("Graphics card", "I.Gpu", "CoralSoft", "CoralDeep", now, parts);
        SensorTree? seen = NotRead;
        DeviceNode? gpu = null;
        string? memTemp = null, memClock = null, coreVoltage = null, videoEngine = null;
        return (view, f =>
        {
            var hw = f.Hw;
            TextCase.Set(values[0], F(hw?.GpuLoad));
            TextCase.Set(values[1], F(hw?.GpuTemp));
            TextCase.Set(values[2], F(hw?.GpuClockMhz));
            TextCase.Set(now, Verdict(hw?.GpuLoad, "Relaxed", "Boosting").Text);
            power?.Busy(hw?.GpuPower, $"{hw?.GpuPower:0}", "W", hw?.GpuLoad, "Idle", "Working hard");
            if (hotSpot is not null)
            {
                if (hw?.GpuHotSpot is { } hot)
                    hotSpot.Show($"{hot:0}", "°C", Math.Clamp((hot - 30) / 80, 0, 1),
                        hot >= 105 ? ("Too hot, slowing down", "Danger") : hot >= 95 ? ("Hot", "ApricotDeep") : hot >= 80 ? ("Warm, normal in games", "GoldDeep") : ("Cool", "SageDeep"), null);
                else hotSpot.Unavailable();
            }
            if (fan is not null)
            {
                if (hw?.GpuFanPercent is { } pct)
                    fan.Show($"{pct:0}", "%", pct / 100, pct < 1 ? ("Stopped (quiet mode)", "SageDeep") : pct >= 75 ? ("Loud", "ApricotDeep") : ("Spinning", "SageDeep"),
                        hw.GpuFanRpm is { } rpm ? $"{rpm:N0} RPM" : null);
                else if (hw?.GpuFanRpm is { } rpm2)
                    fan.Show($"{rpm2:N0}", "RPM", null, rpm2 < 1 ? ("Stopped (quiet mode)", "SageDeep") : ("Spinning", "SageDeep"), null);
                else fan.Unavailable();
            }
            if (vram is not null)
            {
                if (hw?.VramUsedMb is { } used && hw.VramTotalMb is { } total and > 0)
                {
                    double share = used / total;
                    vram.Show($"{used / 1024:0.0}", $"of {total / 1024:0} GB", share, share >= 0.9 ? ("Nearly full", "ApricotDeep") : share >= 0.6 ? ("In use", "SlateDeep") : ("Plenty free", "SageDeep"), null);
                }
                else vram.Unavailable();
            }
            if (chart is not null) Plot(chart, _samples["gpuLoad"], _samples["gpuTemp"]);
            if (details is not null)
            {
                if (!ReferenceEquals(f.Tree, seen))
                {
                    seen = f.Tree;
                    gpu = f.Tree?.OfKind(DeviceKind.Gpu)
                        .OrderByDescending(g => SensorLookup.Find(g, SensorKind.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total")?.Value ?? 0).FirstOrDefault();
                    if (gpu is not null)
                    {
                        memTemp = V(SensorLookup.Find(gpu, SensorKind.Temperature, "GPU Memory", "GPU Memory Junction"));
                        memClock = V(SensorLookup.Find(gpu, SensorKind.Clock, "GPU Memory"));
                        coreVoltage = V(SensorLookup.Find(gpu, SensorKind.Voltage, "GPU Core", "GPU Core Voltage"));
                        videoEngine = V(SensorLookup.Find(gpu, SensorKind.Load, "GPU Video Engine", "D3D Video Decode"));
                    }
                }
                if (gpu is null) details.Waiting(GpuDetails);
                else details.Show(Pair(GpuDetails, gpu.Name, memTemp, memClock, coreVoltage, videoEngine,
                    hw?.GpuHotSpot is { } hs && hw.GpuTemp is { } gt ? $"{hs - gt:0} °C" : null,
                    Peak(_samples["gpuLoad"], "0'%'"), Peak(_samples["gpuTemp"], "0 °C")));
            }
        });
    }

    private (FrameworkElement, Action<Frame>) MemoryPanel(PanelSize size)
    {
        var now = new TextBlock();
        var ram = new Reading("RAM in use", "Memory programs are using. Near 100% Windows starts swapping to disk and everything slows down.");
        var parts = new List<UIElement>();
        Reading? vram = null;
        Sparkline? spark = null;
        LineChart? chart = null;
        Details? details = null;
        if (size == PanelSize.Small) parts.Add(ram.Root);
        else
        {
            vram = new Reading("Video memory", "Graphics memory in use.");
            details = new Details();
            var left = new StackPanel { Children = { ram.Root, vram.Root } };
            if (size == PanelSize.Medium) left.Children.Add(Spark(out spark, "PlumDeep", "RAM, last two minutes"));
            parts.Add(Side(left, details.Root, size));
        }
        if (size == PanelSize.Large)
            parts.Add(ChartBlock(out chart, ("RAM", "PlumDeep", "%", 0, 100), ("VRAM", "Plum", "%", 0, 100)));
        var view = Card("Memory", "I.Memory", "PlumSoft", "PlumDeep", now, parts);
        SensorTree? seen = NotRead;
        List<DeviceNode>? modules = null;
        string? speed = null, rated = null;
        return (view, f =>
        {
            var hw = f.Hw;
            if (hw?.RamUsedGb is { } used && hw.RamTotalGb is { } total and > 0)
            {
                double share = used / total;
                ram.Show($"{used:0.0}", $"of {total:0} GB", share, share >= 0.9 ? ("Nearly full", "ApricotDeep") : share >= 0.7 ? ("Busy", "GoldDeep") : ("Plenty free", "SageDeep"), $"{share * 100:0}% in use");
                TextCase.Set(now, $"{share * 100:0}%");
            }
            else
            {
                ram.Unavailable();
                TextCase.Set(now, "");
            }
            if (vram is not null)
            {
                if (hw?.VramUsedMb is { } v && hw.VramTotalMb is { } vt and > 0)
                    vram.Show($"{v / 1024:0.0}", $"of {vt / 1024:0} GB", v / vt, v / vt >= 0.9 ? ("Nearly full", "ApricotDeep") : ("In use", "SlateDeep"), null);
                else vram.Unavailable();
            }
            if (spark is not null) { spark.Capacity = SampleCapacity; spark.SetValues(_samples["ram"]); }
            if (chart is not null) Plot(chart, _samples["ram"], _samples["vram"]);
            if (details is not null)
            {
                if (!ReferenceEquals(f.Tree, seen))
                {
                    seen = f.Tree;
                    modules = f.Tree?.OfKind(DeviceKind.Memory).SelectMany(SensorLookup.Flatten).Where(d => d.Children.Count == 0 && d.Sensors.Any(s => s.Unit == "MT/s")).ToList();
                    speed = modules is null ? null : V(modules.Select(m => SensorLookup.Find(m, SensorKind.Other, "Configured speed")).FirstOrDefault(s => s?.Value is not null));
                    rated = modules is null ? null : V(modules.Select(m => SensorLookup.Find(m, SensorKind.Other, "Rated speed")).FirstOrDefault(s => s?.Value is not null));
                }
                if (modules is null) details.Waiting(MemoryDetails);
                else details.Show(Pair(MemoryDetails,
                    hw?.RamTotalGb is { } t ? $"{t:0.0} GB" : null,
                    hw?.RamUsedGb is { } u && hw.RamTotalGb is { } t2 ? $"{t2 - u:0.0} GB" : null,
                    modules.Count > 0 ? modules.Count.ToString() : null,
                    speed, rated, Peak(_samples["ram"], "0'%'")));
            }
        });
    }

    private (FrameworkElement, Action<Frame>) StoragePanel(PanelSize size)
    {
        var now = new TextBlock();
        var activity = new Reading("Drive activity", "How much of the time your busiest drive is busy. Constantly near 100% can cause hitches while games load.");
        var parts = new List<UIElement>();
        TextBlock[]? rates = null;
        MeterList? drives = null, volumes = null;
        Details? details = null;
        if (size == PanelSize.Small) parts.Add(activity.Root);
        else
        {
            var (row, values) = Stats(("Reading", "MB/s"), ("Writing", "MB/s"));
            rates = values;
            details = new Details();
            parts.Add(Side(new StackPanel { Children = { activity.Root, row } }, details.Root, size));
        }
        if (size == PanelSize.Large)
        {
            drives = new MeterList { TextBelow = true };
            volumes = new MeterList { TextBelow = true };
            var left = new StackPanel { Children = { Section("Each drive"), drives.Root } };
            var right = new StackPanel { Children = { Section("Each volume"), volumes.Root } };
            parts.Add(Side(left, right, size, secondaryMin: ReadingWidth));
        }
        var view = Card("Storage", "I.HardDrive", "GoldSoft", "GoldDeep", now, parts);
        SensorTree? seen = NotRead;
        bool haveStorage = false;
        (string Label, string? Text)[] detailItems = Array.Empty<(string Label, string? Text)>();
        List<(string Key, string Name, string Text, double? Share)>? driveItems = null, volumeItems = null;
        return (view, f =>
        {
            var hw = f.Hw;
            if (hw?.DiskActivePercent is { } disk)
            {
                activity.Show($"{disk:0}", "%", disk / 100, disk >= 90 ? ("Flat out", "ApricotDeep") : disk >= 40 ? ("Busy", "GoldDeep") : ("Quiet", "SageDeep"), null);
                TextCase.Set(now, disk >= 40 ? "Busy" : "Quiet");
            }
            else
            {
                activity.Unavailable();
                TextCase.Set(now, "");
            }
            if (rates is not null)
            {
                TextCase.Set(rates[0], hw?.DiskReadBps is { } r ? $"{r / 1048576.0:0.0}" : "–");
                TextCase.Set(rates[1], hw?.DiskWriteBps is { } w ? $"{w / 1048576.0:0.0}" : "–");
            }
            if (!ReferenceEquals(f.Tree, seen))
            {
                seen = f.Tree;
                var storage = f.Tree?.OfKind(DeviceKind.Storage).ToList();
                haveStorage = storage is not null;
                if (details is not null)
                {
                    var disks = storage?.Where(d => d.Id != "/volumes").ToList();
                    var temps = disks?.Select(d => SensorLookup.Find(d, SensorKind.Temperature, "Temperature", "Composite Temperature")).Where(x => x?.Value is not null).ToList();
                    var vols = storage?.FirstOrDefault(d => d.Id == "/volumes")?.Children
                        .Select(v => (v.Name, Cap: SensorLookup.Find(v, SensorKind.Data, "Capacity")?.Value, Free: SensorLookup.Find(v, SensorKind.Data, "Free")?.Value))
                        .Where(v => v.Cap is > 0 && v.Free is not null).ToList();
                    var fullest = vols?.OrderByDescending(v => 1 - v.Free!.Value / v.Cap!.Value).FirstOrDefault();
                    detailItems = Pair(StorageDetails,
                        disks is null ? null : disks.Count.ToString(),
                        temps is null || temps.Count == 0 ? null : V(temps.MaxBy(x => x!.Value)),
                        vols is null || vols.Count == 0 ? null : $"{SensorFormat.Format(SensorKind.Data, vols.Sum(v => v.Free!.Value))} of {SensorFormat.Format(SensorKind.Data, vols.Sum(v => v.Cap!.Value))}",
                        fullest is { Name: { } n } fv ? $"{n.Split(' ')[0]} {(1 - fv.Free!.Value / fv.Cap!.Value) * 100:0}% full" : null);
                }
                if (drives is not null && volumes is not null)
                {
                    driveItems = storage?.Where(d => d.Id != "/volumes").Select(d =>
                    {
                        var act = SensorLookup.Find(d, SensorKind.Load, "Total Activity");
                        var used = SensorLookup.Find(d, SensorKind.Load, "Used Space");
                        var temp = SensorLookup.Find(d, SensorKind.Temperature, "Temperature", "Composite Temperature");
                        var read = SensorLookup.Find(d, SensorKind.Throughput, "Read Rate");
                        var write = SensorLookup.Find(d, SensorKind.Throughput, "Write Rate");
                        var text = Join(act?.Value is { } a ? $"{a:0}% busy" : null, temp?.Value is { } t ? $"{t:0} °C" : null, used?.Value is { } u ? $"{u:0}% full" : null);
                        if (read?.Value is not null || write?.Value is not null)
                            text = (text.Length == 0 ? "" : text + "\n") + $"{(read?.Value is { } r ? SensorFormat.Rate(r) : "–")} read · {(write?.Value is { } w ? SensorFormat.Rate(w) : "–")} write";
                        return (d.Id, d.Name, text, (act ?? used)?.Value / 100.0);
                    }).ToList();
                    volumeItems = storage?.FirstOrDefault(d => d.Id == "/volumes")?.Children.Select(v =>
                    {
                        var cap = SensorLookup.Find(v, SensorKind.Data, "Capacity")?.Value;
                        var free = SensorLookup.Find(v, SensorKind.Data, "Free")?.Value;
                        return (v.Id, v.Name, cap is { } c && free is { } fr ? $"{SensorFormat.Format(SensorKind.Data, fr)} free of {SensorFormat.Format(SensorKind.Data, c)}" : "",
                            cap is > 0 && free is { } fr2 ? (double?)(1 - fr2 / cap.Value) : null);
                    }).ToList();
                }
            }
            if (haveStorage) details?.Show(detailItems);
            else details?.Waiting(StorageDetails);
            if (drives is null || volumes is null) return;
            drives.Show(driveItems, haveStorage ? "No drive readings on this PC." : "Reading drives…");
            volumes.Show(volumeItems, haveStorage ? "No volume info on this PC." : "Reading volumes…");
        });
    }

    private (FrameworkElement, Action<Frame>) NetworkPanel(PanelSize size)
    {
        var now = new TextBlock();
        var parts = new List<UIElement>();
        TextBlock[]? values = null;
        Reading? ping = null, down = null, up = null;
        LineChart? chart = null;
        if (size == PanelSize.Small)
        {
            var (row, v) = Stats(("Ping", "ms"), ("Down", "Mbit/s"), ("Up", "Mbit/s"));
            values = v;
            parts.Add(row);
        }
        else
        {
            ping = new Reading("Ping", "Time for a tiny message to reach the internet and come back. Under 30 ms is great for online racing; over 100 ms you'll notice it.") { Steady = true };
            down = new Reading("Download", "Data coming in right now, compared with your connection's link speed.") { Steady = true };
            up = new Reading("Upload", "Data going out right now, compared with your connection's link speed.") { Steady = true };
        }
        Details? details = null;
        if (size >= PanelSize.Medium)
        {
            details = new Details();
            parts.Add(Side(new StackPanel { Children = { ping!.Root, down!.Root, up!.Root } }, details.Root, size));
        }
        if (size == PanelSize.Large)
            parts.Add(ChartBlock(out chart, ("Download", "AccentText", "", 0, 125_000), ("Ping", "Apricot", " ms", 0, 100)));
        var view = Card("Network", "I.Wifi", "SageSoft", "SageDeep", now, parts);
        Ui.Anchor(view, "monitor.network");
        return (view, f =>
        {
            var net = f.Net;
            details?.Show(
                ("Connection", net is null ? null : net.Connected ? Join(net.Adapter, net.AdapterKind) : "No connection"),
                ("Link speed", net?.LinkSpeedBps is { } ls ? NetworkMonitor.Bits(ls / 8.0) : null),
                ("Packet loss", net?.LossPercent is { } lost ? $"{lost:0.#}% of last {net.PingsSent} pings" : null),
                ("Ping target", net?.PingHost));
            TextCase.Set(now, net is null ? "" : !net.Connected ? "Offline" : net.Adapter ?? "");
            if (values is not null)
            {
                TextCase.Set(values[0], net is { Connected: true, PingMs: { } p } ? $"{p:0}" : "–");
                TextCase.Set(values[1], net is { Connected: true } ? $"{net.DownBytesPerSec * 8 / 1e6:0.0}" : "–");
                TextCase.Set(values[2], net is { Connected: true } ? $"{net.UpBytesPerSec * 8 / 1e6:0.0}" : "–");
            }
            if (ping is not null)
            {
                if (net is { Connected: true, PingMs: { } p })
                    ping.Show($"{p:0}", "ms", Math.Clamp(p / 150, 0, 1),
                        p < 30 ? ("Great", "SageDeep") : p < 60 ? ("Good", "SageDeep") : p < 100 ? ("OK", "GoldDeep") : ("Laggy", "Danger"),
                        net.LossPercent is > 0 and var loss ? $"{loss:0.#}% of pings lost" : "no pings lost");
                else if (net is { Connected: false }) ping.Unavailable("Offline");
                else if (net is { PingsSent: > 0 }) ping.Unavailable("No reply");
                else ping.Waiting();
                Throughput(down!, net, net?.DownBytesPerSec);
                Throughput(up!, net, net?.UpBytesPerSec);
            }
            if (chart is not null)
            {
                var (d, _, pg) = _app.Network.History();
                chart.Capacity = NetworkMonitor.HistoryLength;
                chart.Series[0].Values = d;
                chart.Series[1].Values = pg;
                chart.Refresh();
            }
        });
    }

    private static void Throughput(Reading reading, NetworkSnapshot? net, double? bytesPerSec)
    {
        if (net is null) { reading.Waiting(); return; }
        if (!net.Connected || bytesPerSec is not { } b) { reading.Unavailable(net.Connected ? null : "Offline"); return; }
        string[] parts = NetworkMonitor.Bits(b).Split(' ', 2);
        double? share = net.LinkSpeedBps is > 0 and var link ? Math.Clamp(b * 8 / link, 0, 1) : null;
        reading.Show(parts[0], parts.Length > 1 ? parts[1] : "", share,
            b < 50_000 ? ("Idle", "SageDeep") : share >= 0.5 ? ("Heavy", "ApricotDeep") : ("In use", "SlateDeep"),
            net.LinkSpeedBps is > 0 and var l ? $"{share * 100:0.#}% of the {NetworkMonitor.Bits(l / 8.0)} link" : null);
    }

    private (FrameworkElement, Action<Frame>) MotherboardPanel(PanelSize size)
    {
        var now = new TextBlock();
        var summary = new Details();
        var parts = new List<UIElement> { summary.Root };
        Details? temps = null, volts = null;
        if (size == PanelSize.Large)
        {
            temps = new Details();
            volts = new Details();
            parts.Add(Side(new StackPanel { Children = { Section("Temperatures"), temps.Root } }, new StackPanel { Children = { Section("Voltages"), volts.Root } }, size, secondaryMin: DetailWidth * 2));
        }
        var view = Card("Motherboard", "I.Board", "ApricotSoft", "ApricotDeep", now, parts);
        SensorTree? seen = NotRead;
        DeviceNode? board = null;
        var sensors = new List<SensorReading>();
        var t = new List<SensorReading>();
        var fanHeaders = new List<SensorReading>();
        var v = new List<SensorReading>();
        (string, string?)[] tItems = Array.Empty<(string, string?)>(), vItems = Array.Empty<(string, string?)>();
        return (view, f =>
        {
            if (!ReferenceEquals(f.Tree, seen))
            {
                seen = f.Tree;
                board = f.Tree?.FirstOfKind(DeviceKind.Motherboard);
                sensors.Clear();
                t.Clear();
                fanHeaders.Clear();
                v.Clear();
                board?.CollectSensors(sensors);
                foreach (var x in sensors)
                {
                    if (x.Kind == SensorKind.Temperature && x.Value is > 0) t.Add(x);
                    else if (x.Kind == SensorKind.Fan) fanHeaders.Add(x);
                    else if (x.Kind == SensorKind.Voltage && x.Value is not null) v.Add(x);
                }
                tItems = t.Select(x => (x.Name, V(x))).ToArray();
                vItems = v.Select(x => (x.Name, V(x))).ToArray();
            }
            TextCase.Set(now, size == PanelSize.Small ? "" : board?.Name ?? "");
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            string? missing = _app.Hardware.PawnIoLikelyMissing ? "Needs PawnIO" : null;
            var items = new List<(string, string?)>();
            foreach (var x in t.Take(size == PanelSize.Small ? 2 : 3)) items.Add((x.Name, V(x)));
            if (t.Count == 0) items.Add(("Temperatures", f.Tree is null ? null : missing));
            if (size >= PanelSize.Medium)
            {
                items.Add(("Fan headers", fanHeaders.Count == 0 ? missing : $"{fanHeaders.Count(x => x.Value > 0)} spinning of {fanHeaders.Count}"));
                var twelve = v.FirstOrDefault(x => x.Name.Contains("12V", StringComparison.OrdinalIgnoreCase));
                items.Add(("Voltages", v.Count == 0 ? missing : $"{v.Count} rails" + (twelve?.Value is { } tv ? $" · +12V {tv:0.00} V" : "")));
            }
            items.Add(("Uptime", up.TotalDays >= 1 ? $"{(int)up.TotalDays} d {up.Hours} h" : $"{up.Hours} h {up.Minutes} m"));
            summary.Show(items.ToArray());
            temps?.Show(tItems);
            volts?.Show(vItems);
        });
    }

    private (FrameworkElement, Action<Frame>) FansPanel(PanelSize size)
    {
        var now = new TextBlock();
        var (stats, values) = Stats(("Spinning", ""), ("Fans", ""), ("Fastest", "RPM"));
        var parts = new List<UIElement> { stats };
        MeterList? list = null;
        if (size >= PanelSize.Medium)
        {
            list = new MeterList { ItemWidth = 200 };
            parts.Add(list.Root);
        }
        var view = Card("Fans", "I.Fan", "GoldSoft", "GoldDeep", now, parts);
        return (view, _ =>
        {
            var fans = _app.Hardware.Catalog?.Fans.Where(x => !IsHiddenFan(x.ControlId)).ToList();
            TextCase.Set(values[0], fans is null ? "–" : fans.Count(x => x.Rpm > 0 || x.Percent > 0).ToString());
            TextCase.Set(values[1], fans is null ? "–" : fans.Count.ToString());
            TextCase.Set(values[2], fans?.Max(x => x.Rpm) is { } top ? $"{top:N0}" : "–");
            TextCase.Set(now, _app.Hardware.Catalog?.Emergency == true ? "At 100% (hot!)" : $"Preset: {_app.Config.Fans.ActivePreset}");
            list?.Show(fans?.Take(size == PanelSize.Large ? 32 : 6).Select(x => (x.ControlId, _app.FanName(x),
                Join(x.HasRpm ? x.Rpm is { } r ? $"{r:N0} RPM" : "RPM –" : null, x.Controllable ? x.Percent is { } p ? $"{p:0}%" : "control –" : null),
                x.Percent / 100.0)).ToList(),
                fans is null ? "Reading fans…" : _app.Hardware.PawnIoLikelyMissing ? "No fans reported. Motherboard fans need the PawnIO driver." : "No fans reported.");
        });
    }

    private (FrameworkElement, Action<Frame>) FpsPanel(PanelSize size)
    {
        var game = TextFit.TipWhenTrimmed(new TextBlock { Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis });
        var (stats, values) = Stats(("Now", ""), ("Average", ""), ("1% low", ""), ("Frame time", "ms"));
        values[0].Foreground = Ui.Res<Brush>("SageDeep");
        var parts = new List<UIElement> { game, stats };
        LineChart? chart = null;
        LineChart? frames = null;
        if (size >= PanelSize.Medium)
        {
            var reset = Ui.LabeledButton("I.Refresh", "Reset FPS statistics", _app.ResetFpsStats, "GhostButton");
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.Margin = new Thickness(0, 6, 0, 0);
            parts.Add(reset);
            chart = new LineChart { Height = size == PanelSize.Large ? 150 : 120, Margin = new Thickness(0, 8, 0, 0) };
            chart.Series.Add(new ChartSeries { Stroke = Ui.Res<Brush>("SageDeep"), Unit = "", Max = 60, AutoMax = true });
            parts.Add(Cap(chart, ChartMax));
        }
        if (size == PanelSize.Large)
        {
            parts.Add(Section("Frame time"));
            parts.Add(new TextBlock { Text = "Every frame, newest on the right. A flat line is smooth; spikes are stutters.", Style = Ui.Res<Style>("Caption") });
            frames = new LineChart { Height = 120, Capacity = FrameCount, StartLabel = $"{FrameCount} frames ago", EndLabel = "latest frame", Margin = new Thickness(0, 6, 0, 0) };
            frames.Series.Add(new ChartSeries { Stroke = Ui.Res<Brush>("Accent"), Unit = " ms", Max = 20, AutoMax = true });
            parts.Add(Cap(frames, ChartMax));
        }
        _frameChart = frames;
        var view = Card("Frame rate", "I.Gauge", "SageSoft", "SageDeep", null, parts);
        Ui.Anchor(view, "performance.fps");
        return (view, f =>
        {
            _framePid = f.Game?.ProcessId;
            bool drawFrames = frames is not null && (_framePid is not null || _app.Demo);
            if (drawFrames != _frameTimer.IsEnabled)
            {
                if (drawFrames) _frameTimer.Start();
                else _frameTimer.Stop();
            }
            if (f.Game is { } g)
            {
                TextCase.Set(values[0], g.Stats.Fps.ToString("0"));
                TextCase.Set(values[1], g.Stats.AverageFps.ToString("0"));
                TextCase.Set(values[2], g.Stats.OnePercentLowFps.ToString("0"));
                TextCase.Set(values[3], $"{g.Stats.FrameTimeMs:0.0}");
                TextCase.Set(game, $"Measuring {g.Name}");
            }
            else
            {
                foreach (var v in values) TextCase.Set(v, "–");
                TextCase.Set(game, _app.Frames.Error ?? (_app.Demo ? "No game running (demo data)" : "Start a game and its frame rate shows up here."));
            }
            if (chart is not null)
            {
                chart.Capacity = SampleCapacity;
                chart.Series[0].Values = _samples["fps"];
                chart.Refresh();
            }
        });
    }

    private static Tile Card(string title, string icon, string soft, string deep, TextBlock? now, IEnumerable<UIElement> parts)
    {
        var stack = new StackPanel();
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Ui.Badge(icon, soft, deep, 28, 8, 15));
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = title, Style = Ui.Res<Style>("H3"), Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(name, 1);
        head.Children.Add(name);
        if (now is not null)
        {
            now.Style = Ui.Res<Style>("Caption");
            now.HorizontalAlignment = HorizontalAlignment.Right;
            now.VerticalAlignment = VerticalAlignment.Center;
            now.TextTrimming = TextTrimming.CharacterEllipsis;
            TextFit.TipWhenTrimmed(now);
            now.MaxWidth = 180;
            Grid.SetColumn(now, 2);
            head.Children.Add(now);
        }
        stack.Children.Add(head);
        foreach (var part in parts) stack.Children.Add(part);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(16, 14, 16, 14), Child = stack };
    }

    private static (UIElement Row, TextBlock[] Values) Stats(params (string Label, string Unit)[] items)
    {
        var row = new UniformGrid { Rows = 1, Margin = new Thickness(0, 2, 0, 4) };
        var values = new TextBlock[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            cell.Children.Add(new TextBlock { Text = items[i].Label, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = items[i].Label });
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            values[i] = new TextBlock { Text = "–", FontSize = 24, FontWeight = FontWeights.SemiBold };
            values[i].SetResourceReference(TextBlock.ForegroundProperty, "Text");
            line.Children.Add(values[i]);
            if (items[i].Unit.Length > 0)
            {
                var unit = new TextBlock { Text = items[i].Unit, FontSize = 12.5, Margin = new Thickness(4, 0, 0, 4), VerticalAlignment = VerticalAlignment.Bottom };
                unit.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
                line.Children.Add(unit);
            }
            cell.Children.Add(new Viewbox { Child = line, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
            row.Children.Add(cell);
        }
        return (Cap(row, items.Length * StatWidth), values);
    }

    private static TextBlock Section(string text) => new() { Text = text, Style = Ui.Res<Style>("H3"), Margin = new Thickness(0, 10, 0, 4) };

    private const double StatWidth = 120, ReadingWidth = 260, DetailWidth = 140, ChartMax = 640, SparkMax = 360;

    private static SplitPanel Side(UIElement primary, UIElement secondary, PanelSize size, double secondaryMin = DetailWidth) =>
        new() { PrimaryWidth = ReadingWidth, SecondaryMin = secondaryMin, DropWhenNarrow = size < PanelSize.Large, Children = { primary, secondary } };

    private static string? Peak(List<double?> samples, string format)
    {
        double? max = null;
        foreach (var v in samples)
            if (v is { } x && !double.IsNaN(x) && (max is null || x > max)) max = x;
        return max?.ToString(format);
    }

    private static Grid Cap(UIElement element, double max)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { MaxWidth = max });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(element);
        return grid;
    }

    private static TextBlock Note(string text) => new() { Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap };

    private static UIElement Spark(out Sparkline spark, string color, string caption)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(new TextBlock { Text = caption, Style = Ui.Res<Style>("Caption") });
        spark = new Sparkline { Height = 40, Margin = new Thickness(0, 6, 0, 0), Stroke = Ui.Res<Brush>(color), Capacity = 240, Min = 0, Max = 100 };
        stack.Children.Add(spark);
        return Cap(stack, SparkMax);
    }

    private static UIElement ChartBlock(out LineChart chart, (string Label, string Color, string Unit, double Min, double Max) a, (string Label, string Color, string Unit, double Min, double Max) b)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var legend = new StackPanel { Orientation = Orientation.Horizontal };
        legend.Children.Add(Legend(a.Label, a.Color, false));
        legend.Children.Add(Legend(b.Label, b.Color, true));
        stack.Children.Add(legend);
        chart = new LineChart { Height = 150, Margin = new Thickness(0, 6, 0, 0) };
        bool bits = a.Max > 1000;
        chart.Series.Add(new ChartSeries { Stroke = Ui.Res<Brush>(a.Color), Unit = a.Unit, Min = a.Min, Max = a.Max, AutoMax = bits, Format = bits ? v => NetworkMonitor.Bits(v) : null });
        chart.Series.Add(new ChartSeries { Stroke = Ui.Res<Brush>(b.Color), Unit = b.Unit, Min = b.Min, Max = b.Max });
        stack.Children.Add(chart);
        return Cap(stack, ChartMax);
    }

    private void Plot(LineChart chart, IReadOnlyList<double?> a, IReadOnlyList<double?> b)
    {
        chart.Capacity = SampleCapacity;
        chart.Series[0].Values = a;
        chart.Series[1].Values = b;
        chart.Refresh();
    }

    private static StackPanel Legend(string label, string color, bool dashed)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
        s.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = 0, X2 = 16, Y1 = 6, Y2 = 6, Stroke = Ui.Res<Brush>(color), StrokeThickness = 2,
            StrokeDashArray = dashed ? new DoubleCollection { 2, 1.5 } : null, Width = 16, Height = 12, VerticalAlignment = VerticalAlignment.Center,
        });
        s.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(6, 0, 0, 0) });
        return s;
    }

    private bool IsHiddenFan(string controlId)
    {
        var all = _app.Config.Fans.Hardware;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].ControlId == controlId)
                return all[i].Hidden;
        }
        return false;
    }

    private static (string Text, string Color) Verdict(float? load, string idle, string busy) =>
        load is not { } l ? ("", "TextMuted") : l >= 60 ? (busy, "SlateDeep") : l >= 15 ? ("Light work", "SageDeep") : (idle, "SageDeep");

    private void Refresh(bool sample)
    {
        if (sample) Sample();
        var frame = CurrentFrame();
        foreach (var update in _updates.Values)
            update(frame);
    }

    private Frame CurrentFrame() => new(_app.Hardware.Latest, _app.Network.Latest, _app.Hardware.Tree, _app.Frames.GetMostActive());

    private static string F(float? v) => v is { } x ? x.ToString("0") : "–";

    private static string? V(SensorReading? r) => r?.Value is { } v ? r.Format(v) : null;

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    private sealed class CoreBars
    {
        public UniformGrid Root { get; } = new() { Rows = 1, Height = 44 };
        private readonly List<Border> _bars = new();
        private readonly List<float?> _loads = new();

        public void Show(IReadOnlyList<float?> loads)
        {
            if (loads.Count != _bars.Count)
            {
                Root.Children.Clear();
                _bars.Clear();
                for (int i = 0; i < loads.Count; i++)
                {
                    int core = i;
                    var bar = new Border { CornerRadius = ThemeManager.Round(2), VerticalAlignment = VerticalAlignment.Bottom, ToolTip = "" };
                    bar.ToolTipOpening += (_, _) => bar.ToolTip = $"Core {core + 1}: {(core < _loads.Count ? _loads[core] : null):0}%";
                    var cell = new Border { CornerRadius = ThemeManager.Round(3), Margin = new Thickness(1, 0, 1, 0), Child = bar };
                    cell.SetResourceReference(Border.BackgroundProperty, "MeterTrack");
                    Root.Children.Add(cell);
                    _bars.Add(bar);
                }
                if (loads.Count == 0) Root.Children.Add(Note("Per-core load isn't available yet."));
            }
            _loads.Clear();
            for (int i = 0; i < loads.Count; i++)
            {
                _loads.Add(loads[i]);
                double load = loads[i] is { } l && !float.IsNaN(l) ? Math.Clamp(l, 0, 100) / 100 : 0;
                _bars[i].Height = load * 44;
                _bars[i].Background = ThemeManager.RampBrush(load);
            }
        }
    }

    private static readonly string[] CpuDetails = { "Clock (avg / max)", "Package power", "Hottest core", "Peak load, 2 min", "Peak temp, 2 min", "Core voltage", "Bus speed", "Threads" };
    private static readonly string[] GpuDetails = { "Model", "Memory temp", "Memory clock", "Core voltage", "Video engine", "Hot spot − core", "Peak load, 2 min", "Peak temp, 2 min" };
    private static readonly string[] MemoryDetails = { "Total", "Available", "Modules", "Speed", "Rated speed", "Peak RAM, 2 min" };
    private static readonly string[] StorageDetails = { "Drives", "Hottest drive", "Free space", "Fullest volume" };

    private static (string Label, string? Text)[] Pair(string[] labels, params string?[] texts) => labels.Select((l, i) => (l, i < texts.Length ? texts[i] : null)).ToArray();

    private sealed class Details
    {
        public FlowPanel Root { get; } = new() { ItemWidth = DetailWidth, Margin = new Thickness(0, 6, 0, 0) };
        private readonly List<TextBlock> _values = new();
        private readonly List<string> _labels = new();
        private readonly List<bool> _missing = new();

        public void Waiting(params string[] labels)
        {
            Show(labels.Select(l => (l, (string?)null)).ToArray());
            foreach (var value in _values) TextCase.Set(value, "…");
        }

        public void Show(int count, params (string Label, string? Text)[] items) => Show(items.Length <= count ? items : items[..count]);

        public void Show(params (string Label, string? Text)[] items)
        {
            bool same = items.Length == _labels.Count && Root.Children.Count == _values.Count;
            for (int i = 0; same && i < items.Length; i++)
                same = items[i].Label == _labels[i];
            if (!same)
            {
                Root.Children.Clear();
                _values.Clear();
                _labels.Clear();
                _missing.Clear();
                foreach (var (label, _) in items)
                {
                    _labels.Add(label);
                    var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 6) };
                    cell.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis }));
                    var value = TextFit.TipWhenTrimmed(new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                    value.SetResourceReference(TextBlock.ForegroundProperty, "Text");
                    cell.Children.Add(value);
                    Root.Children.Add(cell);
                    _values.Add(value);
                    _missing.Add(false);
                }
            }
            for (int i = 0; i < items.Length; i++)
            {
                bool missing = items[i].Text is null;
                TextCase.Set(_values[i], items[i].Text ?? "–");
                if (missing == _missing[i]) continue;
                _missing[i] = missing;
                _values[i].SetResourceReference(TextBlock.ForegroundProperty, missing ? "TextFaint" : "Text");
            }
        }
    }

    private sealed class MeterList
    {
        public FlowPanel Root { get; } = new() { ItemWidth = ReadingWidth };
        private readonly List<(TextBlock Text, Meter Bar)> _rows = new();

        public bool TextBelow { get; init; }

        public double ItemWidth { init => Root.ItemWidth = value; }
        private readonly List<string> _keys = new();
        private string? _empty;

        public void Show(IReadOnlyList<(string Key, string Name, string Text, double? Share)>? items, string empty)
        {
            bool none = items is null || items.Count == 0;
            bool same = none ? _empty == empty : _empty is null && items!.Count == _keys.Count;
            for (int i = 0; same && !none && i < _keys.Count; i++)
                same = items![i].Key == _keys[i];
            if (!same)
            {
                Root.Children.Clear();
                _rows.Clear();
                _keys.Clear();
                _empty = null;
                if (none)
                {
                    _empty = empty;
                    Root.Children.Add(Note(empty));
                    return;
                }
                foreach (var item in items!)
                {
                    _keys.Add(item.Key);
                    var grid = new Grid { Margin = new Thickness(0, 3, 20, 5) };
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.RowDefinitions.Add(new RowDefinition());
                    grid.RowDefinitions.Add(new RowDefinition());
                    grid.RowDefinitions.Add(new RowDefinition());
                    grid.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) }));
                    var text = TextFit.TipWhenTrimmed(new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
                    if (TextBelow)
                    {
                        Grid.SetRow(text, 2);
                        Grid.SetColumnSpan(text, 2);
                        text.Margin = new Thickness(0, 3, 0, 0);
                    }
                    else Grid.SetColumn(text, 1);
                    grid.Children.Add(text);
                    var bar = new Meter { Height = 5, Tiered = true, Margin = new Thickness(0, 5, 0, 0) };
                    Grid.SetRow(bar, 1);
                    Grid.SetColumnSpan(bar, 2);
                    grid.Children.Add(bar);
                    Root.Children.Add(grid);
                    _rows.Add((text, bar));
                }
            }
            if (items is null) return;
            for (int i = 0; i < items.Count && i < _rows.Count; i++)
            {
                double? share = items[i].Share is { } v && !double.IsNaN(v) ? v : null;
                TextCase.Set(_rows[i].Text, items[i].Text);
                _rows[i].Bar.Value = Math.Clamp(share ?? 0, 0, 1);
                _rows[i].Bar.Visibility = share is null ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    private sealed class Reading
    {
        public Grid Root { get; } = new() { Margin = new Thickness(0, 4, 0, 6) };
        private readonly TextBlock _value = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
        private readonly TextBlock _unit = new() { FontSize = 13, Margin = new Thickness(5, 0, 0, 3), VerticalAlignment = VerticalAlignment.Bottom };
        private readonly TextBlock _verdict = new() { FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        private readonly Tile _pill = new() { CornerRadius = ThemeManager.Round(9), Padding = new Thickness(8, 1, 8, 2), VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _track = new() { Height = 5, CornerRadius = ThemeManager.Round(2.5), Margin = new Thickness(0, 6, 0, 0) };
        private readonly Border _fill = new() { Height = 5, CornerRadius = ThemeManager.Round(2.5), HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock _detail = TextFit.TipWhenTrimmed(new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
        private double _share;
        public bool Steady { get; init; }
        private Visibility Gone => Steady ? Visibility.Hidden : Visibility.Collapsed;
        private string? _valueColor, _color;

        private void SetValueColor(string key)
        {
            if (key == _valueColor) return;
            _valueColor = key;
            _value.SetResourceReference(TextBlock.ForegroundProperty, key);
        }

        public Reading(string label, string help)
        {
            Root.ToolTip = help;
            Root.ColumnDefinitions.Add(new ColumnDefinition());
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption") });
            var value = new StackPanel { Orientation = Orientation.Horizontal };
            value.Children.Add(_value);
            _unit.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            value.Children.Add(_unit);
            left.Children.Add(value);
            Root.Children.Add(left);
            _pill.Child = _verdict;
            _pill.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            Grid.SetColumn(_pill, 1);
            Root.Children.Add(_pill);

            var bottom = new StackPanel();
            _track.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            _track.Child = _fill;
            _track.SizeChanged += (_, _) => _fill.Width = _track.ActualWidth * _share;
            bottom.Children.Add(_track);
            _detail.Style = Ui.Res<Style>("Caption");
            bottom.Children.Add(_detail);
            Grid.SetRow(bottom, 1);
            Grid.SetColumnSpan(bottom, 2);
            Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Root.Children.Add(bottom);
            Unavailable();
        }

        public void Busy(float? value, string text, string unit, float? load, string idle, string busy)
        {
            if (value is not { } v || v <= 0) { Unavailable(); return; }
            Show(text, unit, null, Verdict(load, idle, busy), null);
        }

        public void Show(string value, string unit, double? share, (string Text, string Color) verdict, string? detail)
        {
            TextCase.Set(_value, value);
            SetValueColor("Text");
            TextCase.Set(_unit, unit);
            TextCase.Set(_verdict, verdict.Text);
            if (verdict.Color != _color)
            {
                _color = verdict.Color;
                _verdict.SetResourceReference(TextBlock.ForegroundProperty, _color);
                _fill.SetResourceReference(Border.BackgroundProperty, _color);
            }
            _pill.Visibility = verdict.Text.Length > 0 ? Visibility.Visible : Gone;
            if (share is { } nan && double.IsNaN(nan)) share = null;
            _share = Math.Clamp(share ?? 0, 0, 1);
            _track.Visibility = share is null ? Gone : Visibility.Visible;
            _fill.Width = _track.ActualWidth * _share;
            TextCase.Set(_detail, detail ?? "");
            _detail.Visibility = detail is null && !Steady ? Visibility.Collapsed : Visibility.Visible;
        }

        public void Waiting()
        {
            Unavailable("…");
            TextCase.Set(_detail, "Measuring…");
        }

        public void Unavailable(string? why = null)
        {
            TextCase.Set(_value, why ?? "–");
            SetValueColor("TextFaint");
            TextCase.Set(_unit, "");
            _pill.Visibility = Gone;
            _track.Visibility = Gone;
            TextCase.Set(_detail, why is null ? "This PC doesn't report it." : "");
            _detail.Visibility = Visibility.Visible;
        }
    }
}

internal sealed class SplitPanel : Panel
{
    public double PrimaryWidth { get; set; } = 260;
    public double SecondaryMin { get; set; } = 140;
    public double Gap { get; set; } = 20;
    public bool DropWhenNarrow { get; set; }

    private bool SideBySide(double width) => width >= PrimaryWidth + Gap + SecondaryMin;

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? PrimaryWidth + Gap + SecondaryMin : available.Width;
        if (InternalChildren.Count < 2) return Measure0(width);
        UIElement a = InternalChildren[0], b = InternalChildren[1];
        if (SideBySide(width))
        {
            a.Measure(new Size(PrimaryWidth, double.PositiveInfinity));
            b.Measure(new Size(width - PrimaryWidth - Gap, double.PositiveInfinity));
            return new Size(width, Math.Max(a.DesiredSize.Height, b.DesiredSize.Height));
        }
        a.Measure(new Size(width, double.PositiveInfinity));
        if (DropWhenNarrow)
        {
            b.Measure(new Size(0, 0));
            return new Size(width, a.DesiredSize.Height);
        }
        b.Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, a.DesiredSize.Height + b.DesiredSize.Height);
    }

    private Size Measure0(double width)
    {
        if (InternalChildren.Count == 0) return new Size();
        InternalChildren[0].Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, InternalChildren[0].DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (InternalChildren.Count == 0) return final;
        UIElement a = InternalChildren[0];
        if (InternalChildren.Count < 2) { a.Arrange(new Rect(final)); return final; }
        UIElement b = InternalChildren[1];
        if (SideBySide(final.Width))
        {
            a.Arrange(new Rect(0, 0, PrimaryWidth, a.DesiredSize.Height));
            b.Arrange(new Rect(PrimaryWidth + Gap, 0, final.Width - PrimaryWidth - Gap, b.DesiredSize.Height));
        }
        else
        {
            a.Arrange(new Rect(0, 0, final.Width, a.DesiredSize.Height));
            b.Arrange(DropWhenNarrow ? new Rect() : new Rect(0, a.DesiredSize.Height, final.Width, b.DesiredSize.Height));
        }
        return final;
    }
}

internal sealed class FlowPanel : Panel
{
    public double ItemWidth { get; set; } = 300;

    private (int Cols, double Cell) Cells(double width)
    {
        int cols = Math.Max(1, (int)(width / ItemWidth));
        return (cols, width / cols);
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? ItemWidth * Math.Max(1, InternalChildren.Count) : available.Width;
        var (cols, cell) = Cells(width);
        double height = 0, row = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(cell, double.PositiveInfinity));
            row = Math.Max(row, child.DesiredSize.Height);
            if (i % cols == cols - 1 || i == InternalChildren.Count - 1)
            {
                height += row;
                row = 0;
            }
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (cols, cell) = Cells(final.Width);
        double y = 0;
        for (int start = 0; start < InternalChildren.Count; start += cols)
        {
            int end = Math.Min(start + cols, InternalChildren.Count);
            double row = 0;
            for (int i = start; i < end; i++) row = Math.Max(row, InternalChildren[i].DesiredSize.Height);
            for (int i = start; i < end; i++) InternalChildren[i].Arrange(new Rect((i - start) * cell, y, cell, row));
            y += row;
        }
        return final;
    }
}

internal sealed class DashboardGrid : Panel
{
    public double MinCellWidth { get; init; } = 440;
    public int MaxColumns { get; init; } = 3;
    public double Gap { get; init; } = 14;

    private readonly List<Rect> _rects = new();

    private int Columns(double width) => Math.Clamp((int)((width + Gap) / (MinCellWidth + Gap)), 1, MaxColumns);

    private double CellWidth(double width, int inRow) => Math.Max(0, Math.Floor((width - Gap * (inRow - 1)) / inRow));

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? MaxColumns * (MinCellWidth + Gap) - Gap : available.Width;
        int n = InternalChildren.Count, cols = Columns(width);
        _rects.Clear();
        double y = 0;
        for (int start = 0; start < n; start += cols)
        {
            int inRow = Math.Min(cols, n - start);
            double cell = CellWidth(width, inRow), height = 0;
            for (int k = 0; k < inRow; k++)
            {
                var child = InternalChildren[start + k];
                child.Measure(new Size(cell, double.PositiveInfinity));
                height = Math.Max(height, child.DesiredSize.Height);
            }
            for (int k = 0; k < inRow; k++)
                _rects.Add(new Rect(k * (cell + Gap), y, cell, height));
            y += height + Gap;
        }
        return new Size(width, Math.Max(0, y - Gap));
    }

    protected override Size ArrangeOverride(Size final)
    {
        for (int i = 0; i < InternalChildren.Count && i < _rects.Count; i++)
            InternalChildren[i].Arrange(_rects[i]);
        return final;
    }
}

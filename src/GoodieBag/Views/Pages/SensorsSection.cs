using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.Hardware;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class SensorsSection : UserControl, IPage, IScreenshotStates, ICancelEdit
{
    private readonly AppServices _app;
    private readonly StackPanel _body = new();
    private readonly PanelGrid _grid;
    private readonly Button _arrange;
    private readonly CollapseAllButton _foldAll = new();
    private readonly TextBlock _state = new();
    private List<DeviceNode> _listed = new();
    private readonly HashSet<string> _collapsed = new();
    private readonly HashSet<string> _arrangeOpen = new();
    private bool _arranging;
    private readonly Dictionary<string, (TextBlock Value, TextBlock Min, TextBlock Max, FrameworkElement Row, string Text, string Device)> _rows = new();
    private readonly List<(FrameworkElement Header, string Device, List<FrameworkElement> Children, Icon Chevron, bool Top)> _groups = new();
    private readonly List<StackPanel> _tables = new();
    private (int Hash, int Count)? _signature;
    private SensorTree? _lastTree;
    private readonly Tile _loading = new();

    public SensorsSection(AppServices app)
    {
        _app = app;
        Content = _body;
        _grid = new PanelGrid(app.Config.Ui.SensorsLayout, app.Save);
        _arrange = Ui.LabeledButton("I.Move", "Arrange", () => SetArranging(!_grid.Editing), "GhostButton");
        Build();
    }

    public FrameworkElement Tools { get; private set; } = null!;

    private const string TreeConsumer = "Hardware Monitor sensors";

    public void OnShown()
    {
        _app.Hardware.Acquire(TreeConsumer, fullTree: true);
        Tick();
        _app.Tick += Tick;
    }

    public void OnHidden()
    {
        SetArranging(false);
        _app.Tick -= Tick;
        _app.Hardware.Release(TreeConsumer);
    }

    private EditUndo? _undo;

    public bool CancelEdit()
    {
        if (!_grid.Editing) return false;
        var undo = _undo;
        SetArranging(false);
        if (undo?.Restore(_app) == true) _grid.InvalidateMeasure();
        return true;
    }

    private void SetArranging(bool on)
    {
        if (on != _grid.Editing) _undo = on ? new EditUndo().Slots(_app.Config.Ui.SensorsLayout) : null;
        _grid.Editing = on;
        _arrange.Style = Ui.Res<Style>(on ? "PrimaryButton" : "GhostButton");
        ((TextBlock)((StackPanel)_arrange.Content).Children[1]).Text = on ? "Done" : "Arrange";
        ((StackPanel)_arrange.Content).Children[0].Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (_arranging == on) return;
        _arranging = on;
        _arrangeOpen.Clear();
        ShowFolds();
    }

    private bool IsCollapsed(string id, bool top) => _arranging && top ? !_arrangeOpen.Contains(id) : _collapsed.Contains(id);

    private void ShowFolds()
    {
        foreach (var g in _groups)
        {
            bool folded = IsCollapsed(g.Device, g.Top);
            g.Chevron.Data = Ui.Res<Geometry>(folded ? "I.ChevronRight" : "I.ChevronDown");
            if (g.Top && g.Header is Border head) head.Padding = new Thickness(0, 2, 0, folded ? 2 : 6);
        }
        ApplyFolds();
        var tops = _groups.Where(g => g.Top).ToList();
        _foldAll.SetOpen(tops.Count(g => !IsCollapsed(g.Device, true)), tops.Count);
    }

    private void ToggleDevice(string id, bool top)
    {
        var set = _arranging && top ? _arrangeOpen : _collapsed;
        if (!set.Remove(id))
            set.Add(id);
        ShowFolds();
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("arrange", () => { SetArranging(true); return Task.CompletedTask; });
    }

    private void Build()
    {
        _state.Style = Ui.Res<Style>("Caption");
        _state.Margin = new Thickness(2, 0, 0, 10);
        _body.Children.Add(_state);

        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        Tools = tools;
        var reset = Ui.LabeledButton("I.Refresh", "Reset min / max", () =>
        {
            _app.Hardware.ResetMinMax();
            _app.Notify("Min / max reset", "Tracking starts again from now.", NoticeKind.Success);
        });
        tools.Children.Add(reset);
        _foldAll.Margin = new Thickness(8, 0, 0, 0);
        _foldAll.Requested += fold => { if (fold) CollapseAll(); else ExpandAll(); };
        tools.Children.Add(_foldAll);
        var devices = Ui.LabeledButton("I.Eye", "Devices", () => { }, "GhostButton");
        devices.Margin = new Thickness(8, 0, 0, 0);
        devices.ToolTip = "Show or hide devices";
        devices.Click += (_, _) => OpenDevicesMenu(devices);
        tools.Children.Add(devices);
        var gridTools = _grid.TakeEditTools();
        gridTools.Margin = new Thickness(8, 0, 0, 0);
        tools.Children.Add(gridTools);
        _arrange.Margin = new Thickness(8, 0, 0, 0);
        _arrange.ToolTip = "Move the devices up and down";
        tools.Children.Add(_arrange);

        var loading = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 36, 0, 36) };
        loading.Children.Add(new LoadingSpinner(48) { Margin = new Thickness(0, 0, 0, 16) });
        loading.Children.Add(new TextBlock { Text = "Reading sensors…", Style = Ui.Res<Style>("H2"), HorizontalAlignment = HorizontalAlignment.Center });
        loading.Children.Add(new TextBlock
        {
            Text = "The first read loads the sensor driver and scans every device. This usually takes a few seconds; readings appear here as soon as they're in.",
            Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 460,
        });
        _loading.Style = Ui.Res<Style>("Card");
        _loading.Child = loading;
        _loading.Margin = new Thickness(0, 0, 0, 12);
        _body.Children.Add(_loading);

        _body.Children.Add(_grid);

        _body.Children.Add(new TextBlock
        {
            Text = "Sources: the hardware's own sensors (CPU, GPU, RAM, motherboard Super I/O, storage SMART, network), Windows WMI (memory modules and speed) and Windows volume info (capacity and free space). " +
                   "CPU temperatures and motherboard sensors need the PawnIO driver (Settings → Hardware access). Anything your hardware or driver doesn't expose is left out rather than estimated.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 14, 0, 0), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left,
        });
    }

    private const double HoverLead = 8;

    private static Grid Row(UIElement name, UIElement value, UIElement min, UIElement max, double indent, bool header = false)
    {
        double lead = header ? 0 : HoverLead;
        var grid = new Grid { Margin = new Thickness(indent - lead, 0, -lead, 0), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110 + lead) });
        int col = 0;
        foreach (var e in new[] { name, value, min, max })
        {
            if (col > 0 && e is TextBlock t)
            {
                t.TextAlignment = TextAlignment.Right;
                t.VerticalAlignment = VerticalAlignment.Center;
            }
            if (!header && e is FrameworkElement fe)
                fe.Margin = new Thickness(col == 0 ? lead : 0, 3, col == 3 ? lead : 0, 3);
            Grid.SetColumn(e, col++);
            grid.Children.Add(e);
        }
        if (!header)
        {
            grid.MouseEnter += (_, _) => grid.SetResourceReference(Panel.BackgroundProperty, "SurfaceAlt");
            grid.MouseLeave += (_, _) => grid.Background = Brushes.Transparent;
        }
        return grid;
    }

    private void Tick()
    {
        var hw = _app.Hardware;
        var tree = hw.Tree;
        _loading.Visibility = tree is null && hw.OpenError is null ? Visibility.Visible : Visibility.Collapsed;
        _grid.Visibility = tree is null ? Visibility.Collapsed : Visibility.Visible;
        if (tree is null)
        {
            TextCase.Set(_state, hw.OpenError is { } openError ? "Sensors unavailable: " + openError : "Reading sensors…");
            return;
        }
        if (ReferenceEquals(tree, _lastTree) && _signature is not null) return;
        _lastTree = tree;

        var listed = Listed(tree);
        var hash = new HashCode();
        int count = 0;
        foreach (var device in listed)
        {
            hash.Add(device.Id);
            foreach (var child in device.Children) hash.Add(HasReadings(child));
            foreach (var sensor in device.AllSensors)
            {
                hash.Add(sensor.Id);
                count++;
            }
        }
        TextCase.Set(_state, hw.OpenError is { } error ? "Sensors unavailable: " + error
            : $"{count} readings from {listed.Count} devices · updated every {Config.PerformanceSettings.Describe(_app.Config.Performance.IntervalMs)}" +(_app.Demo ? " · demo data" : ""));
        var signature = (hash.ToHashCode(), count);
        if (signature != _signature)
        {
            _signature = signature;
            BuildCards(listed);
        }

        foreach (var device in listed)
        {
            foreach (var s in device.AllSensors)
            {
                if (!_rows.TryGetValue(s.Id, out var row))
                    continue;
                TextCase.Set(row.Value, s.Format(s.Value));
                TextCase.Set(row.Min, s.Min is null ? "" : s.Format(s.Min));
                TextCase.Set(row.Max, s.Max is null ? "" : s.Format(s.Max));
            }
        }
    }

    private List<DeviceNode> Listed(SensorTree tree)
    {
        var devices = tree.Devices.Where(HasReadings).ToList();
        var nics = devices.Where(d => d.Kind == DeviceKind.Network).ToList();
        if (nics.Count > 1)
        {
            var keep = nics.FirstOrDefault(n => n.Name == _app.Network.Latest?.Adapter) ?? nics[0];
            devices.RemoveAll(d => d.Kind == DeviceKind.Network && d != keep);
        }
        return devices;
    }

    private static bool HasReadings(DeviceNode device)
    {
        foreach (var s in device.AllSensors)
            if (s.Value is { } v && !float.IsNaN(v) && (device.Kind != DeviceKind.Network || v != 0))
                return true;
        return false;
    }

    private void BuildCards(List<DeviceNode> devices)
    {
        bool editing = _grid.Editing;
        _listed = devices;
        _grid.Clear();
        _rows.Clear();
        _groups.Clear();
        _tables.Clear();
        foreach (var device in devices.Where(d => !_app.Config.Ui.HiddenSensorDevices.Contains(d.Id)))
        {
            var table = new StackPanel();
            _tables.Add(table);
            AddDevice(table, device, device.Name, 0);
            var card = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(18, 12, 18, 12), Child = table };
            string id = device.Id;
            _grid.Add(new PanelDef(id, device.Name, _ => card)
            {
                FullWidth = true,
                VeilClick = () => ToggleDevice(id, top: true),
                VeilClickTip = "show or hide its sensors",
            });
        }
        _grid.Editing = editing;
        ApplyFolds();
    }

    private void AddDevice(StackPanel table, DeviceNode device, string topName, double indent)
    {
        var children = new List<FrameworkElement>();
        bool top = indent == 0;
        var chevron = new Icon { Data = Ui.Res<Geometry>(IsCollapsed(device.Id, top) ? "I.ChevronRight" : "I.ChevronDown"), Width = 14, Height = 14, Foreground = Ui.Res<Brush>("TextMuted") };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(chevron);
        var badge = Ui.Badge(IconFor(device.Kind), SoftFor(device.Kind), DeepFor(device.Kind), 26, 8, 14);
        badge.Margin = new Thickness(6, 0, 10, 0);
        title.Children.Add(badge);
        title.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = device.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }));
        if (device.Source != "LibreHardwareMonitor")
            title.Children.Add(new TextBlock { Text = "  " + device.Source, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
        UIElement head = indent > 0 ? title : Row(title, Ui.Text("Value", "Eyebrow"), Ui.Text("Min", "Eyebrow"), Ui.Text("Max", "Eyebrow"), 0, header: true);
        var headerRow = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(indent, indent > 0 ? 10 : 2, 0, 6), Child = head };
        table.Children.Add(headerRow);

        foreach (var group in device.Sensors.GroupBy(s => SensorFormat.GroupName(s.Kind)).OrderBy(g => GroupOrder(g.First().Kind)))
        {
            var groupHeader = new TextBlock { Text = group.Key, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(indent + 46, 6, 0, 2) };
            table.Children.Add(groupHeader);
            children.Add(groupHeader);
            foreach (var sensor in group)
            {
                var value = new TextBlock { FontWeight = FontWeights.SemiBold };
                var min = new TextBlock { Foreground = Ui.Res<Brush>("TextMuted") };
                var max = new TextBlock { Foreground = Ui.Res<Brush>("TextMuted") };
                var row = Row(TextFit.TipWhenTrimmed(new TextBlock { Text = sensor.Name, TextTrimming = TextTrimming.CharacterEllipsis }), value, min, max, indent + 58);
                table.Children.Add(row);
                children.Add(row);
                _rows[sensor.Id] = (value, min, max, row, sensor.Name + " " + group.Key, topName + " " + device.Name);
            }
        }

        int before = table.Children.Count;
        foreach (var child in device.Children.Where(HasReadings))
            AddDevice(table, child, topName, indent + 20);
        for (int i = before; i < table.Children.Count; i++)
            children.Add((FrameworkElement)table.Children[i]);

        _groups.Add((headerRow, device.Id, children, chevron, top));
        headerRow.MouseLeftButtonUp += (_, _) =>
        {
            if (_grid.Editing) return;
            ToggleDevice(device.Id, top);
        };
    }

    public void CollapseAll()
    {
        if (_arranging) _arrangeOpen.Clear();
        foreach (var g in _groups) _collapsed.Add(g.Device);
        ShowFolds();
    }

    public void ExpandAll()
    {
        if (_arranging) foreach (var g in _groups.Where(g => g.Top)) _arrangeOpen.Add(g.Device);
        _collapsed.Clear();
        ShowFolds();
    }

    private void OpenDevicesMenu(Button button)
    {
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = 220 };
        var hidden = _app.Config.Ui.HiddenSensorDevices;
        foreach (var device in _listed)
        {
            string id = device.Id;
            var item = new MenuItem { Header = device.Name, IsCheckable = true, IsChecked = !hidden.Contains(id), StaysOpenOnClick = true };
            item.Click += (_, _) =>
            {
                hidden.Remove(id);
                if (!item.IsChecked) hidden.Add(id);
                _app.Save();
                _signature = null;
                Tick();
            };
            menu.Items.Add(item);
        }
        var all = new MenuItem { Header = "Show all devices", IsEnabled = hidden.Count > 0 };
        all.Click += (_, _) => { hidden.Clear(); _app.Save(); _signature = null; Tick(); };
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(all);
        menu.IsOpen = true;
    }

    private void ApplyFolds()
    {
        foreach (var g in _groups)
            g.Header.Visibility = Visibility.Visible;
        foreach (var (_, row) in _rows)
            row.Row.Visibility = Visibility.Visible;
        foreach (var (_, id, children, _, top) in _groups.AsEnumerable().Reverse())
        {
            if (IsCollapsed(id, top))
                foreach (var c in children) c.Visibility = Visibility.Collapsed;
        }
        foreach (var table in _tables)
        {
            for (int i = 0; i < table.Children.Count; i++)
            {
                if (table.Children[i] is not TextBlock caption)
                    continue;
                bool any = false;
                for (int j = i + 1; j < table.Children.Count && table.Children[j] is Grid g; j++)
                    any |= g.Visibility == Visibility.Visible;
                caption.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private static int GroupOrder(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => 0, SensorKind.Load => 1, SensorKind.Clock => 2, SensorKind.Power => 3, SensorKind.Voltage => 4,
        SensorKind.Current => 5, SensorKind.Fan => 6, SensorKind.Control => 7, SensorKind.Throughput => 8, _ => 9,
    };

    private static string IconFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Cpu => "I.Cpu", DeviceKind.Gpu => "I.Gpu", DeviceKind.Memory => "I.Memory", DeviceKind.Storage => "I.HardDrive",
        DeviceKind.Network => "I.Network", DeviceKind.Motherboard or DeviceKind.SuperIO => "I.Board", DeviceKind.Controller => "I.Fan", _ => "I.Activity",
    };

    private static string SoftFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Cpu => "SlateSoft", DeviceKind.Gpu => "CoralSoft", DeviceKind.Memory => "PlumSoft", DeviceKind.Storage => "SageSoft",
        DeviceKind.Motherboard or DeviceKind.SuperIO => "ApricotSoft", _ => "AccentSoft",
    };

    private static string DeepFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Cpu => "SlateDeep", DeviceKind.Gpu => "CoralDeep", DeviceKind.Memory => "PlumDeep", DeviceKind.Storage => "SageDeep",
        DeviceKind.Motherboard or DeviceKind.SuperIO => "ApricotDeep", _ => "AccentText",
    };
}

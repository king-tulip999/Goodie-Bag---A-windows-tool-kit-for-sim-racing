using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Fans;
using GoodieBag.Hardware;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class FansPage : UserControl, IPage, IScreenshotStates, ICancelEdit
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private readonly StackPanel _banners = new();
    private readonly CheckBox _master;
    private readonly Tile _presetBar = new();
    private readonly PanelGrid _fanGrid;
    private readonly ContentControl _fanMessage = new() { Focusable = false };
    private readonly StackPanel _hiddenPanel = new();
    private readonly StackPanel _curveList = new();
    private readonly ContentControl _curveEditorHost = new() { Focusable = false };
    private readonly Dictionary<string, FanCardParts> _cards = new();
    private string _fanSignature = "";
    private string? _selectedCurve;
    private CurveEditor? _liveEditor;
    private (TextBlock Text, FanCurve Curve)? _liveMix;
    private readonly List<Action> _openLimits = new();
    private readonly List<Popup> _limitPopups = new();
    private string? _reopenLimits;
    private Tile? _detecting;
    private bool _showHidden;
    private bool _lastEmergency;
    private long _shownAt;
    private const int SettleMs = 4000;

    private sealed class FanCardParts
    {
        public required FanSpinner Spinner;
        public required TextBlock Rpm;
        public required TextBlock Output;
        public required Meter Bar;
        public CurvePreview? Curve;
        public bool Fresh = true;
    }

    private sealed class CurvePreview
    {
        public required string CurveName;
        public required CurveEditor Graph;
        public required TextBlock Status;
        public double Signature = double.NaN;
        public string? StatusText;
    }

    private static double ShapeOf(FanCurve c)
    {
        double sig = (int)c.Type * 1e6 + c.FlatPercent;
        foreach (var p in c.Points) sig = sig * 1.0001 + p.Temp * 131 + p.Percent;
        return sig + c.Points.Count;
    }

    public FansPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        _fanGrid = new PanelGrid(_app.Config.Fans.Layout, _app.Save) { Margin = new Thickness(0, 0, 0, 4), RowHeight = 1, Gap = 0, PanelSpacing = 12 };
        Grid.SetIsSharedSizeScope(_fanGrid, true);
        _master = Ui.Switch(_app.Config.Fans.Enabled, on => _app.SetFanControlEnabled(on));
        Content = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body) };
        BuildPage();
    }

    public void OnShown()
    {
        _shownAt = Environment.TickCount64;
        _master.IsChecked = _app.Config.Fans.Enabled;
        _fanSignature = "";
        BuildBanners();
        BuildPresetBar();
        BuildCurves();
        Tick();
        _app.Tick += Tick;
        _app.FansChanged += OnFansChanged;
        Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _app.Tick -= Tick;
        _app.FansChanged -= OnFansChanged;
        SetRearranging(false);
        foreach (var drop in _limitPopups) drop.IsOpen = false;
    }

    private EditUndo? _undo;

    private void SetRearranging(bool on)
    {
        if (on == _fanGrid.Editing) return;
        _undo = on ? new EditUndo().Slots(Fans.Layout) : null;
        _fanGrid.Editing = on;
        BuildPresetBar();
    }

    public bool CancelEdit()
    {
        if (!_fanGrid.Editing) return false;
        var undo = _undo;
        SetRearranging(false);
        if (undo?.Restore(_app) == true) _fanGrid.InvalidateMeasure();
        return true;
    }

    private void OnFansChanged()
    {
        _master.IsChecked = _app.Config.Fans.Enabled;
        BuildBanners();
        if (!_presetBarQueued)
        {
            _presetBarQueued = true;
            Dispatcher.BeginInvoke(new Action(() => { _presetBarQueued = false; BuildPresetBar(); }));
        }
    }

    private bool _presetBarQueued;
    private string? _bannerSignature, _presetSignature;

    private FanSettings Fans => _app.Config.Fans;

    private void BuildPage()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, Ui.HeaderGap) };
        var titles = new StackPanel();
        titles.Children.Add(Ui.PluginTitle(Plugins.PluginIds.FanControl, "Fan Control"));
        header.Children.Add(titles);
        _body.Children.Add(header);

        var masterRow = Ui.SettingRow("Fan control",
            "On: the fans below follow the active preset. Off: every fan is left to the BIOS / graphics driver.", _master);
        var masterBox = new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(20, 6, 20, 6), Child = new SettingsFlow { Children = { masterRow } } };
        Ui.Anchor(masterBox, "fans.master");
        _body.Children.Add(masterBox);

        _body.Children.Add(_banners);
        _body.Children.Add(_presetBar);
        _body.Children.Add(_fanMessage);
        _body.Children.Add(_fanGrid);
        Ui.Anchor(_presetBar, "fans.presets");
        Ui.Anchor(_fanGrid, "fans.fans");

        _body.Children.Add(_hiddenPanel);
        Ui.Anchor(_hiddenPanel, "fans.hidden");

        var curves = new Grid();
        curves.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        curves.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        curves.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        left.Children.Add(Ui.Text("Curves", "H2"));
        left.Children.Add(new Border { Height = 12 });
        left.Children.Add(_curveList);
        var curveButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        curveButtons.Children.Add(Ui.LabeledButton("I.Plus", "New", () => _ = AddCurveAsync(), "SoftButton"));
        var rename = Ui.IconButton("I.Pencil", "Rename curve", () => _ = RenameCurveAsync());
        rename.Margin = new Thickness(6, 0, 0, 0);
        curveButtons.Children.Add(rename);
        curveButtons.Children.Add(Ui.IconButton("I.Trash", "Delete curve", () => _ = DeleteCurveAsync()));
        left.Children.Add(curveButtons);
        curves.Children.Add(left);
        Grid.SetColumn(_curveEditorHost, 2);
        curves.Children.Add(_curveEditorHost);
        var curvesCard = new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 8, 0, 0), Child = curves };
        Ui.Anchor(curvesCard, "fans.curves");
        _body.Children.Add(curvesCard);

        var safety = new SettingsFlow();
        safety.Children.Add(Ui.Text("Safety", "H2"));
        safety.Children.Add(Ui.SettingRow("Emergency temperature",
            "If the CPU, GPU or any curve's sensor reaches this, every controlled fan runs at 100% until it's 5 °C cooler.",
            Ui.SliderWithValue(70, 105, _app.Config.Fans.EmergencyTempC, "{0:0} °C", v => { _app.Config.Fans.EmergencyTempC = (int)v; _app.FanSettingsChanged(); }, 160)));
        safety.Children.Add(new TextBlock
        {
            Text = "Turning fan control off, turning the Fan Control plugin off, or quitting Goodie Bag always hands every fan back to the BIOS / GPU driver.",
            Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 4, 0, 0),
        });
        var safetyCard = new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 16, 0, 0), Child = safety };
        Ui.Anchor(safetyCard, "fans.failsafe");
        _body.Children.Add(safetyCard);
    }

    private void BuildBanners()
    {
        var catalog = _app.Hardware.Catalog;
        bool onlyGpu = catalog?.Fans is { Count: > 0 } found && found.All(f => f.ControlId.StartsWith("/gpu", StringComparison.OrdinalIgnoreCase));
        string signature = $"{catalog?.Emergency == true}|{Fans.Enabled}|{Fans.EmergencyTempC}|{onlyGpu}|{_app.Hardware.PawnIoLikelyMissing}";
        if (signature == _bannerSignature)
            return;
        _bannerSignature = signature;
        _banners.Children.Clear();
        if (catalog?.Emergency == true && Fans.Enabled)
            _banners.Children.Add(Banner("I.Alert", "DangerSoft", "Running hot: all controlled fans are at 100%",
                $"A temperature reached the emergency limit ({Fans.EmergencyTempC} °C). Fans return to their curves once it drops 5 °C below that."));
        if (!_app.IsAdmin && !_app.Demo)
            _banners.Children.Add(Banner("I.Shield", "ApricotSoft", "Fan control needs administrator rights", "Start Goodie Bag as administrator to read and control fans."));
        else if (onlyGpu)
            _banners.Children.Add(Banner("I.Info", "ApricotSoft", "Only your graphics card's fans were found",
                _app.Hardware.PawnIoLikelyMissing
                    ? "Case, CPU and pump fans on the motherboard need the free PawnIO driver to be seen. Install it from Settings → Hardware access, then restart Goodie Bag."
                    : "Case, CPU and pump fans are read through the motherboard's fan controller chip, and it didn't answer. Make sure PawnIO is installed (Settings → Hardware access) and restart Goodie Bag. If it still shows only the graphics card, the board's chip isn't supported by the sensor library yet; fan hubs controlled by their own software (e.g. iCUE, NZXT CAM) also can't be controlled here.",
                ("Hardware access", () => _window.GoTo("settings", "settings.access"))));
        else if (_app.Hardware.PawnIoLikelyMissing)
            _banners.Children.Add(Banner("I.Info", "ApricotSoft", "Motherboard fans may be missing",
                "Reading CPU temperatures and motherboard fan headers needs the free PawnIO driver. Install it from Settings → Hardware access, then restart Goodie Bag.",
                ("Hardware access", () => _window.GoTo("settings", "settings.access"))));
        if (!Fans.Enabled)
            _banners.Children.Add(Banner("I.Info", "AccentSoft", "Fan control is off",
                "Your fans are under BIOS / graphics driver control. You can still name fans, calibrate them and tune curves; switch fan control on when you're happy."));
    }

    private static Border Banner(string icon, string soft, string title, string text, (string Label, Action Run)? action = null)
    {
        var banner = Ui.Notice(soft.Replace("Soft", ""), icon, title, text, action);
        banner.Margin = new Thickness(0, 0, 0, 12);
        return banner;
    }

    private void BuildPresetBar()
    {
        string signature = string.Join("\n", Fans.Presets.Select(p => p.Name)) + "|" + Fans.ActivePreset + "|" + _fanGrid.Editing;
        if (signature == _presetSignature)
            return;
        _presetSignature = signature;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        label.Children.Add(Ui.Text("Preset", "H3"));
        label.Children.Add(Ui.Text("Which curve or speed each fan uses", "Caption"));
        grid.Children.Add(label);

        int active = Math.Max(0, Fans.Presets.FindIndex(p => p.Name == Fans.ActivePreset));
        var seg = Ui.Segmented(Fans.Presets.Select(p => p.Name).ToList(), active, i =>
        {
            _app.SetFanPreset(Fans.Presets[i].Name);
            _fanSignature = "";
            Tick();
        }, "Preset");
        seg.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(seg, 1);
        grid.Children.Add(seg);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var arrange = Ui.LabeledButton("I.Move", _fanGrid.Editing ? "Done" : "Arrange fans", () => SetRearranging(!_fanGrid.Editing),
            _fanGrid.Editing ? "PrimaryButton" : "GhostButton");
        arrange.ToolTip = "Drag fans up or down to reorder them";
        arrange.Margin = new Thickness(0, 0, 8, 0);
        var gridTools = _fanGrid.TakeEditTools();
        gridTools.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(gridTools);
        buttons.Children.Add(arrange);
        var calibrateAll = Ui.LabeledButton("I.Target", "Calibrate all fans", () => _ = CalibrateAllAsync(), "GhostButton");
        calibrateAll.ToolTip = "Measures every fan that reports its speed, one after another";
        calibrateAll.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(calibrateAll);
        var resetAll = Ui.LabeledButton("I.Undo", "Reset all fans", () => _ = ResetAllFansAsync(), "GhostButton");
        resetAll.ToolTip = "Every fan back to its default name, limits and Auto in every preset (calibration is kept)";
        resetAll.Margin = new Thickness(0, 0, 20, 0);
        buttons.Children.Add(resetAll);
        var newPreset = Ui.LabeledButton("I.Plus", "New preset", () => _ = NewPresetAsync(), "PrimaryButton");
        newPreset.Margin = new Thickness(0, 0, 2, 0);
        buttons.Children.Add(newPreset);
        buttons.Children.Add(Ui.IconButton("I.Pencil", "Rename preset", () => _ = RenamePresetAsync()));
        var del = Ui.IconButton("I.Trash", "Delete preset", () => _ = DeletePresetAsync());
        del.IsEnabled = Fans.Presets.Count > 1;
        buttons.Children.Add(del);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        double natural = 0;
        grid.SizeChanged += (_, e) =>
        {
            if (!e.WidthChanged) return;
            if (natural == 0)
            {
                var free = new Size(double.PositiveInfinity, double.PositiveInfinity);
                label.Measure(free);
                seg.Measure(free);
                buttons.Measure(free);
                natural = label.DesiredSize.Width + seg.DesiredSize.Width + buttons.DesiredSize.Width + 16;
            }
            bool stacked = e.NewSize.Width < natural;
            Grid.SetRow(buttons, stacked ? 1 : 0);
            Grid.SetColumn(buttons, stacked ? 0 : 2);
            Grid.SetColumnSpan(buttons, stacked ? 3 : 1);
            buttons.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            buttons.Margin = new Thickness(0, stacked ? 12 : 0, 0, 0);
        };

        _presetBar.Style = Ui.Res<Style>("Card");
        _presetBar.Padding = new Thickness(18, 12, 12, 12);
        _presetBar.Margin = new Thickness(0, 0, 0, 16);
        _presetBar.Child = grid;
    }

    private async Task NewPresetAsync()
    {
        string? name = await _window.Prompt("New fan preset", "Starts as a copy of the current preset.", $"Preset {Fans.Presets.Count + 1}", n => ValidatePresetName(n));
        if (name is null) return;
        var copy = ConfigStore.Clone(Fans.Active ?? new FanPreset());
        copy.Name = name;
        copy.Hotkey = null;
        Fans.Presets.Add(copy);
        Fans.ActivePreset = name;
        _app.FanSettingsChanged();
        _fanSignature = "";
        Tick();
    }

    private async Task RenamePresetAsync()
    {
        var preset = Fans.Active;
        if (preset is null) return;
        string old = preset.Name;
        string? name = await _window.Prompt("Rename preset", "", old, n => n == old ? null : ValidatePresetName(n, preset));
        if (name is null || name == old) return;
        preset.Name = name;
        Fans.ActivePreset = name;
        _app.FanSettingsChanged();
    }

    private async Task DeletePresetAsync()
    {
        var preset = Fans.Active;
        if (preset is null || Fans.Presets.Count <= 1) return;
        if (!await _window.Confirm($"Delete the “{preset.Name}” preset?", "This can't be undone.", "Delete", danger: true))
            return;
        Fans.Presets.Remove(preset);
        Fans.ActivePreset = Fans.Presets[0].Name;
        _app.FanSettingsChanged();
        _app.Hotkeys.RegisterAll();
        _fanSignature = "";
        Tick();
    }

    private string? ValidatePresetName(string name, FanPreset? self = null) =>
        name.Length == 0 ? "Give the preset a name."
        : Fans.Presets.Any(p => p != self && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ? $"There's already a preset called “{name}”." : null;

    private void Tick()
    {
        var catalog = _app.Hardware.Catalog;
        if (catalog is null)
        {
            if (!_fanGrid.Ids.Any())
            {
                _fanMessage.Content = _detecting ??= new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 16), Child = Ui.Text("Detecting fans…", "Muted") };
                _fanSignature = "";
            }
            return;
        }
        if (_curvesBeforeCatalog) BuildCurveEditor();

        var visible = catalog.Fans.Where(f => !Fans.HardwareFor(f.ControlId).Hidden).ToList();
        string signature = string.Join("|", visible.Select(f => f.ControlId)) + "#" + Fans.ActivePreset + "#" + string.Join(",", Fans.Curves.Select(c => c.Name))
                           + "#" + string.Join("|", catalog.Fans.Where(f => Fans.HardwareFor(f.ControlId).Hidden).Select(f => f.ControlId));
        if (signature != _fanSignature)
        {
            _fanSignature = signature;
            BuildBanners();
            BuildFanCards(visible);
            BuildHidden(catalog.Fans.Where(f => Fans.HardwareFor(f.ControlId).Hidden).ToList());
        }

        foreach (var fan in visible)
        {
            if (!_cards.TryGetValue(fan.ControlId, out var parts)) continue;
            TextCase.Set(parts.Rpm, (fan.Rpm is { } r ? $"{r:N0} rpm" : "– rpm") + (fan.Percent is not null && !fan.Controlled ? " · auto" : ""));
            TextCase.Set(parts.Output, fan.Percent is { } p ? $"{p:0}%" : "–");
            var cal = Fans.HardwareFor(fan.ControlId).Calibration;
            parts.Spinner.SetSpeed(fan.Percent, fan.Rpm, cal?.MaxRpm ?? 0, Motion.Enabled && !parts.Fresh);
            double level = fan.Percent is { } pp ? pp / 100 : cal?.MaxRpm > 0 && fan.Rpm is { } rr ? rr / cal.MaxRpm : 0;
            if (double.IsNaN(level)) level = 0;
            if (parts.Fresh) parts.Bar.Value = level;
            else Motion.To(parts.Bar, Meter.ValueProperty, level, glideFrom: Motion.BarStep);
            parts.Fresh = false;
        }

        foreach (var (id, parts) in _cards)
            if (parts.Curve is { } preview) UpdateCurvePreview(id, preview, catalog);

        if (_liveEditor is not null && _selectedCurve is not null && catalog.Curves.TryGetValue(_selectedCurve, out var live))
        {
            if (live.Temp is { } t) Motion.To(_liveEditor, CurveEditor.LiveTempProperty, t, 700);
            if (live.Output is { } o) Motion.To(_liveEditor, CurveEditor.LiveOutputProperty, o, 700);
        }
        if (_liveMix is { } mix)
            mix.Text.Text = MixLine(mix.Curve, catalog.Curves);

        if (catalog.Emergency != _lastEmergency)
        {
            _lastEmergency = catalog.Emergency;
            BuildBanners();
        }
    }

    private void BuildFanCards(List<FanInfo> visible)
    {
        _fanGrid.Clear();
        _fanMessage.Content = null;
        _cards.Clear();
        _openLimits.Clear();
        foreach (var drop in _limitPopups) drop.IsOpen = false;
        _limitPopups.Clear();
        _openCurves.Clear();
        if (visible.Count == 0)
        {
            var text = _app.Hardware.Catalog?.Fans.Count > 0
                ? "Every fan is hidden. Use “Hidden fans” below to bring some back."
                : "No fans were detected. Motherboard fan headers need the PawnIO driver (Settings → Hardware access); some boards and newer graphics cards don't expose fan control at all.";
            _fanMessage.Content = new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 16), Child = Ui.Text(text, "Muted") };
            return;
        }
        foreach (var fan in visible)
        {
            var f = fan;
            _fanGrid.Add(new PanelDef(f.ControlId, _app.FanName(f), _ => FanCard(f)) { FullWidth = true });
        }
    }

    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action> _openCurves = new();

    private Border FanCard(FanInfo fan)
    {
        var hw = Fans.HardwareFor(fan.ControlId);
        var entry = Fans.Active?.EntryFor(fan.ControlId);
        var root = new StackPanel();

        var left = new Grid { VerticalAlignment = VerticalAlignment.Center };
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        var spinner = hw.IsPump
            ? FanSpinner.Pump(Ui.Res<Brush>("AccentText"), Ui.Res<Brush>("TextFaint"), 34)
            : new FanSpinner(Ui.Res<Geometry>("I.Fan"), Ui.Res<Brush>("AccentText"), 34);
        left.Children.Add(new Tile { Width = 54, Height = 54, CornerRadius = ThemeManager.Round(14), Background = Ui.Res<Brush>(hw.IsPump ? "SlateSoft" : "AccentSoft"), Child = spinner, VerticalAlignment = VerticalAlignment.Center });
        var names = new StackPanel { Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = _app.FanName(fan), Style = Ui.Res<Style>("H2"), TextTrimming = TextTrimming.CharacterEllipsis }));
        names.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = fan.Controllable ? Short(fan.Hardware) : "Read only", Style = Ui.Res<Style>("Caption"), FontSize = 13.5, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap }));
        Grid.SetColumn(names, 1);
        left.Children.Add(names);
        var bar = new Meter { Tiered = !hw.IsPump, Fill = Ui.Res<Brush>("Slate"), Height = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(bar, 2);
        left.Children.Add(bar);
        var output = new TextBlock { Text = "–", Style = Ui.Res<Style>("BigValue"), FontSize = 26, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(output, 3);
        left.Children.Add(output);
        var rpm = new TextBlock { Text = "–", Foreground = Ui.Res<Brush>("TextMuted"), FontSize = 15.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(rpm, 4);
        left.Children.Add(rpm);
        _cards[fan.ControlId] = new FanCardParts { Spinner = spinner, Rpm = rpm, Output = output, Bar = bar };

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var expandHost = new ContentControl { Focusable = false };
        Button? chevron = null;

        if (fan.Controllable && entry is not null)
        {
            var modeHost = new ContentControl { Focusable = false, Width = 130, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            void BuildMode()
            {
                modeHost.Content = null;
                expandHost.Content = null;
                if (_cards.TryGetValue(fan.ControlId, out var parts)) parts.Curve = null;
                if (entry.Mode == FanMode.Curve)
                {
                    var combo = new ComboBox { Width = 130 };
                    foreach (var c in Fans.Curves) combo.Items.Add(c.Name);
                    if (Fans.Curves.Count == 0) combo.Items.Add("(create a curve below)");
                    combo.SelectedItem = entry.CurveName is not null && Fans.Curves.Any(c => c.Name == entry.CurveName) ? entry.CurveName : null;
                    if (combo.SelectedItem is null && Fans.Curves.Count > 0)
                    {
                        entry.CurveName = Fans.Curves[0].Name;
                        combo.SelectedItem = entry.CurveName;
                        _app.FanSettingsChanged();
                    }
                    combo.SelectionChanged += (_, _) =>
                    {
                        if (combo.SelectedItem is string name && Fans.Curves.Any(c => c.Name == name))
                        {
                            entry.CurveName = name;
                            _app.FanSettingsChanged();
                            ShowCurve();
                        }
                    };
                    modeHost.Content = combo;
                    ShowCurve();
                }
                else if (entry.Mode == FanMode.Manual)
                    modeHost.Content = Ui.SliderWithValue(0, 100, entry.ManualPercent, "{0:0}%", v => { entry.ManualPercent = (int)v; _app.FanSettingsChanged(); }, 78);
                if (chevron is not null) chevron.Visibility = entry.Mode == FanMode.Curve ? Visibility.Visible : Visibility.Hidden;
            }
            void ShowCurve()
            {
                expandHost.Content = null;
                if (_cards.TryGetValue(fan.ControlId, out var parts)) parts.Curve = null;
                if (!_expanded.Contains(fan.ControlId) || entry.Mode != FanMode.Curve || entry.CurveName is not { } name || Fans.Curves.FirstOrDefault(c => c.Name == name) is null) return;
                var preview = new CurvePreview
                {
                    CurveName = name,
                    Graph = new CurveEditor { ReadOnly = true, Height = 170, MinHeight = 0, IsHitTestVisible = false },
                    Status = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) },
                };
                var line = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 14) };
                line.SetResourceReference(Border.BackgroundProperty, "Line");
                var graphCell = new Grid();
                graphCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 760 });
                graphCell.Children.Add(preview.Graph);
                expandHost.Content = new StackPanel { Margin = new Thickness(70, 0, 0, 4), Children = { line, graphCell, preview.Status } };
                if (parts is not null) parts.Curve = preview;
                UpdateCurvePreview(fan.ControlId, preview, _app.Hardware.Catalog);
            }
            var seg = Ui.Segmented(new[] { "Auto", "Curve", "Fixed" }, (int)entry.Mode, i =>
            {
                entry.Mode = (FanMode)i;
                _app.FanSettingsChanged();
                BuildMode();
            }, "Mode", new[]
            {
                "Auto: the BIOS or graphics driver controls this fan, as if Goodie Bag weren't here",
                "Curve: the fan follows a curve below (speed rises with a temperature)",
                "Fixed: always the same speed",
            });
            seg.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(seg);
            right.Children.Add(modeHost);
            AddLimits(right, fan, hw);
            chevron = Ui.IconButton("I.ChevronDown", "Show the curve", () =>
            {
                if (!_expanded.Remove(fan.ControlId)) _expanded.Add(fan.ControlId);
                SetChevron();
                ShowCurve();
            });
            void SetChevron()
            {
                bool open = _expanded.Contains(fan.ControlId);
                chevron!.RenderTransformOrigin = new Point(0.5, 0.5);
                chevron.RenderTransform = open ? new RotateTransform(180) : null;
                chevron.ToolTip = open ? "Hide the curve" : "Show the curve";
            }
            SetChevron();
            _openCurves.Add(() => { if (_expanded.Add(fan.ControlId)) { SetChevron(); ShowCurve(); } });
            BuildMode();
        }

        foreach (var (icon, tip, run) in new (string, string, Action)[]
        {
            ("I.Pencil", "Rename", () => _ = RenameFanAsync(fan)),
            ("I.Undo", "Reset this fan to default (name, mode in every preset, limits; calibration is kept)", () => _ = ResetFanAsync(fan)),
            ("I.EyeOff", "Hide this fan", () => SetHidden(fan.ControlId, true)),
        })
            right.Children.Add(Ui.IconButton(icon, tip, run));
        if (chevron is not null) right.Children.Add(chevron);

        right.HorizontalAlignment = HorizontalAlignment.Right;
        var controls = new Grid();
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "FanControls" });
        controls.Children.Add(right);
        root.Children.Add(Ui.SplitRow(left, controls, 20, stackLeft: true));
        root.Children.Add(expandHost);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(20, 20, 16, 20), Child = root };
    }

    private void AddLimits(Panel host, FanInfo fan, FanHardware hw)
    {
        var more = new StackPanel { Width = 350 };
        string summary = CalibrationSummary(hw);
        var toggle = new Button { Style = Ui.Res<Style>("GhostButton"), Width = 240, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 0, 8, 0), Padding = new Thickness(10, 5, 10, 5), VerticalAlignment = VerticalAlignment.Center, ToolTip = summary };
        var toggleIcon = new Icon { Data = Ui.Res<Geometry>("I.ChevronRight"), Width = 14, Height = 14 };
        var toggleContent = new DockPanel();
        DockPanel.SetDock(toggleIcon, Dock.Left);
        toggleContent.Children.Add(toggleIcon);
        toggleContent.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = summary, Margin = new Thickness(6, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }));
        toggle.Content = toggleContent;
        var drop = new Popup
        {
            PlacementTarget = toggle, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 4, 0, 0), Child = more },
        };
        RenderOptions.SetClearTypeHint(drop.Child, ClearTypeHint.Enabled);
        Ui.FollowTarget(drop);
        long closedAt = 0;
        drop.Opened += (_, _) => toggleIcon.Data = Ui.Res<Geometry>("I.ChevronDown");
        drop.Closed += (_, _) => { toggleIcon.Data = Ui.Res<Geometry>("I.ChevronRight"); closedAt = Environment.TickCount64; };
        toggle.Click += (_, _) =>
        {
            if (!drop.IsOpen && Environment.TickCount64 - closedAt < 250) return;
            drop.IsOpen = !drop.IsOpen;
        };
        _openLimits.Add(() => drop.IsOpen = true);
        _limitPopups.Add(drop);
        if (_reopenLimits == fan.ControlId)
        {
            _reopenLimits = null;
            Dispatcher.BeginInvoke(() => { if (toggle.IsLoaded) drop.IsOpen = true; }, DispatcherPriority.Loaded);
        }

        more.Children.Add(Row("Minimum", Ui.SliderWithValue(0, 100, hw.MinPercent, "{0:0}%", v => { hw.MinPercent = (int)v; _app.FanSettingsChanged(); }, 140)));
        more.Children.Add(Row("Maximum", Ui.SliderWithValue(20, 100, hw.MaxPercent, "{0:0}%", v => { hw.MaxPercent = (int)v; _app.FanSettingsChanged(); }, 140)));
        var pump = new CheckBox { Content = "This is a pump (keep at 60% or more)", IsChecked = hw.IsPump, Margin = new Thickness(0, 6, 0, 0) };
        pump.Click += (_, _) =>
        {
            hw.IsPump = pump.IsChecked == true;
            _app.FanSettingsChanged();
            _reopenLimits = fan.ControlId;
            _fanSignature = "";
            Tick();
        };
        more.Children.Add(pump);
        bool canStop = FanController.StopTested(hw);
        var stop = new CheckBox
        {
            Content = "Allow fan stop at low temperatures",
            IsChecked = hw.AllowStop && canStop,
            IsEnabled = canStop,
            Margin = new Thickness(0, 6, 0, 4),
            ToolTip = canStop ? "Calibration showed this fan stops and restarts cleanly." : "Calibrate with “test fan stop” first, so Goodie Bag knows this fan can stop and restart safely.",
        };
        more.Children.Add(stop);

        var start = Row("Start", Ui.SliderWithValue(0, 100, FanController.StartAt(hw), "{0:0}%", v => { hw.StartPercent = (int)v; _app.FanSettingsChanged(); }, 140));
        start.ToolTip = "Speed for the first few seconds when the fan starts (from stopped, or when Goodie Bag takes over), so it gets turning. 0% = no boost";
        more.Children.Add(start);
        var stopAt = Row("Stop", Ui.SliderWithValue(0, 100, hw.StopPercent ?? hw.Calibration?.StopPercent ?? 0, "{0:0}%", v => { hw.StopPercent = (int)v; _app.FanSettingsChanged(); }, 140));
        stopAt.ToolTip = "With fan stop allowed: when the curve asks for this or less, the fan slows to its lowest speed and stops. It starts again a little above it";
        stopAt.IsEnabled = hw.AllowStop && canStop;
        more.Children.Add(stopAt);
        stop.Click += (_, _) => { hw.AllowStop = stop.IsChecked == true; stopAt.IsEnabled = hw.AllowStop; _app.FanSettingsChanged(); };
        var stepUp = Row("Step up", Ui.SliderWithValue(0, 50, hw.StepUpPerSecond, "{0:0}%/s", v => { hw.StepUpPerSecond = (int)v; _app.FanSettingsChanged(); }, 140));
        stepUp.ToolTip = "How fast this fan may speed up. 0 = at once (the curve's own steps still apply)";
        more.Children.Add(stepUp);
        var stepDown = Row("Step down", Ui.SliderWithValue(0, 50, hw.StepDownPerSecond, "{0:0}%/s", v => { hw.StepDownPerSecond = (int)v; _app.FanSettingsChanged(); }, 140));
        stepDown.ToolTip = "How fast this fan may slow down. 0 = at once (the curve's own steps still apply)";
        more.Children.Add(stepDown);

        var calRow = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 2) };
        var calibrate = Ui.LabeledButton("I.Target", hw.Calibration is null ? "Calibrate" : "Recalibrate", () => _ = CalibrateAsync(fan), "SoftButton");
        calibrate.Margin = new Thickness(0, 0, 4, 0);
        calRow.Children.Add(calibrate);
        Button spin = null!;
        spin = Ui.LabeledButton("I.Fan", "Spin up", () => _ = SpinUpAsync(fan, spin), "GhostButton");
        spin.ToolTip = "Runs this fan at 100% for 8 seconds so you can tell which one it is, then says whether it sped up";
        spin.Margin = new Thickness(4, 0, 0, 0);
        calRow.Children.Add(spin);
        more.Children.Add(calRow);
        host.Children.Add(toggle);
        host.Children.Add(drop);
    }

    private void UpdateCurvePreview(string controlId, CurvePreview preview, HardwareCatalog? catalog)
    {
        var curve = Fans.Curves.FirstOrDefault(c => c.Name == preview.CurveName);
        if (curve is null) return;
        double shape = ShapeOf(curve);
        if (shape != preview.Signature)
        {
            preview.Signature = shape;
            preview.Graph.SetPoints(curve.Type switch
            {
                FanCurveType.Graph => curve.Points,
                FanCurveType.Flat => new List<CurvePoint> { new() { Temp = 20, Percent = curve.FlatPercent }, new() { Temp = 105, Percent = curve.FlatPercent } },
                _ => new List<CurvePoint>(),
            });
            preview.Graph.Visibility = curve.Type == FanCurveType.Mix ? Visibility.Collapsed : Visibility.Visible;
        }
        CurveLive? live = catalog?.Curves.TryGetValue(curve.Name, out var l) == true ? l : null;
        preview.Graph.LiveTemp = live?.Temp ?? double.NaN;
        preview.Graph.LiveOutput = live?.Output ?? double.NaN;
        var fan = catalog?.Fans.FirstOrDefault(f => f.ControlId == controlId);
        string mix = curve.Type == FanCurveType.Mix ? $"Mix ({curve.Function}) of {string.Join(", ", curve.MixOf)}. " : "";
        (string text, string brush) = !Fans.Enabled ? (mix + "Selected, not controlling: fan control is off.", "TextFaint")
            : catalog is null || fan is null || (live?.Output is null && Environment.TickCount64 - _shownAt < SettleMs) ? (mix + "Waiting for the fan's readings…", "TextFaint")
            : catalog.Emergency ? (mix + "Not controlling: emergency, every controlled fan is at 100%.", "WarnDeep")
            : live?.Output is null ? (mix + "Not controlling: the curve's sensor isn't reading, so the fan runs at 100%.", "WarnDeep")
            : fan.Error is { } err ? (mix + "Not controlling: the fan didn't accept the speed (" + err + ").", "DangerDeep")
            : fan.Controlled ? (mix + $"Controlling this fan: {live!.Output:0}% now.", "SageDeep")
            : (mix + "Selected, not controlling yet.", "TextFaint");
        if (text == preview.StatusText) return;
        preview.StatusText = text;
        preview.Status.Text = text;
        preview.Status.Foreground = Ui.Res<Brush>(brush);
    }

    private async Task SpinUpAsync(FanInfo fan, Button button)
    {
        FanInfo? Now() => _app.Hardware.Catalog?.Fans.FirstOrDefault(f => f.ControlId == fan.ControlId);
        float? before = Now()?.Rpm;
        button.IsEnabled = false;
        _app.Hardware.SpinUp(fan.ControlId);
        await Task.Delay(7000);
        var after = Now();
        button.IsEnabled = true;
        string name = _app.FanName(fan);
        if (after?.Error is { } error)
            _app.Notify("Spin up didn't work", $"{name} refused the speed: {error}", NoticeKind.Error);
        else if (after?.Rpm is { } rpm && before is { } was && fan.HasRpm)
        {
            if (rpm > was * 1.15 + 100 || rpm > 0 && was == 0)
                _app.Notify("Spin up", $"{name} went from {was:N0} to {rpm:N0} RPM.", NoticeKind.Info);
            else
                _app.Notify("Fan didn't speed up", $"{name} stayed at {rpm:N0} RPM. Its driver may not let other programs set the speed (some graphics cards only follow their own fan curve, or hold the fan stopped in zero-RPM mode).", NoticeKind.Warning, important: true);
        }
        else
            _app.Notify("Spin up sent", $"{name} has no speed reading, so Goodie Bag can't tell whether it sped up; listen for it.", NoticeKind.Info);
    }

    private static string CalibrationSummary(FanHardware hw)
    {
        var c = hw.Calibration;
        if (c is null)
            return "Limits · not calibrated";
        if (!c.RespondsToControl)
            return "Limits · doesn't respond to control";
        if (c.Manual)
            return c.MaxRpm > 0 ? $"Limits · up to {c.MaxRpm:N0} RPM" : "Limits · entered by hand";
        return $"Limits · {c.Points.Where(p => p.Rpm > 0).Select(p => p.Rpm).DefaultIfEmpty(0).Min():N0}–{c.MaxRpm:N0} RPM";
    }

    private static string Short(string hardware) => hardware.Length > 30 ? hardware[..28] + "…" : hardware;

    private static Grid Row(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private void BuildHidden(List<FanInfo> hidden)
    {
        _hiddenPanel.Children.Clear();
        if (hidden.Count == 0)
            return;

        var toggle = new Button { Style = Ui.Res<Style>("GhostButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Icon { Data = Ui.Res<Geometry>(_showHidden ? "I.ChevronDown" : "I.ChevronRight"), Width = 14, Height = 14 });
        content.Children.Add(new TextBlock { Text = $"Hidden fans ({hidden.Count})", Margin = new Thickness(6, 0, 0, 0) });
        toggle.Content = content;
        toggle.Click += (_, _) => { _showHidden = !_showHidden; BuildHidden(hidden); };
        _hiddenPanel.Children.Add(toggle);
        if (!_showHidden)
            return;

        var list = new StackPanel();
        foreach (var fan in hidden)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = _app.FanName(fan) });
            text.Children.Add(new TextBlock { Text = (fan.Rpm is { } r ? $"{r:N0} RPM · " : "") + (fan.Controllable ? fan.Hardware : "read only"), Style = Ui.Res<Style>("Caption") });
            row.Children.Add(text);
            var show = Ui.LabeledButton("I.Eye", "Show", () => SetHidden(fan.ControlId, false), "SoftButton");
            Grid.SetColumn(show, 1);
            row.Children.Add(show);
            list.Children.Add(row);
        }
        var box = new Tile { Style = Ui.Res<Style>("Inset"), Margin = new Thickness(0, 0, 0, 16), Child = list };
        _hiddenPanel.Children.Add(box);
        Motion.Enter(box, distance: 6);
    }

    private void SetHidden(string controlId, bool hidden)
    {
        Fans.HardwareFor(controlId).Hidden = hidden;
        _app.FanSettingsChanged();
        _fanSignature = "";
        Tick();
    }

    private async Task ResetFanAsync(FanInfo fan)
    {
        if (!await _window.Confirm($"Reset “{_app.FanName(fan)}” to default?",
                "Its name, limits, start / stop and step settings go back to default, and it's set to Auto (BIOS / driver) in every preset. Its calibration is kept.",
                "Reset", danger: true))
            return;
        Fans.ResetFan(fan.ControlId);
        _app.FanSettingsChanged();
        _fanSignature = "";
        Tick();
    }

    private async Task ResetAllFansAsync()
    {
        if (!await _window.Confirm("Reset all fans to default?",
                "Every fan, hidden ones included, gets its default name and limits back and is set to Auto (BIOS / driver) in every preset. Calibrations, curves, presets and the card layout are kept.",
                "Reset all", danger: true))
            return;
        Fans.ResetAllFans();
        _app.FanSettingsChanged();
        _fanSignature = "";
        Tick();
    }

    private async Task RenameFanAsync(FanInfo fan)
    {
        var hw = Fans.HardwareFor(fan.ControlId);
        string? name = await _window.Prompt("Rename fan", $"Detected as {fan.DefaultName}. Leave empty to use that name.", hw.CustomName ?? "", _ => null);
        if (name is null) return;
        hw.CustomName = string.IsNullOrWhiteSpace(name) ? null : name;
        _app.FanSettingsChanged();
        _fanSignature = "";
        Tick();
    }

    private async Task CalibrateAsync(FanInfo fan)
    {
        var dialog = new CalibrationDialog(_app, _window, fan);
        await _window.ShowModal(dialog.Card);
        dialog.Cancel();
        _fanSignature = "";
        Tick();
        BuildCurves();
    }

    private async Task CalibrateAllAsync()
    {
        var fans = _app.Hardware.Catalog?.Fans.Where(f => f.Controllable && f.HasRpm && f.Rpm is not null).ToList() ?? [];
        if (fans.Count == 0)
        {
            _app.Notify("No fans to calibrate", "None of the detected fans reports its speed.", NoticeKind.Info);
            return;
        }
        var dialog = new BatchCalibrationDialog(_app, _window, fans);
        await _window.ShowModal(dialog.Card);
        dialog.Cancel();
        _fanSignature = "";
        Tick();
        BuildCurves();
    }

    private void BuildCurves()
    {
        _curveList.Children.Clear();
        if (_selectedCurve is null || Fans.Curves.All(c => c.Name != _selectedCurve))
            _selectedCurve = Fans.Curves.FirstOrDefault()?.Name;

        foreach (var curve in Fans.Curves)
        {
            var rb = new RadioButton
            {
                Style = Ui.Res<Style>("NavItem"),
                GroupName = "FanCurves",
                Content = TextFit.TipWhenTrimmed(new TextBlock { Text = curve.Name, TextTrimming = TextTrimming.CharacterEllipsis }),
                Tag = Ui.Res<Geometry>(curve.Type switch { FanCurveType.Flat => "I.Minus", FanCurveType.Mix => "I.Layers", _ => "I.Activity" }),
                IsChecked = curve.Name == _selectedCurve,
            };
            string name = curve.Name;
            rb.Checked += (_, _) => { _selectedCurve = name; BuildCurveEditor(); };
            _curveList.Children.Add(rb);
        }
        if (Fans.Curves.Count == 0)
            _curveList.Children.Add(new TextBlock { Text = "No curves yet.", Style = Ui.Res<Style>("Caption") });
        BuildCurveEditor();
    }

    private bool _curvesBeforeCatalog;

    private void BuildCurveEditor()
    {
        _curvesBeforeCatalog = _app.Hardware.Catalog is null;
        _liveEditor = null;
        _liveMix = null;
        var curve = Fans.Curves.FirstOrDefault(c => c.Name == _selectedCurve);
        if (curve is null)
        {
            _curveEditorHost.Content = new TextBlock
            {
                Text = "Create a curve to get started. A good setup: one curve for the CPU, one for the GPU, and a Mix (Max) of the two for case fans that should react to both.",
                Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 30, 0, 0),
            };
            return;
        }

        var stack = new StackPanel();
        var top = new WrapPanel();
        top.Children.Add(Ui.Segmented(new[] { "Temperature graph", "Fixed speed", "Mix" }, (int)curve.Type, i =>
        {
            curve.Type = (FanCurveType)i;
            _app.FanSettingsChanged();
            Dispatcher.BeginInvoke(new Action(BuildCurves));
        }, "CurveType", new[]
        {
            "Speed follows a temperature: drag the points to shape it",
            "Always the same speed, whatever the temperatures",
            "Combines several curves: the highest, lowest, average, sum or difference of what they ask for",
        }));
        stack.Children.Add(top);

        if (curve.Type == FanCurveType.Graph)
        {
            var temps = _app.Hardware.Catalog?.Temperatures ?? Array.Empty<TempSensorInfo>();
            var sensor = new ComboBox { MinWidth = 300 };
            foreach (var t in temps.Where(t => t.Value is not null))
                sensor.Items.Add(new Choice(t.Id, $"{t.Label}  ({t.Value:0} °C)", t.Label));
            if (curve.SensorId is not null && sensor.Items.Cast<Choice>().All(s => s.Id != curve.SensorId))
                sensor.Items.Insert(0, new Choice(curve.SensorId, $"(missing) {curve.SensorName ?? curve.SensorId}", curve.SensorName ?? ""));
            sensor.SelectedItem = sensor.Items.Cast<Choice>().FirstOrDefault(s => s.Id == curve.SensorId);
            sensor.SelectionChanged += (_, _) =>
            {
                if (sensor.SelectedItem is not Choice s) return;
                curve.SensorId = s.Id;
                curve.SensorName = s.Extra;
                _app.FanSettingsChanged();
            };

            var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(Labeled("Temperature from", sensor));
            stack.Children.Add(row);

            var editor = new CurveEditor { Height = 290, Margin = new Thickness(0, 12, 0, 0) };
            editor.SetPoints(curve.Points);
            editor.PointsChanged += (_, _) => _app.FanSettingsChanged();
            stack.Children.Add(editor);
            _liveEditor = editor;

            var tune = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            tune.Children.Add(Labeled("Ignore changes smaller than", Ui.SliderWithValue(0, 10, curve.HysteresisC, "{0:0} °C", v => { curve.HysteresisC = (int)v; _app.FanSettingsChanged(); }, 120)));
            tune.Children.Add(Labeled("Speed up by at most", Ui.SliderWithValue(0, 50, curve.StepUpPerSecond, "{0:0}%/s", v => { curve.StepUpPerSecond = (int)v; _app.FanSettingsChanged(); }, 120)));
            tune.Children.Add(Labeled("Slow down by at most", Ui.SliderWithValue(0, 50, curve.StepDownPerSecond, "{0:0}%/s", v => { curve.StepDownPerSecond = (int)v; _app.FanSettingsChanged(); }, 120)));
            stack.Children.Add(tune);
            stack.Children.Add(new TextBlock { Text = "Drag points to move them · double-click to add one · right-click to remove · 0%/s means instant", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 4, 0, 0) });
        }
        else if (curve.Type == FanCurveType.Flat)
        {
            stack.Children.Add(Labeled("Speed", Ui.SliderWithValue(0, 100, curve.FlatPercent, "{0:0}%", v => { curve.FlatPercent = (int)v; _app.FanSettingsChanged(); }, 260)));
            stack.Children.Add(new TextBlock { Text = "Always this speed, whatever the temperature. Handy for pumps.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 8, 0, 0) });
        }
        else
        {
            var line = new TextBlock { Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
            var note = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };
            void Refresh()
            {
                note.Text = MixNotes[(int)curve.Function];
                line.Text = MixLine(curve, _app.Hardware.Catalog?.Curves);
            }
            var function = Ui.Segmented(MixNames, (int)curve.Function, i =>
            {
                curve.Function = (MixFunction)i;
                _app.FanSettingsChanged();
                Refresh();
            }, "MixFunction", MixNotes);
            function.Margin = new Thickness(0, 12, 0, 0);
            function.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(function);
            stack.Children.Add(note);

            var others = Fans.Curves.Where(c => c != curve && !Uses(c, curve.Name, new HashSet<string>())).ToList();
            var sources = new WrapPanel();
            if (others.Count == 0)
                sources.Children.Add(new TextBlock { Text = "No curves to mix yet. Add a temperature graph or fixed speed curve first.", Style = Ui.Res<Style>("Caption") });
            foreach (var other in others)
            {
                var check = new CheckBox { Content = other.Name, IsChecked = curve.MixOf.Contains(other.Name), Margin = new Thickness(0, 4, 18, 4) };
                string name = other.Name;
                check.Click += (_, _) =>
                {
                    curve.MixOf.Remove(name);
                    if (check.IsChecked == true) curve.MixOf.Add(name);
                    _app.FanSettingsChanged();
                    Refresh();
                };
                sources.Children.Add(check);
            }
            stack.Children.Add(sources);
            stack.Children.Add(line);
            Refresh();
            _liveMix = (line, curve);
        }

        _curveEditorHost.Content = stack;
        Motion.Enter(stack, distance: 6, durationMs: 220);
    }

    private static readonly string[] MixNames = { "Max", "Min", "Average", "Sum", "Subtract" };
    private static readonly string[] MixNotes =
    {
        "The highest of the ticked curves: e.g. case fans that follow the CPU or the GPU, whichever is hotter.",
        "The lowest of the ticked curves.",
        "The average of the ticked curves.",
        "The ticked curves added up (at most 100%).",
        "The first ticked curve minus the others (at least 0%). Untick and tick again to change the order.",
    };

    private bool Uses(FanCurve curve, string name, HashSet<string> seen)
    {
        if (curve.Name == name) return true;
        if (curve.Type != FanCurveType.Mix || !seen.Add(curve.Name)) return false;
        return curve.MixOf.Any(n => Fans.Curves.FirstOrDefault(c => c.Name == n) is { } part && Uses(part, name, seen));
    }

    private static string MixLine(FanCurve mix, IReadOnlyDictionary<string, CurveLive>? live)
    {
        if (mix.MixOf.Count == 0) return "Tick the curves to mix. Fans on an empty mix run at 100%.";
        string sep = mix.Function switch { MixFunction.Sum => " + ", MixFunction.Subtract => " − ", _ => " · " };
        string Value(string name) => live?.GetValueOrDefault(name)?.Output is { } v ? $"{name} {v:0}%" : $"{name} –";
        string result = live is null ? "–" : live.GetValueOrDefault(mix.Name)?.Output is { } r ? $"{r:0}%" : "100% (an input has no value)";
        return $"Now: {string.Join(sep, mix.MixOf.Select(Value))}  →  {result}";
    }

    private static StackPanel Labeled(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 22, 8) };
        panel.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 5) });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(control);
        return panel;
    }

    private async Task AddCurveAsync()
    {
        string suggestion = Fans.Curves.All(c => c.Name != "CPU") ? "CPU" : Fans.Curves.All(c => c.Name != "GPU") ? "GPU" : $"Curve {Fans.Curves.Count + 1}";
        string? name = await _window.Prompt("New curve", "Name it after what it cools, e.g. CPU, GPU or Case.", suggestion, n => ValidateCurveName(n, null));
        if (name is null) return;
        var curve = new FanCurve { Name = name };
        var temps = _app.Hardware.Catalog?.Temperatures ?? Array.Empty<TempSensorInfo>();
        var guess = name.Contains("GPU", StringComparison.OrdinalIgnoreCase)
            ? temps.FirstOrDefault(t => t.Name == "GPU Core")
            : temps.FirstOrDefault(t => t.Name is "Core (Tctl/Tdie)" or "CPU Package");
        guess ??= temps.FirstOrDefault();
        if (guess is not null)
        {
            curve.SensorId = guess.Id;
            curve.SensorName = guess.Label;
        }
        Fans.Curves.Add(curve);
        _selectedCurve = name;
        _app.FanSettingsChanged();
        BuildCurves();
    }

    private async Task RenameCurveAsync()
    {
        var curve = Fans.Curves.FirstOrDefault(c => c.Name == _selectedCurve);
        if (curve is null) return;
        string old = curve.Name;
        string? name = await _window.Prompt("Rename curve", "", old, n => ValidateCurveName(n, curve));
        if (name is null || name == old) return;
        ConfigReferences.FanCurve(_app.Config, old, name);
        curve.Name = name;
        _selectedCurve = name;
        _app.FanSettingsChanged();
        _fanSignature = "";
        BuildCurves();
        Tick();
    }

    private async Task DeleteCurveAsync()
    {
        var curve = Fans.Curves.FirstOrDefault(c => c.Name == _selectedCurve);
        if (curve is null) return;
        var mixes = Fans.Curves.Where(c => c != curve && c.Type == FanCurveType.Mix && c.MixOf.Contains(curve.Name, StringComparer.OrdinalIgnoreCase)).Select(c => $"“{c.Name}”").ToList();
        if (mixes.Count > 0)
        {
            _app.Notify($"“{curve.Name}” is part of a mix", $"Take it out of {string.Join(", ", mixes)} first, then delete it.", NoticeKind.Warning);
            return;
        }
        int users = Fans.Presets.SelectMany(p => p.Fans).Count(e => string.Equals(e.CurveName, curve.Name, StringComparison.OrdinalIgnoreCase) && e.Mode == FanMode.Curve);
        if (!await _window.Confirm($"Delete the “{curve.Name}” curve?", users > 0 ? $"{GoodieBag.SimRacing.Race.RaceFormat.Count(users, "fan setting")} {(users == 1 ? "uses" : "use")} it and will go back to Auto." : "No fans use it.", "Delete", danger: true))
            return;
        ConfigReferences.FanCurve(_app.Config, curve.Name, null);
        Fans.Curves.Remove(curve);
        _selectedCurve = null;
        _app.FanSettingsChanged();
        _fanSignature = "";
        BuildCurves();
        Tick();
    }

    private string? ValidateCurveName(string name, FanCurve? self) =>
        name.Length == 0 ? "Give the curve a name."
        : Fans.Curves.Any(c => c != self && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) ? $"There's already a curve called “{name}”." : null;

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("rearrange", () =>
        {
            SetRearranging(true);
            return Task.CompletedTask;
        });
        yield return ("hidden-and-limits", () =>
        {
            SetRearranging(false);
            _showHidden = true;
            _fanSignature = "";
            Tick();
            return Task.CompletedTask;
        });
        yield return ("limits-open", () =>
        {
            _showHidden = false;
            _fanSignature = "";
            Tick();
            UpdateLayout();
            foreach (var open in _openLimits.ToList()) open();
            return Task.CompletedTask;
        });
        yield return ("curves-open", () =>
        {
            foreach (var open in _openCurves.ToList()) open();
            return Task.CompletedTask;
        });
        yield return ("mix", () =>
        {
            _selectedCurve = Fans.Curves.FirstOrDefault(c => c.Type == FanCurveType.Mix)?.Name ?? _selectedCurve;
            BuildCurves();
            _curveEditorHost.BringIntoView();
            return Task.CompletedTask;
        });
        yield return ("calibration", async () =>
        {
            var fan = _app.Hardware.Catalog?.Fans.FirstOrDefault(f => f.ControlId == "/lpc/nct6798d/control/3");
            if (fan is null) return;
            var dialog = new CalibrationDialog(_app, _window, fan);
            _ = _window.ShowModal(dialog.Card);
            dialog.Start();
            await Task.Delay(4000);
        });
        yield return ("calibration-done", async () =>
        {
            await Task.Delay(9000);
        });
        yield return ("closed", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
    }
}

internal sealed class CalibrationDialog
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly FanInfo _fan;
    private readonly FanHardware _hw;
    private readonly StackPanel _content = new();
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
    private CancellationTokenSource? _cts;
    private FanCalibration? _result;
    private bool _testStop;

    public Border Card { get; }

    public CalibrationDialog(AppServices app, MainWindow window, FanInfo fan)
    {
        _app = app;
        _window = window;
        _fan = fan;
        _hw = app.Config.Fans.HardwareFor(fan.ControlId);

        var stack = new StackPanel();
        var title = Ui.Text($"Calibrate {app.FanName(fan)}", "H2");
        title.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(title);
        stack.Children.Add(_content);
        stack.Children.Add(_buttons);
        Card = new Tile
        {
            Style = Ui.Res<Style>("LiftedCard"),
            Width = 560,
            Padding = new Thickness(26, 22, 26, 20),
            Child = stack,
        };

        if (!fan.HasRpm || fan.Rpm is null)
            ShowManual("This fan doesn't report its speed, so it can't be measured automatically. You can enter what you know about it instead.");
        else
            ShowIntro();
    }

    public void Cancel() => _cts?.Cancel();

    private void ShowIntro()
    {
        _content.Children.Clear();
        _content.Children.Add(new TextBlock
        {
            Text = "Goodie Bag will step this fan from full speed down, wait for it to settle at each step and measure its RPM. It takes about a minute and changes nothing else.",
            Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 6, 0, 12),
        });
        foreach (var line in new[]
                 {
                     _hw.IsPump ? "This is marked as a pump, so it never goes below 60%." : "It never goes below 20% unless you ask it to test fan stop.",
                     $"If the CPU or GPU passes {FanCalibrator.AbortTempC:0} °C it stops and hands the fan back. Calibrate while the PC is idle.",
                     "Afterwards the fan goes back to whatever it was doing.",
                 })
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.Shield"), Width = 14, Height = 14, Foreground = Ui.Res<Brush>("SageDeep"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) });
            row.Children.Add(new TextBlock { Text = line, Style = Ui.Res<Style>("Body"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            _content.Children.Add(row);
        }
        var stop = new CheckBox
        {
            Content = "Also test whether it can stop completely (fan-stop)",
            IsEnabled = !_hw.IsPump,
            Margin = new Thickness(0, 14, 0, 0),
            ToolTip = "Takes the fan down to 0% briefly and then finds the speed it needs to start again.",
        };
        stop.Click += (_, _) => _testStop = stop.IsChecked == true;
        _content.Children.Add(stop);

        _buttons.Children.Clear();
        var manual = new Button { Content = "Enter values manually", Style = Ui.Res<Style>("GhostButton"), Margin = new Thickness(0, 0, 8, 0) };
        manual.Click += (_, _) => ShowManual(null);
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => _window.CloseModal(false);
        var start = new Button { Content = "Start calibration", Style = Ui.Res<Style>("PrimaryButton") };
        start.Click += (_, _) => Start();
        _buttons.Children.Add(manual);
        _buttons.Children.Add(cancel);
        _buttons.Children.Add(start);
    }

    public async void Start()
    {
        _content.Children.Clear();
        var rpm = new TextBlock { Text = "–", Style = Ui.Res<Style>("BigValue"), FontSize = 40 };
        var rpmRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var spinner = _hw.IsPump
            ? FanSpinner.Pump(Ui.Res<Brush>("AccentText"), Ui.Res<Brush>("TextFaint"), 34)
            : new FanSpinner(Ui.Res<Geometry>("I.Fan"), Ui.Res<Brush>("AccentText"), 34);
        spinner.Margin = new Thickness(0, 0, 14, 0);
        rpmRow.Children.Add(spinner);
        rpmRow.Children.Add(rpm);
        rpmRow.Children.Add(new TextBlock { Text = "RPM", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(6, 0, 0, 9), VerticalAlignment = VerticalAlignment.Bottom });
        _content.Children.Add(rpmRow);
        var status = new TextBlock { Text = "Starting…", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 6, 0, 10) };
        _content.Children.Add(status);
        var bar = new Meter { Height = 8, Steps = 10 };
        _content.Children.Add(bar);
        var steps = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        _content.Children.Add(steps);

        _buttons.Children.Clear();
        var stop = new Button { Content = "Stop" };
        stop.Click += (_, _) => _cts?.Cancel();
        _buttons.Children.Add(stop);

        _cts = new CancellationTokenSource();
        var seen = new HashSet<int>();
        var progress = new Progress<CalibrationStep>(s =>
        {
            if (s.Rpm is { } r) { rpm.Text = r.ToString("N0"); spinner.SetRpm(r, Motion.Enabled); }
            status.Text = s.Status;
            Motion.To(bar, Meter.ValueProperty, s.Progress, 400);
            if (s.Status.Contains('→') && s.Rpm is { } done && seen.Add(s.Percent * 100000 + done))
            {
                var chip = Ui.Chip($"{s.Percent}% · {done:N0}");
                steps.Children.Add(chip);
                Motion.Enter(chip, distance: 4, durationMs: 180);
            }
        });

        var calibrator = new FanCalibrator(_app.Hardware, _app.Demo);
        var options = new CalibrationOptions { TestStop = _testStop && !_hw.IsPump, Floor = _hw.IsPump ? 60 : 20 };
        try
        {
            _result = await calibrator.RunAsync(_fan.ControlId, options, progress, _cts.Token);
            ShowResult();
        }
        catch (OperationCanceledException)
        {
            status.Text = "Stopped. The fan is back to normal.";
            ShowCloseOnly();
        }
        catch (CalibrationAbortedException ex)
        {
            status.Text = (ex.Safety ? "Stopped for safety: " : "Stopped: ") + ex.Message;
            status.Foreground = Ui.Res<Brush>("Danger");
            ShowCloseOnly();
        }
        catch (Exception ex)
        {
            status.Text = "Calibration failed: " + ex.Message;
            ShowCloseOnly();
        }
    }

    private void ShowCloseOnly()
    {
        _buttons.Children.Clear();
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => _window.CloseModal(false);
        _buttons.Children.Add(close);
    }

    private void ShowResult()
    {
        var r = _result!;
        _content.Children.Clear();
        var badge = Ui.Chip("Calibration complete", "SageSoft", "SageDeep");
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        badge.Margin = new Thickness(0, 12, 0, 12);
        _content.Children.Add(badge);

        var grid = new UniformGrid { Columns = 2 };
        void Stat(string label, string value)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 16, 12) };
            s.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
            s.Children.Add(new TextBlock { Text = value, Style = Ui.Res<Style>("H2"), TextWrapping = TextWrapping.Wrap });
            grid.Children.Add(s);
        }
        int minRpm = r.Points.Where(p => p.Rpm > 0).Select(p => p.Rpm).DefaultIfEmpty(0).Min();
        Stat("Maximum speed", $"{r.MaxRpm:N0} RPM");
        Stat("Slowest steady speed", $"{minRpm:N0} RPM at {r.MinStablePercent}%");
        Stat("Fan stop", r.StopPercent is { } stop ? $"Stops at {stop}%" + (r.StartPercent is { } start ? $", restarts at {start}%" : "") : "Didn't stop in the tested range");
        Stat("Readings", r.StableReadings ? "Steady" : "Jumpy (sensor noise)");
        _content.Children.Add(grid);

        if (!r.RespondsToControl)
            _content.Children.Add(new TextBlock
            {
                Text = "The speed barely changed between steps. This fan may be a 3-pin (DC) fan on a PWM-only header, or the BIOS may still be controlling it. Check the header mode in the BIOS (PWM / DC / Auto).",
                Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>("ApricotDeep"), Margin = new Thickness(0, 0, 0, 6),
            });
        else
            _content.Children.Add(new TextBlock
            {
                Text = $"Curves will never ask this fan for less than {r.MinStablePercent}%" + (r is { StopPercent: not null, StartPercent: not null } ? ", and you can now allow fan stop under Limits." : "."),
                Style = Ui.Res<Style>("Caption"),
            });

        _buttons.Children.Clear();
        var discard = new Button { Content = "Discard", Margin = new Thickness(0, 0, 8, 0) };
        discard.Click += (_, _) => _window.CloseModal(false);
        var save = new Button { Content = "Save calibration", Style = Ui.Res<Style>("PrimaryButton") };
        save.Click += (_, _) => Save(r);
        _buttons.Children.Add(discard);
        _buttons.Children.Add(save);
    }

    private void ShowManual(string? note)
    {
        _content.Children.Clear();
        _content.Children.Add(new TextBlock
        {
            Text = note ?? "Enter what you know about this fan (from its spec sheet, or from watching it in the BIOS).",
            Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 6, 0, 12),
        });
        var existing = _hw.Calibration;
        var maxRpm = new TextBox { Text = (existing?.MaxRpm ?? 0) > 0 ? existing!.MaxRpm.ToString() : "", Tag = "e.g. 1500", Width = 120 };
        var minPct = new TextBox { Text = (existing?.MinStablePercent ?? 20).ToString(), Width = 120 };
        var stopPct = new TextBox { Text = existing?.StopPercent?.ToString() ?? "", Tag = "leave empty", Width = 120 };
        var startPct = new TextBox { Text = existing?.StartPercent?.ToString() ?? "", Tag = "leave empty", Width = 120 };
        _content.Children.Add(Ui.SettingRow("Maximum speed (RPM)", null, maxRpm));
        _content.Children.Add(Ui.SettingRow("Lowest steady speed (%)", "Below this it stalls or surges.", minPct));
        _content.Children.Add(Ui.SettingRow("Stops at or below (%)", "Only if the fan supports stopping.", stopPct));
        _content.Children.Add(Ui.SettingRow("Needs to restart (%)", "Speed that gets it spinning again.", startPct));
        var error = new TextBlock { Foreground = Ui.Res<Brush>("Danger"), FontSize = 12.5, Visibility = Visibility.Collapsed };
        _content.Children.Add(error);

        _buttons.Children.Clear();
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => _window.CloseModal(false);
        var save = new Button { Content = "Save", Style = Ui.Res<Style>("PrimaryButton") };
        save.Click += (_, _) =>
        {
            int? Parse(TextBox b, int lo, int hi) => int.TryParse(b.Text.Trim(), out int v) && v >= lo && v <= hi ? v : null;
            int max = Parse(maxRpm, 0, 20000) ?? 0;
            int? min = Parse(minPct, 0, 100);
            int? stop = string.IsNullOrWhiteSpace(stopPct.Text) ? null : Parse(stopPct, 0, 100);
            int? start = string.IsNullOrWhiteSpace(startPct.Text) ? null : Parse(startPct, 0, 100);
            if (min is null || (!string.IsNullOrWhiteSpace(maxRpm.Text) && Parse(maxRpm, 0, 20000) is null) || (!string.IsNullOrWhiteSpace(stopPct.Text) && stop is null) || (!string.IsNullOrWhiteSpace(startPct.Text) && start is null))
            {
                error.Text = "Use whole numbers: RPM 0–20000, percentages 0–100.";
                error.Visibility = Visibility.Visible;
                return;
            }
            var cal = new FanCalibration
            {
                Date = DateTime.Now, Manual = true, MaxRpm = max, MinStablePercent = min.Value, StopPercent = stop, StartPercent = start,
            };
            Save(cal);
        };
        _buttons.Children.Add(cancel);
        _buttons.Children.Add(save);
    }

    private void Save(FanCalibration calibration)
    {
        _hw.Calibration = calibration;
        if (calibration.StopPercent is null)
            _hw.AllowStop = false;
        _app.FanSettingsChanged();
        _app.Notify("Calibration saved", $"{_app.FanName(_fan)}: {calibration.MaxRpm:N0} RPM max, steady from {calibration.MinStablePercent}%.", NoticeKind.Success);
        _window.CloseModal(true);
    }
}

internal sealed class BatchCalibrationDialog
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly List<FanInfo> _fans;
    private readonly StackPanel _content = new();
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
    private readonly List<(FanInfo Fan, FanCalibration Result)> _results = new();
    private CancellationTokenSource? _cts;
    private bool _testStop;

    public Border Card { get; }

    public BatchCalibrationDialog(AppServices app, MainWindow window, List<FanInfo> fans)
    {
        _app = app;
        _window = window;
        _fans = fans;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text("Calibrate all fans", "H2"));
        stack.Children.Add(_content);
        stack.Children.Add(_buttons);
        Card = new Tile
        {
            Style = Ui.Res<Style>("LiftedCard"),
            Width = 600,
            Padding = new Thickness(26, 22, 26, 20),
            Child = stack,
        };
        ShowIntro();
    }

    public void Cancel() => _cts?.Cancel();

    private void ShowIntro()
    {
        _content.Children.Clear();
        _content.Children.Add(new TextBlock
        {
            Text = $"{GoodieBag.SimRacing.Race.RaceFormat.Count(_fans.Count, "fan")} report their speed. They're calibrated together, each on its own, in about a minute. Pumps never go below 60%.",
            Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10),
        });
        var list = new WrapPanel();
        foreach (var fan in _fans)
            list.Children.Add(Ui.Chip(_app.FanName(fan)));
        _content.Children.Add(list);
        _content.Children.Add(new TextBlock
        {
            Text = $"If the CPU or GPU passes {FanCalibrator.AbortTempC:0} °C the run stops and the fans go back to normal. Calibrate while the PC is idle.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
        });
        var stop = new CheckBox
        {
            Content = "Also test whether they can stop completely (fan-stop)",
            Margin = new Thickness(0, 14, 0, 0),
            ToolTip = "Takes each fan (not pumps) down to 0% briefly and then finds the speed it needs to start again.",
        };
        stop.Click += (_, _) => _testStop = stop.IsChecked == true;
        _content.Children.Add(stop);

        _buttons.Children.Clear();
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => _window.CloseModal(false);
        var start = new Button { Content = "Start calibration", Style = Ui.Res<Style>("PrimaryButton") };
        start.Click += (_, _) => Start();
        _buttons.Children.Add(cancel);
        _buttons.Children.Add(start);
    }

    private async void Start()
    {
        _content.Children.Clear();
        var current = new TextBlock { Text = "Calibrating", Style = Ui.Res<Style>("H3"), Margin = new Thickness(0, 12, 0, 0) };
        var status = new TextBlock { Text = "Starting…", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 4, 0, 10) };
        var bar = new Meter { Height = 8, Steps = _fans.Count };
        var rows = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        _content.Children.Add(current);
        _content.Children.Add(status);
        _content.Children.Add(bar);
        _content.Children.Add(rows);

        _buttons.Children.Clear();
        var stopButton = new Button { Content = "Stop" };
        stopButton.Click += (_, _) => _cts?.Cancel();
        _buttons.Children.Add(stopButton);

        _cts = new CancellationTokenSource();
        var calibrator = new FanCalibrator(_app.Hardware, _app.Demo);
        string? stoppedBecause = null;
        var shares = new double[_fans.Count];
        status.Text = $"{GoodieBag.SimRacing.Race.RaceFormat.Count(_fans.Count, "fan")} at once. Keep the PC idle.";

        async Task One(int index)
        {
            var fan = _fans[index];
            var hw = _app.Config.Fans.HardwareFor(fan.ControlId);
            string name = _app.FanName(fan);
            var (row, detail) = Row(name, "Waiting…", "TextMuted");
            rows.Children.Add(row);
            var progress = new Progress<CalibrationStep>(step =>
            {
                detail.Text = step.Rpm is { } r ? $"{step.Status} · {r:N0} RPM" : step.Status;
                shares[index] = step.Progress;
                Motion.To(bar, Meter.ValueProperty, shares.Average(), 400);
            });
            var options = new CalibrationOptions { TestStop = _testStop && !hw.IsPump, Floor = hw.IsPump ? 60 : 20 };
            try
            {
                var result = await calibrator.RunAsync(fan.ControlId, options, progress, _cts.Token);
                _results.Add((fan, result));
                Tone(detail, $"{result.MaxRpm:N0} RPM max, steady from {result.MinStablePercent}%"
                    + (result.StopPercent is { } stop ? $", stops at {stop}%" : "")
                    + (result.RespondsToControl ? "" : " · barely responds to control (DC fan or BIOS in charge?)"), "SageDeep");
            }
            catch (OperationCanceledException)
            {
                stoppedBecause ??= "Stopped. The fans are back to normal.";
                Tone(detail, "Stopped", "TextMuted");
            }
            catch (CalibrationAbortedException ex) when (ex.Safety)
            {
                stoppedBecause = "Stopped for safety: " + ex.Message;
                Tone(detail, "Stopped", "TextMuted");
                _cts.Cancel();
            }
            catch (Exception ex)
            {
                Tone(detail, "Skipped: " + ex.Message, "ApricotDeep");
            }
            shares[index] = 1;
        }
        await Task.WhenAll(Enumerable.Range(0, _fans.Count).Select(One));
        _results.Sort((a, b) => _fans.IndexOf(a.Fan).CompareTo(_fans.IndexOf(b.Fan)));

        current.Text = stoppedBecause is null ? "Done" : "Stopped";
        status.Text = stoppedBecause ?? $"{_results.Count} of {GoodieBag.SimRacing.Race.RaceFormat.Count(_fans.Count, "fan")} calibrated.";
        if (stoppedBecause is not null && stoppedBecause.StartsWith("Stopped for safety")) status.Foreground = Ui.Res<Brush>("Danger");
        Motion.To(bar, Meter.ValueProperty, 1, 400);

        _buttons.Children.Clear();
        var discard = new Button { Content = _results.Count == 0 ? "Close" : "Discard", Margin = new Thickness(0, 0, 8, 0) };
        discard.Click += (_, _) => _window.CloseModal(false);
        _buttons.Children.Add(discard);
        if (_results.Count > 0)
        {
            var save = new Button { Content = _results.Count == 1 ? "Save calibration" : $"Save {_results.Count} calibrations", Style = Ui.Res<Style>("PrimaryButton") };
            save.Click += (_, _) => Save();
            _buttons.Children.Add(save);
        }
    }

    private static (DockPanel Row, TextBlock Detail) Row(string name, string detail, string tone)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var label = TextFit.TipWhenTrimmed(new TextBlock { Text = name, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, Width = 170, TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(label);
        var text = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap };
        Tone(text, detail, tone);
        row.Children.Add(text);
        return (row, text);
    }

    private static void Tone(TextBlock text, string detail, string tone)
    {
        text.Text = detail;
        text.SetResourceReference(TextBlock.ForegroundProperty, tone);
    }

    private void Save()
    {
        foreach (var (fan, result) in _results)
        {
            var hw = _app.Config.Fans.HardwareFor(fan.ControlId);
            hw.Calibration = result;
            if (result.StopPercent is null)
                hw.AllowStop = false;
        }
        _app.FanSettingsChanged();
        _app.Notify("Calibrations saved", $"{GoodieBag.SimRacing.Race.RaceFormat.Count(_results.Count, "fan")} calibrated.", NoticeKind.Success);
        _window.CloseModal(true);
    }
}

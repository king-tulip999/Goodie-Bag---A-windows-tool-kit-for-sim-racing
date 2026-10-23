using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Overlay;
using GoodieBag.Services;
using GoodieBag.Theme;
using WinForms = System.Windows.Forms;

namespace GoodieBag.Views.Pages;

internal sealed class OverlayPage : UserControl, IPage, IScreenshotStates
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly bool _embedded;
    private readonly StackPanel _body;
    private readonly StackPanel _left = new(), _right = new();
    private StackPanel _position = new();
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, SnapsToDevicePixels = true, ToolTip = "Drag a value or a group name to change the order" };
    private readonly Canvas _scene = new() { ClipToBounds = true };
    private (double W, double H) _overlaySize;
    private (string Group, OverlayMetric? Metric)? _drag;
    private bool _reordered;
    private Vector _grab;
    private Size _heldSize;
    private readonly Border _ghost = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1.5), Opacity = 0.92 };
    private readonly Border _slot = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1.5) };
    private readonly CheckBox _visible;
    private readonly TextBlock _previewNote = new();
    private readonly TextBlock _contrast = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

    private OverlaySettings O => _app.Config.Overlay;
    private OverlayLayout L => O.Current;

    public OverlayPage(AppServices app, MainWindow window, bool embedded = false)
    {
        _app = app;
        _window = window;
        _embedded = embedded;
        _body = new StackPanel { Margin = embedded ? new Thickness(0) : new Thickness(28, 0, 28, 28) };
        _visible = Ui.Switch(O.Visible, on => _app.SetOverlayVisible(on));
        _visible.Content = "Show overlay";
        _visible.ToolTip = "Show or hide the overlay over your game (also from its keybind and the tray)";
        Ui.Anchor(_visible, "overlay.visible");
        Content = embedded ? _body : new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body) };
        BuildFrame();
        BuildSettings();
    }

    public void OnShown()
    {
        _visible.IsChecked = O.Visible;
        if (!ReferenceEquals(_built, L)) BuildSettings();
        RenderPreview();
        _app.Tick += RenderPreview;
        _app.OverlayChanged += OnOverlayChanged;
        _app.Network.Acquire("Overlay page");
        _app.Frames.Acquire("Overlay page");
        if (!_embedded) Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _app.Tick -= RenderPreview;
        _app.OverlayChanged -= OnOverlayChanged;
        _app.Network.Release("Overlay page");
        _app.Frames.Release("Overlay page");
    }

    private void OnOverlayChanged()
    {
        _visible.IsChecked = O.Visible;
        if (!ReferenceEquals(_built, L)) BuildSettings();
        RenderPreview();
    }

    private void Changed()
    {
        _app.OverlaySettingsChanged();
        UpdateContrast();
    }

    private void BuildFrame()
    {
        if (!_embedded)
            BuildTitle();

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_right, 2);
        columns.Children.Add(_left);
        columns.Children.Add(_right);
        _body.Children.Add(columns);
    }

    private void BuildTitle()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, Ui.HeaderGap) };
        var titles = new StackPanel();
        titles.Children.Add(Ui.PluginTitle(Plugins.PluginIds.Overlay, "Performance Overlay"));
        header.Children.Add(titles);
        _body.Children.Add(header);
    }

    private OverlayLayout? _built;

    private void BuildSettings()
    {
        _built = L;
        _left.Children.Clear();
        _right.Children.Clear();
        BuildLayouts();
        BuildPreview();
        BuildGeneral();
        BuildPosition();
        BuildMetrics();
        BuildNote();
        UpdateContrast();
    }

    private void BuildNote()
    {
        _right.Children.Add(new Tile
        {
            Style = Ui.Res<Style>("Inset"),
            Child = new TextBlock
            {
                Text = "The overlay shows over borderless and windowed games, not exclusive fullscreen. On some setups a window on top of the game can stop Windows using its lowest-latency presentation mode; hide the overlay with the keybind for races if you notice it.",
                Style = Ui.Res<Style>("Caption"),
            },
        });
    }

    private void BuildPreview()
    {
        var scene = new Grid { Height = 300, ClipToBounds = true };
        scene.SizeChanged += (_, e) => { if (e.WidthChanged) scene.Height = Math.Round(e.NewSize.Width * 9 / 16); PlacePreview(); };
        scene.Children.Add(new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/OverlayPreview.jpg")),
            Stretch = Stretch.Uniform,
        });
        RenderOptions.SetBitmapScalingMode(scene.Children[0], BitmapScalingMode.HighQuality);
        if (_preview.Parent is null)
        {
            _scene.Children.Add(_preview);
            _scene.Children.Add(_slot);
            _scene.Children.Add(_ghost);
            HookPreviewDrag();
        }
        scene.Children.Add(Detached(_scene));
        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Ui.Text("Preview", "H2");
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);
        Detached(_visible).VerticalAlignment = VerticalAlignment.Center;
        _visible.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_visible, 1);
        head.Children.Add(_visible);
        stack.Children.Add(head);
        stack.Children.Add(new Tile { CornerRadius = ThemeManager.Round(12), Margin = new Thickness(0, 12, 0, 0), Child = new Tile { CornerRadius = ThemeManager.Round(12), Child = scene, ClipToBounds = true } });
        _previewNote.Style = Ui.Res<Style>("Caption");
        _previewNote.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(Detached(_previewNote));
        _left.Children.Add(Card(stack));
    }

    private void HookPreviewDrag()
    {
        _preview.SizeChanged += (_, _) => PlacePreview();
        _preview.MouseLeftButtonDown += (_, e) =>
        {
            var at = InFrame(e);
            if (HitAt(at) is not { } hit || HeldRect((hit.Group, hit.Metric)) is not { } box) return;
            _drag = (hit.Group, hit.Metric);
            _grab = at - box.TopLeft;
            _heldSize = box.Size;
            PickUp(box);
            _preview.CaptureMouse();
            MoveGhost(at);
            e.Handled = true;
        };
        _preview.MouseMove += (_, e) =>
        {
            var at = InFrame(e);
            if (_drag is { } held && _preview.IsMouseCaptured)
            {
                MoveGhost(at);
                DragTo(held, at - _grab + new Vector(_heldSize.Width / 2, _heldSize.Height / 2));
                ShowSlot();
            }
        };
        _preview.MouseLeftButtonUp += (_, _) =>
        {
            if (_drag is not null) _preview.ReleaseMouseCapture();
        };
        _preview.LostMouseCapture += (_, _) =>
        {
            _drag = null;
            _ghost.Visibility = _slot.Visibility = Visibility.Collapsed;
            _ghost.Child = null;
            if (!_reordered) return;
            _reordered = false;
            Dispatcher.InvokeAsync(BuildSettings);
        };
    }

    private Point InFrame(MouseEventArgs e)
    {
        var p = e.GetPosition(_preview);
        double k = _preview.ActualWidth > 0 ? _overlaySize.W / _preview.ActualWidth : 1;
        return new Point(p.X * k, p.Y * k);
    }

    private Rect? HeldRect((string Group, OverlayMetric? Metric) held)
    {
        Rect? box = null;
        foreach (var hit in _app.Overlay.PreviewHits)
        {
            if (hit.Group != held.Group || (held.Metric is not null && hit.Metric != held.Metric)) continue;
            var r = new Rect(hit.Rect.Left, hit.Rect.Top, hit.Rect.Width, hit.Rect.Height);
            box = box is { } b ? Rect.Union(b, r) : r;
        }
        return box;
    }

    private void PickUp(Rect box)
    {
        if (_preview.Source is not BitmapSource source) return;
        var pixels = new Int32Rect((int)Math.Floor(box.X), (int)Math.Floor(box.Y), (int)Math.Ceiling(box.Width) + 1, (int)Math.Ceiling(box.Height) + 1);
        pixels.Width = Math.Min(pixels.Width, source.PixelWidth - pixels.X);
        pixels.Height = Math.Min(pixels.Height, source.PixelHeight - pixels.Y);
        if (pixels.X < 0 || pixels.Y < 0 || pixels.Width <= 0 || pixels.Height <= 0) return;
        var copy = new WriteableBitmap(new CroppedBitmap(source, pixels));
        copy.Freeze();
        _ghost.Child = new Image { Source = copy, Stretch = Stretch.Fill };
        _ghost.Background = Ui.Res<Brush>("Surface");
        _ghost.BorderBrush = Ui.Res<Brush>("AccentText");
        _slot.BorderBrush = Ui.Res<Brush>("AccentText");
        _slot.Background = Ui.Res<Brush>("AccentSoft").CloneCurrentValue();
        _slot.Background.Opacity = 0.35;
        _ghost.Visibility = Visibility.Visible;
        ShowSlot();
    }

    private double PreviewScale => _overlaySize.W > 0 && _preview.ActualWidth > 0 ? _preview.ActualWidth / _overlaySize.W : 1;

    private void MoveGhost(Point at)
    {
        double k = PreviewScale;
        var topLeft = at - _grab;
        _ghost.Width = _heldSize.Width * k + 3;
        _ghost.Height = _heldSize.Height * k + 3;
        Canvas.SetLeft(_ghost, Canvas.GetLeft(_preview) + topLeft.X * k - 1.5 + 3);
        Canvas.SetTop(_ghost, Canvas.GetTop(_preview) + topLeft.Y * k - 1.5 - 3);
    }

    private void ShowSlot()
    {
        if (_drag is not { } held || HeldRect(held) is not { } box) return;
        double k = PreviewScale;
        _slot.Width = box.Width * k + 3;
        _slot.Height = box.Height * k + 3;
        Canvas.SetLeft(_slot, Canvas.GetLeft(_preview) + box.X * k - 1.5);
        Canvas.SetTop(_slot, Canvas.GetTop(_preview) + box.Y * k - 1.5);
        _slot.Visibility = Visibility.Visible;
    }

    private OverlayHit? HitAt(Point p)
    {
        foreach (var hit in _app.Overlay.PreviewHits)
            if (hit.Rect.Contains((float)p.X, (float)p.Y)) return hit;
        return null;
    }

    private void DragTo((string Group, OverlayMetric? Metric) held, Point at)
    {
        var hits = _app.Overlay.PreviewHits;
        if (held.Metric is { } metric)
        {
            var cells = hits.Where(h => h.Group == held.Group && h.Metric is not null).GroupBy(h => h.Metric!.Value)
                .Select(g => (Metric: g.Key, Start: g.Min(h => h.Rect.Left), End: g.Max(h => h.Rect.Right))).ToList();
            int from = cells.FindIndex(c => c.Metric == metric);
            if (from < 0) return;
            int to = OverlayRenderer.DropIndex(cells.Select(c => (c.Start, c.End)).ToList(), from, (float)at.X);
            if (to == from) return;
            var members = L.Metrics.Where(m => OverlayLayout.GroupOf(m.Metric) == held.Group).Select(m => m.Metric).ToList();
            L.MoveWithinGroup(metric, members.IndexOf(cells[to].Metric));
        }
        else
        {
            bool across = L.Style == OverlayStyle.Compact;
            var groups = hits.GroupBy(h => h.Group)
                .Select(g => (Group: g.Key, Start: g.Min(h => across ? h.Rect.Left : h.Rect.Top), End: g.Max(h => across ? h.Rect.Right : h.Rect.Bottom))).ToList();
            int from = groups.FindIndex(g => g.Group == held.Group);
            if (from < 0) return;
            int to = OverlayRenderer.DropIndex(groups.Select(g => (g.Start, g.End)).ToList(), from, (float)(across ? at.X : at.Y));
            if (to == from) return;
            L.MoveGroup(held.Group, L.GroupOrder().IndexOf(groups[to].Group));
        }
        _reordered = true;
        Changed();
    }

    private (double X, double Y) Room() =>
        (Math.Max(0, _scene.ActualWidth - _preview.ActualWidth), Math.Max(0, _scene.ActualHeight - _preview.ActualHeight));

    private void PlacePreview()
    {
        if (_scene.ActualWidth <= 0) return;
        _preview.MaxWidth = _scene.ActualWidth;
        _preview.MaxHeight = _scene.ActualHeight;
        var (monitorW, monitorH) = OverlayController.MonitorSize(L);
        var (fx, fy) = OverlayController.FractionOf(L, monitorW, monitorH, _overlaySize.W, _overlaySize.H);
        var (maxX, maxY) = Room();
        Canvas.SetLeft(_preview, Math.Round(fx * maxX));
        Canvas.SetTop(_preview, Math.Round(fy * maxY));
    }

    private void BuildLayouts()
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text("Layout", "H2"));
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var pick = Ui.Combo(O.Layouts.Select(l => new Choice(l.Name, l.Name)), O.ActiveLayout, c =>
        {
            O.ActiveLayout = c.Id!;
            Changed();
            BuildSettings();
        }, 200);
        pick.Margin = new Thickness(0, 0, 6, 6);
        row.Children.Add(pick);
        row.Children.Add(Wrapped(Ui.LabeledButton("I.Plus", "New", () => _ = NewLayoutAsync(copy: false), "GhostButton")));
        row.Children.Add(Wrapped(Ui.LabeledButton("I.Copy", "Duplicate", () => _ = NewLayoutAsync(copy: true), "GhostButton")));
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 6) };
        icons.Children.Add(Ui.IconButton("I.Pencil", "Rename", () => _ = RenameAsync()));
        var delete = Ui.IconButton("I.Trash", "Delete this layout", () => _ = DeleteAsync());
        delete.IsEnabled = O.Layouts.Count > 1;
        icons.Children.Add(delete);
        row.Children.Add(icons);
        stack.Children.Add(row);
        var card = Card(stack);
        Ui.Anchor(card, "overlay.layouts");
        _left.Children.Add(card);
    }

    private void BuildGeneral()
    {
        var hotkey = HotkeyEditor("overlay", () => O.Hotkey, hk => O.Hotkey = hk);
        var std = new OverlayLayout();
        Ui.SettingDefault D(Func<bool> same, Action reset, string shown) => new(same, () => { reset(); Changed(); BuildSettings(); }, shown);
        var general = new SettingsFlow();
        general.Children.Add(Ui.Text("General", "H2"));
        general.Children.Add(Ui.SubHeader("Keybinds", first: true));
        general.Children.Add(Anchored(Ui.SettingRow("Show / hide", null, hotkey), "overlay.hotkey"));
        general.Children.Add(Anchored(Ui.SettingRow("Reset FPS statistics", null,
            HotkeyEditor("overlay.fps", () => O.ResetFpsHotkey, hk => O.ResetFpsHotkey = hk)), "overlay.fpshotkey"));
        general.Children.Add(Ui.SubHeader("Data"));
        general.Children.Add(Anchored(Ui.SettingRow("Update rate", "All layouts.",
            Ui.Segmented(new[] { "Fast", "Normal", "Relaxed" }, O.UpdateIntervalMs switch { <= 250 => 0, >= 1000 => 2, _ => 1 },
                i => { O.UpdateIntervalMs = i switch { 0 => 250, 2 => 1000, _ => 500 }; Changed(); }, "Rate",
                new[] { "Fast: 4 times a second. Smoothest numbers, slightly more CPU", "Normal: twice a second", "Relaxed: once a second. The lightest; numbers jump less" }),
            D(() => O.UpdateIntervalMs == 500, () => O.UpdateIntervalMs = 500, "Normal")), "overlay.rate"));
        general.Children.Add(Ui.SettingRow("FPS statistics", "Average and 1% low cover the whole session for each game.",
            Ui.LabeledButton("I.Refresh", "Reset", _app.ResetFpsStats)));
        general.Children.Add(Ui.SubHeader("Look"));
        general.Children.Add(Anchored(Ui.SettingRow("Layout style", null,
            Ui.Segmented(new[] { "Expanded", "Compact" }, (int)L.Style, i => { L.Style = (OverlayStyle)i; Changed(); }, "Style",
                new[] { "One row per group (CPU, GPU, FPS…), easy to read at a glance", "Everything on one line: takes the least room over your game" }),
            D(() => L.Style == std.Style, () => L.Style = std.Style, std.Style.ToString())), "overlay.style"));
        general.Children.Add(Anchored(Ui.SettingRow("Text size", null, Ui.SliderWithValue(60, 200, L.FontScale, "{0:0}%", v => { L.FontScale = (int)v; Changed(); }, 160, 5),
            D(() => L.FontScale == std.FontScale, () => L.FontScale = std.FontScale, std.FontScale + "%")), "overlay.font"));
        general.Children.Add(Anchored(Ui.SettingRow("Background opacity", null, Ui.SliderWithValue(0, 100, L.BackgroundOpacity, "{0:0}%", v => { L.BackgroundOpacity = (int)v; Changed(); }, 160, 5),
            D(() => L.BackgroundOpacity == std.BackgroundOpacity, () => L.BackgroundOpacity = std.BackgroundOpacity, std.BackgroundOpacity + "%")), "overlay.opacity"));
        general.Children.Add(Anchored(Ui.SettingRow("Frame-time graph", "Spikes are stutters.",
            Ui.Switch(L.ShowGraph, v => { L.ShowGraph = v; Changed(); }),
            D(() => L.ShowGraph == std.ShowGraph, () => L.ShowGraph = std.ShowGraph, std.ShowGraph ? "On" : "Off")), "overlay.graph"));
        var rename = Ui.LabeledButton("I.Pencil", "Rename items…", () => _ = RenameItemsAsync());
        rename.ToolTip = "Your own names for what the overlay shows (e.g. GPU → 7900 XTX)";
        general.Children.Add(Anchored(Ui.SettingRow("Names", null, rename), "overlay.names"));
        BuildColors(general);
        _left.Children.Add(Card(general));
    }

    private void BuildPosition()
    {
        var position = new SettingsFlow();
        position.Children.Add(Ui.Text("Position", "H2"));
        var corners = new UniformGrid { Columns = 2 };
        string group = "Corner" + Guid.NewGuid().ToString("N");
        foreach (var (corner, label) in new[] { (OverlayCorner.TopLeft, "Top left"), (OverlayCorner.TopRight, "Top right"), (OverlayCorner.BottomLeft, "Bottom left"), (OverlayCorner.BottomRight, "Bottom right") })
        {
            var rb = new RadioButton { Style = Ui.Res<Style>("Segment"), GroupName = group, Content = label, IsChecked = L.Corner == corner, Margin = new Thickness(3) };
            var c = corner;
            rb.Checked += (_, _) =>
            {
                L.Corner = c;
                L.OffsetX = L.OffsetY = 16;
                Changed();
            };
            corners.Children.Add(rb);
        }
        position.Children.Add(Anchored(Ui.SettingRow("Corner", null, new Tile { Style = Ui.Res<Style>("Segmented"), Child = corners }), "overlay.position"));
        var monitors = SafeMonitors().Select(m => new Choice(m.Id, m.Label)).ToList();
        position.Children.Add(Ui.SettingRow("Monitor", null, Ui.Combo(monitors, L.Monitor, c => { L.Monitor = c.Id!; Changed(); }, 230)));
        _position = position;
        _right.Children.Add(Card(position));
    }

    private void BuildColors(StackPanel colors)
    {
        var custom = new WrapPanel { ItemWidth = 146, Visibility = L.Colors == OverlayColorPreset.Custom ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        colors.Children.Add(Anchored(Ui.SettingRow("Colors", null,
            ThemeMenu.OverlayButton(
                () => new ThemeMenu.Pick(L.Colors.IsApp, L.Colors == OverlayColorPreset.Custom, L.Colors.Theme ?? ThemeManager.Current.Id, L.ColorSet),
                pick =>
                {
                    if (pick.Custom)
                    {
                        if (L.Colors != OverlayColorPreset.Custom) L.Custom = OverlayPresets.For(L.Colors, L.Custom, L.ColorSet);
                        L.Colors = OverlayColorPreset.Custom;
                    }
                    else if (pick.FollowApp) { L.Colors = OverlayColorPreset.App; L.ColorSet = null; }
                    else { L.Colors = OverlayLayout.For(pick.Theme); L.ColorSet = pick.Set; }
                    Changed();
                    BuildSettings();
                }, followApp: true, customLabel: "Custom", minWidth: 200)), "overlay.colors"));

        var c = L.Custom;
        custom.Children.Add(ColorRow("Main text", () => c.Text, v => c.Text = v));
        custom.Children.Add(ColorRow("Secondary text", () => c.Secondary, v => c.Secondary = v));
        custom.Children.Add(ColorRow("Accent (labels)", () => c.Accent, v => c.Accent = v));
        custom.Children.Add(ColorRow("Background", () => c.Background, v => c.Background = v));
        custom.Children.Add(ColorRow("Graph", () => c.Graph, v => c.Graph = v));
        custom.Children.Add(ColorRow("FPS", () => c.Fps, v => c.Fps = v));
        custom.Children.Add(ColorRow("Warning", () => c.Warning, v => c.Warning = v));
        custom.Children.Add(ColorRow("Critical", () => c.Critical, v => c.Critical = v));
        colors.Children.Add(custom);
        _contrast.Style = Ui.Res<Style>("Caption");
        _contrast.Foreground = Ui.Res<Brush>("Warn");
        _contrast.Margin = new Thickness(0, 8, 0, 0);
        colors.Children.Add(Detached(_contrast));
    }

    private StackPanel ColorRow(string label, Func<string> get, Action<string> set)
    {
        var swatch = new Button { Width = 30, Height = 26, MinHeight = 0, Padding = new Thickness(0), ToolTip = "Pick a color", Margin = new Thickness(0, 0, 6, 0) };
        var hex = new TextBox { Width = 96, Text = get() };
        void Paint()
        {
            var color = OverlayRenderer.Parse(get(), System.Drawing.Color.Gray);
            swatch.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        }
        hex.TextChanged += (_, _) =>
        {
            string text = hex.Text.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            if (System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9A-Fa-f]{6}$"))
            {
                set(text.ToUpperInvariant());
                Paint();
                Changed();
            }
        };
        swatch.Click += (_, _) =>
        {
            if (!GoodieBag.Services.External.Allowed("color dialog")) return;
            var current = OverlayRenderer.Parse(get(), System.Drawing.Color.Gray);
            using var dialog = new WinForms.ColorDialog { Color = current, FullOpen = true, AnyColor = true };
            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                hex.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        };
        Paint();
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(swatch);
        controls.Children.Add(hex);
        var cell = new StackPanel { Margin = new Thickness(0, 4, 8, 6) };
        cell.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = label, Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis }));
        cell.Children.Add(controls);
        return cell;
    }

    private void UpdateContrast()
    {
        var colors = OverlayPresets.For(L.Colors, L.Custom, L.ColorSet);
        var text = Palettes.Hex(colors.Text);
        var bg = Palettes.Hex(colors.Background);
        double ratio = ThemeManager.Contrast(text, bg);
        bool weak = ratio < 3 && L.BackgroundOpacity >= 40;
        _contrast.Text = weak ? $"Text may be hard to read on this background (contrast {ratio:0.0}:1, 3:1 or more is easier on the eyes)." : "";
        _contrast.Visibility = weak ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildMetrics()
    {
        var metrics = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var head = new StackPanel();
        var title = Ui.Text("What to show", "H2");
        title.Margin = new Thickness(0, 0, 0, 4);
        var tip = Ui.IconText("I.Info", new TextBlock { Text = "Values and group names can be dragged into a new order in the live preview", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap }, "TextMuted");
        head.Children.Add(title);
        head.Children.Add(tip);
        metrics.Children.Add(head);
        foreach (var group in L.GroupOrder())
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var label = new TextBlock { Text = group, Style = Ui.Res<Style>("Eyebrow"), Width = 70, Margin = new Thickness(0, 7, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            row.Children.Add(label);
            var switches = new WrapPanel();
            foreach (var entry in L.Metrics.Where(m => OverlayLayout.GroupOf(m.Metric) == group))
            {
                var e = entry;
                var sw = Ui.Switch(e.Enabled, v => { e.Enabled = v; Changed(); });
                sw.Content = ShortLabel(e.Metric);
                sw.ToolTip = MetricLabel(e.Metric);
                sw.Width = 150;
                sw.Margin = new Thickness(0, 3, 8, 3);
                AutomationProperties.SetName(sw, MetricLabel(e.Metric));
                switches.Children.Add(sw);
            }
            row.Children.Add(switches);
            metrics.Children.Add(row);
        }
        Ui.Anchor(metrics, "overlay.metrics");
        _position.Children.Add(Ui.Divider());
        _position.Children.Add(metrics);
    }

    private static string ShortLabel(OverlayMetric m) => m switch
    {
        OverlayMetric.Fps => "FPS",
        OverlayMetric.AverageFps => "Average",
        OverlayMetric.OnePercentLow => "1% low",
        OverlayMetric.FrameTime => "Frame time",
        OverlayMetric.GpuTemp or OverlayMetric.CpuTemp => "Temp",
        OverlayMetric.GpuLoad or OverlayMetric.CpuLoad => "Usage",
        OverlayMetric.GpuHotSpot => "Hot spot",
        OverlayMetric.GpuClock or OverlayMetric.CpuClock => "Clock",
        OverlayMetric.GpuPower or OverlayMetric.CpuPower => "Power",
        OverlayMetric.GpuFan => "Fan speed",
        OverlayMetric.Vram => "VRAM used",
        OverlayMetric.Ram => "RAM used",
        OverlayMetric.Ping => "Ping",
        OverlayMetric.Network => "Down / up",
        _ => "Fan speeds",
    };

    public static string MetricLabel(OverlayMetric m) => m switch
    {
        OverlayMetric.Fps => "FPS",
        OverlayMetric.AverageFps => "Average FPS",
        OverlayMetric.OnePercentLow => "1% low",
        OverlayMetric.FrameTime => "Frame time",
        OverlayMetric.GpuTemp => "GPU temperature",
        OverlayMetric.GpuLoad => "GPU usage",
        OverlayMetric.GpuHotSpot => "GPU hot spot",
        OverlayMetric.GpuClock => "GPU clock",
        OverlayMetric.GpuPower => "GPU power",
        OverlayMetric.GpuFan => "GPU fan speed",
        OverlayMetric.Vram => "GPU VRAM",
        OverlayMetric.CpuTemp => "CPU temperature",
        OverlayMetric.CpuLoad => "CPU usage",
        OverlayMetric.CpuClock => "CPU clock",
        OverlayMetric.CpuPower => "CPU power",
        OverlayMetric.Ram => "RAM usage",
        OverlayMetric.Ping => "Network ping",
        OverlayMetric.Network => "Network download / upload",
        OverlayMetric.Fans => "Fan speeds",
        _ => m.ToString(),
    };

    private HotkeyBox HotkeyEditor(string id, Func<string?> get, Action<string?> set)
    {
        var box = new HotkeyBox();
        box.SetHotkey(get());
        box.HotkeyChanged += hk =>
        {
            if (_app.Hotkeys.Clash(hk, id) is { } clash)
            {
                _app.Notify("Keybind not changed", clash, NoticeKind.Warning, important: true);
                box.SetHotkey(get());
                return;
            }
            set(hk);
            _app.Save();
            _app.Hotkeys.RegisterAll();
        };
        return box;
    }

    private async Task NewLayoutAsync(bool copy)
    {
        string? name = copy
            ? await _window.Prompt("Duplicate layout", "A copy of the current layout, to change from there.", L.Name + " copy", n => ValidateName(n))
            : await _window.Prompt("New overlay layout", "Starts from the default layout.", $"Layout {O.Layouts.Count + 1}", n => ValidateName(n));
        if (name is null) return;
        var layout = copy ? ConfigStore.Clone(L) : new OverlayLayout();
        layout.Name = name;
        layout.Complete();
        O.Layouts.Add(layout);
        O.ActiveLayout = name;
        Changed();
        BuildSettings();
    }

    private async Task RenameItemsAsync()
    {
        var layout = L;
        var groups = layout.Metrics.Select(m => OverlayLayout.GroupOf(m.Metric)).Distinct().ToList();
        var stack = new StackPanel();
        var groupBoxes = new Dictionary<string, TextBox>();
        var tagBoxes = new Dictionary<OverlayMetric, TextBox>();
        stack.Children.Add(Ui.EditorSection("Groups"));
        foreach (var g in groups)
        {
            var box = new TextBox { Text = layout.GroupNames.TryGetValue(g, out var own) ? own : "", Tag = g, MaxLength = 24, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            groupBoxes[g] = box;
            stack.Children.Add(Ui.FieldRow(g, box));
        }
        stack.Children.Add(Ui.EditorSection("Value tags"));
        stack.Children.Add(new TextBlock { Text = "The small word in front of a value. Leave a box empty for no tag.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (var (metric, standard) in TagDefaults)
        {
            if (!layout.Metrics.Any(m => m.Metric == metric)) continue;
            var box = new TextBox { Text = layout.MetricTag(metric, standard), Tag = standard.Length > 0 ? standard : "no tag", MaxLength = 16, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
            tagBoxes[metric] = box;
            stack.Children.Add(Ui.FieldRow(MetricLabel(metric), box));
        }
        var reset = Ui.LabeledButton("I.Refresh", "Standard names", () =>
        {
            foreach (var b in groupBoxes.Values) b.Text = "";
            foreach (var (metric, standard) in TagDefaults) if (tagBoxes.TryGetValue(metric, out var b)) b.Text = standard;
        }, "GhostButton");
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(reset);
        var card = Ui.Dialog(_window, "Rename items", $"Names shown in the overlay for “{layout.Name}”.", new ScrollViewer { Content = stack, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Ui.DialogButtons(_window, "Save", () =>
            {
                layout.GroupNames = groupBoxes.Where(p => !string.IsNullOrWhiteSpace(p.Value.Text) && p.Value.Text.Trim() != p.Key)
                    .ToDictionary(p => p.Key, p => p.Value.Text.Trim(), StringComparer.OrdinalIgnoreCase);
                layout.MetricTags = tagBoxes.Where(p => p.Value.Text.Trim() != TagDefaults.First(d => d.Metric == p.Key).Tag)
                    .ToDictionary(p => p.Key, p => p.Value.Text.Trim());
                return true;
            }), null, 520);
        if (await _window.ShowModal(card)) Changed();
    }

    private static readonly (OverlayMetric Metric, string Tag)[] TagDefaults =
    {
        (OverlayMetric.AverageFps, "avg"), (OverlayMetric.OnePercentLow, "1%"), (OverlayMetric.GpuHotSpot, "hotspot"), (OverlayMetric.GpuFan, "fan"),
        (OverlayMetric.Fps, ""), (OverlayMetric.GpuTemp, ""), (OverlayMetric.GpuLoad, ""), (OverlayMetric.CpuTemp, ""), (OverlayMetric.CpuLoad, ""),
    };

    private async Task RenameAsync()
    {
        var layout = L;
        string? name = await _window.Prompt("Rename layout", "A name for this layout.", layout.Name, n => n == layout.Name ? null : ValidateName(n, layout));
        if (name is null || name == layout.Name) return;
        string old = layout.Name;
        layout.Name = name;
        O.ActiveLayout = name;
        Changed();
        BuildSettings();
    }

    private async Task DeleteAsync()
    {
        var layout = L;
        if (O.Layouts.Count <= 1 || !await _window.Confirm($"Delete “{layout.Name}”?", "This can't be undone.", "Delete", danger: true))
            return;
        O.Layouts.Remove(layout);
        O.ActiveLayout = O.Layouts[0].Name;
        Changed();
        BuildSettings();
    }

    private string? ValidateName(string name, OverlayLayout? self = null) =>
        name.Length == 0 ? "Give the layout a name."
        : O.Layouts.Any(l => l != self && string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) ? $"There's already a layout named “{name}”." : null;

    private static Border Card(UIElement child) => new Tile { Style = Ui.Res<Style>("Card"), Child = child, Margin = new Thickness(0, 0, 0, 16) };

    private static FrameworkElement Wrapped(FrameworkElement e)
    {
        e.Margin = new Thickness(0, 0, 6, 6);
        return e;
    }

    private static T Detached<T>(T element) where T : FrameworkElement
    {
        if (element.Parent is Panel panel)
            panel.Children.Remove(element);
        return element;
    }

    private static FrameworkElement Anchored(FrameworkElement e, string id)
    {
        Ui.Anchor(e, id);
        return e;
    }

    private static IEnumerable<(string Id, string Label)> SafeMonitors()
    {
        try { return OverlayController.AvailableMonitors().ToList(); }
        catch { return new[] { ("primary", "Primary monitor") }; }
    }

    private void RenderPreview()
    {
        var game = _app.Frames.GetMostActive();
        var stats = game?.Stats ?? new FrameStats(144, 141, 118, 6.9);
        var times = game is { } g ? _app.Frames.GetFrameTimes(g.ProcessId, 120) : FrameRateService.SampleFrameTimes(120);
        _previewNote.Text = game is not null
            ? $"Live values · FPS from {game.Value.Name}"
            : "Live temperatures and load. FPS and the frame-time graph show sample values until a game is running.";
        try
        {
            using var bitmap = _app.Overlay.RenderPreview(L, stats, times);
            _preview.Source = BitmapConvert.ToSource(bitmap);
            _overlaySize = (bitmap.Width, bitmap.Height);
        }
        catch
        {
            return;
        }
        PlacePreview();
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("compact-custom", () =>
        {
            L.Style = OverlayStyle.Compact;
            L.ShowGraph = true;
            L.Colors = OverlayColorPreset.Custom;
            L.Custom = new OverlayColors { Text = "#EEEEEE", Background = "#D8D8D8" };
            Changed();
            BuildSettings();
            return Task.CompletedTask;
        });
    }
}

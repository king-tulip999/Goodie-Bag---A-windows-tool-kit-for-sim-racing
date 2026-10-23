using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Plugins;
using GoodieBag.Plugins.BuiltIn;
using GoodieBag.Services;
using GoodieBag.SimRacing.Hud;
using GoodieBag.SimRacing.Hud.Editors;
using GoodieBag.Theme;
using GoodieBag.Views.HudSettings;

namespace GoodieBag.Views.Pages;

internal sealed class RaceHudWidgets
{
    private static readonly string[] SessionKinds = { "Practice", "Qualify", "Race", "TimeTrial" };
    private const string LayoutTab = "display", ContentTab = "content", VisibilityTab = "visibility", PositionTab = "position";

    private readonly AppServices _app;
    private readonly IHudEditorHost _section;
    private readonly ContentControl _list = new() { Focusable = false };
    private readonly HudPainter _painter = new() { Items = new(), Regions = new(), Overflow = new() };
    private Image? _preview;
    private Canvas? _previewMarks;
    private Canvas? _previewHighlight;
    private float _previewScale = 1;
    private double _previewPixels = 1;
    private readonly TranslateTransform _previewNudge = new();
    private (string Key, string Id, Point From)? _press;
    private Point? _click;
    private bool _dragging;
    private readonly Dictionary<string, string> _fitSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HudPainter _trialPainter = new() { Items = new(), Overflow = new() };
    private System.Drawing.Bitmap? _trialBitmap;
    private int _trialRun;
    private Border? _previewWell;
    private TextBlock? _previewNote;
    private FrameworkElement? _pickedCard;
    private string? _selected;
    private HudSettingContext? _context;
    private PresetRow? _presets;
    private readonly List<(HudSetting Setting, FrameworkElement Row)> _rows = new();
    private readonly List<SettingGroup> _groups = new();
    private string? _hoverSetting;
    private string? _reveal;

    public RaceHudWidgets(AppServices app, IHudEditorHost section, bool inPage = false)
    {
        _app = app;
        _section = section;
        _inPage = inPage;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        if (_inPage)
        {
            var nav = new Border { Padding = new Thickness(10, 12, 10, 12), Margin = new Thickness(14, 14, 0, 14), BorderThickness = new Thickness(1), Child = _nav, VerticalAlignment = VerticalAlignment.Top };
            nav.SetResourceReference(Border.BackgroundProperty, "Panel");
            nav.SetResourceReference(Border.BorderBrushProperty, "Line");
            grid.Children.Add(nav);
            var right = new Border { Padding = new Thickness(16, 14, 18, 18), Child = _list };
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            Sticky.Install(nav, grid, () => true, gap: 14);
            Root = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 12), Child = grid };
        }
        else
        {
            var nav = new Border { Padding = new Thickness(10, 12, 10, 12), Margin = new Thickness(14, 14, 0, 14), BorderThickness = new Thickness(1), Child = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = _nav, Focusable = false } };
            nav.SetResourceReference(Border.BackgroundProperty, "Panel");
            nav.SetResourceReference(Border.BorderBrushProperty, "Line");
            grid.Children.Add(nav);
            var pane = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = new Border { Padding = new Thickness(16, 14, 26, 18), Child = _list }, Focusable = false };
            Grid.SetColumn(pane, 1);
            grid.Children.Add(pane);
            Root = grid;
        }
        Fill();
    }

    private readonly bool _inPage;
    private readonly StackPanel _nav = new();
    private string _tab = LayoutTab;
    private bool _general;

    public FrameworkElement Root { get; }

    private RaceHudSettings S => _app.Config.SimRacing.Hud;
    private HudHost? Host => _app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin { Hud: { } hud } ? hud : null;

    private HudLayout? Layout => S.Layout(_section.ShownLayout) ?? S.Layouts.FirstOrDefault();

    private string NameOf(HudWidgetConfig w)
    {
        string name = HudCatalog.Find(w.Kind)?.Name ?? w.Kind;
        if (Layout?.Widgets is not { } all) return name;
        int index = 0, count = 0;
        foreach (var other in all)
        {
            if (other.Kind != w.Kind) continue;
            if (ReferenceEquals(other, w)) index = count;
            count++;
        }
        return count > 1 && index > 0 ? $"{name} {index + 1}" : name;
    }

    private string? _filledFor;

    public void FollowLayout()
    {
        if (Layout?.Name == _filledFor) return;
        _selected = null;
        Host?.Pick(null);
        Fill();
    }

    private void Changed()
    {
        _section.Changed();
        Host?.Pick(_selected);
    }

    public void SelectId(string? id)
    {
        if (id == _selected && !(_general && id is not null)) return;
        _selected = id;
        if (id is not null) _general = false;
        Fill();
        _list.Dispatcher.BeginInvoke(ScrollToPicked, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    internal void SetOption(string kind, string key, string? value)
    {
        if (Layout?.Widgets.FirstOrDefault(w => w.Kind == kind) is not { } w) return;
        if (value is null) w.Options.Remove(key); else w.Options[key] = value;
        _selected = null;
        Select(kind);
    }

    internal void ShowTab(string? kind, string tab = LayoutTab)
    {
        _general = kind is null;
        if (kind is not null) _selected = Layout?.Widgets.FirstOrDefault(w => w.Kind == kind)?.Id ?? _selected;
        _tab = tab;
        Fill();
    }

    internal void Reveal(string kind, string settingId)
    {
        _selected = Layout?.Widgets.FirstOrDefault(w => w.Kind == kind)?.Id ?? _selected;
        _general = false;
        if (Selected() is { } w && HudCatalog.Find(w.Kind) is { } def) Reveal(w, def, settingId);
    }

    internal void Point(string kind, string settingId)
    {
        var def = HudCatalog.Find(kind);
        ShowTab(kind, def?.EditorDef.GroupOf(settingId)?.Tab == HudTab.Content ? ContentTab : LayoutTab);
        _list.Dispatcher.BeginInvoke(() => Highlight(settingId), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    internal void OpenPicker(string kind, string key)
    {
        ShowTab(kind, ContentTab);
        _list.Dispatcher.BeginInvoke(() => { if (_context?.Pickers.TryGetValue(key, out var picker) == true) picker.ShowAdd(); }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    public void Select(string kind)
    {
        _selected = Layout?.Widgets.FirstOrDefault(w => w.Kind == kind)?.Id ?? _selected;
        _general = false;
        Fill();
        _list.Dispatcher.BeginInvoke(ScrollToPicked, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    internal void ScrollPageBy(double dy)
    {
        if (PageOf(_pickedCard) is { } page) page.ScrollToVerticalOffset(page.VerticalOffset + dy);
    }

    private static ScrollViewer? PageOf(DependencyObject? d)
    {
        while (d is not null and not ScrollViewer) d = VisualTreeHelper.GetParent(d);
        return d as ScrollViewer;
    }

    private void ScrollToPicked()
    {
        if (_pickedCard is not { IsLoaded: true } card) return;
        if (PageOf(card) is not { } page) { card.BringIntoView(); return; }
        double top = card.TransformToVisual(page).Transform(new Point(0, 0)).Y;
        page.ScrollToVerticalOffset(page.VerticalOffset + top - 24);
    }

    private System.Windows.Threading.DispatcherTimer? _previewTimer;
    private long _heartbeat;
    private System.Drawing.Bitmap? _previewBitmap;
    private System.Windows.Media.Imaging.WriteableBitmap? _previewTarget;

    private HudWidgetConfig? Selected() => _selected is null ? null : Layout?.Widgets.FirstOrDefault(w => w.Id == _selected);

    public void Tick()
    {
        _heartbeat = Environment.TickCount64;
        if (_preview is null || Selected() is not { } w) return;
        int hz = Math.Clamp(w.UpdateHz > 0 ? w.UpdateHz : HudCatalog.Find(w.Kind)?.DefaultHz ?? 10, 1, 60);
        var interval = TimeSpan.FromMilliseconds(1000.0 / hz);
        if (_previewTimer is null)
        {
            _previewTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render);
            _previewTimer.Tick += (_, _) => PreviewFrame();
        }
        if (_previewTimer.Interval != interval) _previewTimer.Interval = interval;
        if (!_previewTimer.IsEnabled) _previewTimer.Start();
    }

    private void PreviewFrame()
    {
        if (Environment.TickCount64 - _heartbeat > 800) { _previewTimer!.Stop(); return; }
        if (_dragging) return;
        if (_preview is null || Selected() is not { } w) return;
        _section.Preview.Advance(Host);
        DrawPreview(w);
    }

    private void Fill()
    {
        _fitSignatures.Clear();
        _preview = null;
        _previewWell = null;
        _previewHighlight = null;
        _pickedCard = null;
        _context = null;
        _presets = null;
        _rows.Clear();
        _groups.Clear();
        var layout = Layout;
        _filledFor = layout?.Name;
        _nav.Children.Clear();
        var widgets = layout?.Widgets ?? new List<HudWidgetConfig>();
        if (_inPage) _general = false;
        if (!_general && (_selected is null || widgets.All(x => x.Id != _selected))) _selected = widgets.FirstOrDefault()?.Id;
        if (_selected is null && !_inPage) _general = true;
        string group = "hudnav" + GetHashCode();
        RadioButton Item(string text, bool on, bool? dot, Action pick)
        {
            var row = new DockPanel { LastChildFill = true };
            if (dot is { } shown)
            {
                var mark = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = shown ? "On screen" : "Switched off" };
                mark.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, shown ? "Sage" : "LineStrong");
                DockPanel.SetDock(mark, Dock.Right);
                row.Children.Add(mark);
            }
            row.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }));
            var b = new RadioButton { Style = Ui.Res<Style>("NavItem"), GroupName = group, IsChecked = on, Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Height = 34 };
            b.Checked += (_, _) => pick();
            return b;
        }
        TextBlock Eyebrow(string text) => new() { Text = text, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(8, 6, 0, 6) };
        if (!_inPage)
        {
            _nav.Children.Add(Eyebrow("Overlay"));
            _nav.Children.Add(Item("Hud Settings", _general, null, () => { _general = true; Host?.Pick(null); Fill(); }));
        }
        _nav.Children.Add(Eyebrow(layout is null ? "Widgets" : $"Widgets · {layout.Name}"));
        foreach (var w in widgets)
        {
            var id = w.Id;
            _nav.Children.Add(Item(NameOf(w), !_general && id == _selected, w.Enabled, () => { _general = false; _selected = id; Host?.Pick(id); Fill(); }));
        }
        var add = Ui.LabeledButton("I.Plus", "Add widget", () => { }, "SoftButton");
        add.Click += (_, _) => AddMenu(add);
        add.HorizontalAlignment = HorizontalAlignment.Stretch;
        add.Margin = new Thickness(0, 10, 0, 0);
        _nav.Children.Add(add);

        var body = new StackPanel();
        if (_inPage && widgets.FirstOrDefault(x => x.Id == _selected) is null)
            body.Children.Add(new TextBlock { Text = "No widgets in this layout yet: add one.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
        else if (_general || widgets.FirstOrDefault(x => x.Id == _selected) is not { } picked)
        {
            body.Children.Add(TitleCard("Hud Settings", "For every widget in every layout.", null));
            body.Children.Add(RaceHudSection.HudSettings(_app, Changed, Fill, null));
        }
        else
        {
            var def = HudCatalog.Find(picked.Kind);
            var on = Ui.Switch(picked.Enabled, v => { picked.Enabled = v; Changed(); Fill(); });
            on.ToolTip = "Show this widget";
            on.VerticalAlignment = VerticalAlignment.Center;
            var remove = RemoveButton(picked);
            remove.Margin = new Thickness(8, 0, 0, 0);
            var title = TitleCard(NameOf(picked), def?.Description ?? "This widget was made by a newer version of Goodie Bag. It's kept, but not drawn.",
                new StackPanel { Orientation = Orientation.Horizontal, Children = { on, remove } });
            if (_inPage) _pickedCard = title;
            body.Children.Add(title);
            if (def is not null && _inPage) body.Children.Add(PreviewBeside(picked, def));
            else if (def is not null) body.Children.Add(Settings(picked, def));
        }
        _list.Content = body;
        if (_preview is not null && Selected() is { } shown2) DrawPreview(shown2);
        if (_reveal is { } reveal)
        {
            _reveal = null;
            _list.Dispatcher.BeginInvoke(() => Flash(reveal), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    private FrameworkElement Settings(HudWidgetConfig w, HudWidgetDef def)
    {
        var editor = def.EditorDef;
        if (_tab == ContentTab && !editor.Has(HudTab.Content)) _tab = LayoutTab;
        var stack = new StackPanel();
        if (editor.Presets.Count >= 2)
        {
            _presets = new PresetRow(editor.Presets, preset =>
            {
                Host?.RecordUndo();
                editor.Apply(def, w, preset);
                Changed();
                Fill();
            });
            _presets.Show(editor.PresetName(def, w));
            stack.Children.Add(_presets);
        }
        stack.Children.Add(Tabs(editor));
        stack.Children.Add(TabBody(w, def, editor));
        return stack;
    }

    private FrameworkElement PreviewBeside(HudWidgetConfig w, HudWidgetDef def)
    {
        var title = new TextBlock { Text = "Preview", Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center };
        var hint = new TextBlock { Text = "Click a part to find its setting; drag items to reorder", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        var previewColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        previewColumn.SetResourceReference(Panel.BackgroundProperty, "Surface");
        Panel.SetZIndex(previewColumn, 1);
        previewColumn.Children.Add(new DockPanel { Margin = new Thickness(0, 4, 0, 8), Children = { title, hint } });
        previewColumn.Children.Add(PreviewBlock(w));
        var tabs = Settings(w, def);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(previewColumn);
        body.Children.Add(tabs);
        bool? wide = null;
        bool Stick() => wide == true || (PageOf(body) is { } page && previewColumn.ActualHeight < page.ViewportHeight * 0.45);
        var follow = Sticky.Install(previewColumn, body, Stick);
        void Arrange(double width)
        {
            bool two = width >= TwoColumnsFrom;
            if (wide == two) return;
            wide = two;
            body.ColumnDefinitions[0].Width = two ? new GridLength(5, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
            body.ColumnDefinitions[1].Width = two ? new GridLength(6, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(tabs, two ? 1 : 0);
            Grid.SetRow(tabs, two ? 0 : 1);
            tabs.Margin = new Thickness(two ? 24 : 0, two ? 0 : 16, 0, 0);
            _previewMaxHeight = two ? 480 : 260;
            DrawPreview(w);
            follow();
        }
        Arrange(0);
        body.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        return body;
    }

    private const double TwoColumnsFrom = 940;

    private FrameworkElement PreviewBlock(HudWidgetConfig w)
    {
        _preview = new Image { Stretch = Stretch.None, RenderTransform = _previewNudge };
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.NearestNeighbor);
        _previewMarks = new Canvas { IsHitTestVisible = false };
        _previewHighlight = new Canvas { IsHitTestVisible = false };
        var previewHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Children = { _preview, _previewHighlight, _previewMarks } };
        HookPreviewDrag(previewHost, w);
        _previewWell = new Border { BorderThickness = new Thickness(1), CornerRadius = ThemeManager.Round(8), Padding = new Thickness(16), MinHeight = 120, Child = previewHost };
        _previewWell.SetResourceReference(Border.BackgroundProperty, "Bg");
        _previewWell.SetResourceReference(Border.BorderBrushProperty, "Line");
        _previewWell.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) DrawPreview(w); };
        _previewNote = new TextBlock { Style = Ui.Res<Style>("Caption"), Margin = new Thickness(2, 8, 0, 0) };
        var previewStack = new StackPanel();
        previewStack.Children.Add(_previewWell);
        previewStack.Children.Add(_previewNote);
        return previewStack;
    }

    private static string Anchor(HudAnchor a) => a switch
    {
        HudAnchor.TopLeft => "top left", HudAnchor.Top => "top", HudAnchor.TopRight => "top right", HudAnchor.Left => "left", HudAnchor.Center => "center",
        HudAnchor.Right => "right", HudAnchor.BottomLeft => "bottom left", HudAnchor.Bottom => "bottom", _ => "bottom right",
    };

    private const double ComboWidth = 240;

    private List<FrameworkElement> PlacementRows(HudWidgetConfig w, HudWidgetDef def)
    {
        var rows = new List<FrameworkElement>();
        var monitors = HudHost.MonitorChoices().Select(m => new Choice(m.Id, m.Label)).ToList();
        rows.Add(SettingRows.Row("Monitor", null, false, Ui.Combo(monitors, w.Monitor, c => { w.Monitor = c.Id!; Changed(); }, ComboWidth)));
        NumberBox Num(double value, double min, double max, Action<double> set)
        {
            var b = new NumberBox(value, "px", 64) { Minimum = min, Maximum = max, Step = 4 };
            b.ValueChanged += v => { Host?.RecordUndo(); set(v); w.Clamp(); Changed(); };
            return b;
        }
        rows.Add(SettingRows.Row("Size", null, false, Pair(Num(w.W, def.MinW, HudWidgetConfig.MaxSize, v => w.W = v), "Width", Num(w.H, def.MinH, HudWidgetConfig.MaxSize, v => w.H = v), "Height")));
        rows.Add(SettingRows.Row("Position", $"Measured from the {Anchor(w.Anchor)} of the monitor. Dragging it with Arrange on screen sets these for you.", false,
            Pair(Num(w.X, -HudWidgetConfig.MaxSize, HudWidgetConfig.MaxSize, v => w.X = v), "Across", Num(w.Y, -HudWidgetConfig.MaxSize, HudWidgetConfig.MaxSize, v => w.Y = v), "Down")));
        var home = Ui.LabeledButton("I.Target", "Bring back to the middle", () =>
        {
            Host?.RecordUndo();
            w.Monitor = "primary";
            w.Anchor = HudAnchor.Center;
            w.X = 0;
            w.Y = 0;
            w.Clamp();
            Changed();
            Fill();
        }, "GhostButton");
        home.ToolTip = "Puts this widget in the middle of the main monitor, e.g. when it's off screen or behind something";
        rows.Add(SettingRows.Row("", null, false, home));
        return rows;
    }

    private List<FrameworkElement> BehaviourRows(HudWidgetConfig w, HudWidgetDef def)
    {
        var rows = new List<FrameworkElement>();
        if (def.HideWhenEmpty is { } hideByDefault)
            rows.Add(SettingRows.Row("Only when needed", def.Kind == "alerts" ? "Hidden until an alert comes up." : "Hidden until a car of another class is near.", false,
                Ui.Switch(w.OnlyWhenNeeded ?? hideByDefault, v => { w.OnlyWhenNeeded = v; Changed(); })));
        rows.Add(SettingRows.Row("Only while driving", "Hidden in the garage and while you're out of the car.", false, Ui.Switch(w.OnlyOnTrack, v => { w.OnlyOnTrack = v; Changed(); })));
        rows.Add(SettingRows.Row("Locked", "Can't be dragged while arranging on screen.", false, Ui.Switch(w.Locked, v => { w.Locked = v; Changed(); })));
        rows.Add(SettingRows.Row("Hide in replays", "Off: it shows while you watch a replay too.", false, Ui.Switch(w.HideInReplay, v => { w.HideInReplay = v; Changed(); })));
        rows.Add(SettingRows.Row("Opacity", "The whole widget, text included.", false, Ui.SliderWithValue(20, 100, w.Opacity, "{0:0}%", v => { w.Opacity = (int)v; Changed(); }, 180, 5)));
        var background = Ui.SliderWithValue(0, 100, w.Background ?? S.Appearance.Background, "{0:0}%", v => { w.Background = (int)v; Changed(); if (_preview is not null) DrawPreview(w); }, 180, 5);
        background.IsEnabled = w.Background is not null;
        var own = Ui.Switch(w.Background is not null, on =>
        {
            w.Background = on ? S.Appearance.Background : null;
            background.IsEnabled = on;
            Changed();
            if (_preview is not null) DrawPreview(w);
        });
        own.ToolTip = "Off: the overlay's Background (Hud Settings)";
        var bgRow = new StackPanel { Orientation = Orientation.Horizontal };
        own.Margin = new Thickness(0, 0, 12, 0);
        own.VerticalAlignment = VerticalAlignment.Center;
        bgRow.Children.Add(own);
        bgRow.Children.Add(background);
        rows.Add(SettingRows.Row("Own background", "The panel behind the text only; switched off it follows the overlay's Background.", false, bgRow));
        var rates = new[] { 0, 1, 2, 5, 10, 15, 20, 30, 60 }.Select(r => new Choice(r.ToString(), r == 0 ? $"Default ({def.DefaultHz} per second)" : $"{r} per second"));
        rows.Add(SettingRows.Row("Redraw", null, false, Ui.Combo(rates, w.UpdateHz.ToString(), c => { w.UpdateHz = int.Parse(c.Id!); Changed(); }, ComboWidth)));
        var sessions = SettingRows.Toggles(SessionKinds.Select(k => (k, SessionLabel(k), (string?)null)).ToList(),
            kind => w.Sessions.Contains(kind, StringComparer.OrdinalIgnoreCase),
            (kind, on) =>
            {
                w.Sessions.RemoveAll(s => string.Equals(s, kind, StringComparison.OrdinalIgnoreCase));
                if (on) w.Sessions.Add(kind);
                Changed();
            });
        rows.Add(SettingRows.Row("Only in", "None ticked = every session.", true, sessions));
        return rows;
    }

    private FrameworkElement ActionsRow(HudWidgetConfig w)
    {
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var dup = Ui.LabeledButton("I.Copy", "Duplicate", () =>
        {
            var copy = w.Copy();
            copy.Id = HudWidgetConfig.NewId(w.Kind);
            copy.Y += 24;
            Layout!.Widgets.Add(copy);
            _selected = copy.Id;
            Changed();
            Fill();
        });
        dup.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(dup);
        var reset = Ui.LabeledButton("I.Undo", "Default options", () => { w.Options.Clear(); Changed(); Fill(); });
        reset.ToolTip = "Puts this widget's display options back as they were when it was added (place and size stay)";
        reset.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(reset);
        return actions;
    }

    private static FrameworkElement TitleCard(string title, string text, FrameworkElement? toggle)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel();
        words.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = title, Style = Ui.Res<Style>("H2"), TextTrimming = TextTrimming.CharacterEllipsis }));
        words.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 16, 0) });
        grid.Children.Add(words);
        if (toggle is not null)
        {
            toggle.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);
        }
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 10), Child = grid };
    }

    private FrameworkElement Tabs(HudEditorDef editor)
    {
        var names = new List<(string Id, string Title)> { (LayoutTab, "Layout") };
        if (editor.Has(HudTab.Content)) names.Add((ContentTab, editor.ContentTitle));
        names.Add((VisibilityTab, "When shown"));
        names.Add((PositionTab, "Placement"));
        var row = new WrapPanel();
        foreach (var (id, title) in names)
        {
            bool on = id == _tab;
            var text = new TextBlock { Text = title, FontSize = 14 };
            text.SetResourceReference(TextBlock.ForegroundProperty, on ? "Heading" : "TextMuted");
            if (on) text.FontWeight = FontWeights.SemiBold;
            var underline = new Border { Height = 2, Margin = new Thickness(0, 8, 0, 0) };
            if (on) underline.SetResourceReference(Border.BackgroundProperty, "AccentText");
            var stack = new StackPanel { Children = { text, underline } };
            var tabId = id;
            var b = new Button { Style = Ui.Res<Style>("QuietButton"), Content = stack, Padding = new Thickness(14, 8, 14, 0), MinHeight = 0, Height = double.NaN };
            System.Windows.Automation.AutomationProperties.SetName(b, title + " tab");
            b.Click += (_, _) => { if (_tab == tabId) return; _tab = tabId; Fill(); };
            row.Children.Add(b);
        }
        var line = new Border { Height = 1 };
        line.SetResourceReference(Border.BackgroundProperty, "Line");
        return new StackPanel { Margin = new Thickness(0, 2, 0, 12), Children = { row, line } };
    }

    private FrameworkElement TabBody(HudWidgetConfig w, HudWidgetDef def, HudEditorDef editor)
    {
        var stack = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        void Heading(string text) => stack.Children.Add(new Border { Margin = new Thickness(0, 6, 0, 8), Child = new TextBlock { Text = text, Style = Ui.Res<Style>("H3") } });
        switch (_tab)
        {
            case VisibilityTab:
                Heading("When shown");
                foreach (var row in BehaviourRows(w, def)) stack.Children.Add(row);
                return stack;
            case PositionTab:
                Heading("Position and size");
                foreach (var row in PlacementRows(w, def)) stack.Children.Add(row);
                var divider = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 0) };
                divider.SetResourceReference(Border.BackgroundProperty, "Line");
                stack.Children.Add(divider);
                stack.Children.Add(ActionsRow(w));
                return stack;
        }
        var tab = _tab == ContentTab ? HudTab.Content : HudTab.Layout;
        _context = new HudSettingContext
        {
            Def = def, W = w, Frame = _section.Preview.Race.Frame, KnownClasses = S.KnownClasses,
            Set = values => SetValues(w, def, values), Rebuild = Fill,
        };
        foreach (var group in editor.Groups.Where(g => g.Tab == tab))
        {
            string fold = HudEditorDef.GroupFoldId(def.Kind, group.Id);
            var box = new SettingGroup(group.Title, group.Open != S.EditorSectionsToggled.Contains(fold, StringComparer.OrdinalIgnoreCase), open =>
            {
                S.EditorSectionsToggled.RemoveAll(x => string.Equals(x, fold, StringComparison.OrdinalIgnoreCase));
                if (open != group.Open) S.EditorSectionsToggled.Add(fold);
                _app.Save();
            });
            foreach (var setting in group.Settings)
            {
                var row = Linked(setting, SettingRows.Build(setting, _context, group.Title));
                _rows.Add((setting, row));
                box.Add(row);
            }
            _groups.Add(box);
            stack.Children.Add(box);
        }
        RefreshVisibility();
        if (tab == HudTab.Layout && def.Limits is { } limits)
            stack.Children.Add(new TextBlock { Text = limits, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        return stack;
    }

    private FrameworkElement Linked(HudSetting setting, FrameworkElement row)
    {
        var box = new Border { Child = row, Tag = setting.Id, Background = Brushes.Transparent, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(-6, 0, -6, 0) };
        box.MouseEnter += (_, _) => Highlight(setting.Id);
        box.MouseLeave += (_, _) => { if (!box.IsKeyboardFocusWithin && _hoverSetting == setting.Id) Highlight(null); };
        box.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if (e.NewValue is true) Highlight(setting.Id);
            else if (_hoverSetting == setting.Id && !box.IsMouseOver) Highlight(null);
        };
        return box;
    }

    private void RefreshVisibility()
    {
        if (_context is null) return;
        var o = _context.Options;
        foreach (var (setting, row) in _rows) row.Visibility = setting.Visible(o) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var g in _groups) g.Visibility = g.HasVisibleRows ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetValues(HudWidgetConfig w, HudWidgetDef def, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var (key, value) in values)
        {
            if (def.Option(key) is not { } o) continue;
            if (o == HudFlags.BannerOption && value is not null)
            {
                var (l0, t0, r0, b0) = HudFlags.Room(new HudOptions(def, w).Choice(o.Key));
                var (l1, t1, r1, b1) = HudFlags.Room(value);
                w.W = Math.Max(1, w.W + l1 + r1 - l0 - r0);
                w.H = Math.Max(1, w.H + t1 + b1 - t0 - b0);
            }
            if (value is null || value == o.Default) w.Options.Remove(key); else w.Options[key] = value;
        }
        Changed();
        if (_preview is not null) DrawPreview(w);
        RefreshVisibility();
        _presets?.Show(def.EditorDef.PresetName(def, w));
    }

    private void Reveal(HudWidgetConfig w, HudWidgetDef def, string settingId)
    {
        var editor = def.EditorDef;
        if (editor.GroupOf(settingId) is not { } group) return;
        _tab = group.Tab == HudTab.Content ? ContentTab : LayoutTab;
        string fold = HudEditorDef.GroupFoldId(def.Kind, group.Id);
        bool open = group.Open != S.EditorSectionsToggled.Contains(fold, StringComparer.OrdinalIgnoreCase);
        if (!open)
        {
            S.EditorSectionsToggled.RemoveAll(x => string.Equals(x, fold, StringComparison.OrdinalIgnoreCase));
            if (!group.Open) S.EditorSectionsToggled.Add(fold);
            _app.Save();
        }
        _reveal = settingId;
        Fill();
    }

    private void Flash(string settingId)
    {
        var row = _rows.FirstOrDefault(r => string.Equals(r.Setting.Id, settingId, StringComparison.OrdinalIgnoreCase)).Row;
        if (row is not Border { IsLoaded: true, IsVisible: true } box) return;
        if (PageOf(box) is { } page)
        {
            double top = box.TransformToVisual(page).Transform(new Point(0, 0)).Y;
            double stuck = _previewWell is { IsLoaded: true } well && well.ActualWidth > box.ActualWidth * 1.5 ? 0 : (_previewWell?.ActualHeight ?? 0) + 60;
            if (top < stuck + 12 || top + box.ActualHeight > page.ViewportHeight - 12)
                page.ScrollToVerticalOffset(page.VerticalOffset + top - stuck - Math.Max(24, (page.ViewportHeight - stuck - box.ActualHeight) / 3));
        }
        else box.BringIntoView();
        var brush = (Ui.Res<Brush>("AccentSoft") as SolidColorBrush)?.Clone() ?? new SolidColorBrush(Colors.Transparent);
        box.Background = brush;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1600)) { BeginTime = TimeSpan.FromMilliseconds(400) };
        fade.Completed += (_, _) => box.Background = Brushes.Transparent;
        brush.BeginAnimation(Brush.OpacityProperty, fade);
        if (box.Child is FrameworkElement child) child.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private static string SessionLabel(string kind) => kind switch { "Qualify" => "Qualifying", "TimeTrial" => "Time trial", _ => kind };

    private static FrameworkElement Pair(FrameworkElement a, string labelA, FrameworkElement b, string labelB)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (control, label) in new[] { (a, labelA), (b, labelB) })
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 16, 2) };
            cell.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(2, 0, 0, 2) });
            cell.Children.Add(control);
            row.Children.Add(cell);
        }
        return row;
    }

    private Button RemoveButton(HudWidgetConfig w)
    {
        var remove = Ui.IconButton("I.Trash", "Remove this widget from the layout", async () =>
        {
            if (!await _section.Confirm($"Remove this {HudCatalog.Find(w.Kind)?.Name ?? w.Kind}?", $"It's taken out of the {Layout?.Name} layout with its settings.", "Remove", danger: true)) return;
            Layout?.Widgets.Remove(w);
            if (_selected == w.Id) _selected = null;
            Changed();
            Fill();
        }, "QuietIconButton");
        remove.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(remove, "Remove " + (HudCatalog.Find(w.Kind)?.Name ?? w.Kind));
        return remove;
    }

    private const double MaxPreviewScale = 1.5;

    private double _previewMaxHeight = 480;

    private void DrawPreview(HudWidgetConfig w)
    {
        if (_preview is null) return;
        if (Host is not { } host) { _previewNote!.Text = "The preview needs the Sim Racing Tools plugin on."; return; }
        var race = _section.Preview.Race;
        try
        {
            double room = _previewWell is { ActualWidth: > 40 } well ? well.ActualWidth - well.Padding.Left - well.Padding.Right - 2 : 560;
            float scale = (float)Math.Clamp(Math.Min(room / Math.Max(1, w.W), _previewMaxHeight / Math.Max(1, w.H)), 0.2, MaxPreviewScale);
            AlignPreview();
            double k = _previewPixels;
            host.RenderPreview(w, race, _painter, (float)(scale * k), ref _previewBitmap);
            var bitmap = _previewBitmap!;
            _previewScale = scale;
            if (_previewTarget is null || _previewTarget.PixelWidth != bitmap.Width || _previewTarget.PixelHeight != bitmap.Height || Math.Abs(_previewTarget.DpiX - 96 * k) > 0.01)
                _previewTarget = new System.Windows.Media.Imaging.WriteableBitmap(bitmap.Width, bitmap.Height, 96 * k, 96 * k, PixelFormats.Pbgra32, null);
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try { _previewTarget.WritePixels(new Int32Rect(0, 0, bitmap.Width, bitmap.Height), data.Scan0, data.Stride * bitmap.Height, data.Stride); }
            finally { bitmap.UnlockBits(data); }
            if (_preview.Source != _previewTarget) _preview.Source = _previewTarget;
            _preview.Width = bitmap.Width / k;
            _preview.Height = bitmap.Height / k;
            string size = Math.Abs(scale - 1) < 0.01 ? "actual size" : $"{scale * 100:0}% of its size";
            string note = (race.Frame.Simulated ? "Sample data (no live session)" : "Live from iRacing") + $" · {size}";
            if (!TextCase.Plain(_previewNote!.Text).Equals(note, StringComparison.OrdinalIgnoreCase)) _previewNote.Text = note;
            MarkOverflow(w, scale);
            DrawHighlight();
        }
        catch (Exception ex)
        {
            _previewNote!.Text = "Couldn't draw the preview: " + ex.Message;
            App.LogError(ex);
        }
    }

    private void AlignPreview()
    {
        if (_preview is null || PresentationSource.FromVisual(_preview) is not { RootVisual: { } root, CompositionTarget: { } target }) return;
        var m = Matrix.Identity;
        DependencyObject? at = _preview;
        while (at is Visual v && !ReferenceEquals(v, root))
        {
            if (VisualTreeHelper.GetTransform(v) is { } t) m.Append(t.Value);
            var offset = VisualTreeHelper.GetOffset(v);
            m.Translate(offset.X, offset.Y);
            at = VisualTreeHelper.GetParent(v);
        }
        if (!ReferenceEquals(at, root)) return;
        if (!ScreenshotRunner.Active) m.Append(target.TransformToDevice);
        double k = m.M11;
        if (k < 0.1 || Math.Abs(m.M22 - k) > 0.001 || m.M12 != 0 || m.M21 != 0) return;
        _previewPixels = k;
        var origin = m.Transform(new Point(0, 0));
        _previewNudge.X = Math.Clamp(_previewNudge.X - (origin.X - Math.Round(origin.X)) / k, -1, 1);
        _previewNudge.Y = Math.Clamp(_previewNudge.Y - (origin.Y - Math.Round(origin.Y)) / k, -1, 1);
    }

    private void MarkOverflow(HudWidgetConfig w, float scale)
    {
        if (_context is not { Pickers.Count: > 0 } context || HudCatalog.Find(w.Kind) is not { } def) return;
        var options = new HudOptions(def, w);
        var trials = new List<(string Key, string Id)>();
        foreach (var (key, picker) in context.Pickers)
        {
            var hidden = (_painter.Overflow ?? new()).Where(x => x.Key == key).Select(x => x.Id).ToHashSet();
            var picked = options.Columns(key);
            string signature = string.Join(",", hidden.Order()) + "|" + string.Join(",", picked) + $"|{w.W:0}x{w.H:0}|{scale:0.00}";
            if (_fitSignatures.GetValueOrDefault(key) == signature) continue;
            _fitSignatures[key] = signature;
            _trialRun++;
            var candidates = def.Option(key)?.Choices.Select(c => c.Id).Where(id => !picked.Contains(id)).ToList() ?? new();
            if (hidden.Count > 0) { picker.Mark(key, hidden, candidates.ToHashSet()); continue; }
            picker.Mark(key, hidden, new HashSet<string>());
            trials.AddRange(candidates.Select(id => (key, id)));
        }
        if (trials.Count > 0) TryCandidates(w, scale, trials);
    }

    private void TryCandidates(HudWidgetConfig w, float scale, List<(string Key, string Id)> trials)
    {
        int run = ++_trialRun;
        var blocked = trials.Select(t => t.Key).Distinct().ToDictionary(k => k, _ => new HashSet<string>());
        int next = 0;
        void Step()
        {
            if (run != _trialRun || _preview is null || Host is not { } host || HudCatalog.Find(w.Kind) is not { } def) return;
            if (next >= trials.Count)
            {
                var none = new HashSet<string>();
                foreach (var (key, ids) in blocked)
                    if (_context?.Pickers.TryGetValue(key, out var picker) == true) picker.Mark(key, none, ids);
                return;
            }
            var (k, id) = trials[next++];
            var copy = w.Copy();
            copy.Id = "fit-" + w.Id;
            copy.Options[k] = string.Join(",", new HudOptions(def, w).Columns(k).Append(id));
            try
            {
                host.RenderPreview(copy, _section.Preview.Race, _trialPainter, scale, ref _trialBitmap);
                if (_trialPainter.Overflow!.Any(x => x.Key == k)) blocked[k].Add(id);
            }
            catch (Exception ex) { App.LogError(ex); }
            _list.Dispatcher.BeginInvoke(Step, System.Windows.Threading.DispatcherPriority.Background);
        }
        _list.Dispatcher.BeginInvoke(Step, System.Windows.Threading.DispatcherPriority.Background);
    }

    private List<(string Id, System.Drawing.RectangleF Box)> Drawn(string key) =>
        (_painter.Items ?? new()).Where(i => i.Key == key).Select(i => (i.Id, i.Box)).ToList();

    private System.Drawing.PointF PainterPoint(Point at) => new((float)(at.X / _previewScale), (float)(at.Y / _previewScale));

    private (string Key, string Id)? ItemAt(Point at)
    {
        if (_painter.Items is not { } items) return null;
        var p = PainterPoint(at);
        for (int i = items.Count - 1; i >= 0; i--)
            if (items[i].Box.Contains(p) && Drawn(items[i].Key).Count > 1) return (items[i].Key, items[i].Id);
        return null;
    }

    private string? SettingAt(HudEditorDef editor, Point at)
    {
        var p = PainterPoint(at);
        string? best = null;
        float bestArea = float.MaxValue;
        void Consider(string name, System.Drawing.RectangleF box)
        {
            float area = box.Width * box.Height;
            if (!box.Contains(p) || area >= bestArea || editor.SettingForRegion(name) is not { } id) return;
            best = id;
            bestArea = area;
        }
        foreach (var (id, box) in _painter.Regions ?? new()) Consider(id, box);
        foreach (var (key, _, box) in _painter.Items ?? new()) Consider(key, box);
        return best;
    }

    private void Highlight(string? settingId)
    {
        if (_hoverSetting == settingId) return;
        _hoverSetting = settingId;
        DrawHighlight();
    }

    private void DrawHighlight()
    {
        if (_previewHighlight is null) return;
        _previewHighlight.Children.Clear();
        if (_hoverSetting is not { } id || _dragging || Selected() is not { } w || HudCatalog.Find(w.Kind) is not { } def) return;
        var names = def.EditorDef.RegionsOf(id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var boxes = (_painter.Regions ?? new()).Where(r => names.Contains(r.Id)).Select(r => r.Box).ToList();
        foreach (var group in (_painter.Items ?? new()).Where(i => names.Contains(i.Key)).GroupBy(i => i.Key))
            boxes.Add(group.Select(i => i.Box).Aggregate(System.Drawing.RectangleF.Union));
        double s = _previewScale;
        foreach (var box in boxes)
        {
            var mark = new Border { Width = Math.Max(4, box.Width * s + 4), Height = Math.Max(4, box.Height * s + 4), BorderThickness = new Thickness(2) };
            mark.SetResourceReference(Border.BorderBrushProperty, "AccentText");
            var fill = (Ui.Res<Brush>("AccentSoft") as SolidColorBrush)?.Clone();
            if (fill is not null) { fill.Opacity = 0.35; mark.Background = fill; }
            Canvas.SetLeft(mark, box.X * s - 2);
            Canvas.SetTop(mark, box.Y * s - 2);
            _previewHighlight.Children.Add(mark);
        }
    }

    private string? _dragBefore;
    private System.Windows.Vector _grab;
    private ImageBrush? _liftedLook;

    private void HookPreviewDrag(FrameworkElement host, HudWidgetConfig w)
    {
        var editor = HudCatalog.Find(w.Kind)?.EditorDef;
        host.MouseMove += (_, e) =>
        {
            var at = e.GetPosition(host);
            if (_press is not { } press)
            {
                if (_click is not null || editor is null) return;
                var hover = SettingAt(editor, at);
                host.Cursor = hover is null ? null : ItemAt(at) is not null ? Cursors.SizeAll : Cursors.Hand;
                Highlight(hover);
                return;
            }
            if (!_dragging && (at - press.From).Length < 4) return;
            if (!_dragging)
            {
                _liftedLook = Lift(press.Key, press.Id);
                Highlight(null);
            }
            _dragging = true;
            Reorder(w, press.Key, press.Id, at);
            ShowDrag(press.Key, press.Id, at);
        };
        host.MouseLeave += (_, _) => { if (_press is null) Highlight(null); };
        host.MouseLeftButtonDown += (_, e) =>
        {
            var at = e.GetPosition(host);
            if (ItemAt(at) is not { } item)
            {
                _click = at;
                e.Handled = true;
                return;
            }
            _press = (item.Key, item.Id, at);
            _dragBefore = w.Options.GetValueOrDefault(item.Key);
            var box = Drawn(item.Key).FirstOrDefault(d => d.Id == item.Id).Box;
            _grab = new System.Windows.Vector(at.X - box.X * _previewScale, at.Y - box.Y * _previewScale);
            host.CaptureMouse();
            e.Handled = true;
        };
        host.MouseLeftButtonUp += (_, e) =>
        {
            var at = e.GetPosition(host);
            if (_click is { } click)
            {
                _click = null;
                if ((at - click).Length < 4 && editor is not null && SettingAt(editor, at) is { } id && HudCatalog.Find(w.Kind) is { } def) Reveal(w, def, id);
                return;
            }
            if (_press is not { } press) return;
            bool dragged = _dragging;
            _press = null;
            EndDrag(host);
            if (dragged) Commit(w, press.Key);
            else if (editor?.SettingForRegion(press.Key) is { } id && HudCatalog.Find(w.Kind) is { } def) Reveal(w, def, id);
        };
        host.LostMouseCapture += (_, _) =>
        {
            if (_press is not { } press) return;
            bool dragged = _dragging;
            EndDrag(host);
            if (!dragged) return;
            if (_dragBefore is null) w.Options.Remove(press.Key); else w.Options[press.Key] = _dragBefore;
            DrawPreview(w);
        };
    }

    private void EndDrag(FrameworkElement host)
    {
        _press = null;
        _dragging = false;
        _previewMarks?.Children.Clear();
        if (host.IsMouseCaptured) host.ReleaseMouseCapture();
    }

    private void Reorder(HudWidgetConfig w, string key, string id, Point at)
    {
        var def = HudCatalog.Find(w.Kind);
        if (def?.Option(key) is not { Kind: HudOptionKind.Columns } option) return;
        var drawn = Drawn(key);
        if (drawn.Count == 0) return;
        var box = drawn.FirstOrDefault(d => d.Id == id).Box;
        var centre = new System.Drawing.PointF((float)((at.X - _grab.X) / _previewScale + box.Width / 2), (float)((at.Y - _grab.Y) / _previewScale + box.Height / 2));
        var before = HudItemDrag.Before(drawn, id, centre);
        string? last = drawn.Where(d => d.Id != id).Select(d => d.Id).LastOrDefault();
        var order = new HudOptions(def, w).Columns(key);
        string value = string.Join(",", HudItemDrag.Move(order, id, before, last));
        if (value == string.Join(",", order)) return;
        if (value == option.Default) w.Options.Remove(key); else w.Options[key] = value;
        DrawPreview(w);
    }

    private void ShowDrag(string key, string id, Point at)
    {
        if (_previewMarks is null) return;
        _previewMarks.Children.Clear();
        double s = _previewScale;
        var (_, box) = Drawn(key).FirstOrDefault(d => d.Id == id);
        if (box.Width <= 0) return;
        var slot = new Border { Width = box.Width * s, Height = box.Height * s, BorderThickness = new Thickness(1.5), CornerRadius = ThemeManager.Round(4) };
        slot.SetResourceReference(Border.BorderBrushProperty, "AccentText");
        slot.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        slot.Opacity = 0.45;
        Canvas.SetLeft(slot, box.X * s);
        Canvas.SetTop(slot, box.Y * s);
        _previewMarks.Children.Add(slot);
        var lifted = new Border { Width = box.Width * s, Height = box.Height * s, BorderThickness = new Thickness(2), CornerRadius = ThemeManager.Round(4) };
        lifted.SetResourceReference(Border.BorderBrushProperty, "Accent");
        lifted.Background = _liftedLook;
        Canvas.SetLeft(lifted, at.X - _grab.X);
        Canvas.SetTop(lifted, at.Y - _grab.Y);
        _previewMarks.Children.Add(lifted);
    }

    private ImageBrush? Lift(string key, string id)
    {
        if (_preview?.Source is not System.Windows.Media.Imaging.BitmapSource bitmap) return null;
        double s = _previewScale * _previewPixels;
        var (_, box) = Drawn(key).FirstOrDefault(d => d.Id == id);
        var crop = new Int32Rect((int)Math.Max(0, box.X * s), (int)Math.Max(0, box.Y * s), 0, 0);
        crop.Width = (int)Math.Min(bitmap.PixelWidth - crop.X, Math.Ceiling(box.Width * s));
        crop.Height = (int)Math.Min(bitmap.PixelHeight - crop.Y, Math.Ceiling(box.Height * s));
        if (crop.Width <= 0 || crop.Height <= 0) return null;
        var copy = new System.Windows.Media.Imaging.CroppedBitmap(bitmap, crop);
        var frozen = new System.Windows.Media.Imaging.WriteableBitmap(copy);
        frozen.Freeze();
        var brush = new ImageBrush(frozen) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
        brush.Freeze();
        return brush;
    }

    private void Commit(HudWidgetConfig w, string key)
    {
        string? now = w.Options.GetValueOrDefault(key);
        if (now == _dragBefore) return;
        Changed();
        if (_context?.Pickers.TryGetValue(key, out var picker) == true && HudCatalog.Find(w.Kind) is { } def
            && def.EditorDef.SettingForKey(key) is { } setting)
            picker.Reload(setting.Keys.Select(k => new HudOptions(def, w).Value(k)).ToList());
        _presets?.Show(HudCatalog.Find(w.Kind)?.EditorDef.PresetName(HudCatalog.Find(w.Kind)!, w));
        DrawPreview(w);
    }

    public void DrawEveryKind()
    {
        if (Host is not { } host) return;
        var look = S.Appearance;
        var colors = look.Colors;
        string? root = ScreenshotRunner.Folder is { } folder ? System.IO.Path.Combine(folder, "hud-widgets") : null;
        try
        {
            foreach (var pick in Enum.GetValues<HudColors>())
            {
                look.Colors = pick;
                string? dir = root is null ? null : System.IO.Path.Combine(root, pick.ToString());
                if (dir is not null) System.IO.Directory.CreateDirectory(dir);
                foreach (var def in HudCatalog.All)
                {
                    var config = new HudWidgetConfig { Id = "check-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H };
                    using (var a = host.RenderPreview(config, _section.Preview.Race, _painter, 1))
                        if (dir is not null) a.Save(System.IO.Path.Combine(dir, def.Kind + ".png"));
                    config.W = def.MinW;
                    config.H = def.MinH;
                    using (var b = host.RenderPreview(config, _section.Preview.Race, _painter, 1))
                        if (dir is not null) b.Save(System.IO.Path.Combine(dir, def.Kind + "-min.png"));
                }
            }
        }
        finally
        {
            look.Colors = colors;
        }
    }

    private void AddMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var def in HudCatalog.All.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new MenuItem { Header = def.Name, ToolTip = def.Description };
            item.Click += (_, _) => Add(def);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void Add(HudWidgetDef def)
    {
        var layout = Layout;
        if (layout is null) return;
        var w = new HudWidgetConfig { Id = HudWidgetConfig.NewId(def.Kind), Kind = def.Kind, W = def.W, H = def.H, Anchor = HudAnchor.Center };
        var monitors = HudHost.ReadMonitors();
        var monitor = HudGeometry.MonitorFor("primary", monitors);
        var others = layout.Widgets.Where(x => x.Enabled && HudGeometry.MonitorFor(x.Monitor, monitors) == monitor).Select(x => HudGeometry.Place(x, monitor)).ToList();
        var rect = HudGeometry.Resolve(HudGeometry.Place(w, monitor), others, monitor.Bounds);
        HudGeometry.Store(w, rect, monitor);
        layout.Widgets.Add(w);
        _selected = w.Id;
        _general = false;
        Changed();
        Fill();
    }
}

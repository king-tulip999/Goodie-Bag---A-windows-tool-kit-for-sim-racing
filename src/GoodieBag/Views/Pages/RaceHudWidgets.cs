using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Plugins;
using GoodieBag.Plugins.BuiltIn;
using GoodieBag.Services;
using GoodieBag.SimRacing.Hud;
using GoodieBag.SimRacing.Race;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class RaceHudWidgets
{
    private static readonly string[] SessionKinds = { "Practice", "Qualify", "Race", "TimeTrial" };

    private readonly AppServices _app;
    private readonly IHudEditorHost _section;
    private readonly ContentControl _list = new() { Focusable = false };
    private readonly HudPainter _painter = new() { Items = new(), Overflow = new() };
    private Image? _preview;
    private Canvas? _previewMarks;
    private float _previewScale = 1;
    private double _previewPixels = 1;
    private readonly TranslateTransform _previewNudge = new();
    private (string Key, string Id, System.Windows.Point From)? _press;
    private bool _dragging;
    private readonly Dictionary<string, Action<string>> _columnPickers = new();
    private readonly Dictionary<string, (Action<IReadOnlySet<string>, IReadOnlySet<string>> Mark, string Last)> _fitMarks = new();
    private readonly HudPainter _trialPainter = new() { Items = new(), Overflow = new() };
    private System.Drawing.Bitmap? _trialBitmap;
    private int _trialRun;
    private Border? _previewWell;
    private TextBlock? _previewNote;
    private FrameworkElement? _pickedCard;
    private string? _selected;

    public RaceHudWidgets(AppServices app, IHudEditorHost section, bool sidebar = false, bool inPage = false)
    {
        _app = app;
        _section = section;
        _sidebar = sidebar;
        _inPage = sidebar && inPage;
        if (_inPage)
        {
            _head = new Border();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var nav = new Border { Padding = new Thickness(10, 12, 10, 12), Margin = new Thickness(14, 14, 0, 14), BorderThickness = new Thickness(1), Child = _nav, VerticalAlignment = VerticalAlignment.Top };
            nav.SetResourceReference(Border.BackgroundProperty, "Panel");
            nav.SetResourceReference(Border.BorderBrushProperty, "Line");
            grid.Children.Add(nav);
            var right = new Border { Padding = new Thickness(16, 14, 18, 18), Child = _list };
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            Sticky.Install(nav, grid, () => true, gap: 14);
            Root = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 12), Child = grid };
            Fill();
            return;
        }
        if (sidebar)
        {
            _head = new Border();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var nav = new Border { Padding = new Thickness(10, 12, 10, 12), Margin = new Thickness(14, 14, 0, 14), BorderThickness = new Thickness(1), Child = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = _nav, Focusable = false } };
            nav.SetResourceReference(Border.BackgroundProperty, "Panel");
            nav.SetResourceReference(Border.BorderBrushProperty, "Line");
            grid.Children.Add(nav);
            _pane = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = new Border { Padding = new Thickness(16, 14, 26, 18), Child = _list }, Focusable = false };
            Grid.SetColumn(_pane, 1);
            grid.Children.Add(_pane);
            Root = grid;
            Fill();
            return;
        }
        var root = new StackPanel();
        var add = Ui.LabeledButton("I.Plus", "Add widget", () => { }, "PrimaryButton");
        add.Click += (_, _) => AddMenu(add);
        _head = Ui.SectionHeader("Widgets", "", add);
        root.Children.Add(_head);
        root.Children.Add(_list);
        Root = new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 12), Child = root };
        Fill();
    }

    private readonly FrameworkElement _head;
    private readonly bool _sidebar;
    private readonly bool _inPage;
    private readonly StackPanel _nav = new();
    private readonly ScrollViewer? _pane;
    private string _tab = "display";
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

    internal void ShowTab(string? kind, string tab = "display")
    {
        _general = kind is null;
        if (kind is not null) _selected = Layout?.Widgets.FirstOrDefault(w => w.Kind == kind)?.Id ?? _selected;
        _tab = tab;
        Fill();
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
        DependencyObject? d = _pickedCard;
        while (d is not null and not ScrollViewer) d = VisualTreeHelper.GetParent(d);
        if (d is ScrollViewer page) page.ScrollToVerticalOffset(page.VerticalOffset + dy);
    }

    private void ScrollToPicked()
    {
        if (_pickedCard is not { IsLoaded: true } card) return;
        DependencyObject? d = card;
        while (d is not null and not ScrollViewer) d = VisualTreeHelper.GetParent(d);
        if (d is not ScrollViewer page) { card.BringIntoView(); return; }
        double top = card.TransformToVisual(page).Transform(new Point(0, 0)).Y;
        page.ScrollToVerticalOffset(page.VerticalOffset + top - 24);
    }

    private System.Windows.Threading.DispatcherTimer? _previewTimer;
    private long _heartbeat;
    private System.Drawing.Bitmap? _previewBitmap;
    private System.Windows.Media.Imaging.WriteableBitmap? _previewTarget;

    public void Tick()
    {
        _heartbeat = Environment.TickCount64;
        if (_preview is null || _selected is null || Layout?.Widgets.FirstOrDefault(w => w.Id == _selected) is not { } w) return;
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
        if (_preview is null || _selected is null || Layout?.Widgets.FirstOrDefault(w => w.Id == _selected) is not { } w) return;
        _section.Preview.Advance(Host);
        DrawPreview(w);
    }

    private void Fill()
    {
        _summaries.Clear();
        _columnPickers.Clear();
        _fitMarks.Clear();
        _preview = null;
        _previewWell = null;
        _pickedCard = null;
        _sections.Clear();
        var layout = Layout;
        _filledFor = layout?.Name;
        if (_sidebar) { FillSidebar(layout); return; }
        if (_head is Grid { Children.Count: > 0 } head && head.Children[0] is StackPanel titles && titles.Children.Count > 1 && titles.Children[1] is TextBlock caption)
            caption.Text = layout is null ? "" : $"In the {layout.Name} layout. Click a widget to set it up; drag one up or down to reorder the list. Arrange on screen puts them in place.";
        if (layout is null) { _list.Content = null; return; }
        if (layout.Widgets.Count == 0)
        {
            _list.Content = new TextBlock { Text = "No widgets in this layout yet: add one.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap };
            return;
        }
        var slots = layout.Widgets.Select(w => new PanelSlot { Id = w.Id }).ToList();
        var grid = new PanelGrid(slots, () =>
        {
            var byId = layout.Widgets.ToDictionary(w => w.Id);
            layout.Widgets = slots.Select(s => byId.GetValueOrDefault(s.Id)).OfType<HudWidgetConfig>().Concat(layout.Widgets.Where(w => slots.All(s => s.Id != w.Id))).ToList();
            Changed();
        }) { LiveEdit = true, RowHeight = 2, Gap = 12, MinColumns = 1 };
        for (int i = 0; i < layout.Widgets.Count; i++)
        {
            var w = layout.Widgets[i];
            grid.Add(new PanelDef(w.Id, NameOf(w), _ => Card(w))
            {
                FullWidth = true, Order = i,
                VeilClick = () => { _selected = _selected == w.Id ? null : w.Id; Host?.Pick(_selected); Fill(); },
            });
        }
        _list.Content = grid;
    }

    private FrameworkElement Card(HudWidgetConfig w)
    {
        bool picked = w.Id == _selected;
        const double edge = 1;
        var outer = ThemeManager.Round(12);
        var inner = new CornerRadius(Math.Max(0, outer.TopLeft - edge), Math.Max(0, outer.TopRight - edge), Math.Max(0, outer.BottomRight - edge), Math.Max(0, outer.BottomLeft - edge));
        var card = new Border
        {
            CornerRadius = outer, BorderThickness = new Thickness(edge),
            Cursor = System.Windows.Input.Cursors.Hand, ToolTip = picked ? null : HudCatalog.Find(w.Kind)?.Description,
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        var stack = new StackPanel();
        var header = Header(w, picked);
        header.CornerRadius = picked ? new CornerRadius(inner.TopLeft, inner.TopRight, 0, 0) : inner;
        stack.Children.Add(header);
        if (picked)
        {
            var divider = new Border { Height = 1 };
            divider.SetResourceReference(Border.BackgroundProperty, "LineStrong");
            stack.Children.Add(divider);
            var editor = Editor(w);
            editor.CornerRadius = new CornerRadius(0, 0, inner.BottomRight, inner.BottomLeft);
            stack.Children.Add(editor);
            _pickedCard = card;
        }
        card.Child = stack;
        header.MouseEnter += (_, _) =>
        {
            header.SetResourceReference(Border.BackgroundProperty, "Hover");
            card.SetResourceReference(Border.BorderBrushProperty, "InputEdgeHover");
        };
        header.MouseLeave += (_, _) =>
        {
            if (picked) header.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            else header.Background = Brushes.Transparent;
            card.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        };
        return card;
    }

    private Border Header(HudWidgetConfig w, bool picked)
    {
        var def = HudCatalog.Find(w.Kind);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bar = new Border { CornerRadius = ThemeManager.Round(1.5), Margin = new Thickness(0, 1, 0, 1) };
        if (picked) bar.SetResourceReference(Border.BackgroundProperty, "AccentText");
        grid.Children.Add(bar);
        var chevron = new Icon { Data = Ui.Res<Geometry>(picked ? "I.ChevronDown" : "I.ChevronRight"), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        chevron.SetResourceReference(Icon.ForegroundProperty, picked ? "AccentText" : "TextMuted");
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(chevron);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 12, 0) };
        text.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = def is null ? $"{w.Kind} (needs a newer Goodie Bag)" : NameOf(w), FontWeight = FontWeights.SemiBold, Style = Ui.Res<Style>("Body"), TextTrimming = TextTrimming.CharacterEllipsis }));
        var summary = TextFit.TipWhenTrimmed(new TextBlock { Text = Summary(w), Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        _summaries[w.Id] = summary;
        text.Children.Add(summary);
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);
        var on = Ui.Switch(w.Enabled, v => { w.Enabled = v; Changed(); text.Opacity = v ? 1 : 0.6; });
        on.VerticalAlignment = VerticalAlignment.Center;
        on.ToolTip = "Show this widget";
        System.Windows.Automation.AutomationProperties.SetName(on, def?.Name ?? w.Kind);
        text.Opacity = w.Enabled ? 1 : 0.6;
        Grid.SetColumn(on, 3);
        grid.Children.Add(on);
        var remove = RemoveButton(w);
        remove.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(remove, 4);
        grid.Children.Add(remove);
        var header = new Border { Padding = new Thickness(8, 10, 14, 10), Child = grid, Background = Brushes.Transparent };
        if (picked) header.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        return header;
    }

    private static string Anchor(HudAnchor a) => a switch
    {
        HudAnchor.TopLeft => "top left", HudAnchor.Top => "top", HudAnchor.TopRight => "top right", HudAnchor.Left => "left", HudAnchor.Center => "center",
        HudAnchor.Right => "right", HudAnchor.BottomLeft => "bottom left", HudAnchor.Bottom => "bottom", _ => "bottom right",
    };

    private const double LabelWidth = 180;
    private const double ComboWidth = 240;
    private const double ChipFont = 12.5, ChipHeight = 30;

    private static Tile TickChip(CheckBox box)
    {
        box.Margin = new Thickness(4, 0, 6, 0);
        box.VerticalAlignment = VerticalAlignment.Center;
        var chip = new Tile { Style = Ui.Res<Style>("Chip"), Child = box, MinHeight = ChipHeight, Padding = new Thickness(6, 0, 6, 0) };
        chip.SetValue(TextElement.FontSizeProperty, ChipFont);
        return chip;
    }

    private const double TwoColumnsFrom = 940;

    private sealed class EditorSection
    {
        public required string Id;
        public required bool OpenByDefault;
        public required FrameworkElement Root;
        public required Border Body;
        public required Icon Chevron;
        public TextBlock? SummaryText;
        public Func<string>? Summary;
        public bool Fixed;
        public DockPanel? Line;
    }

    private readonly List<EditorSection> _sections = new();
    private bool IsOpen(EditorSection s) => s.Fixed || s.OpenByDefault != S.EditorSectionsToggled.Contains(s.Id, StringComparer.OrdinalIgnoreCase);

    private void SetOpen(EditorSection s, bool open)
    {
        S.EditorSectionsToggled.RemoveAll(x => string.Equals(x, s.Id, StringComparison.OrdinalIgnoreCase));
        if (open != s.OpenByDefault) S.EditorSectionsToggled.Add(s.Id);
        _app.Save();
    }

    private void ShowSections()
    {
        foreach (var s in _sections)
        {
            bool open = IsOpen(s);
            s.Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            s.Chevron.Data = Ui.Res<Geometry>(open ? "I.ChevronDown" : "I.ChevronRight");
            if (s.SummaryText is not null)
            {
                s.SummaryText.Text = s.Summary?.Invoke() ?? "";
                s.SummaryText.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    private EditorSection Section(string id, string title, bool openByDefault, Func<string>? summary = null, bool fixedOpen = false)
    {
        var chevron = new Icon { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        chevron.SetResourceReference(Icon.ForegroundProperty, "TextMuted");
        var name = new TextBlock { Text = title, Style = Ui.Res<Style>("Eyebrow"), VerticalAlignment = VerticalAlignment.Center };
        var summaryText = summary is null ? null : TextFit.TipWhenTrimmed(new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(14, 0, 0, 0) });
        var line = new DockPanel { LastChildFill = true };
        line.Children.Add(chevron);
        line.Children.Add(name);
        if (summaryText is not null) line.Children.Add(summaryText);
        var body = new Border { Padding = new Thickness(6, 6, 0, 10) };
        var root = new StackPanel();
        if (fixedOpen)
        {
            chevron.Visibility = Visibility.Collapsed;
            root.Children.Add(new Border { Padding = new Thickness(6, 7, 10, 7), Child = line });
            root.Children.Add(body);
            var still = new EditorSection { Id = id, OpenByDefault = true, Fixed = true, Root = root, Body = body, Chevron = chevron, Line = line };
            _sections.Add(still);
            return still;
        }
        var bar = new Button { Style = Ui.Res<Style>("QuietButton"), Content = line, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 7, 10, 7) };
        Controls.ClickGuard.SetAllowRepeat(bar, true);
        System.Windows.Automation.AutomationProperties.SetName(bar, title);
        root.Children.Add(bar);
        root.Children.Add(body);
        var section = new EditorSection { Id = id, OpenByDefault = openByDefault, Root = root, Body = body, Chevron = chevron, SummaryText = summaryText, Summary = summary };
        bar.Click += (_, _) =>
        {
            SetOpen(section, !IsOpen(section));
            ShowSections();
        };
        _sections.Add(section);
        return section;
    }

    private static void Add(EditorSection section, FrameworkElement row)
    {
        if (section.Body.Child is not StackPanel stack) section.Body.Child = stack = new StackPanel();
        stack.Children.Add(row);
    }

    private TextBlock? _previewHint;

    private Border Editor(HudWidgetConfig w)
    {
        var def = HudCatalog.Find(w.Kind);
        _sections.Clear();
        var editor = new Border { Padding = new Thickness(18, 14, 18, 16), Cursor = System.Windows.Input.Cursors.Arrow };
        editor.SetResourceReference(Border.BackgroundProperty, "Panel");
        PanelGrid.SetNoDrag(editor, true);
        var stack = new StackPanel();
        editor.Child = stack;
        if (def is null)
        {
            stack.Children.Add(new TextBlock { Text = "This widget was made by a newer version of Goodie Bag. It's kept, but not drawn.", Style = Ui.Res<Style>("Caption") });
            return editor;
        }

        stack.Children.Add(new TextBlock { Text = def.Description, Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 6) });

        var preview = Section("preview", "Preview", true, fixedOpen: true);
        _previewHint = new TextBlock { Text = "Drag any item to move it, live", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed };
        preview.Line!.Children.Add(_previewHint);
        Add(preview, PreviewBlock(w));

        var settings = new List<EditorSection>();
        foreach (var (group, id, title) in new[] { (HudOptionGroup.Parts, "parts", "Widget sections"), (HudOptionGroup.Show, "show", "What to show"), (HudOptionGroup.Filter, "filter", "Filter"), (HudOptionGroup.Options, "options", "Display options") })
        {
            var options = def.Options.Where(o => o.Section == group).ToList();
            if (options.Count == 0) continue;
            var section = Section(id, title, true);
            foreach (var o in options)
                Add(section, OptionRow(w, o));
            settings.Add(section);
        }

        var place = Section("placement", "Placement & size", false, () => $"{w.W:0} × {w.H:0} px · {Anchor(w.Anchor)}" + (w.Monitor == "primary" ? "" : " · " + w.Monitor.Replace(@"\\.\", "")));
        foreach (var row in PlacementRows(w, def)) Add(place, row);
        var behaviour = Section("behaviour", "Behavior", false, () =>
            (w.Locked ? "Locked" : "Unlocked") + $" · {w.Opacity}%" + (w.HideInReplay ? "" : " · in replays") + (w.UpdateHz == 0 ? "" : $" · {w.UpdateHz}/s")
            + ((w.OnlyWhenNeeded ?? def.HideWhenEmpty) == true ? " · only when needed" : "") + (w.OnlyOnTrack ? " · driving only" : "")
            + " · " + (w.Sessions.Count == 0 ? "every session" : string.Join(", ", w.Sessions.Select(SessionLabel))));
        foreach (var row in BehaviourRows(w, def)) Add(behaviour, row);
        BuildBody(w, stack, preview, settings, place, behaviour, def);
        ShowSections();
        DrawPreview(w);
        return editor;
    }

    private FrameworkElement PreviewBlock(HudWidgetConfig w)
    {
        _preview = new Image { Stretch = Stretch.None, RenderTransform = _previewNudge };
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.NearestNeighbor);
        _previewMarks = new Canvas { IsHitTestVisible = false };
        var previewHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Children = { _preview, _previewMarks } };
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

    private List<FrameworkElement> PlacementRows(HudWidgetConfig w, HudWidgetDef def)
    {
        var rows = new List<FrameworkElement>();
        var monitors = HudHost.MonitorChoices().Select(m => new Choice(m.Id, m.Label)).ToList();
        rows.Add(FieldRow("Monitor", Ui.Combo(monitors, w.Monitor, c => { w.Monitor = c.Id!; Changed(); }, ComboWidth)));
        NumberBox Num(double value, double min, double max, Action<double> set)
        {
            var b = new NumberBox(value, "px", 64) { Minimum = min, Maximum = max, Step = 4 };
            b.ValueChanged += v => { Host?.RecordUndo(); set(v); w.Clamp(); Changed(); RefreshRow(); };
            return b;
        }
        rows.Add(FieldRow("Size", Pair(Num(w.W, def.MinW, HudWidgetConfig.MaxSize, v => w.W = v), "Width", Num(w.H, def.MinH, HudWidgetConfig.MaxSize, v => w.H = v), "Height")));
        rows.Add(FieldRow("Position", Pair(Num(w.X, -HudWidgetConfig.MaxSize, HudWidgetConfig.MaxSize, v => w.X = v), "Across", Num(w.Y, -HudWidgetConfig.MaxSize, HudWidgetConfig.MaxSize, v => w.Y = v), "Down"),
            $"Measured from the {Anchor(w.Anchor)} of the monitor. Dragging it with Arrange on screen sets these for you."));
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
        rows.Add(FieldRow("", home));
        return rows;
    }

    private List<FrameworkElement> BehaviourRows(HudWidgetConfig w, HudWidgetDef def)
    {
        var rows = new List<FrameworkElement>();
        if (def.HideWhenEmpty is { } hideByDefault)
            rows.Add(FieldRow("Only when there's something to show", Ui.Switch(w.OnlyWhenNeeded ?? hideByDefault, v => { w.OnlyWhenNeeded = v; Changed(); RefreshRow(); }),
                def.Kind == "alerts" ? "Hidden until an alert comes up." : "Hidden until a car of another class is near."));
        rows.Add(FieldRow("Only while I'm driving", Ui.Switch(w.OnlyOnTrack, v => { w.OnlyOnTrack = v; Changed(); RefreshRow(); }), "Hidden in the garage and while you're out of the car."));
        rows.Add(FieldRow("Locked", Ui.Switch(w.Locked, v => { w.Locked = v; Changed(); RefreshRow(); }), "Can't be dragged while arranging on screen."));
        rows.Add(FieldRow("Hide in replays", Ui.Switch(w.HideInReplay, v => { w.HideInReplay = v; Changed(); RefreshRow(); }), "Off: it shows while you watch a replay too."));
        rows.Add(FieldRow("Opacity", Ui.SliderWithValue(20, 100, w.Opacity, "{0:0}%", v => { w.Opacity = (int)v; Changed(); RefreshRow(); }, 180, 5), "The whole widget, text included."));
        var background = Ui.SliderWithValue(0, 100, w.Background ?? S.Appearance.Background, "{0:0}%", v => { w.Background = (int)v; Changed(); RefreshRow(); if (_preview is not null) DrawPreview(w); }, 180, 5);
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
        rows.Add(FieldRow("Own background", bgRow, "The panel behind the text only; switched off it follows the overlay's Background."));
        var rates = new[] { 0, 1, 2, 5, 10, 15, 20, 30, 60 }.Select(r => new Choice(r.ToString(), r == 0 ? $"Default ({def.DefaultHz} per second)" : $"{r} per second"));
        rows.Add(FieldRow("Redraw", Ui.Combo(rates, w.UpdateHz.ToString(), c => { w.UpdateHz = int.Parse(c.Id!); Changed(); RefreshRow(); }, ComboWidth)));
        var sessions = new WrapPanel();
        foreach (var kind in SessionKinds)
        {
            var chip = new CheckBox { Content = SessionLabel(kind), IsChecked = w.Sessions.Contains(kind, StringComparer.OrdinalIgnoreCase) };
            chip.Click += (_, _) =>
            {
                w.Sessions.RemoveAll(s => string.Equals(s, kind, StringComparison.OrdinalIgnoreCase));
                if (chip.IsChecked == true) w.Sessions.Add(kind);
                Changed();
                RefreshRow();
            };
            sessions.Children.Add(TickChip(chip));
        }
        rows.Add(FieldRow("Only in", sessions, "Tick the sessions it shows in; none ticked = every session."));
        return rows;
    }

    private void BuildBody(HudWidgetConfig w, StackPanel stack, EditorSection preview, List<EditorSection> settings, EditorSection place, EditorSection behaviour, HudWidgetDef def)
    {
        var settingsStack = new StackPanel();
        var previewColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        previewColumn.Children.Add(preview.Root);
        var body = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(previewColumn);
        body.Children.Add(settingsStack);
        bool? wide = null;
        var follow = Sticky.Install(previewColumn, body, () => wide == true);
        void Arrange(double width)
        {
            bool two = width >= TwoColumnsFrom;
            if (wide == two) return;
            wide = two;
            settingsStack.Children.Clear();
            while (previewColumn.Children.Count > 1) previewColumn.Children.RemoveAt(1);
            foreach (var s in settings) settingsStack.Children.Add(s.Root);
            foreach (var s in new[] { place, behaviour }) (two ? previewColumn : settingsStack).Children.Add(s.Root);
            body.ColumnDefinitions[0].Width = two ? new GridLength(5, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
            body.ColumnDefinitions[1].Width = two ? new GridLength(6, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(settingsStack, two ? 1 : 0);
            Grid.SetRow(settingsStack, two ? 0 : 1);
            settingsStack.Margin = new Thickness(two ? 24 : 0, 0, 0, 0);
            _previewMaxHeight = two ? 480 : 260;
            DrawPreview(w);
            follow();
        }
        Arrange(0);
        body.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        stack.Children.Add(body);

        if (def.Limits is { } limits)
        {
            var note = new DockPanel { Margin = new Thickness(6, 10, 0, 0) };
            var info = new Icon { Data = Ui.Res<Geometry>("I.Info"), Width = 14, Height = 14, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            info.SetResourceReference(Icon.ForegroundProperty, "TextMuted");
            note.Children.Add(info);
            note.Children.Add(new TextBlock { Text = limits, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(note);
        }

        var divider = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 0) };
        divider.SetResourceReference(Border.BackgroundProperty, "Line");
        stack.Children.Add(divider);
        stack.Children.Add(ActionsRow(w));
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

    private static readonly (string Id, string Title)[] TabNames = { ("display", "Display"), ("content", "Content"), ("visibility", "Visibility"), ("position", "Position") };

    private void FillSidebar(HudLayout? layout)
    {
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
            else if (def is not null)
            {
                body.Children.Add(Tabs());
                body.Children.Add(TabBody(picked, def));
            }
        }
        _list.Content = body;
        if (_preview is not null && widgets.FirstOrDefault(x => x.Id == _selected) is { } shown2) DrawPreview(shown2);
    }

    private FrameworkElement PreviewBeside(HudWidgetConfig w, HudWidgetDef def)
    {
        var title = new TextBlock { Text = "Preview", Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center };
        _previewHint = new TextBlock { Text = "Drag any item to move it, live", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed };
        var previewColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        previewColumn.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8), Children = { title, _previewHint } });
        previewColumn.Children.Add(PreviewBlock(w));
        var tabs = new StackPanel { Children = { Tabs(), TabBody(w, def) } };
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(previewColumn);
        body.Children.Add(tabs);
        bool? wide = null;
        var follow = Sticky.Install(previewColumn, body, () => wide == true);
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

    private FrameworkElement Tabs()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (id, title) in TabNames)
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
            b.Click += (_, _) => { if (_tab == tabId) return; _tab = tabId; Fill(); };
            row.Children.Add(b);
        }
        var line = new Border { Height = 1 };
        line.SetResourceReference(Border.BackgroundProperty, "Line");
        return new StackPanel { Margin = new Thickness(0, 2, 0, 12), Children = { row, line } };
    }

    private FrameworkElement TabBody(HudWidgetConfig w, HudWidgetDef def)
    {
        var stack = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        void Heading(string text, FrameworkElement? beside = null)
        {
            var title = new TextBlock { Text = text, Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(beside is null ? new Border { Margin = new Thickness(0, 6, 0, 8), Child = title } : new DockPanel { Margin = new Thickness(0, 6, 0, 8), Children = { title, beside } });
        }
        void Options(HudOptionGroup group)
        {
            foreach (var o in def.Options.Where(o => o.Section == group)) stack.Children.Add(OptionRow(w, o));
        }
        switch (_tab)
        {
            case "content":
                bool any = false;
                if (def.Options.Any(o => o.Section == HudOptionGroup.Parts)) { Heading("Widget sections"); Options(HudOptionGroup.Parts); any = true; }
                if (def.Options.Any(o => o.Section == HudOptionGroup.Show)) { Heading("What to show"); Options(HudOptionGroup.Show); any = true; }
                if (def.Options.Any(o => o.Section == HudOptionGroup.Filter)) { Heading("Filter"); Options(HudOptionGroup.Filter); any = true; }
                if (!any) stack.Children.Add(new TextBlock { Text = "Nothing to pick for this widget: its options are on the Display tab.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
                break;
            case "visibility":
                Heading("Visibility");
                foreach (var row in BehaviourRows(w, def)) stack.Children.Add(row);
                break;
            case "position":
                Heading("Position and size");
                foreach (var row in PlacementRows(w, def)) stack.Children.Add(row);
                var divider = new Border { Height = 1, Margin = new Thickness(0, 14, 0, 0) };
                divider.SetResourceReference(Border.BackgroundProperty, "Line");
                stack.Children.Add(divider);
                stack.Children.Add(ActionsRow(w));
                break;
            default:
                if (def.Options.Any(o => o.Section == HudOptionGroup.Options)) { Heading("Display"); Options(HudOptionGroup.Options); }
                if (def.Limits is { } limits)
                    stack.Children.Add(new TextBlock { Text = limits, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
                break;
        }
        return stack;
    }

    private static string SessionLabel(string kind) => kind switch { "Qualify" => "Qualifying", "TimeTrial" => "Time trial", _ => kind };

    private static Grid FieldRow(string label, FrameworkElement control, string? help = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 34 });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Body"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        if (help is not null)
        {
            var text = new TextBlock { Text = help, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetColumn(text, 1);
            Grid.SetRow(text, 1);
            grid.Children.Add(text);
        }
        return grid;
    }

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

    private readonly Dictionary<string, TextBlock> _summaries = new();

    private void RefreshRow()
    {
        if (_selected is not null && _summaries.TryGetValue(_selected, out var text) && Layout?.Widgets.FirstOrDefault(w => w.Id == _selected) is { } w)
            text.Text = Summary(w);
        foreach (var s in _sections)
            if (s.SummaryText is not null) s.SummaryText.Text = s.Summary?.Invoke() ?? "";
    }

    private static string Summary(HudWidgetConfig w) =>
        $"{w.W:0}×{w.H:0} · {Anchor(w.Anchor)}" + (w.Monitor == "primary" ? "" : " · " + w.Monitor.Replace(@"\\.\", "")) + (w.Locked ? " · locked" : "")
        + (w.Sessions.Count > 0 ? " · " + string.Join(", ", w.Sessions) : "");

    private FrameworkElement OptionRow(HudWidgetConfig w, HudOption o)
    {
        string current = w.Options.TryGetValue(o.Key, out var v) ? v : o.Default;
        void Set(string value)
        {
            if (o == HudFlags.BannerOption)
            {
                var (l0, t0, r0, b0) = HudFlags.Room(new HudOptions(HudCatalog.Find(w.Kind)!, w).Choice(o.Key));
                var (l1, t1, r1, b1) = HudFlags.Room(value);
                w.W = Math.Max(1, w.W + l1 + r1 - l0 - r0);
                w.H = Math.Max(1, w.H + t1 + b1 - t0 - b0);
            }
            if (value == o.Default) w.Options.Remove(o.Key); else w.Options[o.Key] = value;
            Changed();
            if (_preview is not null) DrawPreview(w);
        }
        switch (o.Kind)
        {
            case HudOptionKind.Columns:
                return ColumnPicker(o, current, Set, reload => _columnPickers[o.Key] = reload, mark => _fitMarks[o.Key] = (mark, ""));
            case HudOptionKind.Set:
                return SetPicker(o, current, Set);
            case HudOptionKind.Classes:
                return ClassPicker(o, current, Set);
        }
        FrameworkElement control;
        switch (o.Kind)
        {
            case HudOptionKind.Bool:
                control = Ui.Switch(current == "true", on => Set(on ? "true" : "false"));
                break;
            case HudOptionKind.Int:
            case HudOptionKind.Number:
                double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out var num);
                var box = new NumberBox(num, o.Unit ?? "", 64) { Minimum = o.Min, Maximum = o.Max, Step = o.Kind == HudOptionKind.Int ? 1 : o.Step, Decimals = o.Kind == HudOptionKind.Int ? 0 : o.Step < 0.1 ? 2 : o.Step < 1 ? 1 : 0 };
                box.ValueChanged += x => Set(x.ToString(o.Kind == HudOptionKind.Int ? "0" : "0.###", CultureInfo.InvariantCulture));
                control = box;
                break;
            case HudOptionKind.Choice:
                control = o.Choices.Count <= 3 && o.Choices.All(c => c.Label.Length < 22)
                    ? Ui.Segmented(o.Choices.Select(c => c.Label).ToList(), Math.Max(0, o.Choices.ToList().FindIndex(c => c.Id == current)), i => Set(o.Choices[i].Id), "opt" + w.Id + o.Key, o.Choices.Select(c => c.Help).ToList())
                    : Ui.Combo(o.Choices.Select(c => new Choice(c.Id, c.Help is null ? c.Label : $"{c.Label}  ({c.Help})")), current, c => Set(c.Id!), ComboWidth);
                break;
            default:
                control = new TextBlock { Text = current };
                break;
        }
        return FieldRow(o.Label, control, o.Help);
    }

    private static FrameworkElement Block(HudOption o, FrameworkElement picker, FrameworkElement? shortcuts)
    {
        var block = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        var title = new TextBlock { Text = o.Label, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        block.Children.Add(shortcuts is null ? title : Ui.SplitRow(title, shortcuts));
        if (o.Help is not null) block.Children.Add(new TextBlock { Text = o.Help, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        picker.Margin = new Thickness(0, 8, 0, 0);
        block.Children.Add(picker);
        return block;
    }

    private static FrameworkElement Shortcuts(params (string Label, string Tip, Action Click)[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, tip, click) in buttons)
        {
            var b = new Button { Style = Ui.Res<Style>("GhostButton"), Content = label, ToolTip = tip, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), MinHeight = 0 };
            b.Click += (_, _) => click();
            row.Children.Add(b);
        }
        return row;
    }

    private static Tile PickedChip(string label, string? tip, Action remove, TextBlock? note = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 4, 0) });
        if (note is not null) panel.Children.Add(note);
        var x = new Button { Style = Ui.Res<Style>("QuietButton"), Padding = new Thickness(3), ToolTip = "Take out", Content = new Icon { Data = Ui.Res<Geometry>("I.X"), Width = 10, Height = 10 } };
        x.Click += (_, _) => remove();
        panel.Children.Add(x);
        var chip = new Tile { Style = Ui.Res<Style>("Chip"), Child = panel, ToolTip = tip, Margin = new Thickness(0), Padding = new Thickness(9, 0, 3, 0), MinHeight = ChipHeight };
        chip.SetValue(TextElement.FontSizeProperty, ChipFont);
        panel.VerticalAlignment = VerticalAlignment.Center;
        chip.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        chip.SetResourceReference(TextElement.ForegroundProperty, "AccentText");
        return chip;
    }

    private static Button AddChip(string label, string? tip, Action add)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.Plus"), Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Style = Ui.Res<Style>("GhostButton"), Content = panel, ToolTip = tip, Padding = new Thickness(9, 0, 11, 0), Margin = new Thickness(0, 0, 6, 6), FontSize = ChipFont, Height = ChipHeight };
        b.Click += (_, _) => add();
        return b;
    }

    private static FrameworkElement ColumnPicker(HudOption o, string current, Action<string> set, Action<Action<string>>? register = null,
        Action<Action<IReadOnlySet<string>, IReadOnlySet<string>>>? registerFit = null)
    {
        var host = new ContentControl { Focusable = false };
        List<string> Parse(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(id => o.Choices.Any(c => c.Id == id)).Distinct().ToList();
        var on = Parse(current);
        var chips = new List<(string Id, Tile Chip, TextBlock Note, string? Tip)>();
        var adds = new List<(string Id, Button Button, string? Tip)>();
        IReadOnlySet<string> overflow = new HashSet<string>(), blocked = new HashSet<string>();
        Button? all = null;
        void Mark(IReadOnlySet<string> hidden, IReadOnlySet<string> noRoomToAdd)
        {
            overflow = hidden;
            blocked = noRoomToAdd;
            foreach (var (id, chip, note, tip) in chips)
            {
                bool noRoom = hidden.Contains(id);
                chip.Opacity = noRoom ? 0.6 : 1;
                note.Visibility = noRoom ? Visibility.Visible : Visibility.Collapsed;
                chip.ToolTip = noRoom ? "Doesn't fit at this width: widen the widget, or drag it earlier" : tip;
            }
            foreach (var (id, button, tip) in adds)
            {
                bool full = blocked.Contains(id);
                button.IsEnabled = !full;
                button.ToolTip = !full ? tip : hidden.Count > 0 ? "No room: take one out first, or widen the widget" : "Too wide for the room left: take one out, or widen the widget";
            }
            if (all is not null) all.IsEnabled = hidden.Count == 0 && blocked.Count == 0;
        }
        void Save() => set(string.Join(",", on));
        void Build()
        {
            chips.Clear();
            adds.Clear();
            var stack = new StackPanel();
            if (on.Count == 0) stack.Children.Add(new TextBlock { Text = "Nothing shown yet: add from the list below.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 6) });
            if (on.Count > 0)
            {
                var grid = new ReorderGrid { Flow = true, MinCellWidth = 40, Gap = 6 };
                foreach (var id in on)
                {
                    var choice = o.Choices.First(c => c.Id == id);
                    var note = new TextBlock { Text = "doesn't fit", FontStyle = FontStyles.Italic, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Visibility = Visibility.Collapsed };
                    var chip = PickedChip(choice.Label, choice.Help, () => { on.Remove(id); Save(); Build(); }, note);
                    chip.Tag = id;
                    chips.Add((id, chip, note, choice.Help));
                    grid.Children.Add(chip);
                }
                grid.Moved += (_, _) => { on = grid.Children.OfType<FrameworkElement>().Select(t => (string)t.Tag).ToList(); Save(); };
                stack.Children.Add(grid);
            }
            var available = o.Choices.Where(c => !on.Contains(c.Id)).ToList();
            if (available.Count > 0)
            {
                stack.Children.Add(new TextBlock { Text = "Add", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 12, 0, 6) });
                var add = new WrapPanel();
                foreach (var c in available)
                {
                    var chip = AddChip(c.Label, c.Help, () => { on.Add(c.Id); Save(); Build(); });
                    ToolTipService.SetShowOnDisabled(chip, true);
                    adds.Add((c.Id, chip, c.Help));
                    add.Children.Add(chip);
                }
                stack.Children.Add(add);
            }
            host.Content = stack;
            Mark(overflow, blocked);
        }
        Build();
        register?.Invoke(value => { on = Parse(value); Build(); });
        registerFit?.Invoke(Mark);
        var shortcuts = Shortcuts(
            ("All", "Show every one (the ones shown keep their order)", () => { on = on.Concat(o.Choices.Select(c => c.Id).Where(id => !on.Contains(id))).ToList(); Save(); Build(); }),
            ("None", "Take every one out", () => { on = new(); Save(); Build(); }),
            ("Default", "As the widget starts", () => { on = Parse(o.Default); Save(); Build(); }));
        if (registerFit is not null && shortcuts is Panel { Children: [Button first, ..] })
        {
            all = first;
            ToolTipService.SetShowOnDisabled(all, true);
        }
        return Block(o, host, shortcuts);
    }

    private static FrameworkElement SetPicker(HudOption o, string current, Action<string> set)
    {
        var on = current.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        var panel = new WrapPanel();
        var boxes = new List<(CheckBox Box, string Id)>();
        void Save() => set(string.Join(",", o.Choices.Select(c => c.Id).Where(on.Contains)));
        foreach (var choice in o.Choices)
        {
            var chip = new CheckBox { Content = choice.Label, IsChecked = on.Contains(choice.Id), ToolTip = choice.Help };
            chip.Click += (_, _) =>
            {
                if (chip.IsChecked == true) on.Add(choice.Id); else on.Remove(choice.Id);
                Save();
            };
            boxes.Add((chip, choice.Id));
            panel.Children.Add(TickChip(chip));
        }
        void Pick(IEnumerable<string> ids)
        {
            on = ids.ToHashSet();
            foreach (var (box, id) in boxes) box.IsChecked = on.Contains(id);
            Save();
        }
        return Block(o, panel, Shortcuts(
            ("All", "Tick every one", () => Pick(o.Choices.Select(c => c.Id))),
            ("None", "Untick every one", () => Pick(Array.Empty<string>())),
            ("Default", "As the widget starts", () => Pick(o.Default.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))));
    }

    private FrameworkElement ClassPicker(HudOption o, string current, Action<string> set)
    {
        var f = _section.Preview.Race.Frame;
        var session = f.Session.Drivers.Where(d => d.Races && d.ClassName.Length > 0).Select(d => d.ClassName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string? mine = f.Session.Player?.ClassName is { Length: > 0 } m ? m : null;
        var hidden = current.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var known = SessionNames.Merge(null, S.KnownClasses, SessionNames.Classes).Where(c => !session.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        var others = hidden.Where(h => !session.Contains(h, StringComparer.OrdinalIgnoreCase) && !known.Contains(h, StringComparer.OrdinalIgnoreCase)).Concat(known).ToList();
        var boxes = new List<(CheckBox Box, Tile Chip, string Name)>();
        void Save() => set(string.Join(",", hidden));
        WrapPanel Chips(IEnumerable<string> names)
        {
            var panel = new WrapPanel();
            foreach (var cls in names)
            {
                var box = new CheckBox { Content = cls, IsChecked = hidden.Contains(cls, StringComparer.OrdinalIgnoreCase),
                    ToolTip = string.Equals(cls, mine, StringComparison.OrdinalIgnoreCase) ? "Your class. Ticked = left out" : "Ticked = left out" };
                box.Click += (_, _) =>
                {
                    hidden.RemoveAll(x => string.Equals(x, cls, StringComparison.OrdinalIgnoreCase));
                    if (box.IsChecked == true) hidden.Add(cls);
                    Save();
                };
                var chip = TickChip(box);
                boxes.Add((box, chip, cls));
                panel.Children.Add(chip);
            }
            return panel;
        }
        var stack = new StackPanel();
        if (session.Count > 0)
        {
            stack.Children.Add(new TextBlock { Text = (f.Simulated ? "In the simulated session" : "In this session") + " · ticked = left out", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 6) });
            stack.Children.Add(Chips(session));
        }
        if (others.Count > 0)
        {
            stack.Children.Add(new TextBlock { Text = "Other classes", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, session.Count > 0 ? 10 : 2, 0, 6) });
            stack.Children.Add(Chips(others));
        }
        if (session.Count == 0 && others.Count == 0) stack.Children.Add(new TextBlock { Text = "Classes appear here once a session is loaded.", Style = Ui.Res<Style>("Caption") });
        void Pick(IEnumerable<string> names)
        {
            hidden = names.ToList();
            foreach (var (box, _, name) in boxes) box.IsChecked = hidden.Contains(name, StringComparer.OrdinalIgnoreCase);
            Save();
        }
        var shortcuts = new List<(string, string, Action)> { ("Show all", "Leave no class out", () => Pick(Array.Empty<string>())) };
        if (mine is not null && session.Count > 1)
            shortcuts.Add(("Only my class", $"Leave out every class in this session but {mine}", () => Pick(session.Where(c => !string.Equals(c, mine, StringComparison.OrdinalIgnoreCase)))));
        return Block(o, stack, Shortcuts(shortcuts.ToArray()));
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
            bool draggable = (_painter.Items ?? new()).GroupBy(i => i.Key).Any(g => g.Count() > 1);
            string note = (race.Frame.Simulated ? "Sample data (no live session)" : "Live from iRacing") + $" · {size}";
            if (!TextCase.Plain(_previewNote!.Text).Equals(note, StringComparison.OrdinalIgnoreCase)) _previewNote.Text = note;
            if (_previewHint is not null) _previewHint.Visibility = draggable ? Visibility.Visible : Visibility.Collapsed;
            MarkOverflow(w, scale);
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
        var origin = m.Transform(new System.Windows.Point(0, 0));
        _previewNudge.X = Math.Clamp(_previewNudge.X - (origin.X - Math.Round(origin.X)) / k, -1, 1);
        _previewNudge.Y = Math.Clamp(_previewNudge.Y - (origin.Y - Math.Round(origin.Y)) / k, -1, 1);
    }

    private void MarkOverflow(HudWidgetConfig w, float scale)
    {
        if (_fitMarks.Count == 0 || HudCatalog.Find(w.Kind) is not { } def) return;
        var options = new HudOptions(def, w);
        var trials = new List<(string Key, string Id)>();
        foreach (var key in _fitMarks.Keys.ToList())
        {
            var hidden = (_painter.Overflow ?? new()).Where(x => x.Key == key).Select(x => x.Id).ToHashSet();
            var picked = options.Columns(key);
            string signature = string.Join(",", hidden.Order()) + "|" + string.Join(",", picked) + $"|{w.W:0}x{w.H:0}|{scale:0.00}";
            var (mark, last) = _fitMarks[key];
            if (signature == last) continue;
            _fitMarks[key] = (mark, signature);
            _trialRun++;
            var candidates = def.Option(key)?.Choices.Select(c => c.Id).Where(id => !picked.Contains(id)).ToList() ?? new();
            if (hidden.Count > 0) { mark(hidden, candidates.ToHashSet()); continue; }
            mark(hidden, new HashSet<string>());
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
                    if (_fitMarks.TryGetValue(key, out var fit)) fit.Mark(none, ids);
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

    private (string Key, string Id)? ItemAt(System.Windows.Point at)
    {
        if (_painter.Items is not { } items) return null;
        var p = new System.Drawing.PointF((float)(at.X / _previewScale), (float)(at.Y / _previewScale));
        for (int i = items.Count - 1; i >= 0; i--)
            if (items[i].Box.Contains(p) && Drawn(items[i].Key).Count > 1) return (items[i].Key, items[i].Id);
        return null;
    }

    private string? _dragBefore;
    private System.Windows.Vector _grab;
    private ImageBrush? _liftedLook;

    private void HookPreviewDrag(FrameworkElement host, HudWidgetConfig w)
    {
        host.MouseMove += (_, e) =>
        {
            var at = e.GetPosition(host);
            if (_press is not { } press) return;
            if (!_dragging && (at - press.From).Length < 4) return;
            if (!_dragging) _liftedLook = Lift(press.Key, press.Id);
            _dragging = true;
            Reorder(w, press.Key, press.Id, at);
            ShowDrag(press.Key, press.Id, at);
        };
        host.MouseLeftButtonDown += (_, e) =>
        {
            var at = e.GetPosition(host);
            if (ItemAt(at) is not { } item) return;
            _press = (item.Key, item.Id, at);
            _dragBefore = w.Options.GetValueOrDefault(item.Key);
            var box = Drawn(item.Key).FirstOrDefault(d => d.Id == item.Id).Box;
            _grab = new System.Windows.Vector(at.X - box.X * _previewScale, at.Y - box.Y * _previewScale);
            host.CaptureMouse();
            e.Handled = true;
        };
        host.MouseLeftButtonUp += (_, _) =>
        {
            if (_press is not { } press) return;
            bool dragged = _dragging;
            _press = null;
            EndDrag(host);
            if (dragged) Commit(w, press.Key);
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

    private void Reorder(HudWidgetConfig w, string key, string id, System.Windows.Point at)
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

    private void ShowDrag(string key, string id, System.Windows.Point at)
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
        if (_columnPickers.TryGetValue(key, out var reload)) reload(now ?? HudCatalog.Find(w.Kind)?.Option(key)?.Default ?? "");
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

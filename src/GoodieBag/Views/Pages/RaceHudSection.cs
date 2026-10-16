using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Hotkeys;
using GoodieBag.Plugins;
using GoodieBag.Plugins.BuiltIn;
using GoodieBag.Services;
using GoodieBag.SimRacing.Hud;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal interface IHudEditorHost
{
    PreviewSource Preview { get; }
    string ShownLayout { get; }
    void Changed();
    Task<bool> Confirm(string title, string message, string confirmText, bool danger);
}

internal sealed class RaceHudSection : StackPanel, IScreenshotStates, ICancelEdit, IHudEditorHost
{
    private static readonly string[] Tabs = { "Widgets", "Alerts", "Fuel", "Hud Settings" };
    private const int WidgetsTab = 0, AlertsTabIndex = 1, FuelTabIndex = 2, SettingsTab = 3;

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly Border _body = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private Border _statusChip = new();
    private Border _segments = new();
    private int _tab;
    private bool _shown;
    private RaceHudWidgets? _widgets;
    private Action? _tick;

    public PreviewSource Preview { get; }

    public RaceHudSection(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        Preview = new PreviewSource(() => S);
        _timer.Tick += (_, _) => { Preview.Advance(Host); RefreshStatus(); _tick?.Invoke(); };

        var top = new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 12) };
        var topStack = new StackPanel();
        var onOff = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        onOff.Children.Add(_statusChip);
        var power = Ui.Switch(S.Enabled, on => Host?.SetEnabled(on));
        power.Margin = new Thickness(12, 0, 0, 0);
        power.VerticalAlignment = VerticalAlignment.Center;
        onOff.Children.Add(power);
        _status.Style = Ui.Res<Style>("Body");
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        System.Windows.Automation.AutomationProperties.SetName(power, "iRacing Overlay");
        topStack.Children.Add(Ui.SplitRow(_status, onOff, 16, stackLeft: true));
        top.Child = topStack;
        Children.Add(top);

        _segments = Ui.Segmented(Tabs, 0, i => { _tab = i; Build(); }, "hudtabs");
        _segments.HorizontalAlignment = HorizontalAlignment.Left;
        _segments.VerticalAlignment = VerticalAlignment.Center;
        var tabRow = Ui.SplitRow(_segments, _arrange);
        tabRow.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(tabRow);
        Children.Add(_body);
        Ui.Anchor(this, "simracing.hud");
        RefreshStatus();
        RefreshArrange();
        Build();
    }

    private Tile? _rulesCard;

    private readonly StackPanel _arrange = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private Button? _editLayout;

    private void RefreshArrange()
    {
        _arrange.Children.Clear();
        var h = Host;
        if (h?.Editing == true)
        {
            var cancel = Ui.LabeledButton("I.Undo", "Cancel", () => { Host?.EndEdit(keep: false); RefreshArrange(); });
            cancel.ToolTip = "Put every widget back where it was";
            cancel.Margin = new Thickness(0, 0, 8, 0);
            _arrange.Children.Add(cancel);
            _arrange.Children.Add(Ui.LabeledButton("I.Check", "Done arranging", () => { Host?.EndEdit(keep: true); RefreshArrange(); }, "PrimaryButton"));
            return;
        }
        bool simulating = h?.Preview == true;
        var simulate = Ui.LabeledButton("I.Play", simulating ? "Stop simulating" : "Simulate data", () => { Host?.SetPreview(!(Host?.Preview ?? false)); RefreshStatus(); RefreshArrange(); },
            simulating ? "SelectedButton" : "GhostButton");
        simulate.IsEnabled = !_app.Demo && h is not null;
        simulate.ToolTip = _app.Demo ? "Not with demo data (no widgets are put on screen)"
            : "Puts the widgets on screen with a made-up race (marked SIMULATED), to try layouts without iRacing (not while a session runs). Not saved.";
        ToolTipService.SetShowOnDisabled(simulate, true);
        simulate.Margin = new Thickness(0, 0, 8, 0);
        _arrange.Children.Add(simulate);
        _editLayout = Ui.LabeledButton("I.Pencil", "Edit layout", () => { if (Host is { } hud) HudQuickEditor.Toggle(_app, hud); });
        _editLayout.Margin = new Thickness(0, 0, 8, 0);
        ToolTipService.SetShowOnDisabled(_editLayout, true);
        SyncEditLayout();
        _arrange.Children.Add(_editLayout);
        var arrange = Ui.LabeledButton("I.Move", "Arrange on screen", () => { Host?.BeginEdit(); RefreshArrange(); }, "PrimaryButton");
        arrange.IsEnabled = !_app.Demo && h is not null;
        arrange.ToolTip = _app.Demo ? "Not with demo data (no widgets are put on screen)"
            : "Drag widgets to move them, drag a corner or edge to resize (Alt = no snapping). The bar at the top of the screen has Undo, Snap and Done.";
        ToolTipService.SetShowOnDisabled(arrange, true);
        _arrange.Children.Add(arrange);
    }

    private void SyncEditLayout()
    {
        if (_editLayout is not { } b) return;
        bool on = !_app.Demo && Host?.OnScreen == true;
        if (b.IsEnabled != on) b.IsEnabled = on;
        string key = S.QuickEditHotkey is { Length: > 0 } k ? $" ({Hotkey.Display(k)})" : "";
        b.ToolTip = _app.Demo ? "Not with demo data (no widgets are put on screen)"
            : on ? $"Opens the overlay editor over the game: the widgets on screen, their settings, undo, snap{key}"
            : $"Opens once the overlay is on screen: in a session, or with Simulate data{key}";
    }

    private RaceHudSettings S => _app.Config.SimRacing.Hud;

    private HudHost? _watched;

    private HudHost? Host => _app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin { Hud: { } hud } ? hud : null;

    public void OnShown()
    {
        if (_shown) return;
        _shown = true;
        if (Host is { } h)
        {
            h.Changed += OnHostChanged;
            h.Watch(true);
            _watched = h;
        }
        _timer.Start();
        RefreshStatus();
    }

    public void OnHidden()
    {
        if (!_shown) return;
        _shown = false;
        if (Host is { } h) h.Changed -= OnHostChanged;
        _watched?.Watch(false);
        _watched = null;
        _timer.Stop();
    }

    private void OnHostChanged()
    {
        RefreshStatus();
        RefreshArrange();
        if (_tab == WidgetsTab)
        {
            if (LayoutsSignature() != _builtLayouts)
                Dispatcher.BeginInvoke(() => { if (_tab == WidgetsTab && LayoutsSignature() != _builtLayouts) Build(); }, DispatcherPriority.Background);
            else SyncLayoutPick();
        }
    }

    private string _builtLayouts = "";
    private ComboBox? _layoutPick;

    private string LayoutsSignature() => string.Join("|", S.Layouts.Select(l => l.Name));

    public Task<bool> Confirm(string title, string message, string confirmText, bool danger) => _window.Confirm(title, message, confirmText, danger);

    public string ShownLayout => Host?.LayoutName is { Length: > 0 } n ? n : S.ActiveLayout;

    private void SyncLayoutPick()
    {
        if (_layoutPick is null) return;
        var want = _layoutPick.Items.Cast<Choice>().FirstOrDefault(c => c.Id == ShownLayout);
        if (want is not null && !Equals(_layoutPick.SelectedItem, want)) _layoutPick.SelectedItem = want;
        _widgets?.FollowLayout();
    }

    public void Changed()
    {
        if (Host is { } h) { h.SettingsChanged(); return; }
        bool renamed = HudPresets.Fork(S) is not null;
        _app.Save();
        if (renamed && _tab == WidgetsTab) Dispatcher.BeginInvoke(Build, DispatcherPriority.Background);
    }

    private void RefreshStatus()
    {
        SyncEditLayout();
        var t = _app.Telemetry;
        var h = Host;
        var (text, soft, deep) = !S.Enabled ? ("Off", "DangerSoft", "Danger")
            : t.Simulated && _app.Demo ? ("Simulated (demo)", "PlumSoft", "PlumDeep")
            : t.Status switch
            {
                SimStatus.Connected => (h?.Preview == true ? "Preview" : "iRacing connected", "SageSoft", "SageDeep"),
                SimStatus.Waiting => ("iRacing loading", "WarnSoft", "WarnDeep"),
                _ => ("Waiting for iRacing", "SlateSoft", "SlateDeep"),
            };
        var chip = Ui.Chip(text, soft, deep);
        chip.VerticalAlignment = VerticalAlignment.Center;
        if (_statusChip.Parent is Panel parent)
        {
            int i = parent.Children.IndexOf(_statusChip);
            parent.Children.RemoveAt(i);
            parent.Children.Insert(i, chip);
        }
        _statusChip = chip;
        string layout = ShownLayout;
        string why = h?.MatchedRule is { } rule ? $" (rule: {rule.Describe()})" : "";
        _status.Text = _app.Demo ? $"Layout {layout} · demo data: previews only"
            : !S.Enabled ? $"Layout {layout} · overlay off: nothing shows until it's switched on"
            : $"Layout {layout}{why} · {(h?.OnScreen == true ? "on screen" : ShowWhenText())}" + (h?.Editing == true ? " · arranging" : "");
        _status.Visibility = _status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string ShowWhenText() => S.ShowWhen switch
    {
        HudShowWhen.Always => "shown always",
        HudShowWhen.Connected => "shows when a session is loaded",
        _ => "shows when you're in the car",
    };

    private void Build()
    {
        _tick = null;
        _rules = null;
        _alertGroups = null;
        _widgets = null;
        _layoutPick = null;
        _body.Child = _tab switch
        {
            AlertsTabIndex => AlertsTab(),
            FuelTabIndex => FuelTab(),
            SettingsTab => HudSettingsTab(),
            _ => WidgetsTabView(),
        };
        if (_widgets is not null) _tick = _widgets.Tick;
        Motion.Stagger(_body.Child as Panel ?? new StackPanel());
    }

    private static Tile Group(string title, string? caption, params UIElement[] rows)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(title, "H3"));
        if (caption is not null) stack.Children.Add(Caption(caption, 2));
        var body = new SettingsFlow { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var r in rows) body.Children.Add(r);
        stack.Children.Add(body);
        return new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 12), Child = stack };
    }

    private static TextBlock Caption(string text, double top = 6) => new()
    {
        Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0),
    };

    private StackPanel WidgetsTabView()
    {
        var root = new StackPanel();

        var layouts = new StackPanel();
        var pick = _layoutPick = Ui.Combo(S.Layouts.Select(l => new Choice(l.Name, l.Name)), ShownLayout, c =>
        {
            if (c.Id == ShownLayout) return;
            Host?.UseLayout(c.Id!);
            if (Host is null) { S.ActiveLayout = c.Id!; _app.Save(); }
            RefreshStatus();
            _widgets?.FollowLayout();
        }, 260);
        _builtLayouts = LayoutsSignature();
        var add = Ui.LabeledButton("I.Plus", "New layout", () => { }, "PrimaryButton");
        add.Click += (_, _) => NewLayoutBox(add, S, S.Layout(ShownLayout), AddLayout);
        var dup = Ui.IconButton("I.Copy", "Duplicate this layout", () =>
        {
            var from = S.Layout(ShownLayout);
            if (from is null) return;
            var copy = from.Copy(S.UniqueName(from.Name + " copy"));
            foreach (var w in copy.Widgets) w.Id = HudWidgetConfig.NewId(w.Kind);
            AddLayout(copy);
        });
        var reset = Ui.LabeledButton("I.Restore", "Reset to default", async () =>
        {
            string name = ShownLayout;
            if (!await _window.Confirm($"Reset “{name}” to the default layout?", "Its widgets, their places and settings go back to how the overlay starts. Duplicate it first to keep this version.", "Reset", danger: true)) return;
            if (Host is { } h) h.ResetLayout(name);
            else { S.ResetToDefault(name, HudPresets.DefaultLayouts()[0]); _app.Save(); }
            Build();
        });
        reset.IsEnabled = S.Layout(ShownLayout) is { } shown && !HudPresets.IsDefault(shown);
        reset.ToolTip = reset.IsEnabled ? "This layout back to the overlay's default widgets and places" : "This layout is the default already";
        ToolTipService.SetShowOnDisabled(reset, true);
        var rename = Ui.IconButton("I.Pencil", "Rename this layout", RenameLayout);
        var delete = Ui.IconButton("I.Trash", "Delete this layout", async () =>
        {
            string name = ShownLayout;
            if (S.Layouts.Count <= 1 || !await _window.Confirm($"Delete “{name}”?", "Its widgets and the rules that pick it are removed.", "Delete", danger: true)) return;
            S.DeleteLayout(name);
            Changed();
            Build();
        });
        bool isDefault = RaceHudSettings.IsDefaultName(ShownLayout);
        delete.IsEnabled = S.Layouts.Count > 1 && !isDefault;
        rename.IsEnabled = !isDefault;
        if (isDefault)
        {
            rename.ToolTip = delete.ToolTip = "The default layout stays as it is; a change to it goes into a new Custom layout";
            ToolTipService.SetShowOnDisabled(rename, true);
            ToolTipService.SetShowOnDisabled(delete, true);
        }
        var row = new WrapPanel();
        row.Children.Add(pick);
        foreach (var b in new[] { dup, rename, delete }) { b.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(b); }
        add.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(add);
        reset.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(reset);
        layouts.Children.Add(row);
        root.Children.Add(Foldable("layout", "Layout", null, null, layouts, foldable: false));

        var rules = new StackPanel();
        var addRule = Ui.LabeledButton("I.Plus", "Add rule", () =>
        {
            S.Rules.Add(new HudLayoutRule { Layout = ShownLayout, Session = "Race" });
            Changed();
            _rules?.Fill();
            _rulesEmpty!.Visibility = S.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }, "SoftButton");
        _rules = new ArrangeList(
            () => S.Rules.Select(r => (r.Id, r.Describe(), (Func<FrameworkElement>)(() => RuleRow(r)))).ToList(),
            () => S.Rules.Select(r => r.Id).ToList(),
            order =>
            {
                S.Rules = order.Select(id => S.Rules.First(r => r.Id == id)).ToList();
                Changed();
                RefreshStatus();
            },
            "Arrange", "Drag the rules into order: when two fit a session equally, the higher one wins");
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _rules.Button.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(_rules.Button);
        actions.Children.Add(addRule);
        rules.Children.Add(Ui.SettingRow("Use these rules", null, Ui.Switch(S.AutoSwitch, on => { S.AutoSwitch = on; Changed(); RefreshStatus(); _rules?.View.SetValue(IsEnabledProperty, on); })));
        _rules.Fill();
        _rules.View.IsEnabled = S.AutoSwitch;
        _rules.View.Margin = new Thickness(0, 8, 0, 0);
        rules.Children.Add(_rules.View);
        _rulesEmpty = Caption("No rules: the layout picked above always shows.");
        _rulesEmpty.Visibility = S.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        rules.Children.Add(_rulesEmpty);
        root.Children.Add(_rulesCard = Foldable("rules", "Switch layouts automatically",
            "The most specific matching rule wins, then the higher one; with none, the layout above shows.",
            actions, rules));

        _widgets = new RaceHudWidgets(_app, this, sidebar: true, inPage: true);
        root.Children.Add(_widgets.Root);
        return root;
    }

    private Tile Foldable(string id, string title, string? caption, FrameworkElement? actions, FrameworkElement body, bool foldable = true)
    {
        bool folded = foldable && S.FoldedPanels.Contains(id, StringComparer.OrdinalIgnoreCase);
        var chevron = new Icon { Data = Ui.Res<Geometry>(folded ? "I.ChevronRight" : "I.ChevronDown"), Width = 15, Height = 15, Foreground = Ui.Res<Brush>("TextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Ui.Text(title, "H3"));
        if (caption is not null) titles.Children.Add(Caption(caption, 2));
        var header = new DockPanel { LastChildFill = true };
        header.Children.Add(chevron);
        header.Children.Add(titles);
        var bar = new Button { Style = Ui.Res<Style>("QuietButton"), Content = header, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16, 14, 12, 14) };
        Controls.ClickGuard.SetAllowRepeat(bar, true);
        System.Windows.Automation.AutomationProperties.SetName(bar, title);
        body.Margin = new Thickness(12, 0, 16, 14);
        void Show(bool open)
        {
            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (actions is not null) actions.Visibility = body.Visibility;
            chevron.Data = Ui.Res<Geometry>(open ? "I.ChevronDown" : "I.ChevronRight");
        }
        Show(!folded);
        bar.Click += (_, _) =>
        {
            bool fold = body.Visibility == Visibility.Visible;
            Show(!fold);
            S.FoldedPanels.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            if (fold) S.FoldedPanels.Add(id);
            _app.Save();
        };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (foldable) head.Children.Add(bar);
        else
        {
            header.Children.Remove(chevron);
            bar.Content = null;
            header.Margin = new Thickness(16, 14, 12, 14);
            head.Children.Add(header);
        }
        if (actions is not null)
        {
            actions.VerticalAlignment = VerticalAlignment.Center;
            actions.Margin = new Thickness(12, 0, 16, 0);
            Grid.SetColumn(actions, 1);
            head.Children.Add(actions);
        }
        if (foldable)
        {
            bar.Background = Brushes.Transparent;
            head.Background = Brushes.Transparent;
            head.MouseEnter += (_, _) => head.SetResourceReference(Panel.BackgroundProperty, "Hover");
            head.MouseLeave += (_, _) => head.Background = Brushes.Transparent;
            head.Cursor = System.Windows.Input.Cursors.Hand;
            head.MouseLeftButtonUp += (_, e) =>
            {
                if (e.Handled) return;
                e.Handled = true;
                bar.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
        }
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        return new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(0), Child = stack };
    }

    private ArrangeList? _rules;
    private TextBlock? _rulesEmpty;

    private FrameworkElement RuleRow(HudLayoutRule rule)
    {
        var f = Preview.Race.Frame;
        var me = f.Session.Player;
        var parts = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement Part(string label, ComboBox combo)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 6) };
            stack.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(2, 0, 0, 3) });
            stack.Children.Add(combo);
            return stack;
        }
        ComboBox Pick(IEnumerable<string> known, string? value, string any, Action<string?> set, double width)
        {
            var names = known.ToList();
            if (value is not null && !names.Contains(value, StringComparer.OrdinalIgnoreCase)) names.Insert(0, value);
            var combo = new SearchCombo { MinWidth = width };
            foreach (var c in names.Select(n => new Choice(n, n)).Prepend(new Choice("", any))) combo.Items.Add(c);
            combo.SelectedItem = combo.Items.Cast<Choice>().FirstOrDefault(c => string.Equals(c.Id, value ?? "", StringComparison.OrdinalIgnoreCase)) ?? combo.Items[0];
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice c) { set(c.Id is { Length: > 0 } s ? s : null); Changed(); RefreshStatus(); } };
            return combo;
        }
        parts.Children.Add(Part("Show layout", Ui.Combo(S.Layouts.Select(l => new Choice(l.Name, l.Name)), rule.Layout, c => { rule.Layout = c.Id!; Changed(); RefreshStatus(); }, 150)));
        parts.Children.Add(Part("In", Ui.Combo(new[]
        {
            new Choice("", "Every session"), new Choice("Practice", "Practice"), new Choice("Qualify", "Qualifying"), new Choice("Race", "Race"),
            new Choice("TimeTrial", "Time trials"),
        }, rule.Session ?? "", c => { rule.Session = c.Id is { Length: > 0 } s ? s : null; Changed(); RefreshStatus(); }, 140)));
        parts.Children.Add(Part("Class", Pick(SessionNames.Merge(rule.Class, S.KnownClasses.Append(me?.ClassName), SessionNames.Classes), rule.Class, "Any class", v => rule.Class = v, 150)));
        parts.Children.Add(Part("Car", Pick(SessionNames.Merge(rule.Car, S.KnownCars.Append(me?.CarName), GoodieBag.SimRacing.Setups.SetupCars.BuiltInNames), rule.Car, "Any car", v => rule.Car = v, 230)));
        parts.Children.Add(Part("Track", Pick(SessionNames.Merge(rule.Track, S.KnownTracks.Append(f.Session.TrackDisplayName), SessionNames.Tracks), rule.Track, "Any track", v => rule.Track = v, 250)));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(parts);
        var remove = Ui.IconButton("I.Trash", "Remove this rule", () =>
        {
            S.Rules.Remove(rule);
            Changed();
            RefreshStatus();
            _rules?.Fill();
            if (_rulesEmpty is not null) _rulesEmpty.Visibility = S.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }, "QuietIconButton");
        remove.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(remove, 1);
        grid.Children.Add(remove);
        var tile = new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(12, 8, 8, 4), BorderThickness = new Thickness(1), Child = grid };
        tile.SetResourceReference(Border.BorderBrushProperty, "Line");
        return tile;
    }

    internal static void NewLayoutBox(Button anchor, RaceHudSettings settings, HudLayout? current, Action<HudLayout> add)
    {
        var name = new TextBox { Text = settings.UniqueName(current is null ? "Layout" : current.Name + " copy"), Width = 260, MaxLength = Ui.NameLength };
        var clean = new CheckBox { Content = "Clean screen", IsChecked = current is null, IsEnabled = current is not null, Margin = new Thickness(0, 12, 0, 0) };
        var cleanNote = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, 2, 0, 0), MaxWidth = 300 };
        void Note()
        {
            cleanNote.Text = clean.IsChecked == true ? "Starts empty: add the widgets you want." : $"Starts as a copy of “{current?.Name}”.";
            if (clean.IsChecked == true && name.Text == settings.UniqueName(current?.Name + " copy")) name.Text = settings.UniqueName("Layout");
            else if (clean.IsChecked != true && name.Text == settings.UniqueName("Layout") && current is not null) name.Text = settings.UniqueName(current.Name + " copy");
        }
        Note();
        clean.Click += (_, _) => Note();
        var error = new TextBlock { Foreground = Ui.Res<Brush>("DangerDeep"), TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "New layout", Style = Ui.Res<Style>("H3"), Margin = new Thickness(0, 0, 0, 10) });
        stack.Children.Add(new TextBlock { Text = "Name", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(name);
        stack.Children.Add(error);
        stack.Children.Add(clean);
        stack.Children.Add(cleanNote);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Ui.LabeledButton("I.X", "Cancel", () => { }, "GhostButton");
        var save = Ui.LabeledButton("I.Check", "Save", () => { }, "PrimaryButton");
        save.Margin = new Thickness(8, 0, 0, 0);
        save.IsDefault = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        stack.Children.Add(buttons);

        var box = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(18, 14, 18, 16), Margin = new Thickness(0, 6, 0, 0), Child = stack },
        };
        RenderOptions.SetClearTypeHint(box.Child, ClearTypeHint.Enabled);
        Ui.FollowTarget(box);
        void Save()
        {
            string wanted = name.Text.Trim();
            if (wanted.Length == 0 || settings.Layout(wanted) is not null)
            {
                error.Text = wanted.Length == 0 ? "Give it a name." : "A layout with that name already exists.";
                error.Visibility = Visibility.Visible;
                return;
            }
            HudLayout layout;
            if (clean.IsChecked == true || current is null) layout = new HudLayout { Name = wanted };
            else
            {
                layout = current.Copy(wanted);
                foreach (var w in layout.Widgets) w.Id = HudWidgetConfig.NewId(w.Kind);
            }
            box.IsOpen = false;
            add(layout);
        }
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => box.IsOpen = false;
        name.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { Save(); e.Handled = true; }
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) { box.IsOpen = false; e.Handled = true; }
        };
        box.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        box.IsOpen = true;
    }

    private void AddLayout(HudLayout layout)
    {
        S.Layouts.Add(layout);
        S.ActiveLayout = layout.Name;
        Host?.UseLayout(layout.Name);
        Changed();
        Build();
    }

    private void RenameLayout()
    {
        string old = ShownLayout;
        var box = new TextBox { Text = old, Width = 320, MaxLength = Ui.NameLength };
        var error = new TextBlock();
        var buttons = Ui.DialogButtons(_window, "Save", () =>
        {
            if (!S.RenameLayout(old, box.Text)) { error.Text = "That name is empty, already used or the default's."; return false; }
            Changed();
            Build();
            return true;
        });
        _ = _window.ShowModal(Ui.Dialog(_window, "Rename layout", "Rules that pick it follow the new name.", Ui.FieldRow("Name", box), buttons, error, 480));
        box.Focus();
        box.SelectAll();
    }

    private StackPanel HudSettingsTab() => HudSettings(_app, Changed, Build, RefreshStatus);

    internal static StackPanel HudSettings(AppServices app, Action changed, Action rebuild, Action? statusChanged)
    {
        var S = app.Config.SimRacing.Hud;
        HudHost? Host() => app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin { Hud: { } hud } ? hud : null;
        var a = S.Appearance;
        var root = new StackPanel();
        var custom = new StackPanel { Visibility = a.Colors == HudColors.Custom ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        var colors = Ui.Segmented(new[] { "Default", "Custom" }, (int)a.Colors, i =>
        {
            a.Colors = (HudColors)i;
            custom.Visibility = a.Colors == HudColors.Custom ? Visibility.Visible : Visibility.Collapsed;
            changed();
        }, "hudcolors");
        FillCustomColours(custom, S, changed, rebuild);
        var when = Ui.Combo(new[]
        {
            new Choice(nameof(HudShowWhen.Driving), "In the car"),
            new Choice(nameof(HudShowWhen.Connected), "Whenever a session is loaded"),
            new Choice(nameof(HudShowWhen.Always), "Always"),
        }, S.ShowWhen.ToString(), c => { S.ShowWhen = Enum.Parse<HudShowWhen>(c.Id!); changed(); statusChanged?.Invoke(); }, 240);
        root.Children.Add(Group("Overlay", null,
            Ui.SettingRow("Show the widgets", null, when),
            Ui.SettingRow("Click-through", "Clicks go to the game. Off: click a driver in the Relative or Standings to highlight them in both (the window never takes the keyboard).",
                Ui.Switch(!S.Interactive, on => { S.Interactive = !on; changed(); }))));
        root.Children.Add(new KeybindTree(app, e => e.Id.StartsWith("hud.", StringComparison.Ordinal), skip: 1) { Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(Group("Look", "The overlay keeps its own colors whatever the app's theme; flags keep their real colors.",
            Ui.SettingRow("Colors", null, colors),
            custom,
            Ui.SettingRow("Background", null, Ui.SliderWithValue(0, 100, a.Background, "{0:0}%", v => { a.Background = (int)v; changed(); })),
            Ui.SettingRow("Corners", null, Ui.SliderWithValue(0, 16, a.Corner, "{0:0}", v => { a.Corner = (int)v; changed(); })),
            Ui.SettingRow("Outline", null, Ui.Switch(a.Outline, on => { a.Outline = on; changed(); })),
            Ui.SettingRow("Units", null, Ui.Segmented(new[] { "Metric", "Imperial" }, (int)S.Units, i => { S.Units = (HudUnits)i; changed(); }, "hudunits"))));
        var grid = Ui.SliderWithValue(2, 64, S.GridSize, "{0:0} px", v =>
        {
            S.GridSize = (int)v;
            changed();
            Host()?.ShowGrid();
        }, step: 2);
        root.Children.Add(Group("Arranging on screen", null,
            Ui.SettingRow("Snap while arranging", "Edges line up with other widgets and the screen, else a grid. Hold Alt to place freely.", Ui.Switch(S.Snap, on => { if (Host() is { } host) host.SetSnap(on); else S.Snap = on; changed(); })),
            Ui.SettingRow("Grid", app.Demo ? "The grid is shown over your screens while you adjust it (not with demo data)." : "Shown over your screens while you adjust it.", grid)));

        return root;
    }

    private static void FillCustomColours(StackPanel into, RaceHudSettings S, Action changed, Action rebuild)
    {
        var c = S.Appearance.Custom;
        var groups = new (string Title, (string Label, string Help, string Id)[] Rows)[]
        {
            ("Panels and text", new[]
            {
                ("Background", "Behind the panels", nameof(HudCustomColors.Background)),
                ("Panels", "Rows, cells and boxes", nameof(HudCustomColors.Surface)),
                ("Lines", "Outlines and dividers", nameof(HudCustomColors.Lines)),
                ("Text", "Names and values", nameof(HudCustomColors.Text)),
                ("Headings", "Titles and big figures", nameof(HudCustomColors.Heading)),
                ("Secondary text", "Labels and units", nameof(HudCustomColors.Muted)),
            }),
            ("Highlights", new[]
            {
                ("Accent", "Highlights, your car on the map", nameof(HudCustomColors.Accent)),
                ("Your row", "Your row in Relative and Standings", nameof(HudCustomColors.YourRow)),
                ("Your row text", "Text on your row", nameof(HudCustomColors.YourRowText)),
                ("Good", "Gains, faster", nameof(HudCustomColors.Good)),
                ("Warning", "Cautions, low fuel", nameof(HudCustomColors.Warn)),
                ("Danger", "Losses, critical alerts", nameof(HudCustomColors.Danger)),
            }),
            ("Timing", new[]
            {
                ("Fastest lap", "Fastest laps and personal bests", nameof(HudCustomColors.FastestLap)),
                ("Lapping you", "Cars a lap or more ahead", nameof(HudCustomColors.Lapping)),
                ("Lapped", "Cars a lap or more behind", nameof(HudCustomColors.Lapped)),
            }),
        };
        var entries = c.All().ToDictionary(e => e.Id, e => e);
        var grid = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
        foreach (var (title, rows) in groups)
        {
            grid.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 6, 0, 6) });
            var wrap = new WrapPanel();
            foreach (var (label, help, id) in rows)
            {
                var (_, get, set) = entries[id];
                var cell = new StackPanel { Width = 200, Margin = new Thickness(0, 0, 14, 8) };
                cell.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 3), ToolTip = help });
                cell.Children.Add(ColorField.Build(get, hex => { set(hex); changed(); return true; }, help, 92));
                wrap.Children.Add(cell);
            }
            grid.Children.Add(wrap);
        }
        into.Children.Add(grid);
        var reset = Ui.LabeledButton("I.Undo", "Start from Default", () =>
        {
            S.Appearance.Custom = new HudCustomColors();
            changed();
            rebuild();
        }, "GhostButton");
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 0, 0, 8);
        into.Children.Add(reset);
    }

    private ArrangeList? _alertGroups;
    private Action? _alertFoldChanged;

    private StackPanel AlertsTab()
    {
        var root = new StackPanel();
        _alertGroups = new ArrangeList(
            () => AlertCatalog.Groups.Select(g => (g.Id, g.Title, (Func<FrameworkElement>)(() => AlertGroup(g)))).ToList(),
            () => S.AlertGroupOrder,
            order => { S.AlertGroupOrder = order; Changed(); },
            "Arrange", "Drag the groups into the order you want them here");
        var foldAll = new CollapseAllButton { Margin = new Thickness(0, 0, 8, 0) };
        void CountOpen() => foldAll.SetOpen(AlertCatalog.Groups.Count(g => !S.AlertGroupsFolded.Contains(g.Id, StringComparer.OrdinalIgnoreCase)), AlertCatalog.Groups.Count());
        foldAll.Requested += fold =>
        {
            S.AlertGroupsFolded = fold ? AlertCatalog.Groups.Select(g => g.Id).ToList() : new List<string>();
            _app.Save();
            _alertGroups?.Fill();
            CountOpen();
        };
        _alertFoldChanged = CountOpen;
        CountOpen();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(foldAll);
        buttons.Children.Add(_alertGroups.Button);
        var head = Ui.SectionHeader("Alerts", "Shown in the Alerts widget. Not raised again within its cooldown; sticky ones stay until the condition clears.", buttons);
        head.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(head);
        _alertGroups.Fill();
        root.Children.Add(_alertGroups.View);
        return root;
    }

    private FrameworkElement AlertGroup((string Id, string Title, string Icon, string[] Alerts) group)
    {
        var defs = group.Alerts.Select(AlertCatalog.Find).OfType<AlertDef>().ToList();
        bool folded = S.AlertGroupsFolded.Contains(group.Id, StringComparer.OrdinalIgnoreCase);
        var count = new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        void Count() => count.Text = $"{defs.Count(d => RaceState.ChoiceFor(S, d).Enabled)} of {defs.Count} on";
        Count();
        var rows = new StackPanel { Margin = new Thickness(10, 0, 14, 10), Visibility = folded ? Visibility.Collapsed : Visibility.Visible };
        for (int i = 0; i < defs.Count; i++)
        {
            if (i > 0) rows.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 2, 0, 2), Background = Ui.Res<Brush>("Line") });
            rows.Children.Add(AlertRow(defs[i], Count));
        }
        var chevron = new Icon { Data = Ui.Res<Geometry>(folded ? "I.ChevronRight" : "I.ChevronDown"), Width = 15, Height = 15, Foreground = Ui.Res<Brush>("TextMuted"), VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel { LastChildFill = true };
        header.Children.Add(chevron);
        var badge = Ui.Badge(group.Icon, "AccentSoft", "AccentText", 28, 8, 15);
        badge.Margin = new Thickness(8, 0, 10, 0);
        header.Children.Add(badge);
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(count);
        header.Children.Add(new TextBlock { Text = ThemeManager.Case(group.Title), Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center });
        var bar = new Button { Style = Ui.Res<Style>("QuietButton"), Content = header, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16, 14, 22, 14) };
        Controls.ClickGuard.SetAllowRepeat(bar, true);
        System.Windows.Automation.AutomationProperties.SetName(bar, group.Title);
        bar.Click += (_, _) =>
        {
            bool fold = rows.Visibility == Visibility.Visible;
            rows.Visibility = fold ? Visibility.Collapsed : Visibility.Visible;
            chevron.Data = Ui.Res<Geometry>(fold ? "I.ChevronRight" : "I.ChevronDown");
            S.AlertGroupsFolded.RemoveAll(x => string.Equals(x, group.Id, StringComparison.OrdinalIgnoreCase));
            if (fold) S.AlertGroupsFolded.Add(group.Id);
            _app.Save();
            _alertFoldChanged?.Invoke();
        };
        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(rows);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Child = stack };
    }

    private const double ThresholdColumn = 210, SwitchColumn = 56;

    private FrameworkElement AlertRow(AlertDef def, Action changed)
    {
        var choice = RaceState.ChoiceFor(S, def);
        HudAlertSetting Mine() => S.Alerts.TryGetValue(def.Id, out var x) ? x : S.Alerts[def.Id] = new HudAlertSetting();
        var grid = new Grid { Margin = new Thickness(30, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ThresholdColumn) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SwitchColumn) });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        var title = new TextBlock { Text = def.Title, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        text.Children.Add(title);
        text.Children.Add(new TextBlock { Text = def.Description, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        grid.Children.Add(text);
        NumberBox? box = null;
        if (def.Threshold is not null)
        {
            box = new NumberBox(choice.Threshold ?? def.Threshold.Value, def.ThresholdUnit, 60)
            {
                Minimum = 0, Maximum = 1000, Step = def.Threshold.Value < 5 ? 0.5 : 1, Decimals = def.Threshold.Value % 1 == 0 ? 0 : 1,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 14, 0),
                ToolTip = "When it's raised",
            };
            box.ValueChanged += v => { Mine().Threshold = v; Changed(); };
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
        }
        var on = Ui.Switch(choice.Enabled, v =>
        {
            Mine().Enabled = v;
            Changed();
            Apply(v);
            changed();
        });
        on.HorizontalAlignment = HorizontalAlignment.Right;
        on.VerticalAlignment = VerticalAlignment.Center;
        on.ToolTip = "Raise this alert";
        System.Windows.Automation.AutomationProperties.SetName(on, def.Title);
        Grid.SetColumn(on, 2);
        grid.Children.Add(on);
        void Apply(bool enabled)
        {
            text.Opacity = enabled ? 1 : 0.55;
            if (box is not null) box.IsEnabled = enabled;
        }
        Apply(choice.Enabled);
        return grid;
    }

    private StackPanel FuelTab()
    {
        var root = new StackPanel();
        var fuel = S.Fuel;
        NumberBox Box(double value, string unit, double min, double max, double step, int decimals, Action<double> set)
        {
            var b = new NumberBox(value, unit, 70) { Minimum = min, Maximum = max, Step = step, Decimals = decimals };
            b.ValueChanged += v => { set(v); Changed(); RefreshFormulas(); };
            return b;
        }
        root.Children.Add(Group("Fuel & strategy", null,
            Ui.SettingRow("Laps measured", "Fuel per lap comes from this many recent clean laps under green; newer laps count more and a lap far from the rest is left out.", Box(fuel.Window, "laps", 1, 30, 1, 0, v => fuel.Window = (int)v)),
            Ui.SettingRow("Safety reserve", "Extra fuel for how much your laps vary, how sure the average is and how fuel use drifts over a stint (at least 1 / 1.5 / 2% of the fuel to the flag): Aggressive ≈ enough 9 times in 10, Balanced ≈ 19 in 20, Conservative ≈ 99 in 100. A model, not a guarantee; it needs 2 clean laps.",
                Ui.Segmented(new[] { "Aggressive", "Balanced", "Conservative" }, (int)fuel.Safety, i => { fuel.Safety = (FuelSafety)i; Changed(); RefreshFormulas(); }, "fuelsafety")),
            Ui.SettingRow("Margin at the flag", "Your own fuel kept in hand on top of the safety reserve, in laps.", Box(fuel.MarginLaps, "laps", 0, 10, 0.1, 1, v => fuel.MarginLaps = v)),
            Ui.SettingRow("Fixed reserve", "Liters kept on top of everything else (added to your margin).", Box(fuel.ReserveLiters, "L", 0, 50, 0.1, 1, v => fuel.ReserveLiters = v)),
            Ui.SettingRow("Extra consumption", "This share of the expected fuel to the flag is added, for a pace or style that uses more than your clean laps did.", Box(fuel.ExtraPercent, "%", 0, 25, 0.5, 1, v => fuel.ExtraPercent = v)),
            Ui.SettingRow("Extra distance", "Distance on top of the race (a long pit lane, the lap after the flag), turned into laps of this track.", Box(fuel.ExtraKm, "km", 0, 50, 0.1, 1, v => fuel.ExtraKm = v)),
            Ui.SettingRow("A pit stop costs", "Lane plus standing time, for strategy notes.", Box(fuel.PitLaneLossSeconds, "s", 0, 300, 1, 0, v => fuel.PitLaneLossSeconds = v)),
            Ui.SettingRow("Longest stint", "The Stint time alert warns before it (0 = no limit).", Box(fuel.MaxStintMinutes, "min", 0, 600, 5, 0, v => fuel.MaxStintMinutes = v))));

        _formulas = new StackPanel();
        _formulaNote = Caption("", 0);
        var card = Group("How the numbers are worked out", "The fuel widget's sums, with the numbers from the session running now (or the simulated one). Every figure to the flag is an estimate.", _formulaNote, _formulas);
        Ui.Anchor(card, "simracing.hud.fuelformulas");
        root.Children.Add(card);
        RefreshFormulas();
        _tick = RefreshFormulas;
        return root;
    }

    private StackPanel? _formulas;
    private TextBlock? _formulaNote;

    private void RefreshFormulas()
    {
        if (_formulas is null || _formulaNote is null) return;
        var race = Preview.Race;
        var f = race.Frame;
        var u = S.Units;
        var plan = race.Fuel;
        string F(double? litres) => litres is { } l ? RaceFormat.Fuel(l, u) : "–";
        string L(double? laps) => laps is { } l ? $"{l:0.0} laps" : "–";
        var est = race.FuelData.Estimate;
        double? togo = race.LapsToGo(f);
        double floor = GoodieBag.SimRacing.FuelEngine.FuelStrategyCalculator.MinimumReserveFraction(new GoodieBag.SimRacing.FuelEngine.FuelStrategyRequest
        {
            CurrentFuelLiters = 0, LapsRemaining = 0, Consumption = est, SafetyMode = FuelPlanner.Mode(S.Fuel.Safety),
        });
        string Spread() => est.StandardDeviationLitersPerLap is { } sd ? $"± {RaceFormat.Fuel(sd, u)} a lap" : "needs 2 clean laps";
        var rows = new (string Name, string Formula, string Now)[]
        {
            ("Fuel per lap (measured)", $"From your last {S.Fuel.Window} clean laps under green (no pit road, refuel, full-course caution, formation lap or incident): newer laps count more, and from 5 laps one far from the rest is left out.",
                est.HasEstimate ? $"{RaceFormat.Fuel(est.MeanLitersPerLap, u)} · {est.UsedLapCount} lap{(est.UsedLapCount == 1 ? "" : "s")} · {GoodieBag.SimRacing.Hud.Widgets.FuelWidget.Confidence(est.Reliability)}" : race.FuelData.EnergyUnits ? "kWh car: not supported" : "measuring…"),
            ("Lap to lap", "How much the clean laps differ (weighted spread); it sets the safety reserve.", est.HasEstimate ? Spread() : "–"),
            ("Laps of fuel", "Fuel in the tank ÷ fuel per lap.", plan is { } p1 ? $"{F(f.Fuel)} ÷ {F(p1.PerLap)} = {L(p1.LapsOfFuel)}" : "–"),
            ("Laps to go", "Lap races: total laps − laps done − the part of this lap already driven (− laps the leader is ahead). Timed races (a forecast until iRacing shows the white flag): the leader takes the flag at their first crossing once the clock has run out; you finish at your first crossing after that, at your recent pace.",
                togo is { } tg ? L(tg) + (plan?.LapsToGoForecast == true ? " (forecast)" : "") : "–"),
            ("Rest of this lap", "From the fuel your clean laps used on each tenth of the lap (once 3 clean laps cover what's left of this one); until then this lap's share × fuel per lap.",
                plan?.CurrentLap is { } cl ? $"{F(cl)} ({(plan.CurrentLapFromProfile ? "lap profile" : "share of a lap")})" : "–"),
            ("Expected to the flag", "Rest of this lap + whole laps after it × fuel per lap.", F(plan?.Expected)),
            ("Safety reserve", $"{S.Fuel.Safety}: covers the lap-to-lap spread over the laps to go, the average's own uncertainty, and at least {floor:P1} of the expected fuel for drift over a stint (a model, not a guarantee).",
                plan is null ? "–" : plan.StatisticalReserve > 0 ? F(plan.StatisticalReserve) : "none yet (needs 2 clean laps)"),
            ("Your margin", $"{S.Fuel.MarginLaps:0.#} laps × fuel per lap" + (S.Fuel.ExtraKm > 0 ? $" + {S.Fuel.ExtraKm:0.#} km as laps" : "") + (S.Fuel.ReserveLiters > 0 ? $" + {RaceFormat.Fuel(S.Fuel.ReserveLiters, u)}" : "") + (S.Fuel.ExtraPercent > 0 ? $" + {S.Fuel.ExtraPercent:0.#}% of the fuel to the flag" : "") + ".", plan is null ? "–" : F(plan.Margin)),
            ("Fuel to the flag", "Expected + safety reserve + your margin.", F(plan?.ToFinish)),
            ("Left at the flag", "Fuel in the tank − expected to the flag (no reserve taken off; below zero = you run out first).", plan?.AtFlag is { } at ? (at >= 0 ? F(at) : $"short by {F(-at)}") : "–"),
            ("Surplus / short", "Fuel in the tank − fuel to the flag (below zero = you need to stop).", plan?.Surplus is { } s ? (s >= 0 ? $"+{F(s)} to spare" : $"short by {F(-s)}") : "–"),
            ("Stops", "Shortfall ÷ usable tank, rounded up.", plan?.Stops is { } st ? $"{st}" + (race.Tank is { } tank ? $" (tank {F(tank)})" : "") : "–"),
            ("Add at the next stop", $"The shortfall rounded up to {FuelMath.AddStep:0.0} L, at most a full tank (you arrive close to empty).", F(plan?.AddAtNextStop)),
            ("Pit window", "Opens when the stops still to come can cover the rest: laps to go − (stops × tank − reserve and margin) ÷ fuel per lap. Closes when the fuel runs out (laps of fuel).",
                plan?.WindowOpensIn is { } o ? (o <= 0 ? $"open now · closes in {L(plan.WindowClosesIn)}" : $"opens in {L(o)} · closes in {L(plan.WindowClosesIn)}") : plan?.Stops == 0 ? "no stop needed" : "–"),
        };
        string source = f.Status != SimStatus.Connected ? "Waiting for a session." : f.Simulated ? "Numbers from the simulated session (no iRacing running)." : "Live from iRacing.";
        string? caution = race.FuelData.EnergyUnits ? "This car reports energy (kWh), not fuel: the fuel calculator doesn't run for it." : plan?.Warning ?? FuelPlanner.Warning(est);
        _formulaNote.Text = caution is null ? source : $"{source} {caution}";
        _formulaNote.Foreground = Ui.Res<Brush>(caution is null || f.Status != SimStatus.Connected ? "TextMuted" : "WarnDeep");
        if (_formulas.Children.Count != rows.Length * 2 - 1)
        {
            _formulas.Children.Clear();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) _formulas.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6), Background = Ui.Res<Brush>("Line") });
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
                grid.Children.Add(new TextBlock { Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) });
                var formula = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 16, 0) };
                Grid.SetColumn(formula, 1);
                grid.Children.Add(formula);
                var now = new TextBlock { Style = Ui.Res<Style>("Body"), FontFamily = Ui.Res<FontFamily>("MonoFont"), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right };
                Grid.SetColumn(now, 2);
                grid.Children.Add(now);
                _formulas.Children.Add(grid);
            }
        }
        for (int i = 0; i < rows.Length; i++)
        {
            var g = (Grid)_formulas.Children[i * 2];
            ((TextBlock)g.Children[0]).Text = rows[i].Name;
            ((TextBlock)g.Children[1]).Text = rows[i].Formula;
            ((TextBlock)g.Children[2]).Text = rows[i].Now;
        }
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("hud", () => { SelectTab(WidgetsTab); return Task.CompletedTask; });
        yield return ("hud-rules", () => { _rulesCard?.BringIntoView(); return Task.CompletedTask; });
        yield return ("hud-rules-arrange", () => { _rules?.SetEditing(true); _rulesCard?.BringIntoView(); return Task.CompletedTask; });
        yield return ("hud-rules-done", () => { _rules?.SetEditing(false); return Task.CompletedTask; });
        yield return ("hud-widgets", () => { _widgets?.Select("relative"); return Task.CompletedTask; });
        yield return ("hud-standings", () => { _widgets?.Select("standings"); return Task.CompletedTask; });
        yield return ("hud-standings-every-column", () =>
        {
            var all = GoodieBag.SimRacing.Hud.Widgets.StandingsWidget.Options.First(o => o.Key == "columns").Choices!.Select(c => c.Id);
            _widgets?.SetOption("standings", "columns", string.Join(",", all));
            _widgets?.ShowTab("standings", "content");
            return Task.CompletedTask;
        });
        yield return ("hud-standings-default-columns", () => { _widgets?.SetOption("standings", "columns", null); return Task.CompletedTask; });
        yield return ("hud-standings-scrolled", async () =>
        {
            _widgets?.ShowTab("standings", "display");
            await Task.Delay(300);
            _widgets?.ScrollPageBy(900);
            await Task.Delay(300);
        });
        yield return ("hud-every-widget", () => { _widgets?.DrawEveryKind(); _widgets?.Select("fuel"); return Task.CompletedTask; });
        RaceHudWidgets? quick = null;
        Border? quickBox = null;
        yield return ("hud-quick-editor", () =>
        {
            quick = new RaceHudWidgets(_app, this, sidebar: true);
            quickBox = new Border { Width = 1000, Height = 720, BorderThickness = new Thickness(1), Child = quick.Root, Margin = new Thickness(0, 12, 0, 0) };
            quickBox.SetResourceReference(Border.BackgroundProperty, "Bg");
            quickBox.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
            Children.Add(quickBox);
            quick.ShowTab("relative");
            quickBox.BringIntoView();
            return Task.CompletedTask;
        });
        yield return ("hud-quick-editor-content", () => { quick?.ShowTab("relative", "content"); return Task.CompletedTask; });
        yield return ("hud-quick-editor-visibility", () => { quick?.ShowTab("fuel", "visibility"); return Task.CompletedTask; });
        yield return ("hud-quick-editor-position", () => { quick?.ShowTab("fuel", "position"); return Task.CompletedTask; });
        yield return ("hud-quick-editor-settings", () => { quick?.ShowTab(null); return Task.CompletedTask; });
        yield return ("hud-quick-editor-done", () => { if (quickBox is not null) Children.Remove(quickBox); return Task.CompletedTask; });
        yield return ("hud-alerts", () => { SelectTab(AlertsTabIndex); return Task.CompletedTask; });
        yield return ("hud-alerts-arrange", () => { _alertGroups?.SetEditing(true); return Task.CompletedTask; });
        yield return ("hud-alerts-done", () => { _alertGroups?.SetEditing(false); return Task.CompletedTask; });
        yield return ("hud-fuel", () => { SelectTab(FuelTabIndex); return Task.CompletedTask; });
        yield return ("hud-settings", () => { SelectTab(SettingsTab); return Task.CompletedTask; });
        yield return ("hud-settings-custom", () =>
        {
            S.Appearance.Colors = HudColors.Custom;
            Build();
            return Task.CompletedTask;
        });
        yield return ("hud-settings-back", () => { S.Appearance.Colors = HudColors.Default; SelectTab(WidgetsTab); return Task.CompletedTask; });
    }

    public bool CancelEdit() => _rules?.Cancel() == true || _alertGroups?.Cancel() == true;

    private void SelectTab(int tab)
    {
        if (_segments.Child is Panel panel && panel.Children[tab] is RadioButton rb) rb.IsChecked = true;
        _tab = tab;
        Build();
    }
}

internal sealed class PreviewSource
{
    private readonly DemoTelemetry _demo = new();
    private readonly TelemetryFrame _frame = new();
    private long _seq;

    public PreviewSource(Func<RaceHudSettings> settings)
    {
        Simulated = new RaceState(settings);
        for (double t = -WarmUp; t < 0; t += 0.5)
        {
            _demo.Fill(_frame, t);
            _frame.Sequence = ++_seq;
            Simulated.Update(_frame);
        }
        Advance(null);
    }

    private const double WarmUp = 6 * 110;

    public RaceState Simulated { get; }

    public RaceState Race { get; private set; } = null!;

    public void Advance(HudHost? host)
    {
        if (host?.Race.Frame is { Status: SimStatus.Connected })
        {
            Race = host.Race;
            return;
        }
        _demo.Fill(_frame);
        _frame.Sequence = ++_seq;
        Simulated.Update(_frame);
        Race = Simulated;
    }
}

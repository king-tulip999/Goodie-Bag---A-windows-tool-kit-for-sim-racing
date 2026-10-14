using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.SimRacing.Setups;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class SetupManagerSection : StackPanel
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly SetupStore _store;
    private readonly Border _intro = new(), _inbox = new(), _filters = new(), _classes = new(), _explorer = new();
    private readonly StackPanel _carList = new();
    private readonly ScrollViewer _carScroll;
    private readonly TextBox _search;
    private readonly Button _sort;
    private readonly List<(Button Button, SetupNode Folder)> _crumbs = new();
    private ScrollViewer? _treeScroll;

    private int _view;
    private bool _hasSetups, _newOnly;
    private readonly HashSet<string> _classFilter;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _target = new(StringComparer.OrdinalIgnoreCase);
    private string? _anchor, _lastClick;
    private DateTime _lastClickAt;
    private bool _loaded;

    public SetupManagerSection(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        _classFilter = new HashSet<string>(S.ClassFilter, StringComparer.OrdinalIgnoreCase);
        _store = new SetupStore(S, app.Demo);
        _store.Changed += () => { Prune(); RenderAll(); };

        Children.Add(_intro);
        _inbox.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(_inbox);

        var card = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(16) };
        var stack = new StackPanel();
        card.Child = stack;
        Children.Add(card);

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search = new TextBox { Tag = "Search cars, folders or setups…", MinWidth = 220 };
        _search.TextChanged += (_, _) => { RenderCars(); RenderExplorer(); };
        top.Children.Add(_search);
        var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _sort = new Button { Style = Ui.Res<Style>("Btn"), Padding = new Thickness(12, 3, 10, 3), MinWidth = 200, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        _sort.Click += (_, _) => OpenSortMenu();
        Ui.Anchor(_sort, "simracing.setups.sort");
        UpdateSortButton();
        sortRow.Children.Add(_sort);
        Grid.SetColumn(sortRow, 1);
        top.Children.Add(sortRow);
        stack.Children.Add(top);

        _filters.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(_filters);
        _classes.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(_classes);

        var split = new Grid { Margin = new Thickness(0, 14, 0, 0), Height = 760 };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        split.ColumnDefinitions.Add(new ColumnDefinition());
        _carScroll = new ScrollViewer { Content = _carList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4, 4, 8, 4) };
        split.Children.Add(Pane(_carScroll));
        Grid.SetColumn(_explorer, 2);
        split.Children.Add(_explorer);
        stack.Children.Add(split);

        Focusable = true;
        FocusVisualStyle = null;
        PreviewKeyDown += OnKey;
    }

    private SetupManagerSettings S => _app.Config.SimRacing.Setups;

    private static Tile Pane(UIElement child)
    {
        var pane = new Tile { Style = Ui.Res<Style>("Inset"), BorderThickness = new Thickness(1), Child = child };
        pane.SetResourceReference(Border.BorderBrushProperty, "InputEdge");
        return pane;
    }

    public void OnShown()
    {
        _shown = true;
        if (_loaded) _store.Watch(true);
        _ = LoadAsync();
        if (!_loaded) RenderAll();
    }

    public void OnHidden()
    {
        _shown = false;
        _store.Watch(false);
    }

    private bool _shown;

    private async Task LoadAsync()
    {
        bool changed = await _store.LoadAsync();
        if (!_shown) return;
        bool first = !_loaded;
        _loaded = true;
        if (first) _store.Watch(true);
        if (!changed && !first) return;
        Prune();
        RenderAll();
    }

    private void Prune()
    {
        _selected.RemoveWhere(k => _store.Find(k) is null);
        _open.RemoveWhere(k => _store.Find(k) is null);
        foreach (var dir in _target.Keys.ToList())
            if (_store.Find(_target[dir]) is not { IsFolder: true }) _target.Remove(dir);
    }

    private void Save() => _app.Save();

    private void RenderAll()
    {
        RenderIntro();
        RenderInbox();
        _classFilter.RemoveWhere(cls => !_store.Cars.Any(c => c.Class == cls));
        RenderFilters();
        RenderClasses();
        RenderCars();
        RenderExplorer();
    }

    private void RenderIntro()
    {
        if (S.IntroSeen) { _intro.Child = null; _intro.Visibility = Visibility.Collapsed; return; }
        _intro.Visibility = Visibility.Visible;
        _intro.Margin = new Thickness(0, 0, 0, 12);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.Info"), Width = 18, Height = 18, Foreground = Ui.Res<Brush>("AccentText"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = "Your iRacing setups, without File Explorer", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold });
        foreach (var line in new[]
        {
            "Pick a car on the left to see its setup folder. Search finds cars, folders and setup names.",
            "Rename, move, import and share here: the changes happen to the real files in Documents\\iRacing\\setups, so iRacing sees them right away.",
            "Deletes go to the Recycle Bin. Names, classes and orders you change here are only how Goodie Bag shows things; iRacing's folders keep their names.",
        })
            text.Children.Add(new TextBlock { Text = "• " + line, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var ok = Ui.LabeledButton("I.Check", "Got it", () => { S.IntroSeen = true; Save(); RenderIntro(); });
        ok.VerticalAlignment = VerticalAlignment.Top;
        ok.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(ok, 2);
        grid.Children.Add(ok);
        _intro.Child = new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(14, 12, 14, 12), Child = grid };
    }

    private void RenderInbox()
    {
        var items = _store.Inbox;
        if (!_loaded) { _inbox.Child = null; _inbox.Visibility = Visibility.Collapsed; return; }
        _inbox.Visibility = Visibility.Visible;
        var stack = new StackPanel();
        var head = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Ui.Badge("I.Download", "AccentSoft", "AccentText", 30, 8, 16));
        var title = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var label = new TextBlock { Text = "Downloads", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(label, Dock.Left);
        title.Children.Add(label);
        string found = items.Count > 0 ? $"{SetupTree.Plural(items.Count, "new setup file")} found, ready to move into iRacing" : "No new setups or share packs found";
        title.Children.Add(new TextBlock
        {
            Text = found, ToolTip = found, Margin = new Thickness(12, 0, 0, 0),
            Style = Ui.Res<Style>("Muted"), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(title, 1);
        head.Children.Add(title);
        var scan = Ui.LabeledButton("I.Refresh", "Scan Downloads", () =>
        {
            _store.ScanInbox();
            S.InboxFolded = false;
            Save();
            RenderInbox();
            _app.Notify("Downloads scanned", _store.Inbox.Count == 0 ? "No new setups or share packs found." : $"{SetupTree.Plural(_store.Inbox.Count, "setup file")} ready to file.", NoticeKind.Info);
        }, "GhostButton");
        scan.ToolTip = "Look through " + (_store.Downloads ?? "Downloads") + " again";
        scan.Margin = new Thickness(12, 0, 10, 0);
        scan.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(scan, 2);
        head.Children.Add(scan);
        var chevron = new Icon { Data = Ui.Res<Geometry>(S.InboxFolded ? "I.ChevronRight" : "I.ChevronDown"), Width = 14, Height = 14, Foreground = Ui.Res<Brush>("TextMuted"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 3);
        head.Children.Add(chevron);
        head.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInButton(d)) return;
            S.InboxFolded = !S.InboxFolded;
            Save();
            RenderInbox();
        };
        stack.Children.Add(head);

        if (!S.InboxFolded)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Goodie Bag watches your Downloads folder for iRacing setups (.sto) and share packs (.zip) from league mates or setup shops. Each one is listed with the car it looks like it's for. File it moves the file into that car's iRacing setups folder, so it shows up in the iRacing garage. Change the car if the guess is wrong, or press × to leave the file where it is.",
                Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6),
            });
            if (items.Count == 0)
                stack.Children.Add(new TextBlock
                {
                    Text = _store.Downloads is { } folder && Directory.Exists(folder)
                        ? $"Nothing from the last {SetupStore.InboxDays} days in {folder}. New downloads show up here on their own; Scan Downloads looks again."
                        : "Your Downloads folder wasn't found.",
                    Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>("TextFaint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
                });
            var choices = _store.Cars.OrderBy(c => c.Name, Natural.Comparer).Select(c => new Choice(c.Dir, c.Name)).Prepend(new Choice(null, "Choose the car…")).ToList();
            foreach (var item in items.ToList())
                stack.Children.Add(InboxRow(item, choices));
            int ready = items.Count(i => i.Car is not null);
            if (ready > 1)
            {
                var all = Ui.LabeledButton("I.Check", $"File all {ready} matched", () => FileDownloads(items.Where(i => i.Car is not null).ToList()));
                all.HorizontalAlignment = HorizontalAlignment.Right;
                all.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(all);
            }
        }
        _inbox.Child = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(14, 12, 14, 12), Child = stack };
    }

    private FrameworkElement InboxRow(DownloadItem item, List<Choice> choices)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Icon { Data = Ui.Res<Geometry>(item.IsPack ? "I.Layers" : "I.FileText"), Width = 16, Height = 16, Foreground = Ui.Res<Brush>("TextMuted"), VerticalAlignment = VerticalAlignment.Center });
        var name = new StackPanel { Margin = new Thickness(10, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(TextCase.Verbatim(new TextBlock { Text = item.Name, Style = Ui.Res<Style>("Body"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = TextCase.VerbatimTip(item.Path) }));
        string what = item.IsPack ? $"Share pack · {SetupTree.Plural(item.Entries.Count, "setup")}" : SetupTree.Size(item.Size);
        name.Children.Add(new TextBlock { Text = $"{what} · {Ago(item.Modified)}", Style = Ui.Res<Style>("Caption") });
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        FrameworkElement car;
        if (item.PackCars.Count > 0)
            car = new TextBlock
            {
                Text = "→ " + string.Join(", ", item.PackCars.Select(d => _store.Car(d)?.Name ?? d)), Style = Ui.Res<Style>("Muted"),
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = "The pack says which car each setup is for",
            };
        else
        {
            car = Ui.Combo(choices, item.Car, c => { item.Car = c.Id; RenderInbox(); }, 240);
            car.ToolTip = item.Car is null
                ? "The file name doesn't say clearly which car this is for. Choose it here."
                : "Picked from the file name when it says clearly which car. Change it if it isn't right.";
        }
        Grid.SetColumn(car, 2);
        grid.Children.Add(car);
        var file = Ui.LabeledButton("I.Check", "File it", () => FileDownloads(new List<DownloadItem> { item }), "PrimaryButton");
        file.IsEnabled = item.Car is not null;
        file.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(file, 3);
        grid.Children.Add(file);
        var ignore = Ui.IconButton("I.X", "Leave it in Downloads (not offered again)", () => { _store.Ignore(item); Save(); RenderInbox(); }, "QuietIconButton");
        ignore.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(ignore, 4);
        grid.Children.Add(ignore);
        return grid;
    }

    private void FileDownloads(List<DownloadItem> items)
    {
        var added = new List<SetupNode>();
        foreach (var item in items)
        {
            var (nodes, error) = _store.FileDownload(item);
            added.AddRange(nodes);
            if (error is not null) _app.Notify("Couldn't file " + item.Name, error, NoticeKind.Warning);
        }
        Save();
        if (added.Count > 0)
        {
            Reveal(added);
            _app.Notify("Setups filed", $"{SetupTree.Plural(added.Count, "setup")} moved into iRacing's setups folder.", NoticeKind.Success);
        }
        RenderAll();
    }

    private static string Ago(DateTime t)
    {
        var span = DateTime.Now - t;
        return span.TotalHours < 1 ? "just now" : span.TotalHours < 24 ? $"{(int)span.TotalHours} h ago" : $"{(int)span.TotalDays} d ago";
    }

    private void RenderFilters()
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var listed = _store.Cars.Where(c => !S.IsClassHidden(c.Class)).ToList();
        int all = listed.Count(c => !Hidden(c)), fav = listed.Count(c => Fav(c) && !Hidden(c)), hidden = listed.Count(Hidden);
        var view = Ui.Segmented(new[] { $"All {all}", $"Favorites {fav}", $"Hidden {hidden}" }, _view, i => { _view = i; RenderFilters(); RenderCars(); RenderExplorer(); }, "setupview",
            new[] { "Every car except hidden ones", "Cars you favorited", "Cars you hid from this list. Their files are untouched." });
        view.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(view);
        var setups = Ui.Segmented(new[] { "Any car", "Has setups", "New setups" }, _newOnly ? 2 : _hasSetups ? 1 : 0, i =>
        {
            _hasSetups = i == 1;
            _newOnly = i == 2;
            RenderFilters(); RenderCars(); RenderExplorer();
        }, "setupshave", new[] { "Every car", "Only cars with at least one setup", "Cars with setups changed in the last 3 days" });
        setups.VerticalAlignment = VerticalAlignment.Center;
        setups.Margin = new Thickness(16, 0, 0, 0);
        row.Children.Add(setups);
        if (Filtering)
        {
            var clear = new Button { Content = "Clear filters", Style = Ui.Res<Style>("GhostButton"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            clear.Click += (_, _) => { _search.Text = ""; _view = 0; _hasSetups = _newOnly = false; SetClassFilter(() => _classFilter.Clear()); RenderAll(); };
            row.Children.Add(clear);
        }
        _filters.Child = row;
    }

    private void SetClassFilter(Action change)
    {
        change();
        S.ClassFilter = _classFilter.ToList();
        Save();
    }

    private bool Filtering => _search.Text.Trim().Length > 0 || _view != 0 || _hasSetups || _newOnly || _classFilter.Count > 0;

    private List<string> ClassOrder => SetupCars.Order(S.ClassOrder);

    private void RenderClasses()
    {
        var present = ClassOrder.Where(k => _store.Cars.Any(c => c.Class == k) && !S.IsClassHidden(k)).ToList();
        var grid = new ReorderGrid { Flow = true, MinCellWidth = 52, Gap = 6 };
        foreach (var cls in present)
        {
            var (lit, on) = SetupCars.FilterLook(cls, _classFilter);
            var name = new TextBlock
            {
                Text = ThemeManager.Case(cls), VerticalAlignment = VerticalAlignment.Center,
                FontWeight = on ? FontWeights.Bold : FontWeights.SemiBold, Foreground = Ui.Res<Brush>(on ? "AccentText" : lit ? "Text" : "TextFaint"),
            };
            var tile = new Tile
            {
                Tag = cls, Height = 30, CornerRadius = ThemeManager.Round(6), BorderThickness = new Thickness(on ? 2 : 1), Padding = new Thickness(12, 0, 12, 0),
                Background = Ui.Res<Brush>(on ? "AccentSoft" : "SurfaceAlt"), BorderBrush = Ui.Res<Brush>(on ? "AccentText" : "Line"),
                ToolTip = on ? $"Showing {cls}. Click to stop filtering by it; drag to change the class order."
                    : $"Show only {cls} (click more classes to add them). Drag to change the class order. Hide a class in the Sort menu.",
                Child = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { name } },
            };
            Tile.SetChamfer(tile, true);
            tile.MouseLeftButtonUp += (_, _) =>
            {
                SetClassFilter(() => { if (!_classFilter.Remove(cls)) _classFilter.Add(cls); });
                RenderFilters(); RenderClasses(); RenderCars(); RenderExplorer();
            };
            grid.Children.Add(tile);
        }
        grid.Moved += (_, _) =>
        {
            var shown = grid.Children.OfType<FrameworkElement>().Select(e => (string)e.Tag).ToList();
            S.ClassOrder = SetupCars.Reorder(ClassOrder, shown);
            S.Sort = "class";
            UpdateSortButton();
            Save();
            RenderCars();
        };

        bool picked = _classFilter.Count > 0 || S.HiddenClasses.Count > 0;
        var all = Ui.LabeledButton("I.Layers", "Show all classes", () =>
        {
            S.HiddenClasses.Clear();
            SetClassFilter(() => _classFilter.Clear());
            UpdateSortButton();
            RenderFilters(); RenderClasses(); RenderCars(); RenderExplorer();
        });
        all.IsEnabled = picked;
        all.ToolTip = picked ? "List every class again (clears the pick and shows the classes hidden in the Sort menu)" : "Every class is already shown";
        all.VerticalAlignment = VerticalAlignment.Top;
        all.Margin = new Thickness(12, 0, 0, 0);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(grid);
        Grid.SetColumn(all, 1);
        row.Children.Add(all);
        _classes.Child = row;
    }

    private static readonly (string Id, string Label)[] SortChoices =
    {
        ("class", "Class"), ("name", "Name A → Z"), ("name-desc", "Name Z → A"), ("count", "Most setups"), ("recent", "Recently changed"),
    };

    private void UpdateSortButton()
    {
        string label = SortChoices.FirstOrDefault(c => c.Id == S.Sort).Label ?? "Class";
        int hidden = S.HiddenClasses.Count;
        var row = new DockPanel { LastChildFill = true };
        var arrow = new Icon { Data = Ui.Res<Geometry>("I.ChevronDown"), Width = 12, Height = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(arrow, Dock.Right);
        row.Children.Add(arrow);
        row.Children.Add(new TextBlock { Text = $"Sort: {label}" + (hidden > 0 ? $" · {hidden} class{(hidden == 1 ? "" : "es")} hidden" : ""), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        _sort.Content = row;
        _sort.ToolTip = "How the cars are ordered, and which classes are listed (a switch per class)";
    }

    private void OpenSortMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _sort, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = 260 };
        menu.Items.Add(new MenuItem { Header = "Sort by", IsEnabled = false });
        foreach (var (id, label) in SortChoices)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = S.Sort == id };
            item.Click += (_, _) =>
            {
                S.Sort = id;
                Save();
                UpdateSortButton();
                RenderCars();
            };
            menu.Items.Add(item);
        }
        var reset = new MenuItem { Header = "Reset the class order", IsEnabled = S.ClassOrder.Count > 0, ToolTip = "Back to the default order of the classes (after dragging them)" };
        reset.Click += (_, _) =>
        {
            S.ClassOrder.Clear();
            S.Sort = "class";
            Save();
            UpdateSortButton();
            RenderClasses();
            RenderCars();
            _app.Notify("Class order", "The classes are back in their default order.", NoticeKind.Info);
        };
        menu.Items.Add(reset);

        var present = ClassOrder.Where(k => _store.Cars.Any(c => c.Class == k)).ToList();
        if (present.Count > 0)
        {
            menu.Items.Add(new Separator());
            var classes = new MenuItem { Header = "Classes in the list" };
            menu.Items.Add(classes);
            MenuItem? all = null;
            foreach (var cls in present)
            {
                bool shown = !S.IsClassHidden(cls);
                var line = new DockPanel { LastChildFill = true, MinWidth = 200 };
                var sw = Ui.Switch(shown, _ => { });
                sw.IsHitTestVisible = false;
                sw.Focusable = false;
                DockPanel.SetDock(sw, Dock.Right);
                line.Children.Add(sw);
                line.Children.Add(new TextBlock { Text = cls, VerticalAlignment = VerticalAlignment.Center });
                var item = new MenuItem { Header = line, StaysOpenOnClick = true, ToolTip = shown ? $"Hide {cls} and its cars from the list (files untouched)" : $"List {cls} again" };
                item.Click += (_, _) =>
                {
                    bool hide = !S.IsClassHidden(cls);
                    S.SetClassHidden(cls, hide);
                    if (hide) _classFilter.Remove(cls);
                    S.ClassFilter = _classFilter.ToList();
                    Save();
                    sw.IsChecked = !hide;
                    item.ToolTip = hide ? $"List {cls} again" : $"Hide {cls} and its cars from the list (files untouched)";
                    UpdateSortButton();
                    if (all is not null) all.IsEnabled = S.HiddenClasses.Count > 0;
                    RenderFilters(); RenderClasses(); RenderCars(); RenderExplorer();
                };
                classes.Items.Add(item);
            }
            classes.Items.Add(new Separator());
            all = new MenuItem { Header = "Show every class", IsEnabled = S.HiddenClasses.Count > 0 };
            all.Click += (_, _) =>
            {
                S.HiddenClasses.Clear();
                Save();
                UpdateSortButton();
                RenderFilters(); RenderClasses(); RenderCars(); RenderExplorer();
                menu.IsOpen = false;
            };
            classes.Items.Add(all);
        }
        menu.IsOpen = true;
    }

    private bool Fav(SetupCar car) => SetupManagerSettings.Has(S.Favorites, car.Dir);
    private bool Hidden(SetupCar car) => SetupManagerSettings.Has(S.HiddenCars, car.Dir);

    private SetupSearch? Search(SetupCar car) => SetupSearch.For(car, _search.Text);

    private List<SetupCar> VisibleCars()
    {
        var since = _store.NewSince;
        var cars = _store.Cars.Where(c =>
        {
            if (S.IsClassHidden(c.Class)) return false;
            if (_view == 2 ? !Hidden(c) : Hidden(c)) return false;
            if (_view == 1 && !Fav(c)) return false;
            if (_classFilter.Count > 0 && !_classFilter.Contains(c.Class)) return false;
            if (_hasSetups && c.Root.Files == 0) return false;
            if (_newOnly && c.Root.NewCount(since) == 0) return false;
            return Search(c) is not { } m || m.Matches;
        });
        return SetupCars.Sorted(cars, S.Sort, ClassOrder).ToList();
    }

    private SetupCar? SelectedCar(List<SetupCar> visible)
    {
        var car = _store.Car(S.SelectedCar);
        if (car is null || !visible.Contains(car)) car = visible.FirstOrDefault();
        return car;
    }

    private void RenderCars()
    {
        double offset = _carScroll.VerticalOffset;
        _carList.Children.Clear();
        var visible = VisibleCars();
        var selected = SelectedCar(visible);
        if (!_loaded)
        {
            _carList.Children.Add(Empty("Reading your setups…", "Documents\\iRacing\\setups"));
            return;
        }
        if (_store.Root is null && !_store.Demo)
        {
            _carList.Children.Add(Empty("iRacing's setups folder wasn't found", "Goodie Bag looks in Documents\\iRacing\\setups. Start iRacing once and save a setup in the garage, then come back."));
            return;
        }
        if (visible.Count == 0)
        {
            _carList.Children.Add(Empty(_store.Cars.Count == 0 ? "No cars yet" : "No cars match", _store.Cars.Count == 0
                ? "iRacing makes a folder for a car the first time you save a setup for it in the garage."
                : _view == 2 ? "No hidden cars." : S.HiddenClasses.Count > 0 && _store.Cars.All(c => S.IsClassHidden(c.Class))
                    ? "Every class is hidden. Show all classes (next to the class tiles) brings them back."
                    : "Try another search or class, or clear the filters."));
            return;
        }
        var since = _store.NewSince;
        if (S.Sort == "class")
            foreach (var group in visible.GroupBy(c => c.Class))
                _carList.Children.Add(ClassGroup(group.Key, group.ToList(), selected, since));
        else
            _carList.Children.Add(Rows(visible, selected, since, chips: true));
        _carScroll.ScrollToVerticalOffset(offset);
    }

    private FrameworkElement ClassGroup(string cls, List<SetupCar> cars, SetupCar? selected, DateTime since)
    {
        var r = ThemeManager.Round(7);
        var head = new Border
        {
            Background = Ui.Res<Brush>("SurfaceAlt"), Padding = new Thickness(10, 5, 10, 5), BorderBrush = Ui.Res<Brush>("Line"),
            BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(r.TopLeft, r.TopRight, 0, 0),
        };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = ThemeManager.Case(cls), Style = Ui.Res<Style>("Eyebrow"), Foreground = Ui.Res<Brush>("Heading"), FontWeight = FontWeights.Bold });
        title.Children.Add(new TextBlock { Text = "  " + SetupTree.Plural(cars.Count, "car"), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
        head.Child = title;
        var body = new StackPanel();
        body.Children.Add(head);
        var rows = Rows(cars, selected, since, chips: false);
        rows.Margin = new Thickness(2, 2, 2, 2);
        body.Children.Add(rows);
        return new Tile
        {
            Child = body, CornerRadius = r, BorderThickness = new Thickness(1),
            Background = Ui.Res<Brush>("Surface"), BorderBrush = Ui.Res<Brush>("Line"),
            Margin = new Thickness(0, _carList.Children.Count == 0 ? 0 : 10, 0, 0),
        };
    }

    private StackPanel Rows(List<SetupCar> cars, SetupCar? selected, DateTime since, bool chips)
    {
        var stack = new StackPanel();
        for (int i = 0; i < cars.Count; i++)
        {
            if (i > 0)
                stack.Children.Add(new Border
                {
                    Height = 1, Margin = new Thickness(36, 0, 8, 0), Background = Ui.Res<Brush>("Line"),
                    Opacity = cars[i] == selected || cars[i - 1] == selected ? 0 : 0.7,
                });
            stack.Children.Add(CarRow(cars[i], cars[i] == selected, since, chips));
        }
        return stack;
    }

    private FrameworkElement CarRow(SetupCar car, bool selected, DateTime since, bool chip)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool fav = Fav(car), hidden = Hidden(car);
        var star = Ui.IconButton("I.Star", fav ? "Remove from favorites" : "Add to favorites", () =>
        {
            SetupManagerSettings.Set(S.Favorites, car.Dir, !fav);
            Save();
            RenderFilters();
            RenderCars();
        }, "QuietIconButton");
        if (fav && star.Content is Icon starIcon) { starIcon.Foreground = Ui.Res<Brush>("Accent"); starIcon.Fill = Ui.Res<Brush>("Accent"); }
        star.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(star);

        var text = new StackPanel { Margin = new Thickness(6, 6, 6, 6), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = car.Name, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = car.Name });
        var sub = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        if (chip)
        {
            var tag = Ui.Chip(car.Class);
            tag.MinWidth = 74;
            tag.Padding = new Thickness(6, 1, 6, 1);
            if (tag.Child is TextBlock tagText) tagText.HorizontalAlignment = HorizontalAlignment.Center;
            sub.Children.Add(tag);
        }
        var count = Small(SetupTree.Plural(car.Root.Files, "setup"));
        if (!chip) count.Margin = new Thickness(0);
        sub.Children.Add(count);
        int fresh = car.Root.NewCount(since);
        if (fresh > 0) sub.Children.Add(Small($"· {fresh} new", "AccentText"));
        if (Search(car) is { Hits.Count: > 0 } m) sub.Children.Add(Small($"· {SetupTree.Plural(m.Hits.Count, "match")}", "AccentText"));
        if (!car.Known) sub.Children.Add(Small("· folder name", "TextFaint"));
        text.Children.Add(sub);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var hide = Ui.IconButton(hidden ? "I.Eye" : "I.EyeOff", hidden ? "Show this car again" : "Hide this car from the list", () =>
        {
            SetupManagerSettings.Set(S.HiddenCars, car.Dir, !hidden);
            Save();
            RenderFilters();
            RenderCars();
            RenderExplorer();
        }, "QuietIconButton");
        hide.VerticalAlignment = VerticalAlignment.Center;
        hide.Opacity = hidden ? 1 : 0;
        Grid.SetColumn(hide, 2);
        grid.Children.Add(hide);

        var row = new Tile
        {
            Child = grid, CornerRadius = ThemeManager.Round(6), Cursor = Cursors.Hand, Margin = new Thickness(0, 1, 0, 1),
            Background = selected ? Ui.Res<Brush>("AccentSoft") : Brushes.Transparent,
        };
        row.MouseEnter += (_, _) => { hide.Opacity = 1; if (!selected) row.Background = Ui.Res<Brush>("SurfaceAlt"); };
        row.MouseLeave += (_, _) => { hide.Opacity = hidden ? 1 : 0; if (!selected) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInButton(d)) return;
            if (S.SelectedCar != car.Dir) { S.SelectedCar = car.Dir; _selected.Clear(); Save(); }
            RenderCars();
            RenderExplorer();
        };
        return row;
    }

    private static bool IsInButton(DependencyObject d)
    {
        for (var cur = d; cur is not null; cur = cur is Visual ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    private static TextBlock Small(string text, string brush = "TextMuted") =>
        new() { Text = text, Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>(brush), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    private static FrameworkElement Empty(string title, string text)
    {
        var stack = new StackPanel { Margin = new Thickness(16, 28, 16, 28) };
        stack.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        return stack;
    }

    private SetupNode Target(SetupCar car) =>
        _target.TryGetValue(car.Dir, out var key) && car.Root.Find(key) is { IsFolder: true } folder ? folder : car.Root;

    private List<SetupNode> SelectedNodes(SetupCar car) =>
        _selected.Select(k => car.Root.Find(k)).OfType<SetupNode>().ToList();

    private void RenderExplorer()
    {
        _crumbs.Clear();
        double offset = _treeScroll?.VerticalOffset ?? 0;
        var car = _loaded ? SelectedCar(VisibleCars()) : null;
        if (car is null)
        {
            _explorer.Child = Pane(Empty(!_loaded ? "Reading your setups…" : _store.Cars.Count == 0 ? "Nothing to show yet" : "Pick a car", "Its setups and folders show here."));
            return;
        }
        var target = Target(car);
        var sel = SelectedNodes(car);
        var search = Search(car);
        var stack = new DockPanel();

        var title = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(car.Name, "H2");
        name.VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(name);
        var chip = Ui.Chip(car.Class);
        chip.Margin = new Thickness(10, 0, 0, 0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(chip);
        var raw = new TextBlock { Text = car.Dir, Style = Ui.Res<Style>("Caption"), FontFamily = Ui.Res<FontFamily>("MonoFont"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "The folder's real name on disk (never renamed)" };
        title.Children.Add(raw);
        var rename = Ui.IconButton("I.Pencil", "Change the name or class Goodie Bag shows", () => EditCarName(car), "QuietIconButton");
        rename.VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(rename);
        var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Spaced(Ui.LabeledButton("I.Download", "Import", () => PickImport(target), "PrimaryButton"), "Copy .sto files or a .zip share pack into " + Label(target)));
        actions.Children.Add(Spaced(Ui.LabeledButton("I.External", sel.Count > 0 ? $"Share / Export ({sel.Count})" : "Share / Export", () => Export(car, sel.Count > 0 ? sel : new List<SetupNode> { target })), "Save a share pack for league mates, or plain copies"));
        actions.Children.Add(Spaced(Ui.LabeledButton("I.Plus", "New folder", () => NewFolder(target)), "Make a folder inside " + Label(target)));
        actions.Children.Add(Spaced(Ui.LabeledButton("I.Folder", "Open in Explorer", () => OpenFolder(target)), "Open " + Label(target) + " in File Explorer"));
        var head = Ui.SplitRow(title, actions);
        DockPanel.SetDock(head, Dock.Top);
        stack.Children.Add(head);

        if (!target.IsRoot)
        {
            var path = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            var chain = new List<SetupNode>();
            for (var n = target; n is not null; n = n.Parent) chain.Insert(0, n);
            for (int i = 0; i < chain.Count; i++)
            {
                var folder = chain[i];
                if (i > 0) path.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.ChevronRight"), Width = 12, Height = 12, Foreground = Ui.Res<Brush>("TextFaint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) });
                var crumb = new Button
                {
                    Style = Ui.Res<Style>("QuietButton"), Padding = new Thickness(6, 2, 6, 2), BorderThickness = new Thickness(1),
                    Content = i == 0 ? new Icon { Data = Ui.Res<Geometry>("I.Folder"), Width = 14, Height = 14, Foreground = Ui.Res<Brush>("GoldDeep") } : (object)folder.Name,
                    ToolTip = i == 0 ? "Back to the top of " + car.Name : null,
                };
                if (i == chain.Count - 1 && i > 0) crumb.Foreground = Ui.Res<Brush>("AccentText");
                crumb.Click += (_, _) => { _target[car.Dir] = folder.Key; _selected.Clear(); RenderExplorer(); };
                _crumbs.Add((crumb, folder));
                path.Children.Add(crumb);
            }
            DockPanel.SetDock(path, Dock.Top);
            stack.Children.Add(path);
        }

        if (sel.Count > 0)
        {
            var selected = new TextBlock { Text = Describe(sel) + " selected", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var buttons = new WrapPanel();
            var one = sel.Count == 1 ? sel[0] : null;
            if (one is not null) buttons.Children.Add(Spaced(Ui.LabeledButton("I.Pencil", "Rename", () => Rename(one)), "Rename"));
            buttons.Children.Add(Spaced(Ui.LabeledButton("I.Move", "Move", () => PickMove(car, sel)), "Move to another folder of this car"));
            if (one is not null)
                buttons.Children.Add(Spaced(Ui.LabeledButton("I.Folder", one.IsFolder ? "Open in Explorer" : "Show in Explorer", () => { if (one.IsFolder) OpenFolder(one); else Reveal(one); }), null));
            buttons.Children.Add(Spaced(Ui.LabeledButton("I.Trash", "Delete", () => Delete(sel), "DangerButton"), "Move to the Recycle Bin"));
            var clear = Ui.IconButton("I.X", "Clear the selection", () => { _selected.Clear(); RenderExplorer(); }, "QuietIconButton");
            buttons.Children.Add(clear);
            var bar = Ui.SplitRow(selected, buttons);
            bar.Margin = new Thickness(0, 10, 0, 0);
            var barTile = new Tile { Style = Ui.Res<Style>("Inset"), Background = Ui.Res<Brush>("AccentSoft"), Padding = new Thickness(0, 6, 6, 6), Child = bar };
            DockPanel.SetDock(barTile, Dock.Top);
            stack.Children.Add(barTile);
        }

        var foot = new TextBlock
        {
            Text = "Double-click a folder to open it · Drag rows up or down to reorder, onto a folder to move them into it, or onto the gap under a folder to move them out of it",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        };
        DockPanel.SetDock(foot, Dock.Bottom);
        stack.Children.Add(foot);

        var body = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        _treeScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4, 4, 4, 12) };
        var tree = new SetupTree(_treeScroll) { Focusable = true, FocusVisualStyle = null };
        _treeScroll.Content = tree;
        var items = TreeItems(car, search, target, new HashSet<SetupNode>(sel), _store.NewSince).ToList();
        if (items.Count == 0)
            _treeScroll.Content = Empty(search is { CarHit: false } ? "No matching setups" : "No setups for this car yet",
                search is { CarHit: false } ? "Try another search." : "Import .sto files or a share pack, or save a setup in the iRacing garage.");
        else
            tree.Show(items);
        tree.RowClicked += (node, e) => Click(car, node);
        tree.Toggled += node => { if (!_open.Remove(node.Key)) _open.Add(node.Key); RenderExplorer(); };
        tree.Checked += node => { if (!_selected.Remove(node.Key)) _selected.Add(node.Key); _anchor = node.Key; RenderExplorer(); };
        tree.DragSet = node => _selected.Contains(node.Key) ? SelectedNodes(car) : new List<SetupNode> { node };
        tree.OutsideTarget = e =>
        {
            foreach (var (button, folder) in _crumbs)
            {
                var p = e.GetPosition(button);
                if (p.X >= 0 && p.Y >= 0 && p.X <= button.ActualWidth && p.Y <= button.ActualHeight) return (folder, button);
            }
            return null;
        };
        tree.Dropped += (nodes, into, parent, order) => Dropped(nodes, into, parent, order);
        body.Children.Add(Pane(_treeScroll));
        var details = Details(car, sel, target);
        body.Children.Add(details);
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Place()
        {
            bool under = body.ActualWidth is > 0 and < 620;
            Grid.SetRow(details, under ? 1 : 0);
            Grid.SetColumn(details, under ? 0 : 1);
            Grid.SetColumnSpan(details, under ? 2 : 1);
            body.ColumnDefinitions[1].Width = under ? new GridLength(0) : new GridLength(250);
            details.Margin = under ? new Thickness(0, 12, 0, 0) : new Thickness(14, 0, 0, 0);
        }
        Place();
        body.SizeChanged += (_, e) => { if (e.WidthChanged) Place(); };
        stack.Children.Add(body);

        _explorer.Child = stack;
        _treeScroll.ScrollToVerticalOffset(offset);
    }

    private static FrameworkElement Spaced(Button button, string? tip)
    {
        button.Margin = new Thickness(6, 3, 0, 3);
        if (tip is not null) button.ToolTip = tip;
        return button;
    }

    private IEnumerable<(SetupNode, int, bool, bool, bool, int)> TreeItems(SetupCar car, SetupSearch? search, SetupNode target, HashSet<SetupNode> sel, DateTime since)
    {
        bool filtered = search is { CarHit: false };
        IEnumerable<(SetupNode, int, bool, bool, bool, int)> Walk(SetupNode folder, int depth)
        {
            foreach (var node in SetupOrder.Of(folder, S.Orders))
            {
                if (filtered && !search!.Show.Contains(node)) continue;
                bool open = node.IsFolder && (_open.Contains(node.Key) || search?.Open.Contains(node) == true);
                yield return (node, depth, open, sel.Contains(node), node == target, node.NewCount(since));
                if (open)
                    foreach (var child in Walk(node, depth + 1)) yield return child;
            }
        }
        return Walk(car.Root, 0);
    }

    private void Click(SetupCar car, SetupNode node)
    {
        var mods = Keyboard.Modifiers;
        string key = node.Key;
        bool dbl = _lastClick == key && (DateTime.Now - _lastClickAt).TotalMilliseconds < 450;
        _lastClick = dbl ? null : key;
        _lastClickAt = DateTime.Now;
        if (dbl && mods == ModifierKeys.None)
        {
            if (node.IsFolder) { if (!_open.Remove(key)) _open.Add(key); RenderExplorer(); }
            return;
        }
        if (mods.HasFlag(ModifierKeys.Shift) && _anchor is not null && _treeScroll?.Content is SetupTree tree)
        {
            var order = tree.Visible.Select(n => n.Key).ToList();
            int a = order.IndexOf(_anchor), b = order.IndexOf(key);
            if (a >= 0 && b >= 0)
                foreach (var k in order.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1)) _selected.Add(k);
        }
        else if (mods.HasFlag(ModifierKeys.Control))
        {
            if (!_selected.Remove(key)) _selected.Add(key);
            _anchor = key;
        }
        else
        {
            _selected.Clear();
            _selected.Add(key);
            _anchor = key;
        }
        _target[car.Dir] = (node.IsFolder ? node : node.Parent ?? car.Root).Key;
        RenderExplorer();
        Focus();
    }

    private void Dropped(List<SetupNode> nodes, SetupNode? into, SetupNode? parent, List<SetupNode>? order)
    {
        if (into is not null)
        {
            var (moved, error) = _store.Move(nodes, into);
            if (error is not null) _app.Notify("Couldn't move everything", error, NoticeKind.Warning);
            if (moved > 0) _open.Add(into.Key);
            Save();
            AfterMove();
            return;
        }
        if (parent is not null && order is not null)
        {
            var moving = nodes.Where(n => n.Parent != parent).ToList();
            if (moving.Count > 0)
            {
                var (_, error) = _store.Move(moving, parent);
                if (error is not null) _app.Notify("Couldn't move everything", error, NoticeKind.Warning);
            }
            SetupOrder.Save(parent, order.Where(n => n.Parent == parent).Select(n => n.Name).ToList(), S.Orders);
            Save();
            AfterMove();
            return;
        }
        RenderExplorer();
    }

    private void AfterMove()
    {
        _selected.Clear();
        _anchor = null;
        Prune();
        RenderCars();
        RenderExplorer();
    }

    private static string Describe(List<SetupNode> nodes)
    {
        int folders = nodes.Count(n => n.IsFolder), files = nodes.Count - folders;
        return string.Join(", ", new[] { folders > 0 ? SetupTree.Plural(folders, "folder") : null, files > 0 ? SetupTree.Plural(files, "setup") : null }.OfType<string>());
    }

    private string Label(SetupNode folder) => folder.IsRoot ? folder.Car.Name : folder.Name;

    private FrameworkElement Details(SetupCar car, List<SetupNode> sel, SetupNode target)
    {
        var stack = new StackPanel();
        Grid.SetIsSharedSizeScope(stack, true);
        void Kicker(string text) => stack.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Eyebrow") });
        void Title(string text) => stack.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) });
        void Fact(string label, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 90, SharedSizeGroup = "Fact" });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>("TextFaint"), Margin = new Thickness(0, 0, 10, 0) });
            var v = new TextBlock { Text = value, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            stack.Children.Add(g);
        }
        void PathBox(SetupNode node)
        {
            string path = _store.PathOf(node);
            var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBox { Text = path, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = Ui.Res<FontFamily>("MonoFont"), FontSize = 11.5 });
            var copy = Ui.IconButton("I.Copy", "Copy the path", () =>
            {
                try { Clipboard.SetText(path); _app.Notify("Path copied", path, NoticeKind.Success); }
                catch { }
            }, "QuietIconButton");
            copy.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(copy, 1);
            g.Children.Add(copy);
            stack.Children.Add(g);
        }
        void Note(string text) => stack.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });

        if (sel.Count == 1 && !sel[0].IsFolder)
        {
            var n = sel[0];
            Kicker(n.Modified >= _store.NewSince ? "Setup file · new" : "Setup file");
            Title(n.Name);
            Fact("Size", SetupTree.Size(n.Size));
            Fact("Changed", Dates.DateTime(n.Modified));
            Fact("Folder", n.Parent is { IsRoot: false } p ? p.Name : car.Name);
            PathBox(n);
            Note("Goodie Bag manages the file. Setup values are loaded and changed in the iRacing garage.");
        }
        else if (sel.Count > 1)
        {
            Kicker("Selection");
            Title(Describe(sel));
            Fact("Setups", sel.Sum(n => n.Files).ToString());
            Fact("Total size", SetupTree.Size(sel.Sum(n => n.TotalSize)));
            Note("Ctrl-click or Shift-click rows to change the selection. Drag any selected row onto a folder to move them all.");
        }
        else
        {
            var folder = sel.Count == 1 ? sel[0] : target;
            Kicker(folder.IsRoot ? "Car setup folder" : "Folder");
            Title(Label(folder));
            Fact("Setups", folder.Files.ToString());
            Fact("Subfolders", folder.Folders.ToString());
            if (folder.Files > 0) Fact("Last change", SetupTree.Date(folder.Latest));
            PathBox(folder);
            Note("Import and New folder add to this folder. Click a folder to make it the target.");
        }
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void PickImport(SetupNode folder)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import setups into " + Label(folder), Multiselect = true, CheckFileExists = true,
            Filter = "iRacing setups and share packs (*.sto;*.zip)|*.sto;*.zip|iRacing setups (*.sto)|*.sto|Share packs (*.zip)|*.zip",
        };
        if (dialog.ShowDialog() == true) ImportFiles(dialog.FileNames, folder);
    }

    private void ImportFiles(IEnumerable<string> files, SetupNode folder)
    {
        var (added, skipped) = _store.Import(files, folder);
        if (added.Count > 0)
        {
            Reveal(added);
            _app.Notify("Setups imported", $"{SetupTree.Plural(added.Count, "setup")} copied into iRacing's setups folder.", NoticeKind.Success);
        }
        if (skipped.Count > 0)
            _app.Notify("Some files were skipped", "Only .sto setups and .zip share packs with readable setups inside can be imported. Skipped: " + string.Join(", ", skipped), NoticeKind.Warning);
        RenderAll();
    }

    private void Reveal(List<SetupNode> added)
    {
        var car = added[0].Car;
        S.SelectedCar = car.Dir;
        _selected.Clear();
        foreach (var n in added.Where(n => n.Car == car))
        {
            _selected.Add(n.Key);
            for (var p = n.Parent; p is { IsRoot: false }; p = p.Parent) _open.Add(p.Key);
        }
        _target[car.Dir] = (added[0].Parent ?? car.Root).Key;
        if (Hidden(car)) _view = 2;
    }

    private async void NewFolder(SetupNode parent)
    {
        string suggestion = SetupNames.Unique(parent.Children.Select(c => c.Name), "New folder", isFile: false);
        var name = await _window.Prompt("New folder", "Inside " + Label(parent) + ". Folders show up in the iRacing garage's setup list too.", suggestion,
            text => SetupNames.Check(text, false, parent).Error, maxLength: 0);
        if (name is null) return;
        var (folder, error) = _store.CreateFolder(parent, name);
        if (error is not null) { _app.Notify("Couldn't make the folder", error, NoticeKind.Warning); return; }
        if (!parent.IsRoot) _open.Add(parent.Key);
        _target[parent.Car.Dir] = folder!.Key;
        _selected.Clear();
        RenderExplorer();
    }

    private async void Rename(SetupNode node)
    {
        if (node.Parent is not { } parent) return;
        string current = node.IsFolder ? node.Name : Path.GetFileNameWithoutExtension(node.Name);
        var name = await _window.Prompt(node.IsFolder ? "Rename folder" : "Rename setup", node.IsFolder ? "The folder is renamed on disk too." : "The file is renamed on disk; .sto is kept.", current,
            text => SetupNames.Check(text, !node.IsFolder, parent, node).Error, maxLength: 0);
        if (name is null) return;
        bool wasOpen = _open.Remove(node.Key), wasTarget = _target.TryGetValue(node.Car.Dir, out var t) && t == node.Key;
        var error = _store.Rename(node, name);
        if (error is not null) { _app.Notify("Couldn't rename", error, NoticeKind.Warning); return; }
        Save();
        _selected.Clear();
        _selected.Add(node.Key);
        if (wasOpen) _open.Add(node.Key);
        if (wasTarget) _target[node.Car.Dir] = node.Key;
        Prune();
        RenderExplorer();
    }

    private async void Delete(List<SetupNode> nodes)
    {
        int files = nodes.Sum(n => n.Files), folders = nodes.Count(n => n.IsFolder);
        string what = nodes.Count == 1 ? $"“{nodes[0].Name}”" : Describe(nodes);
        string inside = folders > 0 && files > 0 ? $" {SetupTree.Plural(files, "setup")} inside {(folders == 1 ? "it" : "them")} go too." : "";
        if (!await _window.Confirm("Delete " + what + "?", "It goes to the Windows Recycle Bin, so it can be restored from there." + inside, "Move to Recycle Bin", danger: true)) return;
        var (deleted, error) = _store.Delete(nodes);
        if (error is not null) _app.Notify("Couldn't delete everything", error, NoticeKind.Warning);
        else if (deleted > 0) _app.Notify("Moved to the Recycle Bin", deleted == 1 ? nodes[0].Name : $"{deleted} items", NoticeKind.Success);
        _selected.Clear();
        Prune();
        RenderAll();
    }

    private void PickMove(SetupCar car, List<SetupNode> nodes)
    {
        var list = new StackPanel();
        void Add(SetupNode folder, int depth)
        {
            bool ok = SetupStore.Movable(nodes, folder).Count > 0;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.Folder"), Width = 15, Height = 15, Foreground = Ui.Res<Brush>("GoldDeep") });
            content.Children.Add(new TextBlock { Text = folder.IsRoot ? car.Name : folder.Name, Margin = new Thickness(8, 0, 0, 0) });
            var row = new Button { Style = Ui.Res<Style>("QuietButton"), Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(depth * 20, 1, 0, 1), IsEnabled = ok };
            row.Click += (_, _) =>
            {
                _window.CloseModal(true);
                var (moved, error) = _store.Move(nodes, folder);
                if (error is not null) _app.Notify("Couldn't move everything", error, NoticeKind.Warning);
                else if (moved > 0) _app.Notify("Moved", $"{SetupTree.Plural(moved, "item")} moved to {Label(folder)}.", NoticeKind.Success);
                if (!folder.IsRoot) _open.Add(folder.Key);
                AfterMove();
            };
            list.Children.Add(row);
            foreach (var child in SetupOrder.Of(folder, S.Orders).Where(c => c.IsFolder)) Add(child, depth + 1);
        }
        Add(car.Root, 0);
        var buttons = new StackPanel();
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => _window.CloseModal(false);
        buttons.Children.Add(cancel);
        _ = _window.ShowModal(Ui.Dialog(_window, "Move " + (nodes.Count == 1 ? $"“{nodes[0].Name}”" : Describe(nodes)), "Pick a folder of " + car.Name + ".", list, buttons, width: 480));
    }

    private void Export(SetupCar car, List<SetupNode> nodes)
    {
        bool pack = true;
        string destination = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var content = new StackPanel();
        var where = new TextBlock { Text = destination, Style = Ui.Res<Style>("Caption"), FontFamily = Ui.Res<FontFamily>("MonoFont"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var describe = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        void Explain() => describe.Text = pack
            ? $"One file, {SetupStore.PackName(car, nodes)}, for league mates: they can import it in Goodie Bag, or unzip it straight into Documents\\iRacing\\setups and each setup lands under the right car."
            : $"Plain copies of the .sto files in a “{car.Name}” folder, keeping the folder structure.";
        content.Children.Add(Ui.Segmented(new[] { "Share pack (.zip)", "Copy .sto files" }, 0, i => { pack = i == 0; Explain(); }, "setupexport"));
        Explain();
        content.Children.Add(describe);
        var row = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = "Save to", Style = Ui.Res<Style>("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        Grid.SetColumn(where, 1);
        row.Children.Add(where);
        var browse = Ui.LabeledButton("I.Folder", "Choose…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Where to save", InitialDirectory = destination };
            if (dialog.ShowDialog() == true) { destination = dialog.FolderName; where.Text = destination; }
        });
        browse.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(browse, 2);
        row.Children.Add(browse);
        content.Children.Add(row);
        int count = nodes.Sum(n => n.Files);
        var buttons = Ui.DialogButtons(_window, "Save", () =>
        {
            var (path, error) = _store.Export(nodes, destination, pack);
            if (error is not null) { _app.Notify("Nothing saved", error, NoticeKind.Warning); return false; }
            _app.Notify(pack ? "Share pack saved" : "Setups copied", path!, NoticeKind.Success);
            if (path is not null) OpenPath(pack ? $"/select,\"{path}\"" : $"\"{path}\"");
            return true;
        });
        _ = _window.ShowModal(Ui.Dialog(_window, "Share or export " + SetupTree.Plural(count, "setup"), "From " + car.Name + ". The originals stay where they are.", content, buttons, width: 560));
    }

    private void EditCarName(SetupCar car)
    {
        var content = new StackPanel();
        var box = new TextBox { Text = car.Name, Margin = new Thickness(0, 4, 0, 0) };
        content.Children.Add(Ui.FieldRow("Name", box, 90));
        string cls = car.Class;
        var classes = Ui.Combo(SetupCars.Classes.Select(c => new Choice(c, c)), cls, c => cls = c.Id ?? cls, 200);
        content.Children.Add(Ui.FieldRow("Class", classes, 90));
        content.Children.Add(new TextBlock { Text = "The folder on disk stays " + car.Dir + ".", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 8, 0, 0) });
        var buttons = Ui.DialogButtons(_window, "Save", () =>
        {
            if (string.IsNullOrWhiteSpace(box.Text)) return false;
            S.Names[car.Dir] = new SetupCarName { Name = box.Text.Trim(), Class = cls };
            _store.Describe();
            Save();
            RenderAll();
            return true;
        });
        if (car.Custom)
        {
            string back = SetupCars.BuiltInName(car.Dir) ?? car.Dir;
            var reset = new Button { Content = "Use “" + back + "”", Margin = new Thickness(0, 0, 8, 0) };
            reset.Click += (_, _) =>
            {
                S.Names.Remove(car.Dir);
                _store.Describe();
                Save();
                _window.CloseModal(true);
                RenderAll();
            };
            buttons.Children.Insert(0, reset);
        }
        _ = _window.ShowModal(Ui.Dialog(_window, "Name shown for this car", "Only how Goodie Bag shows it; iRacing finds the car by its folder name.", content, buttons, width: 520));
    }

    private void OpenFolder(SetupNode folder)
    {
        string path = _store.PathOf(folder);
        if (!_store.Demo && !Directory.Exists(path)) { _app.Notify("Folder not found", path, NoticeKind.Warning); return; }
        OpenPath($"\"{path}\"");
    }

    private void Reveal(SetupNode file) => OpenPath($"/select,\"{_store.PathOf(file)}\"");

    private void OpenPath(string arguments)
    {
        if (_store.Demo) { _app.Notify("File Explorer", "Opens File Explorer there (not available with demo data).", NoticeKind.Info); return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true })?.Dispose(); }
        catch { _app.Notify("File Explorer", "Windows couldn't open File Explorer.", NoticeKind.Warning); }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        var car = _store.Car(S.SelectedCar) ?? SelectedCar(VisibleCars());
        if (car is null) return;
        var sel = SelectedNodes(car);
        switch (e.Key)
        {
            case Key.Delete when sel.Count > 0: Delete(sel); e.Handled = true; break;
            case Key.F2 when sel.Count == 1: Rename(sel[0]); e.Handled = true; break;
            case Key.Escape when sel.Count > 0: _selected.Clear(); RenderExplorer(); e.Handled = true; break;
        }
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("setups", () =>
        {
            S.SelectedCar = "porsche963gtp";
            _open.Add("porsche963gtp/IMSA");
            _open.Add("porsche963gtp/IMSA/Sebring");
            _selected.Clear();
            _selected.Add("porsche963gtp/IMSA/Sebring/Race.sto");
            RenderAll();
            return Task.CompletedTask;
        });
        yield return ("setups-gtp-hidden", () =>
        {
            S.SetClassHidden("GTP", true);
            _classFilter.Remove("GTP");
            RenderAll();
            if (VisibleCars().Any(c => c.Class == "GTP"))
                throw new InvalidOperationException("A hidden class's cars stayed in the car list.");
            return Task.CompletedTask;
        });
        yield return ("setups-classes-back", () =>
        {
            S.HiddenClasses.Clear();
            S.SelectedCar = "porsche963gtp";
            RenderAll();
            return Task.CompletedTask;
        });
    }
}

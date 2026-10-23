using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.SimRacing;
using GoodieBag.SimRacing.IracingFiles;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class IracingControllerSection : StackPanel
{
    private static readonly string[] Tabs = { "All settings", "Keybinds", "Overview", "Triple screens", "Backups" };
    private const int MaxRows = 80;
    private static readonly HashSet<string> HiddenSections = new(StringComparer.OrdinalIgnoreCase) { "renderer|AutoCfg", "renderer|Debug" };
    private const string WideSection = "renderer|Graphics Options";
    private const int WideMinRows = 6;

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly Func<FovSetup> _fov;
    private readonly Workspace _ws;
    private readonly StackPanel _tab = new();
    private readonly StackPanel _tabTools = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Tile _pending = new();
    private int _tabIndex;
    private string _search = "";
    private int _area;
    private bool _simRunning;
    private readonly HashSet<string> _searchFolded = new(StringComparer.OrdinalIgnoreCase);
    private Action? _draftsChanged;

    public IracingControllerSection(AppServices app, MainWindow window, Func<FovSetup> fov)
    {
        _app = app;
        _window = window;
        _fov = fov;
        _ws = new Workspace(app.Config.SimRacing.IracingController, app.Demo);
        _ws.Load();
        Build();
    }

    private IracingControllerSettings S => _app.Config.SimRacing.IracingController;
    private SettingsSession? Session => _ws.Session;

    private void Build()
    {
        Children.Clear();
        if (Session is null)
        {
            Children.Add(NotFound());
            return;
        }

        var tabs = Ui.Segmented(Tabs, _tabIndex, i => { _tabIndex = i; ShowTab(); }, "TiTab");
        tabs.HorizontalAlignment = HorizontalAlignment.Left;
        tabs.VerticalAlignment = VerticalAlignment.Center;
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        (_tabTools.Parent as Panel)?.Children.Remove(_tabTools);
        tools.Children.Add(_tabTools);
        if (_ws.Modes.Count > 1)
        {
            var modes = Ui.Combo(_ws.Modes.Select(m => new Choice(m.File, m.Label + " graphics")), Session.RendererFile, c => _ = SwitchMode(c.Id!), 170);
            modes.ToolTip = "Which display mode's graphics file to show (iRacing keeps one per mode)";
            tools.Children.Add(modes);
        }
        if (!_ws.Demo && _ws.Folder is { } folder)
            tools.Children.Add(Ui.IconButton("I.Folder", "Open the iRacing folder", () => Ui.OpenUrl(folder)));
        tools.Children.Add(Ui.IconButton("I.Refresh", "Read the files again", () => _ = Reload()));
        var bar = Ui.SplitRow(tabs, tools);
        bar.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(bar);

        var where = TextFit.TipWhenTrimmed(new TextBlock { Style = Ui.Res<Style>("Caption"), Margin = new Thickness(2, 0, 0, 10), TextTrimming = TextTrimming.CharacterEllipsis });
        if (_ws.Demo) where.Inlines.Add(new System.Windows.Documents.Run("Demo settings (nothing is read from or written to the PC) · "));
        else where.Inlines.Add(TextCase.Verbatim(new System.Windows.Documents.Run(_ws.Folder + " · ")));
        where.Inlines.Add(new System.Windows.Documents.Run($"{Session.Settings.Count} settings in "));
        where.Inlines.Add(TextCase.Verbatim(new System.Windows.Documents.Run(string.Join(", ", Session.FileNames))));
        Children.Add(where);

        _pending.Style = Ui.Res<Style>("Inset");
        _pending.Padding = new Thickness(14, 10, 10, 10);
        _pending.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(_pending);
        Children.Add(_tab);
        ShowTab();
    }

    private Tile NotFound()
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(_ws.LoadError is null ? "iRacing's settings weren't found" : "iRacing's settings couldn't be read", "H3"));
        stack.Children.Add(new TextBlock
        {
            Text = _ws.LoadError ?? "iRacing Controller reads Documents\\iRacing (app.ini, core.ini and the graphics files). Start iRacing once so it creates them, then check again.",
            Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12),
        });
        var again = Ui.LabeledButton("I.Refresh", "Check again", () => _ = Reload());
        again.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(again);
        return new Tile { Style = Ui.Res<Style>("Card"), Child = stack };
    }

    private void ShowTab()
    {
        _tab.Children.Clear();
        _tabTools.Children.Clear();
        _draftsChanged = null;
        _simRunning = _ws.SimRunning;
        UpdatePending();
        switch (_tabIndex)
        {
            case 0: AllSettings(); break;
            case 1: _tab.Children.Add(IracingKeybindsView.Build(_app, _ws)); break;
            case 2: Overview(); break;
            case 3: Triples(); break;
            default: BackupsTab(); break;
        }
    }

    private async Task Reload()
    {
        if (!await ConfirmDropPending()) return;
        _ws.Load();
        Build();
    }

    private async Task SwitchMode(string file)
    {
        if (file == Session?.RendererFile) return;
        if (!await ConfirmDropPending()) { Build(); return; }
        _ws.SetMode(file);
        _app.Save();
        Build();
    }

    private async Task<bool> ConfirmDropPending()
    {
        int n = Session?.Pending.Count() ?? 0;
        return n == 0 || await _window.Confirm("Discard changes?", $"{n} change{(n == 1 ? " hasn't" : "s haven't")} been written to iRacing yet.", "Discard", danger: true);
    }

    private long _simCheckedAt = long.MinValue / 2;

    private void UpdatePending()
    {
        _draftsChanged?.Invoke();
        var pending = Session?.Pending.ToList() ?? new List<Setting>();
        _pending.Visibility = pending.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (pending.Count == 0) return;
        if (Environment.TickCount64 - _simCheckedAt > 2000)
        {
            _simRunning = _ws.SimRunning;
            _simCheckedAt = Environment.TickCount64;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool running = _simRunning;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = $"{pending.Count} change{(pending.Count == 1 ? "" : "s")} not written yet", FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = running ? "iRacing is running. It rewrites its files when it closes, so close it first, then write."
                : _ws.Demo ? "Demo: nothing is written." : "Everything is backed up before writing.",
            Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>(running ? "Warn" : "TextMuted"),
        });
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Ui.LabeledButton("I.Eye", "Review", () => _ = Review(), "GhostButton"));
        var discard = Ui.LabeledButton("I.Undo", "Discard", () => { Session!.DiscardAll(); ShowTab(); }, "GhostButton");
        discard.Margin = new Thickness(4, 0, 4, 0);
        buttons.Children.Add(discard);
        var write = Ui.LabeledButton("I.Save", "Write to iRacing", () => _ = Write(), "PrimaryButton");
        write.IsEnabled = !running && !_ws.Demo;
        buttons.Children.Add(write);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        _pending.Child = grid;
    }

    private async Task Review()
    {
        var list = new StackPanel();
        foreach (var s in Session!.Pending)
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel();
            name.Children.Add(new TextBlock { Text = s.Def.Label, FontWeight = FontWeights.SemiBold });
            name.Children.Add(TextCase.Verbatim(new TextBlock { Text = $"{s.FileName} · [{s.Section}] {s.Key}", Style = Ui.Res<Style>("Caption") }));
            row.Children.Add(name);
            var change = new TextBlock { Text = $"{s.Show(s.Applied)}  →  {s.Show(s.Draft)}", Foreground = Ui.Res<Brush>("AccentText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(change, 1);
            row.Children.Add(change);
            list.Children.Add(row);
        }
        var close = new StackPanel();
        var ok = new Button { Content = "Close", Style = Ui.Res<Style>("PrimaryButton") };
        ok.Click += (_, _) => _window.CloseModal(false);
        close.Children.Add(ok);
        await _window.ShowModal(Ui.Dialog(_window, "Changes to write", "These go into iRacing's files when you write them.", list, close, null, 620));
    }

    private async Task Write()
    {
        if (_ws.SimRunning)
        {
            _app.Notify("Close iRacing first", "It rewrites its settings when it closes, which would undo the changes.", NoticeKind.Warning, important: true);
            _simRunning = true;
            UpdatePending();
            return;
        }
        var danger = Session!.Pending.Where(s => s.Def.Danger is not null).ToList();
        if (danger.Count > 0)
        {
            if (!await _window.Confirm("Write risky changes?", string.Join("\n\n", danger.Select(s => $"{s.Def.Label}: {s.Def.Danger}")), "Write anyway", danger: true))
                return;
            if (_ws.SimRunning)
            {
                _app.Notify("Close iRacing first", "It rewrites its settings when it closes, which would undo the changes.", NoticeKind.Warning, important: true);
                _simRunning = true;
                UpdatePending();
                return;
            }
        }
        if (Session is null) return;
        var layoutKeys = Session.Pending.Select(s => s.Section + "|" + s.Key).ToList();
        try
        {
            var result = Session.Write(_ws.Store);
            string skipped = result.Skipped.Count == 0 ? "" : $" Not found any more: {string.Join(", ", result.Skipped)}.";
            _app.Notify($"Wrote {result.Written} change{(result.Written == 1 ? "" : "s")} to iRacing", $"Backup #{result.Backup?.Number} has the files from before.{skipped}", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _app.Notify("Changes weren't written", ex.Message + " Your changes are still here to try again.", NoticeKind.Warning, important: true);
            return;
        }
        _ws.Load();
        if (Session is { } written) { LayoutGuard.Adopt(written, S.SavedLayout, layoutKeys); _app.Save(); }
        Build();
    }

    private void Overview()
    {
        HealthReport report;
        try { report = _ws.Health(); }
        catch (Exception ex)
        {
            _tab.Children.Add(FindingCard(Rating.Concerning, "The overview couldn't be built", ex.Message));
            return;
        }
        var layout = LayoutGuard.Differences(Session!, S.SavedLayout);
        int problems = report.Findings.Count + (report.TelemetryLarge ? 1 : 0) + (layout.Count > 0 ? 1 : 0);

        var counts = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(-6, 0, -6, 6) };
        counts.Children.Add(Counter(problems.ToString(CultureInfo.InvariantCulture), problems == 1 ? "thing to look at" : "things to look at", problems == 0 ? "SageSoft" : "WarnSoft", problems == 0 ? "SageDeep" : "Warn"));
        counts.Children.Add(Counter(report.Unusual.Count.ToString(CultureInfo.InvariantCulture), "changed from iRacing's default", "SlateSoft", "SlateDeep"));
        counts.Children.Add(Counter(report.Passed.Count.ToString(CultureInfo.InvariantCulture), "checks passed", "SageSoft", "SageDeep"));
        _tab.Children.Add(counts);

        if (layout.Count > 0)
            _tab.Children.Add(FindingCard(Rating.Concerning, "iRacing changed your triple-screen layout",
                $"{layout.Count} value{(layout.Count == 1 ? "" : "s")} differ from the layout you saved ({string.Join(", ", layout.Take(4).Select(d => d.Setting.Def.Label))}). This often happens after a driver update or a crash.",
                ("Put it back", () => { LayoutGuard.StageRestore(Session!, S.SavedLayout); ShowTab(); })));

        foreach (var f in report.Findings)
        {
            var actions = new List<(string, Action)>();
            if (f.Id == "telemetry-off" && f.Setting is { } mem)
                actions.Add(("Turn it on", () => { Session!.SetDraft(mem, "1"); ShowTab(); }));
            actions.Add(("Ignore", () => { S.IgnoredFindings.Add(f.Id); _app.Save(); ShowTab(); }));
            _tab.Children.Add(FindingCard(f.Rating, f.Title, f.Detail, actions.ToArray()));
        }

        if (report.TelemetryLarge && report.Telemetry is { } t)
        {
            var old = t.OlderThan(30);
            _tab.Children.Add(FindingCard(Rating.Concerning, $"The telemetry folder is {HealthCheck.Gb(t.TotalBytes)}",
                $"{t.Files.Count} .ibt files from auto-logging. {old.Count} are older than 30 days ({HealthCheck.Gb(old.Sum(f => f.Length))}).",
                ("Move old files to the Recycle Bin", () => _ = CleanTelemetry(old)),
                ("Ignore", () => { S.IgnoredFindings.Add("telemetry-large"); _app.Save(); ShowTab(); })));
        }

        if (report.Unusual.Count > 0)
        {
            _tab.Children.Add(Ui.EditorSection("Changed from iRacing's default"));
            _tab.Children.Add(new TextBlock { Text = "Usually on purpose. Reset one if you didn't mean to change it, or keep it to stop it being listed.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 6) });
            var card = new StackPanel();
            foreach (var s in report.Unusual.Take(30))
                card.Children.Add(UnusualRow(s));
            if (report.Unusual.Count > 30)
                card.Children.Add(new TextBlock { Text = $"+{report.Unusual.Count - 30} more: search for them under All settings.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 8, 0, 0) });
            _tab.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(18, 8, 18, 10), Child = card });
        }

        if (report.Passed.Count > 0)
        {
            _tab.Children.Add(Ui.EditorSection("Passed"));
            var wrap = new WrapPanel();
            foreach (var p in report.Passed)
            {
                var chip = Ui.Chip(p, "SageSoft", "SageDeep");
                chip.Margin = new Thickness(0, 0, 6, 6);
                wrap.Children.Add(chip);
            }
            _tab.Children.Add(wrap);
        }
    }

    private static Tile Counter(string value, string label, string soft, string deep)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Ui.Res<Brush>(deep) });
        stack.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption") });
        return new Tile { Style = Ui.Res<Style>("Card"), Background = Ui.Res<Brush>(soft), BorderBrush = Ui.Res<Brush>(soft), Margin = new Thickness(6), Padding = new Thickness(16, 12, 16, 12), Child = stack };
    }

    private static Tile FindingCard(Rating rating, string title, string detail, params (string Label, Action Run)[] actions)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool bad = rating == Rating.Bad;
        grid.Children.Add(new Tile
        {
            Width = 30, Height = 30, CornerRadius = ThemeManager.Round(8), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0),
            Background = Ui.Res<Brush>(bad ? "DangerSoft" : "WarnSoft"),
            Child = new Icon { Data = Ui.Res<Geometry>("I.Alert"), Width = 15, Height = 15, Foreground = Ui.Res<Brush>(bad ? "Danger" : "Warn") },
        });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = detail, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        for (int i = 0; i < actions.Length; i++)
        {
            var (label, run) = actions[i];
            var b = new Button { Content = label, Style = Ui.Res<Style>(i == 0 && label != "Ignore" ? "SoftButton" : "GhostButton"), Margin = new Thickness(4, 0, 0, 0) };
            b.Click += (_, _) => run();
            buttons.Children.Add(b);
        }
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        var card = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(16, 12, 14, 12), Margin = new Thickness(0, 0, 0, 8), Child = grid };
        if (bad)
        {
            card.Background = Ui.Res<Brush>("DangerSoft");
            card.BorderBrush = Ui.Res<Brush>("Danger");
            card.BorderThickness = new Thickness(1.5);
            var heading = (TextBlock)text.Children[0];
            heading.Foreground = Ui.Res<Brush>("DangerDeep");
            heading.FontWeight = FontWeights.Bold;
        }
        return card;
    }

    private Grid UnusualRow(Setting s)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = s.Def.Label, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = $"{s.Show(s.Draft)} · {s.UnusualNote} · {s.SectionInfo.Name}", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
        row.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (Session!.DefaultOf(s) is { } std && !s.IsReadOnly)
        {
            var reset = new Button { Content = "Reset to default", Style = Ui.Res<Style>("SoftButton") };
            reset.Click += (_, _) => { Session.SetDraft(s, std); ShowTab(); };
            buttons.Children.Add(reset);
        }
        var keep = new Button { Content = "Keep", Style = Ui.Res<Style>("GhostButton"), Margin = new Thickness(4, 0, 0, 0), ToolTip = "It's on purpose: stop listing it until it changes" };
        keep.Click += (_, _) => { Session.Ignore(s); _app.Save(); ShowTab(); };
        buttons.Children.Add(keep);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        return row;
    }

    private async Task CleanTelemetry(List<FileInfo> files)
    {
        if (files.Count == 0)
        {
            _app.Notify("Nothing to move", "No telemetry files are older than 30 days.", NoticeKind.Info);
            return;
        }
        if (!await _window.Confirm("Move old telemetry to the Recycle Bin?", $"{files.Count} .ibt files ({HealthCheck.Gb(files.Sum(f => f.Length))}) older than 30 days. The newest file is always kept, and you can restore them from the Recycle Bin.", "Move to Recycle Bin", danger: true))
            return;
        try
        {
            int moved = RecycleBin.Send(files.Select(f => f.FullName).ToList());
            _app.Notify("Telemetry tidied", $"{moved} file{(moved == 1 ? "" : "s")} moved to the Recycle Bin.", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _app.Notify("Telemetry files weren't moved", ex.Message, NoticeKind.Warning, important: true);
        }
        _ws.ForgetTelemetry();
        ShowTab();
    }

    private void AllSettings()
    {
        var areas = Session!.Catalog.Areas;
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var tabs = top;
        _tab.Children.Add(top);
        var search = new TextBox { Width = 220, Text = _search, Tag = "Search every setting", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var cards = new List<SectionCard>();
        var foldAll = new CollapseAllButton { VerticalAlignment = VerticalAlignment.Center };
        foldAll.Requested += fold => FoldAll(fold);
        _tabTools.Children.Add(search);
        _tabTools.Children.Add(foldAll);
        var wide = new StackPanel();
        _tab.Children.Add(wide);
        var results = new MasonryPanel { MinColumnWidth = 560, MaxColumns = 3 };
        _tab.Children.Add(results);

        IEnumerable<Setting> Listed(SectionInfo sec) => Session.In(sec).Where(s => !s.IsHidden);
        bool Searching() => _search.Length > 0;
        bool IsFolded(string key) => Searching() ? _searchFolded.Contains(key) : S.IsCollapsed(key);
        void SetFolded(string key, bool folded)
        {
            if (Searching())
            {
                if (folded) _searchFolded.Add(key); else _searchFolded.Remove(key);
                return;
            }
            S.SetCollapsed(key, folded);
            _app.Save();
        }
        void UpdateFoldAll()
        {
            foldAll.SetOpen(cards.Count(c => c.Expanded), Math.Max(1, cards.Count));
        }
        void FoldAll(bool fold)
        {
            foreach (var card in cards)
            {
                SetFolded(card.Key, fold);
                card.SetExpanded(!fold);
            }
            UpdateFoldAll();
        }

        void FillTabs()
        {
            tabs.Children.Clear();
            for (int i = 0; i < areas.Count; i++)
            {
                int index = i;
                int changed = Session.Sections.Where(sec => sec.AreaIndex == i && Shown(sec)).SelectMany(Listed).Count(x => x.IsChanged);
                bool selected = _search.Length == 0 && i == _area;
                var look = AreaLook(areas[i].Title, i);
                var glyph = new Icon { Data = Ui.Res<Geometry>(look.Icon), Width = 13, Height = 13, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                glyph.SetResourceReference(Control.ForegroundProperty, look.Hue.Deep);
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(glyph);
                content.Children.Add(new TextBlock { Text = areas[i].Title + (changed > 0 ? $" · {changed}" : ""), VerticalAlignment = VerticalAlignment.Center });
                var b = new Button
                {
                    Content = content, Style = Ui.Res<Style>(selected ? "SelectedButton" : "GhostButton"), Margin = new Thickness(0, 0, 6, 6),
                };
                if (selected)
                {
                    b.SetResourceReference(Control.BackgroundProperty, look.Hue.Soft);
                    b.SetResourceReference(Control.BorderBrushProperty, look.Hue.Deep);
                    b.SetResourceReference(Control.ForegroundProperty, look.Hue.Deep);
                }
                AutomationProperties.SetName(b, areas[i].Title);
                b.Click += (_, _) => { _area = index; _search = ""; search.Text = ""; FillTabs(); Fill(); };
                tabs.Children.Add(b);
            }
        }

        void Fill()
        {
            wide.Children.Clear();
            results.Children.Clear();
            cards.Clear();
            var words = _search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matches = Session.Sections
                .Where(sec => Shown(sec) && (words.Length > 0 || sec.AreaIndex == _area))
                .SelectMany(Listed)
                .Where(s => words.All(w =>
                    s.Def.Label.Contains(w, StringComparison.OrdinalIgnoreCase) || s.Key.Contains(w, StringComparison.OrdinalIgnoreCase)
                    || s.Def.Help.Contains(w, StringComparison.OrdinalIgnoreCase) || s.SectionInfo.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (matches.Count == 0)
            {
                results.Children.Add(new TextBlock { Text = words.Length > 0 ? "No settings match." : "None of this area's settings are in your files.", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(2, 6, 0, 0) });
                UpdateFoldAll();
                return;
            }
            int max = words.Length > 0 ? MaxRows : int.MaxValue;
            foreach (var group in matches.Take(max).GroupBy(s => s.SectionInfo))
            {
                var settings = group.ToList();
                string key = SectionKey(group.Key);
                bool span = settings.Count >= WideMinRows && key == WideSection;
                var card = new SectionCard(this, group.Key, key, settings, words.Length > 0, span, !IsFolded(key), expanded =>
                {
                    SetFolded(key, !expanded);
                    UpdateFoldAll();
                });
                cards.Add(card);
                if (span)
                {
                    card.Margin = new Thickness(0, 0, 0, results.Gap);
                    wide.Children.Add(card);
                }
                else results.Children.Add(card);
            }
            if (matches.Count > max)
                results.Children.Add(new TextBlock { Text = $"Showing the first {max} of {matches.Count}. Add a word to narrow it down.", Style = Ui.Res<Style>("Caption") });
            UpdateFoldAll();
        }

        _draftsChanged = () => { foreach (var card in cards) card.UpdateSummary(); };

        var debounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            if (Session is null || top.Parent != _tab) return;
            FillTabs();
            Fill();
        };
        search.TextChanged += (_, _) =>
        {
            var text = search.Text.Trim();
            if (text == _search) return;
            _search = text;
            _searchFolded.Clear();
            debounce.Stop();
            debounce.Start();
        };
        FillTabs();
        Fill();
    }

    private static ((string Soft, string Deep) Hue, string Icon) AreaLook(string title, int index) => title switch
    {
        "Graphics and display" => (Hues.Accent, "I.Monitor"),
        "View and HUD" => (Hues.Named("Slate"), "I.Eye"),
        "Wheel and controls" => (Hues.Named("Coral"), "I.Wheel"),
        "Sound" => (Hues.Named("Gold"), "I.Speaker"),
        "Spotter and chat" => (Hues.Named("Plum"), "I.Keyboard"),
        "Pit and setups" => (Hues.Named("Sage"), "I.Sliders"),
        "Replay and broadcast" => (Hues.Named("Coral"), "I.Play"),
        "Telemetry and system" => (Hues.Named("Slate"), "I.Gauge"),
        _ => (Hues.At(index), "I.Sliders"),
    };

    private sealed class SectionCard : Tile
    {
        private readonly IracingControllerSection _owner;
        private readonly List<Setting> _settings;
        private readonly bool _searching;
        private readonly bool _span;
        private readonly SectionInfo _section;
        private readonly Action<bool> _toggled;
        private readonly Icon _chevron = new() { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        private readonly Border _body = new();
        private readonly Button _header;
        private readonly TextBlock _fileLine;

        public SectionCard(IracingControllerSection owner, SectionInfo section, string key, List<Setting> settings, bool searching, bool span, bool expanded, Action<bool> toggled)
        {
            _owner = owner;
            _section = section;
            Key = key;
            _settings = settings;
            _searching = searching;
            _span = span;
            _toggled = toggled;
            Style = Ui.Res<Style>("Card");
            Padding = new Thickness(0);
            _body.Margin = new Thickness(18, 0, 18, 18);

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hue = AreaLook(section.Area, section.AreaIndex).Hue;
            _chevron.SetResourceReference(Control.ForegroundProperty, hue.Deep);
            _chevron.Width = _chevron.Height = 12;
            var badge = new Tile { Width = 22, Height = 22, CornerRadius = ThemeManager.Round(6), VerticalAlignment = VerticalAlignment.Center, Child = _chevron };
            badge.SetResourceReference(Border.BackgroundProperty, hue.Soft);
            head.Children.Add(badge);
            var name = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var title = Ui.Text(section.Name, "H3");
            title.TextWrapping = TextWrapping.Wrap;
            title.VerticalAlignment = VerticalAlignment.Center;
            name.Children.Add(title);
            if (section.IsRare)
            {
                var chip = Ui.Chip("expert", "PlumSoft", "PlumDeep");
                chip.Margin = new Thickness(8, 0, 0, 0);
                chip.Padding = new Thickness(6, 0, 6, 0);
                chip.VerticalAlignment = VerticalAlignment.Center;
                name.Children.Add(chip);
            }
            Grid.SetColumn(name, 1);
            head.Children.Add(name);
            _summary.Style = Ui.Res<Style>("Caption");
            Grid.SetColumn(_summary, 2);
            head.Children.Add(_summary);
            _fileLine = TextFit.TipWhenTrimmed(new TextBlock
            {
                Text = $"{(_searching ? _section.Area + " · " : "")}{_section.FileName} [{_section.Section}]",
                Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            });
            TextCase.SetVerbatim(_fileLine, !_searching);
            TextFit.TipWhenTrimmed(_fileLine);
            var headStack = new StackPanel();
            headStack.Children.Add(head);
            headStack.Children.Add(_fileLine);
            _header = new Button
            {
                Style = Ui.Res<Style>("QuietButton"), Content = headStack,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(24, 22, 26, 22),
            };
            AutomationProperties.SetName(_header, section.Name);
            ClickGuard.SetAllowRepeat(_header, true);
            _header.Click += (_, _) =>
            {
                SetExpanded(!Expanded);
                _toggled(Expanded);
            };

            var stack = new StackPanel();
            stack.Children.Add(_header);
            stack.Children.Add(_body);
            Child = stack;
            UpdateSummary();
            SetExpanded(expanded);
        }

        public string Key { get; }
        public bool Expanded { get; private set; }

        public void SetExpanded(bool expanded)
        {
            Expanded = expanded;
            _chevron.Data = Ui.Res<Geometry>(expanded ? "I.ChevronDown" : "I.ChevronRight");
            _header.ToolTip = expanded ? "Fold this section" : "Show this section's settings";
            _fileLine.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (expanded && _body.Child is null) _body.Child = BuildBody();
            _body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }

        public void UpdateSummary()
        {
            int changed = _settings.Count(s => s.IsChanged);
            _summary.Text = $"{_settings.Count} setting{(_settings.Count == 1 ? "" : "s")}" + (changed > 0 ? $" · {changed} changed" : "");
            _summary.SetResourceReference(TextBlock.ForegroundProperty, changed > 0 ? "AccentText" : "TextMuted");
        }

        private StackPanel BuildBody()
        {
            var body = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            Panel list = _span ? new SplitRows { MinColumnWidth = 480, Gap = 32 } : new StackPanel();
            body.Children.Add(list);
            int next = 0;
            void Add(int count)
            {
                for (int end = Math.Min(_settings.Count, next + count); next < end; next++)
                    list.Children.Add(_owner.SettingRow(_settings[next]));
            }
            void More()
            {
                if (_body.Child != body) return;
                Add(RowsPerBatch);
                if (next < _settings.Count) Dispatcher.BeginInvoke(More, System.Windows.Threading.DispatcherPriority.Background);
            }
            Add(RowsPerBatch);
            if (next < _settings.Count) Dispatcher.BeginInvoke(More, System.Windows.Threading.DispatcherPriority.Background);
            return body;
        }

        private const int RowsPerBatch = 24;
    }

    public void CollapseAll()
    {
        if (Session is null) return;
        foreach (var sec in Session.Sections.Where(Shown))
            S.SetCollapsed(SectionKey(sec), true);
        _app.Save();
        if (_tabIndex == 0) ShowTab();
    }

    private static string SectionKey(SectionInfo sec) => SettingsCatalog.FileKey(sec.FileName) + "|" + sec.Section;

    private static bool Shown(SectionInfo sec) => !HiddenSections.Contains(SectionKey(sec));

    private sealed class SplitRows : Panel
    {
        public double MinColumnWidth { get; set; } = 480;
        public double Gap { get; set; } = 32;

        private int _split = -1;
        private double _splitWidth = double.NaN;
        private int _splitCount;

        private bool TwoColumns(double width) => !double.IsInfinity(width) && width >= MinColumnWidth * 2 + Gap && InternalChildren.Count > 1;

        protected override Size MeasureOverride(Size available)
        {
            int n = InternalChildren.Count;
            if (!TwoColumns(available.Width))
            {
                double h = 0, w = 0;
                foreach (UIElement child in InternalChildren)
                {
                    child.Measure(new Size(available.Width, double.PositiveInfinity));
                    h += child.DesiredSize.Height;
                    w = Math.Max(w, child.DesiredSize.Width);
                }
                return new Size(double.IsInfinity(available.Width) ? w : available.Width, h);
            }

            double col = (available.Width - Gap) / 2;
            var heights = new double[n];
            for (int i = 0; i < n; i++)
            {
                InternalChildren[i].Measure(new Size(col, double.PositiveInfinity));
                heights[i] = InternalChildren[i].DesiredSize.Height;
            }
            if (_split < 0 || available.Width != _splitWidth || n != _splitCount)
            {
                double total = heights.Sum(), left = 0, best = double.MaxValue;
                for (int k = 1; k < n; k++)
                {
                    left += heights[k - 1];
                    double tallest = Math.Max(left, total - left);
                    if (tallest < best) { best = tallest; _split = k; }
                }
                _splitWidth = available.Width;
                _splitCount = n;
            }
            double l = heights.Take(_split).Sum(), r = heights.Skip(_split).Sum();
            return new Size(available.Width, Math.Max(l, r));
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (!TwoColumns(final.Width) || _split < 0 || _splitCount != InternalChildren.Count)
            {
                double y = 0;
                foreach (UIElement child in InternalChildren)
                {
                    child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height));
                    y += child.DesiredSize.Height;
                }
                return final;
            }
            double col = (final.Width - Gap) / 2, top = 0;
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                if (i == _split) top = 0;
                var child = InternalChildren[i];
                child.Arrange(new Rect(i < _split ? 0 : col + Gap, top, col, child.DesiredSize.Height));
                top += child.DesiredSize.Height;
            }
            return final;
        }
    }

    private Border SettingRow(Setting s)
    {
        var host = new Border { BorderBrush = Ui.Res<Brush>("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 10) };
        void Fill()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            var title = new WrapPanel();
            title.Children.Add(new TextBlock { Text = s.Def.Label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            if (s.IsChanged)
            {
                var chip = Ui.Chip("changed", "AccentSoft", "AccentText");
                chip.Margin = new Thickness(8, 0, 0, 0);
                chip.Padding = new Thickness(6, 0, 6, 0);
                chip.VerticalAlignment = VerticalAlignment.Center;
                title.Children.Add(chip);
            }
            text.Children.Add(title);
            text.Children.Add(new TextBlock { Text = s.Def.Help, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            void Note(string? note, string brush, string? icon = null)
            {
                if (string.IsNullOrWhiteSpace(note)) return;
                var line = new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>(brush), TextWrapping = TextWrapping.Wrap };
                FrameworkElement shown = icon is null ? line : Ui.IconText(icon, line, brush);
                shown.Margin = new Thickness(0, 3, 0, 0);
                text.Children.Add(shown);
            }
            Note(s.Def.Danger, "Danger", "I.Alert");
            Note(s.IsChanged ? s.Def.Caution : null, "Warn");
            Note(s.IsInert ? s.Def.DependsOn?.Message : null, "TextFaint");
            var meta = new List<string>();
            if (Session!.DefaultOf(s) is { } std) meta.Add("default " + s.Show(std));
            if (s.IsChanged) meta.Add("in the file now: " + s.Show(s.Applied));
            var keyLine = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            keyLine.Children.Add(TextCase.Verbatim(new TextBlock { Text = $"[{s.Section}] {s.Key}", Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>("TextFaint") }));
            if (meta.Count > 0)
                keyLine.Children.Add(new TextBlock { Text = " · " + string.Join(" · ", meta), Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>("TextFaint"), TextWrapping = TextWrapping.Wrap });
            text.Children.Add(keyLine);
            grid.Children.Add(text);

            var control = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            control.Children.Add(Editor(s, Fill));
            if (s.IsChanged)
            {
                var undo = Ui.IconButton("I.Undo", "Keep the value in the file", () => { Session.Discard(s); Fill(); UpdatePending(); });
                undo.Margin = new Thickness(6, 0, 0, 0);
                control.Children.Add(undo);
            }
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            grid.Opacity = s.IsInert ? 0.6 : 1;
            host.Child = grid;
        }
        Fill();
        return host;
    }

    private FrameworkElement Editor(Setting s, Action refresh)
    {
        void Set(string value)
        {
            if (Session!.SetDraft(s, value) is { } problem)
                _app.Notify(s.Def.Label, problem, NoticeKind.Warning);
            refresh();
            UpdatePending();
        }

        if (s.IsReadOnly)
            return new TextBlock
            {
                Text = s.Show(s.Draft), Style = Ui.Res<Style>("Muted"), MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = (s.Def.ReadOnlyNote ?? "iRacing marks this setting as not to be edited.") + (s.Draft.Length > 0 ? "\n" + s.Draft : ""),
            };

        switch (s.Def.Type)
        {
            case "text" when s.Def.Pick == ValueSources.Folder:
                return FolderEditor(s, Set);
            case "text" when TextChoices(s) is { } textChoices:
            {
                var choices = textChoices.Select(c => new Choice(c.Value, c.Label, c.Tip)).ToList();
                var box = Ui.Combo(choices, s.Draft, c => { if (c.Id is { } v && v != s.Draft) Set(v); }, 180);
                box.MaxWidth = 260;
                return box;
            }
            case "bool":
                return Ui.Switch(s.IsOn(s.Draft), on => Set(on != s.Def.Invert ? "1" : "0"));
            case "enum" when s.Def.Options is { Count: > 0 } options:
            {
                var choices = options.Select(o => new Choice(Setting.Format(o.Value), o.Label, o.Tip)).ToList();
                if (!choices.Any(c => Setting.SameValue(c.Id, s.Draft)))
                    choices.Insert(0, new Choice(s.Draft, s.Draft + " (not a listed option)"));
                var selected = choices.First(c => Setting.SameValue(c.Id, s.Draft)).Id;
                return Ui.Combo(choices, selected, c => Set(c.Id ?? ""), 180);
            }
            case "num" or "range" or "enum":
            {
                CommentInference.TryNumber(s.Draft, out var n);
                int dot = s.Draft.IndexOf('.');
                int decimals = dot < 0 ? 0 : Math.Min(4, s.Draft.Length - dot - 1);
                var box = new NumberBox(n, s.Def.Unit ?? "")
                {
                    Minimum = s.Def.Min ?? -1e9, Maximum = s.Def.Max ?? 1e9, Decimals = decimals,
                    Step = s.Def.Step ?? (decimals > 0 ? Math.Pow(10, -Math.Min(decimals, 2)) : 1),
                };
                box.ValueChanged += v => { Session!.SetDraft(s, v.ToString(CultureInfo.InvariantCulture)); UpdatePending(); };
                box.LostKeyboardFocus += (_, _) => refresh();
                return box;
            }
            default:
            {
                var box = new TextBox { Text = s.Draft, Width = 220 };
                box.LostKeyboardFocus += (_, _) => { if (box.Text != s.Draft) Set(box.Text); };
                return box;
            }
        }
    }

    private List<TextChoice>? TextChoices(Setting s)
    {
        var pick = s.Def.Pick;
        if (pick is null) return null;
        if (pick == ValueSources.Choices)
            return s.Def.Choices is { Count: > 0 } ? ValueSources.Merge(s.Def.Choices, null, s.Draft) : null;
        if (!ValueSources.IsListed(pick) || _ws.Found(pick) is not { } found) return null;
        return ValueSources.Merge(s.Def.Choices, found, s.Draft);
    }

    private static StackPanel FolderEditor(Setting s, Action<string> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bool blank = s.Draft.Length == 0;
        panel.Children.Add(new TextBlock
        {
            Text = s.Show(s.Draft), Style = Ui.Res<Style>(blank ? "Muted" : "Body"), MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = blank ? s.Def.Help : s.Draft,
        });
        var browse = Ui.LabeledButton("I.Folder", "Browse", () =>
        {
            if (!GoodieBag.Services.External.Allowed("folder dialog")) return;
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = s.Def.Label };
            if (!blank && Directory.Exists(s.Draft)) dialog.InitialDirectory = s.Draft;
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.FolderName)) set(dialog.FolderName);
        }, "GhostButton");
        browse.ToolTip = "Pick a folder";
        panel.Children.Add(browse);
        if (!blank)
        {
            var reset = Ui.IconButton("I.X", "Clear: back to the default folder", () => set(""));
            reset.Margin = new Thickness(4, 0, 0, 0);
            panel.Children.Add(reset);
        }
        return panel;
    }

    private void Triples()
    {
        var ses = Session!;
        if (ses.Find(IracingPaths.MonitorRenderer, "MonitorSetup", "NumMonitors") is null)
        {
            _tab.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Child = new TextBlock { Text = "The triple-screen settings live in the Monitor graphics file (rendererDX11Monitor.ini), which isn't loaded. Pick Monitor graphics above.", Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap } });
            return;
        }

        double screen = LayoutGuard.Number(ses, "ScreenWidth", 0), monitor = LayoutGuard.Number(ses, "MonitorWidth", 0),
            dist = LayoutGuard.Number(ses, "ViewingDist", 0), angle = LayoutGuard.Number(ses, "ScreenAngles", 0), count = LayoutGuard.Number(ses, "NumMonitors", 1);
        double ideal = LayoutGuard.IdealAngle(Math.Max(screen, monitor), dist);

        var r = FovCalculator.Calculate(_fov());
        var setup = _fov();
        var labels = new Dictionary<string, string>
        {
            ["NumMonitors"] = "Screens", ["ScreenWidth"] = "Visible width of one screen", ["MonitorWidth"] = "Full width of one monitor, with bezels",
            ["ViewingDist"] = "Eyes to the center screen", ["ScreenAngles"] = "Side screen angle", ["MonitorType"] = "Curved", ["RadiusOfCurvature"] = "Curve radius",
        };
        var calc = FovExchange.IracingValues(setup, r).Select(v => (v.Key, v.Value, Label: labels.GetValueOrDefault(v.Key, v.Key))).ToList();

        var card = new StackPanel();
        card.Children.Add(Ui.Text("iRacing's screen setup", "H3"));
        card.Children.Add(new TextBlock { Text = "What iRacing uses to draw the side screens at the right angle ([MonitorSetup] in rendererDX11Monitor.ini), next to what the FOV calculator above works out for your rig.", Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) });
        var table = new Grid();
        foreach (var w in new[] { new GridLength(1, GridUnitType.Star), new GridLength(140), new GridLength(140) })
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        int row = 0;
        void Cell(string text, int col, bool head = false, string? brush = null)
        {
            var t = new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 4), TextAlignment = col == 0 ? TextAlignment.Left : TextAlignment.Right };
            if (head) t.Style = Ui.Res<Style>("Eyebrow");
            if (brush is not null) t.Foreground = Ui.Res<Brush>(brush);
            Grid.SetRow(t, row);
            Grid.SetColumn(t, col);
            table.Children.Add(t);
        }
        table.RowDefinitions.Add(new RowDefinition());
        Cell("Setting", 0, true); Cell("In iRacing", 1, true); Cell("FOV calculator", 2, true);
        int differ = 0;
        foreach (var (key, value, label) in calc)
        {
            if (ses.Find(IracingPaths.MonitorRenderer, "MonitorSetup", key) is not { } s) continue;
            row++;
            table.RowDefinitions.Add(new RowDefinition());
            bool same = Setting.SameValue(s.Draft, value);
            if (!same) differ++;
            Cell(label, 0);
            Cell(s.Show(s.Draft), 1);
            Cell(s.Show(value), 2, brush: same ? null : "AccentText");
        }
        card.Children.Add(table);
        card.Children.Add(new TextBlock
        {
            Text = count >= 3 ? $"Ideal side angle for iRacing's current values: {ideal:0}° (each side screen square-on to your eyes)." : "iRacing is set to a single screen.",
            Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 8, 0, 0),
        });
        var use = Ui.LabeledButton("I.Download", differ == 0 ? "iRacing already matches" : "Use the calculator's values", () =>
        {
            foreach (var (key, value, _) in calc)
                if (ses.Find(IracingPaths.MonitorRenderer, "MonitorSetup", key) is { } s) ses.SetDraft(s, value);
            ShowTab();
        }, "SoftButton");
        use.IsEnabled = differ > 0;
        use.HorizontalAlignment = HorizontalAlignment.Left;
        use.Margin = new Thickness(0, 12, 0, 0);
        card.Children.Add(use);
        _tab.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 12), Child = card });

        var guard = new StackPanel();
        guard.Children.Add(Ui.Text("Layout guard", "H3"));
        guard.Children.Add(new TextBlock { Text = "iRacing sometimes resets the triple layout (after a driver update, with the side screens off, or after a crash). Save yours and the Overview tells you when it changes, with one click to put it back.", Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) });
        var diffs = LayoutGuard.Differences(ses, S.SavedLayout);
        if (S.SavedLayout.Count == 0)
            guard.Children.Add(new TextBlock { Text = "No layout saved yet.", Style = Ui.Res<Style>("Caption") });
        else if (diffs.Count == 0)
            guard.Children.Add(Ui.IconText("I.Check", new TextBlock { Text = "The file matches your saved layout.", TextWrapping = TextWrapping.Wrap }, "SageDeep"));
        else
            foreach (var d in diffs)
                guard.Children.Add(new TextBlock { Text = $"{d.Setting.Def.Label}: {d.Setting.Show(d.Setting.Applied)} now, {d.Setting.Show(d.Saved)} saved", Foreground = Ui.Res<Brush>("Warn") });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var save = Ui.LabeledButton("I.Save", S.SavedLayout.Count == 0 ? "Save this layout" : "Save the current file as my layout", () =>
        {
            S.SavedLayout = LayoutGuard.Capture(ses);
            _app.Save();
            ShowTab();
        }, "SoftButton");
        buttons.Children.Add(save);
        if (diffs.Count > 0)
        {
            var restore = Ui.LabeledButton("I.Restore", "Put my layout back", () => { LayoutGuard.StageRestore(ses, S.SavedLayout); ShowTab(); }, "PrimaryButton");
            restore.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(restore);
        }
        guard.Children.Add(buttons);
        _tab.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Child = guard });
    }

    private void BackupsTab()
    {
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = "A full backup (settings, controls, calibration, cameras and profiles) is taken before every write and restore. The last 30 automatic ones are kept; ones you take stay until you delete them.",
            Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0),
        });
        var now = Ui.LabeledButton("I.Save", "Back up now", () => _ = BackupNow(), "PrimaryButton");
        now.IsEnabled = !_ws.Demo;
        now.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(now, 1);
        head.Children.Add(now);
        _tab.Children.Add(head);

        if (_ws.Demo)
        {
            _tab.Children.Add(new TextBlock { Text = "Demo: backups are made from your real iRacing folder, so there are none here.", Style = Ui.Res<Style>("Caption") });
            return;
        }

        IReadOnlyList<StrayCopy> strays;
        IReadOnlyList<Backup> backups;
        try
        {
            strays = _ws.StrayCopies();
            backups = _ws.Backups();
        }
        catch (Exception ex)
        {
            _tab.Children.Add(Ui.DangerBanner("Backups couldn't be listed", ex.Message));
            return;
        }

        if (strays.Count > 0)
            _tab.Children.Add(FindingCard(Rating.Concerning, $"{strays.Count} hand-made cop{(strays.Count == 1 ? "y" : "ies")} in your iRacing folder",
                string.Join(", ", strays.Take(4).Select(c => c.FileName)) + (strays.Count > 4 ? "…" : "") + ". Turn them into dated backups here so you can compare and restore them (the copies themselves stay where they are).",
                ("Turn into backups", () => ImportStrays(strays))));

        if (backups.Count == 0)
        {
            _tab.Children.Add(new TextBlock { Text = "No backups yet.", Style = Ui.Res<Style>("Caption") });
            return;
        }
        var list = new StackPanel();
        foreach (var b in backups)
            list.Children.Add(BackupRow(b));
        _tab.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(18, 6, 18, 6), Child = list });
        var where = TextCase.Verbatim(TextFit.TipWhenTrimmed(new TextBlock { Text = "Kept in " + _ws.Store.Root, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(2, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }));
        _tab.Children.Add(where);
    }

    private Border BackupRow(Backup b)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = $"#{b.Number}  {b.Title}", FontWeight = FontWeights.SemiBold });
        if (b.Kind != BackupKind.Auto)
        {
            var chip = Ui.Chip(b.Kind == BackupKind.Manual ? "kept" : "imported", "SlateSoft", "SlateDeep");
            chip.Margin = new Thickness(8, 0, 0, 0);
            chip.Padding = new Thickness(6, 0, 6, 0);
            title.Children.Add(chip);
        }
        text.Children.Add(title);
        text.Children.Add(new TextBlock { Text = $"{Dates.DateTime(b.Created)} · {SetupTree.Plural(b.Files.Count, "file")}", Style = Ui.Res<Style>("Caption") });
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Ui.LabeledButton("I.Eye", "Compare", () => _ = Compare(b), "GhostButton"));
        var restore = Ui.LabeledButton("I.Restore", "Restore", () => _ = Restore(b), "GhostButton");
        restore.Margin = new Thickness(6, 0, 2, 0);
        buttons.Children.Add(restore);
        buttons.Children.Add(Ui.IconButton("I.Trash", "Delete this backup", () => _ = DeleteBackup(b)));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return new Border { BorderBrush = Ui.Res<Brush>("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 9, 0, 9), Child = grid };
    }

    private async Task BackupNow()
    {
        var name = await _window.Prompt("Back up iRacing's settings", "A name to recognize it by (optional).", "", _ => null);
        if (name is null || _ws.Folder is not { } folder) return;
        try
        {
            var b = _ws.Store.Create("Taken by hand", IracingPaths.BackupFiles(folder), folder, BackupKind.Manual, name);
            _app.Notify($"Backup #{b.Number} taken", $"{SetupTree.Plural(b.Files.Count, "file")}.", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't back up", ex.Message, NoticeKind.Warning, important: true);
        }
        ShowTab();
    }

    private void ImportStrays(IReadOnlyList<StrayCopy> strays)
    {
        int done = 0;
        foreach (var copy in strays)
        {
            try { _ws.Store.Import(copy, _ws.Folder!); done++; }
            catch { }
        }
        _app.Notify("Copies imported", $"{done} of {strays.Count} turned into backups.", NoticeKind.Success);
        ShowTab();
    }

    private async Task Compare(Backup b)
    {
        var list = new StackPanel();
        try
        {
            var diffs = BackupStore.Compare(b, _ws.Folder);
            foreach (var d in diffs.Where(d => d.State != FileState.Same))
            {
                list.Children.Add(new TextBlock { Text = d.Name + (d.State == FileState.MissingNow ? " (missing now)" : ""), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
                foreach (var k in d.Keys.Take(60))
                    list.Children.Add(new TextBlock { Text = $"[{k.Section}] {k.Key}: {k.Then ?? "(not there)"}  →  now {k.Now ?? "(not there)"}", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
                if (d.Keys.Count > 60)
                    list.Children.Add(new TextBlock { Text = $"+{d.Keys.Count - 60} more", Style = Ui.Res<Style>("Caption") });
            }
            if (list.Children.Count == 0)
                list.Children.Add(new TextBlock { Text = "No differences: your files match this backup.", Style = Ui.Res<Style>("Muted") });
        }
        catch (Exception ex)
        {
            list.Children.Add(Ui.DangerBanner("The backup couldn't be compared with your files", ex.Message));
        }
        var close = new StackPanel();
        var ok = new Button { Content = "Close", Style = Ui.Res<Style>("PrimaryButton") };
        ok.Click += (_, _) => _window.CloseModal(false);
        close.Children.Add(ok);
        await _window.ShowModal(Ui.Dialog(_window, $"Backup #{b.Number} compared with now", "What restoring it would change (backup → current value).", list, close, null, 680));
    }

    private async Task Restore(Backup b)
    {
        if (_ws.SimRunning)
        {
            _app.Notify("Close iRacing first", "It rewrites its settings when it closes, which would undo the restore.", NoticeKind.Warning, important: true);
            return;
        }
        if (!await ConfirmDropPending()) return;
        if (!await _window.Confirm($"Restore backup #{b.Number}?", $"Puts back {SetupTree.Plural(b.Files.Count, "file")} from {Dates.DateTime(b.Created)}. Your current files are backed up first, so this can be undone.", "Restore"))
            return;
        if (_ws.SimRunning)
        {
            _app.Notify("Close iRacing first", "It rewrites its settings when it closes, which would undo the restore.", NoticeKind.Warning, important: true);
            return;
        }
        var layoutBefore = Session is { } current ? LayoutGuard.Capture(current) : new Dictionary<string, string>();
        try
        {
            var before = _ws.Store.Restore(b, _ws.Folder);
            _app.Notify($"Backup #{b.Number} restored", $"Backup #{before.Number} has the files from before.", NoticeKind.Success);
            _ws.Load();
            if (Session is { } restored)
            {
                var changed = LayoutGuard.Capture(restored).Where(kv => !layoutBefore.TryGetValue(kv.Key, out var was) || !Setting.SameValue(was, kv.Value)).Select(kv => kv.Key).ToList();
                LayoutGuard.Adopt(restored, S.SavedLayout, changed);
                _app.Save();
            }
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't restore", ex.Message, NoticeKind.Warning, important: true);
            _ws.Load();
        }
        Build();
    }

    private async Task DeleteBackup(Backup b)
    {
        if (!await _window.Confirm($"Delete backup #{b.Number}?", "Its copies of your files are removed from Goodie Bag's backup folder. Your iRacing files aren't touched.", "Delete", danger: true))
            return;
        try { BackupStore.Delete(b); }
        catch (Exception ex) { _app.Notify("Couldn't delete the backup", ex.Message, NoticeKind.Warning, important: true); }
        ShowTab();
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        for (int i = 1; i < Tabs.Length; i++)
        {
            int tab = i;
            yield return (Tabs[i].Replace(' ', '-').ToLowerInvariant(), () => { _tabIndex = tab; Build(); return Task.CompletedTask; });
        }
        yield return ("search", () => { _tabIndex = 0; _search = "fps"; Build(); return Task.CompletedTask; });
        yield return ("back-to-start", () => { _tabIndex = 0; _search = ""; Build(); return Task.CompletedTask; });
    }
}

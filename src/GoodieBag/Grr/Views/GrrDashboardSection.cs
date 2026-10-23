using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.Theme;
using GoodieBag.Views;

namespace GoodieBag.Grr.Views;

internal sealed class GrrDashboardSection : StackPanel
{
    private const string Consumer = "GRR League dashboard";
    private const double TwoColumnsAt = 900, Gap = 16;
    private const int TopRows = 10;
    private const int ShowAllUpTo = 25;

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly ContentControl _leaguePicker = new() { Focusable = false };
    private readonly TextBlock _freshness = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly Button _refresh;
    private readonly ContentControl _content = new() { Focusable = false };
    private TextBlock? _heroClock;
    private bool _heroLive;
    private string? _freshnessText;
    private GrrRace? _heroRace;
    private string _shape = "";
    private bool _held, _allStandings, _fullSeason, _allResults;
    private string? _eventKey;
    private int _session;
    private string _builtLeague = "";
    private FrameworkElement? _standingsCard, _resultsCard, _scheduleCard;

    internal enum Part { Top, Standings, Results, Schedule }

    internal FrameworkElement? CardFor(Part part)
    {
        if (_held && _builtLeague != (League ?? "")) { _shape = ""; Build(); }
        return part switch { Part.Standings => _standingsCard, Part.Results => _resultsCard, Part.Schedule => _scheduleCard, _ => null };
    }

    public GrrDashboardSection(AppServices app, MainWindow window, bool leagueTabs = true)
    {
        _app = app;
        _window = window;
        _refresh = GrrParts.RefreshButton(app);
        _refresh.Margin = new Thickness(0);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_freshness);
        right.Children.Add(_refresh);
        var edit = GrrParts.Compact("I.Pencil", "Driver & leagues", () => _ = GrrParts.EditAsync(_app, _window));
        edit.Margin = new Thickness(8, 0, 0, 0);
        edit.ToolTip = "Your driver name on the GRR website and the leagues you follow";
        right.Children.Add(edit);
        LeagueTabs = leagueTabs;
        if (leagueTabs)
        {
            var toolbar = Ui.SplitRow(_leaguePicker, right);
            toolbar.Margin = new Thickness(0, 0, 0, Gap);
            Children.Add(toolbar);
        }
        else
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            Tools = right;
        }
        Children.Add(_content);
        SizeChanged += (_, e) => { if (e.WidthChanged && Shape() != _shape) Build(); };
    }

    internal FrameworkElement? Tools { get; }

    private string? League => _app.Config.Grr.DashboardLeagueOrNull();

    internal bool LeagueTabs { get; }

    internal void ShowLeague()
    {
        _shape = "";
        Build();
    }
    private string? Driver => _app.Config.Grr.DriverName;

    public void OnShown()
    {
        if (_held) return;
        _held = true;
        _app.Grr.Changed += OnChanged;
        _app.Tick += OnTick;
        _app.Grr.Acquire(Consumer);
        _shape = "";
        Build();
    }

    public void OnHidden()
    {
        if (!_held) return;
        _held = false;
        _app.Grr.Changed -= OnChanged;
        _app.Tick -= OnTick;
        _app.Grr.Release(Consumer);
    }

    private void OnChanged()
    {
        _shape = "";
        Build();
    }

    private void OnTick()
    {
        var now = DateTime.Now;
        if (_heroRace is { } race && _heroClock is not null)
        {
            if (race.Start + GrrService.RaceLength <= now || race.Start <= now && !_heroLive)
            {
                _shape = "";
                Build();
                return;
            }
            if (!_heroLive) _heroClock.Text = GrrLeagues.Countdown(GrrLeagues.Until(race.Start, now));
        }
        UpdateFreshness();
    }

    private double Width0 => ActualWidth > 0 ? ActualWidth : 1100;
    private bool TwoColumns => Width0 >= TwoColumnsAt;
    private double CardWidth => TwoColumns ? (Width0 - Gap) / 2 : Width0;

    private static int Level(double width) => width < 430 ? 1 : width < 600 ? 2 : width < 800 ? 3 : 4;

    private string Shape() => $"{League}|{TwoColumns}|{Level(CardWidth - 40)}";

    private void Build()
    {
        if (!_held) return;
        var shape = Shape();
        var league = League;
        if (_builtLeague != (league ?? "")) { _eventKey = null; _session = 0; _allStandings = _allResults = false; _builtLeague = league ?? ""; }
        _shape = shape;
        _heroClock = null;
        _heroRace = null;
        _leaguePicker.Content = LeagueTabs ? LeaguePicker(league) : null;
        UpdateFreshness();
        if (league is null)
        {
            _content.Content = NoLeague();
            return;
        }

        var grr = _app.Grr;
        var data = grr.Data(league);
        var now = DateTime.Now;
        FrameworkElement next, driver, standings, results, schedule;
        if (data is null)
        {
            next = Card("Next race", "I.Flag", Empty(grr));
            driver = Card("Driver profile", null, GrrParts.Placeholder(140, 90));
            standings = Card("Championship", "I.Trophy", grr.Busy ? GrrParts.Placeholder(220, 260, 240, 260) : Note("Shows once the league has been read."));
            results = Card("Results", "I.FileText", grr.Busy ? GrrParts.Placeholder(200, 240, 220) : Note("Shows once the league has been read."));
            schedule = Card("Schedule", "I.Calendar", grr.Busy ? GrrParts.Placeholder(180, 200, 180) : Note("Shows once the league has been read."));
            if (!grr.Busy) driver = Card("Driver profile", null, Note("Shows once the league has been read."));
        }
        else
        {
            next = NextRaceCard(data, now);
            driver = DriverCard(data);
            standings = StandingsCard(data);
            results = ResultsCard(data);
            schedule = ScheduleCard(data, now, strip: TwoColumns);
        }
        _standingsCard = standings;
        _resultsCard = results;
        _scheduleCard = schedule;

        if (TwoColumns)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            void Place(FrameworkElement card, int row, int col, int span = 1)
            {
                while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                card.Margin = new Thickness(0, row == 0 ? 0 : Gap, 0, 0);
                Grid.SetRow(card, row);
                Grid.SetColumn(card, col);
                Grid.SetColumnSpan(card, span);
                grid.Children.Add(card);
            }
            Place(next, 0, 0);
            Place(driver, 0, 2);
            Place(standings, 1, 0);
            Place(results, 1, 2);
            Place(schedule, 2, 0, 3);
            _content.Content = grid;
        }
        else _content.Content = Column(next, driver, standings, results, schedule);
    }

    private FrameworkElement NoLeague()
    {
        var body = new StackPanel();
        body.Children.Add(Note("Pick the GRR series you race in. Their tabs above unlock, with their next race, standings, results and schedule."));
        var pick = GrrParts.Compact("I.Plus", "Choose leagues", () => _ = GrrParts.EditAsync(_app, _window), "PrimaryButton");
        pick.HorizontalAlignment = HorizontalAlignment.Left;
        pick.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(pick);
        return Card("No league picked", "I.Flag", body);
    }

    private static StackPanel Column(params FrameworkElement[] cards)
    {
        var stack = new StackPanel();
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i].Margin = new Thickness(0, i == 0 ? 0 : Gap, 0, 0);
            stack.Children.Add(cards[i]);
        }
        return stack;
    }

    private FrameworkElement LeaguePicker(string? league)
    {
        var followed = _app.Config.Grr.Leagues;
        bool Follows(GrrLeague l) => followed.Contains(l.Id, StringComparer.OrdinalIgnoreCase);
        int selected = league is null ? -1 : GrrLeagues.All.ToList().FindIndex(l => l.Id == league);
        var tips = GrrLeagues.All.Select(l => (string?)(Follows(l) ? l.Name : $"{l.Name}: tick it under Driver & leagues to see it here")).ToList();
        var picker = Ui.Segmented(GrrLeagues.All.Select(l => l.Name).ToList(), selected, i =>
        {
            if (!Follows(GrrLeagues.All[i])) return;
            _app.Config.Grr.DashboardLeague = GrrLeagues.All[i].Id;
            _app.Save();
            _shape = "";
            Dispatcher.InvokeAsync(Build);
        }, "grrleague", tips);
        if (picker.Child is Panel tabs)
            for (int i = 0; i < tabs.Children.Count && i < GrrLeagues.All.Count; i++)
                if (tabs.Children[i] is RadioButton tab)
                {
                    tab.IsEnabled = Follows(GrrLeagues.All[i]);
                    ToolTipService.SetShowOnDisabled(tab, true);
                    System.Windows.Automation.AutomationProperties.SetName(tab, GrrLeagues.All[i].Name + (tab.IsEnabled ? "" : " (not followed)"));
                }
        picker.VerticalAlignment = VerticalAlignment.Center;
        return picker;
    }

    private void UpdateFreshness()
    {
        var grr = _app.Grr;
        GrrParts.SyncRefresh(_refresh, _app);
        string text; string brush = "TextMuted"; string? tip = null;
        if (League is null) text = "";
        else if (grr.Busy) text = "Updating…";
        else if (grr.ProblemOf(League!) is { } problem)
        {
            text = grr.FetchedAt(League!) is { } t ? $"Couldn't update · from {GrrParts.Ago(t)}" : "Couldn't reach the GRR website";
            brush = "WarnDeep";
            tip = problem;
        }
        else text = grr.FetchedAt(League!) is { } t ? $"Updated {GrrParts.Ago(t)}" : "Not read yet";
        if (_freshnessText != text) { _freshnessText = text; _freshness.Text = text; }
        _freshness.Foreground = Ui.Res<Brush>(brush);
        _freshness.FontSize = 12.5;
        _freshness.ToolTip = tip;
    }

    private static Tile Card(string title, string? icon, UIElement body, params FrameworkElement[] tools)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14), LastChildFill = true };
        var toolRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var t in tools)
        {
            t.Margin = new Thickness(8, 0, 0, 0);
            toolRow.Children.Add(t);
        }
        DockPanel.SetDock(toolRow, Dock.Right);
        head.Children.Add(toolRow);
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null)
            name.Children.Add(new Icon { Data = Ui.Res<Geometry>(icon), Width = 16, Height = 16, Foreground = Ui.Res<Brush>("AccentText"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        name.Children.Add(Ui.Text(title, "H3"));
        head.Children.Add(name);
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        return new Tile { Style = Ui.Res<Style>("Card"), Child = stack };
    }

    private static TextBlock Note(string text) => GrrParts.Wrapped(text);

    private FrameworkElement Empty(GrrService grr) =>
        grr.Busy ? GrrParts.Placeholder(160, 210, 240, 180)
        : grr.ProblemOf(League!) is { } problem ? GrrParts.Problem(_app, "Couldn't reach the GRR website.", problem)
        : Note("Not read from the GRR website yet.");

    private FrameworkElement Footer(params FrameworkElement[] items)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var i in items) row.Children.Add(i);
        return row;
    }

    private Button Toggle(string label, Action flip)
    {
        var b = new Button { Content = label, Style = Ui.Res<Style>("Btn"), Padding = new Thickness(10, 4, 10, 4), FontSize = 12, Margin = new Thickness(0, 0, 6, 6) };
        b.Click += (_, _) => { flip(); _shape = ""; Build(); };
        return b;
    }

    private GrrLeague LeagueInfo => GrrLeagues.Find(League!)!;

    private Tile NextRaceCard(GrrLeagueData data, DateTime now)
    {
        var body = new StackPanel();
        var round = data.Schedule?.FirstOrDefault(r => r.Start + GrrService.RaceLength > now);
        if (round is null)
            body.Children.Add(Note(data.Schedule is null ? "The league's feed has no schedule." : "No more races on this season's schedule."));
        else
        {
            var race = new GrrRace(LeagueInfo.Id, round.Start, round.Track, round.Number is { } n ? $"Round {n}" : null);
            body.Children.Add(GrrParts.Hero(race, data, now, out var clock, 38));
            _heroClock = clock;
            _heroRace = race;
            _heroLive = race.Start <= now;
        }
        if (data.Schedule is { Count: > 0 } s)
        {
            int done = s.Count(r => r.Start + GrrService.RaceLength <= now);
            var progress = new Grid { Margin = new Thickness(17, 16, 0, 0) };
            progress.ColumnDefinitions.Add(new ColumnDefinition());
            progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            progress.Children.Add(new Meter { Height = 6, Steps = s.Count, StepsUseFill = true, Value = (double)done / s.Count, Fill = Ui.Res<Brush>("Accent"), VerticalAlignment = VerticalAlignment.Center });
            var label = GrrParts.Caption($"{done} of {GoodieBag.SimRacing.Race.RaceFormat.Count(s.Count, "round")} run");
            label.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(label, 1);
            progress.Children.Add(label);
            body.Children.Add(progress);
        }
        var season = data.Season is { } name ? new TextBlock { Text = name, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center } : null;
        return season is null ? Card("Next race", "I.Flag", body) : Card("Next race", "I.Flag", body, season);
    }

    private Tile DriverCard(GrrLeagueData data)
    {
        var body = new StackPanel();
        var driver = Driver;
        if (driver is null)
        {
            body.Children.Add(Note("Add your driver name as it appears on the GRR website to see your championship here and be highlighted in the standings and results."));
            var add = GrrParts.Compact("I.Plus", "Add driver name", () => _ = GrrParts.EditAsync(_app, _window));
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(add);
            return Card("Driver profile", null, body);
        }
        var mine = data.StandingOf(driver);
        body.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = mine?.Row.Driver ?? driver, Style = Ui.Res<Style>("H2"), TextTrimming = TextTrimming.CharacterEllipsis }));
        body.Children.Add(GrrParts.Caption(mine is { } m
            ? string.Join(" · ", new[] { data.ClassLabel(m.Class), m.Row.Car, m.Row.Team }.Where(x => !string.IsNullOrEmpty(x))) is { Length: > 0 } sub ? sub : LeagueInfo.Name
            : $"Not in the {LeagueInfo.Name} standings"));

        if (mine is { } me)
        {
            var table = data.Standings![me.Class];
            var main = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            main.Children.Add(Stat("Championship", GrrParts.Position(me.Row.Rank, true, 30, prefix: true, plain: "AccentText"), $"of {table.Count}", big: true));
            main.Children.Add(Stat("Points", GrrParts.Points(me.Row.Points),
                table[0] == me.Row ? "Leader" : $"{GrrParts.Points(table[0].Points - me.Row.Points)} behind P1", big: true));
            body.Children.Add(main);
            var record = new WrapPanel();
            if (me.Row.Wins is { } w) record.Children.Add(Stat("Wins", $"{w}"));
            if (me.Row.Podiums is { } p) record.Children.Add(Stat("Podiums", $"{p}"));
            else if (me.Row.Top5 is { } t5) record.Children.Add(Stat("Top 5", $"{t5}"));
            if (me.Row.Poles is { } poles) record.Children.Add(Stat("Poles", $"{poles}"));
            if (me.Row.Starts is { } st) record.Children.Add(Stat("Starts", $"{st}"));
            if (data.LastEvent is { } e && GrrLeagueData.ResultOf(e, driver) is { } r)
                record.Children.Add(Stat("Last race", GrrParts.Position(r.Position, false, 16, prefix: true, plain: "Heading"), data.RoundFor(e)?.Number is { } n ? $"Round {n}" : e.Track));
            if (record.Children.Count > 0)
            {
                body.Children.Add(new Border { Height = 1, Background = Ui.Res<Brush>("Line"), Margin = new Thickness(0, 2, 0, 12) });
                body.Children.Add(record);
            }
        }
        body.Children.Add(Footer(GrrParts.Link("Driver history", GrrLeagues.DriverUrl, GrrParts.DriverHistoryTip(driver), () => GrrParts.OpenDriverHistory(driver))));
        return Card("Driver profile", null, body);
    }

    private static StackPanel Stat(string label, string value, string? note = null, bool big = false)
    {
        var v = GrrParts.Clock(big ? 30 : 16);
        v.Text = value;
        return Stat(label, v, note, big);
    }

    private static StackPanel Stat(string label, FrameworkElement v, string? note = null, bool big = false)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, big ? 36 : 24, 10), MinWidth = big ? 90 : 48 };
        s.Children.Add(GrrParts.Eyebrow(label));
        v.Margin = new Thickness(0, 2, 0, 0);
        v.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(v);
        if (note is not null) s.Children.Add(GrrParts.Caption(note));
        return s;
    }

    private sealed record Col<T>(string Header, double Width, Func<T, string?> Value, int Level, bool Right = true, string? Tip = null, bool Fill = false);

    private Tile StandingsCard(GrrLeagueData data)
    {
        if (data.Standings is null || data.Standings.Count == 0)
            return Card("Championship", "I.Trophy", Note(data.Standings is null ? "Couldn't read the standings from the league's feed." : "No standings posted yet."),
                GrrParts.Link("Standings", LeagueInfo.Url(LeagueInfo.StandingsPage)));
        var keys = data.StandingKeys.ToList();
        var picked = _app.Config.Grr.DashboardClass;
        string key = picked is not null && keys.Contains(picked) ? picked : data.StandingOf(Driver)?.Class ?? keys[0];
        var table = data.Standings[key];
        var leader = table.FirstOrDefault();
        var columns = new List<Col<GrrStanding>>
        {
            new("Pts", 62, r => GrrParts.Points(r.Points), 1),
            new("Gap", 62, r => leader is null || r == leader ? "–" : leader.Points == r.Points ? "0" : "−" + GrrParts.Points(leader.Points - r.Points), 2, Tip: "Points behind the leader"),
            new("Wins", 50, r => r.Wins?.ToString(), 2),
            new("Pod", 50, r => r.Podiums?.ToString(), 2, Tip: "Podiums"),
            new("Top 5", 54, r => r.Top5?.ToString(), 2),
            new("Top 10", 58, r => r.Top10?.ToString(), 3),
            new("Poles", 52, r => r.Poles?.ToString(), 3),
            new("Starts", 64, r => r.Starts?.ToString(), 3),
            new("Led", 56, r => r.LapsLed?.ToString(), 4, Tip: "Laps led"),
            new("Team", 120, r => r.Team, 3, Right: false, Fill: true),
        };
        int level = Level(CardWidth - 40);
        columns = columns.Where(c => c.Level <= level && table.Any(r => c.Value(r) is not null)).ToList();
        columns = columns.Skip(1).Concat(columns.Take(1)).ToList();

        bool small = table.Count <= ShowAllUpTo;
        var rows = Visible(table, r => GrrFeed.SameDriver(r.Driver, Driver), small || _allStandings, out _);
        var body = new StackPanel();
        body.Children.Add(Table(rows, r => r.Rank, r => r.Driver, r => level >= 3 ? r.Car : null, columns,
            r => GrrFeed.SameDriver(r.Driver, Driver)));
        var footer = new List<FrameworkElement>();
        if (!small) footer.Add(Toggle(_allStandings ? $"Show top {TopRows}" : $"Show all {table.Count}", () => _allStandings = !_allStandings));
        footer.Add(GrrParts.Link("Full standings", LeagueInfo.Url(LeagueInfo.StandingsPage)));
        body.Children.Add(Footer(footer.ToArray()));

        var tools = new List<FrameworkElement>();
        if (keys.Count > 1)
        {
            var classes = Ui.Segmented(keys.Select(data.ClassLabel).ToList(), keys.IndexOf(key), i =>
            {
                _app.Config.Grr.DashboardClass = keys[i];
                _app.Save();
                _shape = "";
                Dispatcher.InvokeAsync(Build);
            }, "grrclass");
            tools.Add(classes);
        }
        return Card("Championship", "I.Trophy", body, tools.ToArray());
    }

    private static List<T?> Visible<T>(IReadOnlyList<T> rows, Func<T, bool> mine, bool all, out bool more) where T : class
    {
        more = rows.Count > TopRows;
        if (all || !more) return rows.Cast<T?>().ToList();
        var list = rows.Take(TopRows).Cast<T?>().ToList();
        if (rows.Skip(TopRows).FirstOrDefault(mine) is { } me)
        {
            list.Add(null);
            list.Add(me);
        }
        return list;
    }

    private const double FillGap = 16;

    private static Grid Table<T>(List<T?> rows, Func<T, int> pos, Func<T, string> name, Func<T, string?> second, List<Col<T>> columns, Func<T, bool> mine, Func<T, FrameworkElement?>? marks = null) where T : class
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        bool fill = columns.Any(c => c.Fill);
        grid.ColumnDefinitions.Add(fill
            ? new ColumnDefinition { Width = GridLength.Auto, MinWidth = 120, MaxWidth = 260 }
            : new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        foreach (var c in columns)
            grid.ColumnDefinitions.Add(c.Fill ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = c.Width } : new ColumnDefinition { Width = new GridLength(c.Width) });
        int span = columns.Count + 2;

        void Cell(UIElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); grid.Children.Add(e); }
        TextBlock Head(string text, bool right) => TextFit.TipWhenTrimmed(new TextBlock
        {
            Text = text, Style = Ui.Res<Style>("Eyebrow"), TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
            Margin = new Thickness(0, 0, right ? 10 : 0, 6), TextTrimming = TextTrimming.CharacterEllipsis,
        });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Cell(Head("Pos", false), 0, 0);
        Cell(Head("Driver", false), 0, 1);
        for (int c = 0; c < columns.Count; c++)
        {
            var h = Head(columns[c].Header, columns[c].Right);
            h.ToolTip = columns[c].Tip;
            if (columns[c].Fill) h.Margin = new Thickness(FillGap, 0, 0, 6);
            Cell(h, 0, c + 2);
        }
        var rule = new Border { Height = 1, Background = Ui.Res<Brush>("Line"), VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumnSpan(rule, span);
        Cell(rule, 0, 0);

        var hover = Ui.Res<Brush>("Hover");
        for (int i = 0; i < rows.Count; i++)
        {
            int r = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (rows[i] is not { } row)
            {
                var gap = new TextBlock { Text = "⋯", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumnSpan(gap, span);
                Cell(gap, r, 0);
                continue;
            }
            bool me = mine(row);
            var back = new Border { Background = me ? Ui.Res<Brush>("AccentSoft") : Brushes.Transparent, CornerRadius = ThemeManager.Round(4) };
            if (!me)
            {
                back.MouseEnter += (_, _) => back.Background = hover;
                back.MouseLeave += (_, _) => back.Background = Brushes.Transparent;
            }
            Grid.SetColumnSpan(back, span);
            Cell(back, r, 0);
            if (i < rows.Count - 1 && !me && !(rows[i + 1] is { } below && mine(below)))
            {
                var hairline = new Border { Height = 1, Background = Ui.Res<Brush>("Line"), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Margin = new Thickness(8, 0, 8, 0) };
                Grid.SetColumnSpan(hairline, span);
                Cell(hairline, r, 0);
            }
            string fg = me ? "AccentText" : "Text";
            var weight = me ? FontWeights.SemiBold : FontWeights.Normal;
            var posCell = GrrParts.Position(pos(row), me);
            posCell.Margin = new Thickness(8, 7, 0, 7);
            posCell.IsHitTestVisible = pos(row) <= 3;
            Cell(posCell, r, 0);

            var driverCell = new Grid { Margin = new Thickness(0, 6, 8, 6) };
            driverCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            driverCell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            who.Children.Add(new TextBlock { Text = name(row), FontSize = 13.5, FontWeight = weight, Foreground = Ui.Res<Brush>(fg), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name(row) });
            if (second(row) is { } sub) who.Children.Add(GrrParts.Caption(sub));
            driverCell.Children.Add(who);
            if (marks?.Invoke(row) is { } m)
            {
                m.Margin = new Thickness(16, 0, 0, 0);
                m.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(m, 1);
                driverCell.Children.Add(m);
            }
            Cell(driverCell, r, 1);
            for (int c = 0; c < columns.Count; c++)
            {
                var col = columns[c];
                var t = TextFit.TipWhenTrimmed(new TextBlock
                {
                    Text = col.Value(row) ?? "–", FontSize = 13, FontWeight = weight, Foreground = Ui.Res<Brush>(col == columns[^1] && !me ? "Heading" : col.Right ? (me ? fg : "TextMuted") : fg),
                    TextAlignment = col.Right ? TextAlignment.Right : TextAlignment.Left, Margin = new Thickness(0, 0, col.Right ? 10 : 8, 0), VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false, Typography = { NumeralAlignment = FontNumeralAlignment.Tabular },
                });
                if (col == columns[^1]) t.FontWeight = FontWeights.SemiBold;
                if (col.Fill)
                {
                    t.Margin = new Thickness(FillGap, 6, 8, 6);
                    t.TextWrapping = TextWrapping.Wrap;
                    t.TextTrimming = TextTrimming.None;
                }
                Cell(t, r, c + 2);
            }
        }
        return grid;
    }

    private Tile ResultsCard(GrrLeagueData data)
    {
        var link = GrrParts.Link("Latest results", LeagueInfo.Url(LeagueInfo.ResultsPage), "The latest race's results on the GRR website");
        var events = (data.Events ?? Array.Empty<GrrEvent>()).Where(e => e.Sessions.Any(s => s.Rows.Count > 0)).Reverse().ToList();
        if (events.Count == 0)
            return Card("Results", "I.FileText", Note(data.Events is null ? "Couldn't read the results from the league's feed." : "No results posted yet this season."), link);

        var ev = events.FirstOrDefault(e => Key(e) == _eventKey) ?? events[0];
        var round = data.RoundFor(ev);
        var picker = new ComboBox { MinWidth = 200, MaxWidth = 300 };
        foreach (var e in events)
        {
            var r = data.RoundFor(e);
            picker.Items.Add(new ComboBoxItem { Content = $"{(r?.Number is { } n ? $"Round {n} · " : "")}{e.Track}", Tag = Key(e), IsSelected = e == ev, ToolTip = GrrParts.LongDate(e.Date) });
        }
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is ComboBoxItem { Tag: string k } && k != Key(ev))
            {
                _eventKey = k;
                _session = 0;
                _allResults = false;
                _shape = "";
                Dispatcher.InvokeAsync(Build);
            }
        };
        picker.ToolTip = "Results of another round";

        var body = new StackPanel();
        var sessions = ev.Sessions.Where(s => s.Rows.Count > 0).ToList();
        int si = Math.Clamp(_session, 0, sessions.Count - 1);
        if (sessions.Count > 1)
        {
            var tabs = Ui.Segmented(sessions.Select(s => s.Label).ToList(), si, i =>
            {
                _session = i;
                _allResults = false;
                _shape = "";
                Dispatcher.InvokeAsync(Build);
            }, "grrsession");
            tabs.HorizontalAlignment = HorizontalAlignment.Left;
            tabs.Margin = new Thickness(0, 0, 0, 12);
            body.Children.Add(tabs);
        }
        var session = sessions[si];
        var winners = round?.Winners.Count > 1 ? " · " + string.Join(" · ", round.Winners.Select(w => $"{w.Class} {w.Driver}")) : "";
        body.Children.Add(GrrParts.Caption($"{GrrParts.LongDate(ev.Date)}{(round?.Format is { } f ? $" · {char.ToUpperInvariant(f[0])}{f[1..]}" : "")}{(round?.Pole is { } pole ? $" · pole {pole}" : "")}", 0));
        if (winners.Length > 0) body.Children.Add(GrrParts.Caption("Winners" + winners, 2));

        int level = Level(CardWidth - 40);
        var rows = session.Rows;
        var columns = new List<Col<GrrResult>>
        {
            new("Pts", 50, r => r.Points is { } p ? GrrParts.Points(p) : null, 1),
            new("Class", 70, r => r.Class, 2, Right: false),
            new("Gap", 74, r => r.Position == 1 ? "" : r.Interval, 2, Tip: "Gap to the car ahead or the leader, as posted"),
            new("Grid", 46, r => r.Start?.ToString(), 3, Tip: "Starting position"),
            new("Led", 46, r => r.Led?.ToString(), 3, Tip: "Laps led"),
            new("Inc", 42, r => r.Incidents?.ToString(), 4, Tip: "Incidents"),
            new("Laps", 46, r => r.Laps?.ToString(), 4),
        };
        columns = columns.Where(c => c.Level <= level && rows.Any(r => !string.IsNullOrEmpty(c.Value(r)))).ToList();
        columns = columns.Skip(1).Concat(columns.Take(1)).ToList();
        var visible = Visible(rows, r => GrrFeed.SameDriver(r.Driver, Driver), _allResults, out bool more);
        body.Children.Add(new Border { Height = 10 });
        bool classed = data.Classes.Count > 0;
        string? tableClass = data.ClassNamed(session.Label);
        body.Children.Add(Table(visible, r => r.Position, r => r.Driver, r => level >= 2 ? r.Car : null, columns, r => GrrFeed.SameDriver(r.Driver, Driver), Marks));
        var footer = new List<FrameworkElement>();
        if (more || _allResults) footer.Add(Toggle(_allResults ? $"Show top {TopRows}" : $"Show all {rows.Count}", () => _allResults = !_allResults));
        footer.Add(link);
        body.Children.Add(Footer(footer.ToArray()));
        return Card("Results", "I.FileText", body, picker);

        FrameworkElement? Marks(GrrResult r)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (r.Pole) row.Children.Add(GrrParts.Badge("Pole", "Slate", "Pole position: started first"));
            if (r.FastestLap)
            {
                string? cls = classed ? r.Class ?? tableClass : null;
                row.Children.Add(GrrParts.Badge(GrrParts.FastestLapLabel(cls), "Plum", cls is null ? "Fastest lap of the race" : $"Fastest lap in {cls}"));
            }
            if (r.Penalty is double pen && pen > 0) row.Children.Add(GrrParts.Badge("Pen", "Danger", $"Penalty: {GrrParts.Points(pen)} points taken off"));
            if (r.Status is { } status && !status.Equals("Running", StringComparison.OrdinalIgnoreCase))
                row.Children.Add(GrrParts.Badge(status.Length > 12 ? status[..12] : status, "Warn", $"Finished as: {status}"));
            return row.Children.Count > 0 ? row : null;
        }
    }

    private static string Key(GrrEvent e) => e.Key ?? e.Date.ToString("yyyy-MM-dd");

    private Tile ScheduleCard(GrrLeagueData data, DateTime now, bool strip)
    {
        var link = GrrParts.Link("Full schedule", LeagueInfo.Url(LeagueInfo.SchedulePage));
        if (data.Schedule is not { Count: > 0 } rounds)
            return Card("Schedule", "I.Calendar", Note(data.Schedule is null ? "Couldn't read the schedule from the league's feed." : "No rounds on the schedule yet."), link);

        int next = rounds.ToList().FindIndex(r => r.Start + GrrService.RaceLength > now);
        int from = 0, to = rounds.Count;
        if (!_fullSeason)
        {
            int anchor = next < 0 ? rounds.Count - 1 : next;
            from = Math.Max(0, anchor - 2);
            to = Math.Min(rounds.Count, anchor + 4);
        }
        var list = new StackPanel();
        for (int i = from; i < to; i++)
        {
            var r = rounds[i];
            bool past = r.Start + GrrService.RaceLength <= now;
            var mark = i == next ? GrrParts.TimelineMark.Next : past ? GrrParts.TimelineMark.Done : GrrParts.TimelineMark.Later;
            var eyebrow = (r.Number is { } n ? $"Round {n}" : "Round") + (r.Format is { } f ? " · " + f : "") + (i == next ? (r.Start <= now ? " · racing now" : " · next") : "");
            FrameworkElement? right = null;
            Action? click = null;
            object? tipContent = null;
            if (past)
            {
                var outcome = data.ClassResults(r);
                var winners = outcome.Where(o => o.Winner is not null).ToList();
                right = winners.Count switch
                {
                    0 => new TextBlock { Text = r.Done ? "Run" : "No result", Style = Ui.Res<Style>("Caption") },
                    1 => Winner(winners[0].Winner!, winners[0].FastestLap is { } fl && GrrFeed.SameDriver(fl, winners[0].Winner!)),
                    _ => Winner($"{winners[0].Winner} +{winners.Count - 1}", winners[0].FastestLap is { } fl2 && GrrFeed.SameDriver(fl2, winners[0].Winner!)),
                };
                var e = data.EventFor(r);
                if (e is not null)
                    click = () => { _eventKey = Key(e); _session = 0; _allResults = false; _shape = ""; Build(); ShowResults(); };
                tipContent = GrrParts.RoundTip(outcome, e is null ? null : "Click to see the results");
            }
            else if (i == next)
            {
                var c = GrrParts.Clock(13, "AccentText");
                c.Text = GrrLeagues.ShortCountdown(GrrLeagues.Until(r.Start, now));
                right = c;
            }
            list.Children.Add(strip
                ? StripCell(eyebrow, r.Track, GrrParts.ShortWhen(r.Start), right, mark, i == from || (i - from) % StripColumns == 0, i == to - 1 || (i - from) % StripColumns == StripColumns - 1, click, tipContent)
                : GrrParts.TimelineRow(eyebrow, r.Track, GrrParts.ShortWhen(r.Start), right, mark, i == from, i == to - 1, click, tipContent));
        }
        var body = new StackPanel();
        if (strip)
        {
            var cells = new System.Windows.Controls.Primitives.UniformGrid { Columns = Math.Min(StripColumns, Math.Max(1, to - from)) };
            foreach (UIElement c in list.Children.Cast<UIElement>().ToList()) { list.Children.Remove(c); cells.Children.Add(c); }
            body.Children.Add(cells);
        }
        else body.Children.Add(list);
        var footer = new List<FrameworkElement>();
        if (rounds.Count > 6) footer.Add(Toggle(_fullSeason ? "Show fewer" : $"Show all {rounds.Count} rounds", () => _fullSeason = !_fullSeason));
        footer.Add(link);
        body.Children.Add(Footer(footer.ToArray()));
        return Card("Schedule", "I.Calendar", body);
    }

    private const int StripColumns = 6;

    private void ShowResults() => Dispatcher.InvokeAsync(() =>
    {
        if (_resultsCard is { } card) card.BringIntoView(new Rect(0, -12, card.ActualWidth, 1));
    }, System.Windows.Threading.DispatcherPriority.Loaded);

    private static StackPanel Winner(string name, bool fastestLap)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, MaxWidth = 190 };
        row.Children.Add(new Icon { Data = Ui.Res<Geometry>("I.Trophy"), Width = 12, Height = 12, Foreground = Ui.Res<Brush>("AccentText"), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        if (fastestLap)
        {
            var fl = new Icon { Data = Ui.Res<Geometry>("I.Clock"), Width = 12, Height = 12, Foreground = Ui.Res<Brush>("PlumDeep"), Margin = new Thickness(-1, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(fl, "Fastest lap");
            row.Children.Add(fl);
        }
        row.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("Text"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }));
        return row;
    }

    private static Button StripCell(string eyebrow, string track, string when, FrameworkElement? info, GrrParts.TimelineMark mark, bool first, bool last, Action? click, object? tip)
    {
        var rail = new Grid { Height = 14, Margin = new Thickness(0, 0, 0, 8) };
        rail.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Height = 1, Fill = Ui.Res<Brush>("LineStrong"), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(first ? 6 : -8, 0, last ? 0 : -8, 0), Visibility = first && last ? Visibility.Collapsed : Visibility.Visible,
        });
        var size = mark == GrrParts.TimelineMark.Next ? 12 : 10;
        rail.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, StrokeThickness = 2,
            Stroke = Ui.Res<Brush>(mark switch { GrrParts.TimelineMark.Next => "Accent", GrrParts.TimelineMark.Done => "TextFaint", _ => "LineStrong" }),
            Fill = Ui.Res<Brush>(mark switch { GrrParts.TimelineMark.Next => "Accent", GrrParts.TimelineMark.Done => "TextFaint", _ => "Surface" }),
        });
        var stack = new StackPanel();
        stack.Children.Add(rail);
        stack.Children.Add(GrrParts.Eyebrow(eyebrow, mark == GrrParts.TimelineMark.Next ? "AccentText" : null));
        stack.Children.Add(new TextBlock
        {
            Text = track, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>(mark == GrrParts.TimelineMark.Done ? "TextMuted" : "Text"),
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 38, Margin = new Thickness(0, 2, 0, 0), ToolTip = track,
        });
        stack.Children.Add(GrrParts.Caption(when, 2));
        if (info is not null)
        {
            info.Margin = new Thickness(0, 4, 0, 0);
            info.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(info);
        }
        var b = new Button
        {
            Style = Ui.Res<Style>("QuietButton"), Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(6, 4, 10, 8), FontWeight = FontWeights.Normal, ToolTip = tip, IsHitTestVisible = click is not null, Focusable = click is not null,
        };
        if (click is not null) b.Click += (_, _) => click();
        return b;
    }

    internal IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        var followed = _app.Config.Grr.Leagues.ToList();
        Task Pick(string league, Action? also = null)
        {
            _app.Config.Grr.DashboardLeague = league;
            also?.Invoke();
            _shape = "";
            Build();
            return Task.CompletedTask;
        }
        yield return ("grr-gt", () => Pick("gt"));
        yield return ("grr-gt-results", () => { _resultsCard?.BringIntoView(); return Task.CompletedTask; });
        yield return ("grr-gt-schedule", () => { _scheduleCard?.BringIntoView(); return Task.CompletedTask; });
        yield return ("grr-cup", () => Pick("cup"));
        yield return ("grr-all", () => Pick("cup", () => { _allStandings = _fullSeason = _allResults = true; }));
        yield return ("grr-indycar", () => Pick("indycar", () => { _allStandings = _fullSeason = _allResults = false; }));
        yield return ("grr-cup-only", () => Pick("gt", () => _app.Config.Grr.Leagues = new() { "cup" }));
        yield return ("grr-none", () => Pick("gt", () => _app.Config.Grr.Leagues = new()));
        yield return ("grr-followed-again", () => Pick("gt", () => _app.Config.Grr.Leagues = followed));
    }
}

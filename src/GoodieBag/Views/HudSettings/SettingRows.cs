using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.SimRacing.Hud;
using GoodieBag.SimRacing.Hud.Editors;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.Views.HudSettings;

internal sealed class HudSettingContext
{
    public required HudWidgetDef Def { get; init; }
    public required HudWidgetConfig W { get; init; }
    public required Action<IReadOnlyDictionary<string, string?>> Set { get; init; }
    public required TelemetryFrame Frame { get; init; }
    public required IReadOnlyList<string> KnownClasses { get; init; }
    public Action? Rebuild { get; init; }
    public Dictionary<string, ItemPicker> Pickers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HudOptions Options => new(Def, W);
    public HudOption Option(string key) => Def.Option(key) ?? throw new InvalidOperationException($"{Def.Kind} has no option {key}");
    public void SetOne(string key, string? value) => Set(new Dictionary<string, string?> { [key] = value });
}

internal static class SettingRows
{
    public const double LabelWidth = 180;
    private const double ComboWidth = 240;

    public static FrameworkElement Build(HudSetting s, HudSettingContext c, string? groupTitle = null) => s switch
    {
        PickerSetting p => Picker(p, c, groupTitle),
        LanesSetting l => Lanes(l, c, groupTitle),
        MergedSetting m => Merged(m, c),
        TogglesSetting t => Row(t.Label ?? t.Title, t.Help, false, Toggles(t.Toggles.Select(k => (k, c.Option(k).Label, c.Option(k).Help)).ToList(), k => c.Options.Bool(k), (k, on) => c.SetOne(k, on ? "true" : "false"))),
        ClassFilterSetting => ClassFilter(c),
        OptionSetting o => Option(o, c),
        _ => new TextBlock { Text = s.Id },
    };

    public static Grid Row(string label, string? help, bool inlineHelp, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 34 });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        name.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Body"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = LabelWidth - 34 });
        if (help is not null && !inlineHelp) name.Children.Add(HelpTip.For(help, label));
        grid.Children.Add(new Border { MinHeight = 34, VerticalAlignment = VerticalAlignment.Top, Child = name });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        if (help is not null && inlineHelp)
        {
            var text = new TextBlock { Text = help, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetColumn(text, 1);
            Grid.SetRow(text, 1);
            grid.Children.Add(text);
        }
        return grid;
    }

    private static FrameworkElement Option(OptionSetting s, HudSettingContext c)
    {
        var o = c.Option(s.Key);
        string label = s.Label ?? o.Label;
        string current = c.Options.Value(s.Key);
        switch (o.Kind)
        {
            case HudOptionKind.Columns:
                return Picker(new PickerSetting(s.Key, FieldSettings.NoCategories) { Label = s.Label }, c);
            case HudOptionKind.Set:
                var on = current.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                return Row(label, o.Help, s.InlineHelp, Toggles(o.Choices.Select(ch => (ch.Id, ch.Label, ch.Help)).ToList(), id => on.Contains(id), (id, now) =>
                {
                    if (now) on.Add(id); else on.Remove(id);
                    c.SetOne(s.Key, string.Join(",", o.Choices.Select(ch => ch.Id).Where(on.Contains)));
                }));
            case HudOptionKind.Bool:
                return Row(label, o.Help, s.InlineHelp, Ui.Switch(current == "true", v => c.SetOne(s.Key, v ? "true" : "false")));
            case HudOptionKind.Int:
            case HudOptionKind.Number:
                double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out var num);
                bool whole = o.Kind == HudOptionKind.Int;
                var box = new NumberBox(num, o.Unit ?? "", 64) { Minimum = o.Min, Maximum = o.Max, Step = whole ? 1 : o.Step, Decimals = whole ? 0 : o.Step < 0.1 ? 2 : o.Step < 1 ? 1 : 0 };
                box.ValueChanged += x => c.SetOne(s.Key, x.ToString(whole ? "0" : "0.###", CultureInfo.InvariantCulture));
                System.Windows.Automation.AutomationProperties.SetName(box, label);
                return Row(label, o.Help, s.InlineHelp, box);
            case HudOptionKind.Choice:
                int index = Math.Max(0, o.Choices.ToList().FindIndex(ch => ch.Id == current));
                if (o.Choices.Count <= 3 && o.Choices.All(ch => ch.Label.Length < 22))
                    return Row(label, o.Help, s.InlineHelp, Ui.Segmented(o.Choices.Select(ch => ch.Label).ToList(), index, i => c.SetOne(s.Key, o.Choices[i].Id), "opt" + c.W.Id + s.Key, o.Choices.Select(ch => ch.Help).ToList()));
                string? help = string.Join("\n", new[] { o.Help }.Concat(o.Choices.Where(ch => ch.Help is not null).Select(ch => $"{ch.Label}: {ch.Help}")).OfType<string>());
                var combo = Ui.Combo(o.Choices.Select(ch => new Choice(ch.Id, ch.Label)), current, ch => c.SetOne(s.Key, ch.Id), ComboWidth);
                System.Windows.Automation.AutomationProperties.SetName(combo, label);
                return Row(label, help.Length == 0 ? null : help, s.InlineHelp, combo);
            default:
                return Row(label, o.Help, s.InlineHelp, new TextBlock { Text = current });
        }
    }

    public static WrapPanel Toggles(IReadOnlyList<(string Id, string Label, string? Help)> items, Func<string, bool> isOn, Action<string, bool> changed)
    {
        var panel = new WrapPanel();
        foreach (var (id, label, help) in items)
        {
            var box = new CheckBox { Content = label, IsChecked = isOn(id), ToolTip = help };
            box.Click += (_, _) => changed(id, box.IsChecked == true);
            panel.Children.Add(TickChip(box));
        }
        return panel;
    }

    public static Tile TickChip(CheckBox box)
    {
        box.Margin = new Thickness(4, 0, 6, 0);
        box.VerticalAlignment = VerticalAlignment.Center;
        var chip = new Tile { Style = Ui.Res<Style>("Chip"), Child = box, MinHeight = 30, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 6, 6) };
        chip.SetValue(TextElement.FontSizeProperty, 12.5);
        return chip;
    }

    private static FrameworkElement Merged(MergedSetting m, HudSettingContext c)
    {
        var host = new ContentControl { Focusable = false };
        void Render()
        {
            var o = c.Options;
            var choices = m.ChoicesFor(o);
            host.Content = Ui.Segmented(choices.Select(ch => ch.Label).ToList(), m.Index(o), i =>
            {
                var pick = choices[i];
                var values = pick.Values.ToDictionary(v => v.Key, v => (string?)v.Value, StringComparer.OrdinalIgnoreCase);
                if (pick.Also?.Invoke(c.Options) is { } more) foreach (var (k, v) in more) values[k] = v;
                c.Set(values);
                host.Dispatcher.BeginInvoke(Render, System.Windows.Threading.DispatcherPriority.Background);
            }, "merged" + c.W.Id + m.Id, choices.Select(ch => ch.Tip).ToList());
        }
        Render();
        return Row(m.Label ?? m.Title, m.Help, m.InlineHelp, host);
    }

    private static bool Repeats(string title, string? groupTitle) => string.Equals(title, groupTitle, StringComparison.OrdinalIgnoreCase);

    private static FrameworkElement Picker(PickerSetting p, HudSettingContext c, string? groupTitle = null)
    {
        var o = c.Option(p.Key);
        string title = p.Label ?? o.Label;
        var picker = new ItemPicker(title, o.Help, o.Choices, p.Categories,
            new[] { new PickerLane(p.Key, null, c.Options.Value(p.Key), o.Default) },
            values => c.SetOne(p.Key, values[0]), !Repeats(title, groupTitle));
        c.Pickers[p.Key] = picker;
        return picker;
    }

    private static FrameworkElement Lanes(LanesSetting l, HudSettingContext c, string? groupTitle)
    {
        var first = c.Option(l.Lanes[0].Key);
        var lanes = l.Lanes.Select(x => new PickerLane(x.Key, x.Lane, c.Options.Value(x.Key), c.Option(x.Key).Default)).ToList();
        string title = l.Label ?? l.Title;
        var picker = new ItemPicker(title, l.Help ?? "Items in a bar above or below the widget. Drag between Top and Bottom.", first.Choices, l.Categories, lanes,
            values => c.Set(l.Lanes.Select((x, i) => (x.Key, values[i])).ToDictionary(v => v.Key, v => (string?)v.Item2, StringComparer.OrdinalIgnoreCase)), !Repeats(title, groupTitle));
        foreach (var x in l.Lanes) c.Pickers[x.Key] = picker;
        return picker;
    }

    private static FrameworkElement ClassFilter(HudSettingContext c)
    {
        var scope = HudClassFilter.Scope;
        var session = c.Frame.Session.Drivers.Where(d => d.Races && d.ClassName.Length > 0).Select(d => d.ClassName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string? mine = c.Frame.Session.Player?.ClassName is { Length: > 0 } m ? m : null;
        var shown = c.Options.Columns(HudClassFilter.ListKey).ToList();
        var known = SessionNames.Merge(null, c.KnownClasses, SessionNames.Classes).Where(k => !session.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        var others = shown.Where(k => !session.Contains(k, StringComparer.OrdinalIgnoreCase) && !known.Contains(k, StringComparer.OrdinalIgnoreCase)).Concat(known).ToList();

        var pick = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var empty = new TextBlock { Text = "Nothing ticked: no cars show.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 6) };
        void Save()
        {
            empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            c.SetOne(HudClassFilter.ListKey, string.Join(",", shown));
        }
        WrapPanel Chips(IEnumerable<string> names) => Toggles(
            names.Select(n => (n, n, string.Equals(n, mine, StringComparison.OrdinalIgnoreCase) ? "Your class" : (string?)null)).ToList(),
            n => shown.Contains(n, StringComparer.OrdinalIgnoreCase),
            (n, on) =>
            {
                shown.RemoveAll(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
                if (on) shown.Add(n);
                Save();
            });
        empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        pick.Children.Add(empty);
        if (session.Count > 0)
        {
            pick.Children.Add(new TextBlock { Text = c.Frame.Simulated ? "In the simulated session" : "In this session", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 6) });
            pick.Children.Add(Chips(session));
        }
        if (others.Count > 0)
        {
            var more = new SettingGroup($"Other classes ({others.Count})", session.Count == 0, null);
            more.Add(Chips(others));
            pick.Children.Add(more);
        }
        if (session.Count == 0 && others.Count == 0) pick.Children.Add(new TextBlock { Text = "Classes appear here once a session is loaded.", Style = Ui.Res<Style>("Caption") });

        var choice = c.Options.Choice(HudClassFilter.ScopeKey);
        pick.Visibility = choice == HudClassFilter.Pick ? Visibility.Visible : Visibility.Collapsed;
        var segmented = Ui.Segmented(scope.Choices.Select(ch => ch.Label).ToList(), Math.Max(0, scope.Choices.ToList().FindIndex(ch => ch.Id == choice)), i =>
        {
            string id = scope.Choices[i].Id;
            pick.Visibility = id == HudClassFilter.Pick ? Visibility.Visible : Visibility.Collapsed;
            if (id == HudClassFilter.Pick && shown.Count == 0 && session.Count > 0)
            {
                c.Set(new Dictionary<string, string?> { [HudClassFilter.ScopeKey] = id, [HudClassFilter.ListKey] = string.Join(",", session) });
                c.Rebuild?.Invoke();
                return;
            }
            c.SetOne(HudClassFilter.ScopeKey, id);
        }, "classes" + c.W.Id, new[] { "Every class in the session", mine is null ? "Your class only" : $"Only {mine}", "Tick the classes to show" });
        var stack = new StackPanel { Children = { segmented, pick } };
        return Row("Show", "Cars of classes not shown are still counted (positions, gaps).", false, stack);
    }
}

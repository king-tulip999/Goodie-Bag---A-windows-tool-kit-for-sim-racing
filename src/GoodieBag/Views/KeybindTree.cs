using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.Theme;
using GoodieBag.SimRacing.IracingFiles;

namespace GoodieBag.Views;

internal sealed class KeybindTree : Tile
{
    private const double RowIndent = 40, RowHeight = 42, KeyWidth = 186, CellWidth = 560, CellGap = 16;
    private readonly AppServices _app;
    private readonly Action<string>? _navigate;
    private readonly Func<HotkeyEntry, bool> _include;
    private readonly int _skip;
    private readonly StackPanel _root = new();
    private bool _arranging;
    private string _query = "";

    public KeybindTree(AppServices app, Func<HotkeyEntry, bool> include, int skip = 0, Action<string>? navigate = null)
    {
        _app = app;
        _include = include;
        _skip = skip;
        _navigate = navigate;
        Style = Ui.Res<Style>("Card");
        Padding = new Thickness(14, 8, 14, 10);
        Child = _root;
        Rebuild();
    }

    private UiSettings U => _app.Config.Ui;

    public bool Arranging
    {
        get => _arranging;
        set
        {
            if (_arranging == value) return;
            _arranging = value;
            _undo = value ? new EditUndo().Items(U.KeybindOrder) : null;
            Rebuild();
        }
    }

    private EditUndo? _undo;

    public bool CancelArrange()
    {
        if (!_arranging) return false;
        var undo = _undo;
        Arranging = false;
        if (undo?.Restore(_app) == true) Rebuild();
        return true;
    }

    private readonly Dictionary<string, FrameworkElement> _groups = new(StringComparer.Ordinal);

    public void ShowGroup(string key)
    {
        if (_groups.TryGetValue(key, out var group)) group.BringIntoView(new Rect(0, 0, 1, 400));
    }

    private readonly List<(TextBlock Count, List<HotkeyEntry> Entries)> _counts = new();

    private void RecountGroups()
    {
        foreach (var (count, entries) in _counts)
            count.Text = $"{entries.Count(e => !string.IsNullOrWhiteSpace(e.Get()))} of {entries.Count} set";
    }

    public string Query
    {
        get => _query;
        set
        {
            value = value.Trim();
            if (value == _query) return;
            _query = value;
            Rebuild();
        }
    }

    private bool Searching => _query.Length > 0;

    internal static bool Matches(HotkeyEntry e, string query) =>
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word =>
            e.Label.Contains(word, StringComparison.OrdinalIgnoreCase) || e.Note?.Contains(word, StringComparison.OrdinalIgnoreCase) == true
            || e.Get()?.Contains(word, StringComparison.OrdinalIgnoreCase) == true || e.Path.Any(p => p.Contains(word, StringComparison.OrdinalIgnoreCase)));

    public void ResetOrder()
    {
        U.KeybindOrder.RemoveAll(HotkeyCatalog.IsCategoryKey);
        _app.Save();
        Rebuild();
    }

    public void Rebuild()
    {
        _root.Children.Clear();
        _groups.Clear();
        _counts.Clear();
        var entries = _app.Hotkeys.Catalog().Where(e => _app.Hotkeys.IsAvailable(e) && _include(e) && e.Path.Count > _skip).ToList();
        if (entries.Count == 0)
        {
            _root.Children.Add(new TextBlock { Text = "No keybinds yet: modes, display profiles and the overlay add theirs when they're on.", Style = Ui.Res<Style>("Caption") });
            return;
        }
        string prefix = string.Join("/", entries[0].Path.Take(_skip));
        if (_arranging)
        {
            Arrange(entries, prefix);
            return;
        }
        if (Searching) entries = entries.Where(e => Matches(e, _query)).ToList();
        if (entries.Count == 0)
        {
            _root.Children.Add(new TextBlock { Text = $"No keybinds match “{_query}”.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(6, 6, 0, 6) });
            return;
        }
        _root.Children.Add(Level(entries, _skip, prefix, Hues.Accent));
    }

    private static string Join(string parent, string name) => parent.Length == 0 ? name : parent + "/" + name;

    private UIElement Level(List<HotkeyEntry> entries, int depth, string parentKey, (string Soft, string Deep) hue)
    {
        var host = new StackPanel();
        var leaves = entries.Where(e => e.Path.Count == depth).ToList();
        if (leaves.Count > 0)
            host.Children.Add(Stacks(leaves, hue));
        bool first = leaves.Count == 0;
        foreach (var name in GroupNames(entries, depth))
        {
            var inside = entries.Where(e => e.Path.Count > depth && e.Path[depth] == name).ToList();
            host.Children.Add(Group(name, Join(parentKey, name), inside, depth, HueOf(name, depth - _skip, inside, hue), first));
            first = false;
        }
        return host;
    }

    private static readonly Dictionary<string, (string Hue, string Icon)> IracingLooks = new(StringComparer.Ordinal)
    {
        ["Driving"] = ("Coral", "I.Wheel"), ["In-car adjustments"] = ("Sage", "I.Sliders"), ["Black boxes"] = ("Gold", "I.Gauge"),
        ["View"] = ("Slate", "I.Eye"), ["Screen and UI"] = ("Plum", "I.Monitor"), ["Chat"] = ("Accent", "I.Keyboard"),
        ["Spotter, sound and force feedback"] = ("Gold", "I.Speaker"), ["Replay and TV cameras"] = ("Coral", "I.Play"), ["Camera tool"] = ("Sage", "I.Target"),
    };

    private (string Soft, string Deep) HueOf(string name, int depth, List<HotkeyEntry> entries, (string Soft, string Deep) parent)
    {
        if (IracingLooks.TryGetValue(name, out var look)) return Hues.Named(look.Hue);
        if (depth == 0 && entries.FirstOrDefault()?.PluginId is { } id && _app.Plugins.Find(id) is { } plugin) return plugin.Colors;
        return depth == 0 ? Hues.Accent : parent;
    }

    private string? IconOf(string name, List<HotkeyEntry> entries, int depth)
    {
        if (IracingLooks.TryGetValue(name, out var look)) return look.Icon;
        if (depth != 0) return null;
        if (name == HotkeyCatalog.WindowGroup) return "I.Bag";
        return entries.FirstOrDefault()?.PluginId is { } id ? _app.Plugins.Find(id)?.Icon : "I.Keyboard";
    }

    private UIElement Stacks(List<HotkeyEntry> leaves, (string Soft, string Deep) hue)
    {
        var stacks = new List<List<HotkeyEntry>>();
        string? last = null;
        foreach (var group in leaves.GroupBy(e => e.Cluster ?? e.Id).Select(g => g.ToList()))
        {
            bool single = group.Count == 1;
            if (single && last == "" && stacks.Count > 0) stacks[^1].AddRange(group);
            else stacks.Add(group);
            last = single ? "" : group[0].Cluster;
        }
        var panel = new MasonryPanel { MinColumnWidth = CellWidth, Gap = CellGap, MaxColumns = 6, Margin = new Thickness(RowIndent, 0, 0, 4) };
        foreach (var stack in stacks)
        {
            var column = new StackPanel();
            foreach (var e in stack)
            {
                var row = Row(e, hue);
                row.Height = RowHeight;
                column.Children.Add(row);
            }
            var holder = new Border { BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(8, 0, 0, 0), Child = column };
            holder.SetResourceReference(Border.BorderBrushProperty, hue.Soft);
            panel.Children.Add(holder);
        }
        return panel;
    }

    private List<string> GroupNames(List<HotkeyEntry> entries, int depth)
    {
        var names = entries.Where(e => e.Path.Count > depth).Select(e => e.Path[depth]).Distinct();
        return depth == 0 ? HotkeyCatalog.Ordered(names, HotkeyCatalog.CategoryKey, U.KeybindOrder) : names.ToList();
    }

    private bool DefaultFolded(string key) => key.StartsWith(HotkeyCatalog.IracingGroup, StringComparison.Ordinal);

    private bool IsFolded(string key) => DefaultFolded(key) ^ U.KeybindsFolded.Contains(key);

    private static StackPanel GroupTitle(string name, int level, List<HotkeyEntry> entries, out TextBlock count,
        (string Soft, string Deep)? hue = null, string? icon = null)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        if (hue is { } h && icon is not null)
        {
            var glyph = Ui.Glyph(icon);
            glyph.Width = glyph.Height = level == 0 ? 14 : 13;
            glyph.SetResourceReference(Control.ForegroundProperty, h.Deep);
            if (level == 0)
            {
                var badge = new Tile { Width = 24, Height = 24, CornerRadius = ThemeManager.Round(7), Margin = new Thickness(6, 0, 0, 0), Child = glyph };
                badge.SetResourceReference(Border.BackgroundProperty, h.Soft);
                head.Children.Add(badge);
            }
            else
            {
                glyph.Margin = new Thickness(6, 0, 0, 0);
                glyph.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(glyph);
            }
        }
        var title = new TextBlock
        {
            Text = name, FontWeight = FontWeights.SemiBold, FontSize = level == 0 ? 15 : 13.5,
            Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        if (level > 0 && hue is { } sub) title.SetResourceReference(TextBlock.ForegroundProperty, sub.Deep);
        head.Children.Add(title);
        int set = entries.Count(e => !string.IsNullOrWhiteSpace(e.Get()));
        count = new TextBlock { Text = $"{set} of {entries.Count} set", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
        if (level == 0 && hue is { } top)
        {
            count.Margin = new Thickness(0);
            count.FontSize = 11.5;
            count.SetResourceReference(TextBlock.ForegroundProperty, top.Deep);
            var chip = new Tile { CornerRadius = ThemeManager.Round(8), Chamfer = true, Padding = new Thickness(7, 1, 7, 2), VerticalAlignment = VerticalAlignment.Center, Child = count };
            chip.SetResourceReference(Border.BackgroundProperty, top.Soft);
            head.Children.Add(chip);
        }
        else head.Children.Add(count);
        return head;
    }

    private FrameworkElement Group(string name, string key, List<HotkeyEntry> entries, int depth, (string Soft, string Deep) hue, bool first)
    {
        int level = depth - _skip;
        var panel = new StackPanel { Margin = new Thickness(0, level == 0 ? 2 : 0, 0, 0) };
        if (level == 0 && !first)
        {
            var rule = new Border { Height = 1, Margin = new Thickness(6, 6, 6, 8) };
            rule.SetResourceReference(Border.BackgroundProperty, "Line");
            panel.Children.Add(rule);
        }
        _groups[key] = panel;
        bool folded = !Searching && IsFolded(key);
        var chevron = new Icon { Data = Ui.Res<Geometry>(folded ? "I.ChevronRight" : "I.ChevronDown"), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        var head = GroupTitle(name, level, entries, out var count, hue, IconOf(name, entries, level));
        _counts.Add((count, entries));
        chevron.SetResourceReference(Control.ForegroundProperty, level == 0 ? "TextMuted" : hue.Deep);
        head.Children.Insert(0, chevron);
        var header = new Button { Style = Ui.Res<Style>("QuietButton"), Content = head, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3, 10, 3), Margin = new Thickness(level * 16, 0, 0, 0) };
        AutomationProperties.SetName(header, name);
        panel.Children.Add(header);

        var body = new Border { Margin = new Thickness(0, 0, 0, level == 0 ? 6 : 2), Visibility = folded ? Visibility.Collapsed : Visibility.Visible };
        if (!folded)
            body.Child = Body(entries, depth, key, hue);
        panel.Children.Add(body);

        ClickGuard.SetAllowRepeat(header, true);
        header.Click += (_, _) =>
        {
            bool fold = body.Visibility == Visibility.Visible;
            if (!fold && body.Child is null)
                body.Child = Body(entries, depth, key, hue);
            body.Visibility = fold ? Visibility.Collapsed : Visibility.Visible;
            chevron.Data = Ui.Res<Geometry>(fold ? "I.ChevronRight" : "I.ChevronDown");
            if (Searching) return;
            U.KeybindsFolded.Remove(key);
            if (fold != DefaultFolded(key)) U.KeybindsFolded.Add(key);
            _app.Save();
        };
        return panel;
    }

    private UIElement Body(List<HotkeyEntry> entries, int depth, string key, (string Soft, string Deep) hue)
    {
        var level = Level(entries, depth + 1, key, hue);
        if (depth != 0 || key != HotkeyCatalog.IracingGroup || Pages.IracingKeybindsView.ProfileRow(_app, Rebuild) is not { } picker) return level;
        picker.Margin = new Thickness(RowIndent + 6, 4, 12, 10);
        return new StackPanel { Children = { picker, level } };
    }

    private FrameworkElement Row(HotkeyEntry entry, (string Soft, string Deep) hue)
    {
        var grid = new Grid { Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(KeyWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_navigate is null ? 0 : 30) });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        names.Children.Add(new TextBlock { Text = entry.Label, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = entry.Label });
        if (entry.Note is { } note)
            names.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis }));
        grid.Children.Add(names);

        var reset = Ui.IconButton("I.Undo", "Back to the default: " + entry.Default, () => { }, "QuietIconButton");
        if (reset.Content is Icon undo) undo.SetResourceReference(Control.ForegroundProperty, hue.Deep);
        reset.Width = reset.Height = 28;
        reset.Margin = new Thickness(2, 0, 0, 0);
        Grid.SetColumn(reset, 2);

        if (entry.Locked is { } locked)
        {
            var lockedText = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var pad = new Icon { Data = Ui.Res<Geometry>("I.Gamepad"), Width = 13, Height = 13, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            pad.SetResourceReference(Control.ForegroundProperty, "TextFaint");
            lockedText.Children.Add(pad);
            lockedText.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = GoodieBag.Hotkeys.Hotkey.Display(entry.Get()), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }));
            var fixedKey = new Border
            {
                Style = Ui.Res<Style>("Inset"), Height = 30, Padding = new Thickness(10, 0, 10, 0), ToolTip = locked, Child = lockedText,
            };
            Grid.SetColumn(fixedKey, 1);
            grid.Children.Add(fixedKey);
            reset.Visibility = Visibility.Hidden;
        }
        else
        {
            var box = new HotkeyBox { NumpadByPosition = entry.Owner == HotkeyOwner.Iracing, Width = KeyWidth, VerticalAlignment = VerticalAlignment.Center, ToolTip = entry.Owner == HotkeyOwner.Iracing ? "Used inside iRacing (saved to its controls file; close iRacing first)" : entry.Global ? "Works in any program" : "Works while the Goodie Bag window is active" };
            box.SetHotkey(entry.Get());
            void SyncKey()
            {
                bool set = !string.IsNullOrWhiteSpace(entry.Get());
                if (set) box.SetResourceReference(Control.ForegroundProperty, hue.Deep);
                else box.ClearValue(Control.ForegroundProperty);
                box.FontWeight = set ? FontWeights.SemiBold : FontWeights.Normal;
            }
            void SyncReset()
            {
                reset.Visibility = entry.Default is { } d && !Same(entry, entry.Get(), d) ? Visibility.Visible : Visibility.Hidden;
                SyncKey();
            }
            void Apply(string? hk)
            {
                if (_app.Hotkeys.Change(entry, hk) is { } problem)
                    _app.Notify("Keybind not changed", problem, NoticeKind.Warning, important: true);
                box.SetHotkey(entry.Get());
                SyncReset();
                RecountGroups();
            }
            box.HotkeyChanged += Apply;
            reset.Click += (_, _) => Apply(entry.Default);
            SyncReset();
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
        }
        grid.Children.Add(reset);

        if (_navigate is not null && entry.Page != "settings")
        {
            var open = Ui.IconButton("I.ChevronRight", $"Open where it's set up ({entry.Path[0]})", () => _navigate(entry.Page), "QuietIconButton");
            open.Width = open.Height = 28;
            open.Margin = new Thickness(2, 0, 0, 0);
            Grid.SetColumn(open, 3);
            grid.Children.Add(open);
        }
        var row = new Border { CornerRadius = ThemeManager.Round(6), Background = Brushes.Transparent, Child = grid };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    private static bool Same(HotkeyEntry entry, string? a, string? b) =>
        entry.Owner == HotkeyOwner.Iracing
            ? IracingKey.FromHotkey(a) is { } x && IracingKey.FromHotkey(b) is { } y && x.SameAs(y)
            : GoodieBag.Hotkeys.Hotkey.SameKey(a, b) || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Arrange(List<HotkeyEntry> entries, string prefix)
    {
        int depth = _skip;
        _root.Children.Add(new TextBlock
        {
            Text = "Drag the categories into the order you want. The keybinds inside each one keep their default order.",
            Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>("AccentText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 0, 8),
        });
        var groups = GroupNames(entries, depth);
        var list = new ReorderGrid { MaxColumns = 1, Gap = 4, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var name in groups)
        {
            var inside = entries.Where(e => e.Path.Count > depth && e.Path[depth] == name).ToList();
            var head = GroupTitle(name, 0, inside, out _, HueOf(name, 0, inside, Hues.Accent), IconOf(name, inside, 0));
            head.Margin = new Thickness(4, 7, 8, 7);
            var cell = new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(0), Child = head, Background = Ui.Res<Brush>("Surface"), ToolTip = $"Drag {name} to move it" };
            cell.SetResourceReference(Border.BorderBrushProperty, "BtnOutline");
            cell.BorderThickness = new Thickness(1);
            if (Searching && !inside.Any(e => Matches(e, _query))) cell.Opacity = 0.4;
            AutomationProperties.SetName(cell, name);
            list.Children.Add(cell);
        }
        list.Moved += (from, to) => SaveOrder(Move(groups, from, to).Select(n => HotkeyCatalog.CategoryKey(Join(prefix, n))).ToList());
        _root.Children.Add(list);
    }

    private static List<string> Move(List<string> items, int from, int to)
    {
        var moved = items.ToList();
        var item = moved[from];
        moved.RemoveAt(from);
        moved.Insert(to, item);
        return moved;
    }

    private void SaveOrder(List<string> siblings)
    {
        HotkeyCatalog.SaveOrder(U.KeybindOrder, siblings);
        _app.Save();
        Rebuild();
    }
}

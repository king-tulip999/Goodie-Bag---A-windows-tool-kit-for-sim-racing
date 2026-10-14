using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.Theme;
using WinForms = System.Windows.Forms;

namespace GoodieBag.Views.Pages;

internal sealed class CustomThemeSection : StackPanel
{
    private readonly AppServices _app;
    private readonly MainWindow _window;

    private CustomThemeSettings C => _app.Config.Ui.OledCustom;

    public CustomThemeSection(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        bool oled = _app.Config.Ui.Theme == AppTheme.Custom;

        var useSwitch = Ui.Switch(C.Enabled, on =>
        {
            C.Enabled = on;
            if (!on) C.EditingProfile = null;
            _app.OledCustomChanged();
        });
        var nameRow = Ui.SettingRow("Theme name", "Empty = Custom.", NameBox());
        Children.Add(nameRow);
        string caption = oled
            ? "Your own colors for the Custom theme. Your colors are kept when you switch themes."
            : "Pick Custom above to use your own colors. They're kept while you use another theme.";
        var coloursRow = Ui.SettingRow("Custom colors", caption, useSwitch);
        Children.Add(coloursRow);
        if (!oled)
        {
            nameRow.IsEnabled = false;
            nameRow.Opacity = 0.5;
            coloursRow.IsEnabled = false;
            coloursRow.Opacity = 0.5;
            return;
        }
        if (!C.Enabled) return;

        var editor = new StackPanel();
        editor.Children.Add(Toolbar());
        editor.Children.Add(Grid());
        Children.Add(new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(14, 12, 14, 6), Margin = new Thickness(0, 0, 0, 10), Child = editor });
    }

    private TextBox NameBox()
    {
        var name = new TextBox { Text = C.Name ?? "", Width = 190, MaxLength = 24, Tag = Themes.DefaultCustomName, VerticalAlignment = VerticalAlignment.Center };
        void CommitName()
        {
            string v = name.Text.Trim();
            if (v == (C.Name ?? "")) return;
            if (C.Profile(C.EditingProfile) is { } editing && v.Length > 0 && C.Profile(v) is null)
            {
                ConfigReferences.ColorSet(_app.Config, editing.Name, v);
                editing.Name = v;
                C.EditingProfile = v;
            }
            C.Name = v.Length == 0 ? null : v;
            _app.OledCustomChanged();
        }
        name.LostKeyboardFocus += (_, _) => CommitName();
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitName(); };
        return name;
    }

    private UIElement Toolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

        var colours = Ui.LabeledButton("I.Palette", "Colors", () => { }, "Btn");
        ((StackPanel)colours.Content).Children.Add(new Controls.Icon { Data = Ui.Res<Geometry>("I.ChevronDown"), Width = 12, Height = 12, Margin = new Thickness(6, 0, 0, 0) });
        colours.ToolTip = "The theme's own colors or one of your saved sets";
        colours.Click += (_, _) => OpenMenu(colours);
        bar.Children.Add(Spaced(colours));

        var save = Ui.LabeledButton("I.Plus", "Save as new", () => _ = SaveAsNewAsync(), "GhostButton");
        save.ToolTip = "Keep these colors as a saved set (listed under Colors)";
        bar.Children.Add(Spaced(save));

        if (C.Profile(C.EditingProfile) is { } profile)
        {
            var chip = Ui.Chip($"Editing “{profile.Name}”: changes are saved into it", "AccentSoft", "AccentText");
            chip.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(Spaced(chip));
            var done = Ui.LabeledButton("I.Check", "Done", () => { C.EditingProfile = null; _app.OledCustomChanged(); }, "GhostButton");
            done.ToolTip = "Stop editing the saved set (the colors stay on)";
            bar.Children.Add(Spaced(done));
        }
        return bar;
    }

    private static FrameworkElement Spaced(FrameworkElement e, double right = 8)
    {
        e.Margin = new Thickness(0, 0, right, 6);
        e.VerticalAlignment = VerticalAlignment.Center;
        return e;
    }

    private void OpenMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, MinWidth = 240 };

        var reset = new MenuItem { Header = Labelled("Default colors", ThemeColors.Default), ToolTip = "The Custom theme's own colors" };
        reset.Click += (_, _) =>
        {
            C.Colors = new ThemeColors();
            C.EditingProfile = null;
            _app.OledCustomChanged();
        };
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());

        if (C.Profiles.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No saved colors yet (Save as new)", IsEnabled = false });
        foreach (var profile in C.Profiles.ToList())
        {
            var p = profile;
            var entry = new MenuItem { Header = Labelled(p.Name, p.Colors) };
            var use = new MenuItem { Header = "Use", ToolTip = "Switch to these colors" };
            use.Click += (_, _) => Use(p.Colors.Copy(), p.Name);
            var edit = new MenuItem { Header = "Edit", ToolTip = "Use these colors and save every change into them" };
            edit.Click += (_, _) =>
            {
                C.Colors = p.Colors.Copy();
                C.Name = p.Name;
                C.EditingProfile = p.Name;
                _app.OledCustomChanged();
            };
            var delete = new MenuItem { Header = "Delete" };
            delete.Click += async (_, _) =>
            {
                if (!await _window.Confirm("Delete saved colors?", $"“{p.Name}” is removed from My colors. The colors in use now don't change.", "Delete", danger: true)) return;
                C.Profiles.Remove(p);
                ConfigReferences.ColorSet(_app.Config, p.Name, null);
                if (string.Equals(C.EditingProfile, p.Name, StringComparison.OrdinalIgnoreCase)) C.EditingProfile = null;
                _app.OledCustomChanged();
            };
            entry.Items.Add(use);
            entry.Items.Add(edit);
            entry.Items.Add(new Separator());
            entry.Items.Add(delete);
            menu.Items.Add(entry);
        }
        menu.IsOpen = true;
    }

    private static UIElement Labelled(string name, ThemeColors colors)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var chip = new Border { CornerRadius = ThemeManager.Round(3), Padding = new Thickness(3), Margin = new Thickness(0, 0, 10, 0), Background = Brush(colors.Background, Brushes.Black), VerticalAlignment = VerticalAlignment.Center };
        chip.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        chip.BorderThickness = new Thickness(1);
        var dots = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var hex in new[] { colors.Accent, colors.Highlight, colors.Text })
            dots.Children.Add(new Ellipse { Width = 8, Height = 8, Margin = new Thickness(1, 0, 1, 0), Fill = Brush(hex, Brushes.Gray) });
        chip.Child = dots;
        row.Children.Add(chip);
        row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static Brush Brush(string hex, Brush fallback) => CustomPalette.Parse(hex) is { } c ? new SolidColorBrush(c) : fallback;

    private void Use(ThemeColors colors, string? name)
    {
        C.Colors = colors;
        C.Name = name ?? C.Name;
        C.EditingProfile = null;
        _app.OledCustomChanged();
    }

    private async Task SaveAsNewAsync()
    {
        string suggestion = string.IsNullOrWhiteSpace(C.Name) ? "My colors" : C.Name!;
        for (int n = 2; C.Profile(suggestion) is not null; n++) suggestion = $"{(string.IsNullOrWhiteSpace(C.Name) ? "My colors" : C.Name)} {n}";
        var name = await _window.Prompt("Save colors", "A name for these colors (Colors → My colors).", suggestion,
            v => v.Trim().Length == 0 ? "Give it a name." : C.Profile(v.Trim()) is not null ? "There's already a set with that name." : null);
        if (name is null) return;
        name = name.Trim();
        C.Profiles.Add(new ThemeColorProfile { Name = name, Colors = C.Colors.Copy() });
        C.Name = name;
        C.EditingProfile = name;
        _app.OledCustomChanged();
    }

    private UIElement Grid()
    {
        var c = C.Colors;
        var rows = new (string Label, string Help, Func<string> Get, Action<string> Set)[]
        {
            ("Background", "The window's black", () => c.Background, v => c.Background = v),
            ("Edges", "Outlines, lines and the tint of cards", () => c.Edges, v => c.Edges = v),
            ("Text", "Headings; body text is a touch softer", () => c.Text, v => c.Text = v),
            ("Accent", "Buttons, switches, the selected item, bars", () => c.Accent, v => c.Accent = v),
            ("Highlight", "The active mode and keyboard focus", () => c.Highlight, v => c.Highlight = v),
            ("Success", "All good, running", () => c.Success, v => c.Success = v),
            ("Warning", "Cautions", () => c.Warning, v => c.Warning = v),
            ("Error", "Problems and deleting", () => c.Error, v => c.Error = v),
        };
        var grid = new WrapPanel { ItemWidth = 152 };
        foreach (var (label, help, get, set) in rows)
            grid.Children.Add(Cell(label, help, get, set));
        return grid;
    }

    private FrameworkElement Cell(string label, string help, Func<string> get, Action<string> set)
    {
        var swatch = new Button
        {
            Width = 34, Height = 30, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Pick a color", Style = Ui.Res<Style>("Btn"),
        };
        swatch.Content = new Border { Width = 22, Height = 18, CornerRadius = ThemeManager.Round(3), Background = Brush(get(), Brushes.Transparent), BorderThickness = new Thickness(1), BorderBrush = Ui.Res<Brush>("LineStrong") };
        var box = new TextBox { Text = get(), Width = 96, MaxLength = 7, FontFamily = Ui.Res<FontFamily>("MonoFont"), VerticalAlignment = VerticalAlignment.Center, ToolTip = help };
        void Commit(string text)
        {
            var color = CustomPalette.Parse(text);
            if (color is not { } v) { box.Text = get(); return; }
            string hex = $"#{v.R:X2}{v.G:X2}{v.B:X2}";
            if (string.Equals(hex, get(), StringComparison.OrdinalIgnoreCase)) { box.Text = get(); return; }
            set(hex);
            if (C.Profile(C.EditingProfile) is { } editing) editing.Colors = C.Colors.Copy();
            _app.OledCustomChanged();
        }
        box.LostKeyboardFocus += (_, _) => Commit(box.Text);
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(box.Text); };
        swatch.Click += (_, _) =>
        {
            var now = CustomPalette.Parse(get()) ?? Colors.Black;
            using var dialog = new WinForms.ColorDialog { Color = System.Drawing.Color.FromArgb(now.R, now.G, now.B), FullOpen = true, AnyColor = true };
            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                Commit($"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
        };
        var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };
        cell.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), ToolTip = help });
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(swatch);
        line.Children.Add(box);
        cell.Children.Add(line);
        return cell;
    }
}

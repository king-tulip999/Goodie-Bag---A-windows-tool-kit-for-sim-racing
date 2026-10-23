using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal static class ThemeMenu
{
    internal readonly record struct Pick(bool FollowApp, bool Custom, AppTheme Theme, string? Set);

    private static CustomThemeSettings? Oled => Services.AppServices.Current?.Config.Ui.OledCustom;

    public static string Describe(Pick pick, string customLabel) =>
        pick.FollowApp ? "Same as the app"
        : pick.Custom ? customLabel
        : pick.Theme == AppTheme.Custom && !string.IsNullOrEmpty(pick.Set)
            ? $"{Themes.For(AppTheme.Custom).Name} · {(pick.Set == Themes.OledDefaultSet ? "Default" : pick.Set)}"
            : Themes.For(pick.Theme).Name;

    public static Button OverlayButton(Func<Pick> current, Action<Pick> picked, bool followApp, string? customLabel, double minWidth = 200)
    {
        var text = TextFit.TipWhenTrimmed(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var row = new DockPanel { LastChildFill = true };
        var chevron = new Icon { Data = Ui.Res<Geometry>("I.ChevronDown"), Width = 12, Height = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(chevron, Dock.Right);
        row.Children.Add(chevron);
        var dots = new ContentControl { Focusable = false, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(dots, Dock.Left);
        row.Children.Add(dots);
        row.Children.Add(text);
        var button = new Button { Style = Ui.Res<Style>("Btn"), Content = row, MinWidth = minWidth, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 3, 10, 3) };
        void Show()
        {
            var pick = current();
            text.Text = Describe(pick, customLabel ?? "Custom colors");
            dots.Content = pick.FollowApp || pick.Custom ? null : Dots(Themes.ForSet(pick.Theme, pick.Set));
        }
        Show();
        button.Click += (_, _) =>
        {
            var pick = current();
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, MinWidth = Math.Max(220, button.ActualWidth) };
            MenuItem Item(string header, ThemeSpec? spec, bool on, Pick choice)
            {
                var item = new MenuItem { Header = Header(header, spec), IsCheckable = false, IsChecked = on };
                item.Click += (_, _) => { picked(choice); Show(); };
                return item;
            }
            if (followApp)
            {
                menu.Items.Add(Item("Same as the app", null, pick.FollowApp, new Pick(true, false, pick.Theme, null)));
                menu.Items.Add(new Separator());
            }
            foreach (var theme in Themes.All)
            {
                bool chosen = !pick.FollowApp && !pick.Custom && pick.Theme == theme.Id;
                if (theme.Id != AppTheme.Custom)
                {
                    menu.Items.Add(Item(theme.Name, theme, chosen, new Pick(false, false, theme.Id, null)));
                    continue;
                }
                var oled = new MenuItem { Header = Header(theme.Name, theme), IsChecked = chosen };
                oled.Items.Add(Item($"As in the app ({theme.Name})", theme, chosen && string.IsNullOrEmpty(pick.Set), new Pick(false, false, theme.Id, null)));
                oled.Items.Add(Item("Default", Themes.Custom, chosen && pick.Set == Themes.OledDefaultSet, new Pick(false, false, theme.Id, Themes.OledDefaultSet)));
                AddSets(oled, set => chosen && pick.Set == set, set => { picked(new Pick(false, false, AppTheme.Custom, set)); Show(); });
                menu.Items.Add(oled);
            }
            if (customLabel is not null)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(Item(customLabel, null, pick.Custom, new Pick(false, true, pick.Theme, null)));
            }
            menu.IsOpen = true;
        };
        return button;
    }

    public static void OpenOledMenu(FrameworkElement anchor, Action<string?> use)
    {
        var c = Oled;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, MinWidth = Math.Max(220, anchor.ActualWidth) };
        bool usingCustom = c?.InUse == true;
        var own = new MenuItem { Header = Header("Default", Themes.Custom), IsChecked = ThemeManager.Current.Id == AppTheme.Custom && !usingCustom };
        own.Click += (_, _) => use(null);
        menu.Items.Add(own);
        if (c is not null && !c.Colors.IsDefault)
        {
            var mine = new MenuItem
            {
                Header = Header(string.IsNullOrWhiteSpace(c.Name) ? "Your custom colors" : $"Your custom colors ({c.Name})", Themes.Custom with { Colors = CustomPalette.Build(c.Colors) }),
                IsChecked = ThemeManager.Current.Id == AppTheme.Custom && usingCustom,
            };
            mine.Click += (_, _) => use("");
            menu.Items.Add(mine);
        }
        foreach (var profile in (c?.Profiles ?? new List<ThemeColorProfile>()).ToList())
        {
            var p = profile;
            var item = new MenuItem { Header = Header(p.Name, Themes.Custom with { Colors = CustomPalette.Build(p.Colors) }) };
            item.Click += (_, _) => use(p.Name);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private static void AddSets(MenuItem into, Func<string, bool> isChecked, Action<string> pick)
    {
        into.Items.Add(new Separator());
        var profiles = Oled?.Profiles ?? new List<ThemeColorProfile>();
        if (profiles.Count == 0)
            into.Items.Add(new MenuItem { Header = "No saved colors yet (Settings → Appearance → Custom colors)", IsEnabled = false });
        foreach (var profile in profiles.ToList())
        {
            var p = profile;
            var item = new MenuItem { Header = Header(p.Name, Themes.Custom with { Colors = CustomPalette.Build(p.Colors) }), IsChecked = isChecked(p.Name) };
            item.Click += (_, _) => pick(p.Name);
            into.Items.Add(item);
        }
    }

    private static object Header(string text, ThemeSpec? spec)
    {
        if (spec is null) return text;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Dots(spec));
        row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    public static FrameworkElement Dots(ThemeSpec spec)
    {
        var c = ThemeManager.Resolve(spec);
        Brush B(string key) => new SolidColorBrush(c.TryGetValue(key, out var v) ? v : Colors.Gray);
        var dots = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var key in new[] { "Accent", "ActiveModeFg", "Heading" })
            dots.Children.Add(new Ellipse { Width = 8, Height = 8, Margin = new Thickness(1, 0, 1, 0), Fill = B(key) });
        var chip = new Border { Child = dots, Padding = new Thickness(3), CornerRadius = ThemeManager.Round(3), Background = B("Bg"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        chip.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        return chip;
    }
}

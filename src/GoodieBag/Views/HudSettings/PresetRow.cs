using System.Windows;
using System.Windows.Controls;
using GoodieBag.Controls;
using GoodieBag.SimRacing.Hud.Editors;

namespace GoodieBag.Views.HudSettings;

internal sealed class PresetRow : DockPanel
{
    private readonly List<(string Name, RadioButton Button)> _buttons = new();
    private readonly Border _custom;
    private bool _quiet;

    public PresetRow(IReadOnlyList<HudPreset> presets, Action<HudPreset> apply)
    {
        Margin = new Thickness(0, 0, 0, 12);
        LastChildFill = false;
        var label = new TextBlock { Text = "Preset", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Children.Add(label);
        var panel = new WrapPanel();
        string group = "preset-" + Guid.NewGuid().ToString("N");
        foreach (var preset in presets)
        {
            var p = preset;
            var rb = new RadioButton { Content = preset.Name, Style = Ui.Res<Style>("Segment"), GroupName = group, ToolTip = Tip(preset.Name) };
            rb.Checked += (_, _) => { if (!_quiet) apply(p); };
            _buttons.Add((preset.Name, rb));
            panel.Children.Add(rb);
        }
        Children.Add(new Tile { Style = Ui.Res<Style>("Segmented"), Child = panel });
        _custom = new Border
        {
            Child = new TextBlock { Text = "Modified", Style = Ui.Res<Style>("Caption"), FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Center },
            Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Your own settings: they don't match a preset",
        };
        Children.Add(_custom);
    }

    private static string Tip(string name) => name switch
    {
        HudPreset.Minimal => "The fewest items: content and layout only, place and visibility stay",
        HudPreset.Everything => "Every item it can show: content and layout only, place and visibility stay",
        _ => "As the widget starts: content and layout only, place and visibility stay",
    };

    public void Show(string? current)
    {
        _quiet = true;
        foreach (var (name, button) in _buttons) button.IsChecked = name == current;
        _quiet = false;
        _custom.Visibility = current is null ? Visibility.Visible : Visibility.Collapsed;
    }
}

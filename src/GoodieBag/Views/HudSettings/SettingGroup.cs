using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GoodieBag.Controls;

namespace GoodieBag.Views.HudSettings;

internal sealed class SettingGroup : StackPanel
{
    private readonly Border _body = new() { Padding = new Thickness(4, 2, 0, 8) };
    private readonly StackPanel _rows = new();
    private readonly Icon? _chevron;
    private readonly Action<bool>? _toggled;

    public SettingGroup(string? title, bool open, Action<bool>? toggled)
    {
        _toggled = toggled;
        _body.Child = _rows;
        if (title is not null)
        {
            _chevron = new Icon { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            _chevron.SetResourceReference(Control.ForegroundProperty, "TextMuted");
            var line = new DockPanel { LastChildFill = true };
            line.Children.Add(_chevron);
            line.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Eyebrow"), VerticalAlignment = VerticalAlignment.Center });
            var bar = new Button { Style = Ui.Res<Style>("QuietButton"), Content = line, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 7, 10, 7), Margin = new Thickness(0, 4, 0, 0) };
            ClickGuard.SetAllowRepeat(bar, true);
            System.Windows.Automation.AutomationProperties.SetName(bar, title);
            bar.Click += (_, _) =>
            {
                Open = !Open;
                _toggled?.Invoke(Open);
            };
            Children.Add(bar);
        }
        Children.Add(_body);
        Open = open || title is null;
    }

    public bool Open
    {
        get => _body.Visibility == Visibility.Visible;
        set
        {
            _body.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (_chevron is not null) _chevron.Data = Ui.Res<Geometry>(value ? "I.ChevronDown" : "I.ChevronRight");
        }
    }

    public void Add(UIElement row) => _rows.Children.Add(row);

    public bool HasVisibleRows => _rows.Children.OfType<UIElement>().Any(r => r.Visibility == Visibility.Visible);
}

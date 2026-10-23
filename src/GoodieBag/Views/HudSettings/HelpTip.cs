using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Controls;

namespace GoodieBag.Views.HudSettings;

internal static class HelpTip
{
    public static FrameworkElement For(string text, string? name = null)
    {
        var icon = new Icon { Data = Ui.Res<Geometry>("I.Info"), Width = 13, Height = 13 };
        icon.SetResourceReference(Control.ForegroundProperty, "TextFaint");
        var box = new Border
        {
            Child = icon, Padding = new Thickness(3), Margin = new Thickness(4, 0, 0, 0), Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Help, Focusable = true, ToolTip = text,
            FocusVisualStyle = Ui.Res<Style>("FocusRing"),
        };
        KeyboardNavigation.SetIsTabStop(box, true);
        ToolTipService.SetShowsToolTipOnKeyboardFocus(box, true);
        ToolTipService.SetInitialShowDelay(box, 150);
        ToolTipService.SetShowDuration(box, 60000);
        System.Windows.Automation.AutomationProperties.SetName(box, name is null ? "More about this" : "More about " + name);
        System.Windows.Automation.AutomationProperties.SetHelpText(box, text);
        box.GotKeyboardFocus += (_, _) => icon.SetResourceReference(Control.ForegroundProperty, "AccentText");
        box.LostKeyboardFocus += (_, _) => icon.SetResourceReference(Control.ForegroundProperty, "TextFaint");
        box.MouseEnter += (_, _) => icon.SetResourceReference(Control.ForegroundProperty, "AccentText");
        box.MouseLeave += (_, _) => { if (!box.IsKeyboardFocused) icon.SetResourceReference(Control.ForegroundProperty, "TextFaint"); };
        return box;
    }
}

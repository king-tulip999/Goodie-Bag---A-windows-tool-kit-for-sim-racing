using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GoodieBag.Controls;

internal static class ClickGuard
{
    public static readonly DependencyProperty AllowRepeatProperty =
        DependencyProperty.RegisterAttached("AllowRepeat", typeof(bool), typeof(ClickGuard), new PropertyMetadata(false));

    public static void SetAllowRepeat(DependencyObject d, bool value) => d.SetValue(AllowRepeatProperty, value);

    public static bool GetAllowRepeat(DependencyObject d) => (bool)d.GetValue(AllowRepeatProperty);

    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPress));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnMenuOpened));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler(OnMenuClosed));
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContextMenu, ButtonBase> Openers = new();
    private static ButtonBase? _closedMenu;
    private static long _closedAt;

    private static void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        var button = ButtonOf(Mouse.DirectlyOver as DependencyObject)
            ?? (Environment.TickCount64 - _clickedAt < 1000 ? _clicked : null);
        if (sender is ContextMenu menu && button is not null)
            Openers.AddOrUpdate(menu, button);
    }

    private static void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || !Openers.TryGetValue(menu, out var opener) || Mouse.LeftButton != MouseButtonState.Pressed) return;
        if (ButtonOf(opener.InputHitTest(Mouse.GetPosition(opener)) as DependencyObject) != opener) return;
        _closedMenu = opener;
        _closedAt = Environment.TickCount64;
    }

    private static ButtonBase? ButtonOf(DependencyObject? d)
    {
        for (; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase button) return button;
        return null;
    }

    private static ButtonBase? _clicked;
    private static long _clickedAt;

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        _clicked = sender as ButtonBase;
        _clickedAt = Environment.TickCount64;
        if (_closedMenu is null || sender != _closedMenu) return;
        _closedMenu = null;
        if (Environment.TickCount64 - _closedAt < 1500) e.Handled = true;
    }

    private static void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || sender is RepeatButton || sender is not ButtonBase button || GetAllowRepeat(button))
            return;
        e.Handled = true;
    }
}

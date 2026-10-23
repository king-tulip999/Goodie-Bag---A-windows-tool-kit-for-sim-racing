using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace GoodieBag.Views;

internal static class MenuFollow
{
    private static readonly ConditionalWeakTable<ContextMenu, Watch> Watching = new();

    private sealed class Watch(ContextMenu menu, FrameworkElement target)
    {
        public readonly FrameworkElement Target = target;

        public void OnUnloaded(object? sender, RoutedEventArgs e) => menu.IsOpen = false;

        public void OnVisible(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is false) menu.IsOpen = false;
        }
    }

    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnOpened));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler(OnClosed));
    }

    private static void OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.PlacementTarget is not FrameworkElement target) return;
        Stop(menu);
        var watch = new Watch(menu, target);
        target.Unloaded += watch.OnUnloaded;
        target.IsVisibleChanged += watch.OnVisible;
        Watching.Add(menu, watch);
    }

    private static void OnClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu) Stop(menu);
    }

    private static void Stop(ContextMenu menu)
    {
        if (!Watching.TryGetValue(menu, out var watch)) return;
        watch.Target.Unloaded -= watch.OnUnloaded;
        watch.Target.IsVisibleChanged -= watch.OnVisible;
        Watching.Remove(menu);
    }
}

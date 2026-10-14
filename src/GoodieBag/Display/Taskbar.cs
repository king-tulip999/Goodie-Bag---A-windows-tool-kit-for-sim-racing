using System.Runtime.InteropServices;

namespace GoodieBag.Display;

internal static class Taskbar
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Value = "MMTaskbarEnabled";

    public static bool OnAllDisplays()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue(Value) is int v && v != 0;
    }

    public static void SetOnAllDisplays(bool on)
    {
        if (OnAllDisplays() == on)
            return;
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key))
            key.SetValue(Value, on ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "TraySettings", 0x0002, 1000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}

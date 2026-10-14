using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GoodieBag.Hotkeys;

internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
    }

    public bool TryRegister(string text, Action action, out string? error)
    {
        if (!Hotkey.TryParse(text, out var hotkey))
        {
            error = "isn't a valid key combination";
            return false;
        }

        int id = _nextId++;
        if (!RegisterHotKey(Handle, id, hotkey.Modifiers | MOD_NOREPEAT, (uint)(hotkey.Key & Keys.KeyCode)))
        {
            int code = Marshal.GetLastWin32Error();
            error = code == ERROR_HOTKEY_ALREADY_REGISTERED
                ? "is already used by another app"
                : $"couldn't be registered (error {code})";
            return false;
        }

        _actions[id] = action;
        error = null;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _actions.Keys)
            UnregisterHotKey(Handle, id);
        _actions.Clear();
        _nextId = 1;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action))
        {
            action();
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

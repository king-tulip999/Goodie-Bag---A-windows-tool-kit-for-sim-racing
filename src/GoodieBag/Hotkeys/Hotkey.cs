using System.Windows.Forms;

namespace GoodieBag.Hotkeys;

internal readonly record struct Hotkey(uint Modifiers, Keys Key)
{
    public const uint ModAlt = 0x1;
    public const uint ModControl = 0x2;
    public const uint ModShift = 0x4;
    public const uint ModWin = 0x8;

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        uint modifiers = 0;
        Keys key = Keys.None;

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win" or "windows": modifiers |= ModWin; break;
                default:
                    if (key != Keys.None)
                        return false;
                    if (part.Length == 1 && char.IsDigit(part[0]))
                        key = Keys.D0 + (part[0] - '0');
                    else if (int.TryParse(part, out _) || !Enum.TryParse(part, ignoreCase: true, out key))
                        return false;
                    break;
            }
        }

        if (key == Keys.None || IsModifierKey(key))
            return false;

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    public static Hotkey? FromWpf(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers)
    {
        var keys = (Keys)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (keys == Keys.None || IsModifierKey(keys))
            return null;

        uint mods = 0;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) mods |= ModControl;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) mods |= ModAlt;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) mods |= ModShift;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows)) mods |= ModWin;
        return new Hotkey(mods, keys);
    }

    public static bool SameKey(string? a, string? b) =>
        TryParse(a, out var x) && TryParse(b, out var y) && x == y;

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((Modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((Modifiers & ModShift) != 0) parts.Add("Shift");
        if ((Modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(Key is >= Keys.D0 and <= Keys.D9 ? ((int)(Key - Keys.D0)).ToString() : Key.ToString());
        return string.Join("+", parts);
    }

    public static string Display(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (text.EndsWith("++", StringComparison.Ordinal)) parts = parts[..^2].Append("+").ToArray();
        return string.Join("+", parts.Where(p => p.Length > 0 || parts.Length == 1).Select(KeyLabel));
    }

    public static string KeyLabel(string name)
    {
        if (Friendly.TryGetValue(name, out var friendly)) return friendly;
        if (name.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && name.Length == 7) return "Num " + name[6..];
        if (name.Length == 2 && (name[0] is 'D' or 'd') && char.IsDigit(name[1])) return name[1..];
        return name;
    }

    private static readonly Dictionary<string, string> Friendly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Oem1"] = ";", ["OemSemicolon"] = ";",
        ["Oemplus"] = "=", ["OemPlus"] = "=",
        ["Oemcomma"] = ",", ["OemComma"] = ",",
        ["OemMinus"] = "-",
        ["OemPeriod"] = ".",
        ["Oem2"] = "/", ["OemQuestion"] = "/",
        ["Oem3"] = "`", ["Oemtilde"] = "`", ["OemTilde"] = "`",
        ["Oem4"] = "[", ["OemOpenBrackets"] = "[",
        ["Oem5"] = "\\", ["OemPipe"] = "\\",
        ["Oem6"] = "]", ["OemCloseBrackets"] = "]",
        ["Oem7"] = "'", ["OemQuotes"] = "'",
        ["Oem102"] = "\\ (extra key)", ["OemBackslash"] = "\\ (extra key)",
        ["Next"] = "Page Down", ["PageDown"] = "Page Down", ["Prior"] = "Page Up", ["PageUp"] = "Page Up",
        ["Return"] = "Enter", ["Back"] = "Backspace", ["Capital"] = "Caps Lock", ["CapsLock"] = "Caps Lock",
        ["Scroll"] = "Scroll Lock", ["NumLock"] = "Num Lock", ["Snapshot"] = "Print Screen", ["PrintScreen"] = "Print Screen",
        ["Escape"] = "Esc", ["Pause"] = "Pause", ["Apps"] = "Menu", ["Insert"] = "Insert", ["Delete"] = "Delete",
        ["Multiply"] = "Num *", ["Add"] = "Num +", ["Subtract"] = "Num -", ["Decimal"] = "Num .", ["Divide"] = "Num /", ["Separator"] = "Num ,",
        ["Left"] = "Left arrow", ["Right"] = "Right arrow", ["Up"] = "Up arrow", ["Down"] = "Down arrow",
        ["VolumeMute"] = "Mute", ["VolumeDown"] = "Volume down", ["VolumeUp"] = "Volume up",
        ["MediaNextTrack"] = "Next track", ["MediaPreviousTrack"] = "Previous track", ["MediaPlayPause"] = "Play / pause", ["MediaStop"] = "Stop",
        ["BrowserBack"] = "Browser back", ["BrowserForward"] = "Browser forward",
    };

    private static bool IsModifierKey(Keys key) => key is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin;
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GoodieBag.Services;

namespace GoodieBag.Controls;

public sealed class HotkeyBox : TextBox
{
    private const string EmptyHint = "Click to set…";
    private bool _paused, _recording;
    private Window? _window;
    private readonly Popup _hint;
    private readonly TextBlock _hintText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };

    public string? Hotkey { get; private set; }

    public bool NumpadByPosition { get; init; }

    private bool _lastExtended;

    public event Action<string?>? HotkeyChanged;

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        Tag = EmptyHint;
        Width = 190;
        InputMethod.SetIsInputMethodEnabled(this, false);
        SetResourceReference(StyleProperty, typeof(TextBox));
        _hintText.SetResourceReference(TextBlock.ForegroundProperty, "TooltipText");
        var border = new Border { Padding = new Thickness(10, 6, 10, 7), CornerRadius = Theme.ThemeManager.Round(6), Child = _hintText };
        border.SetResourceReference(Border.BackgroundProperty, "TooltipBg");
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        border.BorderThickness = new Thickness(1);
        _hint = new Popup
        {
            PlacementTarget = this, Placement = PlacementMode.Bottom, VerticalOffset = 4, AllowsTransparency = true,
            StaysOpen = true, Focusable = false, IsHitTestVisible = false, Child = border,
        };
        RenderOptions.SetClearTypeHint(border, ClearTypeHint.Enabled);
        Unloaded += (_, _) => StopRecording();
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopRecording(); };
    }

    public void SetHotkey(string? hotkey)
    {
        Hotkey = string.IsNullOrWhiteSpace(hotkey) ? null : hotkey;
        if (!_recording) Text = Theme.ThemeManager.Case(GoodieBag.Hotkeys.Hotkey.Display(Hotkey));
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (_recording) return;
        _recording = true;
        if (!_paused)
        {
            _paused = true;
            AppServices.Current?.Hotkeys.Pause();
        }
        Text = "";
        Tag = "Press the keys…";
        _hintText.Text = Hotkey is null
            ? "Press the key combination to use.\nClick anywhere else to cancel."
            : $"Press the new keys to replace {GoodieBag.Hotkeys.Hotkey.Display(Hotkey)}.\nEsc deletes this keybind · click anywhere else to cancel.";
        _hint.IsOpen = true;
        if (NumpadByPosition) ComponentDispatcher.ThreadPreprocessMessage += OnRawKey;
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.PreviewMouseDown += OnWindowMouseDown;
            _window.Deactivated += OnWindowDeactivated;
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        StopRecording();
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsMouseOver) Cancel();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => Cancel();

    private void Cancel()
    {
        StopRecording();
        if (IsKeyboardFocused) Keyboard.ClearFocus();
        if (FocusManager.GetFocusScope(this) is { } scope && FocusManager.GetFocusedElement(scope) == this)
            FocusManager.SetFocusedElement(scope, null);
    }

    private void StopRecording()
    {
        if (_window is not null)
        {
            _window.PreviewMouseDown -= OnWindowMouseDown;
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }
        _hint.IsOpen = false;
        ComponentDispatcher.ThreadPreprocessMessage -= OnRawKey;
        if (_paused)
        {
            _paused = false;
            AppServices.Current?.Hotkeys.Resume();
        }
        _recording = false;
        Tag = EmptyHint;
        Text = Theme.ThemeManager.Case(GoodieBag.Hotkeys.Hotkey.Display(Hotkey));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (Hotkey is not null) Update(null);
            Cancel();
            return;
        }
        if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = false;
            return;
        }

        if (NumpadByPosition && !_lastExtended && NumpadName(key) is { } pad)
        {
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Windows)) return;
            Update((mods.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (mods.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + (mods.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + pad);
            Cancel();
            return;
        }
        if (GoodieBag.Hotkeys.Hotkey.FromWpf(key, Keyboard.Modifiers) is { } hotkey)
        {
            Update(hotkey.ToString());
            Cancel();
        }
    }

    private void OnRawKey(ref System.Windows.Interop.MSG msg, ref bool handled)
    {
        const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
        if (msg.message is WM_KEYDOWN or WM_SYSKEYDOWN)
            _lastExtended = ((long)msg.lParam & (1L << 24)) != 0;
    }

    internal static string? NumpadName(Key key) => key switch
    {
        Key.Insert => "Num 0", Key.End => "Num 1", Key.Down => "Num 2", Key.PageDown => "Num 3", Key.Left => "Num 4",
        Key.Clear => "Num 5", Key.Right => "Num 6", Key.Home => "Num 7", Key.Up => "Num 8", Key.PageUp => "Num 9",
        Key.Delete => "Num .",
        _ => null,
    };

    private void Update(string? hotkey)
    {
        Hotkey = string.IsNullOrWhiteSpace(hotkey) ? null : hotkey;
        HotkeyChanged?.Invoke(Hotkey);
    }
}

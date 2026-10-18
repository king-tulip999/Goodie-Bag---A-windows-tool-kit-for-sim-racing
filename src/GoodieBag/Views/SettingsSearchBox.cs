using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Controls;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal sealed class SettingsSearchBox : Grid
{
    private readonly MainWindow _window;
    private readonly TextBox _box = new() { Tag = "Search settings and keybinds", Padding = new Thickness(32, 7, 9, 7) };
    private readonly Popup _popup;
    private readonly StackPanel _results = new();
    private List<SearchEntry> _found = new();
    private int _selected;

    public SettingsSearchBox(MainWindow window)
    {
        _window = window;
        Children.Add(_box);
        var icon = new Icon
        {
            Data = (Geometry)Application.Current.FindResource("I.Search"), Width = 14, Height = 14,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 0, 0),
            IsHitTestVisible = false,
        };
        icon.SetResourceReference(Control.ForegroundProperty, "TextFaint");
        Children.Add(icon);
        _popup = new Popup
        {
            PlacementTarget = _box, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(6), Margin = new Thickness(0, 0, 14, 14), Child = _results },
        };
        RenderOptions.SetClearTypeHint(_popup.Child, ClearTypeHint.Enabled);
        Children.Add(_popup);
        _box.SizeChanged += (_, _) => _popup.Width = Math.Max(420, _box.ActualWidth + 14);
        _box.TextChanged += (_, _) => Update();
        _box.GotKeyboardFocus += (_, _) => Update();
        _box.PreviewKeyDown += OnKey;
        _box.LostKeyboardFocus += (_, _) => { if (!_popup.IsKeyboardFocusWithin && _box.Text.Length == 0) Dismissed?.Invoke(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) _popup.IsOpen = false; };
    }

    public event Action? Opened;
    public event Action? Dismissed;

    public void FocusBox()
    {
        _box.Focus();
        _box.SelectAll();
    }

    private void Update()
    {
        string query = _box.Text.Trim();
        _found = _window.Search(query);
        _selected = 0;
        Render();
        _popup.IsOpen = query.Length > 0 && _box.IsKeyboardFocused;
    }

    private void Render()
    {
        _results.Children.Clear();
        if (_found.Count == 0)
        {
            _results.Children.Add(new TextBlock { Text = "No settings or keybinds match", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(10, 8, 10, 8) });
            return;
        }
        for (int i = 0; i < _found.Count; i++)
        {
            var entry = _found[i];
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = entry.Title, FontWeight = FontWeights.SemiBold });
            text.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = $"{entry.Category} · {entry.Description}", Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap }));
            var row = new Tile
            {
                CornerRadius = ThemeManager.Round(8),
                Padding = new Thickness(10, 7, 10, 7),
                Cursor = Cursors.Hand,
                Background = i == _selected ? Ui.Res<Brush>("AccentSoft") : Brushes.Transparent,
                Child = text,
            };
            row.MouseLeftButtonUp += (_, _) => Open(entry);
            Motion.Pressable(row);
            _results.Children.Add(row);
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when _found.Count > 0:
                _selected = (_selected + 1) % _found.Count;
                Render();
                break;
            case Key.Up when _found.Count > 0:
                _selected = (_selected + _found.Count - 1) % _found.Count;
                Render();
                break;
            case Key.Enter when _found.Count > 0:
                Open(_found[_selected]);
                break;
            case Key.Escape:
                _box.Text = "";
                _popup.IsOpen = false;
                Keyboard.ClearFocus();
                Dismissed?.Invoke();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Open(SearchEntry entry)
    {
        _popup.IsOpen = false;
        _box.Text = "";
        Keyboard.ClearFocus();
        Opened?.Invoke();
        _window.OpenSearchResult(entry);
    }
}

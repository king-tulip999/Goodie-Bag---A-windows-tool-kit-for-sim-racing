using System.Windows;
using System.Windows.Controls;
using GoodieBag.Config;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal sealed class ArrangeList
{
    private readonly ContentControl _host = new() { Focusable = false };
    private readonly Func<IReadOnlyList<(string Id, string Label, Func<FrameworkElement> Build)>> _items;
    private readonly Func<IReadOnlyList<string>> _getOrder;
    private readonly Action<List<string>> _setOrder;
    private readonly string _label;
    private readonly TextBlock _buttonText;
    private List<string>? _before;

    public ArrangeList(Func<IReadOnlyList<(string Id, string Label, Func<FrameworkElement> Build)>> items, Func<IReadOnlyList<string>> getOrder,
        Action<List<string>> setOrder, string label = "Arrange", string? tip = null)
    {
        _items = items;
        _getOrder = getOrder;
        _setOrder = setOrder;
        _label = label;
        Button = Ui.LabeledButton("I.Move", label, () => SetEditing(!Editing), "GhostButton");
        Button.ToolTip = tip ?? "Drag the cards into a new order; Done keeps it, Esc puts it back";
        _buttonText = ((StackPanel)Button.Content).Children.OfType<TextBlock>().Last();
    }

    public Button Button { get; }

    public FrameworkElement View => _host;

    public bool Editing { get; private set; }

    public event Action<bool>? EditingChanged;

    public void Fill()
    {
        var items = _items();
        var order = _getOrder();
        var slots = order.Where(id => items.Any(i => i.Id == id)).Concat(items.Select(i => i.Id).Where(id => !order.Contains(id)))
            .Select(id => new PanelSlot { Id = id }).ToList();
        var grid = new PanelGrid(slots, () => _setOrder(slots.Select(s => s.Id).ToList())) { Editing = Editing, RowHeight = 2, Gap = 8, MinColumns = 1 };
        for (int i = 0; i < items.Count; i++)
        {
            var (id, label, build) = items[i];
            grid.Add(new PanelDef(id, label, _ => build()) { FullWidth = true, Order = i });
        }
        _host.Content = grid;
        Button.IsEnabled = items.Count > 1 || Editing;
    }

    public void SetEditing(bool on)
    {
        if (on == Editing) return;
        Editing = on;
        _before = on ? _getOrder().ToList() : null;
        Button.Style = Ui.Res<Style>(on ? "PrimaryButton" : "GhostButton");
        _buttonText.Text = ThemeManager.Case(on ? "Done" : _label);
        ((StackPanel)Button.Content).Children[0].Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        Fill();
        EditingChanged?.Invoke(on);
    }

    public bool Cancel()
    {
        if (!Editing) return false;
        var before = _before;
        SetEditing(false);
        if (before is not null && !before.SequenceEqual(_getOrder()))
        {
            _setOrder(before);
            Fill();
        }
        return true;
    }
}

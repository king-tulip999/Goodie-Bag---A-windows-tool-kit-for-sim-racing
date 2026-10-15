using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GoodieBag.Views;

namespace GoodieBag.Controls;

internal sealed class SearchCombo : ComboBox
{
    private object? _before;
    private readonly TextBox _search = new() { Tag = "Search…", Margin = new Thickness(2, 2, 2, 6), Padding = new Thickness(28, 6, 8, 6) };

    public SearchCombo()
    {
        SetResourceReference(StyleProperty, typeof(ComboBox));
        IsTextSearchEnabled = false;
        _search.TextChanged += (_, _) => ApplyFilter();
        _search.PreviewKeyDown += OnSearchKey;
        DropDownOpened += (_, _) =>
        {
            _before = SelectedItem;
            Dispatcher.BeginInvoke(new Action(() => { _search.Focus(); Keyboard.Focus(_search); }), System.Windows.Threading.DispatcherPriority.Input);
        };
        DropDownClosed += (_, _) =>
        {
            _search.Text = "";
            if (SelectedItem is null && _before is not null && Items.Contains(_before)) SelectedItem = _before;
            _before = null;
        };
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Popup") is not Popup { Child: Border { Child: DockPanel card } } || card.Children.OfType<ScrollViewer>().FirstOrDefault() is not { } list) return;
        var head = new Grid();
        head.Children.Add(_search);
        head.Children.Add(new Icon
        {
            Data = (Geometry)FindResource("I.Search"), Width = 13, Height = 13, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 0, 4), Foreground = Ui.Res<Brush>("TextFaint"),
        });
        head.Margin = new Thickness(4, 4, 4, 0);
        DockPanel.SetDock(head, Dock.Top);
        card.Children.Insert(card.Children.IndexOf(list), head);
    }

    private void ApplyFilter()
    {
        var words = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) { Items.Filter = null; return; }
        object? first = Items.Count > 0 ? Items.GetItemAt(0) : null;
        Items.Filter = item => item == first || item is Choice c && words.All(w => c.Text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                var match = _search.Text.Trim().Length > 0 ? Items.Cast<object>().Skip(1).FirstOrDefault() : null;
                if (match is not null) SelectedItem = match;
                IsDropDownOpen = false;
                e.Handled = true;
                break;
            case Key.Escape:
                IsDropDownOpen = false;
                e.Handled = true;
                break;
            case Key.Down when ItemContainerGenerator.ContainerFromIndex(0) is ComboBoxItem item:
                item.Focus();
                e.Handled = true;
                break;
        }
    }
}

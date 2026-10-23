using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using GoodieBag.Controls;
using GoodieBag.Config;
using GoodieBag.Services;
using GoodieBag.SimRacing.Hud;
using GoodieBag.SimRacing.Telemetry;
using GoodieBag.Views.Pages;

namespace GoodieBag.Views;

internal sealed class HudQuickEditor : Window, IHudEditorHost
{
    private static HudQuickEditor? _open;

    private readonly AppServices _app;
    private readonly HudHost _hud;
    private readonly RaceHudWidgets _widgets;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Grid _modal = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _layout = new();
    private readonly Button _undo, _redo, _snap;
    private readonly ComboBox _layoutPick = new() { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center, ToolTip = "The layout on screen (and edited here)" };
    private string _layoutsShown = "";
    private bool _fillingLayouts;
    private readonly IntPtr _returnTo;
    private TaskCompletionSource<bool>? _confirm;
    private bool _closing;

    public static bool IsOpen => _open is not null;

    public static void Toggle(AppServices app, HudHost hud)
    {
        if (_open is { } open) { open.Close(); return; }
        if (app.Demo) { app.Notify("In-game editor", "Not with demo data: no widgets are put on screen.", NoticeKind.Info); return; }
        _open = new HudQuickEditor(app, hud);
        _open.Show();
        _open.Activate();
    }

    private HudQuickEditor(AppServices app, HudHost hud)
    {
        _app = app;
        _hud = hud;
        _returnTo = GetForegroundWindow();
        Preview = new PreviewSource(() => app.Config.SimRacing.Hud);
        Title = "Goodie Bag · iRacing Overlay editor";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        UseLayoutRounding = true;
        SetResourceReference(FontFamilyProperty, "UiFont");
        SetResourceReference(ForegroundProperty, "Text");
        FontSize = 13.5;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        Topmost = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        double scale = app.Config.Ui.ScaleFactor;
        Width = Math.Min(1080 * scale, SystemParameters.PrimaryScreenWidth * 0.9);
        Height = Math.Min(780 * scale, SystemParameters.PrimaryScreenHeight * 0.85);
        MinWidth = 640;
        MinHeight = 400;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(BorderBrushProperty, "LineStrong");
        BorderThickness = new Thickness(1);

        _undo = Ui.IconButton("I.Undo", "Undo the last move or resize", () => { _hud.Undo(); Refresh(); });
        _redo = Ui.IconButton("I.Redo", "Redo", () => { _hud.Redo(); Refresh(); });
        _snap = Ui.LabeledButton("I.Hash", "Snap", () => { _hud.ToggleSnap(); Refresh(); }, "GhostButton");
        _snap.ToolTip = "Snap widgets to the grid and to each other while dragging (hold Alt for a free move). Click to switch on / off.";
        _layoutPick.SelectionChanged += (_, _) =>
        {
            if (_fillingLayouts || _layoutPick.SelectedItem is not Choice { Id: { } name } || name == ShownLayout) return;
            _hud.UseLayout(name);
        };
        var add = Ui.LabeledButton("I.Plus", "Add new", () => { }, "GhostButton");
        add.ToolTip = "A new layout: a clean screen or a copy of this one. It goes on screen at once.";
        add.Click += (_, _) => RaceHudSection.NewLayoutBox(add, _app.Config.SimRacing.Hud, _app.Config.SimRacing.Hud.Layout(ShownLayout), AddLayout);
        var done = Ui.LabeledButton("I.Check", "Done", Close, "PrimaryButton");
        done.ToolTip = "Save and close (or press the keybind again, or Esc): the widgets go back to letting clicks through";

        var titleText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleText.Children.Add(new TextBlock { Text = "iRacing Overlay editor", Style = Ui.Res<Style>("H3") });
        _layout.Style = Ui.Res<Style>("Caption");
        titleText.Children.Add(_layout);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in new[] { _undo, _redo }) tools.Children.Add(b);
        _snap.Margin = new Thickness(6, 0, 8, 0);
        tools.Children.Add(_snap);
        tools.Children.Add(_layoutPick);
        add.Margin = new Thickness(6, 0, 8, 0);
        tools.Children.Add(add);
        tools.Children.Add(done);
        var bar = Ui.SplitRow(titleText, tools);
        var barHost = new Border { Padding = new Thickness(16, 10, 12, 10), Child = bar, BorderThickness = new Thickness(0, 0, 0, 1) };
        barHost.SetResourceReference(Border.BackgroundProperty, "Surface");
        barHost.SetResourceReference(Border.BorderBrushProperty, "Line");
        barHost.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        var hint = new TextBlock
        {
            Text = "Drag a widget on screen to move it; drag its right or bottom edge or its corner to resize. Click one on screen to open its settings here.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 10, 18, 2),
        };
        _widgets = new RaceHudWidgets(app, this);
        var body = new DockPanel();
        DockPanel.SetDock(barHost, Dock.Top);
        DockPanel.SetDock(hint, Dock.Top);
        body.Children.Add(barHost);
        body.Children.Add(hint);
        body.Children.Add(_widgets.Root);
        var root = new Grid { Children = { body, _modal } };
        if (Math.Abs(scale - 1) > 0.001) root.LayoutTransform = new ScaleTransform(scale, scale);
        RenderOptions.SetClearTypeHint(root, ClearTypeHint.Enabled);
        Content = root;

        _hud.WidgetPicked += OnPicked;
        _hud.Changed += OnHudChanged;
        _timer.Tick += (_, _) => { Preview.Advance(_hud); _widgets.Tick(); };
        _timer.Start();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            if (_confirm is not null) { EndConfirm(false); e.Handled = true; return; }
            if (Keyboard.FocusedElement is HotkeyBox or TextBox { Text.Length: > 0 }) return;
            e.Handled = true;
            Close();
        };
        _hud.BeginEdit(toolbar: false);
        Refresh();
    }

    public PreviewSource Preview { get; }

    public string ShownLayout => _hud.LayoutName is { Length: > 0 } n ? n : _app.Config.SimRacing.Hud.ActiveLayout;

    public void Changed() => _hud.SettingsChanged();

    private void OnPicked(string id) => _widgets.SelectId(id);

    private void OnHudChanged()
    {
        if (!_hud.Editing && !_closing) { Close(); return; }
        _widgets.FollowLayout();
        Refresh();
    }

    private void Refresh()
    {
        _undo.IsEnabled = _hud.CanUndo;
        _redo.IsEnabled = _hud.CanRedo;
        bool snap = _app.Config.SimRacing.Hud.Snap;
        Ui.SetToggled(_snap, snap);
        _snap.Content = Ui.IconLabel(snap ? "I.Check" : "I.Hash", snap ? "Snap on" : "Snap off");
        FillLayouts();
        bool live = _app.Telemetry.Status == SimStatus.Connected && !_app.Telemetry.Simulated;
        _layout.Text = $"Layout {ShownLayout} · " + (live ? "live from iRacing" : "sample data until a session is running");
    }

    private void FillLayouts()
    {
        var layouts = _app.Config.SimRacing.Hud.Layouts;
        string shown = string.Join("|", layouts.Select(l => l.Name)) + ">" + ShownLayout;
        if (shown == _layoutsShown) return;
        _layoutsShown = shown;
        _fillingLayouts = true;
        _layoutPick.Items.Clear();
        foreach (var l in layouts) _layoutPick.Items.Add(new Choice(l.Name, l.Name));
        _layoutPick.SelectedItem = _layoutPick.Items.Cast<Choice>().FirstOrDefault(c => string.Equals(c.Id, ShownLayout, StringComparison.OrdinalIgnoreCase));
        _fillingLayouts = false;
    }

    private void AddLayout(HudLayout layout)
    {
        _app.Config.SimRacing.Hud.Layouts.Add(layout);
        _hud.UseLayout(layout.Name);
    }

    public Task<bool> Confirm(string title, string message, string confirmText, bool danger)
    {
        EndConfirm(false);
        _confirm = new TaskCompletionSource<bool>();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("H3"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = message, Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => EndConfirm(false);
        var ok = new Button { Content = confirmText, Style = Ui.Res<Style>(danger ? "DangerConfirmButton" : "PrimaryButton") };
        ok.Click += (_, _) => EndConfirm(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        var card = new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(20, 18, 20, 16), MaxWidth = 420, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Child = stack };
        _modal.Children.Clear();
        var dim = new Border();
        dim.SetResourceReference(Border.BackgroundProperty, "Dim");
        _modal.Children.Add(dim);
        _modal.Children.Add(card);
        _modal.Visibility = Visibility.Visible;
        ok.Focus();
        return _confirm.Task;
    }

    private void EndConfirm(bool result)
    {
        var pending = _confirm;
        _confirm = null;
        _modal.Visibility = Visibility.Collapsed;
        _modal.Children.Clear();
        pending?.TrySetResult(result);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        EndConfirm(false);
        _timer.Stop();
        _hud.WidgetPicked -= OnPicked;
        _hud.Changed -= OnHudChanged;
        if (_hud.Editing) _hud.EndEdit(keep: true);
        if (ReferenceEquals(_open, this)) _open = null;
        if (_returnTo != IntPtr.Zero) SetForegroundWindow(_returnTo);
        base.OnClosed(e);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hWnd);
}

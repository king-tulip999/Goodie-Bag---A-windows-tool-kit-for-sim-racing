using System.Windows;
using System.Windows.Controls;
using GoodieBag.Automations;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Processes;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class TriggerEditor
{
    private const string PluginPrefix = "plugin:";
    private readonly AppServices _app;
    private readonly StackPanel _fields = new();
    private TextBox? _timeText, _dateText;

    public AutomationRule Trigger { get; }
    public Tile View { get; }

    public TriggerEditor(AppServices app, AutomationRule trigger, Action remove)
    {
        _app = app;
        Trigger = trigger;
        var t = trigger;

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var on = Ui.Switch(t.Enabled, v => t.Enabled = v);
        on.ToolTip = "Switch this trigger on or off";
        on.VerticalAlignment = VerticalAlignment.Center;
        on.Margin = new Thickness(0, 0, 12, 0);
        top.Children.Add(on);

        var choices = AutomationCatalog.Triggers.Select(x => new Choice(x.Type.ToString(), x.Label))
            .Concat(app.Automations.PluginTriggers.Select(p => new Choice(PluginPrefix + p.Id, p.Label)))
            .Concat(t.Trigger == TriggerType.Plugin && app.Automations.Find(t.PluginTrigger) is null ? new[] { new Choice(PluginPrefix + t.PluginTrigger, $"{t.PluginTrigger} (not installed)") } : Array.Empty<Choice>())
            .ToList();
        var combo = Ui.Combo(choices, t.Trigger == TriggerType.Plugin ? PluginPrefix + t.PluginTrigger : t.Trigger.ToString(), c =>
        {
            var before = t.Trigger == TriggerType.Plugin ? app.Automations.Find(t.PluginTrigger) : null;
            if (c.Id!.StartsWith(PluginPrefix, StringComparison.Ordinal))
            {
                t.Trigger = TriggerType.Plugin;
                t.PluginTrigger = c.Id[PluginPrefix.Length..];
                if (app.Automations.Find(t.PluginTrigger) is { } picked && picked != before) t.Threshold = picked.DefaultThreshold;
            }
            else
            {
                t.Trigger = Enum.Parse<TriggerType>(c.Id);
                t.PluginTrigger = null;
            }
            if (before is not null && before != app.Automations.Find(t.PluginTrigger)) before.ClearOptions(t);
            if (AutomationCatalog.UsesThreshold(t.Trigger)) t.Threshold = AutomationCatalog.DefaultThreshold(t.Trigger);
            if (t.Trigger == TriggerType.Once && t.Date is null) t.Date = DateTime.Today.AddDays(1).AddHours(19);
            Fill();
        }, 260);
        combo.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(combo, 1);
        top.Children.Add(combo);
        var delete = Ui.IconButton("I.Trash", "Remove this trigger", remove, "QuietIconButton");
        Grid.SetColumn(delete, 2);
        top.Children.Add(delete);

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(_fields);
        View = new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(12, 10, 8, 10), Margin = new Thickness(0, 0, 0, 8), Child = stack };
        Fill();
    }

    private void Fill()
    {
        _fields.Children.Clear();
        var t = Trigger;
        var plugin = t.Trigger == TriggerType.Plugin ? _app.Automations.Find(t.PluginTrigger) : null;
        string help = t.Trigger != TriggerType.Plugin ? AutomationCatalog.Triggers.FirstOrDefault(x => x.Type == t.Trigger).Help ?? ""
            : plugin?.Help ?? "It comes from a plugin that's off or no longer exists. It waits, unchanged, until that's back.";
        _fields.Children.Add(new TextBlock { Text = help, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap });
        if (plugin is not null)
        {
            try { plugin.BuildEditor(t, _fields); }
            catch (Exception ex) { App.LogError(ex); }
        }
        if (AutomationCatalog.UsesApp(t.Trigger))
            _fields.Children.Add(Ui.FieldRow("Program", ProgramField(t.App, t.Name, (process, name) => { t.App = process; t.Name = name; }, "Choose a program")));
        if (AutomationCatalog.UsesThreshold(t.Trigger))
        {
            bool idle = t.Trigger == TriggerType.Idle;
            var box = new NumberBox(t.Threshold, idle ? "min" : "°C") { Minimum = idle ? 1 : 30, Maximum = idle ? 600 : 120 };
            box.ValueChanged += v => t.Threshold = (int)v;
            _fields.Children.Add(Ui.FieldRow(idle ? "Idle for" : "Above", box));
        }
        _timeText = null;
        _dateText = null;
        if (t.Trigger is TriggerType.Daily or TriggerType.Weekly)
        {
            _timeText = new TextBox { Text = ScheduleTriggers.Time(t.AtMinute), Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Tag = "e.g. " + ScheduleTriggers.Time(19 * 60 + 45) };
            _fields.Children.Add(Ui.FieldRow("At", _timeText));
        }
        if (t.Trigger == TriggerType.Weekly)
        {
            var days = new WrapPanel();
            foreach (var day in Enumerable.Range(1, 7).Select(i => (DayOfWeek)(i % 7)))
            {
                var d = day;
                var box = new CheckBox { Content = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(d), IsChecked = t.Days.Contains(d), Margin = new Thickness(0, 0, 14, 0) };
                box.Click += (_, _) => { if (box.IsChecked == true) { if (!t.Days.Contains(d)) t.Days.Add(d); } else t.Days.Remove(d); };
                days.Children.Add(box);
            }
            _fields.Children.Add(Ui.FieldRow("On", days));
        }
        if (t.Trigger == TriggerType.Once)
        {
            var at = t.Date ?? DateTime.Today.AddDays(1).AddHours(19);
            _dateText = new TextBox { Text = at.ToString("d"), Width = 140, HorizontalAlignment = HorizontalAlignment.Left, Tag = "e.g. " + DateTime.Today.ToString("d") };
            _timeText = new TextBox { Text = at.ToString("t"), Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Tag = "e.g. " + ScheduleTriggers.Time(19 * 60 + 45) };
            _fields.Children.Add(Ui.FieldRow("Date", _dateText));
            _fields.Children.Add(Ui.FieldRow("At", _timeText));
        }
        if (t.Trigger == TriggerType.WhileRunning)
            return;

        _fields.Children.Add(Ui.FieldRow("Only in mode", Ui.Combo(ModeEditor.ModeChoices(_app, "Any mode"), t.OnlyInMode, c => t.OnlyInMode = c.Id)));
        _fields.Children.Add(Ui.FieldRow("Only if running", ProgramField(t.OnlyIfRunning, null, (process, _) => t.OnlyIfRunning = process, "Any (no condition)")));
        if (ScheduleTriggers.IsScheduled(t.Trigger) || t.Trigger == TriggerType.WindowsStartup)
            return;
        var cooldown = new NumberBox(t.CooldownSeconds, "s") { Minimum = 0, Maximum = 3600, ToolTip = "Not again within this time, so a program that restarts or a temperature that hovers can't repeat it over and over" };
        cooldown.ValueChanged += v => t.CooldownSeconds = (int)v;
        _fields.Children.Add(Ui.FieldRow("Not again within", cooldown));
    }

    private static StackPanel ProgramField(string? process, string? name, Action<string?, string?> set, string placeholder)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var box = new TextBox { Text = string.IsNullOrWhiteSpace(name) ? process ?? "" : name, Tag = placeholder, Width = 220, ToolTip = process is null ? null : TextCase.VerbatimTip(process + ".exe") };
        string? pickedName = name;
        box.TextChanged += (_, _) =>
        {
            if (pickedName is not null && box.Text == pickedName) return;
            pickedName = null;
            set(string.IsNullOrWhiteSpace(box.Text) ? null : AppWatcher.Normalize(box.Text), null);
        };
        var pick = Ui.LabeledButton("I.AppWindow", "Pick…", () => { }, "GhostButton");
        pick.Click += (_, _) => ProgramPicker.Show(pick, p =>
        {
            pickedName = p.Name;
            set(p.Process, p.Name);
            box.Text = p.Name;
            box.ToolTip = TextCase.VerbatimTip(p.Process + ".exe");
        });
        pick.Margin = new Thickness(6, 0, 0, 0);
        panel.Children.Add(box);
        panel.Children.Add(pick);
        return panel;
    }

    public string? Problem(bool oneOff)
    {
        var t = Trigger;
        int? at = _timeText is null ? null : ScheduleTriggers.ParseTime(_timeText.Text);
        DateTime? date = _dateText is not null && DateTime.TryParse(_dateText.Text, out var d) ? d.Date : null;
        if (at is { } minute)
        {
            t.AtMinute = minute;
            if (date is { } day) t.Date = day.AddMinutes(minute);
        }
        string? problem = t.Trigger == TriggerType.WhileRunning && oneOff ? "“While a program runs” needs a mode that stays active: switch One-off off, or pick “A program starts”."
            : AutomationCatalog.UsesApp(t.Trigger) && string.IsNullOrWhiteSpace(t.App) ? $"Choose the program for “{AutomationCatalog.Label(t.Trigger)}”."
            : _timeText is not null && at is null ? $"“{_timeText.Text}” isn't a time. Try {ScheduleTriggers.Time(19 * 60 + 45)}."
            : _dateText is not null && date is null ? $"“{_dateText.Text}” isn't a date."
            : t.Trigger == TriggerType.Once && t.Enabled && t.Date <= DateTime.Now && t.LastDue != t.Date ? "That date and time has already passed."
            : t.Trigger == TriggerType.Weekly && t.Days.Count == 0 ? "Tick at least one day."
            : null;
        if (problem is not null) return problem;
        if (!AutomationCatalog.UsesApp(t.Trigger)) { t.App = null; t.Name = null; }
        if (t.Trigger != TriggerType.Weekly) t.Days.Clear();
        if (t.Trigger != TriggerType.Once) t.Date = null;
        if (t.Trigger == TriggerType.WhileRunning) { t.OnlyInMode = null; t.OnlyIfRunning = null; t.CooldownSeconds = 0; }
        t.Hotkey = null;
        t.Actions = null;
        return null;
    }
}

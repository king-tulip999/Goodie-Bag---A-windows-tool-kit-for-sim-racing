using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GoodieBag.Automations;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Modes;
using GoodieBag.Plugins;
using GoodieBag.Processes;
using GoodieBag.Services;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class ModesPage : UserControl, IPage, IScreenshotStates
{
    private const string Consumer = "Modes page";
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(28, 0, 28, 28) };
    private readonly MasonryPanel _grid = new() { MinColumnWidth = 440, MaxColumns = 3, Margin = new Thickness(0, 0, 0, 16) };

    public ModesPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        Content = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body), Focusable = false };
    }

    private ModeSwitchingSettings S => _app.Config.ModeSwitching;
    private bool _shown, _buildQueued;
    private HashSet<string> _programs = new(StringComparer.OrdinalIgnoreCase);

    public void OnShown()
    {
        _shown = true;
        Watch();
        Build();
        _app.Watcher.Started += OnProcess;
        _app.Watcher.Stopped += OnProcess;
        _app.DisplayProfiles.Changed += QueueBuild;
        _app.ModesChanged += OnModesChanged;
        _app.Modes.ActiveChanged += QueueBuild;
        _app.Modes.OneOffRan += QueueBuild;
        _app.Switcher.Changed += QueueBuild;
        _app.Automations.Changed += QueueBuild;
        _app.Displays.Changed += QueueBuild;
        Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _shown = false;
        _app.Watcher.Release(Consumer);
        _app.Watcher.Started -= OnProcess;
        _app.Watcher.Stopped -= OnProcess;
        _app.DisplayProfiles.Changed -= QueueBuild;
        _app.ModesChanged -= OnModesChanged;
        _app.Modes.ActiveChanged -= QueueBuild;
        _app.Modes.OneOffRan -= QueueBuild;
        _app.Switcher.Changed -= QueueBuild;
        _app.Automations.Changed -= QueueBuild;
        _app.Displays.Changed -= QueueBuild;
    }

    private void Watch()
    {
        _programs = _app.Config.Modes.Where(m => m.Program is { } p && ModeLauncher.CanDetect(p)).Select(m => ModeLauncher.ProcessOf(m.Program!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_programs.Count > 0) _app.Watcher.Acquire(Consumer, _programs);
        else _app.Watcher.Release(Consumer);
    }

    private void OnModesChanged()
    {
        Watch();
        QueueBuild();
    }

    private void OnProcess(string name)
    {
        if (_programs.Contains(name)) QueueBuild();
    }

    private void QueueBuild()
    {
        if (_buildQueued) return;
        _buildQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _buildQueued = false;
            if (_shown) Build();
        }, DispatcherPriority.Background);
    }

    private void Build()
    {
        _body.Children.Clear();
        var header = new Grid { Margin = new Thickness(0, 0, 0, Ui.HeaderGap) };
        header.Children.Add(Ui.PluginTitle(PluginIds.Modes, "Automations and Modes"));
        _body.Children.Add(header);
        _body.Children.Add(SwitchingCard());

        _grid.Children.Clear();
        if (_app.Config.Modes.Count == 0)
        {
            _body.Children.Add(EmptyState());
            return;
        }
        int n = _app.Config.Modes.Count;
        var create = Ui.LabeledButton("I.Plus", "New mode", () => StartNewMode(), "PrimaryButton");
        create.ToolTip = "Starts from the setup that's on right now";
        _body.Children.Add(Ui.SectionHeader("Your modes", $"{n} saved · Apply switches the whole PC to one; triggers do it for you", create));
        var active = _app.Modes.Active;
        foreach (var mode in _app.Config.Modes)
            _grid.Children.Add(ModeCard(mode, mode == active));
        _body.Children.Add(_grid);
    }

    private Tile SwitchingCard()
    {
        var settings = new SettingsFlow();
        bool paused = _app.Automations.Paused || _app.Switcher.Paused;
        var autoRow = Ui.SettingRow("Switch automatically",
            paused ? "Paused: modes kept triggering each other (too many switches in a minute). Check their triggers, then resume."
            : S.Enabled ? null
            : "Triggers wait: only Apply, keybinds and the tray switch modes.",
            Ui.Switch(S.Enabled, v => { S.Enabled = v; _app.RaiseModesChanged(); }));
        Ui.Anchor(autoRow, "modes.auto");
        settings.Children.Add(autoRow);
        if (paused)
        {
            var resume = Ui.LabeledButton("I.Play", "Resume", () => { _app.Automations.Resume(); _app.Switcher.Resume(); }, "SoftButton");
            resume.HorizontalAlignment = HorizontalAlignment.Left;
            resume.Margin = new Thickness(0, 0, 0, 8);
            settings.Children.Add(resume);
        }
        settings.Children.Add(Ui.Divider());
        var restoreRow = Ui.SettingRow("Restore the previous mode", "When a program with a “while it runs” trigger closes, go back to the mode from before it started (unless you switched mode yourself in the meantime).",
            Ui.Switch(S.RestorePrevious, v => { S.RestorePrevious = v; _app.Save(); }));
        Ui.Anchor(restoreRow, "modes.restore");
        settings.Children.Add(restoreRow);
        return new Tile { Style = Ui.Res<Style>("Card"), Margin = new Thickness(0, 0, 0, 18), Padding = new Thickness(20, 8, 20, 8), Child = settings };
    }

    private Border EmptyState()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 24) };
        var plugin = _app.Plugins.Find(PluginIds.Modes);
        var colors = Hues.Tool(PluginIds.Modes, plugin?.Colors ?? Hues.Accent);
        var icon = new NavIcon { Glyph = plugin is { Pages.Count: > 0 } ? plugin.Pages[0].Icon : plugin?.Icon, IsActive = true, Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetResourceReference(NavIcon.ForegroundProperty, colors.Deep);
        stack.Children.Add(icon);
        stack.Children.Add(new TextBlock { Text = "No modes yet", Style = Ui.Res<Style>("H2"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 4) });
        stack.Children.Add(new TextBlock
        {
            Text = "A mode bundles a display profile, audio, fans, overlay and programs, so one click, a keybind or a trigger (a game starting, a schedule, the PC idle) sets the PC up for gaming, sim racing or work.",
            Style = Ui.Res<Style>("Muted"), TextAlignment = TextAlignment.Center, MaxWidth = 480, TextWrapping = TextWrapping.Wrap,
        });
        var create = Ui.LabeledButton("I.Plus", "New mode", () => StartNewMode(), "PrimaryButton");
        create.HorizontalAlignment = HorizontalAlignment.Center;
        create.Margin = new Thickness(0, 18, 0, 6);
        stack.Children.Add(create);
        return new Tile { Style = Ui.Res<Style>("Card"), Child = stack };
    }

    private Border ModeCard(Mode mode, bool isActive)
    {
        var stack = new StackPanel();

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(mode.Program is { } prog ? ExeIcons.IconTile(prog.IconPath ?? prog.Path, "I.Gamepad", 40) : ModeVisuals.Badge(40));
        var names = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition());
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.Children.Add(new TextBlock { Text = mode.Name, ToolTip = mode.Name, Style = Ui.Res<Style>("H2"), TextTrimming = TextTrimming.CharacterEllipsis });
        FrameworkElement? chip = isActive ? Ui.Chip(_app.Modes.AppliedFor is { } forApp && _app.Modes.LastApplied == mode.Name ? $"Active for {forApp}" : "Active", "ActiveModeBg", "ActiveModeFg")
            : mode.OneOff ? Ui.Chip("One-off") : null;
        if (chip is not null)
        {
            chip.Margin = new Thickness(10, 2, 0, 0);
            chip.VerticalAlignment = VerticalAlignment.Center;
            if (mode.OneOff) chip.ToolTip = "Runs its changes without becoming the active mode";
            Grid.SetColumn(chip, 1);
            nameRow.Children.Add(chip);
        }
        names.Children.Add(nameRow);
        if (!mode.OneOff || mode.DisplayProfile is not null)
            names.Children.Add(Ui.Text(Summary(mode), "Caption"));
        Grid.SetColumn(names, 1);
        top.Children.Add(names);

        string hotkey = mode.Hotkey is null ? "" : $" ({GoodieBag.Hotkeys.Hotkey.Display(mode.Hotkey)})";
        Button apply;
        if (mode.Program is not null)
        {
            bool running = ModeLauncher.IsRunning(_app, mode);
            string program = ModeLauncher.ProgramName(mode);
            var launch = running
                ? Ui.LabeledButton("I.Check", "Running", () => _ = ModeLauncher.LaunchAsync(_app, mode), "SoftButton")
                : Ui.LabeledButton("I.Play", "Launch", () => _ = ModeLauncher.LaunchAsync(_app, mode), "PrimaryButton");
            launch.ToolTip = running ? $"{program} is running" : $"Apply this mode, start its programs, then {program}";
            launch.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(launch, 2);
            top.Children.Add(launch);
            apply = isActive
                ? Ui.LabeledButton("I.Check", "Applied", () => _ = ApplyAsync(mode), "GhostButton")
                : Ui.LabeledButton("I.Zap", "Apply only", () => _ = ApplyAsync(mode), "GhostButton");
            apply.ToolTip = isActive ? "Apply again" : $"Apply this mode without starting {program}" + hotkey;
        }
        else
        {
            apply = mode.OneOff ? Ui.LabeledButton("I.Play", "Run", () => _ = ApplyAsync(mode), "PrimaryButton")
                : isActive ? Ui.LabeledButton("I.Check", "Applied", () => _ = ApplyAsync(mode), "GhostButton")
                : Ui.LabeledButton("I.Zap", "Apply", () => _ = ApplyAsync(mode), "PrimaryButton");
            apply.ToolTip = (mode.OneOff ? "Run its changes now" : isActive ? "Apply again" : "Apply this mode") + hotkey;
            apply.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(apply, 2);
            top.Children.Add(apply);
        }
        stack.Children.Add(top);

        if (!mode.OneOff || mode.DisplayProfile is not null)
        {
            var previewBox = new Tile { Style = Ui.Res<Style>("Inset"), Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(8) };
            if (_app.DisplayProfiles.Find(mode.DisplayProfile)?.Layout is not { } layout)
            {
                previewBox.Child = new TextBlock { Text = mode.DisplayProfile is null ? "Leaves displays as they are" : $"Display profile “{mode.DisplayProfile}”", Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 30) };
            }
            else
            {
                var preview = new MonitorLayoutView { Height = 100 };
                preview.SetTiles(ModeLayout.Tiles(_app, layout));
                previewBox.Child = preview;
            }
            stack.Children.Add(previewBox);
        }

        var chips = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var text in SettingChips(mode))
            chips.Children.Add(Ui.Chip(text));
        stack.Children.Add(chips);

        stack.Children.Add(new TextBlock { Text = mode.OneOff ? "RUNS WHEN" : "SWITCHES ON WHEN", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 10, 0, 6) });
        if (mode.Triggers.Count == 0)
            stack.Children.Add(Ui.Text(mode.Hotkey is null ? "Only when you pick it: Apply or the tray." : $"Only when you pick it: Apply, the tray or {GoodieBag.Hotkeys.Hotkey.Display(mode.Hotkey)}.", "Caption"));
        foreach (var trigger in mode.Triggers)
            stack.Children.Add(TriggerLine(trigger));
        var addTrigger = Ui.LabeledButton("I.Plus", "Add trigger", () => _ = EditAsync(mode, addTrigger: true), "SoftButton");
        addTrigger.HorizontalAlignment = HorizontalAlignment.Left;
        addTrigger.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(addTrigger);

        var changes = _app.Modes.Preview(mode);
        stack.Children.Add(new TextBlock { Text = mode.OneOff ? "RUNNING IT WILL" : isActive ? "COMPARED WITH NOW" : "APPLYING IT WILL", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 12, 0, 6) });
        if (changes.Count == 0)
        {
            stack.Children.Add(Ui.ChangeLine(mode.Overlay == OverlayMode.Unchanged && mode.Message is null ? "Nothing to change: this matches your current setup." : "Nothing else to change right now.", "Same"));
        }
        else
        {
            foreach (var change in changes.Take(5))
                stack.Children.Add(Ui.ChangeLine(change.Text, change.Area));
            if (changes.Count > 5)
                stack.Children.Add(Ui.Text($"…and {changes.Count - 5} more", "Caption"));
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        void Action(Button b)
        {
            b.Margin = new Thickness(0, 0, 8, 8);
            actions.Children.Add(b);
        }
        if (mode.Program is not null) Action(apply);
        Action(Ui.LabeledButton("I.Pencil", "Edit", () => _ = EditAsync(mode), "GhostButton"));
        if (!mode.OneOff)
        {
            var update = Ui.LabeledButton("I.Refresh", "Update to current", () => UpdateToCurrent(mode), "GhostButton");
            update.ToolTip = "Use the display profile and audio devices that are active right now";
            Action(update);
        }
        Action(Ui.LabeledButton("I.Trash", "Delete", () => _ = DeleteAsync(mode), "DangerButton"));
        stack.Children.Add(actions);

        var card = new Tile { Style = Ui.Res<Style>("Card"), Child = stack };
        if (isActive)
        {
            card.BorderBrush = Ui.Res<Brush>("ActiveModeEdge");
            card.BorderThickness = new Thickness(1.5);
        }
        return card;
    }

    private Grid TriggerLine(AutomationRule trigger)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 4), Opacity = trigger.Enabled ? 1 : 0.6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var on = Ui.Switch(trigger.Enabled, v => { trigger.Enabled = v; _app.RaiseModesChanged(); });
        on.ToolTip = trigger.Enabled ? "Switch this trigger off" : "Switch this trigger on";
        on.VerticalAlignment = VerticalAlignment.Top;
        on.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(on);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = AutomationCatalog.Sentence(trigger), Style = Ui.Res<Style>("Body"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        if (Status(trigger) is { } status)
        {
            var line = new TextBlock { Text = status.Text, Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>(status.Brush), TextWrapping = TextWrapping.Wrap };
            text.Children.Add(status.Brush == "Warn" ? Ui.IconText("I.Alert", line, "Warn") : line);
        }
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private (string Text, string Brush)? Status(AutomationRule t)
    {
        if (!t.Enabled) return null;
        if (_app.Automations.TriggerOffNote(t) is { } off) return (off, "Warn");
        if (AutomationCatalog.UsesApp(t.Trigger) && string.IsNullOrWhiteSpace(t.App)) return ("No program chosen.", "Warn");
        if (!string.IsNullOrWhiteSpace(t.OnlyInMode) && _app.Modes.Find(t.OnlyInMode) is null) return ($"Only runs in the mode “{t.OnlyInMode}”, which no longer exists. Edit the trigger to pick another.", "Warn");
        if (t.Trigger == TriggerType.WhileRunning)
            return _app.Switcher.InCharge is { } c && c.Trigger.Id == t.Id ? ("Running now: this mode is in charge", "AccentText") : null;
        string? last = _app.Automations.LastRuns.TryGetValue(t.Id, out var run)
            ? run.Error is null ? $"Last ran {Dates.TimeWithSeconds(run.At)}" : $"Last run {Dates.TimeWithSeconds(run.At)}: {run.Error}" : null;
        string? next = null;
        if (ScheduleTriggers.IsScheduled(t.Trigger))
            next = _app.Automations.NextDue(t) is { } due ? $"next {due:ddd} {Dates.Short(due)}, {Dates.Time(due)}"
                : t.Trigger == TriggerType.Plugin ? _app.Automations.Find(t.PluginTrigger)?.NothingDue.TrimStart(' ', '·') : "not due again";
        string line = string.Join(" · ", new[] { last, next }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return line.Length == 0 ? null : (line, run?.Error is null ? "TextMuted" : "Danger");
    }

    private IEnumerable<string> SettingChips(Mode m)
    {
        if (m.Hotkey is not null) yield return "Keybind " + GoodieBag.Hotkeys.Hotkey.Display(m.Hotkey);
        if (m.DisplayProfile is { } display) yield return "Displays: " + display;
        if (m.Windows.Count > 0 && !m.OneOff) yield return m.Windows.Count == 1 ? "Window: " + m.Windows[0].Label : $"{m.Windows.Count} windows";
        if (m.Audio?.PlaybackName is { } speakers) yield return "Audio: " + Shorten(speakers);
        if (m.Volume is { } v) yield return $"Volume {v}%";
        bool overlay = _app.Plugins.IsActive(PluginIds.Overlay);
        if (m.Overlay != OverlayMode.Unchanged && overlay) yield return m.Overlay switch { OverlayMode.Show => "Overlay on", OverlayMode.Hide => "Overlay off", _ => "Overlay on / off" };
        if (m.OverlayLayout is { } layout && overlay) yield return "Overlay: " + layout;
        if (m.FanPreset is not null && _app.Plugins.IsActive(PluginIds.FanControl)) yield return "Fans: " + m.FanPreset;
        if (m.Launch.Count > 0) yield return $"Starts {m.Launch.Count} program{(m.Launch.Count == 1 ? "" : "s")}" + (m.Program is not null && m.CloseLaunchedOnExit ? $", closed with {ModeLauncher.ProgramName(m)}" : "");
        if (m.Close.Count > 0) yield return $"Closes {m.Close.Count}";
        if (m.Message is not null) yield return "Notification";
        if (m.Priority != AutoPriority.Normal) yield return $"Switching priority: {m.Priority}";
    }

    private static string Shorten(string s) => s.Length > 28 ? s[..26] + "…" : s;

    private string Summary(Mode p)
    {
        if (_app.DisplayProfiles.Find(p.DisplayProfile)?.Layout is not { } layout)
            return p.DisplayProfile is null ? "Leaves displays alone" : "Display profile " + p.DisplayProfile;
        var monitors = _app.Displays.Describe(layout);
        var main = monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();
        string count = monitors.Count == 1 ? "1 display" : $"{monitors.Count} displays";
        return main is null ? p.DisplayProfile! : $"{count} · {main.Width}×{main.Height} @ {main.Hz} Hz";
    }

    private async Task ApplyAsync(Mode mode)
    {
        await _app.Modes.ApplyAsync(mode);
    }

    private void UpdateToCurrent(Mode mode)
    {
        var fresh = _app.Modes.CaptureCurrent(mode.Name);
        var took = new List<string>();
        if (fresh.DisplayProfile is not null)
        {
            mode.DisplayProfile = fresh.DisplayProfile;
            took.Add("display profile");
        }
        if (fresh.Audio is not null && (mode.DisplayProfile is null || mode.Audio is not null))
        {
            bool comms = mode.Audio?.SetCommunications ?? true;
            mode.Audio = fresh.Audio;
            mode.Audio.SetCommunications = comms;
            took.Add("audio");
        }
        if (took.Count == 0)
        {
            _app.Notify("Nothing to update", $"No display profile matches your monitors now, and {mode.Name} keeps its audio from its display profile. Save a display profile first.", NoticeKind.Info);
            return;
        }
        _app.Modes.MarkApplied(mode.Name);
        _app.RaiseModesChanged();
        _app.Notify("Mode updated", $"{mode.Name} now uses your current {string.Join(" and ", took)}.", NoticeKind.Success);
    }

    private async Task DeleteAsync(Mode mode)
    {
        string extra = mode.Triggers.Count == 0 ? "" : $" Its {(mode.Triggers.Count == 1 ? "trigger goes" : $"{mode.Triggers.Count} triggers go")} with it.";
        if (!await _window.Confirm($"Delete “{mode.Name}”?", "The mode is removed. Your displays, audio and settings stay exactly as they are now." + extra, "Delete", danger: true))
            return;
        _app.Modes.Remove(mode);
        _app.RaiseModesChanged();
    }

    public void StartNewMode() => _ = EditAsync(_app.Modes.CaptureCurrent(""), isNew: true);

    private async Task EditAsync(Mode mode, bool isNew = false, bool addTrigger = false)
    {
        string oldName = mode.Name;
        var editor = new ModeEditor(_app, _window, mode, isNew, addTrigger);
        if (!await _window.ShowModal(editor.Card))
            return;
        if (isNew)
        {
            _app.Config.Modes.Add(mode);
            if (!mode.OneOff && mode.Triggers.Count == 0 && mode.Program is null && mode.Launch.Count == 0)
                _app.Modes.MarkApplied(mode.Name);
        }
        else
        {
            _app.Modes.Renamed(oldName, mode.Name);
        }
        _app.RaiseModesChanged();
        _app.Notify(isNew ? "Mode saved" : "Mode updated", mode.Hotkey is null ? mode.Name : $"{mode.Name} · {GoodieBag.Hotkeys.Hotkey.Display(mode.Hotkey)}", NoticeKind.Success);
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("editor", () =>
        {
            if (_app.Config.Modes.FirstOrDefault() is { } first)
                _ = EditAsync(first);
            return Task.CompletedTask;
        });
        yield return ("editor-triggers", () =>
        {
            _window.FindModalScroll()?.ScrollToVerticalOffset(0);
            return Task.CompletedTask;
        });
        yield return ("editor-audio", () =>
        {
            if (_window.FindModalScroll() is { } sv) sv.ScrollToVerticalOffset(sv.ScrollableHeight * 0.45);
            return Task.CompletedTask;
        });
        yield return ("editor-bottom", () =>
        {
            if (_window.FindModalScroll() is { } sv) sv.ScrollToEnd();
            return Task.CompletedTask;
        });
        yield return ("close-editor", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        yield return ("one-off-editor", () =>
        {
            if (_app.Config.Modes.FirstOrDefault(m => m.OneOff) is { } oneOff)
                _ = EditAsync(oneOff);
            return Task.CompletedTask;
        });
        yield return ("close-one-off", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        yield return ("bottom", () =>
        {
            (Content as ScrollViewer)?.ScrollToEnd();
            return Task.CompletedTask;
        });
    }
}

using System.Windows;
using System.Windows.Controls;
using GoodieBag.Controls;
using GoodieBag.Plugins;
using GoodieBag.Plugins.BuiltIn;
using GoodieBag.Services;
using GoodieBag.SimRacing;
using GoodieBag.Theme;

namespace GoodieBag.Views.Pages;

internal sealed class CompanionAppSection : Tile
{
    private readonly AppServices _app;
    private readonly Func<SimRacingPlugin, CompanionApp?> _pick;
    private readonly string _about;
    private readonly StackPanel _stack = new SettingsFlow();
    private CompanionApp? _subscribed;

    public CompanionAppSection(AppServices app, Func<SimRacingPlugin, CompanionApp?> pick, string about)
    {
        _app = app;
        _pick = pick;
        _about = about;
        Style = Ui.Res<Style>("Card");
        Child = _stack;
        Build();
    }

    private CompanionApp? Service => _app.Plugins.Find(PluginIds.SimRacing) is SimRacingPlugin plugin ? _pick(plugin) : null;

    public void OnShown()
    {
        if (_subscribed is null && Service is { } service)
        {
            service.Changed += Build;
            service.WatchStatus(true);
            _subscribed = service;
        }
        Build();
    }

    public void OnHidden()
    {
        if (_subscribed is not null)
        {
            _subscribed.Changed -= Build;
            _subscribed.WatchStatus(false);
        }
        _subscribed = null;
    }

    private void Build()
    {
        _stack.Children.Clear();
        if (Service is not { } app)
            return;
        var spec = app.Spec;
        var status = app.Status();
        var s = app.Settings;

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var (text, soft, deep) = status switch
        {
            CompanionStatus.Running => ("Running", "SageSoft", "SageDeep"),
            CompanionStatus.NotRunning => ("Installed · not running", "SlateSoft", "SlateDeep"),
            CompanionStatus.NotInstalled => ("Not installed", "ApricotSoft", "ApricotDeep"),
            _ => ("Unable to determine whether it's running", "WarnSoft", "Warn"),
        };
        var chip = Ui.Chip(text, soft, deep);
        chip.HorizontalAlignment = HorizontalAlignment.Left;
        info.Children.Add(chip);
        var where = new TextBlock
        {
            Text = app.ExePath ?? $"{CompanionApp.Upper(spec.Program)} wasn't found on this PC. {_about} Install it from the official website, then come back here.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 12, 0), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left,
        };
        TextCase.SetVerbatim(where, app.ExePath is not null);
        info.Children.Add(where);
        top.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var visit = Ui.LabeledButton("I.External", spec.Host, () => Ui.OpenUrl(spec.Site), "GhostButton");
        visit.ToolTip = $"Open {spec.Site}";
        visit.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(visit);
        if (status == CompanionStatus.NotInstalled)
        {
            actions.Children.Add(Ui.LabeledButton("I.Folder", "Choose the program…", () =>
            {
                if (ProgramPicker.BrowseExe() is { } path) app.SetCustomPath(path);
            }));
            var site = Ui.LabeledButton("I.Download", $"Open the {spec.Name} website", () => Ui.OpenUrl(spec.Website), "PrimaryButton");
            site.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(site);
        }
        else
        {
            var check = Ui.IconButton("I.Refresh", "Check again", app.Refresh);
            check.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(check);
            bool running = status == CompanionStatus.Running;
            var launch = Ui.LabeledButton(running ? "I.Check" : "I.Play", running ? "Running" : $"Launch {spec.Name}", () =>
            {
                app.Launch();
                _ = Task.Delay(2500).ContinueWith(_ => { if (_subscribed is not null) Build(); }, TaskScheduler.FromCurrentSynchronizationContext());
            }, "PrimaryButton");
            launch.IsEnabled = !running;
            actions.Children.Add(launch);
            if (running)
            {
                Button stop = null!;
                stop = Ui.LabeledButton("I.Stop", "Stop", async () =>
                {
                    stop.IsEnabled = false;
                    await app.CloseAsync();
                }, "DangerButton");
                stop.ToolTip = $"Close {spec.Program}";
                stop.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(stop);
            }
        }
        Grid.SetColumn(actions, 1);
        top.Children.Add(actions);
        _stack.Children.Add(top);

        _stack.Children.Add(new Border { Height = 1, Background = Ui.Res<System.Windows.Media.Brush>("Line"), Margin = new Thickness(0, 14, 0, 4) });
        _stack.Children.Add(Ui.SettingRow($"Launch {spec.Name} with Goodie Bag", "Starts it when Goodie Bag starts (skipped if it's already running).",
            Ui.Switch(s.LaunchWithGoodieBag, v => { s.LaunchWithGoodieBag = v; _app.Save(); })));
        _stack.Children.Add(Ui.SettingRow("Launch when iRacing starts", null,
            Ui.Switch(s.LaunchWithIracing, v => { s.LaunchWithIracing = v; _app.Save(); app.UpdateWatch(); })));
        _stack.Children.Add(new TextBlock
        {
            Text = $"Goodie Bag starts {spec.Program} the normal way and never changes or updates it. It keeps running after Goodie Bag exits; Stop closes it.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        });
    }
}

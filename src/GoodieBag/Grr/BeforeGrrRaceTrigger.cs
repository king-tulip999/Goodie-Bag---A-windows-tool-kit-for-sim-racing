using System.Windows;
using System.Windows.Controls;
using GoodieBag.Automations;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.Views;

namespace GoodieBag.Grr;

internal sealed class BeforeGrrRaceTrigger : PluginTrigger
{
    private const string Consumer = "Automations";
    private const string LeaguesOption = "leagues";
    private readonly AppServices _app;
    private bool _held;

    public BeforeGrrRaceTrigger(AppServices app) => _app = app;

    public override string Id => "BeforeGrrRace";
    public override string Label => "Before a GRR race";
    public override string Help => "Runs the chosen number of minutes before each race of the GRR leagues you pick, from the schedules on the GRR website.";
    public override int DefaultThreshold => 30;
    public override string NothingDue => " · no upcoming race found yet";

    public IReadOnlyList<string> LeaguesOf(AutomationRule rule) =>
        rule.Option<List<string>>(LeaguesOption) is { Count: > 0 } own ? own : _app.Config.Grr.Leagues;

    public override DateTime? NextDue(AutomationRule rule, DateTime from) => Due(rule.Threshold, _app.Grr.Starts(LeaguesOf(rule)), from);

    public static DateTime? Due(int leadMinutes, IEnumerable<DateTime> starts, DateTime from)
    {
        var lead = TimeSpan.FromMinutes(Math.Max(0, leadMinutes));
        var times = starts.Select(start => start - lead).Where(t => t >= from).ToList();
        return times.Count > 0 ? times.Min() : null;
    }

    public override string Summary(AutomationRule rule)
    {
        var own = rule.Option<List<string>>(LeaguesOption);
        return $"{rule.Threshold} min before {(own is not { Count: > 0 } ? "my GRR races" : string.Join(" / ", own.Select(GrrLeagues.Name)) + " races")}";
    }

    public override void BuildEditor(AutomationRule rule, Panel into)
    {
        var before = new NumberBox(rule.Threshold, "min before") { Minimum = 0, Maximum = 600 };
        before.ValueChanged += v => rule.Threshold = (int)v;
        into.Children.Add(Ui.FieldRow("Start", before));
        var picked = rule.Option<List<string>>(LeaguesOption) ?? new List<string>();
        var leagues = new WrapPanel();
        foreach (var league in GrrLeagues.All)
        {
            var id = league.Id;
            var box = new CheckBox { Content = league.Name, IsChecked = picked.Contains(id), Margin = new Thickness(0, 0, 14, 0) };
            box.Click += (_, _) =>
            {
                if (box.IsChecked == true) { if (!picked.Contains(id)) picked.Add(id); }
                else picked.Remove(id);
                rule.SetOption(LeaguesOption, picked.Count > 0 ? picked : null);
            };
            leagues.Children.Add(box);
        }
        into.Children.Add(Ui.FieldRow("GRR leagues", leagues));
        into.Children.Add(new TextBlock { Text = "None ticked = the leagues you follow (GRR League page → your driver and leagues).", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(150, 0, 0, 0) });
    }

    public override void ClearOptions(AutomationRule rule) => rule.SetOption<List<string>>(LeaguesOption, null);

    public override void SetInUse(bool inUse)
    {
        if (inUse == _held) return;
        _held = inUse;
        if (inUse) _app.Grr.Acquire(Consumer);
        else _app.Grr.Release(Consumer);
    }
}

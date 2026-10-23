using GoodieBag.SimRacing.Hud.Widgets;

namespace GoodieBag.SimRacing.Hud;

internal static class HudCatalog
{
    public static readonly HudWidgetDef[] All =
    {
        new("relative", "Relative", "The cars nearest to you on track, with gaps in seconds, laps ahead / behind, classes, license badges, pit stops and battles.", 448, 300, () => new RelativeWidget())
            { MinW = 180, MinH = 90, DefaultHz = 15, Options = RelativeWidget.Options, Editor = Editors.RelativeEditor.Def, Clickable = true,
              Limits = "Gaps are iRacing's own time-behind estimates for cars in your class, and distance × that car's pace for other classes." },
        new("standings", "Standings", "The whole field: overall and class positions, gaps, laps, pit stops, out laps, licenses and more, grouped by class.", 488, 520, () => new StandingsWidget())
            { MinW = 200, MinH = 120, DefaultHz = 5, Options = StandingsWidget.Options, Editor = Editors.StandingsEditor.Def, Clickable = true,
              Limits = "Pit stop counts, stints and out laps start when the overlay first sees the session. Other drivers' incidents come from iRacing's session information, which it updates every so often." },
        new("timing", "Lap timing & delta", "Live delta to your best, the session best or your optimal lap, predicted lap, lap times and sectors.", 380, 150, () => new TimingWidget())
            { MinW = 200, MinH = 70, DefaultHz = 20, Options = TimingWidget.Options, Editor = Editors.TimingEditor.Def,
              Limits = "Deltas are iRacing's own. Sector times are measured by Goodie Bag at iRacing's sector lines. iRacing doesn't publish whether a lap counts, so “clean” means no incident points and no pit road." },
        new("fuel", "Fuel & strategy", "Laps to go, the next pit lap and stops left; the tank with the laps it lasts; a grid of this lap / average / most / least with per lap, laps, refuel and what's left at the flag; your recent laps' use; the plan's state.", 320, 300, () => new FuelWidget())
            { MinW = 220, MinH = 120, DefaultHz = 4, Options = FuelWidget.Options, Editor = Editors.FuelEditor.Def,
              Limits = "Per-lap use is measured from your clean, green laps. Everything to the flag is an estimate." },
        new("session", "Session", "Session, time or laps left, lap, positions, phase, temperatures and progress.", 340, 120, () => new SessionWidget())
            { MinW = 140, MinH = 60, DefaultHz = 2, Options = SessionWidget.Options, Editor = Editors.SessionEditor.Def },
        new("alerts", "Alerts", "One place for every alert: fuel, pit limit, traffic, flags, incidents, best laps…", 340, 150, () => new AlertsWidget())
            { MinW = 160, MinH = 50, DefaultHz = 10, Options = AlertsWidget.Options, Editor = Editors.AlertsEditor.Def, HideWhenEmpty = true },
        new("traffic", "Multiclass traffic", "Other classes within a few seconds, closing rates and when you'll meet.", 320, 160, () => new TrafficWidget())
            { MinW = 180, MinH = 70, DefaultHz = 5, Options = TrafficWidget.Options, Editor = Editors.TrafficEditor.Def, HideWhenEmpty = true },
        new("tyres", "Tires", "Pressures, temperatures (inner / middle / outer) and wear for each corner.", 280, 180, () => new TyresWidget())
            { MinW = 180, MinH = 124, DefaultHz = 2, Options = TyresWidget.Options, Editor = Editors.TyresEditor.Def,
              Limits = "iRacing updates tire data in the pit box, not while driving." },
        new("weather", "Weather & track", "The sky, track wetness and rain, air and track temperature, humidity, and the wind as a pointer turning with your car.", 340, 124, () => new WeatherWidget())
            { MinW = 140, MinH = 80, DefaultHz = 5, Options = WeatherWidget.Options, Editor = Editors.WeatherEditor.Def,
              Limits = "No forecast: iRacing doesn't publish one." },
        new("wind", "Wind", "A pointer in a dotted dial showing where the wind blows, with its speed under it, turning with your car: red for a headwind, amber across, green behind.", 150, 150, () => new WindWidget())
            { MinW = 70, MinH = 70, DefaultHz = 5, Options = WindWidget.Options, Editor = Editors.WindEditor.Def,
              Limits = "iRacing gives the wind at the start / finish line, the same all round the track." },
        new("laps", "Lap history", "Your laps with deltas, what happened on them, fuel used, and consistency.", 320, 260, () => new LapsWidget())
            { MinW = 200, MinH = 110, DefaultHz = 2, Options = LapsWidget.Options, Editor = Editors.LapsEditor.Def },
        new("events", "Race events", "A timeline of flags, pit stops, places gained or lost, incidents and best laps.", 320, 200, () => new EventsWidget())
            { MinW = 300, MinH = 130, DefaultHz = 2, Options = EventsWidget.Options, Editor = Editors.EventsEditor.Def },
        new("block", "Data block", "Any single value (live, session or calculated) as a number, bar or graph.", 170, 80, () => new BlockWidget())
            { MinW = 150, MinH = 72, DefaultHz = 10, Options = BlockWidget.Options, Editor = Editors.BlockEditor.Def },
    };

    private static readonly Dictionary<string, HudWidgetDef> ByKind = All.ToDictionary(d => d.Kind, StringComparer.OrdinalIgnoreCase);

    public static HudWidgetDef? Find(string? kind) => kind is not null && ByKind.TryGetValue(kind, out var d) ? d : null;
}

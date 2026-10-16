using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using GoodieBag.Config;
using GoodieBag.Controls;
using GoodieBag.SessionStats;
using GoodieBag.SimRacing.Race;

namespace GoodieBag.Views.Pages;

internal static class FuelCalculator
{
    private const double GallonsPerLitre = 0.264172;

    public static Tile Card(string carDir, string carName, FuelCalcSettings settings, Action save)
    {
        var car = settings.For(carDir);
        var results = new Grid();
        for (int c = 0; c < 4; c++) results.ColumnDefinitions.Add(new ColumnDefinition());
        results.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        results.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var warning = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        var lengthHost = new ContentControl();
        var fuelHost = new ContentControl();
        var lapBox = new TextBox { Text = StatFormat.LapTime(car.LapTime), Width = 120, VerticalContentAlignment = VerticalAlignment.Center };
        lapBox.ToolTip = "m:ss.000 (1.19.5 or 1.19 read as 1:19.5 / 1:19)";
        var lossBox = new NumberBox(settings.PitLoss, "s", 70) { Minimum = 0, Maximum = 600, Step = 1 };
        NumberBox perLapBox = null!, tankBox = null!;

        double Unit() => settings.Gallons ? GallonsPerLitre : 1;

        void Recalc()
        {
            bool imperial = settings.Gallons;
            var plan = settings.ByLaps
                ? StintCalculator.Plan(settings.Laps, car.PerLap, car.Tank, settings.ExtraLap, settings.FormationLap)
                : StintCalculator.TimedPlan(settings.Minutes, car.LapTime, car.PerLap, car.Tank, settings.ExtraLap, settings.FormationLap, settings.PitLoss);
            results.Children.Clear();
            string Fuel(double litres) => StatFormat.Value(StatKind.Fuel, litres, imperial);
            void Put(int row, int col, FrameworkElement e)
            {
                e.Margin = new Thickness(0, row == 0 ? 0 : 14, 16, 0);
                Grid.SetRow(e, row);
                Grid.SetColumn(e, col);
                results.Children.Add(e);
            }
            var culture = CultureInfo.CurrentCulture;
            Put(0, 0, Result("Total laps", plan is null ? "–" : plan.TotalLaps.ToString(culture)));
            Put(0, 1, Result("Laps per stint", plan is null ? "–" : plan.LapsPerStint.ToString(culture)));
            Put(0, 2, Result("Final stint", plan is null ? "–" : plan.FinalStintLaps.ToString(culture) + (plan.FinalStintLaps == 1 ? " lap" : " laps")));
            Put(0, 3, Result("Pit stops", plan is null ? "–" : plan.Stops.ToString(culture), plan is { Stops: > 0 } ? StatsUi.Hue("Gold").Deep : null));
            Put(1, 0, Result("Fuel in total", plan is null ? "–" : Fuel(plan.TotalFuel), "AccentText"));
            Put(1, 1, Result(plan is { Stops: 0 } ? "Fuel to start" : "Fuel per stint", plan is null ? "–" : Fuel(plan.FuelPerStint)));
            Put(1, 2, Result("Final stint fuel", plan is null ? "–" : Fuel(plan.FinalStintFuel)));
            if (!settings.ByLaps && plan is { Stops: > 0 } && settings.PitLoss > 0)
                Put(1, 3, Result("Time in the pits", StatFormat.Duration(plan.Stops * settings.PitLoss)));
            warning.Visibility = plan is null ? Visibility.Visible : Visibility.Collapsed;
            warning.Text = "Fill in a race length, lap time and fuel use to see the plan.";
        }

        FrameworkElement LengthInput()
        {
            if (settings.ByLaps)
            {
                var box = new NumberBox(settings.Laps, "laps", 70) { Minimum = 1, Maximum = 2000, Step = 1 };
                box.ValueChanged += v => { settings.Laps = (int)Math.Round(v); save(); Recalc(); };
                return box;
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var minutes = new NumberBox(settings.Minutes, "min", 70) { Minimum = 1, Maximum = 1440, Step = 5 };
            minutes.ValueChanged += v => { settings.Minutes = v; save(); Recalc(); };
            row.Children.Add(minutes);
            foreach (int preset in new[] { 20, 30, 45, 60 })
            {
                var b = Ui.LabeledButton("I.Clock", preset.ToString(CultureInfo.CurrentCulture), () => { settings.Minutes = preset; save(); lengthHost.Content = LengthInput(); Recalc(); }, "GhostButton");
                b.Margin = new Thickness(6, 0, 0, 0);
                Ui.SetToggled(b, Math.Abs(settings.Minutes - preset) < 0.01);
                row.Children.Add(b);
            }
            return row;
        }

        void FuelInputs()
        {
            string unit = settings.Gallons ? "gal" : "L";
            perLapBox = new NumberBox(Math.Round(car.PerLap * Unit(), 2), unit + "/lap", 70) { Minimum = 0.01, Maximum = 50, Step = 0.05, Decimals = 2 };
            perLapBox.ValueChanged += v => { car.PerLap = v / Unit(); save(); Recalc(); };
            tankBox = new NumberBox(Math.Round(car.Tank * Unit(), 1), unit, 70) { Minimum = 1, Maximum = 1000, Step = 1, Decimals = 1 };
            tankBox.ValueChanged += v => { car.Tank = v / Unit(); save(); Recalc(); };
            fuelHost.Content = new StackPanel { Children = { perLapBox, Spaced(tankBox) } };
        }

        lengthHost.Content = LengthInput();
        FuelInputs();
        var mode = Ui.Segmented(new[] { "Time", "Laps" }, settings.ByLaps ? 1 : 0, i =>
        {
            settings.ByLaps = i == 1;
            save();
            lengthHost.Content = LengthInput();
            Recalc();
        }, "fuelCalcMode", new[] { "A timed race: laps from your lap time (the lap running when time ends is finished)", "A set number of laps" });
        var units = Ui.Segmented(new[] { "Liters", "Gallons" }, settings.Gallons ? 1 : 0, i =>
        {
            settings.Gallons = i == 1;
            save();
            FuelInputs();
            Recalc();
        }, "fuelCalcUnits");

        void CommitLap()
        {
            if (ParseLap(lapBox.Text) is { } t && Math.Abs(t - car.LapTime) > 0.0005) { car.LapTime = t; save(); }
            Recalc();
        }
        lapBox.LostKeyboardFocus += (_, _) => { CommitLap(); lapBox.Text = StatFormat.LapTime(car.LapTime); };
        lapBox.TextChanged += (_, _) => { if (ParseLap(lapBox.Text) is not null) CommitLap(); };
        lossBox.ValueChanged += v => { settings.PitLoss = v; save(); Recalc(); };
        var extra = new CheckBox { Content = "Extra lap", IsChecked = settings.ExtraLap, ToolTip = "Fuel for one lap more than the race needs", Margin = new Thickness(0, 0, 18, 0) };
        extra.Click += (_, _) => { settings.ExtraLap = extra.IsChecked == true; save(); Recalc(); };
        var formation = new CheckBox { Content = "Formation lap", IsChecked = settings.FormationLap, ToolTip = "Fuel for a formation lap before the start" };
        formation.Click += (_, _) => { settings.FormationLap = formation.IsChecked == true; save(); Recalc(); };

        var inputs = new SettingsFlow();
        inputs.Children.Add(Ui.SettingRow("Race length", null, new StackPanel { Children = { mode, Spaced(lengthHost) } }));
        inputs.Children.Add(Ui.SettingRow("Lap time", "Remembered for this car", lapBox));
        inputs.Children.Add(Ui.SettingRow("Fuel use and tank", "Per lap, then the car's usable tank; remembered for this car", new StackPanel { Children = { units, Spaced(fuelHost) } }));
        inputs.Children.Add(Ui.SettingRow("Pit stop", "Time a stop costs over a racing lap (timed races)", lossBox));
        inputs.Children.Add(Ui.SettingRow("Margin", null, new StackPanel { Orientation = Orientation.Horizontal, Children = { extra, formation } }));

        var resultsBlock = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var rule = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 14) };
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        resultsBlock.Children.Add(rule);
        resultsBlock.Children.Add(results);
        resultsBlock.Children.Add(warning);
        Recalc();
        return StatsSections.Section("Fuel calculator", "I.Droplet", carName, null, new StackPanel { Children = { inputs, resultsBlock } });
    }

    internal const double ShortestLap = 10;

    internal static double? ParseLap(string text)
    {
        text = text.Trim().Replace(',', '.');
        if (text.Length == 0) return null;
        var parts = text.Split(':', '.');
        if (parts.Any(p => p.Length is 0 or > 9 || !p.All(char.IsAsciiDigit))) return null;
        bool colon = text.Contains(':');
        double total;
        if (parts.Length == 3)
        {
            if (parts[1].Length != 2) return null;
            total = Minutes(parts[0], parts[1]) + double.Parse("0." + parts[2], CultureInfo.InvariantCulture);
        }
        else if (parts.Length == 2 && colon) total = Minutes(parts[0], parts[1]);
        else if (parts.Length == 2)
        {
            total = double.Parse(text, CultureInfo.InvariantCulture);
            if (total < ShortestLap && parts[1].Length >= 2)
                total = Minutes(parts[0], parts[1][..2]) + (parts[1].Length > 2 ? double.Parse("0." + parts[1][2..], CultureInfo.InvariantCulture) : 0);
        }
        else if (parts.Length == 1) total = double.Parse(text, CultureInfo.InvariantCulture);
        else return null;
        return double.IsFinite(total) && total is >= ShortestLap and < 3600 ? total : null;

        static double Minutes(string minutes, string seconds)
        {
            int s = int.Parse(seconds, CultureInfo.InvariantCulture);
            return s >= 60 ? double.NaN : int.Parse(minutes, CultureInfo.InvariantCulture) * 60 + s;
        }
    }

    private static FrameworkElement Spaced(FrameworkElement e)
    {
        e.Margin = new Thickness(0, 8, 0, 0);
        return e;
    }

    private static FrameworkElement Result(string label, string value, string? brush = null)
    {
        var v = new TextBlock { Text = value, Style = Ui.Res<Style>("BigValue"), FontSize = 20 };
        if (brush is not null) v.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return new StackPanel { Margin = new Thickness(0, 0, 16, 0), Children = { v, new TextBlock { Text = label, Style = Ui.Res<Style>("Caption") } } };
    }
}

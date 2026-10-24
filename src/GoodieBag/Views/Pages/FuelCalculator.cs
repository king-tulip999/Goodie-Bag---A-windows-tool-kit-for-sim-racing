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
        var headline = new TextBlock { Style = Ui.Res<Style>("BigValue"), FontSize = 28, TextWrapping = TextWrapping.Wrap };
        var subline = new TextBlock { Style = Ui.Res<Style>("Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var facts = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        facts.ColumnDefinitions.Add(new ColumnDefinition());
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var warning = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        var lengthHost = new ContentControl();
        var perLapHost = new ContentControl();
        var tankHost = new ContentControl();
        var lapBox = new TextBox { Text = StatFormat.LapTime(car.LapTime), Width = 120, VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        lapBox.ToolTip = "m:ss.000 (1.19.5 or 1.19 read as 1:19.5 / 1:19)";
        var lossBox = new NumberBox(settings.PitLoss, "s", 70) { Minimum = 0, Maximum = 600, Step = 1 };
        FrameworkElement lossRow = null!;
        NumberBox perLapBox = null!, tankBox = null!;

        double Unit() => settings.Gallons ? GallonsPerLitre : 1;

        void Recalc()
        {
            bool imperial = settings.Gallons;
            var plan = settings.ByLaps
                ? StintCalculator.Plan(settings.Laps, car.PerLap, car.Tank, settings.ExtraLap, settings.FormationLap)
                : StintCalculator.TimedPlan(settings.Minutes, car.LapTime, car.PerLap, car.Tank, settings.ExtraLap, settings.FormationLap, settings.PitLoss);
            if (lossRow is not null) lossRow.Visibility = settings.ByLaps ? Visibility.Collapsed : Visibility.Visible;
            facts.Children.Clear();
            facts.RowDefinitions.Clear();
            string Fuel(double litres) => StatFormat.Value(StatKind.Fuel, litres, imperial);
            void Fact(string label, string value)
            {
                int row = facts.RowDefinitions.Count;
                facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 4, 16, 4), VerticalAlignment = VerticalAlignment.Center };
                var v = new TextBlock { Text = value, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetRow(l, row);
                Grid.SetRow(v, row);
                Grid.SetColumn(v, 1);
                facts.Children.Add(l);
                facts.Children.Add(v);
            }
            var culture = CultureInfo.CurrentCulture;
            if (plan is null)
            {
                headline.Text = "–";
                subline.Text = "";
                warning.Visibility = Visibility.Visible;
                warning.Text = "Fill in a race length, lap time and fuel use to see the plan.";
                return;
            }
            warning.Visibility = Visibility.Collapsed;
            headline.Text = "Start with " + Fuel(plan.FuelPerStint);
            subline.Text = plan.Stops == 0 ? "No stop needed" : $"{RaceFormat.Count(plan.Stops, "stop")} · {RaceFormat.Count(plan.LapsPerStint, "lap")} per stint";
            Fact("Race laps", plan.TotalLaps.ToString(culture));
            Fact("Fuel in total", Fuel(plan.TotalFuel));
            if (plan.Stops > 0)
            {
                Fact("Fuel per stint", Fuel(plan.FuelPerStint));
                Fact("Final stint", RaceFormat.Count(plan.FinalStintLaps, "lap") + " · " + Fuel(plan.FinalStintFuel));
                if (!settings.ByLaps && settings.PitLoss > 0)
                    Fact("Time in the pits", StatFormat.Duration(plan.Stops * settings.PitLoss));
            }
        }

        FrameworkElement LengthInput()
        {
            if (settings.ByLaps)
            {
                var box = new NumberBox(settings.Laps, "laps", 70) { Minimum = 1, Maximum = 2000, Step = 1 };
                box.ValueChanged += v => { settings.Laps = (int)Math.Round(v); save(); Recalc(); };
                return box;
            }
            var row = new WrapPanel();
            var minutes = new NumberBox(settings.Minutes, "min", 70) { Minimum = 1, Maximum = 1440, Step = 5, Margin = new Thickness(0, 0, 6, 6) };
            minutes.ValueChanged += v => { settings.Minutes = v; save(); Recalc(); };
            row.Children.Add(minutes);
            foreach (int preset in new[] { 20, 30, 45, 60 })
            {
                var b = Ui.LabeledButton("I.Clock", preset.ToString(CultureInfo.CurrentCulture), () => { settings.Minutes = preset; save(); lengthHost.Content = LengthInput(); Recalc(); }, "GhostButton");
                b.Margin = new Thickness(0, 0, 6, 6);
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
            perLapHost.Content = perLapBox;
            tankHost.Content = tankBox;
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

        var inputs = new StackPanel();
        inputs.Children.Add(Input("Race length", new StackPanel { Children = { mode, Spaced(lengthHost) } }));
        inputs.Children.Add(Input("Lap time", lapBox, "Remembered for this car"));
        inputs.Children.Add(Input("Units", units));
        inputs.Children.Add(Input("Fuel per lap", perLapHost, "Remembered for this car"));
        inputs.Children.Add(Input("Usable tank", tankHost, "Remembered for this car"));
        lossRow = Input("Pit stop", lossBox, "Time a stop costs over a racing lap");
        inputs.Children.Add(lossRow);
        inputs.Children.Add(Input("Margin", new StackPanel { Orientation = Orientation.Horizontal, Children = { extra, formation } }));

        var summary = new Tile
        {
            Style = Ui.Res<Style>("Inset"), Padding = new Thickness(18, 16, 18, 16), VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel { Children = { new TextBlock { Text = "Plan", Style = Ui.Res<Style>("Eyebrow") }, headline, subline, facts, warning } },
        };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(inputs);
        layout.Children.Add(summary);
        void Place()
        {
            bool under = layout.ActualWidth is > 0 and < 820;
            Grid.SetRow(summary, under ? 1 : 0);
            Grid.SetColumn(summary, under ? 0 : 1);
            Grid.SetColumnSpan(summary, under ? 2 : 1);
            layout.ColumnDefinitions[1].Width = under ? new GridLength(0) : new GridLength(380);
            summary.Margin = under ? new Thickness(0, 12, 0, 0) : new Thickness(24, 0, 0, 0);
        }
        Place();
        layout.SizeChanged += (_, e) => { if (e.WidthChanged) Place(); };
        Recalc();
        return StatsSections.Section("Fuel calculator", "I.Droplet", carName, null, layout);
    }

    private static FrameworkElement Input(string label, FrameworkElement control, string? hint = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var text = new TextBlock { Text = label, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 12, 0) };
        if (hint is not null) Ui.AddInfo(text, hint);
        grid.Children.Add(text);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
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

}

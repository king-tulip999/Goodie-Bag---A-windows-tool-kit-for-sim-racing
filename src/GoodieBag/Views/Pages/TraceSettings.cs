using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GoodieBag.Controls;
using GoodieBag.Services;
using GoodieBag.SessionStats;

namespace GoodieBag.Views.Pages;

internal static class TraceSettings
{
    private static Popup? _open;
    private static DispatcherTimer? _settle;

    public static void CloseOpen()
    {
        if (_open is { } p) p.IsOpen = false;
    }

    internal static void PickLapsPerSession(string id)
    {
        if (_open?.Child is not Tile { Child: Panel body }) return;
        var combos = new List<ComboBox>();
        void Find(DependencyObject d)
        {
            if (d is ComboBox box) combos.Add(box);
            foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Find(child);
        }
        Find(body);
        if (combos.Count > 1) combos[1].SelectedItem = combos[1].Items.Cast<Choice>().FirstOrDefault(c => c.Id == id);
    }

    public static Button Button(AppServices app, SessionStatsService stats, MainWindow window)
    {
        Button? gear = null;
        gear = Ui.IconButton("I.Settings", "Lap traces: what's kept and the space it takes", () => Open(app, stats, window, gear!));
        Ui.Anchor(gear, "stats.traces");
        return gear;
    }

    private static void Open(AppServices app, SessionStatsService stats, MainWindow window, FrameworkElement anchor)
    {
        var settings = stats.Settings;
        var body = new StackPanel { Width = 380 };
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Custom, StaysOpen = false, AllowsTransparency = true,
            CustomPopupPlacementCallback = (size, target, _) => new[] { new CustomPopupPlacement(new Point(target.Width - size.Width, target.Height), PopupPrimaryAxis.Horizontal) },
            Child = new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(18, 14, 18, 16), Margin = new Thickness(0, 4, 0, 0), Child = body },
        };
        RenderOptions.SetClearTypeHint(popup.Child, ClearTypeHint.Enabled);
        Ui.FollowTarget(popup);
        _open = popup;
        popup.Closed += (_, _) => { if (ReferenceEquals(_open, popup)) _open = null; };

        async void Changed(Action? undo = null)
        {
            settings.Normalize();
            if (undo is not null && stats.Traces is { } traces)
            {
                var keep = stats.Retention;
                int drop = 0;
                try { drop = await Task.Run(() => TraceStore.Pruned(traces.All(), keep, DateTime.UtcNow, null).Count); }
                catch (Exception ex) { App.LogError(ex); }
                if (drop > 0)
                {
                    popup.IsOpen = false;
                    if (!await window.Confirm($"Remove {drop:N0} lap trace{(drop == 1 ? "" : "s")}?",
                            "They no longer fit what you chose to keep, so their lines and inputs are deleted now. Lap times and stats stay. Laps still in iRacing's telemetry files come back if you keep more later.",
                            "Remove", danger: true))
                    {
                        undo();
                        settings.Normalize();
                        return;
                    }
                }
            }
            app.Save();
            _settle ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _settle.Stop();
            _settle.Tick -= OnSettle;
            _settle.Tick += OnSettle;
            _settle.Start();
            void OnSettle(object? sender, EventArgs e) { _settle!.Stop(); _settle.Tick -= OnSettle; stats.RetentionChanged(); }
        }

        body.Children.Add(Ui.Text("Lap traces", "H3"));
        body.Children.Add(new TextBlock { Text = "Every lap's line and inputs, recorded while you drive or read from iRacing's telemetry files, for lap analysis.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) });
        var usage = new TextBlock { Text = "Working out the space used…", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

        var keepFor = Ui.Combo(Config.SessionStatsSettings.KeepDayChoices.Select(d => new Choice(d.ToString(CultureInfo.InvariantCulture), DaysName(d))),
            settings.TraceKeepDays.ToString(CultureInfo.InvariantCulture), c =>
            {
                int old = settings.TraceKeepDays;
                settings.TraceKeepDays = int.Parse(c.Id!, CultureInfo.InvariantCulture);
                Changed(() => settings.TraceKeepDays = old);
            }, 170);
        var perSession = Ui.Combo(Config.SessionStatsSettings.LapChoices.Select(n => new Choice(n.ToString(CultureInfo.InvariantCulture), n switch { 0 => "All laps", 1 => "Fastest only", _ => $"Fastest {n}" })),
            settings.TraceLapsPerSession.ToString(CultureInfo.InvariantCulture), c =>
            {
                int old = settings.TraceLapsPerSession;
                settings.TraceLapsPerSession = int.Parse(c.Id!, CultureInfo.InvariantCulture);
                Changed(() => settings.TraceLapsPerSession = old);
            }, 170);
        perSession.ToolTip = "Per session, clean laps first";
        var record = Ui.Switch(settings.TraceRecord, on => { settings.TraceRecord = on; keepFor.IsEnabled = perSession.IsEnabled = on; Changed(); });
        keepFor.IsEnabled = perSession.IsEnabled = settings.TraceRecord;
        var bests = Ui.Switch(settings.TraceKeepBests, on => { settings.TraceKeepBests = on; Changed(() => settings.TraceKeepBests = !on); });

        body.Children.Add(Row("Keep lap traces", record, "Off: no new traces are kept. Back on: sessions driven meanwhile get theirs from their files"));
        body.Children.Add(Row("Keep for", keepFor, null));
        body.Children.Add(Row("Laps per session", perSession, null));
        body.Children.Add(Row("Keep my best laps", bests, "Each car and track's best clean lap stays, however old"));
        body.Children.Add(usage);

        var delete = Ui.LabeledButton("I.Trash", "Delete all lap traces", async () =>
        {
            popup.IsOpen = false;
            if (!await window.Confirm("Delete all lap traces?", "Every lap's line and inputs go. Your session stats and lap times stay. New sessions are still traced while traces are on.", "Delete all", danger: true)) return;
            stats.DeleteAllTraces();
        }, "DangerButton");
        delete.HorizontalAlignment = HorizontalAlignment.Left;
        delete.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(delete);

        popup.IsOpen = true;
        _ = FillUsage(stats, usage);
    }

    private static async Task FillUsage(SessionStatsService stats, TextBlock usage)
    {
        var (traces, store) = (stats.Traces, stats.Store);
        if (traces is null || store is null) { usage.Text = "Lap traces aren't available (their database didn't open)."; return; }
        var (bytes, laps, average, perMonth) = await Task.Run(() =>
        {
            var u = traces.Usage();
            long since = StatsStore.Unix(DateTime.UtcNow.AddDays(-90));
            double lapsPerMonth = Convert.ToDouble(store.Db.Scalar("SELECT COUNT(*) FROM laps l JOIN sessions s ON s.id = l.session_id WHERE s.start_utc >= ?1", since) ?? 0L) / 3;
            return (u.Bytes, u.Laps, u.AverageBytes, lapsPerMonth);
        });
        usage.Text = $"Using {Size(bytes)} · {laps:N0} lap{(laps == 1 ? "" : "s")}";
        if (laps < 20 || perMonth <= 0) return;
        usage.Text += $"\nAt your pace a year of traces takes about {Size(average * perMonth * 12)}";
    }

    internal static string DaysName(int days) => days switch
    {
        0 => "Forever", 30 => "30 days", 90 => "90 days", 182 => "6 months", 365 => "1 year", 730 => "2 years", _ => days + " days",
    };

    private static string Size(double bytes) => bytes switch
    {
        >= 1e9 => (bytes / 1e9).ToString("0.0", CultureInfo.CurrentCulture) + " GB",
        >= 1e6 => (bytes / 1e6).ToString("0", CultureInfo.CurrentCulture) + " MB",
        _ => (bytes / 1e3).ToString("0", CultureInfo.CurrentCulture) + " KB",
    };

    private static FrameworkElement Row(string label, FrameworkElement control, string? help)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10), ToolTip = help };
        control.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Body"), FontSize = 13 });
        if (help is not null) text.Children.Add(new TextBlock { Text = help, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 12, 0) });
        row.Children.Add(text);
        return row;
    }
}

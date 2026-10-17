using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GoodieBag.Services;
using GoodieBag.SessionStats;
using GoodieBag.SimRacing.IracingFiles;

namespace GoodieBag.Views.Pages;

internal sealed class TelemetryFilesCard : ContentControl
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly SessionStatsService _stats;
    private readonly StackPanel _body = new();
    private int _version;

    public TelemetryFilesCard(AppServices app, MainWindow window, SessionStatsService stats)
    {
        _app = app;
        _window = window;
        _stats = stats;
        Content = StatsSections.Section("Telemetry files", "I.Folder", null, null, _body);
        Ui.Anchor(this, "stats.telemetry");
        _body.Children.Add(Ui.Text("Reading your telemetry folder…", "Caption"));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        int version = ++_version;
        var store = _stats.Store;
        TelemetryReport? report;
        try { report = await Task.Run(() => TelemetryFiles.Read(name => store?.Imported(name) == true)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { report = null; }
        if (version != _version) return;
        Show(report);
    }

    private void Show(TelemetryReport? r)
    {
        _body.Children.Clear();
        var flow = new SettingsFlow();
        var recording = Ui.Switch(r?.Recording == true, on => _ = SetRecordingAsync(on));
        recording.IsEnabled = r?.Recording is not null && !_app.Demo;
        flow.Children.Add(Ui.SettingRow("Record telemetry to disk",
            "iRacing saves an .ibt file of every session you drive (its own setting, saved in app.ini; it takes effect the next time iRacing starts). "
            + "Laps from those files get their exact GPS line in the lap analysis and replay; laps only recorded live are worked out from speed and heading and can sit 2–6 m off.",
            recording));
        _body.Children.Add(flow);

        if (r is null)
        {
            _body.Children.Add(Note("iRacing's documents folder wasn't found, so there's no telemetry folder to show."));
            return;
        }

        string rate = r.BytesPerHour is { } bph ? $" · about {Mb(bph)} per hour of driving (measured from your newest files)" : "";
        var size = Ui.Text($"{r.Files.Count:N0} file{(r.Files.Count == 1 ? "" : "s")} · {HealthCheck.Gb(r.TotalBytes)}{rate}", "Body");
        size.TextWrapping = TextWrapping.Wrap;
        var sizeLine = Ui.IconText("I.Folder", size, "TextMuted");
        sizeLine.Margin = new Thickness(0, 14, 0, 0);
        _body.Children.Add(sizeLine);

        if (r.TotalBytes >= HealthCheck.TelemetryWarnBytes)
        {
            var warn = Ui.Notice("Warn", "I.Alert", $"The folder is {HealthCheck.Gb(r.TotalBytes)}",
                "iRacing never deletes these files. Clear the ones Session Stats has already read to get the space back.");
            warn.Margin = new Thickness(0, 14, 0, 0);
            _body.Children.Add(warn);
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var open = Ui.LabeledButton("I.Folder", "Open telemetry folder", () => OpenFolder(r.Folder));
        open.Margin = new Thickness(0, 0, 8, 8);
        buttons.Children.Add(open);
        var clear = Ui.LabeledButton("I.Trash", r.Clearable.Count > 0
            ? $"Move {r.Clearable.Count:N0} read file{(r.Clearable.Count == 1 ? "" : "s")} to the Recycle Bin ({HealthCheck.Gb(r.ClearableBytes)})"
            : "Nothing to clear", () => _ = ClearAsync(r), "DangerButton");
        clear.IsEnabled = r.Clearable.Count > 0 && !_app.Demo;
        clear.Margin = new Thickness(0, 0, 8, 8);
        buttons.Children.Add(clear);
        _body.Children.Add(buttons);
        _body.Children.Add(Note("Only files Session Stats has already read are moved: their laps, stats and lines are saved here. "
            + "The newest file and any file not read yet are always kept."));
    }

    private static FrameworkElement Note(string text)
    {
        var t = Ui.Text(text, "Caption");
        t.TextWrapping = TextWrapping.Wrap;
        var line = Ui.IconText("I.Info", t, "TextMuted");
        line.Margin = new Thickness(0, 4, 0, 0);
        return line;
    }

    private static string Mb(double bytes) => bytes >= 1e9
        ? (bytes / 1e9).ToString("0.0", CultureInfo.CurrentCulture) + " GB"
        : (bytes / 1e6).ToString("0", CultureInfo.CurrentCulture) + " MB";

    private async Task SetRecordingAsync(bool on)
    {
        if (_app.Demo) return;
        try
        {
            await Task.Run(() => TelemetryFiles.SetRecording(on));
            _app.Notify(on ? "Telemetry recording on" : "Telemetry recording off",
                on ? "iRacing saves an .ibt file of every session from its next start." : "iRacing stops saving .ibt files from its next start.", NoticeKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _app.Notify("Telemetry recording wasn't changed", ex.Message, NoticeKind.Warning, important: true);
        }
        await LoadAsync();
    }

    private async Task ClearAsync(TelemetryReport r)
    {
        if (_app.Demo || r.Clearable.Count == 0) return;
        if (!await _window.Confirm("Move read telemetry to the Recycle Bin?",
                $"{r.Clearable.Count:N0} .ibt file{(r.Clearable.Count == 1 ? "" : "s")} ({HealthCheck.Gb(r.ClearableBytes)}) that Session Stats has already read. "
                + "Their laps and lines stay here, and you can restore the files from the Recycle Bin.", "Move to Recycle Bin", danger: true))
            return;
        try
        {
            int moved = await Task.Run(() => RecycleBin.Send(r.Clearable.Select(f => f.FullName).ToList()));
            _app.Notify("Telemetry tidied", $"{moved:N0} file{(moved == 1 ? "" : "s")} moved to the Recycle Bin.", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _app.Notify("Telemetry files weren't moved", ex.Message, NoticeKind.Warning, important: true);
        }
        await LoadAsync();
    }

    private static void OpenFolder(string folder)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { App.LogError(ex); }
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GoodieBag.Controls;
using GoodieBag.Processes;
using GoodieBag.Theme;

namespace GoodieBag.Views;

internal sealed record PickedProgram(string Process, string? Path, string Name);

internal static class ProgramPicker
{
    public static void Show(FrameworkElement anchor, Action<PickedProgram> picked)
    {
        var search = new TextBox { Tag = "Filter running programs", Margin = new Thickness(0, 0, 0, 8) };
        var list = new StackPanel();
        var scroll = new ScrollViewer { Content = list, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        var browse = Ui.LabeledButton("I.Folder", "Browse for a program…", () =>
        {
            popup.IsOpen = false;
            if (BrowseExe() is { } path)
                picked(new PickedProgram(AppWatcher.Normalize(path), path, FileDescription(path) ?? System.IO.Path.GetFileNameWithoutExtension(path)));
        }, "SoftButton");
        browse.HorizontalAlignment = HorizontalAlignment.Stretch;
        browse.Margin = new Thickness(0, 8, 0, 0);

        var stack = new StackPanel { Width = 360 };
        stack.Children.Add(new TextBlock { Text = "RUNNING NOW", Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(2, 0, 0, 6) });
        stack.Children.Add(search);
        stack.Children.Add(scroll);
        stack.Children.Add(browse);
        popup.Child = new Tile
        {
            Style = Ui.Res<Style>("LiftedCard"),
            Padding = new Thickness(12),
            Margin = new Thickness(8, 4, 8, 16),
            Child = stack,
        };

        List<PickedProgram>? programs = null;
        void Fill()
        {
            list.Children.Clear();
            if (programs is null)
            {
                list.Children.Add(new TextBlock { Text = "Looking at what's running…", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(4, 6, 0, 6) });
                return;
            }
            string f = search.Text.Trim();
            foreach (var p in programs.Where(p => f.Length == 0 || p.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || p.Process.Contains(f, StringComparison.OrdinalIgnoreCase)).Take(60))
            {
                var row = new StackPanel();
                row.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }));
                row.Children.Add(TextCase.Verbatim(new TextBlock { Text = p.Process + ".exe", Style = Ui.Res<Style>("Caption") }));
                var b = new Button { Style = Ui.Res<Style>("QuietButton"), Content = row, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 10, 6) };
                var chosen = p;
                b.Click += (_, _) => { popup.IsOpen = false; picked(chosen); };
                list.Children.Add(b);
            }
            if (list.Children.Count == 0)
                list.Children.Add(new TextBlock { Text = "Nothing matches. Start the program, or browse for it.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(4, 6, 0, 6) });
        }
        search.TextChanged += (_, _) => Fill();
        Fill();
        popup.IsOpen = true;
        search.Focus();
        _ = LoadAsync();
        async Task LoadAsync()
        {
            try { programs = await Task.Run(Running); }
            catch (Exception ex) { App.LogError(ex); programs = new(); }
            Fill();
        }
    }

    private static List<PickedProgram> Running()
    {
        var withWindow = new List<PickedProgram>();
        var others = new List<PickedProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        static string NameOf(Process p) { try { return p.ProcessName; } catch { return ""; } }
        foreach (var p in Process.GetProcesses().OrderBy(NameOf))
        {
            try
            {
                if (p.Id == self || p.Id <= 4 || !seen.Add(p.ProcessName))
                    continue;
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                if (path is null || path.StartsWith(windows, StringComparison.OrdinalIgnoreCase) && p.MainWindowHandle == IntPtr.Zero)
                    continue;
                string name = FileDescription(path) ?? p.ProcessName;
                (p.MainWindowHandle != IntPtr.Zero ? withWindow : others).Add(new PickedProgram(p.ProcessName.ToLowerInvariant(), path, name));
            }
            catch { }
            finally { p.Dispose(); }
        }
        return withWindow.Concat(others).ToList();
    }

    private static string? FileDescription(string? path)
    {
        try
        {
            var d = path is null ? null : FileVersionInfo.GetVersionInfo(path).FileDescription;
            return string.IsNullOrWhiteSpace(d) ? null : d.Trim();
        }
        catch { return null; }
    }

    public static string? BrowseExe(string filter = "Programs (*.exe)|*.exe|Shortcuts and scripts (*.lnk;*.bat;*.cmd;*.ps1)|*.lnk;*.bat;*.cmd;*.ps1|All files (*.*)|*.*")
    {
        if (!GoodieBag.Services.External.Allowed("open dialog")) return null;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

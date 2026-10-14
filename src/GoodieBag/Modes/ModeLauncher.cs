using System.IO;
using GoodieBag.Config;
using GoodieBag.Processes;
using GoodieBag.Services;

namespace GoodieBag.Modes;

internal static class ModeLauncher
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> Watching = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<Mode> InFlight = new();

    public static string ProcessOf(ModeProgram program) =>
        AppWatcher.Normalize(string.IsNullOrWhiteSpace(program.ProcessName) ? program.Path : program.ProcessName!);

    public static bool CanDetect(ModeProgram program) =>
        !string.IsNullOrWhiteSpace(program.ProcessName) || program.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    public static bool IsRunning(AppServices app, Mode mode) =>
        mode.Program is { } p && CanDetect(p) && app.Watcher.IsRunning(ProcessOf(p));

    public static string ProgramName(Mode mode) =>
        mode.Program is { } p && !p.Path.Contains("://") ? Path.GetFileNameWithoutExtension(p.Path) : mode.Name;

    public static async Task LaunchAsync(AppServices app, Mode mode)
    {
        if (mode.Program is null || !InFlight.Add(mode)) return;
        try { await LaunchCoreAsync(app, mode, mode.Program); }
        finally { InFlight.Remove(mode); }
    }

    private static async Task LaunchCoreAsync(AppServices app, Mode mode, ModeProgram program)
    {
        var running = AppWatcher.Snapshot();
        if (CanDetect(program) && running.Contains(ProcessOf(program)))
        {
            app.Notify($"{ProgramName(mode)} is already running", "Nothing was started again.", NoticeKind.Info, important: true);
            return;
        }
        if (!app.Demo && !program.Path.Contains("://") && Path.IsPathFullyQualified(program.Path) && !File.Exists(program.Path))
        {
            app.Notify($"{mode.Name}: the program wasn't found", $"{program.Path} doesn't exist any more. Edit the mode to pick it again; nothing was changed.", NoticeKind.Warning, important: true);
            return;
        }

        var started = mode.Launch.Where(l => !running.Contains(AppWatcher.Normalize(l.Path))).Select(l => l.Path).ToList();
        if (!string.Equals(app.Modes.LastApplied, mode.Name, StringComparison.OrdinalIgnoreCase))
            app.Switcher.NoteRestorePoint(app.Modes.LastApplied, CanDetect(program) ? ProcessOf(program) : null, mode.Launch.Select(l => l.Path));
        bool applied = await app.Modes.ApplyAsync(mode, quiet: true);

        string? error = app.Demo ? null : ProcessLauncher.Launch(program.Path, program.Arguments, program.AsAdmin);
        if (error is not null)
        {
            app.Notify($"{mode.Name}: the program didn't start", error, NoticeKind.Warning, important: true);
            return;
        }
        if (mode.CloseLaunchedOnExit && started.Count > 0 && CanDetect(program) && !app.Demo)
            CloseWhenDone(app, mode, ProcessOf(program), started);
        if (applied)
            app.Notify($"Starting {mode.Name}", "", NoticeKind.Success);
    }

    private static void CloseWhenDone(AppServices app, Mode mode, string main, List<string> helpers)
    {
        string consumer = "Launch: " + main;
        if (!Watching.Add(consumer))
            return;
        bool seen = false;

        void Done()
        {
            app.Watcher.Started -= OnStarted;
            app.Watcher.Stopped -= OnStopped;
            app.Watcher.Release(consumer);
            Watching.Remove(consumer);
        }
        void OnStarted(string name) { if (name == main) seen = true; }
        async void OnStopped(string name)
        {
            if (name != main || !seen)
                return;
            Done();
            var closed = new List<string>();
            foreach (var helper in helpers)
            {
                try
                {
                    if (await ProcessLauncher.CloseAsync(helper, force: true, graceMs: 5000) > 0)
                        closed.Add(Path.GetFileNameWithoutExtension(helper));
                }
                catch { }
            }
            if (closed.Count > 0)
                app.Notify($"{mode.Name} closed", "Also closed " + string.Join(", ", closed) + ".", NoticeKind.Info);
        }

        app.Watcher.Started += OnStarted;
        app.Watcher.Stopped += OnStopped;
        app.Watcher.Acquire(consumer, new[] { main });
        seen = app.Watcher.IsRunning(main);
        _ = Task.Delay(StartTimeout).ContinueWith(_ =>
        {
            if (!seen && Watching.Contains(consumer))
                Done();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}

using System.IO;
using System.Text;
using System.Windows;
using GoodieBag.Config;
using GoodieBag.Hardware;

namespace GoodieBag.Services;

internal static class FanProbe
{
    public const string Arg = "--fan-probe";

    public static void Run(Application app, string file)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var log = new StringBuilder($"Goodie Bag fan probe {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        if (!SingleInstance.TryAcquire() || FanService.IsRunning)
        {
            Write(file, log.AppendLine("Goodie Bag (or its background fan control) is running: quit it (Stop fan control) and run the probe again."));
            app.Shutdown();
            return;
        }
        var hw = new HardwareService();
        hw.SetFanSettings(new FanSettings { Enabled = true });
        Task.Run(async () =>
        {
            try { await Probe(hw, log); }
            catch (Exception ex) { log.AppendLine("probe failed: " + ex); }
            finally
            {
                hw.Release("Fan probe");
                for (int i = 0; i < 50 && hw.Running; i++) await Task.Delay(100);
                await Task.Delay(1500);
                Write(file, log);
                _ = app.Dispatcher.BeginInvoke(new Action(app.Shutdown));
            }
        });
    }

    private static async Task Probe(HardwareService hw, StringBuilder log)
    {
        log.AppendLine(ChipLockContention());
        hw.Acquire("Fan probe");
        for (int i = 0; i < 300 && hw.Catalog is null; i++) await Task.Delay(100);
        if (hw.Catalog is null) { log.AppendLine("no readings after 30 s: " + (hw.OpenError ?? "sensors didn't open")); return; }
        await Task.Delay(2500);
        log.AppendLine(await hw.ReadFansAsync(f => f.DescribeControl()));
        Snapshot(hw, log, "start (BIOS / driver control)");
        await Modes(hw, log);

        log.AppendLine("opening the full sensor list (storage / network) and closing it again…");
        hw.Acquire("Fan probe", fullTree: true);
        for (int i = 0; i < 450 && hw.Tree is null; i++) await Task.Delay(100);
        await Task.Delay(3000);
        hw.Acquire("Fan probe", fullTree: false);
        await Task.Delay(3000);
        Snapshot(hw, log, "after closing it");

        var fans = hw.Catalog!.Fans.Where(f => f.Controllable).ToList();
        foreach (var fan in fans) hw.SpinUp(fan.ControlId);
        await Task.Delay(1500);
        Snapshot(hw, log, "spin up (every fan asked for 100 %), 1.5 s");
        await Modes(hw, log);
        await Task.Delay(3000);
        Snapshot(hw, log, "spin up, 4.5 s");
        await Task.Delay(7000);
        Snapshot(hw, log, "spin up over: back to BIOS / driver");
        await Modes(hw, log);

        foreach (var fan in fans.Where(f => f.Rpm > 0))
        {
            hw.SetOverride(fan.ControlId, 100);
            await Task.Delay(2000);
            Snapshot(hw, log, $"{fan.DefaultName} held at 100 %, 2 s", fan.ControlId);
            await Task.Delay(3000);
            Snapshot(hw, log, $"{fan.DefaultName} held at 100 %, 5 s", fan.ControlId);
            hw.SetOverride(fan.ControlId, 40);
            await Task.Delay(5000);
            Snapshot(hw, log, $"{fan.DefaultName} held at 40 %, 5 s", fan.ControlId);
            hw.ClearOverride(fan.ControlId);
            await Task.Delay(5000);
            Snapshot(hw, log, $"{fan.DefaultName} released, 5 s", fan.ControlId);
        }
        await Modes(hw, log);
    }

    private static async Task Modes(HardwareService hw, StringBuilder log)
    {
        if (await hw.ReadFansAsync(f => f.MotherboardModes()) is { } modes) log.AppendLine("   motherboard chip: " + modes);
    }

    private static string ChipLockContention()
    {
        try
        {
            using var bus = Mutex.OpenExisting(@"Global\Access_ISABUS.HTP.Method");
            int busy = 0, tries = 300;
            for (int i = 0; i < tries; i++)
            {
                if (bus.WaitOne(0)) bus.ReleaseMutex(); else busy++;
                Thread.Sleep(5);
            }
            return $"motherboard chip lock: held by another program {busy} of {tries} checks";
        }
        catch (WaitHandleCannotBeOpenedException) { return "motherboard chip lock: no other program has it open"; }
        catch (Exception ex) { return "motherboard chip lock: " + ex.Message; }
    }

    private static void Snapshot(HardwareService hw, StringBuilder log, string step, string? only = null)
    {
        log.AppendLine($"-- {step}");
        foreach (var f in hw.Catalog?.Fans ?? [])
        {
            if (!f.Controllable || only is not null && f.ControlId != only) continue;
            log.AppendLine($"   {f.DefaultName,-48} {(f.Rpm is { } r ? $"{r,6:0} RPM" : "    no RPM")}  read-back {(f.Percent is { } p ? $"{p,3:0} %" : "  n/a")}  {(f.Controlled ? "driven" : "auto  ")}  {f.Error}");
        }
    }

    private static void Write(string file, StringBuilder log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            File.WriteAllText(file, log.ToString());
        }
        catch (Exception ex) { App.LogError(ex); }
    }
}

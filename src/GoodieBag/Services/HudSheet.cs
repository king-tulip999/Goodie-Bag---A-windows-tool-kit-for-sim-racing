using System.Drawing;
using System.IO;
using GoodieBag.Config;
using GoodieBag.SimRacing.Hud;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.Services;

internal static class HudSheet
{
    public const string Arg = "--hud-sheet";

    public static void Run(App app, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Render(folder);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(folder, "error.txt"), ex.ToString()); } catch { }
        }
        app.Shutdown();
    }

    private static void Render(string folder)
    {
        var settings = new RaceHudSettings();
        var race = new RaceState(() => settings);
        var demo = new DemoTelemetry();
        var frame = new TelemetryFrame();
        long seq = 0;
        for (double t = -1500; t <= 0; t += 0.25)
        {
            demo.Fill(frame, t);
            frame.Sequence = ++seq;
            race.Update(frame);
        }
        Thread.Sleep(300);
        demo.Fill(frame, 0.25);
        frame.Sequence = ++seq;
        race.Update(frame);
        using var painter = new HudPainter();
        foreach (var colors in Enum.GetValues<HudColors>())
        {
            string name = colors.ToString();
            settings.Appearance.Colors = colors;
            var palette = HudPalette.Of(settings.Appearance);
            string dir = Path.Combine(folder, name);
            Directory.CreateDirectory(dir);
            foreach (var def in HudCatalog.All)
            {
                Save(def, def.W, def.H, Path.Combine(dir, def.Kind + ".png"));
                Save(def, def.MinW, def.MinH, Path.Combine(dir, def.Kind + "-min.png"));
            }
            var relative = HudCatalog.Find("relative")!;
            Save(relative, 460, relative.H, Path.Combine(dir, "relative-strips.png"), 1, ("header", "lap,best,delta,sof,air,track"), ("footer", "wind,humidity,wetness,cars,csof"));
            var flags = race.Frame.Flags;
            foreach (var (file, where, flag) in new[] { ("relative-flag-top", "top", RaceFlags.Yellow), ("relative-flag-checkered", "top", RaceFlags.Checkered), ("relative-flag-blue", "top", RaceFlags.Blue), ("relative-flag-meatball", "top", RaceFlags.Repair) })
            {
                race.Frame.Flags = flag;
                var (fl, ft, fr, fb) = HudFlags.Room(where);
                Save(relative, relative.W + fl + fr, relative.H + ft + fb, Path.Combine(dir, file + ".png"), 1, ("flagBanner", where), ("header", "lap,best,delta,sof,air,track"), ("footer", "wind,humidity,wetness,cars,csof"));
            }
            race.Frame.Flags = flags;

            foreach (var kind in new[] { "relative", "standings", "fuel" })
                Save(HudCatalog.Find(kind)!, HudCatalog.Find(kind)!.W, HudCatalog.Find(kind)!.H, Path.Combine(dir, kind + "@150.png"), 1.5f);

            void Save(HudWidgetDef def, double w, double h, string path, float scale = 1, params (string Key, string Value)[] options)
            {
                var config = new HudWidgetConfig { Id = "sheet-" + def.Kind, Kind = def.Kind, W = w, H = h };
                foreach (var (key, value) in options) config.Options[key] = value;
                using var bitmap = new Bitmap((int)Math.Round(w * scale), (int)Math.Round(h * scale), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    painter.Simulated = true;
                    painter.MarkSimulated = false;
                    painter.IsPreview = true;
                    painter.Begin(g, scale, new SizeF((float)w, (float)h), palette, settings.Appearance);
                    var view = new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = new HudOptions(def, config), Def = def };
                    def.Create().Draw(painter, view);
                }
                bitmap.Save(path);
            }
        }
    }
}

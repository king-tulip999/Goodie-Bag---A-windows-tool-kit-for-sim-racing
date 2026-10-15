using System.Globalization;
using System.IO;
using GoodieBag.Config;
using GoodieBag.SimRacing.IracingFiles;

namespace GoodieBag.SimRacing;

internal static class FovExchange
{
    public static IReadOnlyList<(string Key, string Value)> IracingValues(FovSetup setup, FovResult r)
    {
        static string Mm(double mm) => Math.Round(mm).ToString(CultureInfo.InvariantCulture);
        var values = new List<(string, string)>
        {
            ("NumMonitors", setup.Triple ? "3" : "1"),
            ("ScreenWidth", Mm(r.Centre.WidthCm * 10)),
            ("MonitorWidth", Mm(r.Centre.WidthCm * 10 + Math.Max(0, setup.BezelMm))),
            ("ViewingDist", Mm(setup.DistanceCm * 10)),
        };
        if (setup.Triple) values.Add(("ScreenAngles", Math.Round(Math.Clamp(r.SideAngle, 10, 65)).ToString(CultureInfo.InvariantCulture)));
        bool curved = r.Centre.RadiusCm is not null;
        values.Add(("MonitorType", curved ? "1" : "0"));
        if (curved) values.Add(("RadiusOfCurvature", Mm(setup.Centre.RadiusMm)));
        return values;
    }

    public static string? IracingFolder() =>
        IracingPaths.FindDocumentsFolder() is { } folder && File.Exists(Path.Combine(folder, IracingPaths.MonitorRenderer)) ? folder : null;

    public static double IracingFov(FovResult r) => Math.Round(r.HorizontalFovTotal, 1);

    public static int WriteIracing(string folder, FovSetup setup, FovResult r)
    {
        if (Processes.AppWatcher.IsRunningNow(IracingPaths.SimProcess))
            throw new InvalidOperationException("iRacing is running. It rewrites its settings when it closes, so close it first.");
        var store = new BackupStore(IracingPaths.BackupFolder);
        var backup = store.Create("Before the FOV calculator wrote the screen setup and FOV", IracingPaths.BackupFiles(folder), folder);
        SafeFileWriter.WriteAllAtomic(Updated(folder, setup, r).Where(u => u.Doc.IsDirty)
            .Select(u => (Path.Combine(folder, u.File), u.Doc.ToBytes())).ToList(), "written");
        return backup.Number;
    }

    public static List<string> SaveCopies(string source, string target, FovSetup setup, FovResult r)
    {
        if (SameFolder(source, target) || (IracingPaths.FindDocumentsFolder() is { } live && SameFolder(live, target)))
            throw new InvalidOperationException("That's iRacing's own settings folder. Pick another folder, or use “Write into iRacing” (it backs the files up first).");
        var written = new List<string>();
        foreach (var (file, doc) in Updated(source, setup, r))
        {
            string original = Path.Combine(source, file), stem = Path.GetFileNameWithoutExtension(file);
            File.Copy(original, FreeName(target, stem + "_backup", ".ini"), overwrite: false);
            var destination = Path.Combine(target, file);
            if (File.Exists(destination)) File.Copy(destination, FreeName(target, stem + "_previous", ".ini"), overwrite: false);
            SafeFileWriter.WriteAtomic(destination, doc.ToBytes());
            written.Add(file);
        }
        return written;
    }

    internal static string FreeName(string folder, string stem, string extension)
    {
        var path = Path.Combine(folder, stem + extension);
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem} ({n}){extension}");
        return path;
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(string File, IniDocument Doc)> Updated(string folder, FovSetup setup, FovResult r)
    {
        var monitorPath = Path.Combine(folder, IracingPaths.MonitorRenderer);
        if (!File.Exists(monitorPath))
            throw new FileNotFoundException($"{IracingPaths.MonitorRenderer} isn't in {folder}. Start iRacing once so it creates it.");
        var monitor = IniDocument.Load(monitorPath);
        foreach (var (key, value) in IracingValues(setup, r))
            monitor.TrySet("MonitorSetup", key, value);
        yield return (IracingPaths.MonitorRenderer, monitor);

        var appPath = Path.Combine(folder, IracingPaths.App);
        if (File.Exists(appPath))
        {
            var app = IniDocument.Load(appPath);
            if (app.Find("View", "drivingCamFOV") is { } fov)
                app.TrySet("View", "drivingCamFOV", SameStyle(fov.Value, IracingFov(r)));
            yield return (IracingPaths.App, app);
        }
    }

    private static string SameStyle(string? current, double value)
    {
        int dot = current?.IndexOf('.') ?? -1;
        return dot >= 0 ? value.ToString("F" + (current!.Length - dot - 1), CultureInfo.InvariantCulture) : Math.Round(value).ToString(CultureInfo.InvariantCulture);
    }

    public static double? CurrentIracingFov(string folder)
    {
        var path = Path.Combine(folder, IracingPaths.App);
        return File.Exists(path) && IniDocument.Load(path).Find("View", "drivingCamFOV") is { } line && CommentInference.TryNumber(line.Value, out var v) ? v : null;
    }

    public static FovSetup FromIracing(string path, FovSetup current)
    {
        var doc = IniDocument.Load(path);
        double? Get(string key) => doc.Find("MonitorSetup", key) is { } line && CommentInference.TryNumber(line.Value, out var n) && double.IsFinite(n) ? n : null;
        if (Get("ScreenWidth") is not { } screen || Get("ViewingDist") is not { } dist)
            throw new InvalidDataException("That file has no [MonitorSetup] screen width and viewing distance.");
        if (screen <= 0 || dist <= 0)
            throw new InvalidDataException("That file's [MonitorSetup] screen width or viewing distance isn't set (0 or less).");

        var rig = current.Copy();
        rig.Name = "From iRacing";
        double aspect = FovCalculator.AspectOf(rig.Centre);
        rig.Centre.SizeMode = ScreenSizeMode.Measured;
        rig.Centre.WidthCm = screen / 10;
        rig.Centre.HeightCm = screen / 10 / aspect;
        rig.DistanceCm = dist / 10;
        rig.Triple = (Get("NumMonitors") ?? 1) >= 3;
        if (Get("MonitorWidth") is { } monitor) rig.BezelMm = Math.Max(0, monitor - screen);
        if (Get("ScreenAngles") is { } angle) { rig.AutoAngle = false; rig.SideAngleDeg = angle; }
        rig.Centre.Curved = Get("MonitorType") == 1;
        if (Get("RadiusOfCurvature") is { } radius) rig.Centre.RadiusMm = radius;
        rig.MixedSides = false;
        return rig;
    }
}

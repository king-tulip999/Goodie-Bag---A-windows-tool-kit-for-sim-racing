using System.Runtime.InteropServices;
using GoodieBag.Config;
using static GoodieBag.Display.Ccd;

namespace GoodieBag.Display;

internal sealed record DisplayMode(int Width, int Height, int Hz);

internal sealed class MonitorInfo
{
    public required string Key { get; init; }

    public required string HardwareName { get; init; }

    public string Name => CustomName?.Invoke(Key) is { Length: > 0 } custom ? custom : HardwareName;

    internal static Func<string, string?>? CustomName;
    public string? GdiName { get; init; }

    public int Number { get; init; }

    public bool Active { get; init; }
    public bool Primary { get; init; }

    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public int Hz { get; init; }

    public int Rotation { get; init; }

    public string Connector { get; init; } = "";
    public bool HdrSupported { get; init; }
    public bool HdrOn { get; init; }

    public IReadOnlyList<DisplayMode> Modes { get; init; } = Array.Empty<DisplayMode>();

    internal LUID Adapter { get; init; }
    internal uint TargetId { get; init; }

    public (int W, int H) Landscape => (Math.Max(Width, Height), Math.Min(Width, Height));

    public string Label => Number > 0 ? $"{Number}" : "–";
}

internal sealed class MonitorEdit
{
    public required string Key { get; init; }
    public int X { get; set; }
    public int Y { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }
    public int Hz { get; set; }
    public int Rotation { get; set; }
    public bool Primary { get; set; }

    public bool Portrait => Rotation is 90 or 270;
    public int DesktopWidth => Portrait ? Height : Width;
    public int DesktopHeight => Portrait ? Width : Height;

    public MonitorEdit Copy() => (MonitorEdit)MemberwiseClone();
}

internal sealed record SnapshotMonitor(string Key, string Name, int X, int Y, int Width, int Height, int Hz, int Rotation, bool Primary);

internal sealed class DisplayService : IDisposable
{
    private readonly bool _demo;
    private List<MonitorInfo>? _demoMonitors;

    public DisplayService(bool demo)
    {
        _demo = demo;
        _ui = SynchronizationContext.Current;
        if (!demo)
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Invalidate();

    public void Dispose()
    {
        if (!_demo)
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    public event Action? Changed;

    private readonly SynchronizationContext? _ui;

    private List<MonitorInfo>? _cache;
    private long _cacheAt;

    private readonly Dictionary<(string Key, string Gdi), List<DisplayMode>> _modes = new();

    private List<DisplayMode> ModesOf(string key, string gdiName)
    {
        lock (_modes)
        {
            if (_modes.TryGetValue((key, gdiName), out var known))
                return known;
        }
        var modes = EnumerateModes(gdiName);
        lock (_modes) _modes[(key, gdiName)] = modes;
        return modes;
    }

    private void ForgetModes()
    {
        lock (_modes) _modes.Clear();
    }

    private void Invalidate()
    {
        _cache = null;
        ForgetModes();
    }

    public List<MonitorInfo> GetMonitors()
    {
        if (_demo)
            return _demoMonitors ??= DemoMonitors();
        var cache = _cache;
        if (cache is not null && Environment.TickCount64 - _cacheAt < 1500)
            return cache;
        cache = ReadMonitors();
        _cache = cache;
        _cacheAt = Environment.TickCount64;
        return cache;
    }

    private List<MonitorInfo> ReadMonitors()
    {
        var result = new List<MonitorInfo>();
        var seen = new HashSet<(LUID, uint)>();

        var (active, _) = DisplayManager.Query(QDC_ONLY_ACTIVE_PATHS);
        foreach (var path in active)
        {
            var t = path.TargetInfo;
            if (!seen.Add((t.AdapterId, t.Id)))
                continue;

            var name = DisplayManager.GetTargetName(t.AdapterId, t.Id);
            string? gdi = SourceGdiName(path.SourceInfo.AdapterId, path.SourceInfo.Id);
            var current = gdi is null ? null : CurrentMode(gdi);
            var (hdrSupported, hdrOn) = HdrManager.GetState(t.AdapterId, t.Id);
            string key = KeyFor(name, t);

            int width = (int)(current?.dmPelsWidth ?? 0), height = (int)(current?.dmPelsHeight ?? 0);
            int x = current?.dmPositionX ?? 0, y = current?.dmPositionY ?? 0;
            result.Add(new MonitorInfo
            {
                Key = key,
                HardwareName = FriendlyName(name, t),
                GdiName = gdi,
                Number = DisplayNumber(gdi),
                Active = true,
                Primary = current is not null && x == 0 && y == 0,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Hz = (int)(current?.dmDisplayFrequency ?? (uint)RoundHz(t.RefreshRate)),
                Rotation = (int)(current?.dmDisplayOrientation ?? 0) * 90,
                Connector = ConnectorName(t.OutputTechnology),
                HdrSupported = hdrSupported,
                HdrOn = hdrOn,
                Modes = gdi is null ? Array.Empty<DisplayMode>() : ModesOf(key, gdi),
                Adapter = t.AdapterId,
                TargetId = t.Id,
            });
        }

        var (all, _) = DisplayManager.Query(QDC_ALL_PATHS);
        foreach (var path in all)
        {
            var t = path.TargetInfo;
            if (t.TargetAvailable == 0 || !seen.Add((t.AdapterId, t.Id)))
                continue;
            var name = DisplayManager.GetTargetName(t.AdapterId, t.Id);
            if (name is null || string.IsNullOrEmpty(name.Value.MonitorDevicePath))
                continue;
            result.Add(new MonitorInfo
            {
                Key = KeyFor(name, t),
                HardwareName = FriendlyName(name, t),
                Active = false,
                Connector = ConnectorName(t.OutputTechnology),
                Adapter = t.AdapterId,
                TargetId = t.Id,
            });
        }

        return result.OrderByDescending(m => m.Active).ThenBy(m => m.X).ThenBy(m => m.Y).ToList();
    }

    public List<SnapshotMonitor> Describe(DisplaySnapshot snapshot)
    {
        if (_demo && snapshot.Paths.StartsWith("demo:", StringComparison.Ordinal))
            return DemoDescribe(snapshot);

        var list = new List<SnapshotMonitor>();
        PathInfo[] paths;
        ModeInfo[] modes;
        try
        {
            paths = DisplayManager.FromBase64<PathInfo>(snapshot.Paths);
            modes = DisplayManager.FromBase64<ModeInfo>(snapshot.Modes);
        }
        catch
        {
            return list;
        }

        for (int i = 0; i < paths.Length; i++)
        {
            var p = paths[i];
            uint si = p.SourceInfo.ModeInfoIdx;
            if (si >= modes.Length || modes[si].InfoType != MODE_INFO_TYPE_SOURCE)
                continue;
            var sm = modes[si].SourceMode;
            string key = i < snapshot.TargetDevicePaths.Count ? snapshot.TargetDevicePaths[i] : "";
            string name = i < snapshot.Summary.Count ? snapshot.Summary[i].Split(" · ")[0] : $"Display {i + 1}";
            int rotation = p.TargetInfo.Rotation switch { 2 => 90, 3 => 180, 4 => 270, _ => 0 };
            list.Add(new SnapshotMonitor(key, name, sm.Position.X, sm.Position.Y, (int)sm.Width, (int)sm.Height,
                RoundHz(p.TargetInfo.RefreshRate), rotation, sm.Position.X == 0 && sm.Position.Y == 0));
        }
        return list;
    }

    public void ApplyLayout(IReadOnlyList<MonitorEdit> edits)
    {
        if (edits.Count == 0)
            return;

        var primary = edits.FirstOrDefault(e => e.Primary) ?? edits[0];
        int dx = primary.X, dy = primary.Y;
        foreach (var e in edits)
        {
            e.X -= dx;
            e.Y -= dy;
            e.Primary = e == primary;
        }

        if (_demo)
        {
            _demoMonitors = GetMonitors().Select(m =>
            {
                var e = edits.FirstOrDefault(x => x.Key == m.Key);
                return e is null ? m : new MonitorInfo
                {
                    Key = m.Key, HardwareName = m.HardwareName, GdiName = m.GdiName, Number = m.Number, Active = m.Active, Connector = m.Connector,
                    Modes = m.Modes, HdrSupported = m.HdrSupported, HdrOn = m.HdrOn,
                    X = e.X, Y = e.Y, Width = e.DesktopWidth, Height = e.DesktopHeight, Hz = e.Hz, Rotation = e.Rotation, Primary = e.Primary,
                };
            }).ToList();
            NotifyChanged();
            return;
        }

        var monitors = GetMonitors().Where(m => m.Active && m.GdiName is not null).ToDictionary(m => m.Key);
        var problems = new List<string>();

        var changes = new List<(MonitorEdit Edit, MonitorInfo Monitor, Gdi.DEVMODE Mode)>();
        foreach (var group in edits.Where(e => monitors.ContainsKey(e.Key)).GroupBy(e => monitors[e.Key].GdiName!, StringComparer.OrdinalIgnoreCase))
        {
            var e = group.FirstOrDefault(x => IsChanged(x, monitors[x.Key])) ?? group.First();
            var m = monitors[e.Key];
            var dm = CurrentMode(m.GdiName!) ?? Gdi.DEVMODE.Create();
            dm.dmPelsWidth = (uint)e.DesktopWidth;
            dm.dmPelsHeight = (uint)e.DesktopHeight;
            dm.dmDisplayFrequency = (uint)e.Hz;
            dm.dmDisplayOrientation = (uint)(e.Rotation / 90);
            dm.dmPositionX = e.X;
            dm.dmPositionY = e.Y;
            dm.dmFields = Gdi.DM_PELSWIDTH | Gdi.DM_PELSHEIGHT | Gdi.DM_DISPLAYFREQUENCY | Gdi.DM_DISPLAYORIENTATION | Gdi.DM_POSITION;
            changes.Add((e, m, dm));
        }

        foreach (var (_, m, mode) in changes)
        {
            var dm = mode;
            int r = Gdi.ChangeDisplaySettingsEx(m.GdiName!, ref dm, IntPtr.Zero, Gdi.CDS_TEST, IntPtr.Zero);
            if (r < 0)
                problems.Add($"{m.Name}: {Gdi.Describe(r)}");
        }
        if (problems.Count > 0)
            throw new LayoutApplyException(0, "Nothing was changed. " + string.Join("\n", problems), restored: true);

        foreach (var (e, m, mode) in changes.OrderByDescending(c => c.Edit.Primary))
        {
            var dm = mode;
            uint flags = Gdi.CDS_UPDATEREGISTRY | Gdi.CDS_NORESET | (e.Primary ? Gdi.CDS_SET_PRIMARY : 0);
            int r = Gdi.ChangeDisplaySettingsEx(m.GdiName!, ref dm, IntPtr.Zero, flags, IntPtr.Zero);
            if (r < 0)
                problems.Add($"{m.Name}: {Gdi.Describe(r)}");
        }

        int apply = Gdi.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (apply < 0)
            problems.Add(Gdi.Describe(apply));

        NotifyChanged();
        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join("\n", problems));
    }

    private static bool IsChanged(MonitorEdit e, MonitorInfo m) =>
        e.DesktopWidth != m.Width || e.DesktopHeight != m.Height || e.Hz != m.Hz || e.Rotation != m.Rotation || e.Primary != m.Primary;

    public void SetEnabled(MonitorInfo monitor, bool enabled)
    {
        if (_demo)
        {
            var list = GetMonitors().ToList();
            int i = list.FindIndex(m => m.Key == monitor.Key);
            if (i < 0) return;
            var m = list[i];
            int right = list.Where(x => x.Active).Select(x => x.X + x.Width).DefaultIfEmpty(0).Max();
            list[i] = new MonitorInfo
            {
                Key = m.Key, HardwareName = m.HardwareName, Connector = m.Connector, Active = enabled, Number = enabled ? 4 : 0,
                X = right, Y = 0, Width = enabled ? 3840 : 0, Height = enabled ? 2160 : 0, Hz = enabled ? 60 : 0,
                Modes = enabled ? new[] { new DisplayMode(3840, 2160, 60), new DisplayMode(3840, 2160, 120), new DisplayMode(1920, 1080, 60) } : Array.Empty<DisplayMode>(),
                HdrSupported = true,
            };
            _demoMonitors = list;
            NotifyChanged();
            return;
        }

        var (active, activeModes) = DisplayManager.Query(QDC_ONLY_ACTIVE_PATHS);
        List<PathInfo> paths;

        if (!enabled)
        {
            paths = active.Where(p => !(p.TargetInfo.AdapterId.Equals(monitor.Adapter) && p.TargetInfo.Id == monitor.TargetId)).ToList();
            if (paths.Count == 0)
                throw new InvalidOperationException("You can't turn off your only active display.");
        }
        else
        {
            var (all, _) = DisplayManager.Query(QDC_ALL_PATHS);
            var usedSources = active.Select(p => (p.SourceInfo.AdapterId, p.SourceInfo.Id)).ToHashSet();
            int index = Array.FindIndex(all, p =>
                p.TargetInfo.AdapterId.Equals(monitor.Adapter) && p.TargetInfo.Id == monitor.TargetId &&
                !usedSources.Contains((p.SourceInfo.AdapterId, p.SourceInfo.Id)));
            if (index < 0)
                throw new InvalidOperationException("Windows has no free output for that monitor (the GPU may already be driving its maximum number of displays).");

            var candidate = all[index];
            candidate.Flags |= DISPLAYCONFIG_PATH_ACTIVE;
            candidate.SourceInfo.ModeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            candidate.TargetInfo.ModeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            paths = active.ToList();
            paths.Add(candidate);
        }

        const uint flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE;
        var pathArray = paths.ToArray();
        int err = SetDisplayConfig((uint)pathArray.Length, pathArray, (uint)activeModes.Length, activeModes, flags);
        if (err != ERROR_SUCCESS)
        {
            for (int i = 0; i < pathArray.Length; i++)
            {
                pathArray[i].SourceInfo.ModeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
                pathArray[i].TargetInfo.ModeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            }
            err = SetDisplayConfig((uint)pathArray.Length, pathArray, 0, null,
                SDC_APPLY | SDC_TOPOLOGY_SUPPLIED | SDC_ALLOW_PATH_ORDER_CHANGES);
        }

        NotifyChanged();
        if (err != ERROR_SUCCESS)
            throw new InvalidOperationException($"Windows didn't accept the change (error {err}).");
    }

    public void SetHdr(MonitorInfo monitor, bool on)
    {
        if (_demo)
            return;
        int err = HdrManager.SetState(monitor.Adapter, monitor.TargetId, on);
        NotifyChanged();
        if (err != ERROR_SUCCESS)
            throw new InvalidOperationException($"Windows didn't change HDR (error {err}).");
    }

    public DisplaySnapshot? CaptureForUndo()
    {
        if (_demo)
            return new DisplaySnapshot { Paths = "demo:undo" };
        try { return DisplayManager.Capture(); }
        catch { return null; }
    }

    public void Restore(DisplaySnapshot snapshot)
    {
        if (_demo)
            return;
        DisplayManager.Apply(snapshot);
        NotifyChanged();
    }

    public DisplaySnapshot CaptureForMode()
    {
        if (_demo)
        {
            var active = GetMonitors().Where(m => m.Active).ToList();
            return new DisplaySnapshot
            {
                Paths = "demo:" + string.Join(";", active.Select(m => $"{m.Key},{m.X},{m.Y},{m.Width},{m.Height},{m.Hz},{m.Rotation}")),
                Summary = active.Select(m => $"{m.Name} · {m.Width}×{m.Height} · {m.Hz} Hz").ToList(),
                TargetDevicePaths = active.Select(m => m.Key).ToList(),
            };
        }
        return DisplayManager.Capture();
    }

    public void NotifyChanged()
    {
        Invalidate();
        if (_ui is null || SynchronizationContext.Current == _ui) Changed?.Invoke();
        else _ui.Post(_ => Changed?.Invoke(), null);
    }

    private static Gdi.DEVMODE? CurrentMode(string gdiName)
    {
        var dm = Gdi.DEVMODE.Create();
        return Gdi.EnumDisplaySettingsEx(gdiName, Gdi.ENUM_CURRENT_SETTINGS, ref dm, 0) ? dm : null;
    }

    private static List<DisplayMode> EnumerateModes(string gdiName)
    {
        var set = new HashSet<DisplayMode>();
        for (int i = 0; i < 2000; i++)
        {
            var dm = Gdi.DEVMODE.Create();
            if (!Gdi.EnumDisplaySettingsEx(gdiName, i, ref dm, 0))
                break;
            if (dm.dmBitsPerPel < 24 || dm.dmPelsWidth == 0 || dm.dmDisplayFrequency <= 1)
                continue;
            int w = (int)Math.Max(dm.dmPelsWidth, dm.dmPelsHeight), h = (int)Math.Min(dm.dmPelsWidth, dm.dmPelsHeight);
            set.Add(new DisplayMode(w, h, (int)dm.dmDisplayFrequency));
        }
        return set.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.Hz).ToList();
    }

    private static string? SourceGdiName(LUID adapter, uint sourceId)
    {
        var request = new SourceDeviceName();
        request.Header.Type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        request.Header.Size = (uint)Marshal.SizeOf<SourceDeviceName>();
        request.Header.AdapterId = adapter;
        request.Header.Id = sourceId;
        return DisplayConfigGetDeviceInfo(ref request) == ERROR_SUCCESS ? request.ViewGdiDeviceName : null;
    }

    private static int DisplayNumber(string? gdi)
    {
        if (gdi is null)
            return 0;
        int i = gdi.Length;
        while (i > 0 && char.IsDigit(gdi[i - 1]))
            i--;
        return int.TryParse(gdi[i..], out int n) ? n : 0;
    }

    private static string KeyFor(TargetDeviceName? name, PathTargetInfo t) =>
        name is { } n && !string.IsNullOrEmpty(n.MonitorDevicePath) ? n.MonitorDevicePath : $"{t.AdapterId}/{t.Id}";

    private static string FriendlyName(TargetDeviceName? name, PathTargetInfo t)
    {
        if (name is { } n && !string.IsNullOrWhiteSpace(n.MonitorFriendlyDeviceName))
            return n.MonitorFriendlyDeviceName;
        return t.OutputTechnology == 0x80000000 ? "Built-in display" : "Display";
    }

    private static int RoundHz(Rational r) => r.Denominator == 0 ? 0 : (int)Math.Round((double)r.Numerator / r.Denominator);

    private static string ConnectorName(uint tech) => tech switch
    {
        0 => "VGA",
        4 => "DVI",
        5 => "HDMI",
        6 or 0x80000000 => "Built-in",
        10 => "DisplayPort",
        11 => "Built-in (eDP)",
        15 => "Miracast",
        18 => "USB-C",
        _ => "",
    };

    private static List<MonitorInfo> DemoMonitors()
    {
        var modes = new List<DisplayMode>
        {
            new(2560, 1440, 165), new(2560, 1440, 144), new(2560, 1440, 120), new(2560, 1440, 60),
            new(1920, 1080, 165), new(1920, 1080, 144), new(1920, 1080, 60), new(1600, 900, 60), new(1280, 720, 60),
        };
        return new List<MonitorInfo>
        {
            new() { Key = "demo-left", HardwareName = "Dell S2721DGF", Number = 2, Active = true, X = -2560, Y = 0, Width = 2560, Height = 1440, Hz = 165, Connector = "DisplayPort", Modes = modes, HdrSupported = true },
            new() { Key = "demo-center", HardwareName = "LG 27GP850", Number = 1, Active = true, Primary = true, X = 0, Y = 0, Width = 2560, Height = 1440, Hz = 165, Connector = "DisplayPort", Modes = modes, HdrSupported = true, HdrOn = false },
            new() { Key = "demo-right", HardwareName = "Dell S2721DGF", Number = 3, Active = true, X = 2560, Y = 0, Width = 2560, Height = 1440, Hz = 165, Connector = "DisplayPort", Modes = modes, HdrSupported = true },
            new() { Key = "demo-tv", HardwareName = "LG TV SSCR2", Active = false, Connector = "HDMI" },
        };
    }

    private List<SnapshotMonitor> DemoDescribe(DisplaySnapshot snapshot)
    {
        var names = (_demoMonitors ??= DemoMonitors()).ToDictionary(m => m.Key, m => m.Name);
        var list = new List<SnapshotMonitor>();
        foreach (var part in snapshot.Paths["demo:".Length..].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(',');
            int x = int.Parse(f[1]), y = int.Parse(f[2]);
            list.Add(new SnapshotMonitor(f[0], names.GetValueOrDefault(f[0], "Display"), x, y, int.Parse(f[3]), int.Parse(f[4]), int.Parse(f[5]), int.Parse(f[6]), x == 0 && y == 0));
        }
        return list;
    }
}

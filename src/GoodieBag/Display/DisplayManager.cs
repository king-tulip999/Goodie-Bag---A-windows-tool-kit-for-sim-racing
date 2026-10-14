using System.ComponentModel;
using System.Runtime.InteropServices;
using GoodieBag.Config;
using static GoodieBag.Display.Ccd;

namespace GoodieBag.Display;

internal static class DisplayManager
{
    public static DisplaySnapshot Capture()
    {
        var (paths, modes) = Query(QDC_ONLY_ACTIVE_PATHS);
        var snapshot = new DisplaySnapshot { Paths = ToBase64(paths), Modes = ToBase64(modes) };

        foreach (var path in paths)
        {
            var name = GetTargetName(path.TargetInfo.AdapterId, path.TargetInfo.Id);
            snapshot.TargetDevicePaths.Add(name?.MonitorDevicePath ?? "");
            snapshot.Summary.Add(Describe(path, modes, name));
        }
        return snapshot;
    }

    public static void Apply(DisplaySnapshot snapshot)
    {
        var paths = FromBase64<PathInfo>(snapshot.Paths);
        var modes = FromBase64<ModeInfo>(snapshot.Modes);
        if (paths.Length == 0)
            throw new InvalidOperationException("This mode has no saved display layout.");

        RemapToCurrentHardware(snapshot, paths, modes);

        (PathInfo[] Paths, ModeInfo[] Modes)? previous;
        try { previous = Query(QDC_ONLY_ACTIVE_PATHS); }
        catch { previous = null; }

        const uint fullFlags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES;
        int err = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, fullFlags);
        if (err == ERROR_SUCCESS)
            return;

        var topology = (PathInfo[])paths.Clone();
        for (int i = 0; i < topology.Length; i++)
        {
            topology[i].SourceInfo.ModeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            topology[i].TargetInfo.ModeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        }
        int topologyErr = SetDisplayConfig((uint)topology.Length, topology, 0, null,
            SDC_APPLY | SDC_TOPOLOGY_SUPPLIED | SDC_ALLOW_PATH_ORDER_CHANGES);

        err = SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, fullFlags);
        if (err == ERROR_SUCCESS)
            return;

        string message = $"Windows rejected the saved layout ({new Win32Exception(err).Message}). Try “Update to current” on this display profile.";
        if (topologyErr != ERROR_SUCCESS)
            throw new LayoutApplyException(err, message, restored: true);

        int restoreErr = previous is { } before
            ? SetDisplayConfig((uint)before.Paths.Length, before.Paths, (uint)before.Modes.Length, before.Modes, fullFlags)
            : -1;
        if (restoreErr == ERROR_SUCCESS)
            throw new LayoutApplyException(err, message + " The previous layout was restored.", restored: true);
        throw new LayoutApplyException(err, message + (previous is null
            ? " The previous layout couldn't be restored (it couldn't be read)."
            : $" Restoring the previous layout also failed ({new Win32Exception(restoreErr).Message})."), restored: false);
    }

    private static void RemapToCurrentHardware(DisplaySnapshot snapshot, PathInfo[] paths, ModeInfo[] modes)
    {
        var current = CurrentTargets();
        var luidMap = new Dictionary<LUID, LUID>();
        var targetMap = new Dictionary<(LUID, uint), uint>();
        var missing = new List<string>();

        for (int i = 0; i < paths.Length; i++)
        {
            string devicePath = i < snapshot.TargetDevicePaths.Count ? snapshot.TargetDevicePaths[i] : "";
            if (devicePath.Length == 0)
                continue;

            var target = paths[i].TargetInfo;
            if (current.TryGetValue(devicePath, out var now))
            {
                luidMap[target.AdapterId] = now.Adapter;
                targetMap[(target.AdapterId, target.Id)] = now.Id;
            }
            else
            {
                missing.Add(i < snapshot.Summary.Count ? snapshot.Summary[i] : devicePath);
            }
        }

        if (missing.Count > 0)
            throw new InvalidOperationException("These monitors aren't connected right now: " + string.Join("; ", missing));

        var distinctAdapters = luidMap.Values.Distinct().ToList();
        LUID MapLuid(LUID old) =>
            luidMap.TryGetValue(old, out var mapped) ? mapped
            : distinctAdapters.Count == 1 ? distinctAdapters[0]
            : old;

        for (int i = 0; i < paths.Length; i++)
        {
            ref var target = ref paths[i].TargetInfo;
            if (targetMap.TryGetValue((target.AdapterId, target.Id), out uint newId))
                target.Id = newId;
            target.AdapterId = MapLuid(target.AdapterId);

            ref var source = ref paths[i].SourceInfo;
            source.AdapterId = MapLuid(source.AdapterId);
        }

        for (int i = 0; i < modes.Length; i++)
        {
            ref var mode = ref modes[i];
            if (mode.InfoType == MODE_INFO_TYPE_TARGET && targetMap.TryGetValue((mode.AdapterId, mode.Id), out uint newId))
                mode.Id = newId;
            mode.AdapterId = MapLuid(mode.AdapterId);
        }
    }

    private static Dictionary<string, (LUID Adapter, uint Id)> CurrentTargets()
    {
        var (paths, _) = Query(QDC_ALL_PATHS);
        var seen = new HashSet<(LUID, uint)>();
        var result = new Dictionary<string, (LUID, uint)>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            var target = path.TargetInfo;
            if (target.TargetAvailable == 0 || !seen.Add((target.AdapterId, target.Id)))
                continue;

            var name = GetTargetName(target.AdapterId, target.Id);
            if (name is { } n && !string.IsNullOrEmpty(n.MonitorDevicePath))
                result.TryAdd(n.MonitorDevicePath, (target.AdapterId, target.Id));
        }
        return result;
    }

    internal static (PathInfo[] Paths, ModeInfo[] Modes) Query(uint flags)
    {
        while (true)
        {
            int err = GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if (err != ERROR_SUCCESS)
                throw new Win32Exception(err);

            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];
            err = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);

            if (err == ERROR_INSUFFICIENT_BUFFER)
                continue;
            if (err != ERROR_SUCCESS)
                throw new Win32Exception(err);

            Array.Resize(ref paths, (int)pathCount);
            Array.Resize(ref modes, (int)modeCount);
            return (paths, modes);
        }
    }

    internal static TargetDeviceName? GetTargetName(LUID adapter, uint targetId)
    {
        var request = new TargetDeviceName();
        request.Header.Type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        request.Header.Size = (uint)Marshal.SizeOf<TargetDeviceName>();
        request.Header.AdapterId = adapter;
        request.Header.Id = targetId;
        return DisplayConfigGetDeviceInfo(ref request) == ERROR_SUCCESS ? request : null;
    }

    private static string Describe(PathInfo path, ModeInfo[] modes, TargetDeviceName? name)
    {
        string label = string.IsNullOrWhiteSpace(name?.MonitorFriendlyDeviceName)
            ? $"Display {path.TargetInfo.Id}"
            : name!.Value.MonitorFriendlyDeviceName;

        var parts = new List<string> { label };
        bool primary = false;

        uint sourceIdx = path.SourceInfo.ModeInfoIdx;
        if (sourceIdx < modes.Length && modes[sourceIdx].InfoType == MODE_INFO_TYPE_SOURCE)
        {
            var source = modes[sourceIdx].SourceMode;
            parts.Add($"{source.Width}×{source.Height}");
            primary = source.Position.X == 0 && source.Position.Y == 0;
        }

        var refresh = path.TargetInfo.RefreshRate;
        if (refresh.Denominator != 0)
            parts.Add($"{Math.Round((double)refresh.Numerator / refresh.Denominator)} Hz");

        if (primary)
            parts.Add("primary");

        return string.Join(" · ", parts);
    }

    private static string ToBase64<T>(T[] items) where T : unmanaged =>
        Convert.ToBase64String(MemoryMarshal.AsBytes(items.AsSpan()));

    internal static T[] FromBase64<T>(string data) where T : unmanaged
    {
        if (string.IsNullOrEmpty(data))
            return Array.Empty<T>();

        var bytes = Convert.FromBase64String(data);
        if (bytes.Length % Marshal.SizeOf<T>() != 0)
            throw new InvalidOperationException("Saved display data is corrupt. Re-save this mode.");
        return MemoryMarshal.Cast<byte, T>(bytes).ToArray();
    }
}

internal sealed class LayoutApplyException : Win32Exception
{
    public LayoutApplyException(int error, string message, bool restored) : base(error, message) => Restored = restored;

    public bool Restored { get; }
}

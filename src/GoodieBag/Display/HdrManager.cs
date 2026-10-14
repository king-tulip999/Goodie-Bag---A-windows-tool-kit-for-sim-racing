using System.Runtime.InteropServices;
using static GoodieBag.Display.Ccd;

namespace GoodieBag.Display;

internal static class HdrManager
{
    public sealed record Result(int Changed, int Unsupported, List<string> Errors);

    public static Result Apply(bool enable)
    {
        int changed = 0, unsupported = 0;
        var errors = new List<string>();
        var (paths, _) = DisplayManager.Query(QDC_ONLY_ACTIVE_PATHS);
        var seen = new HashSet<(LUID, uint)>();

        foreach (var path in paths)
        {
            var adapter = path.TargetInfo.AdapterId;
            uint id = path.TargetInfo.Id;
            if (!seen.Add((adapter, id)))
                continue;

            var (supported, enabled) = GetState(adapter, id);
            if (!supported)
            {
                if (enable) unsupported++;
                continue;
            }
            if (enabled == enable)
                continue;

            int err = SetState(adapter, id, enable);
            if (err == ERROR_SUCCESS)
                changed++;
            else
                errors.Add($"{MonitorName(adapter, id)}: error {err}");
        }

        return new Result(changed, unsupported, errors);
    }

    internal static (bool Supported, bool Enabled) GetState(LUID adapter, uint id)
    {
        var info2 = new AdvancedColorInfo2();
        info2.Header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2, Marshal.SizeOf<AdvancedColorInfo2>(), adapter, id);
        if (DisplayConfigGetDeviceInfo(ref info2) == ERROR_SUCCESS)
            return ((info2.Value & (1u << 4)) != 0, (info2.Value & (1u << 5)) != 0);

        var info = new AdvancedColorInfo();
        info.Header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO, Marshal.SizeOf<AdvancedColorInfo>(), adapter, id);
        if (DisplayConfigGetDeviceInfo(ref info) == ERROR_SUCCESS)
            return ((info.Value & 1u) != 0, (info.Value & 2u) != 0);

        return (false, false);
    }

    internal static int SetState(LUID adapter, uint id, bool enable)
    {
        var packet = new SetAdvancedState { Value = enable ? 1u : 0u };
        packet.Header = Header(DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE, Marshal.SizeOf<SetAdvancedState>(), adapter, id);
        int err = DisplayConfigSetDeviceInfo(ref packet);
        if (err == ERROR_SUCCESS)
            return err;

        packet.Header.Type = DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE;
        return DisplayConfigSetDeviceInfo(ref packet);
    }

    private static string MonitorName(LUID adapter, uint id) =>
        DisplayManager.GetTargetName(adapter, id) is { } n && !string.IsNullOrWhiteSpace(n.MonitorFriendlyDeviceName)
            ? n.MonitorFriendlyDeviceName
            : $"Display {id}";

    private static DeviceInfoHeader Header(uint type, int size, LUID adapter, uint id) => new()
    {
        Type = type,
        Size = (uint)size,
        AdapterId = adapter,
        Id = id,
    };
}

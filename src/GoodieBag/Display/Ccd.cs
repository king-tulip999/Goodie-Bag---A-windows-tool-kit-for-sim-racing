using System.Runtime.InteropServices;

namespace GoodieBag.Display;

internal static class Ccd
{
    public const uint QDC_ALL_PATHS = 0x00000001;
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    public const uint SDC_TOPOLOGY_SUPPLIED = 0x00000010;
    public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    public const uint SDC_APPLY = 0x00000080;
    public const uint SDC_SAVE_TO_DATABASE = 0x00000200;
    public const uint SDC_ALLOW_CHANGES = 0x00000400;
    public const uint SDC_ALLOW_PATH_ORDER_CHANGES = 0x00002000;

    public const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF;

    public const uint MODE_INFO_TYPE_SOURCE = 1;
    public const uint MODE_INFO_TYPE_TARGET = 2;

    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    public const int ERROR_SUCCESS = 0;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags,
        ref uint numPathArrayElements, [Out] PathInfo[] pathArray,
        ref uint numModeInfoArrayElements, [Out] ModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements, [In] PathInfo[]? pathArray,
        uint numModeInfoArrayElements, [In] ModeInfo[]? modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName requestPacket);

    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    public const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName requestPacket);

    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    public const uint DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2 = 15;
    public const uint DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE = 16;

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo2 requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref SetAdvancedState setPacket);
}

[StructLayout(LayoutKind.Sequential)]
internal struct AdvancedColorInfo
{
    public DeviceInfoHeader Header;
    public uint Value;
    public uint ColorEncoding;
    public uint BitsPerColorChannel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AdvancedColorInfo2
{
    public DeviceInfoHeader Header;
    public uint Value;
    public uint ColorEncoding;
    public uint BitsPerColorChannel;
    public uint ActiveColorMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SetAdvancedState
{
    public DeviceInfoHeader Header;
    public uint Value;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LUID : IEquatable<LUID>
{
    public uint LowPart;
    public int HighPart;

    public bool Equals(LUID other) => LowPart == other.LowPart && HighPart == other.HighPart;
    public override bool Equals(object? obj) => obj is LUID other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(LowPart, HighPart);
    public static bool operator ==(LUID a, LUID b) => a.Equals(b);
    public static bool operator !=(LUID a, LUID b) => !a.Equals(b);
    public override string ToString() => $"{HighPart:X8}:{LowPart:X8}";
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rational
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PathSourceInfo
{
    public LUID AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PathTargetInfo
{
    public LUID AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint OutputTechnology;
    public uint Rotation;
    public uint Scaling;
    public Rational RefreshRate;
    public uint ScanLineOrdering;
    public int TargetAvailable;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PathInfo
{
    public PathSourceInfo SourceInfo;
    public PathTargetInfo TargetInfo;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Region2D
{
    public uint Cx;
    public uint Cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct VideoSignalInfo
{
    public ulong PixelRate;
    public Rational HSyncFreq;
    public Rational VSyncFreq;
    public Region2D ActiveSize;
    public Region2D TotalSize;
    public uint VideoStandard;
    public uint ScanLineOrdering;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointL
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SourceMode
{
    public uint Width;
    public uint Height;
    public uint PixelFormat;
    public PointL Position;
}

[StructLayout(LayoutKind.Explicit, Size = 64)]
internal struct ModeInfo
{
    [FieldOffset(0)] public uint InfoType;
    [FieldOffset(4)] public uint Id;
    [FieldOffset(8)] public LUID AdapterId;
    [FieldOffset(16)] public VideoSignalInfo TargetVideoSignalInfo;
    [FieldOffset(16)] public SourceMode SourceMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DeviceInfoHeader
{
    public uint Type;
    public uint Size;
    public LUID AdapterId;
    public uint Id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct TargetDeviceName
{
    public DeviceInfoHeader Header;
    public uint Flags;
    public uint OutputTechnology;
    public ushort EdidManufactureId;
    public ushort EdidProductCodeId;
    public uint ConnectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string MonitorFriendlyDeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string MonitorDevicePath;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SourceDeviceName
{
    public DeviceInfoHeader Header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string ViewGdiDeviceName;
}

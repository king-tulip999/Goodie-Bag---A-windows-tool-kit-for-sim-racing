using System.Runtime.InteropServices;

namespace GoodieBag.Display;

internal static class Gdi
{
    public const int ENUM_CURRENT_SETTINGS = -1;

    public const uint DM_POSITION = 0x00000020;
    public const uint DM_DISPLAYORIENTATION = 0x00000080;
    public const uint DM_PELSWIDTH = 0x00080000;
    public const uint DM_PELSHEIGHT = 0x00100000;
    public const uint DM_DISPLAYFREQUENCY = 0x00400000;

    public const uint CDS_UPDATEREGISTRY = 0x00000001;
    public const uint CDS_TEST = 0x00000002;
    public const uint CDS_SET_PRIMARY = 0x00000010;
    public const uint CDS_NORESET = 0x10000000;

    public const int DISP_CHANGE_SUCCESSFUL = 0;
    public const int DISP_CHANGE_RESTART = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;

        public static DEVMODE Create() => new()
        {
            dmDeviceName = "",
            dmFormName = "",
            dmSize = (ushort)Marshal.SizeOf<DEVMODE>(),
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettingsEx(string deviceName, int modeNum, ref DEVMODE devMode, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ChangeDisplaySettingsEx(string? deviceName, IntPtr devMode, IntPtr hwnd, uint flags, IntPtr lParam);

    public static string Describe(int code) => code switch
    {
        DISP_CHANGE_SUCCESSFUL => "OK",
        DISP_CHANGE_RESTART => "Windows needs a restart for this change",
        -1 => "the display driver rejected the mode",
        -2 => "that mode isn't supported by the monitor",
        -3 => "Windows couldn't save the setting",
        -4 => "invalid settings",
        -5 => "invalid settings",
        -6 => "the display adapter didn't accept the change",
        _ => $"error {code}",
    };
}

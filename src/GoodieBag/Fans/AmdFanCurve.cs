using System.Runtime.InteropServices;
using System.Text;

namespace GoodieBag.Fans;

internal sealed class AmdFanCurve : IDisposable
{
    private const int ZeroRpm = 15;
    private const int CurveFirst = 19;
    private const int CurveLast = 28;
    private const int PowerMode = 36;
    private const int CapZeroRpm = 1 << 9;
    private const int CapFanCurve = 1 << 13;
    private const int CapPowerMode = 1 << 16;
    private const int HeaderCount = 77;

    private readonly int _adapter;
    private IntPtr _context;
    private int _caps;
    private int[] _min = [], _max = [], _default = [];
    private int? _powerModeBefore;

    public int MinPercent { get; private set; }
    public int MaxPercent { get; private set; } = 100;

    private AmdFanCurve(int adapter) => _adapter = adapter;

    public static AmdFanCurve? TryOpen(int adapterIndex, out string why)
    {
        var curve = new AmdFanCurve(adapterIndex);
        try
        {
            if (ADL2_Main_Control_Create(Alloc, 1, out curve._context) != 0) { why = "AMD driver library didn't start"; return null; }
            if (ADL2_Overdrive_Caps(curve._context, adapterIndex, out int supported, out _, out int version) != 0 || supported == 0 || version < 8)
            {
                why = $"Overdrive {version} (not 8), supported {supported}";
                curve.Dispose();
                return null;
            }
            if (!curve.ReadInit() || (curve._caps & CapFanCurve) == 0 || curve._min.Length <= CurveLast)
            {
                why = $"no fan curve in Overdrive 8 (caps 0x{curve._caps:X}, {curve._min.Length} settings, {curve._initNote})\n" + curve.Describe();
                curve.Dispose();
                return null;
            }
            curve.MinPercent = curve._min[CurveFirst + 1];
            curve.MaxPercent = curve._max[CurveFirst + 1];
            why = "";
            return curve;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            why = "AMD driver library: " + ex.Message;
            curve.Dispose();
            return null;
        }
    }

    public string? Set(int percent)
    {
        var current = ReadCurrent();
        if (current is null) return "the driver didn't report its fan settings";
        var table = new Entry[_min.Length];
        bool ascending = true;
        for (int i = CurveFirst; i <= CurveLast; i += 2)
            if (i > CurveFirst && current[i] <= current[i - 2]) ascending = false;
        for (int i = CurveFirst; i <= CurveLast; i++)
        {
            bool speed = (i - CurveFirst) % 2 == 1;
            int value = speed ? Math.Clamp(percent, _min[i], _max[i]) : ascending ? current[i] : _default[i];
            table[i] = new Entry(value, Requested: true, Reset: false);
        }
        if ((_caps & CapZeroRpm) != 0)
            table[ZeroRpm] = new Entry(percent <= 0 ? 1 : 0, true, false);

        int rc = Apply(table);
        if (rc < 0 && (_caps & CapPowerMode) != 0 && _powerModeBefore is null && !_manualDidntHelp && current.Length > PowerMode)
        {
            var mode = new Entry[_min.Length];
            mode[PowerMode] = new Entry(3, true, false);
            if (Apply(mode) >= 0)
            {
                _powerModeBefore = current[PowerMode];
                rc = Apply(table);
                if (rc < 0)
                {
                    mode[PowerMode] = new Entry(current[PowerMode], true, false);
                    if (Apply(mode) >= 0) _powerModeBefore = null;
                    _manualDidntHelp = true;
                }
            }
        }
        return rc >= 0 ? null : $"the driver refused the fan curve (code {rc})";
    }

    public string? Restore()
    {
        var table = new Entry[_min.Length];
        for (int i = CurveFirst; i <= CurveLast; i++)
            table[i] = new Entry(_default[i], true, true);
        if ((_caps & CapZeroRpm) != 0)
            table[ZeroRpm] = new Entry(_default[ZeroRpm], true, true);
        int rc = Apply(table), modeRc = 0;
        if (_powerModeBefore is { } before)
        {
            var mode = new Entry[_min.Length];
            mode[PowerMode] = new Entry(before, true, false);
            if ((modeRc = Apply(mode)) >= 0) _powerModeBefore = null;
        }
        return rc < 0 ? $"the driver refused the default fan curve (code {rc})"
            : modeRc < 0 ? $"the driver kept manual tuning on (code {modeRc})"
            : null;
    }

    public string Describe()
    {
        var current = ReadCurrent();
        var sb = new StringBuilder($"  adapter {_adapter}: caps 0x{_caps:X}, {_min.Length} settings, power mode changed: {_powerModeBefore is not null}\n");
        foreach (int i in new[] { 10, 11, 12, ZeroRpm, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 35, PowerMode })
            if (i < _min.Length)
                sb.AppendLine($"  [{i}] min {_min[i]} max {_max[i]} default {_default[i]} current {(current is not null && i < current.Length ? current[i] : -1)}");
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_context == IntPtr.Zero) return;
        try { ADL2_Main_Control_Destroy(_context); } catch { }
        _context = IntPtr.Zero;
    }

    private readonly record struct Entry(int Value, bool Requested, bool Reset);

    private bool _manualDidntHelp;

    private string _initNote = "";

    private bool ReadInit()
    {
        int rc = ADL2_Overdrive8_Init_SettingX2_Get(_context, _adapter, out _caps, out int count, out IntPtr list);
        _initNote = $"X2 code {rc}";
        if (rc != 0 || list == IntPtr.Zero)
        {
            IntPtr fixedTable = Marshal.AllocHGlobal(8 + HeaderCount * 16);
            try
            {
                for (int i = 0; i < 8 + HeaderCount * 16; i += 4) Marshal.WriteInt32(fixedTable, i, 0);
                Marshal.WriteInt32(fixedTable, 0, HeaderCount);
                rc = ADL2_Overdrive8_Init_Setting_Get(_context, _adapter, fixedTable);
                _initNote += $", fixed-size code {rc}";
                if (rc != 0) return false;
                _caps = Marshal.ReadInt32(fixedTable, 4);
                count = HeaderCount;
                _min = new int[count]; _max = new int[count]; _default = new int[count];
                for (int i = 0; i < count; i++)
                {
                    _min[i] = Marshal.ReadInt32(fixedTable, 8 + i * 16 + 4);
                    _max[i] = Marshal.ReadInt32(fixedTable, 8 + i * 16 + 8);
                    _default[i] = Marshal.ReadInt32(fixedTable, 8 + i * 16 + 12);
                }
                _fixedSize = true;
                return true;
            }
            finally { Marshal.FreeHGlobal(fixedTable); }
        }
        try
        {
            _min = new int[count]; _max = new int[count]; _default = new int[count];
            for (int i = 0; i < count; i++)
            {
                _min[i] = Marshal.ReadInt32(list, i * 16 + 4);
                _max[i] = Marshal.ReadInt32(list, i * 16 + 8);
                _default[i] = Marshal.ReadInt32(list, i * 16 + 12);
            }
            return true;
        }
        finally { Marshal.FreeHGlobal(list); }
    }

    private bool _fixedSize;

    private int[]? ReadCurrent()
    {
        if (_fixedSize)
        {
            IntPtr table = Marshal.AllocHGlobal(4 + HeaderCount * 4);
            try
            {
                for (int i = 0; i < 4 + HeaderCount * 4; i += 4) Marshal.WriteInt32(table, i, 0);
                Marshal.WriteInt32(table, 0, HeaderCount);
                if (ADL2_Overdrive8_Current_Setting_Get(_context, _adapter, table) != 0) return null;
                var fixedValues = new int[HeaderCount];
                Marshal.Copy(table + 4, fixedValues, 0, HeaderCount);
                return fixedValues;
            }
            finally { Marshal.FreeHGlobal(table); }
        }
        if (ADL2_Overdrive8_Current_SettingX2_Get(_context, _adapter, out int count, out IntPtr list) != 0 || list == IntPtr.Zero)
            return null;
        try
        {
            var values = new int[count];
            Marshal.Copy(list, values, 0, count);
            return values;
        }
        finally { Marshal.FreeHGlobal(list); }
    }

    private int Apply(Entry[] table)
    {
        int slots = Math.Max(table.Length, HeaderCount);
        IntPtr set = Marshal.AllocHGlobal(4 + slots * 12), current = Marshal.AllocHGlobal(4 + slots * 4);
        try
        {
            for (int i = 0; i < 4 + slots * 12; i += 4) Marshal.WriteInt32(set, i, 0);
            for (int i = 0; i < 4 + slots * 4; i += 4) Marshal.WriteInt32(current, i, 0);
            Marshal.WriteInt32(set, 0, table.Length);
            Marshal.WriteInt32(current, 0, table.Length);
            for (int i = 0; i < table.Length; i++)
            {
                if (!table[i].Requested) continue;
                Marshal.WriteInt32(set, 4 + i * 12, table[i].Value);
                Marshal.WriteInt32(set, 4 + i * 12 + 4, 1);
                Marshal.WriteInt32(set, 4 + i * 12 + 8, table[i].Reset ? 1 : 0);
            }
            return ADL2_Overdrive8_Setting_Set(_context, _adapter, set, current);
        }
        finally
        {
            Marshal.FreeHGlobal(set);
            Marshal.FreeHGlobal(current);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr AdlAlloc(int size);
    private static readonly AdlAlloc Alloc = size => Marshal.AllocHGlobal(size);

    private const string Dll = "atiadlxx.dll";
    [DllImport(Dll)] private static extern int ADL2_Main_Control_Create(AdlAlloc callback, int enumConnectedAdapters, out IntPtr context);
    [DllImport(Dll)] private static extern int ADL2_Main_Control_Destroy(IntPtr context);
    [DllImport(Dll)] private static extern int ADL2_Overdrive_Caps(IntPtr context, int adapter, out int supported, out int enabled, out int version);
    [DllImport(Dll)] private static extern int ADL2_Overdrive8_Init_SettingX2_Get(IntPtr context, int adapter, out int capabilities, out int count, out IntPtr list);
    [DllImport(Dll)] private static extern int ADL2_Overdrive8_Current_SettingX2_Get(IntPtr context, int adapter, out int count, out IntPtr list);
    [DllImport(Dll)] private static extern int ADL2_Overdrive8_Init_Setting_Get(IntPtr context, int adapter, IntPtr initSetting);
    [DllImport(Dll)] private static extern int ADL2_Overdrive8_Current_Setting_Get(IntPtr context, int adapter, IntPtr currentSetting);
    [DllImport(Dll)] private static extern int ADL2_Overdrive8_Setting_Set(IntPtr context, int adapter, IntPtr set, IntPtr current);
}

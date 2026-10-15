using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace GoodieBag.SimRacing.Telemetry;

internal enum IrsdkType { Char = 0, Bool = 1, Int = 2, BitField = 3, Float = 4, Double = 5 }

internal sealed record IrsdkVar(string Name, IrsdkType Type, int Offset, int Count, string Unit, string Description);

internal sealed class IrsdkMemory : IDisposable
{
    public const string MapName = @"Local\IRSDKMemMapFileName";
    public const string EventName = @"Local\IRSDKDataValidEvent";

    private const int HeaderSize = 112, VarHeaderSize = 144, MaxBuffers = 4;
    private static readonly Encoding Latin1 = Encoding.Latin1;

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly EventWaitHandle _event;
    private byte[] _buffer = Array.Empty<byte>();
    private int _varHeaderOffset = -1, _numVars = -1;

    private IrsdkMemory(MemoryMappedFile file, MemoryMappedViewAccessor view, EventWaitHandle ev)
    {
        _file = file;
        _view = view;
        _event = ev;
    }

    public static IrsdkMemory? TryOpen()
    {
        MemoryMappedFile? file = null;
        MemoryMappedViewAccessor? view = null;
        try
        {
            file = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            if (!EventWaitHandle.TryOpenExisting(EventName, out var ev))
                throw new FileNotFoundException("data event");
            return new IrsdkMemory(file, view, ev);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            view?.Dispose();
            file?.Dispose();
            return null;
        }
    }

    public bool WaitForData(int timeoutMs) => _event.WaitOne(timeoutMs);

    public bool Connected => (Int(4) & 1) != 0;

    public int TickRate => Int(8);
    public int SessionInfoUpdate => Int(12);

    public IReadOnlyDictionary<string, IrsdkVar>? VarsIfChanged()
    {
        int num = Int(24), offset = Int(28);
        if (num == _numVars && offset == _varHeaderOffset) return null;
        _numVars = num;
        _varHeaderOffset = offset;
        var vars = new Dictionary<string, IrsdkVar>(StringComparer.OrdinalIgnoreCase);
        if (num <= 0 || num > 10_000 || offset <= 0 || offset + (long)num * VarHeaderSize > _view.Capacity) return vars;
        var raw = new byte[VarHeaderSize];
        for (int i = 0; i < num; i++)
        {
            _view.ReadArray(offset + (long)i * VarHeaderSize, raw, 0, VarHeaderSize);
            var type = (IrsdkType)BitConverter.ToInt32(raw, 0);
            int at = BitConverter.ToInt32(raw, 4), count = BitConverter.ToInt32(raw, 8);
            string name = Str(raw, 16, 32), desc = Str(raw, 48, 64), unit = Str(raw, 112, 32);
            if (name.Length > 0 && count > 0 && Enum.IsDefined(type)) vars[name] = new IrsdkVar(name, type, at, count, unit, desc);
        }
        return vars;
    }

    private byte[] _info = Array.Empty<byte>();

    public string? SessionInfo()
    {
        int len = Int(16), offset = Int(20);
        if (len <= 0 || offset <= 0 || offset + (long)len > _view.Capacity) return null;
        if (_info.Length < len) _info = new byte[len];
        _view.ReadArray(offset, _info, 0, len);
        int end = Array.IndexOf(_info, (byte)0, 0, len);
        return Latin1.GetString(_info, 0, end < 0 ? len : end);
    }

    public int CopyLatest()
    {
        int numBuf = Math.Clamp(Int(32), 1, MaxBuffers), bufLen = Int(36);
        if (bufLen <= 0 || bufLen > 1 << 20) return -1;
        if (_buffer.Length != bufLen) _buffer = new byte[bufLen];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int latest = 0, latestTick = int.MinValue;
            for (int i = 0; i < numBuf; i++)
            {
                int tick = Int(48 + i * 16);
                if (tick > latestTick) { latestTick = tick; latest = i; }
            }
            int offset = Int(48 + latest * 16 + 4);
            if (offset <= 0 || offset + (long)bufLen > _view.Capacity) return -1;
            _view.ReadArray(offset, _buffer, 0, bufLen);
            if (Int(48 + latest * 16) == latestTick) return latestTick;
        }
        return -1;
    }

    public ReadOnlySpan<byte> Data => _buffer;

    private int Int(long at) => _view.ReadInt32(at);

    private static string Str(byte[] raw, int at, int max)
    {
        int end = Array.IndexOf(raw, (byte)0, at, max);
        return Latin1.GetString(raw, at, (end < 0 ? at + max : end) - at).Trim();
    }

    public void Dispose()
    {
        _event.Dispose();
        _view.Dispose();
        _file.Dispose();
    }

    internal static int HeaderBytes => HeaderSize;
    internal static int VarHeaderBytes => VarHeaderSize;

    public static double? Read(ReadOnlySpan<byte> data, IrsdkVar v, int index = 0)
    {
        if ((uint)index >= (uint)v.Count) return null;
        int size = v.Type switch { IrsdkType.Char or IrsdkType.Bool => 1, IrsdkType.Double => 8, _ => 4 };
        int at = v.Offset + index * size;
        if (at < 0 || at + size > data.Length) return null;
        var slice = data.Slice(at, size);
        return v.Type switch
        {
            IrsdkType.Char or IrsdkType.Bool => slice[0],
            IrsdkType.Int or IrsdkType.BitField => MemoryMarshal.Read<int>(slice),
            IrsdkType.Float => MemoryMarshal.Read<float>(slice) is var f && float.IsFinite(f) ? f : null,
            IrsdkType.Double => MemoryMarshal.Read<double>(slice) is var d && double.IsFinite(d) ? d : null,
            _ => null,
        };
    }
}

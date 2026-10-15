using System.IO;
using System.Text;

namespace GoodieBag.SimRacing.Telemetry;

internal sealed class IbtFile : IDisposable
{
    private const int HeaderSize = 112, DiskHeaderSize = 32, VarHeaderSize = 144;
    private readonly FileStream _stream;
    private readonly long _dataOffset;
    private readonly byte[] _record;

    public IReadOnlyDictionary<string, IrsdkVar> Vars { get; }
    public string SessionInfoYaml { get; }
    public int TickRate { get; }
    public int RecordCount { get; }
    public DateTime? StartedUtc { get; private init; }
    public double StartSessionTime { get; private init; }

    private IbtFile(FileStream stream, IReadOnlyDictionary<string, IrsdkVar> vars, string yaml, int tickRate, int records, long dataOffset, int recordLength)
    {
        _stream = stream;
        Vars = vars;
        SessionInfoYaml = yaml;
        TickRate = tickRate;
        RecordCount = records;
        _dataOffset = dataOffset;
        _record = new byte[recordLength];
    }

    public static IbtFile? Open(string path, out string? error)
    {
        error = null;
        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            var header = Read(stream, 0, HeaderSize + DiskHeaderSize);
            int tickRate = BitConverter.ToInt32(header, 8);
            int infoLength = BitConverter.ToInt32(header, 16), infoOffset = BitConverter.ToInt32(header, 20);
            int numVars = BitConverter.ToInt32(header, 24), varOffset = BitConverter.ToInt32(header, 28);
            int bufLength = BitConverter.ToInt32(header, 36), dataOffset = BitConverter.ToInt32(header, 52);
            int records = BitConverter.ToInt32(header, HeaderSize + 28);
            if (numVars is <= 0 or > 10_000 || varOffset <= 0 || bufLength is <= 0 or > 1 << 20 || dataOffset <= 0 || infoLength < 0)
            {
                error = "not an iRacing telemetry file (header out of range)";
                stream.Dispose();
                return null;
            }
            long available = (stream.Length - dataOffset) / bufLength;
            if (records <= 0 || records > available) records = (int)Math.Max(0, available);
            var varBytes = Read(stream, varOffset, numVars * VarHeaderSize);
            var vars = new Dictionary<string, IrsdkVar>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < numVars; i++)
            {
                int at = i * VarHeaderSize;
                var type = (IrsdkType)BitConverter.ToInt32(varBytes, at);
                int offset = BitConverter.ToInt32(varBytes, at + 4), count = BitConverter.ToInt32(varBytes, at + 8);
                string name = Str(varBytes, at + 16, 32), desc = Str(varBytes, at + 48, 64), unit = Str(varBytes, at + 112, 32);
                if (name.Length > 0 && count > 0 && Enum.IsDefined(type)) vars[name] = new IrsdkVar(name, type, offset, count, unit, desc);
            }
            string yaml = infoLength > 0 ? Str(Read(stream, infoOffset, infoLength), 0, infoLength) : "";
            long started = BitConverter.ToInt64(header, HeaderSize);
            return new IbtFile(stream, vars, yaml, tickRate, records, dataOffset, bufLength)
            {
                StartedUtc = started is > 946_684_800 and < 4_102_444_800 ? DateTimeOffset.FromUnixTimeSeconds(started).UtcDateTime : null,
                StartSessionTime = BitConverter.ToDouble(header, HeaderSize + 8),
            };
        }
        catch (Exception ex) when (ex is EndOfStreamException or ArgumentException)
        {
            stream?.Dispose();
            error = ex.Message;
            return null;
        }
        catch
        {
            stream?.Dispose();
            throw;
        }
    }

    public IEnumerable<byte[]> Records()
    {
        _stream.Position = _dataOffset;
        for (int i = 0; i < RecordCount; i++)
        {
            int read = 0;
            while (read < _record.Length)
            {
                int n = _stream.Read(_record, read, _record.Length - read);
                if (n <= 0) yield break;
                read += n;
            }
            yield return _record;
        }
    }

    private static byte[] Read(FileStream stream, long at, int length)
    {
        var bytes = new byte[length];
        stream.Position = at;
        int read = 0;
        while (read < length)
        {
            int n = stream.Read(bytes, read, length - read);
            if (n <= 0) throw new EndOfStreamException("the file ends early");
            read += n;
        }
        return bytes;
    }

    private static string Str(byte[] raw, int at, int max)
    {
        int end = Array.IndexOf(raw, (byte)0, at, max);
        return Encoding.Latin1.GetString(raw, at, (end < 0 ? at + max : end) - at);
    }

    public void Dispose() => _stream.Dispose();
}

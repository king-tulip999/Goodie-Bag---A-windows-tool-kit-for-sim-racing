using System.Text;
using WinKeys = System.Windows.Forms.Keys;

namespace GoodieBag.SimRacing.IracingFiles;

internal sealed class ControlsFile
{
    public const string FileName = "controls.cfg";
    private const int Body = 68;
    private const int SourceAt = 8, KeyAt = 12, ModsAt = 18;
    public const uint SourceNone = 0, SourceAxis = 1, SourceButton = 2, SourceKey = 4;

    private readonly byte[] _data;
    public IReadOnlyList<ControlBinding> Bindings { get; }

    private ControlsFile(byte[] data, List<ControlBinding> bindings)
    {
        _data = data;
        Bindings = bindings;
    }

    public static ControlsFile Parse(byte[] data)
    {
        if (data.Length < 32 || Encoding.ASCII.GetString(data, 0, 4) != "GFCC")
            throw new FormatException("This isn't an iRacing controls file.");
        int chunk = IndexOf(data, "LRTC"u8);
        if (chunk < 0 || chunk + 12 > data.Length)
            throw new FormatException("The controls list wasn't found in the file.");
        int size = BitConverter.ToInt32(data, chunk + 8);
        int at = chunk + 12, end = at + size;
        if (size <= 0 || end > data.Length)
            throw new FormatException("The controls list has an unexpected size.");

        var list = new List<ControlBinding>();
        while (at < end)
        {
            int nul = Array.IndexOf(data, (byte)0, at, Math.Min(80, end - at));
            if (nul <= at)
                throw new FormatException($"Unexpected data at byte {at}.");
            for (int i = at; i < nul; i++)
                if (data[i] is < 0x21 or > 0x7E)
                    throw new FormatException($"Unexpected data at byte {at}.");
            string name = Encoding.ASCII.GetString(data, at, nul - at);
            int body = nul + 1;
            if (body + Body > end)
                throw new FormatException($"The entry \"{name}\" is cut short.");
            list.Add(new ControlBinding(name, body));
            at = body + Body;
        }
        if (at != end)
            throw new FormatException("The controls list doesn't end where its size says.");
        var file = new ControlsFile(data, list);
        foreach (var b in list)
            b.Owner = file;
        return file;
    }

    public byte[] ToBytes() => (byte[])_data.Clone();

    internal uint Source(ControlBinding b) => BitConverter.ToUInt32(_data, b.BodyAt + SourceAt);
    internal byte Key(ControlBinding b) => _data[b.BodyAt + KeyAt];
    internal byte Mods(ControlBinding b) => _data[b.BodyAt + ModsAt];

    public void Set(ControlBinding b, IracingKey? key)
    {
        uint source = Source(b);
        if (source is not (SourceNone or SourceKey))
            throw new InvalidOperationException($"“{b.Name}” is bound to a wheel or controller; change it in iRacing.");
        int at = b.BodyAt;
        BitConverter.GetBytes(key is null ? SourceNone : SourceKey).CopyTo(_data, at + SourceAt);
        for (int i = KeyAt; i < KeyAt + 8; i++)
            _data[at + i] = 0;
        if (key is { } k)
        {
            _data[at + KeyAt] = k.Code;
            _data[at + ModsAt] = k.Mods;
        }
    }

    public ControlBinding? Find(string name) => Bindings.FirstOrDefault(b => b.Name == name);

    public static byte[] Sample(IEnumerable<(string Name, IracingKey? Key, bool Controller)> entries)
    {
        var records = new List<byte>();
        foreach (var (name, key, controller) in entries)
        {
            records.AddRange(Encoding.ASCII.GetBytes(name));
            records.Add(0);
            var body = new byte[Body];
            BitConverter.GetBytes(6u).CopyTo(body, 4);
            BitConverter.GetBytes(controller ? SourceButton : key is null ? SourceNone : SourceKey).CopyTo(body, SourceAt);
            if (key is { } k) { body[KeyAt] = k.Code; body[ModsAt] = k.Mods; }
            records.AddRange(body);
        }
        var file = new List<byte>(Encoding.ASCII.GetBytes("GFCC"));
        file.AddRange(new byte[28]);
        file.AddRange(Encoding.ASCII.GetBytes("LRTC"));
        file.AddRange(BitConverter.GetBytes(8));
        file.AddRange(BitConverter.GetBytes(records.Count));
        file.AddRange(records);
        return file.ToArray();
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> what) => data.AsSpan().IndexOf(what);
}

internal sealed class ControlBinding
{
    public ControlBinding(string name, int bodyAt)
    {
        Name = name;
        BodyAt = bodyAt;
    }

    public string Name { get; }
    internal int BodyAt { get; }
    internal ControlsFile Owner { get; set; } = null!;

    public uint Source => Owner.Source(this);
    public bool OnController => Source is ControlsFile.SourceAxis or ControlsFile.SourceButton;
    public IracingKey? Key => Source == ControlsFile.SourceKey ? new IracingKey(Owner.Key(this), Owner.Mods(this)) : null;
}

internal readonly record struct IracingKey(byte Code, byte Mods)
{
    public const byte Shift = 0x03, Ctrl = 0x0C, Alt = 0x30;

    private static readonly string[] Numpad = { "Num 9", "Num 3", "Num 1", "Num 7", "Num 4", "Num 8", "Num 6", "Num 2", "Num 0", "Num ." };

    private static readonly Dictionary<WinKeys, byte> Special = new()
    {
        [WinKeys.Space] = 0x20, [WinKeys.Tab] = 0x09, [WinKeys.PageUp] = 0x21, [WinKeys.PageDown] = 0x22,
        [WinKeys.End] = 0x23, [WinKeys.Home] = 0x24, [WinKeys.Left] = 0x25, [WinKeys.Up] = 0x26, [WinKeys.Right] = 0x27,
        [WinKeys.Down] = 0x28, [WinKeys.Insert] = 0x2D, [WinKeys.Delete] = 0x2E, [WinKeys.Clear] = 0x0C,
        [WinKeys.OemQuotes] = Punct('\''), [WinKeys.Oemcomma] = Punct(','), [WinKeys.OemMinus] = Punct('-'), [WinKeys.OemPeriod] = Punct('.'),
        [WinKeys.OemQuestion] = Punct('/'), [WinKeys.OemSemicolon] = Punct(';'), [WinKeys.Oemplus] = Punct('='),
        [WinKeys.OemOpenBrackets] = Punct('['), [WinKeys.OemPipe] = Punct('\\'), [WinKeys.OemCloseBrackets] = Punct(']'),
        [WinKeys.Oemtilde] = Punct('`'), [WinKeys.OemBackslash] = Punct('\\'),
    };

    private const string Punctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`";

    private static byte Punct(char c) => (byte)(0x60 + Punctuation.IndexOf(c));

    private static readonly Dictionary<byte, string> Names = new()
    {
        [0x20] = "Space", [0x09] = "Tab", [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x2D] = "Insert", [0x2E] = "Delete",
        [0x0C] = "Num 5",
    };

    public static IracingKey? FromHotkey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        byte mods = 0;
        byte? code = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            var part = raw.Length == 0 && text.EndsWith('+') ? "+" : raw;
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Ctrl; continue;
                case "alt": mods |= Alt; continue;
                case "shift": mods |= Shift; continue;
                case "win" or "windows": return null;
                case "": continue;
            }
            if (code is not null)
                return null;
            code = CodeOf(part);
            if (code is null)
                return null;
        }
        return code is { } c ? new IracingKey(c, mods) : null;
    }

    private static byte? CodeOf(string part)
    {
        if (part.Length == 1 && char.ToUpperInvariant(part[0]) is var ch && (ch is >= 'A' and <= 'Z' or >= '0' and <= '9'))
            return (byte)ch;
        if (part.Length == 1 && Punctuation.IndexOf(part[0]) >= 0)
            return Punct(part[0]);
        int pad = Array.FindIndex(Numpad, n => n.Equals(part, StringComparison.OrdinalIgnoreCase));
        if (pad >= 0)
            return (byte)(0xB0 + pad);
        foreach (var (code, name) in Names)
            if (name.Equals(part, StringComparison.OrdinalIgnoreCase))
                return code;
        if (!Enum.TryParse<WinKeys>(part, ignoreCase: true, out var key) || int.TryParse(part, out _))
            return null;
        return key switch
        {
            >= WinKeys.A and <= WinKeys.Z => (byte)key,
            >= WinKeys.D0 and <= WinKeys.D9 => (byte)key,
            >= WinKeys.F1 and <= WinKeys.F12 => (byte)(0xC5 + (key - WinKeys.F1)),
            _ => Special.TryGetValue(key, out var c) ? c : null,
        };
    }

    public string KeyName => Code switch
    {
        >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)Code).ToString(),
        >= 0xC5 and <= 0xD0 => "F" + (Code - 0xC4),
        >= 0xB0 and <= 0xB9 => Numpad[Code - 0xB0],
        >= 0x60 and <= 0x7B => Punctuation[Code - 0x60].ToString(),
        _ => Names.TryGetValue(Code, out var n) ? n : WindowsName(Code),
    };

    private static string WindowsName(byte code)
    {
        var key = (WinKeys)code;
        if (!Enum.IsDefined(key)) return $"Key 0x{code:X2}";
        var name = key.ToString();
        return name.StartsWith("NumPad", StringComparison.Ordinal) ? "Num " + name[6..] : name;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Mods & Ctrl) != 0) parts.Add("Ctrl");
        if ((Mods & Alt) != 0) parts.Add("Alt");
        if ((Mods & Shift) != 0) parts.Add("Shift");
        parts.Add(KeyName);
        return string.Join("+", parts);
    }

    public bool SameAs(IracingKey other) => Code == other.Code && Norm(Mods) == Norm(other.Mods);

    private static byte Norm(byte m) => (byte)(((m & Shift) != 0 ? Shift : 0) | ((m & Ctrl) != 0 ? Ctrl : 0) | ((m & Alt) != 0 ? Alt : 0));
}

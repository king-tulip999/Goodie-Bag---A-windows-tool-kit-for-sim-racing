using System.IO;
using System.Text;

namespace GoodieBag.SimRacing.IracingFiles;

internal enum IniLineKind { Blank, Section, Entry, Comment, Other }

internal sealed class IniLine
{
    private const string CommentMark = "\t;";
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private readonly string _originalRaw;
    private readonly string? _originalValue;
    private readonly int _originalCommentColumn;

    private IniLine(IniLineKind kind, string raw, string terminator, string? section, int number,
                    string? key = null, string? value = null, string? comment = null, int commentColumn = -1, bool hadPadding = false)
    {
        Kind = kind;
        Raw = _originalRaw = raw;
        Terminator = terminator;
        Section = section;
        LineNumber = number;
        Key = key;
        Value = _originalValue = value;
        Comment = comment;
        CommentColumn = _originalCommentColumn = commentColumn;
        HadPadding = hadPadding;
    }

    public IniLineKind Kind { get; }
    public string Raw { get; private set; }
    public string Terminator { get; }
    public string? Section { get; }
    public int LineNumber { get; }
    public string? Key { get; }
    public string? Value { get; private set; }
    public string? DisplayValue => ToDisplay(Value);
    public string? Comment { get; }
    public int CommentColumn { get; private set; }
    public bool HadPadding { get; }
    public bool IsChanged => Raw != _originalRaw;

    internal static IniLine Parse(string raw, string terminator, string? currentSection, int number)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return new IniLine(IniLineKind.Blank, raw, terminator, currentSection, number);
        if (trimmed[0] == '[')
        {
            int close = trimmed.LastIndexOf(']');
            if (close > 0)
                return new IniLine(IniLineKind.Section, raw, terminator, trimmed[1..close], number);
        }
        if (trimmed[0] is ';' or '#')
            return new IniLine(IniLineKind.Comment, raw, terminator, currentSection, number);

        int eq = raw.IndexOf('=');
        if (eq <= 0)
            return new IniLine(IniLineKind.Other, raw, terminator, currentSection, number);

        var key = raw[..eq];
        var rest = raw[(eq + 1)..];
        int mark = rest.IndexOf(CommentMark, StringComparison.Ordinal);
        if (mark < 0)
            return new IniLine(IniLineKind.Entry, raw, terminator, currentSection, number, key, rest);

        var valuePart = rest[..mark];
        var value = valuePart.TrimEnd(' ', '\t');
        return new IniLine(IniLineKind.Entry, raw, terminator, currentSection, number, key, value,
            comment: rest[(mark + CommentMark.Length)..],
            commentColumn: eq + 1 + mark,
            hadPadding: value.Length < valuePart.Length);
    }

    internal void SetValue(string displayText, int documentColumn)
    {
        if (Kind != IniLineKind.Entry) throw new InvalidOperationException("Only entries have values.");
        if (displayText.IndexOfAny(new[] { '\r', '\n' }) >= 0 || displayText.Contains(CommentMark))
            throw new ArgumentException("A value can't contain a line break or a tab followed by ';'.", nameof(displayText));
        var stored = FromDisplay(displayText);
        if (stored == Value) return;
        if (stored == _originalValue) { Revert(); return; }
        Value = stored;
        Rebuild(documentColumn);
    }

    private void Rebuild(int documentColumn)
    {
        var prefix = Key + "=" + Value;
        if (Comment is null)
        {
            Raw = prefix;
            return;
        }
        int column = HadPadding ? _originalCommentColumn : documentColumn;
        if (prefix.Length < column) prefix = prefix.PadRight(column);
        Raw = prefix + CommentMark + Comment;
        CommentColumn = prefix.Length;
    }

    internal void Revert()
    {
        Raw = _originalRaw;
        Value = _originalValue;
        CommentColumn = _originalCommentColumn;
    }

    internal static string? ToDisplay(string? stored)
    {
        if (stored is null || stored.All(c => c < 0x80)) return stored;
        try { return StrictUtf8.GetString(Encoding.Latin1.GetBytes(stored)); }
        catch (DecoderFallbackException) { return stored; }
    }

    private static string FromDisplay(string text) =>
        text.All(c => c < 0x80) ? text : Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(text));
}

internal sealed class IniDocument
{
    private const int FallbackCommentColumn = 40;
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
    private readonly List<IniLine> _lines;
    private readonly bool _bom;

    private IniDocument(List<IniLine> lines, bool bom)
    {
        _lines = lines;
        _bom = bom;
        CommentColumn = lines.Where(l => l.Kind == IniLineKind.Entry && l.HadPadding).GroupBy(l => l.CommentColumn)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault()?.Key ?? FallbackCommentColumn;
    }

    public int CommentColumn { get; }
    public bool IsDirty => _lines.Any(l => l.IsChanged);
    public IEnumerable<IniLine> Entries => _lines.Where(l => l.Kind == IniLineKind.Entry);

    public static IniDocument Load(string path) => Parse(File.ReadAllBytes(path));

    public static IniDocument Parse(byte[] bytes)
    {
        bool bom = bytes.Length >= Utf8Bom.Length && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2];
        var text = Encoding.Latin1.GetString(bytes, bom ? Utf8Bom.Length : 0, bytes.Length - (bom ? Utf8Bom.Length : 0));
        var lines = new List<IniLine>();
        string? section = null;
        int start = 0, number = 1;
        while (start < text.Length)
        {
            int nl = text.IndexOf('\n', start);
            string raw, terminator;
            if (nl < 0)
            {
                raw = text[start..];
                terminator = "";
                start = text.Length;
            }
            else
            {
                bool crlf = nl > start && text[nl - 1] == '\r';
                raw = text[start..(crlf ? nl - 1 : nl)];
                terminator = crlf ? "\r\n" : "\n";
                start = nl + 1;
            }
            var line = IniLine.Parse(raw, terminator, section, number++);
            if (line.Kind == IniLineKind.Section) section = line.Section;
            lines.Add(line);
        }
        return new IniDocument(lines, bom);
    }

    public byte[] ToBytes()
    {
        var sb = new StringBuilder();
        foreach (var l in _lines) sb.Append(l.Raw).Append(l.Terminator);
        var text = Encoding.Latin1.GetBytes(sb.ToString());
        if (!_bom) return text;
        var withBom = new byte[Utf8Bom.Length + text.Length];
        Utf8Bom.CopyTo(withBom, 0);
        text.CopyTo(withBom, Utf8Bom.Length);
        return withBom;
    }

    public IniLine? Find(string section, string key)
    {
        IniLine? loose = null;
        foreach (var l in Entries.Where(l => string.Equals(l.Section ?? "", section ?? "", StringComparison.OrdinalIgnoreCase)))
        {
            if (l.Key == key) return l;
            if (loose is null && string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase)) loose = l;
        }
        return loose;
    }

    public bool TrySet(string section, string key, string value)
    {
        if (Find(section, key) is not { } line) return false;
        line.SetValue(value, CommentColumn);
        return true;
    }
}

internal static class SafeFileWriter
{
    public static void WriteAtomic(string path, byte[] bytes)
    {
        if (File.Exists(path) && new FileInfo(path).IsReadOnly)
            throw new UnauthorizedAccessException($"{Path.GetFileName(path)} is read-only, so it can't be written.");
        var temp = path + ".gb-new";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path))
                File.Replace(temp, path, null, ignoreMetadataErrors: true);
            else
                File.Move(temp, path);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }

    public static void WriteAllAtomic(IReadOnlyList<(string Path, byte[] Bytes)> files, string done)
    {
        if (files.Count == 0) return;
        var originals = new byte[]?[files.Count];
        for (int i = 0; i < files.Count; i++)
            originals[i] = File.Exists(files[i].Path) ? File.ReadAllBytes(files[i].Path) : null;

        var temps = new string?[files.Count];
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                var (path, bytes) = files[i];
                var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetFileName(path) + ".goodiebag.tmp");
                temps[i] = temp;
                using var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            foreach (var (path, _) in files)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"{Path.GetFileName(path)} is in use by another program (or can't be written), so nothing was {done}. Close it and try again.", ex);
                }
            }

            int replaced = 0;
            try
            {
                for (; replaced < files.Count; replaced++)
                {
                    var path = files[replaced].Path;
                    var temp = temps[replaced]!;
                    if (originals[replaced] is not null && File.Exists(path))
                        File.Replace(temp, path, null, ignoreMetadataErrors: true);
                    else
                        File.Move(temp, path, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var failed = Path.GetFileName(files[replaced].Path);
                var notBack = new List<string>();
                if (originals[replaced] is { } lost && !File.Exists(files[replaced].Path))
                {
                    try { WriteAtomic(files[replaced].Path, lost); }
                    catch (Exception) { notBack.Add(failed); }
                }
                for (int i = 0; i < replaced; i++)
                {
                    var path = files[i].Path;
                    try
                    {
                        if (originals[i] is { } old) WriteAtomic(path, old);
                        else File.Delete(path);
                    }
                    catch (Exception) { notBack.Add(Path.GetFileName(path)); }
                }
                if (replaced == 0 && notBack.Count == 0)
                    throw new IOException($"{failed} couldn't be replaced ({ex.Message}), so nothing was {done}.", ex);
                string after = notBack.Count == 0 ? "The files already changed were put back, so nothing was " + done + "."
                    : $"Putting back {string.Join(", ", notBack)} also failed: restore the backup taken just before.";
                throw new IOException($"{failed} couldn't be replaced ({ex.Message}). {after}", ex);
            }
        }
        finally
        {
            foreach (var temp in temps)
            {
                if (temp is null) continue;
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
            }
        }
    }
}

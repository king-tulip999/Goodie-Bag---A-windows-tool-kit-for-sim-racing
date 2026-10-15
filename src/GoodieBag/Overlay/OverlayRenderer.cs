using System.Drawing.Drawing2D;
using System.Drawing.Text;
using GoodieBag.Config;
using GoodieBag.Hardware;
using GoodieBag.Network;
using System.Drawing;

namespace GoodieBag.Overlay;

internal sealed record OverlayData(
    HardwareSnapshot? Hardware,
    FrameStats? Frames,
    string? FpsNote,
    IReadOnlyList<(string Name, float? Rpm)> Fans,
    NetworkSnapshot? Network,
    double[] FrameTimes);

internal readonly record struct OverlayHit(string Group, OverlayMetric? Metric, RectangleF Rect);

internal sealed class OverlayRenderer : IDisposable
{
    public List<OverlayHit>? Hits { get; init; }

    private const string Missing = GoodieBag.SimRacing.Race.RaceFormat.Missing;

    private const float BaseSize = 1.2f;

    private readonly Bitmap _measureBitmap = new(1, 1);
    private readonly Graphics _measure;
    private readonly StringFormat _format = (StringFormat)StringFormat.GenericTypographic.Clone();
    private float _fontScale;
    private Font? _labelFont, _valueFont, _unitFont;

    private const int MaxCachedWidths = 512, MaxCachedBrushes = 32, MaxCachedPalettes = 8;
    private readonly Dictionary<string, float> _labelWidths = new(), _valueWidths = new(), _unitWidths = new();
    private readonly Dictionary<string, string> _upper = new();
    private readonly Dictionary<int, SolidBrush> _brushes = new();
    private readonly Dictionary<(string?, string?, string?, string?, string?, string?, string?, string?), Palette> _palettes = new();
    private OverlayLayout? _presetLayout;
    private OverlayColorPreset _presetOf;
    private Palette? _presetPalette;
    private Pen? _baselinePen, _graphPen, _rulePen;
    private GraphicsPath? _backPath;
    private (int W, int H, float Scale) _backKey;
    private PointF[] _graphPoints = Array.Empty<PointF>();

    private enum Tone { Normal, Fps, Warning, Critical, Dim }

    private sealed record Cell(string Prefix, string Value, string Unit, string Template, Tone Tone = Tone.Normal, OverlayMetric? Metric = null);
    private sealed record Group(string Key, string Label, List<Cell> Cells);

    private sealed record Palette(Color Text, Color Secondary, Color Accent, Color Background, Color Graph, Color Warning, Color Critical, Color Fps);

    public OverlayRenderer()
    {
        _measure = Graphics.FromImage(_measureBitmap);
        _measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        _format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
    }

    public Bitmap Render(OverlayLayout layout, OverlayData data, float scale)
    {
        Bitmap? bitmap = null;
        Graphics? graphics = null;
        try
        {
            Render(layout, data, scale, size =>
            {
                bitmap = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                return graphics = Graphics.FromImage(bitmap);
            });
            return bitmap!;
        }
        catch
        {
            bitmap?.Dispose();
            throw;
        }
        finally
        {
            graphics?.Dispose();
        }
    }

    public Size Render(OverlayLayout layout, OverlayData data, float scale, Func<Size, Graphics> surface)
    {
        scale *= layout.FontScale / 100f * BaseSize;
        EnsureFonts(scale);
        var colors = PaletteFor(layout);
        var groups = BuildGroups(layout, data);
        Hits?.Clear();
        float across = layout.SpacingX / 100f, down = layout.SpacingY / 100f;

        float pad = 10 * scale, rowGap = 3 * scale * down, cellGap = 12 * scale * across, labelGap = 8 * scale, groupGap = 16 * scale * across;
        float rowHeight = _valueFont!.GetHeight(_measure);

        var rows = layout.Style == OverlayStyle.Compact && groups.Count > 0 ? new List<List<Group>> { groups } : groups.Select(g => new List<Group> { g }).ToList();
        float labelColumn = layout.Style == OverlayStyle.Expanded && groups.Count > 0 ? groups.Max(g => Measure(g.Label, _labelFont!)) : 0;

        float GroupWidth(Group g, bool inline) =>
            (inline ? Measure(g.Label, _labelFont!) : labelColumn) + labelGap + g.Cells.Sum(CellWidth) + cellGap * Math.Max(0, g.Cells.Count - 1);
        float RowWidth(List<Group> row) =>
            row.Sum(g => GroupWidth(g, layout.Style == OverlayStyle.Compact)) + groupGap * Math.Max(0, row.Count - 1);

        bool graph = layout.ShowGraph && data.FrameTimes.Length > 1;
        float graphHeight = graph ? 34 * scale : 0;
        float contentWidth = Math.Max(rows.Count == 0 ? 0 : rows.Max(RowWidth), graph ? 160 * scale : 0);
        int width = Math.Max(20, (int)Math.Ceiling(contentWidth + pad * 2));
        float textHeight = rows.Count * rowHeight + Math.Max(0, rows.Count - 1) * rowGap;
        int height = Math.Max(20, (int)Math.Ceiling(pad * 2 + textHeight + (graph ? graphHeight + (rows.Count > 0 ? 6 * scale : 0) : 0)));

        var g = surface(new Size(width, height));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        int alpha = (int)Math.Round(Math.Clamp(layout.BackgroundOpacity, 0, 100) * 2.55);
        if (alpha > 0)
        {
            if (_backPath is null || _backKey != (width, height, scale))
            {
                _backPath?.Dispose();
                _backPath = RoundedRect(new RectangleF(0, 0, width - 1, height - 1), 9 * scale);
                _backKey = (width, height, scale);
            }
            g.FillPath(BrushFor(Color.FromArgb(alpha, colors.Background)), _backPath);
        }

        float y = pad;
        foreach (var row in rows)
        {
            float x = pad;
            foreach (var group in row)
            {
                bool inline = layout.Style == OverlayStyle.Compact;
                if (inline && !ReferenceEquals(group, row[0])) Rule(g, x - groupGap / 2, y, rowHeight, colors, scale);
                DrawText(g, group.Label, _labelFont!, colors.Accent, x, y + (rowHeight - _labelFont!.GetHeight(g)) / 2);
                float labelWidth = inline ? Measure(group.Label, _labelFont!) : labelColumn;
                Hits?.Add(new(group.Key, null, new RectangleF(x, y, labelWidth, rowHeight)));
                x += labelWidth + labelGap;
                for (int i = 0; i < group.Cells.Count; i++)
                {
                    var cell = group.Cells[i];
                    float start = x;
                    if (i > 0) Rule(g, x - cellGap / 2, y, rowHeight, colors, scale);
                    float prefixWidth = cell.Prefix.Length > 0 ? Measure(cell.Prefix + " ", _unitFont!) : 0;
                    float valueWidth = Measure(cell.Value, _valueFont);
                    float unitWidth = cell.Unit.Length > 0 ? 2 * scale + Measure(cell.Unit, _unitFont!) : 0;
                    x = start + Math.Max(0, (CellWidth(cell) - prefixWidth - valueWidth - unitWidth) / 2);
                    if (cell.Prefix.Length > 0)
                        DrawText(g, cell.Prefix, _unitFont!, colors.Secondary, x, y + UnitOffset(g, rowHeight));
                    x += prefixWidth;
                    DrawText(g, cell.Value, _valueFont, ToneColor(cell.Tone, colors), x, y);
                    x += valueWidth;
                    if (cell.Unit.Length > 0)
                        DrawText(g, cell.Unit, _unitFont!, colors.Secondary, x + 2 * scale, y + UnitOffset(g, rowHeight));
                    Hits?.Add(new(group.Key, cell.Metric, new RectangleF(start, y, CellWidth(cell), rowHeight)));
                    x = start + CellWidth(cell) + cellGap;
                }
                x += groupGap - cellGap;
            }
            y += rowHeight + rowGap;
        }

        if (graph)
        {
            float top = height - pad - graphHeight;
            DrawGraph(g, new RectangleF(pad, top, width - pad * 2, graphHeight), data.FrameTimes, colors, scale);
        }
        return new Size(width, height);
    }

    private void Rule(Graphics g, float x, float y, float rowHeight, Palette colors, float scale)
    {
        float h = _labelFont!.GetHeight(g);
        var rule = CachedPen(ref _rulePen, Color.FromArgb(150, colors.Accent), Math.Max(1f, 1.5f * scale));
        g.DrawLine(rule, x, y + (rowHeight - h) / 2 + h * 0.05f, x, y + (rowHeight + h) / 2 - h * 0.05f);
    }

    private List<Group> BuildGroups(OverlayLayout layout, OverlayData d)
    {
        var groups = new List<Group>();
        var hw = d.Hardware;
        var f = d.Frames;
        foreach (var entry in layout.Metrics.Where(m => m.Enabled))
        {
            string key = OverlayLayout.GroupOf(entry.Metric), name = layout.GroupName(key);
            var group = groups.FirstOrDefault(x => x.Key == key);
            if (group is null)
                groups.Add(group = new Group(key, name, new List<Cell>()));
            string Tag(string standard) => layout.MetricTag(entry.Metric, standard);
            int first = group.Cells.Count;
            if (key == "FPS" && d.FpsNote is not null)
            {
                if (group.Cells.Count == 0)
                    group.Cells.Add(new Cell("", d.FpsNote, "", d.FpsNote, Tone.Dim));
                continue;
            }

            switch (entry.Metric)
            {
                case OverlayMetric.Fps: group.Cells.Add(new(Tag(""), Num(f?.Fps), "", "0000", Tone.Fps)); break;
                case OverlayMetric.AverageFps: group.Cells.Add(new(Tag("avg"), Num(f?.AverageFps), "", "0000")); break;
                case OverlayMetric.OnePercentLow: group.Cells.Add(new(Tag("1%"), Num(f?.OnePercentLowFps), "", "0000")); break;
                case OverlayMetric.FrameTime: group.Cells.Add(new(Tag(""), f is null ? Missing : f.FrameTimeMs.ToString("0.0"), "ms", "00.0")); break;

                case OverlayMetric.GpuTemp: group.Cells.Add(new(Tag(""), Num(hw?.GpuTemp), "°C", "100", TempTone(hw?.GpuTemp, 80, 90))); break;
                case OverlayMetric.GpuLoad: group.Cells.Add(new(Tag(""), Num(hw?.GpuLoad), "%", "100")); break;
                case OverlayMetric.GpuHotSpot: group.Cells.Add(new(Tag("hotspot"), Num(hw?.GpuHotSpot), "°C", "100", TempTone(hw?.GpuHotSpot, 95, 105))); break;
                case OverlayMetric.GpuClock: group.Cells.Add(new(Tag(""), Num(hw?.GpuClockMhz), "MHz", "0000")); break;
                case OverlayMetric.GpuPower: group.Cells.Add(new(Tag(""), Num(hw?.GpuPower), "W", "000")); break;
                case OverlayMetric.GpuFan: group.Cells.Add(new(Tag("fan"), Num(hw?.GpuFanRpm), "rpm", "0000")); break;
                case OverlayMetric.Vram:
                    group.Cells.Add(new(Tag(""), hw?.VramUsedMb is { } vu ? (vu / 1024).ToString("0.0") : Missing, hw?.VramTotalMb is { } vt ? $"/ {vt / 1024:0} GB" : "GB", "00.0"));
                    break;

                case OverlayMetric.CpuTemp: group.Cells.Add(new(Tag(""), Num(hw?.CpuTemp), "°C", "100", TempTone(hw?.CpuTemp, 80, 90))); break;
                case OverlayMetric.CpuLoad: group.Cells.Add(new(Tag(""), Num(hw?.CpuLoad), "%", "100")); break;
                case OverlayMetric.CpuClock: group.Cells.Add(new(Tag(""), hw?.CpuClockMhz is { } mhz ? (mhz / 1000).ToString("0.00") : Missing, "GHz", "0.00")); break;
                case OverlayMetric.CpuPower: group.Cells.Add(new(Tag(""), Num(hw?.CpuPower), "W", "000")); break;

                case OverlayMetric.Ram:
                    group.Cells.Add(new(Tag(""), hw?.RamUsedGb is { } ru ? ru.ToString("0.0") : Missing, hw?.RamTotalGb is { } rt ? $"/ {rt:0} GB" : "GB", "00.0"));
                    break;

                case OverlayMetric.Ping:
                    var ping = d.Network?.PingMs;
                    group.Cells.Add(new(Tag(""), Num(ping), "ms", "000", ping is > 100 ? Tone.Critical : ping is > 60 ? Tone.Warning : Tone.Normal));
                    break;
                case OverlayMetric.Network:
                    group.Cells.Add(new(Tag("↓"), d.Network is { } n1 ? Mbps(n1.DownBytesPerSec) : Missing, "Mbit/s", "000.0"));
                    group.Cells.Add(new("↑", d.Network is { } n2 ? Mbps(n2.UpBytesPerSec) : Missing, "Mbit/s", "000.0"));
                    break;

                case OverlayMetric.Fans:
                    foreach (var fan in d.Fans.Take(4))
                    {
                        string shortName = FanTag(fan.Name);
                        group.Cells.Add(new(shortName, fan.Rpm is { } r ? r.ToString("0") : Missing, "", "0000"));
                    }
                    break;
            }
            for (int i = first; i < group.Cells.Count; i++) group.Cells[i] = group.Cells[i] with { Metric = entry.Metric };
        }
        groups.RemoveAll(g => g.Cells.Count == 0);
        return groups;
    }

    internal static int DropIndex(IReadOnlyList<(float Start, float End)> spans, int from, float at)
    {
        int index = 0;
        for (int i = 0; i < spans.Count; i++)
            if (i != from && at > (spans[i].Start + spans[i].End) / 2) index++;
        return index;
    }

    internal static string FanTag(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";
        string head = words[0].Length > 7 ? words[0][..7] : words[0];
        string last = words[^1];
        return words.Length > 1 && last.All(char.IsDigit) ? (head.Length > 4 ? head[..4] : head) + last : head;
    }

    private static string Mbps(double bytesPerSecond)
    {
        double mbps = bytesPerSecond * 8 / 1e6;
        return mbps >= 100 ? mbps.ToString("0") : mbps.ToString("0.0");
    }

    private static Tone TempTone(float? temp, float warm, float hot) =>
        temp >= hot ? Tone.Critical : temp >= warm ? Tone.Warning : Tone.Normal;

    private static string Num(double? value) => value is { } v ? Math.Round(v).ToString("0") : Missing;
    private static string Num(float? value) => Num((double?)value);

    private void DrawGraph(Graphics g, RectangleF r, double[] times, Palette colors, float scale)
    {
        double max = Math.Max(33.4, times.Max() * 1.1);
        var baseline = CachedPen(ref _baselinePen, Color.FromArgb(70, colors.Secondary), Math.Max(1, scale));
        float y60 = r.Bottom - (float)(16.7 / max) * r.Height;
        g.DrawLine(baseline, r.Left, y60, r.Right, y60);
        if (_graphPoints.Length != times.Length)
            _graphPoints = new PointF[times.Length];
        var points = _graphPoints;
        for (int i = 0; i < times.Length; i++)
            points[i] = new PointF(r.Left + r.Width * i / (times.Length - 1), r.Bottom - (float)(Math.Min(times[i], max) / max) * r.Height);
        var pen = CachedPen(ref _graphPen, colors.Graph, 1.6f * scale);
        g.DrawLines(pen, points);
    }

    private static Pen CachedPen(ref Pen? pen, Color color, float width)
    {
        if (pen is null || pen.Color.ToArgb() != color.ToArgb() || pen.Width != width)
        {
            pen?.Dispose();
            pen = new Pen(color, width) { LineJoin = LineJoin.Round };
        }
        return pen;
    }

    public void InvalidateColors()
    {
        _presetLayout = null;
        _presetPalette = null;
    }

    private Palette PaletteFor(OverlayLayout layout)
    {
        if (_presetPalette is not null && ReferenceEquals(layout, _presetLayout) && layout.Colors == _presetOf)
            return _presetPalette;
        var c = Theme.OverlayPresets.For(layout.Colors, layout.Custom, layout.ColorSet);
        bool preset = !ReferenceEquals(c, layout.Custom);
        var resolved = Resolve(c);
        if (preset)
            (_presetLayout, _presetOf, _presetPalette) = (layout, layout.Colors, resolved);
        return resolved;
    }

    private Palette Resolve(OverlayColors c)
    {
        var key = (c.Text, c.Secondary, c.Accent, c.Background, c.Graph, c.Warning, c.Critical, c.Fps);
        if (_palettes.TryGetValue(key, out var cached))
            return cached;
        var palette = new Palette(Parse(c.Text, Color.White), Parse(c.Secondary, Color.Gray), Parse(c.Accent, Color.Orange), Parse(c.Background, Color.Black),
            Parse(c.Graph, Color.Orange), Parse(c.Warning, Color.Orange), Parse(c.Critical, Color.Red), Parse(c.Fps, Color.White));
        if (_palettes.Count >= MaxCachedPalettes)
            _palettes.Clear();
        _palettes[key] = palette;
        return palette;
    }

    public static Color Parse(string? hex, Color fallback)
    {
        try { return hex is null ? fallback : ColorTranslator.FromHtml(hex); }
        catch { return fallback; }
    }

    private static Color ToneColor(Tone tone, Palette p) => tone switch
    {
        Tone.Fps => p.Fps,
        Tone.Warning => p.Warning,
        Tone.Critical => p.Critical,
        Tone.Dim => p.Secondary,
        _ => p.Text,
    };

    private float CellWidth(Cell c)
    {
        float w = Measure(c.Template, _valueFont!);
        if (c.Prefix.Length > 0) w += Measure(c.Prefix + " ", _unitFont!);
        if (c.Unit.Length > 0) w += Measure(c.Unit, _unitFont!) + 2 * _fontScale;
        return w;
    }

    private float UnitOffset(Graphics g, float rowHeight) =>
        (rowHeight - _unitFont!.GetHeight(g)) * 0.72f;

    private string Upper(string text)
    {
        bool lower = false;
        foreach (char c in text) if (char.IsLower(c)) { lower = true; break; }
        if (!lower) return text;
        if (_upper.TryGetValue(text, out var up)) return up;
        if (_upper.Count >= MaxCachedWidths) _upper.Clear();
        return _upper[text] = text.ToUpperInvariant();
    }

    private float Measure(string text, Font font)
    {
        if (text.Length == 0)
            return 0;
        var cache = ReferenceEquals(font, _valueFont) ? _valueWidths
            : ReferenceEquals(font, _labelFont) ? _labelWidths
            : ReferenceEquals(font, _unitFont) ? _unitWidths
            : null;
        if (cache is not null && cache.TryGetValue(text, out float width))
            return width;
        width = _measure.MeasureString(Upper(text), font, PointF.Empty, _format).Width;
        if (cache is not null)
        {
            if (cache.Count >= MaxCachedWidths)
                cache.Clear();
            cache[text] = width;
        }
        return width;
    }

    private void DrawText(Graphics g, string text, Font font, Color color, float x, float y) =>
        g.DrawString(Upper(text), font, BrushFor(color), x, y, _format);

    private SolidBrush BrushFor(Color color)
    {
        int argb = color.ToArgb();
        if (!_brushes.TryGetValue(argb, out var brush))
        {
            if (_brushes.Count >= MaxCachedBrushes)
                ClearBrushes();
            _brushes[argb] = brush = new SolidBrush(Color.FromArgb(argb));
        }
        return brush;
    }

    private void ClearBrushes()
    {
        foreach (var brush in _brushes.Values)
            brush.Dispose();
        _brushes.Clear();
    }

    private void EnsureFonts(float scale)
    {
        if (_valueFont is not null && Math.Abs(scale - _fontScale) < 0.001f)
            return;

        _labelFont?.Dispose(); _valueFont?.Dispose(); _unitFont?.Dispose();
        _labelWidths.Clear(); _valueWidths.Clear(); _unitWidths.Clear();
        _fontScale = scale;
        _labelFont = new Font("Segoe UI", 11.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _valueFont = new Font("Segoe UI Semibold", 15f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _unitFont = new Font("Segoe UI", 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        _labelFont?.Dispose(); _valueFont?.Dispose(); _unitFont?.Dispose();
        ClearBrushes();
        _baselinePen?.Dispose(); _graphPen?.Dispose(); _rulePen?.Dispose(); _backPath?.Dispose();
        _format.Dispose();
        _measure.Dispose();
        _measureBitmap.Dispose();
    }
}

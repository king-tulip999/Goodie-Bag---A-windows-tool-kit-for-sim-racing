using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.RegularExpressions;
using GoodieBag.Config;
using GoodieBag.Overlay;
using GoodieBag.SimRacing.Hud;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.Showcase;

internal sealed partial class HudMontage : IDisposable
{
    private const int Columns = 5;

    private sealed class Item
    {
        public required HudWidgetDef Def;
        public required HudWidgetConfig Config;
        public required HudWidget Widget;
        public required HudOptions Options;
        public readonly HudPainter Painter = new();
        public readonly OverlayWindow Window = new();
        public Bitmap? Content;
        public Rectangle Box;
        public string Description = "";
        public long ShowAt = long.MaxValue;
        public long LastDraw;
    }

    private readonly RaceHudSettings _settings = new();
    private readonly RaceState _race;
    private readonly DemoTelemetry _demo = new();
    private readonly TelemetryFrame _frame = new();
    private readonly List<Item> _items = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = new();
    private long _seq, _fadeFrom = -1, _lastTick;
    private double _since = 0.25;
    private float _scale = 1.25f;
    private int _labelHeight;
    private HudPalette _palette = HudPalette.Default;
    private Font? _nameFont, _textFont;

    public HudMontage()
    {
        _race = new RaceState(() => _settings);
        _timer.Tick += (_, _) => Tick();
    }

    public IReadOnlyList<HudWidgetDef> Widgets => _items.Select(i => i.Def).ToList();

    public void Warm()
    {
        _settings.Appearance.Colors = HudColors.Default;
        for (double t = -1500; t <= 0; t += 0.25) Feed(t);
        Thread.Sleep(300);
        Feed(0.25);
    }

    private void Feed(double t)
    {
        _demo.Fill(_frame, t);
        _frame.Sequence = ++_seq;
        _race.Update(_frame);
    }

    public void Layout(Rectangle area, float monitorScale)
    {
        foreach (var item in _items) item.Window.Dispose();
        _items.Clear();
        int margin = (int)Math.Round(44 * monitorScale), gap = (int)Math.Round(22 * monitorScale), captionRoom = (int)Math.Round(170 * monitorScale);
        int columnWidth = (area.Width - 2 * margin - (Columns - 1) * gap) / Columns;
        int bottom = area.Bottom - captionRoom;
        for (_scale = monitorScale; ; _scale -= 0.05f)
        {
            _labelHeight = (int)Math.Round(46 * _scale);
            var heights = new int[Columns];
            var placed = new List<(HudWidgetDef Def, Rectangle Box)>();
            foreach (var def in HudCatalog.All)
            {
                int column = Array.IndexOf(heights, heights.Min());
                int h = _labelHeight + (int)Math.Round(def.H * _scale);
                var box = new Rectangle(area.Left + margin + column * (columnWidth + gap), area.Top + margin + heights[column], columnWidth, h);
                heights[column] += h + gap;
                placed.Add((def, box));
            }
            if (heights.Max() - gap <= bottom - area.Top - margin || _scale <= 0.8f * monitorScale)
            {
                foreach (var (def, box) in placed)
                {
                    var config = new HudWidgetConfig { Id = "showcase-" + def.Kind, Kind = def.Kind, W = Math.Max(def.MinW, columnWidth / _scale), H = def.H };
                    _items.Add(new Item
                    {
                        Def = def, Config = config, Widget = def.Create(), Options = new HudOptions(def, config), Box = box,
                        Description = Short(def.Description),
                    });
                }
                break;
            }
        }
        _nameFont = new Font("Segoe UI Semibold", 15.5f * _scale, GraphicsUnit.Pixel);
        _textFont = new Font("Segoe UI", 13f * _scale, GraphicsUnit.Pixel);
    }

    internal static string Short(string description)
    {
        string text = Parenthesised().Replace(description, "").Replace(" ,", ",").Trim().TrimEnd('.');
        var parts = Clauses().Split(text);
        string result = parts[0];
        if (result.Length < 18 && parts.Length > 1) result = text[..(result.Length + 2 + parts[1].Length)];
        return result.Trim();
    }

    [GeneratedRegex(@"\s*\([^)]*\)")] private static partial Regex Parenthesised();
    [GeneratedRegex(@"[,:] ")] private static partial Regex Clauses();

    public void PopIn(int staggerMs)
    {
        _clock.Restart();
        _lastTick = 0;
        for (int i = 0; i < _items.Count; i++) _items[i].ShowAt = i * staggerMs * Stopwatch.Frequency / 1000;
        _fadeFrom = -1;
        Tick();
        _timer.Start();
    }

    public TimeSpan PopInTime(int staggerMs) => TimeSpan.FromMilliseconds((_items.Count - 1) * staggerMs + EnterMs);

    public async Task FadeOut(int ms)
    {
        _fadeMs = ms;
        _fadeFrom = _clock.ElapsedTicks;
        await Task.Delay(ms + 40);
        _timer.Stop();
        foreach (var item in _items) item.Window.Hide();
    }

    private const double EnterMs = 260;
    private int _fadeMs = 500;

    private void Tick()
    {
        long now = _clock.ElapsedTicks;
        _since += (now - _lastTick) / (double)Stopwatch.Frequency;
        _lastTick = now;
        Feed(_since);
        double fade = _fadeFrom < 0 ? 1 : 1 - Math.Clamp((now - _fadeFrom) * 1000.0 / Stopwatch.Frequency / _fadeMs, 0, 1);
        foreach (var item in _items)
        {
            if (now < item.ShowAt) continue;
            double t = Math.Clamp((now - item.ShowAt) * 1000.0 / Stopwatch.Frequency / EnterMs, 0, 1);
            double ease = 1 - Math.Pow(1 - t, 3);
            int hz = Math.Max(item.Def.DefaultHz, 4);
            if (item.Content is null || (now - item.LastDraw) * 1000.0 / Stopwatch.Frequency >= 1000.0 / hz - 4)
            {
                Draw(item);
                item.LastDraw = now;
            }
            else if (t >= 1 && fade >= 1) continue;
            if (!item.Window.Visible) item.Window.Show();
            item.Window.Present(new Point(item.Box.X, item.Box.Y + (int)Math.Round((1 - ease) * 18 * _scale)), (byte)Math.Round(255 * ease * fade));
        }
    }

    private void Draw(Item item)
    {
        int widgetHeight = item.Box.Height - _labelHeight;
        if (item.Content is null) item.Content = new Bitmap(item.Box.Width, widgetHeight, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(item.Content))
        {
            item.Painter.Simulated = true;
            item.Painter.MarkSimulated = false;
            item.Painter.Begin(g, item.Box.Width / (float)item.Config.W, new SizeF((float)item.Config.W, (float)item.Config.H), _palette, _settings.Appearance);
            var view = new HudView { Frame = _race.Frame, Race = _race, Settings = _settings, Config = item.Config, Options = item.Options, Def = item.Def };
            item.Widget.Draw(item.Painter, view);
        }
        var s = item.Window.Surface(item.Box.Size);
        s.ResetTransform();
        s.Clear(Color.Transparent);
        s.SmoothingMode = SmoothingMode.AntiAlias;
        s.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var band = new Rectangle(0, 0, item.Box.Width, _labelHeight - (int)(6 * _scale));
        using (var fill = new SolidBrush(HudPalette.Alpha(_palette.Bg, 225))) s.FillRectangle(fill, band);
        using (var accent = new SolidBrush(_palette.Accent)) s.FillRectangle(accent, new Rectangle(0, 0, (int)Math.Max(3, 3 * _scale), band.Height));
        float x = 12 * _scale;
        using (var name = new SolidBrush(_palette.Heading)) s.DrawString(item.Def.Name.ToUpperInvariant(), _nameFont!, name, x, 3 * _scale);
        using (var muted = new SolidBrush(_palette.Muted))
        using (var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            s.DrawString(item.Description, _textFont!, muted, new RectangleF(x, 21 * _scale, band.Width - x - 6, 18 * _scale), format);
        s.DrawImageUnscaled(item.Content, 0, _labelHeight);
    }

    public void Dispose()
    {
        _timer.Dispose();
        foreach (var item in _items)
        {
            item.Window.Dispose();
            item.Painter.Dispose();
            item.Content?.Dispose();
        }
        _nameFont?.Dispose();
        _textFont?.Dispose();
    }
}

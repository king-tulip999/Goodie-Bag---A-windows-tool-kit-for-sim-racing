using System.Drawing;
using System.Globalization;
using GoodieBag.Config;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Hud;

internal enum HudOptionKind
{
    Bool,
    Int,
    Number,
    Choice,
    Columns,

    Classes,
    Set,
}

internal sealed record HudChoice(string Id, string Label, string? Help = null);

internal enum HudOptionGroup { Show, Filter, Options, Parts }

internal sealed record HudOption(string Key, string Label, HudOptionKind Kind, string Default, string? Help = null)
{
    public IReadOnlyList<HudChoice> Choices { get; init; } = Array.Empty<HudChoice>();
    public double Min { get; init; }
    public double Max { get; init; } = 100;
    public double Step { get; init; } = 1;
    public string? Unit { get; init; }
    public HudOptionGroup? Group { get; init; }

    public HudOptionGroup Section => Group ?? Kind switch
    {
        HudOptionKind.Columns or HudOptionKind.Set => HudOptionGroup.Show,
        HudOptionKind.Classes => HudOptionGroup.Filter,
        _ => HudOptionGroup.Options,
    };

    public static HudOption Bool(string key, string label, bool on, string? help = null) => new(key, label, HudOptionKind.Bool, on ? "true" : "false", help);

    public static HudOption Int(string key, string label, int value, int min, int max, string? unit = null, string? help = null) =>
        new(key, label, HudOptionKind.Int, value.ToString(CultureInfo.InvariantCulture), help) { Min = min, Max = max, Unit = unit };

    public static HudOption Number(string key, string label, double value, double min, double max, double step, string? unit = null, string? help = null) =>
        new(key, label, HudOptionKind.Number, value.ToString(CultureInfo.InvariantCulture), help) { Min = min, Max = max, Step = step, Unit = unit };

    public static HudOption Choice(string key, string label, string value, params HudChoice[] choices) =>
        new(key, label, HudOptionKind.Choice, value) { Choices = choices };

    public static HudOption Columns(string key, string label, string on, params HudChoice[] choices) =>
        new(key, label, HudOptionKind.Columns, on) { Choices = choices };

    public static HudOption Classes(string key, string label, string? help = null) => new(key, label, HudOptionKind.Classes, "", help);

    public static HudOption Set(string key, string label, string on, string? help, params HudChoice[] choices) =>
        new(key, label, HudOptionKind.Set, on, help) { Choices = choices };
}

internal sealed record HudWidgetDef(string Kind, string Name, string Description, double W, double H, Func<HudWidget> Create)
{
    public double MinW { get; init; } = 80;
    public double MinH { get; init; } = 40;
    public int DefaultHz { get; init; } = 10;
    public IReadOnlyList<HudOption> Options { get; init; } = Array.Empty<HudOption>();
    public string? Limits { get; init; }
    public bool Clickable { get; init; }
    public bool? HideWhenEmpty { get; init; }

    public HudOption? Option(string key) => Options.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));
}

internal sealed class HudOptions
{
    private readonly HudWidgetDef _def;
    private readonly HudWidgetConfig _config;
    private readonly Dictionary<string, (string Raw, IReadOnlyList<string> Parsed)> _columns = new(StringComparer.OrdinalIgnoreCase);

    public HudOptions(HudWidgetDef def, HudWidgetConfig config)
    {
        _def = def;
        _config = config;
    }

    public string Raw(string key) =>
        _config.Options.TryGetValue(key, out var v) ? v : _def.Option(key)?.Default ?? "";

    public bool Bool(string key) => bool.TryParse(Raw(key), out var b) ? b : _def.Option(key)?.Default == "true";

    public int Int(string key)
    {
        var o = _def.Option(key);
        int v = int.TryParse(Raw(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i
            : int.TryParse(o?.Default, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 0;
        return o is null ? v : (int)Math.Clamp(v, o.Min, o.Max);
    }

    public double Number(string key)
    {
        var o = _def.Option(key);
        double v = double.TryParse(Raw(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.IsFinite(x) ? x
            : double.TryParse(o?.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        return o is null ? v : Math.Clamp(v, o.Min, o.Max);
    }

    public string Choice(string key)
    {
        string v = Raw(key);
        var o = _def.Option(key);
        return o is null || o.Choices.Any(c => c.Id == v) ? v : o.Default;
    }

    public IReadOnlyList<string> Columns(string key)
    {
        string raw = Raw(key);
        if (_columns.TryGetValue(key, out var cached) && ReferenceEquals(cached.Raw, raw)) return cached.Parsed;
        var o = _def.Option(key);
        var parsed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => o is null || o.Kind == HudOptionKind.Classes || o.Choices.Any(c => c.Id == id)).Distinct().ToList();
        _columns[key] = (raw, parsed);
        return parsed;
    }
}

internal sealed class HudView
{
    public required TelemetryFrame Frame { get; init; }
    public required RaceState Race { get; init; }
    public required RaceHudSettings Settings { get; init; }
    public required HudWidgetConfig Config { get; init; }
    public required HudOptions Options { get; init; }
    public required HudWidgetDef Def { get; init; }
    public HudUnits Units => Settings.Units;
    public double Now => Race.Now;

    public bool ClassShown(int car, string key = "hideClasses", string? scopeKey = "classes")
    {
        if (!Frame.Session.ByCar.TryGetValue(car, out var d)) return false;
        if (scopeKey is not null && Def.Option(scopeKey) is not null && Options.Choice(scopeKey) == "mine"
            && Frame.Session.Player is { } me && me.ClassId != d.ClassId) return false;
        if (Def.Option(key) is null) return true;
        var hidden = Options.Columns(key);
        for (int i = 0; i < hidden.Count; i++)
            if (SessionNames.ClassMatches(d.ClassName, hidden[i])) return false;
        return true;
    }
}

internal abstract class HudWidget
{
    public abstract void Draw(HudPainter p, HudView v);

    public virtual bool HasContent(HudView v) => true;

    public virtual bool Click(PointF at, HudView v) => false;

    protected static bool DrawWaiting(HudPainter p, HudView v, string? title = null)
    {
        if (v.Frame.Status == SimStatus.Connected) return false;
        p.Panel();
        var r = p.Bounds.Inflated(-10 * p.Density, -8 * p.Density);
        if (title is not null)
        {
            p.Text(title, p.Small, p.P.Faint, new RectangleF(r.X, r.Y, r.Width, p.LineHeight(p.Small) + 2), HAlign.Left, VAlign.Top);
            r = new RectangleF(r.X, r.Y + p.LineHeight(p.Small) + 2, r.Width, r.Height - p.LineHeight(p.Small) - 2);
        }
        string text = v.Frame.Status == SimStatus.Waiting ? "Waiting for a session" : "Waiting for iRacing";
        p.Text(text, p.Body, p.P.Muted, r, HAlign.Center, VAlign.Center);
        return true;
    }
}

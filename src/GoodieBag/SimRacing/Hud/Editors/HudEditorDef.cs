using System.Globalization;
using GoodieBag.Config;

namespace GoodieBag.SimRacing.Hud.Editors;

internal enum HudTab { Layout, Content }

internal abstract record HudSetting(string Id)
{
    public abstract IReadOnlyList<string> Keys { get; }
    public string? Label { get; init; }
    public string? Help { get; init; }
    public bool InlineHelp { get; init; }
    public Func<HudOptions, bool>? ShowIf { get; init; }
    public bool Visible(HudOptions o) => ShowIf?.Invoke(o) ?? true;
    public virtual bool Same(HudOptions a, HudOptions b) => Keys.All(k => a.Value(k) == b.Value(k));
}

internal sealed record OptionSetting(string Key) : HudSetting(Key)
{
    public override IReadOnlyList<string> Keys => new[] { Key };
}

internal sealed record PickerSetting(string Key, IReadOnlyList<HudCategory> Categories) : HudSetting(Key)
{
    public override IReadOnlyList<string> Keys => new[] { Key };
}

internal sealed record HudCategory(string Title, params string[] Ids);

internal sealed record LanesSetting(string Id, string Title, IReadOnlyList<(string Key, string Lane)> Lanes, IReadOnlyList<HudCategory> Categories) : HudSetting(Id)
{
    public override IReadOnlyList<string> Keys => Lanes.Select(l => l.Key).ToList();
}

internal sealed record TogglesSetting(string Id, string Title, IReadOnlyList<string> Toggles) : HudSetting(Id)
{
    public override IReadOnlyList<string> Keys => Toggles;
}

internal sealed record ClassFilterSetting() : HudSetting(HudClassFilter.ScopeKey)
{
    public override IReadOnlyList<string> Keys => new[] { HudClassFilter.ScopeKey, HudClassFilter.ListKey };

    public override bool Same(HudOptions a, HudOptions b) =>
        a.Value(HudClassFilter.ScopeKey) == b.Value(HudClassFilter.ScopeKey)
        && (a.Choice(HudClassFilter.ScopeKey) != HudClassFilter.Pick || a.Value(HudClassFilter.ListKey) == b.Value(HudClassFilter.ListKey));
}

internal sealed record MergedChoice(string Label, IReadOnlyDictionary<string, string> Values, string? Tip = null)
{
    public Func<HudOptions, IReadOnlyDictionary<string, string>>? Also { get; init; }

    public bool Matches(HudOptions o) => Values.All(v => o.Value(v.Key) == HudOptions.Normal(o.Option(v.Key), v.Value));
}

internal sealed record MergedSetting(string Id, string Title, IReadOnlyList<string> MergedKeys, IReadOnlyList<MergedChoice> Choices) : HudSetting(Id)
{
    public override IReadOnlyList<string> Keys => MergedKeys;

    public Func<HudOptions, MergedChoice?>? Extra { get; init; }

    public IReadOnlyList<MergedChoice> ChoicesFor(HudOptions o)
    {
        foreach (var c in Choices) if (c.Matches(o)) return Choices;
        return Extra?.Invoke(o) is { } extra ? Choices.Append(extra).ToList() : Choices;
    }

    public int Index(HudOptions o)
    {
        var list = ChoicesFor(o);
        for (int i = 0; i < list.Count; i++) if (list[i].Matches(o)) return i;
        return -1;
    }

    public override bool Same(HudOptions a, HudOptions b)
    {
        int i = Index(a);
        return i >= 0 && i == Index(b) && (i < Choices.Count || Keys.All(k => a.Value(k) == b.Value(k)));
    }
}

internal sealed record HudGroup(string Id, string? Title, HudTab Tab, bool Open, params HudSetting[] Settings);

internal sealed record HudPreset(string Name, IReadOnlyDictionary<string, string> Values)
{
    public const string Minimal = "Minimal", Standard = "Standard", Everything = "Everything";
}

internal sealed class HudEditorDef
{
    public required IReadOnlyList<HudGroup> Groups { get; init; }
    public IReadOnlyList<HudPreset> Presets { get; init; } = Array.Empty<HudPreset>();
    public IReadOnlyList<string> PresetKeeps { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Regions { get; init; } = new Dictionary<string, string>();
    public string ContentTitle { get; init; } = "Columns & info";

    public IEnumerable<HudSetting> Settings => Groups.SelectMany(g => g.Settings);

    public HudSetting? Setting(string id) => Settings.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public HudSetting? SettingForKey(string key) => Settings.FirstOrDefault(s => s.Keys.Contains(key, StringComparer.OrdinalIgnoreCase));

    public HudGroup? GroupOf(string settingId) => Groups.FirstOrDefault(g => g.Settings.Any(s => string.Equals(s.Id, settingId, StringComparison.OrdinalIgnoreCase)));

    public bool Has(HudTab tab) => Groups.Any(g => g.Tab == tab);

    public string? SettingForRegion(string region) =>
        Regions.TryGetValue(region, out var id) ? id : SettingForKey(region)?.Id;

    public IEnumerable<string> RegionsOf(string settingId)
    {
        foreach (var (region, id) in Regions) if (string.Equals(id, settingId, StringComparison.OrdinalIgnoreCase)) yield return region;
        if (Setting(settingId) is { } s) foreach (var k in s.Keys) yield return k;
    }

    public static string GroupFoldId(string kind, string group) => kind + ":" + group;

    private static HudWidgetConfig WithPreset(HudWidgetDef def, HudWidgetConfig w, HudPreset preset, IReadOnlyList<string> keeps)
    {
        var copy = w.Copy();
        Apply(def, copy, preset, keeps);
        return copy;
    }

    public void Apply(HudWidgetDef def, HudWidgetConfig w, HudPreset preset) => Apply(def, w, preset, PresetKeeps);

    private static void Apply(HudWidgetDef def, HudWidgetConfig w, HudPreset preset, IReadOnlyList<string> keeps)
    {
        foreach (var o in def.Options)
        {
            if (keeps.Contains(o.Key, StringComparer.OrdinalIgnoreCase)) continue;
            if (preset.Values.TryGetValue(o.Key, out var value) && value != o.Default) w.Options[o.Key] = value;
            else w.Options.Remove(o.Key);
        }
    }

    public string? PresetName(HudWidgetDef def, HudWidgetConfig w)
    {
        var mine = new HudOptions(def, w);
        foreach (var preset in Presets)
        {
            var theirs = new HudOptions(def, WithPreset(def, w, preset, PresetKeeps));
            bool same = Settings.Where(s => !s.Keys.All(k => PresetKeeps.Contains(k, StringComparer.OrdinalIgnoreCase)))
                .All(s => s.Visible(mine) == s.Visible(theirs) && (!s.Visible(mine) || s.Same(mine, theirs)));
            if (same) return preset.Name;
        }
        return null;
    }

    public static HudEditorDef Plain(HudWidgetDef def) => new()
    {
        Groups = new[] { new HudGroup("main", null, HudTab.Layout, true, def.Options.Select(o => (HudSetting)new OptionSetting(o.Key)).ToArray()) },
    };

    public static OptionSetting O(string key) => new(key);

    public static IReadOnlyDictionary<string, string> V(params (string Key, object Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value switch
        {
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.Value.ToString() ?? "",
        }, StringComparer.OrdinalIgnoreCase);
}

internal static class HudClassFilter
{
    public const string ScopeKey = "classes", ListKey = "showClasses", OldHideKey = "hideClasses";
    public const string All = "all", Mine = "mine", Pick = "pick";

    public static HudOption Scope { get; } = HudOption.Choice(ScopeKey, "Show", All,
        new HudChoice(All, "All classes"), new HudChoice(Mine, "Only mine"), new HudChoice(Pick, "Pick…"));

    public static HudOption List { get; } = HudOption.Classes(ListKey, "Classes shown", "Ticked classes show; the rest are still counted.");

    public static List<string> ShownFromHidden(IEnumerable<string> hidden, IEnumerable<string> known) =>
        known.Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(k => !hidden.Any(h => Race.SessionNames.ClassMatches(k, h)))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static void Migrate(HudWidgetConfig w, IEnumerable<string> known)
    {
        if (!w.Options.TryGetValue(OldHideKey, out var raw)) return;
        w.Options.Remove(OldHideKey);
        var hidden = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (hidden.Count == 0) return;
        if (w.Options.TryGetValue(ScopeKey, out var scope) && scope == Mine) return;
        w.Options[ScopeKey] = Pick;
        w.Options[ListKey] = string.Join(",", ShownFromHidden(hidden, known));
    }
}

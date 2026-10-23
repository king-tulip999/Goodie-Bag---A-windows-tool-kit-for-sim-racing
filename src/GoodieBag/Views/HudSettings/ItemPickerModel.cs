using GoodieBag.SimRacing.Hud;
using GoodieBag.SimRacing.Hud.Editors;

namespace GoodieBag.Views.HudSettings;

internal sealed class ItemPickerModel
{
    private readonly IReadOnlyList<HudChoice> _choices;

    public ItemPickerModel(IReadOnlyList<HudChoice> choices, IEnumerable<IEnumerable<string>> lanes)
    {
        _choices = choices;
        Lanes = lanes.Select(l => l.Where(Valid).Distinct().ToList()).ToList();
    }

    public List<List<string>> Lanes { get; }

    public bool Valid(string id) => _choices.Any(c => c.Id == id);

    public bool IsAdded(string id) => Lanes.Any(l => l.Contains(id));

    public HudChoice Choice(string id) => _choices.First(c => c.Id == id);

    public bool Add(string id, int lane = 0)
    {
        if (!Valid(id) || IsAdded(id) || lane < 0 || lane >= Lanes.Count) return false;
        Lanes[lane].Add(id);
        return true;
    }

    public bool Remove(int lane, string id) => lane >= 0 && lane < Lanes.Count && Lanes[lane].Remove(id);

    public bool Move(int fromLane, string id, int toLane, int index)
    {
        if (fromLane < 0 || fromLane >= Lanes.Count || toLane < 0 || toLane >= Lanes.Count) return false;
        var from = Lanes[fromLane];
        int at = from.IndexOf(id);
        if (at < 0) return false;
        var before = Values;
        from.RemoveAt(at);
        var to = Lanes[toLane];
        if (fromLane == toLane && index > at) index--;
        if (!to.Contains(id)) to.Insert(Math.Clamp(index, 0, to.Count), id);
        return !before.SequenceEqual(Values);
    }

    public void AddAll(int lane = 0)
    {
        foreach (var c in _choices) Add(c.Id, lane);
    }

    public void Clear()
    {
        foreach (var l in Lanes) l.Clear();
    }

    public void Reset(IReadOnlyList<string> values)
    {
        for (int i = 0; i < Lanes.Count && i < values.Count; i++)
            Lanes[i] = Split(values[i]).Where(Valid).Distinct().ToList();
    }

    public IReadOnlyList<string> Values => Lanes.Select(l => string.Join(",", l)).ToList();

    public static IEnumerable<string> Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Matches(HudChoice c, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.All(w => c.Label.Contains(w, StringComparison.OrdinalIgnoreCase) || (c.Help?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    public IReadOnlyList<(string? Title, IReadOnlyList<HudChoice> Items)> Grouped(IReadOnlyList<HudCategory> categories, string query)
    {
        var shown = _choices.Where(c => Matches(c, query)).ToList();
        if (categories.Count == 0) return shown.Count == 0 ? Array.Empty<(string?, IReadOnlyList<HudChoice>)>() : new[] { ((string?)null, (IReadOnlyList<HudChoice>)shown) };
        var groups = new List<(string?, IReadOnlyList<HudChoice>)>();
        foreach (var cat in categories)
        {
            var items = shown.Where(c => cat.Ids.Contains(c.Id)).ToList();
            if (items.Count > 0) groups.Add((cat.Title, items));
        }
        var rest = shown.Where(c => !categories.Any(cat => cat.Ids.Contains(c.Id))).ToList();
        if (rest.Count > 0) groups.Add(("Other", rest));
        return groups;
    }
}

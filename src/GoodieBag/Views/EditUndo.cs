using GoodieBag.Config;
using GoodieBag.Services;

namespace GoodieBag.Views;

internal interface ICancelEdit
{
    bool CancelEdit();
}

internal sealed class EditUndo
{
    private readonly List<Func<bool>> _restore = new();

    public EditUndo Items<T>(List<T> list)
    {
        var copy = list.ToList();
        _restore.Add(() =>
        {
            if (list.SequenceEqual(copy)) return false;
            list.Clear();
            list.AddRange(copy);
            return true;
        });
        return this;
    }

    public EditUndo Slots(List<PanelSlot> slots)
    {
        var copy = slots.Select(s => (Slot: s, s.Cols, s.Rows)).ToList();
        _restore.Add(() =>
        {
            var added = slots.Where(s => copy.All(c => c.Slot != s)).ToList();
            bool changed = !slots.Take(copy.Count).SequenceEqual(copy.Select(c => c.Slot)) || copy.Any(c => c.Slot.Cols != c.Cols || c.Slot.Rows != c.Rows);
            if (!changed) return false;
            slots.Clear();
            foreach (var (slot, cols, rows) in copy)
            {
                (slot.Cols, slot.Rows) = (cols, rows);
                slots.Add(slot);
            }
            slots.AddRange(added);
            return true;
        });
        return this;
    }

    public EditUndo Custom(Func<bool> restore)
    {
        _restore.Add(restore);
        return this;
    }

    public bool Revert()
    {
        bool changed = false;
        foreach (var r in _restore) changed |= r();
        return changed;
    }

    public bool Restore(AppServices app)
    {
        bool changed = Revert();
        if (changed)
        {
            app.Save();
            app.Notify("Changes discarded", "Esc put the layout back the way it was.");
        }
        return changed;
    }
}

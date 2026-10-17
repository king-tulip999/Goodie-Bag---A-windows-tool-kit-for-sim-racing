using static GoodieBag.Display.Ccd;

namespace GoodieBag.Display;

internal static class Duplicate
{
    public static (PathInfo[] Paths, ModeInfo[] Modes) Join(PathInfo[] active, ModeInfo[] modes, LUID adapter, uint target, LUID withAdapter, uint withTarget)
    {
        int t = Find(active, adapter, target);
        int w = Find(active, withAdapter, withTarget);
        if (t < 0 || w < 0)
            throw new InvalidOperationException("Both displays need to be on.");
        if (t == w)
            throw new InvalidOperationException("A display can't duplicate itself.");
        if (!active[t].TargetInfo.AdapterId.Equals(active[w].SourceInfo.AdapterId))
            throw new InvalidOperationException("Windows can only duplicate displays connected to the same graphics card.");
        if (SameSource(active[t], active[w]))
            return (active, modes);

        var rects = Sources(active, modes);
        var oldSource = Source(active[t]);
        bool wasAlone = active.Count(p => Source(p) == oldSource) == 1;

        var paths = (PathInfo[])active.Clone();
        var modeList = (ModeInfo[])modes.Clone();
        paths[t].SourceInfo = active[w].SourceInfo;

        if (wasAlone && rects.TryGetValue(oldSource, out var gone))
        {
            var primary = rects.FirstOrDefault(r => r.Value.X == 0 && r.Value.Y == 0).Key;
            if (primary == oldSource || primary == default)
                primary = Source(active[w]);
            Close(paths, modeList, gone, primary);
        }
        return Compact(paths, modeList);
    }

    public static (PathInfo[] Paths, ModeInfo[] Modes) Split(PathInfo[] active, ModeInfo[] modes, PathInfo[] all, LUID adapter, uint target)
    {
        int t = Find(active, adapter, target);
        if (t < 0)
            throw new InvalidOperationException("That display is off.");
        var shared = Source(active[t]);
        if (active.Count(p => Source(p) == shared) < 2)
            return (active, modes);

        var used = active.Select(Source).ToHashSet();
        int free = Array.FindIndex(all, p =>
            p.TargetInfo.AdapterId.Equals(adapter) && p.TargetInfo.Id == target && !used.Contains(Source(p)));
        if (free < 0)
            throw new InvalidOperationException("Windows has no free output to give that display its own desktop (the graphics card may already be driving its maximum number of desktops).");

        var paths = (PathInfo[])active.Clone();
        var modeList = modes.ToList();
        var rects = Sources(active, modes);
        int right = rects.Values.Select(r => r.X + r.W).DefaultIfEmpty(0).Max();
        var from = rects.TryGetValue(shared, out var r) ? r : (X: 0, Y: 0, W: 0, H: 0);
        uint pixelFormat = paths[t].SourceInfo.ModeInfoIdx < modes.Length ? modes[paths[t].SourceInfo.ModeInfoIdx].SourceMode.PixelFormat : 0;

        var source = all[free].SourceInfo;
        var mode = new ModeInfo { InfoType = MODE_INFO_TYPE_SOURCE, Id = source.Id, AdapterId = source.AdapterId };
        mode.SourceMode = new SourceMode { Width = (uint)from.W, Height = (uint)from.H, PixelFormat = pixelFormat, Position = new PointL { X = right, Y = from.Y } };
        source.ModeInfoIdx = from.W > 0 ? (uint)modeList.Count : DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        if (from.W > 0) modeList.Add(mode);
        source.StatusFlags = 0;
        paths[t].SourceInfo = source;
        return Compact(paths, modeList.ToArray());
    }

    public static (PathInfo[] Paths, ModeInfo[] Modes) Compact(PathInfo[] paths, ModeInfo[] modes)
    {
        var map = new Dictionary<uint, uint>();
        var kept = new List<ModeInfo>();
        uint Keep(uint idx)
        {
            if (idx == DISPLAYCONFIG_PATH_MODE_IDX_INVALID || idx >= modes.Length)
                return DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            if (!map.TryGetValue(idx, out uint now))
            {
                now = (uint)kept.Count;
                kept.Add(modes[idx]);
                map[idx] = now;
            }
            return now;
        }
        var result = (PathInfo[])paths.Clone();
        for (int i = 0; i < result.Length; i++)
        {
            result[i].SourceInfo.ModeInfoIdx = Keep(result[i].SourceInfo.ModeInfoIdx);
            result[i].TargetInfo.ModeInfoIdx = Keep(result[i].TargetInfo.ModeInfoIdx);
        }
        return (result, kept.ToArray());
    }

    public static bool SameSource(PathInfo a, PathInfo b) => Source(a) == Source(b);

    private static (LUID, uint) Source(PathInfo p) => (p.SourceInfo.AdapterId, p.SourceInfo.Id);

    private static int Find(PathInfo[] paths, LUID adapter, uint target) =>
        Array.FindIndex(paths, p => p.TargetInfo.AdapterId.Equals(adapter) && p.TargetInfo.Id == target);

    private static Dictionary<(LUID, uint), (int X, int Y, int W, int H)> Sources(PathInfo[] paths, ModeInfo[] modes)
    {
        var result = new Dictionary<(LUID, uint), (int X, int Y, int W, int H)>();
        foreach (var p in paths)
        {
            uint i = p.SourceInfo.ModeInfoIdx;
            if (i >= modes.Length || modes[i].InfoType != MODE_INFO_TYPE_SOURCE)
                continue;
            var s = modes[i].SourceMode;
            result.TryAdd(Source(p), (s.Position.X, s.Position.Y, (int)s.Width, (int)s.Height));
        }
        return result;
    }

    private static void Close(PathInfo[] paths, ModeInfo[] modes, (int X, int Y, int W, int H) gone, (LUID, uint) primary)
    {
        var indices = paths.Select(p => p.SourceInfo.ModeInfoIdx).Where(i => i < modes.Length && modes[i].InfoType == MODE_INFO_TYPE_SOURCE).Distinct().ToList();
        foreach (uint i in indices)
        {
            ref var pos = ref modes[i].SourceMode.Position;
            if (pos.X >= gone.X + gone.W) pos.X -= gone.W;
            if (pos.Y >= gone.Y + gone.H) pos.Y -= gone.H;
        }
        int origin = Array.FindIndex(paths, p => Source(p) == primary);
        if (origin < 0) return;
        uint pi = paths[origin].SourceInfo.ModeInfoIdx;
        if (pi >= modes.Length) return;
        var (dx, dy) = (modes[pi].SourceMode.Position.X, modes[pi].SourceMode.Position.Y);
        foreach (uint i in indices)
        {
            modes[i].SourceMode.Position.X -= dx;
            modes[i].SourceMode.Position.Y -= dy;
        }
    }
}

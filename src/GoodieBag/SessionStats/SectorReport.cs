namespace GoodieBag.SessionStats;

internal sealed record SectorLine(int Index, double Best, int BestLap, double Average, double Spread, int Laps)
{
    public double Lost => Average - Best;
}

internal static class SectorReport
{
    public static List<SectorLine> Make(IReadOnlyList<LapRow> laps)
    {
        var withSectors = laps.Where(l => l.Clean && l.Sectors is { Count: > 1 }).ToList();
        if (withSectors.Count == 0) return new();
        int count = withSectors.GroupBy(l => l.Sectors!.Count).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        var use = withSectors.Where(l => l.Sectors!.Count == count).ToList();
        var lines = new List<SectorLine>();
        for (int k = 0; k < count; k++)
        {
            var values = use.Select(l => (l.Lap, Time: l.Sectors![k])).ToList();
            var best = values.MinBy(v => v.Time);
            double average = values.Average(v => v.Time);
            double spread = Math.Sqrt(values.Sum(v => (v.Time - average) * (v.Time - average)) / values.Count);
            lines.Add(new SectorLine(k, best.Time, best.Lap, average, spread, values.Count));
        }
        return lines;
    }

    public static int? MostToFind(IReadOnlyList<SectorLine> lines) =>
        lines.Count > 1 && lines.Max(l => l.Lost) > 0.0005 ? lines.MaxBy(l => l.Lost)!.Index : null;
}

namespace GoodieBag.Config;

public sealed class SessionStatsSettings
{
    public bool Imperial { get; set; }

    public string? DriverNumber { get; set; }

    public string Range { get; set; } = "season";

    public string? TimelineCombo { get; set; }

    public long? TimelinePickedUtc { get; set; }

    public bool TraceRecord { get; set; } = true;

    public int TraceKeepDays { get; set; } = 365;

    public int TraceLapsPerSession { get; set; }

    public bool TraceKeepBests { get; set; } = true;

    public List<PanelSlot> PageLayout { get; set; } = new();

    public List<string>? AnalysisLanes { get; set; }

    public string ReplaySync { get; set; } = "distance";

    public double ReplaySpeed { get; set; } = 1;

    public double ReplayZoom { get; set; } = 0.8;

    public bool ReplayTurn { get; set; } = true;

    public bool ReplayShowReference { get; set; } = true;

    public bool ReplayFree { get; set; }

    public bool ReplayTips { get; set; } = true;

    public bool AnalysisWind { get; set; } = true;


    public static readonly int[] KeepDayChoices = { 30, 90, 182, 365, 730, 0 };
    public static readonly int[] LapChoices = { 0, 10, 3, 1 };
    public static readonly string[] Lanes = { "delta", "speed", "pedals", "steering", "gear", "rpm", "latg" };
    public static readonly string[] DefaultLanes = { "delta", "speed", "pedals", "steering", "gear" };

    public void Normalize()
    {
        if (!KeepDayChoices.Contains(TraceKeepDays)) TraceKeepDays = 365;
        if (!LapChoices.Contains(TraceLapsPerSession)) TraceLapsPerSession = 0;
        if (AnalysisLanes is not null) AnalysisLanes = AnalysisLanes.Where(l => Lanes.Contains(l)).Distinct().ToList();
        DriverNumber = string.IsNullOrWhiteSpace(DriverNumber) ? null : DriverNumber.Trim().TrimStart('#');
        if (Range is not ("week" or "month" or "season" or "all")) Range = "season";
        if (ReplaySync is not ("distance" or "time")) ReplaySync = "distance";
        if (!SessionStats.LapReplay.Speeds.Contains(ReplaySpeed)) ReplaySpeed = 1;
        ReplayZoom = double.IsFinite(ReplayZoom) ? Math.Clamp(ReplayZoom, 0, 1) : 0.8;
    }
}

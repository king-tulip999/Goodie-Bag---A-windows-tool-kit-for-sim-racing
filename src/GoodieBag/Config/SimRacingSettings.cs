namespace GoodieBag.Config;

public sealed class SimRacingSettings
{
    public bool Imperial { get; set; }

    public FovSetup Current { get; set; } = new();

    public List<FovSetup> Compare { get; set; } = new();

    public const int MaxCompare = 4;

    public static readonly string[] CompareColors = { "Slate", "Sage", "Gold", "Plum", "Coral", "Apricot", "Warn", "Danger" };

    public const string AccentColor = "Accent";

    public static IEnumerable<string> ColorChoices => CompareColors.Prepend(AccentColor);

    public int ColorVersion { get; set; }

    public void AssignCompareColors()
    {
        Current ??= new();
        Compare ??= new();
        Compare.RemoveAll(setup => setup is null);
        var used = new HashSet<string>(StringComparer.Ordinal);
        if (Current.Color is null || !ColorChoices.Contains(Current.Color))
            Current.Color = AccentColor;
        used.Add(Current.Color);
        foreach (var setup in Compare)
        {
            if (setup.Color is null || !ColorChoices.Contains(setup.Color) || used.Contains(setup.Color))
                setup.Color = CompareColors.FirstOrDefault(key => !used.Contains(key)) ?? CompareColors[0];
            used.Add(setup.Color);
        }
    }

    public List<int> CompareRows { get; set; } = new();

    public List<int> ValidCompareRows()
    {
        var rows = (CompareRows ?? new()).Where(i => i >= -1 && i < Compare.Count).Distinct().ToList();
        for (int i = -1; i < Compare.Count; i++)
            if (!rows.Contains(i)) rows.Add(i);
        CompareRows = rows;
        return rows;
    }

    public void RemoveCompare(int slot)
    {
        if (slot < 0 || slot >= Compare.Count) return;
        var rows = ValidCompareRows();
        Compare.RemoveAt(slot);
        CompareRows = rows.Where(x => x != slot).Select(x => x > slot ? x - 1 : x).ToList();
    }

    public void MakeCurrent(int slot)
    {
        if (slot < 0 || slot >= Compare.Count) return;
        var rows = ValidCompareRows();
        int a = rows.IndexOf(-1), b = rows.IndexOf(slot);
        (rows[a], rows[b]) = (slot, -1);
        (Compare[slot], Current) = (Current, Compare[slot]);
    }

    public void RemoveCurrent()
    {
        var rows = ValidCompareRows();
        int at = rows.IndexOf(-1);
        int? pick = at + 1 < rows.Count ? rows[at + 1] : at > 0 ? rows[at - 1] : (int?)null;
        if (pick is not { } slot)
        {
            Current = new FovSetup { Color = AccentColor };
            CompareRows = new() { -1 };
            return;
        }
        Current = Compare[slot];
        Compare.RemoveAt(slot);
        rows.RemoveAt(at);
        CompareRows = rows.Select(x => x == slot ? -1 : x > slot ? x - 1 : x).ToList();
    }

    public string? Sim { get; set; }

    public List<string> Collapsed { get; set; } = new();

    public IracingControllerSettings IracingController { get; set; } = new();

    public CompanionAppSettings TradingPaints { get; set; } = new();

    public CompanionAppSettings Garage61 { get; set; } = new();

    public SetupManagerSettings Setups { get; set; } = new();

    public RaceHudSettings Hud { get; set; } = new();

    public List<PanelSlot> PanelLayout { get; set; } = new();

    public int PanelLayoutVersion { get; set; }

    public void MigratePanelLayout()
    {
        PanelLayout ??= new();
        if (PanelLayoutVersion < 1)
        {
            var slot = PanelLayout.FirstOrDefault(s => string.Equals(s.Id, "setups", StringComparison.OrdinalIgnoreCase)) ?? new PanelSlot { Id = "setups" };
            PanelLayout.Remove(slot);
            if (PanelLayout.Count > 0) PanelLayout.Insert(0, slot);
        }
        PanelLayoutVersion = 2;
    }

    public List<string> HiddenPanels { get; set; } = new();

    public bool IsPanelHidden(string id) =>
        HiddenPanels?.Any(p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase)) == true;

    public void SetPanelHidden(string id, bool hidden)
    {
        HiddenPanels ??= new();
        HiddenPanels.RemoveAll(p => p is null || string.Equals(p, id, StringComparison.OrdinalIgnoreCase));
        if (hidden) HiddenPanels.Add(id);
    }
}

public sealed class IracingControllerSettings
{
    public string? KeybindProfile { get; set; }

    public string RendererFile { get; set; } = "rendererDX11Monitor.ini";

    public Dictionary<string, string> IgnoredValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> IgnoredFindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> SavedLayout { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> CollapsedSections { get; set; } = new();

    public bool IsCollapsed(string section) =>
        CollapsedSections?.Any(s => string.Equals(s, section, StringComparison.OrdinalIgnoreCase)) == true;

    public void SetCollapsed(string section, bool collapsed)
    {
        CollapsedSections ??= new();
        CollapsedSections.RemoveAll(s => s is null || string.Equals(s, section, StringComparison.OrdinalIgnoreCase));
        if (collapsed) CollapsedSections.Add(section);
    }
}

public sealed class SetupManagerSettings
{
    public List<string> Favorites { get; set; } = new();

    public List<string> HiddenCars { get; set; } = new();

    public List<string> ClassOrder { get; set; } = new();

    public Dictionary<string, SetupCarName> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string Sort { get; set; } = "class";

    public Dictionary<string, List<string>> Orders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> IgnoredDownloads { get; set; } = new();

    public string? SelectedCar { get; set; }

    public bool IntroSeen { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool InboxFolded { get; set; } = true;

    public List<string> ClassFilter { get; set; } = new();

    public List<string> HiddenClasses { get; set; } = new();

    public bool IsClassHidden(string cls) => Has(HiddenClasses, cls);

    public void SetClassHidden(string cls, bool hidden)
    {
        Set(HiddenClasses, cls, hidden);
        if (hidden) ClassFilter.RemoveAll(x => string.Equals(x, cls, StringComparison.OrdinalIgnoreCase));
    }

    public static bool Has(List<string>? list, string id) => list?.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)) == true;

    public static void Set(List<string> list, string id, bool on)
    {
        list.RemoveAll(x => x is null || string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        if (on) list.Add(id);
    }

    public void Normalize()
    {
        Favorites = (Favorites ?? new()).Where(x => x is not null).ToList();
        HiddenCars = (HiddenCars ?? new()).Where(x => x is not null).ToList();
        ClassOrder = (ClassOrder ?? new()).Where(x => x is not null).ToList();
        ClassFilter = (ClassFilter ?? new()).Where(x => x is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        HiddenClasses = (HiddenClasses ?? new()).Where(x => x is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ClassFilter.RemoveAll(IsClassHidden);
        IgnoredDownloads = (IgnoredDownloads ?? new()).Where(x => x is not null).ToList();
        var names = new Dictionary<string, SetupCarName>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dir, name) in Names ?? new())
            if (dir is not null && name is not null && !string.IsNullOrWhiteSpace(name.Name)) names[dir] = name;
        Names = names;
        var orders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, list) in Orders ?? new())
            if (key is not null && list is not null) orders[key] = list.Where(x => x is not null).ToList();
        Orders = orders;
        Sort ??= "class";
    }
}

public sealed class SetupCarName
{
    public string Name { get; set; } = "";
    public string? Class { get; set; }
}

public sealed class CompanionAppSettings
{
    public bool LaunchWithGoodieBag { get; set; }

    public bool LaunchWithIracing { get; set; }

    public string? CustomPath { get; set; }
}

public enum ScreenSizeMode { Diagonal, Measured }

public sealed class FovScreen
{
    public ScreenSizeMode SizeMode { get; set; } = ScreenSizeMode.Diagonal;

    public double DiagonalIn { get; set; } = 27;

    public double WidthCm { get; set; } = 59.8;
    public double HeightCm { get; set; } = 33.6;

    public string Aspect { get; set; } = "auto";
    public double CustomAspectW { get; set; } = 16;
    public double CustomAspectH { get; set; } = 9;

    public int ResolutionW { get; set; } = 2560;
    public int ResolutionH { get; set; } = 1440;

    public bool Curved { get; set; }

    public double RadiusMm { get; set; } = 1000;

    public FovScreen Copy() => (FovScreen)MemberwiseClone();
}

public sealed class FovSetup
{
    public string Name { get; set; } = "My rig";

    public string? Color { get; set; }
    public bool Triple { get; set; } = true;
    public FovScreen Centre { get; set; } = new();

    public bool MixedSides { get; set; }
    public FovScreen Side { get; set; } = new();

    public double DistanceCm { get; set; } = 70;

    public double BezelMm { get; set; } = 20;

    public bool AutoAngle { get; set; } = true;

    public double SideAngleDeg { get; set; } = 60;

    public FovSetup Copy()
    {
        var copy = (FovSetup)MemberwiseClone();
        copy.Centre = Centre.Copy();
        copy.Side = Side.Copy();
        return copy;
    }
}

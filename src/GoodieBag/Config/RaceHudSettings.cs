namespace GoodieBag.Config;

public enum HudShowWhen
{
    Driving,
    Connected,
    Always,
}

public enum HudUnits { Metric, Imperial }

public enum HudDensity { Compact, Standard, Expanded }

public enum HudAnchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

public enum HudColors { Default, Custom }

public sealed class RaceHudSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; }

    public bool Enabled { get; set; }

    public HudShowWhen ShowWhen { get; set; } = HudShowWhen.Driving;

    public bool Interactive { get; set; }

    public string ActiveLayout { get; set; } = SimRacing.Hud.HudPresets.DefaultName;

    public bool AutoSwitch { get; set; } = true;

    public List<HudLayout> Layouts { get; set; } = new();

    public List<HudLayoutRule> Rules { get; set; } = new();

    public HudAppearance Appearance { get; set; } = new();

    public HudUnits Units { get; set; } = HudUnits.Metric;

    public int GridSize { get; set; } = 8;

    public bool Snap { get; set; } = true;

    public HudFuelSettings Fuel { get; set; } = new();

    public Dictionary<string, HudAlertSetting> Alerts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> AlertGroupOrder { get; set; } = new();

    public List<string> AlertGroupsFolded { get; set; } = new();

    public List<string> FoldedPanels { get; set; } = new();

    public List<string> EditorSectionsToggled { get; set; } = new();

    public List<string> KnownClasses { get; set; } = new();
    public List<string> KnownCars { get; set; } = new();
    public List<string> KnownTracks { get; set; } = new();

    public const int MaxKnown = 200;

    public bool Remember(string? carClass, string? car, string? track)
    {
        static bool Add(List<string> list, string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || list.Count >= MaxKnown || list.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase)) return false;
            list.Add(name.Trim());
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return true;
        }
        bool a = Add(KnownClasses, carClass), b = Add(KnownCars, car), c = Add(KnownTracks, track);
        return a || b || c;
    }

    public static string SessionKind(string session) => string.Equals(session, "Warmup", StringComparison.OrdinalIgnoreCase) ? "Practice" : session;

    public static readonly string[] RemovedKinds = { "radar", "blindspot", "spotter", "h2h", "pit", "inputs", "flags", "dash", "trackmap" };

    public string? ToggleHotkey { get; set; }
    public string? EditHotkey { get; set; }
    public string? NextLayoutHotkey { get; set; }
    public string? PreviousLayoutHotkey { get; set; }
    public string? DismissHotkey { get; set; }
    public string? QuickEditHotkey { get; set; } = QuickEditDefault;
    public const string QuickEditDefault = "Ctrl+Alt+E";

    public HudLayout? Layout(string? name) => Layouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    public HudLayout? Resolve(string session, string car, string track, string carClass)
    {
        if (AutoSwitch)
        {
            HudLayoutRule? best = null;
            int bestScore = -1;
            foreach (var rule in Rules)
            {
                if (Layout(rule.Layout) is null || !rule.Matches(session, car, track, carClass)) continue;
                int score = rule.Specificity * 8 + rule.ExactFields(car, track, carClass);
                if (score > bestScore) { best = rule; bestScore = score; }
            }
            if (best is not null) return Layout(best.Layout);
        }
        return Layout(ActiveLayout) ?? Layouts.FirstOrDefault();
    }

    public bool RenameLayout(string from, string to)
    {
        to = to.Trim();
        var layout = Layout(from);
        if (layout is null || to.Length == 0 || IsDefaultName(layout.Name) || IsDefaultName(to)
            || Layouts.Any(l => l != layout && string.Equals(l.Name, to, StringComparison.OrdinalIgnoreCase))) return false;
        foreach (var rule in Rules.Where(r => string.Equals(r.Layout, layout.Name, StringComparison.OrdinalIgnoreCase))) rule.Layout = to;
        if (string.Equals(ActiveLayout, layout.Name, StringComparison.OrdinalIgnoreCase)) ActiveLayout = to;
        layout.Name = to;
        return true;
    }

    public bool DeleteLayout(string name)
    {
        var layout = Layout(name);
        if (layout is null || Layouts.Count <= 1 || IsDefaultName(layout.Name)) return false;
        Layouts.Remove(layout);
        Rules.RemoveAll(r => string.Equals(r.Layout, layout.Name, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(ActiveLayout, layout.Name, StringComparison.OrdinalIgnoreCase)) ActiveLayout = Layouts[0].Name;
        return true;
    }

    public static bool IsDefaultName(string name) => string.Equals(name, SimRacing.Hud.HudPresets.DefaultName, StringComparison.OrdinalIgnoreCase);

    public string? MarkCustomized(Func<HudLayout, bool> isDefault, Func<HudLayout> preset)
    {
        var layout = Layouts.FirstOrDefault(l => IsDefaultName(l.Name));
        if (layout is null || isDefault(layout)) return null;
        var custom = new HudLayout { Name = UniqueName(SimRacing.Hud.HudPresets.CustomName), Widgets = layout.Widgets };
        layout.Widgets = preset().Widgets.Select(w => w.Copy()).ToList();
        foreach (var w in layout.Widgets) w.Clamp();
        Layouts.Insert(Layouts.IndexOf(layout) + 1, custom);
        if (IsDefaultName(ActiveLayout)) ActiveLayout = custom.Name;
        return custom.Name;
    }

    public string ResetToDefault(string name, HudLayout preset)
    {
        var layout = Layout(name);
        if (layout is null) return name;
        layout.Widgets = preset.Widgets.Select(w => w.Copy()).ToList();
        foreach (var w in layout.Widgets) w.Clamp();
        return layout.Name;
    }

    public string UniqueName(string wanted)
    {
        wanted = string.IsNullOrWhiteSpace(wanted) ? "Layout" : wanted.Trim();
        string name = wanted;
        for (int n = 2; Layout(name) is not null; n++) name = $"{wanted} {n}";
        return name;
    }

    public void Normalize(Func<List<HudLayout>> defaults, Func<List<HudLayoutRule>> defaultRules)
    {
        Layouts ??= new();
        Rules ??= new();
        if (Version < 1 && Layouts.Count == 0)
        {
            Layouts.AddRange(defaults());
            Rules.AddRange(defaultRules());
        }
        Version = CurrentVersion;
        Appearance ??= new();
        var builtIn = defaults();
        if (!Layouts.Any(l => l is not null && IsDefaultName(l.Name ?? ""))) Layouts.Add(builtIn[0]);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in Layouts.ToList())
        {
            layout.Name = string.IsNullOrWhiteSpace(layout.Name) ? "Layout" : layout.Name.Trim();
            if (!names.Add(layout.Name)) { layout.Name = UniqueNameAmong(names, layout.Name); names.Add(layout.Name); }
            layout.Widgets ??= new();
            layout.Widgets.RemoveAll(w => w is null || RemovedKinds.Contains(w.Kind, StringComparer.OrdinalIgnoreCase));
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in layout.Widgets)
            {
                w.Kind ??= "";
                if (string.IsNullOrWhiteSpace(w.Id) || !ids.Add(w.Id)) { w.Id = HudWidgetConfig.NewId(w.Kind); ids.Add(w.Id); }
                SideBannerToTop(w);
                w.Clamp();
            }
        }
        Rules.RemoveAll(r => r is null || Layout(r.Layout) is null);
        foreach (var r in Rules) if (r.Session is not null) r.Session = SessionKind(r.Session);
        Rules = Rules.DistinctBy(r => (r.Layout.ToLowerInvariant(), r.Session?.ToLowerInvariant(), r.Class?.ToLowerInvariant(), r.Car?.ToLowerInvariant(), r.Track?.ToLowerInvariant())).ToList();
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Rules)
            if (string.IsNullOrWhiteSpace(r.Id) || !ruleIds.Add(r.Id)) { r.Id = Guid.NewGuid().ToString("N"); ruleIds.Add(r.Id); }
        AlertGroupOrder = (AlertGroupOrder ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AlertGroupsFolded = (AlertGroupsFolded ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        FoldedPanels = (FoldedPanels ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        EditorSectionsToggled = (EditorSectionsToggled ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (Layout(ActiveLayout) is null) ActiveLayout = Layouts[0].Name;
        Appearance ??= new();
        Appearance.Clamp();
        Fuel ??= new();
        Fuel.Clamp();
        GridSize = Math.Clamp(GridSize, 2, 64);
        var alerts = new Dictionary<string, HudAlertSetting>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, a) in Alerts ?? new()) if (id is not null && a is not null) alerts[id] = a;
        Alerts = alerts;
        static List<string> Clean(List<string>? list) =>
            (list ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(MaxKnown).ToList();
        KnownClasses = Clean(KnownClasses);
        KnownCars = Clean(KnownCars);
        KnownTracks = Clean(KnownTracks);
    }

    internal static void SideBannerToTop(HudWidgetConfig w)
    {
        if (w.Options is null || !w.Options.TryGetValue(SimRacing.Hud.HudFlags.BannerOption.Key, out var where) || where != SimRacing.Hud.HudFlags.OldSide) return;
        var old = SimRacing.Hud.HudFlags.OldSideRoom;
        var (l, t, r, b) = SimRacing.Hud.HudFlags.Room("top");
        w.Options[SimRacing.Hud.HudFlags.BannerOption.Key] = "top";
        w.W = Math.Max(1, w.W + l + r - old.L - old.R);
        w.H = Math.Max(1, w.H + t + b - old.T - old.B);
    }

    private static string UniqueNameAmong(HashSet<string> taken, string wanted)
    {
        string name = wanted;
        for (int n = 2; taken.Contains(name); n++) name = $"{wanted} {n}";
        return name;
    }
}

public sealed class HudLayout
{
    public string Name { get; set; } = "Layout";
    public List<HudWidgetConfig> Widgets { get; set; } = new();

    public HudLayout Copy(string name) => new()
    {
        Name = name,
        Widgets = Widgets.Select(w => w.Copy()).ToList(),
    };
}

public sealed class HudWidgetConfig
{
    public const double MinSize = 40, MaxSize = 4000;

    public string Id { get; set; } = "";

    public string Kind { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string Monitor { get; set; } = "primary";

    public HudAnchor Anchor { get; set; } = HudAnchor.TopLeft;

    public double X { get; set; }
    public double Y { get; set; }

    public double W { get; set; } = 300;
    public double H { get; set; } = 200;

    public bool Locked { get; set; }

    public int Opacity { get; set; } = 100;

    public int UpdateHz { get; set; }

    public List<string> Sessions { get; set; } = new();

    public bool HideInReplay { get; set; } = true;

    public bool? OnlyWhenNeeded { get; set; }

    public bool OnlyOnTrack { get; set; }

    public int? Background { get; set; }

    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string NewId(string kind) => $"{(string.IsNullOrEmpty(kind) ? "widget" : kind)}-{Guid.NewGuid().ToString("N")[..6]}";

    public void Clamp()
    {
        W = double.IsFinite(W) ? Math.Clamp(W, MinSize, MaxSize) : 300;
        H = double.IsFinite(H) ? Math.Clamp(H, MinSize, MaxSize) : 200;
        X = double.IsFinite(X) ? Math.Clamp(X, -MaxSize, MaxSize) : 0;
        Y = double.IsFinite(Y) ? Math.Clamp(Y, -MaxSize, MaxSize) : 0;
        Opacity = Math.Clamp(Opacity, 20, 100);
        if (Background is { } bg) Background = Math.Clamp(bg, 0, 100);
        UpdateHz = Math.Clamp(UpdateHz, 0, 60);
        Monitor = string.IsNullOrWhiteSpace(Monitor) ? "primary" : Monitor;
        Sessions = (Sessions ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(RaceHudSettings.SessionKind).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in Options ?? new()) if (k is not null && v is not null) options[k] = v;
        Options = options;
    }

    public HudWidgetConfig Copy()
    {
        var copy = (HudWidgetConfig)MemberwiseClone();
        copy.Sessions = new List<string>(Sessions);
        copy.Options = new Dictionary<string, string>(Options, StringComparer.OrdinalIgnoreCase);
        return copy;
    }

    public bool ShowsIn(string session) => Sessions.Count == 0 || Sessions.Any(s => string.Equals(s, session, StringComparison.OrdinalIgnoreCase));
}

public sealed class HudLayoutRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Layout { get; set; } = "";
    public string? Session { get; set; }
    public string? Car { get; set; }
    public string? Track { get; set; }
    public string? Class { get; set; }

    public int Specificity => (Session is null ? 0 : 1) + (Car is null ? 0 : 1) + (Track is null ? 0 : 1) + (Class is null ? 0 : 1);

    public int ExactFields(string car, string track, string carClass) =>
        (Same(Car, car) ? 1 : 0) + (Same(Track, track) ? 1 : 0) + (Same(Class, carClass) ? 1 : 0);

    private static bool Same(string? want, string actual) =>
        !string.IsNullOrWhiteSpace(want) && string.Equals(want.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);

    public bool Matches(string session, string car, string track, string carClass) =>
        Has(Session, session, exact: true) && Has(Car, car) && Has(Track, track) && Has(Class, carClass);

    private static bool Has(string? want, string actual, bool exact = false) =>
        string.IsNullOrWhiteSpace(want) || (exact ? string.Equals(want.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase) : actual.Contains(want.Trim(), StringComparison.OrdinalIgnoreCase));

    public string Describe()
    {
        var parts = new List<string>();
        if (Session is not null) parts.Add(Session + " sessions");
        if (Class is not null) parts.Add("class " + Class);
        if (Car is not null) parts.Add(Car);
        if (Track is not null) parts.Add("at " + Track);
        return parts.Count == 0 ? "Every session" : string.Join(", ", parts);
    }
}

public sealed class HudAppearance
{
    public HudColors Colors { get; set; } = HudColors.Default;

    public HudCustomColors Custom { get; set; } = new();

    public int Background { get; set; } = 82;

    public int FontScale { get; set; } = 100;

    public HudDensity Density { get; set; } = HudDensity.Compact;

    public int Corner { get; set; } = 6;

    public bool ReducedMotion { get; set; }

    public bool Outline { get; set; } = true;

    public void Clamp()
    {
        Custom ??= new();
        Custom.Clean();
        Background = Math.Clamp(Background, 0, 100);
        FontScale = 100;
        Density = HudDensity.Compact;
        ReducedMotion = false;
        Corner = Math.Clamp(Corner, 0, 24);
    }
}

public enum FuelSafety { Aggressive, Balanced, Conservative }

public sealed class HudFuelSettings
{
    public int Window { get; set; } = 10;

    public FuelSafety Safety { get; set; } = FuelSafety.Balanced;

    public double MarginLaps { get; set; } = 0.5;

    public double ReserveLiters { get; set; }

    public double ExtraPercent { get; set; }

    public double ExtraKm { get; set; }

    public double MaxStintMinutes { get; set; }

    public double PitLaneLossSeconds { get; set; } = 25;

    public void Clamp()
    {
        Window = Math.Clamp(Window, 1, 30);
        MarginLaps = double.IsFinite(MarginLaps) ? Math.Clamp(MarginLaps, 0, 10) : 0.5;
        ReserveLiters = double.IsFinite(ReserveLiters) ? Math.Clamp(ReserveLiters, 0, 50) : 0;
        ExtraPercent = double.IsFinite(ExtraPercent) ? Math.Clamp(ExtraPercent, 0, 25) : 0;
        ExtraKm = double.IsFinite(ExtraKm) ? Math.Clamp(ExtraKm, 0, 50) : 0;
        MaxStintMinutes = double.IsFinite(MaxStintMinutes) ? Math.Clamp(MaxStintMinutes, 0, 600) : 0;
        PitLaneLossSeconds = double.IsFinite(PitLaneLossSeconds) ? Math.Clamp(PitLaneLossSeconds, 0, 300) : 25;
    }
}

public sealed class HudAlertSetting
{
    public bool? Enabled { get; set; }
    public double? Cooldown { get; set; }
    public double? Threshold { get; set; }
    public double? Duration { get; set; }
}

public sealed class HudCustomColors
{
    public string Background { get; set; } = "#0A0A0B";
    public string Surface { get; set; } = "#1C1C1F";
    public string Text { get; set; } = "#F2F2F3";
    public string Heading { get; set; } = "#FFFFFF";
    public string Muted { get; set; } = "#9C9CA2";
    public string Lines { get; set; } = "#45454B";
    public string Accent { get; set; } = "#3B82F6";
    public string YourRow { get; set; } = "#EAB308";
    public string YourRowText { get; set; } = "#FCD34D";
    public string Good { get; set; } = "#22C55E";
    public string Warn { get; set; } = "#F59E0B";
    public string Danger { get; set; } = "#EF4444";
    public string FastestLap { get; set; } = "#B06BFF";
    public string Lapping { get; set; } = "#F87171";
    public string Lapped { get; set; } = "#60A5FA";
    public string MapTrack { get; set; } = "#535A68";
    public string MapEdge { get; set; } = "#8B919C";
    public string MapPit { get; set; } = "#3C4353";
    public string MapCars { get; set; } = "#CFD4DA";

    public IEnumerable<(string Id, Func<string> Get, Action<string> Set)> All()
    {
        yield return (nameof(Background), () => Background, v => Background = v);
        yield return (nameof(Surface), () => Surface, v => Surface = v);
        yield return (nameof(Text), () => Text, v => Text = v);
        yield return (nameof(Heading), () => Heading, v => Heading = v);
        yield return (nameof(Muted), () => Muted, v => Muted = v);
        yield return (nameof(Lines), () => Lines, v => Lines = v);
        yield return (nameof(Accent), () => Accent, v => Accent = v);
        yield return (nameof(YourRow), () => YourRow, v => YourRow = v);
        yield return (nameof(YourRowText), () => YourRowText, v => YourRowText = v);
        yield return (nameof(Good), () => Good, v => Good = v);
        yield return (nameof(Warn), () => Warn, v => Warn = v);
        yield return (nameof(Danger), () => Danger, v => Danger = v);
        yield return (nameof(FastestLap), () => FastestLap, v => FastestLap = v);
        yield return (nameof(Lapping), () => Lapping, v => Lapping = v);
        yield return (nameof(Lapped), () => Lapped, v => Lapped = v);
        yield return (nameof(MapTrack), () => MapTrack, v => MapTrack = v);
        yield return (nameof(MapEdge), () => MapEdge, v => MapEdge = v);
        yield return (nameof(MapPit), () => MapPit, v => MapPit = v);
        yield return (nameof(MapCars), () => MapCars, v => MapCars = v);
    }

    public void Clean()
    {
        var defaults = new HudCustomColors().All().ToDictionary(e => e.Id, e => e.Get());
        static bool Valid(string? v) => v is { Length: 7 } && v[0] == '#' && v.Skip(1).All(Uri.IsHexDigit);
        foreach (var (id, get, set) in All())
            set(Valid(get()) ? get().ToUpperInvariant() : defaults[id]);
    }
}

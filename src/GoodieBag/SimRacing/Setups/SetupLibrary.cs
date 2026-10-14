using System.IO;
using GoodieBag.Config;

namespace GoodieBag.SimRacing.Setups;

internal sealed class SetupNode
{
    public SetupNode(SetupCar car, string name, bool isFolder, SetupNode? parent)
    {
        Car = car;
        Name = name;
        IsFolder = isFolder;
        Parent = parent;
    }

    public SetupCar Car { get; }
    public string Name { get; set; }
    public bool IsFolder { get; }
    public SetupNode? Parent { get; set; }
    public List<SetupNode> Children { get; } = new();
    public long Size { get; set; }
    public DateTime Modified { get; set; }

    public bool IsRoot => Parent is null;

    public IEnumerable<string> Segments()
    {
        var names = new List<string>();
        for (var n = this; n.Parent is not null; n = n.Parent) names.Add(n.Name);
        names.Reverse();
        return names;
    }

    public string Key => string.Join("/", Segments().Prepend(Car.Dir));

    public int Files => IsFolder ? Children.Sum(c => c.Files) : 1;
    public int Folders => Children.Sum(c => c.IsFolder ? 1 + c.Folders : 0);
    public long TotalSize => IsFolder ? Children.Sum(c => c.TotalSize) : Size;
    public DateTime Latest => IsFolder ? Children.Select(c => c.Latest).DefaultIfEmpty(DateTime.MinValue).Max() : Modified;
    public int NewCount(DateTime since) => IsFolder ? Children.Sum(c => c.NewCount(since)) : Modified >= since ? 1 : 0;
    public bool IsAncestorOf(SetupNode other) { for (var p = other.Parent; p is not null; p = p.Parent) if (p == this) return true; return false; }

    public IEnumerable<SetupNode> FilesBelow() => IsFolder ? Children.SelectMany(c => c.FilesBelow()) : new[] { this };

    public SetupNode? Find(string key) =>
        string.Equals(Key, key, StringComparison.OrdinalIgnoreCase) ? this : Children.Select(c => c.Find(key)).FirstOrDefault(f => f is not null);
}

internal sealed class SetupCar
{
    public SetupCar(string dir)
    {
        Dir = dir;
        Root = new SetupNode(this, "", true, null);
    }

    public string Dir { get; }
    public string Name { get; set; } = "";
    public string Class { get; set; } = SetupCars.Other;
    public bool Known { get; set; }
    public bool Custom { get; set; }
    public SetupNode Root { get; set; }

    public void Describe(SetupManagerSettings settings) => (Name, Class, Known, Custom) = SetupCars.Describe(Dir, settings);
}

internal static class SetupCars
{
    public const string Other = "Other";

    public static readonly string[] Classes = { "GTP", "LMP2", "LMP3", "Prototype", "GTE", "GT3", "GT4", "TCR", "Touring", "Sports Car", "Formula", "NASCAR", "Oval", "Dirt", "Rallycross", "Off-road", "Classic", Other };

    private static readonly Dictionary<string, (string Name, string Class)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["acuraarx06gtp"] = ("Acura ARX-06 GTP", "GTP"), ["amvalkyriegtp"] = ("Aston Martin Valkyrie", "GTP"), ["bmwlmdh"] = ("BMW M Hybrid V8", "GTP"),
        ["cadillacvseriesrgtp"] = ("Cadillac V-Series.R GTP", "GTP"), ["ferrari499p"] = ("Ferrari 499P", "GTP"), ["porsche963gtp"] = ("Porsche 963 GTP", "GTP"),
        ["dallarap217"] = ("Dallara P217 LMP2", "LMP2"),
        ["ligierjsp320"] = ("Ligier JS P320", "LMP3"),
        ["audir18"] = ("Audi R18", "Prototype"), ["porsche919"] = ("Porsche 919", "Prototype"), ["hpdarx01c"] = ("HPD ARX-01c", "Prototype"),
        ["rileydp"] = ("Riley MkXX Daytona Prototype", "Prototype"), ["c7vettedp"] = ("Chevrolet Corvette C7 Daytona Prototype", "Prototype"), ["radical sr8"] = ("Radical SR8", "Prototype"),
        ["radicalsr10"] = ("Radical SR10", "Prototype"),
        ["bmwm8gte"] = ("BMW M8 GTE", "GTE"), ["c8rvettegte"] = ("Chevrolet Corvette C8.R GTE", "GTE"), ["ferrari488gte"] = ("Ferrari 488 GTE", "GTE"),
        ["fordgt2017"] = ("Ford GT GTE", "GTE"), ["porsche991rsr"] = ("Porsche 911 RSR", "GTE"),
        ["acuransxevo22gt3"] = ("Acura NSX GT3 EVO 22", "GT3"), ["amvantageevogt3"] = ("Aston Martin Vantage GT3 EVO", "GT3"), ["audir8gt3"] = ("Audi R8 LMS GT3", "GT3"),
        ["audir8lmsevo2gt3"] = ("Audi R8 LMS EVO II GT3", "GT3"), ["bmwm4gt3"] = ("BMW M4 GT3", "GT3"), ["bmwz4gt3"] = ("BMW Z4 GT3", "GT3"),
        ["chevyvettez06rgt3"] = ("Chevrolet Corvette Z06 GT3.R", "GT3"), ["ferrari296gt3"] = ("Ferrari 296 GT3", "GT3"), ["ferrari488gt3"] = ("Ferrari 488 GT3", "GT3"),
        ["ferrarievogt3"] = ("Ferrari 488 GT3 Evo 2020", "GT3"), ["fordgt gt3"] = ("Ford GT GT3", "GT3"), ["fordmustanggt3"] = ("Ford Mustang GT3", "GT3"),
        ["lamborghinievogt3"] = ("Lamborghini Huracán GT3 EVO", "GT3"), ["mclaren720sgt3"] = ("McLaren 720S GT3 EVO", "GT3"), ["mclarenmp4"] = ("McLaren MP4-12C GT3", "GT3"),
        ["mercedesamgevogt3"] = ("Mercedes-AMG GT3 2020", "GT3"), ["mercedesamggt3"] = ("Mercedes-AMG GT3", "GT3"), ["porsche911rgt3"] = ("Porsche 911 GT3 R (991)", "GT3"),
        ["porsche992rgt3"] = ("Porsche 911 GT3 R (992)", "GT3"),
        ["amvantagegt4"] = ("Aston Martin Vantage GT4", "GT4"), ["bmwm4evogt4"] = ("BMW M4 G82 GT4 EVO", "GT4"), ["bmwm4gt4"] = ("BMW M4 GT4", "GT4"),
        ["fordmustanggt4"] = ("Ford Mustang GT4", "GT4"), ["mclaren570sgt4"] = ("McLaren 570S GT4", "GT4"), ["mercedesamggt4"] = ("Mercedes-AMG GT4", "GT4"),
        ["porsche718gt4"] = ("Porsche 718 Cayman GT4 Clubsport MR", "GT4"),
        ["audirs3lms"] = ("Audi RS 3 LMS TCR", "TCR"), ["audirs3lmsgen2"] = ("Audi RS 3 LMS TCR (Gen 2)", "TCR"), ["hondacivictyper"] = ("Honda Civic Type R TCR", "TCR"),
        ["hyundaielantracn7"] = ("Hyundai Elantra N TCR", "TCR"), ["hyundaivelostern"] = ("Hyundai Veloster N TCR", "TCR"),
        ["fordv8sc"] = ("Ford Falcon FG V8 Supercar", "Touring"), ["supercars chevycamarogen3"] = ("Supercars Chevrolet Camaro Gen3", "Touring"), ["supercars fordmustanggen3"] = ("Supercars Ford Mustang Gen3", "Touring"),
        ["v8supercars ford2014"] = ("V8 Supercars Ford Falcon (2014)", "Touring"), ["v8supercars fordmustanggt"] = ("Supercars Ford Mustang GT", "Touring"), ["v8supercars holden2014"] = ("V8 Supercars Holden Commodore (2014)", "Touring"),
        ["v8supercars holden2019"] = ("Supercars Holden ZB Commodore", "Touring"), ["stockcarbrasil corolla"] = ("Stock Car Brasil Toyota Corolla", "Touring"), ["stockcarbrasil cruze"] = ("Stock Car Brasil Chevrolet Cruze", "Touring"),
        ["kiaoptima"] = ("Kia Optima", "Touring"), ["cadillacctsvr"] = ("Cadillac CTS-V Racecar", "Touring"), ["fr500s"] = ("Ford Mustang FR500S", "Touring"),
        ["bmwm2csr"] = ("BMW M2 CS Racing", "Sports Car"), ["bmwm2g87"] = ("BMW M2 (G87)", "Sports Car"), ["ferrari296challenge"] = ("Ferrari 296 Challenge", "Sports Car"),
        ["porsche911cup"] = ("Porsche 911 GT3 Cup (991)", "Sports Car"), ["porsche992cup"] = ("Porsche 911 GT3 Cup (992)", "Sports Car"), ["porsche9922cup"] = ("Porsche 911 GT3 Cup (992.2)", "Sports Car"),
        ["porschemissionr"] = ("Porsche Mission R", "Sports Car"), ["mx5 cup"] = ("Mazda MX-5 Cup (2015)", "Sports Car"), ["mx5 mx52016"] = ("Global Mazda MX-5 Cup", "Sports Car"),
        ["mx5 roadster"] = ("Mazda MX-5 Roadster", "Sports Car"), ["toyotagr86"] = ("Toyota GR86", "Sports Car"), ["solstice"] = ("Pontiac Solstice", "Sports Car"),
        ["solstice rookie"] = ("Pontiac Solstice (Rookie)", "Sports Car"), ["specracer"] = ("Spec Racer Ford", "Sports Car"), ["caterham caterham420r"] = ("Caterham 420R", "Sports Car"),
        ["caterham caterhamacademy"] = ("Caterham Academy", "Sports Car"), ["rufrt12r awd"] = ("RUF RT 12R AWD", "Sports Car"), ["rufrt12r rwd"] = ("RUF RT 12R RWD", "Sports Car"),
        ["rufrt12r track"] = ("RUF RT 12R Track", "Sports Car"), ["rufrt12r track cspec"] = ("RUF RT 12R C-Spec", "Sports Car"), ["jettatdi"] = ("VW Jetta TDI Cup", "Sports Car"),
        ["renaultcliocup"] = ("Renault Clio R.S. V Cup", "Sports Car"),
        ["dallara"] = ("Dallara IR-05", "Formula"), ["dallaradw12"] = ("Dallara DW12 IndyCar", "Formula"), ["dallaraf3"] = ("Dallara F3", "Formula"),
        ["dallarail15"] = ("Dallara IL-15 Indy Lights", "Formula"), ["dallarair01"] = ("Dallara iR-01", "Formula"), ["dallarair18"] = ("Dallara IR18 IndyCar", "Formula"),
        ["formulair04"] = ("FIA F4", "Formula"), ["formulamazda"] = ("Formula Mazda", "Formula"), ["formularenault20"] = ("Formula Renault 2.0", "Formula"),
        ["formularenault35"] = ("Formula Renault 3.5", "Formula"), ["formulavee"] = ("Formula Vee", "Formula"), ["formulavee formulavee2"] = ("Formula Vee (2)", "Formula"),
        ["formulavee formulavee3"] = ("Formula Vee (3)", "Formula"), ["indypropm18"] = ("Indy Pro 2000 PM-18", "Formula"), ["lotus49"] = ("Lotus 49", "Formula"),
        ["lotus79"] = ("Lotus 79", "Formula"), ["mclarenmp430"] = ("McLaren MP4-30", "Formula"), ["mercedesw12"] = ("Mercedes-AMG W12 E Performance", "Formula"),
        ["mercedesw13"] = ("Mercedes-AMG W13 E Performance", "Formula"), ["raygr22"] = ("Ray GR22 FF1600", "Formula"), ["rt2000"] = ("Skip Barber RT2000", "Formula"),
        ["superformulalights324"] = ("Super Formula Lights", "Formula"), ["superformulasf23 honda"] = ("Super Formula SF23 (Honda)", "Formula"), ["superformulasf23 toyota"] = ("Super Formula SF23 (Toyota)", "Formula"),
        ["usf2000usf17"] = ("USF 2000", "Formula"), ["williamsfw31"] = ("Williams FW31", "Formula"),
        ["euronascar"] = ("NASCAR Euro Series", "NASCAR"), ["stockcars buicklesabre87"] = ("NASCAR Buick LeSabre (1987)", "NASCAR"), ["stockcars camarozl12018"] = ("NASCAR Cup Chevrolet Camaro ZL1 (2018)", "NASCAR"),
        ["stockcars chevycamarozl12022"] = ("NASCAR Cup Chevrolet Camaro ZL1", "NASCAR"), ["stockcars chevymontecarlo03"] = ("NASCAR Chevrolet Monte Carlo (2003)", "NASCAR"), ["stockcars chevymontecarlo87"] = ("NASCAR Chevrolet Monte Carlo (1987)", "NASCAR"),
        ["stockcars chevyss"] = ("NASCAR Cup Chevrolet SS", "NASCAR"), ["stockcars fordfusion"] = ("NASCAR Cup Ford Fusion", "NASCAR"), ["stockcars fordmustang2019"] = ("NASCAR Cup Ford Mustang (2019)", "NASCAR"),
        ["stockcars fordmustang2022"] = ("NASCAR Cup Ford Mustang", "NASCAR"), ["stockcars fordtaurus03"] = ("NASCAR Ford Taurus (2003)", "NASCAR"), ["stockcars fordthunderbird87"] = ("NASCAR Ford Thunderbird (1987)", "NASCAR"),
        ["stockcars impala"] = ("NASCAR Chevrolet Impala COT", "NASCAR"), ["stockcars pontiacgrandprix87"] = ("NASCAR Pontiac Grand Prix (1987)", "NASCAR"), ["stockcars toyotacamry"] = ("NASCAR Cup Toyota Camry (2019)", "NASCAR"),
        ["stockcars toyotacamry2022"] = ("NASCAR Cup Toyota Camry", "NASCAR"), ["stockcars2 arcachevy25"] = ("ARCA Chevrolet", "NASCAR"), ["stockcars2 arcaford25"] = ("ARCA Ford", "NASCAR"),
        ["stockcars2 arcatoyota25"] = ("ARCA Toyota", "NASCAR"), ["stockcars2 camaro2019"] = ("NASCAR Xfinity Chevrolet Camaro", "NASCAR"), ["stockcars2 camry2015"] = ("NASCAR Xfinity Toyota Camry (2015)", "NASCAR"),
        ["stockcars2 chevy"] = ("NASCAR Class B Chevrolet Impala", "NASCAR"), ["stockcars2 chevy cot"] = ("NASCAR Chevrolet Impala COT (Class B)", "NASCAR"), ["stockcars2 chevy gen4cup"] = ("NASCAR Gen 4 Cup Chevrolet Monte Carlo", "NASCAR"),
        ["stockcars2 chevy national"] = ("NASCAR Nationwide Chevrolet Impala", "NASCAR"), ["stockcars2 mustang2019"] = ("NASCAR Xfinity Ford Mustang", "NASCAR"), ["stockcars2 nwcamaro2014"] = ("NASCAR Nationwide Chevrolet Camaro (2014)", "NASCAR"),
        ["stockcars2 nwford2013"] = ("NASCAR Nationwide Ford Mustang (2013)", "NASCAR"), ["stockcars2 supra2019"] = ("NASCAR Xfinity Toyota Supra", "NASCAR"), ["trucks fordf150"] = ("NASCAR Truck Ford F-150", "NASCAR"),
        ["trucks ram2026"] = ("NASCAR Truck Ram", "NASCAR"), ["trucks silverado"] = ("NASCAR Truck Chevrolet Silverado (2013)", "NASCAR"), ["trucks silverado2015"] = ("NASCAR Truck Chevrolet Silverado (2015)", "NASCAR"),
        ["trucks silverado2019"] = ("NASCAR Truck Chevrolet Silverado", "NASCAR"), ["trucks toyotatundra2022"] = ("NASCAR Truck Toyota Tundra TRD Pro", "NASCAR"), ["trucks tundra2015"] = ("NASCAR Truck Toyota Tundra (2015)", "NASCAR"),
        ["latemodel"] = ("Late Model", "Oval"), ["latemodel2023"] = ("Late Model Stock", "Oval"), ["legends ford34c"] = ("Legends Ford '34 Coupe", "Oval"),
        ["legends ford34c rookie"] = ("Legends Ford '34 Coupe (Rookie)", "Oval"), ["ministock"] = ("Mini Stock", "Oval"), ["silvercrown"] = ("Silver Crown", "Oval"),
        ["skmodified"] = ("SK Modified", "Oval"), ["skmodified tour"] = ("SK Modified Tour", "Oval"), ["sprint"] = ("Sprint Car", "Oval"),
        ["srx"] = ("SRX", "Oval"), ["streetstock"] = ("Street Stock", "Oval"), ["streetstock streetstock2"] = ("Street Stock (2)", "Oval"),
        ["streetstock streetstock3"] = ("Street Stock (3)", "Oval"), ["superlatemodel"] = ("Super Late Model", "Oval"),
        ["dirtlatemodel 350"] = ("Dirt Late Model Limited", "Dirt"), ["dirtlatemodel 358"] = ("Dirt Late Model Pro", "Dirt"), ["dirtlatemodel 438"] = ("Dirt Super Late Model", "Dirt"),
        ["dirtmicrosprint nonwinged"] = ("Dirt Micro Sprint Non-Winged", "Dirt"), ["dirtmicrosprint nonwinged outlaw"] = ("Dirt Outlaw Micro Sprint Non-Winged", "Dirt"), ["dirtmicrosprint winged"] = ("Dirt Micro Sprint Winged", "Dirt"),
        ["dirtmicrosprint winged outlaw"] = ("Dirt Outlaw Micro Sprint Winged", "Dirt"), ["dirtmidget"] = ("Dirt Midget", "Dirt"), ["dirtministock"] = ("Dirt Mini Stock", "Dirt"),
        ["dirtmodified 358"] = ("Dirt 358 Modified", "Dirt"), ["dirtmodified bigblock"] = ("Dirt Big Block Modified", "Dirt"), ["dirtsprint nonwinged 360"] = ("Dirt 360 Non-Winged Sprint Car", "Dirt"),
        ["dirtsprint nonwinged 410"] = ("Dirt 410 Non-Winged Sprint Car", "Dirt"), ["dirtsprint winged 305"] = ("Dirt 305 Winged Sprint Car", "Dirt"), ["dirtsprint winged 360"] = ("Dirt 360 Winged Sprint Car", "Dirt"),
        ["dirtsprint winged 410"] = ("Dirt 410 Winged Sprint Car", "Dirt"), ["dirtstreetstock"] = ("Dirt Street Stock", "Dirt"), ["dirtumpmod"] = ("Dirt UMP Modified", "Dirt"),
        ["legends dirtford34c"] = ("Dirt Legends Ford '34 Coupe", "Dirt"),
        ["fordfiestarswrc"] = ("Ford Fiesta RS WRC", "Rallycross"), ["subaruwrxsti"] = ("Subaru WRX STI", "Rallycross"), ["vwbeetlegrc"] = ("VW Beetle GRC", "Rallycross"),
        ["vwbeetlegrc lite"] = ("VW Beetle GRC Lite", "Rallycross"),
        ["protrucks pro2lite"] = ("Pro 2 Lite Off-Road Truck", "Off-road"), ["protrucks pro2truck"] = ("Pro 2 Off-Road Truck", "Off-road"), ["protrucks pro4truck"] = ("Pro 4 Off-Road Truck", "Off-road"),
        ["crosscartn11"] = ("Cross Kart", "Off-road"),
        ["astonmartin dbr9"] = ("Aston Martin DBR9 GT1", "Classic"), ["c6r"] = ("Chevrolet Corvette C6.R GT1", "Classic"), ["fordgt"] = ("Ford GT GT2", "Classic"),
        ["nissangtpzxt"] = ("Nissan GTP ZX-T", "Classic"), ["audi90gto"] = ("Audi 90 GTO", "Classic"),
    };

    public static (string Name, string Class, bool Known, bool Custom) Describe(string dir, SetupManagerSettings? settings)
    {
        bool known = Known.TryGetValue(dir, out var builtIn);
        var (name, cls) = known ? builtIn : (dir, Guess(dir));
        if (settings is not null && settings.Names.TryGetValue(dir, out var mine) && !string.IsNullOrWhiteSpace(mine.Name))
            return (mine.Name.Trim(), mine.Class is { } mc && Classes.Contains(mc) ? mc : cls, true, true);
        return (name, cls, known, false);
    }

    public static string? BuiltInName(string dir) => Known.TryGetValue(dir, out var k) ? k.Name : null;

    public static IEnumerable<string> BuiltInNames => Known.Values.Select(k => k.Name).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

    public static string Guess(string dir)
    {
        string d = dir.ToLowerInvariant();
        bool Has(params string[] parts) => parts.Any(d.Contains);
        if (Has("gte", "rsr")) return "GTE";
        if (Has("gt3")) return "GT3";
        if (Has("gt4")) return "GT4";
        if (Has("gtp", "lmdh", "lmh", "hypercar")) return "GTP";
        if (Has("lmp2")) return "LMP2";
        if (Has("lmp3")) return "LMP3";
        if (Has("lmp1", "dpi", "prototype") || d.EndsWith("dp")) return "Prototype";
        if (Has("tcr")) return "TCR";
        if (Has("supercars", "stockcarbrasil")) return "Touring";
        if (d.StartsWith("legends dirt") || d.StartsWith("dirt")) return "Dirt";
        if (d.StartsWith("stockcars") || d.StartsWith("trucks") || Has("arca", "nascar")) return "NASCAR";
        if (d.StartsWith("protrucks") || Has("offroad", "crosscar")) return "Off-road";
        if (Has("rallycross", "grc", "wrc", "rx")) return "Rallycross";
        if (Has("latemodel", "legends", "modified", "streetstock", "ministock", "silvercrown", "sprint")) return "Oval";
        if (Has("formula", "dallara", "indy", "usf", "superformula", "f3", "f4", "ff1600")) return "Formula";
        if (Has("cup", "challenge", "spec", "mx5", "caterham", "radical", "ruf")) return "Sports Car";
        return Other;
    }

    public static (bool Coloured, bool Picked) FilterLook(string cls, IReadOnlyCollection<string> picked)
    {
        bool on = picked.Contains(cls);
        return (picked.Count == 0 || on, on);
    }

    public static List<string> Order(IEnumerable<string>? saved)
    {
        var order = (saved ?? Enumerable.Empty<string>()).Where(Classes.Contains).Distinct().ToList();
        order.AddRange(Classes.Where(c => !order.Contains(c)));
        return order;
    }

    public static List<string> Reorder(IReadOnlyList<string> full, IReadOnlyList<string> shown)
    {
        var set = new HashSet<string>(shown);
        int next = 0;
        return full.Select(c => set.Contains(c) && next < shown.Count ? shown[next++] : c).ToList();
    }

    public static IEnumerable<SetupCar> Sorted(IEnumerable<SetupCar> cars, string sort, IReadOnlyList<string> classOrder) => sort switch
    {
        "name" => cars.OrderBy(c => c.Name, Natural.Comparer),
        "name-desc" => cars.OrderByDescending(c => c.Name, Natural.Comparer),
        "count" => cars.OrderByDescending(c => c.Root.Files).ThenBy(c => c.Name, Natural.Comparer),
        "recent" => cars.OrderByDescending(c => c.Root.Latest).ThenBy(c => c.Name, Natural.Comparer),
        _ => cars.OrderBy(c => Rank(classOrder, c.Class)).ThenBy(c => c.Name, Natural.Comparer),
    };

    private static int Rank(IReadOnlyList<string> order, string cls)
    {
        for (int i = 0; i < order.Count; i++) if (order[i] == cls) return i;
        return int.MaxValue;
    }

    public static SetupCar? GuessFromFileName(string file, IEnumerable<SetupCar> cars)
    {
        var words = Units(Path.GetFileNameWithoutExtension(file), split: true).Where(Keep).Distinct().ToList();
        if (words.Count == 0) return null;
        SetupCar? best = null;
        int top = 0, second = 0;
        foreach (var car in cars)
        {
            var nameWords = new HashSet<string>(Units(car.Name, split: false));
            if (BuiltInName(car.Dir) is { } builtIn) nameWords.UnionWith(Units(builtIn, split: false));
            string flat = new(car.Dir.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            int score = 0;
            foreach (var w in words)
            {
                bool digit = w.Any(char.IsDigit);
                if (nameWords.Contains(w)) score += digit ? 3 : 1;
                if (w.Length >= (digit ? 3 : 4) && flat.Contains(w, StringComparison.Ordinal)) score += 2;
            }
            if (score > top) { second = top; top = score; best = car; }
            else if (score > second) second = score;
        }
        return top >= 3 && top - second >= 2 ? best : null;
    }

    private static IEnumerable<string> Units(string text, bool split)
    {
        text = LetterDashDigit.Replace(text.ToLowerInvariant(), "$1$2");
        foreach (var token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!split) { yield return token; continue; }
            var runs = Runs.Matches(token).Select(m => m.Value).ToList();
            var parts = new List<string>();
            for (int i = 0; i < runs.Count; i++)
                if (char.IsLetter(runs[i][0]) && i + 1 < runs.Count && runs[i + 1].Length <= 2) parts.Add(runs[i] + runs[++i]);
                else parts.Add(runs[i]);
            if (parts.Count > 1) yield return token;
            foreach (var part in parts) yield return part;
        }
    }

    private static bool Keep(string w)
    {
        if (Ignored.Contains(w) || Tag.IsMatch(w)) return false;
        if (w.All(char.IsDigit)) return w.Length >= 3;
        return w.Length >= (w.Any(char.IsDigit) ? 2 : 3);
    }

    private static readonly char[] Separators = " _-.,()[]'+&".ToCharArray();
    private static readonly System.Text.RegularExpressions.Regex Runs = new("[a-z]+|[0-9]+"), LetterDashDigit = new("([a-z])-([0-9])"),
        Tag = new("^(v[0-9]+|[0-9]{2}s[0-9])$");
    private static readonly HashSet<string> Ignored = new()
    {
        "sto", "zip", "race", "qual", "quali", "practice", "league", "setup", "setups", "fixed", "baseline", "safe", "wet", "dry", "open",
        "sprint", "endurance", "the", "and",
    };
}

internal static class SetupNames
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    public static (string? Name, string? Error) Check(string raw, bool isFile, SetupNode folder, SetupNode? self = null)
    {
        string name = (raw ?? "").Trim();
        if (name.Length == 0) return (null, "Enter a name.");
        if (name.IndexOfAny(Invalid) >= 0) return (null, "Names can't contain < > : \" / \\ | ? *");
        if (isFile && !name.EndsWith(".sto", StringComparison.OrdinalIgnoreCase)) name += ".sto";
        if (name.EndsWith('.') || name.EndsWith(' ')) return (null, "Names can't end with a space or a full stop.");
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            return (null, "Windows keeps that name for itself.");
        if (folder.Children.Any(c => c != self && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            return (null, $"There's already a {(isFile ? "setup" : "folder")} called {name} here.");
        return (name, null);
    }

    public static string Unique(IEnumerable<string> taken, string name, bool isFile = true)
    {
        var set = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(name)) return name;
        string ext = isFile ? Path.GetExtension(name) : "", stem = name[..^ext.Length];
        for (int i = 2; ; i++)
        {
            string candidate = $"{stem} ({i}){ext}";
            if (!set.Contains(candidate)) return candidate;
        }
    }

    public static string Clean(string segment)
    {
        var chars = segment.Trim().Select(c => Array.IndexOf(Invalid, c) >= 0 ? '_' : c).ToArray();
        return new string(chars).TrimEnd('.', ' ');
    }

    public static (SetupCar? Car, List<string> Segments) Route(string entry, IEnumerable<SetupCar> cars)
    {
        var parts = entry.Split('/', '\\').Select(Clean).Where(p => p.Length > 0 && p != "." && p != "..").ToList();
        if (parts.Count > 1 && cars.FirstOrDefault(c => string.Equals(c.Dir, parts[0], StringComparison.OrdinalIgnoreCase)) is { } car)
            return (car, parts.Skip(1).ToList());
        return (null, parts);
    }
}

internal static class SetupOrder
{
    public static string KeyOf(SetupNode folder) => folder.Key;

    public static List<SetupNode> Default(SetupNode folder) =>
        folder.Children.OrderBy(c => c.IsFolder ? 0 : 1).ThenBy(c => c.Name, Natural.Comparer).ToList();

    public static List<SetupNode> Of(SetupNode folder, IReadOnlyDictionary<string, List<string>> orders)
    {
        var items = Default(folder);
        if (!orders.TryGetValue(KeyOf(folder), out var saved) || saved.Count == 0) return items;
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < saved.Count; i++) rank.TryAdd(saved[i], i);
        return items.Select((n, i) => (n, r: rank.TryGetValue(n.Name, out int r) ? r : saved.Count + i)).OrderBy(x => x.r).Select(x => x.n).ToList();
    }

    public static void Save(SetupNode folder, IReadOnlyList<string> names, Dictionary<string, List<string>> orders)
    {
        if (names.SequenceEqual(Default(folder).Select(n => n.Name), StringComparer.OrdinalIgnoreCase)) orders.Remove(KeyOf(folder));
        else orders[KeyOf(folder)] = names.ToList();
    }

    public static void FolderMoved(string oldKey, string newKey, Dictionary<string, List<string>> orders)
    {
        foreach (var key in orders.Keys.Where(k => string.Equals(k, oldKey, StringComparison.OrdinalIgnoreCase)
                     || k.StartsWith(oldKey + "/", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var list = orders[key];
            orders.Remove(key);
            orders[newKey + key[oldKey.Length..]] = list;
        }
    }

    public static void Renamed(SetupNode folder, string from, string to, Dictionary<string, List<string>> orders)
    {
        if (!orders.TryGetValue(KeyOf(folder), out var saved)) return;
        int i = saved.FindIndex(n => string.Equals(n, from, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) saved[i] = to;
    }
}

internal sealed record SetupSearch(bool CarHit, HashSet<SetupNode> Hits, HashSet<SetupNode> Show, HashSet<SetupNode> Open)
{
    public static SetupSearch? For(SetupCar car, string query)
    {
        string q = query.Trim();
        if (q.Length == 0) return null;
        bool Has(string s) => s.Contains(q, StringComparison.OrdinalIgnoreCase);
        var hits = new HashSet<SetupNode>();
        var show = new HashSet<SetupNode>();
        var open = new HashSet<SetupNode>();
        bool Walk(SetupNode folder, bool inHit)
        {
            bool any = false;
            foreach (var c in folder.Children)
            {
                bool self = Has(c.Name);
                if (self) hits.Add(c);
                bool below = c.IsFolder && Walk(c, inHit || self);
                if (below) open.Add(c);
                if (self || below || inHit) { show.Add(c); any = true; }
            }
            return any;
        }
        Walk(car.Root, false);
        return new SetupSearch(Has(car.Name) || Has(car.Dir) || Has(car.Class), hits, show, open);
    }

    public bool Matches => CarHit || Show.Count > 0;
}

internal sealed class Natural : IComparer<string>
{
    public static readonly Natural Comparer = new();

    public int Compare(string? a, string? b)
    {
        if (a is null || b is null) return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                string na = a[si..i].TrimStart('0'), nb = b[sj..j].TrimStart('0');
                int c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}

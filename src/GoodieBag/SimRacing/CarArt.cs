using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GoodieBag.SimRacing.Setups;

namespace GoodieBag.SimRacing;

internal static class CarArt
{
    private static readonly Regex Split = new("[^a-z0-9]+", RegexOptions.CultureInvariant), LetterDashDigit = new("([a-z])-([0-9])", RegexOptions.CultureInvariant);

    private static readonly (string Key, string[] Dirs)[] Table =
    {
        ("porsche-911-gt3-r-991", new[] { "porsche911rgt3" }),
        ("porsche-911-gt3-r-992", new[] { "porsche992rgt3" }),
        ("ferrari-296-gt3", new[] { "ferrari296gt3" }),
        ("lamborghini-huracan-gt3", new[] { "lamborghinievogt3" }),
        ("mclaren-720s-gt3", new[] { "mclaren720sgt3" }),
        ("mercedes-amg-gt3", new[] { "mercedesamgevogt3" }),
        ("bmw-m4-gt3", new[] { "bmwm4gt3" }),
        ("audi-r8-gt3", new[] { "audir8lmsevo2gt3" }),
        ("corvette-z06-gt3r", new[] { "chevyvettez06rgt3" }),
        ("ford-mustang-gt3", new[] { "fordmustanggt3" }),
        ("aston-martin-vantage-gt3", new[] { "amvantageevogt3" }),
        ("acura-nsx-gt3", new[] { "acuransxevo22gt3" }),
        ("porsche-963-gtp", new[] { "porsche963gtp" }),
        ("cadillac-v-series-r", new[] { "cadillacvseriesrgtp" }),
        ("acura-arx-06", new[] { "acuraarx06gtp" }),
        ("bmw-m-hybrid-v8", new[] { "bmwlmdh" }),
        ("ferrari-499p", new[] { "ferrari499p" }),
        ("aston-martin-valkyrie", new[] { "amvalkyriegtp" }),
        ("dallara-p217", new[] { "dallarap217" }),
        ("ligier-js-p320", new[] { "ligierjsp320" }),
        ("mercedes-f1", new[] { "mercedesw13" }),
        ("mclaren-f1", new[] { "mclarenmp430" }),
        ("williams-f1", new[] { "williamsfw31" }),
        ("dallara-f3", new[] { "dallaraf3" }),
        ("indycar-ir18", new[] { "dallarair18" }),
        ("super-formula-sf23", new[] { "superformulasf23 honda" }),
        ("super-formula-lights", new[] { "superformulalights324" }),
        ("fia-f4", new[] { "formulair04" }),
        ("usf-2000", new[] { "usf2000usf17" }),
        ("formula-vee", new[] { "formulavee" }),
        ("ray-ff1600", new[] { "raygr22" }),
        ("skip-barber-f2000", new[] { "rt2000" }),
        ("indy-pro-2000", new[] { "indypropm18" }),
        ("indy-nxt-il15", new[] { "dallarail15" }),
        ("formula-renault-20", new[] { "formularenault20" }),
        ("formula-renault-35", new[] { "formularenault35" }),
        ("lotus-49", new[] { "lotus49" }),
        ("lotus-79", new[] { "lotus79" }),
        ("porsche-718-gt4", new[] { "porsche718gt4" }),
        ("bmw-m4-gt4", new[] { "bmwm4evogt4" }),
        ("mercedes-amg-gt4", new[] { "mercedesamggt4" }),
        ("mclaren-570s-gt4", new[] { "mclaren570sgt4" }),
        ("ford-mustang-gt4", new[] { "fordmustanggt4" }),
        ("aston-martin-vantage-gt4", new[] { "amvantagegt4" }),
        ("porsche-911-rsr", new[] { "porsche991rsr" }),
        ("ferrari-488-gte", new[] { "ferrari488gte" }),
        ("corvette-c8r-gte", new[] { "c8rvettegte" }),
        ("bmw-m8-gte", new[] { "bmwm8gte" }),
        ("ford-gt-gte", new[] { "fordgt2017" }),
        ("porsche-911-cup-992", new[] { "porsche9922cup" }),
        ("porsche-911-cup-991", new[] { "porsche911cup" }),
        ("ferrari-296-challenge", new[] { "ferrari296challenge" }),
        ("mazda-mx5-cup", new[] { "mx5 mx52016" }),
        ("mazda-mx5-roadster", new[] { "mx5 roadster" }),
        ("toyota-gr86", new[] { "toyotagr86" }),
        ("renault-clio-cup", new[] { "renaultcliocup" }),
        ("audi-rs3-tcr", new[] { "audirs3lmsgen2" }),
        ("honda-civic-tcr", new[] { "hondacivictyper" }),
        ("hyundai-elantra-tcr", new[] { "hyundaielantracn7" }),
        ("supercars", new[] { "supercars fordmustanggen3" }),
        ("nascar-cup", new[] { "stockcars chevycamarozl12022" }),
        ("nascar-xfinity", new[] { "stockcars2 camaro2019" }),
        ("nascar-truck", new[] { "trucks ram2026" }),
        ("arca", new[] { "stockcars2 arcachevy25" }),
        ("late-model-stock", new[] { "latemodel2023" }),
        ("super-late-model", new[] { "superlatemodel" }),
        ("sk-modified", new[] { "skmodified" }),
        ("sk-modified-tour", new[] { "skmodified tour" }),
        ("legends", new[] { "legends ford34c", "legends ford34c rookie" }),
        ("street-stock", new[] { "streetstock" }),
        ("dirt-late-model", new[] { "dirtlatemodel 438" }),
        ("dirt-modified", new[] { "dirtmodified bigblock" }),
        ("sprint-car", new[] { "dirtsprint winged 410" }),
        ("dirt-midget", new[] { "dirtmidget" }),
        ("micro-sprint", new[] { "dirtmicrosprint nonwinged" }),
        ("pro-2-truck", new[] { "protrucks pro2truck" }),
        ("pro-4-truck", new[] { "protrucks pro4truck" }),
        ("rallycross-supercar", new[] { "subaruwrxsti" }),
        ("rallycross-lite", new[] { "vwbeetlegrc lite" }),
        ("audi-r18", new[] { "audir18" }),
        ("porsche-919", new[] { "porsche919" }),
        ("hpd-arx-01c", new[] { "hpdarx01c" }),
        ("riley-mkxx-daytona-prototype", new[] { "rileydp" }),
        ("chevrolet-corvette-c7-daytona-prototype", new[] { "c7vettedp" }),
        ("radical-sr8", new[] { "radical sr8" }),
        ("radical-sr10", new[] { "radicalsr10" }),
        ("audi-r8-lms-gt3", new[] { "audir8gt3" }),
        ("bmw-z4-gt3", new[] { "bmwz4gt3" }),
        ("ferrari-488-gt3", new[] { "ferrari488gt3" }),
        ("ferrari-488-gt3-evo-2020", new[] { "ferrarievogt3" }),
        ("ford-gt-gt3", new[] { "fordgt gt3" }),
        ("mclaren-mp4-12c-gt3", new[] { "mclarenmp4" }),
        ("mercedes-amg-gt3-legacy", new[] { "mercedesamggt3" }),
        ("bmw-m4-gt4-f82", new[] { "bmwm4gt4" }),
        ("audi-rs-3-lms-tcr", new[] { "audirs3lms" }),
        ("hyundai-veloster-n-tcr", new[] { "hyundaivelostern" }),
        ("ford-falcon-fg-v8-supercar", new[] { "fordv8sc" }),
        ("supercars-chevrolet-camaro-gen3", new[] { "supercars chevycamarogen3" }),
        ("v8-supercars-ford-falcon-2014", new[] { "v8supercars ford2014" }),
        ("supercars-ford-mustang-gt", new[] { "v8supercars fordmustanggt" }),
        ("v8-supercars-holden-commodore-2014", new[] { "v8supercars holden2014" }),
        ("supercars-holden-zb-commodore", new[] { "v8supercars holden2019" }),
        ("stock-car-brasil-toyota-corolla", new[] { "stockcarbrasil corolla" }),
        ("stock-car-brasil-chevrolet-cruze", new[] { "stockcarbrasil cruze" }),
        ("kia-optima", new[] { "kiaoptima" }),
        ("cadillac-cts-v-racecar", new[] { "cadillacctsvr" }),
        ("ford-mustang-fr500s", new[] { "fr500s" }),
        ("bmw-m2-cs-racing", new[] { "bmwm2csr" }),
        ("bmw-m2-g87", new[] { "bmwm2g87" }),
        ("porsche-911-gt3-cup-992", new[] { "porsche992cup" }),
        ("porsche-mission-r", new[] { "porschemissionr" }),
        ("mazda-mx-5-cup-2015", new[] { "mx5 cup" }),
        ("pontiac-solstice", new[] { "solstice", "solstice rookie" }),
        ("spec-racer-ford", new[] { "specracer" }),
        ("caterham-420r", new[] { "caterham caterham420r" }),
        ("caterham-academy", new[] { "caterham caterhamacademy" }),
        ("ruf-rt-12r-awd", new[] { "rufrt12r awd" }),
        ("ruf-rt-12r-rwd", new[] { "rufrt12r rwd" }),
        ("ruf-rt-12r-track", new[] { "rufrt12r track" }),
        ("ruf-rt-12r-c-spec", new[] { "rufrt12r track cspec" }),
        ("vw-jetta-tdi-cup", new[] { "jettatdi" }),
        ("dallara-ir-05", new[] { "dallara" }),
        ("dallara-dw12-indycar", new[] { "dallaradw12" }),
        ("dallara-ir-01", new[] { "dallarair01" }),
        ("formula-mazda", new[] { "formulamazda" }),
        ("formula-vee-2", new[] { "formulavee formulavee2" }),
        ("formula-vee-3", new[] { "formulavee formulavee3" }),
        ("mercedes-amg-w12-e-performance", new[] { "mercedesw12" }),
        ("super-formula-sf23-toyota", new[] { "superformulasf23 toyota" }),
        ("nascar-euro-series", new[] { "euronascar" }),
        ("nascar-buick-lesabre-1987", new[] { "stockcars buicklesabre87" }),
        ("nascar-cup-chevrolet-camaro-zl1-2018", new[] { "stockcars camarozl12018" }),
        ("nascar-chevrolet-monte-carlo-2003", new[] { "stockcars chevymontecarlo03" }),
        ("nascar-chevrolet-monte-carlo-1987", new[] { "stockcars chevymontecarlo87" }),
        ("nascar-cup-chevrolet-ss", new[] { "stockcars chevyss" }),
        ("nascar-cup-ford-fusion", new[] { "stockcars fordfusion" }),
        ("nascar-cup-ford-mustang-2019", new[] { "stockcars fordmustang2019" }),
        ("nascar-cup-ford-mustang", new[] { "stockcars fordmustang2022" }),
        ("nascar-ford-taurus-2003", new[] { "stockcars fordtaurus03" }),
        ("nascar-ford-thunderbird-1987", new[] { "stockcars fordthunderbird87" }),
        ("nascar-chevrolet-impala-cot", new[] { "stockcars impala" }),
        ("nascar-pontiac-grand-prix-1987", new[] { "stockcars pontiacgrandprix87" }),
        ("nascar-cup-toyota-camry-2019", new[] { "stockcars toyotacamry" }),
        ("nascar-cup-toyota-camry", new[] { "stockcars toyotacamry2022" }),
        ("arca-ford", new[] { "stockcars2 arcaford25" }),
        ("arca-toyota", new[] { "stockcars2 arcatoyota25" }),
        ("nascar-xfinity-toyota-camry-2015", new[] { "stockcars2 camry2015" }),
        ("nascar-class-b-chevrolet-impala", new[] { "stockcars2 chevy" }),
        ("nascar-chevrolet-impala-cot-class-b", new[] { "stockcars2 chevy cot" }),
        ("nascar-gen-4-cup-chevrolet-monte-carlo", new[] { "stockcars2 chevy gen4cup" }),
        ("nascar-nationwide-chevrolet-impala", new[] { "stockcars2 chevy national" }),
        ("nascar-xfinity-ford-mustang", new[] { "stockcars2 mustang2019" }),
        ("nascar-nationwide-chevrolet-camaro-2014", new[] { "stockcars2 nwcamaro2014" }),
        ("nascar-nationwide-ford-mustang-2013", new[] { "stockcars2 nwford2013" }),
        ("nascar-xfinity-toyota-supra", new[] { "stockcars2 supra2019" }),
        ("nascar-truck-ford-f-150", new[] { "trucks fordf150" }),
        ("nascar-truck-chevrolet-silverado-2013", new[] { "trucks silverado" }),
        ("nascar-truck-chevrolet-silverado-2015", new[] { "trucks silverado2015" }),
        ("nascar-truck-chevrolet-silverado", new[] { "trucks silverado2019" }),
        ("nascar-truck-toyota-tundra-trd-pro", new[] { "trucks toyotatundra2022" }),
        ("late-model", new[] { "latemodel" }),
        ("mini-stock", new[] { "ministock" }),
        ("silver-crown", new[] { "silvercrown" }),
        ("sprint-car-legacy", new[] { "sprint" }),
        ("srx", new[] { "srx" }),
        ("street-stock-2", new[] { "streetstock streetstock2" }),
        ("street-stock-3", new[] { "streetstock streetstock3" }),
        ("dirt-late-model-limited", new[] { "dirtlatemodel 350" }),
        ("dirt-late-model-pro", new[] { "dirtlatemodel 358" }),
        ("dirt-outlaw-micro-sprint-non-winged", new[] { "dirtmicrosprint nonwinged outlaw" }),
        ("dirt-micro-sprint-winged", new[] { "dirtmicrosprint winged" }),
        ("dirt-outlaw-micro-sprint-winged", new[] { "dirtmicrosprint winged outlaw" }),
        ("dirt-mini-stock", new[] { "dirtministock" }),
        ("dirt-358-modified", new[] { "dirtmodified 358" }),
        ("dirt-360-non-winged-sprint-car", new[] { "dirtsprint nonwinged 360" }),
        ("dirt-410-non-winged-sprint-car", new[] { "dirtsprint nonwinged 410" }),
        ("dirt-305-winged-sprint-car", new[] { "dirtsprint winged 305" }),
        ("dirt-360-winged-sprint-car", new[] { "dirtsprint winged 360" }),
        ("dirt-street-stock", new[] { "dirtstreetstock" }),
        ("dirt-ump-modified", new[] { "dirtumpmod" }),
        ("dirt-legends-ford-34-coupe", new[] { "legends dirtford34c" }),
        ("ford-fiesta-rs-wrc", new[] { "fordfiestarswrc" }),
        ("vw-beetle-grc", new[] { "vwbeetlegrc" }),
        ("pro-2-lite-off-road-truck", new[] { "protrucks pro2lite" }),
        ("cross-kart", new[] { "crosscartn11" }),
        ("aston-martin-dbr9-gt1", new[] { "astonmartin dbr9" }),
        ("chevrolet-corvette-c6-r-gt1", new[] { "c6r" }),
        ("ford-gt-gt2", new[] { "fordgt" }),
        ("nissan-gtp-zx-t", new[] { "nissangtpzxt" }),
        ("audi-90-gto", new[] { "audi90gto" }),
    };

    private static readonly Dictionary<string, string> ByDir = BuildDirs();
    private static readonly List<(string[] Words, string Key)> ByName = BuildNames();
    private static readonly Dictionary<string, string?> Found = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> Keys => Table.Select(t => t.Key);

    public static IEnumerable<string> Dirs => ByDir.Keys;

    public static string? KeyOf(string? car)
    {
        if (string.IsNullOrWhiteSpace(car)) return null;
        string text = car.Trim();
        lock (Found)
        {
            if (Found.TryGetValue(text, out var known)) return known;
            var key = ByDir.TryGetValue(text, out var dir) ? dir : Match(text);
            Found[text] = key;
            return key;
        }
    }

    private static Dictionary<string, string> BuildDirs()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, dirs) in Table)
            foreach (var dir in dirs) map[dir] = key;
        return map;
    }

    private static List<(string[] Words, string Key)> BuildNames()
    {
        var list = new List<(string[], string)>();
        foreach (var (key, dirs) in Table)
            foreach (var dir in dirs)
                if (SetupCars.BuiltInName(dir) is { } name)
                    list.Add((Words(name), key));
        return list;
    }

    private static string? Match(string name)
    {
        var words = Words(name);
        if (words.Length == 0) return null;
        string? best = null;
        double top = 0;
        foreach (var (candidate, key) in ByName)
        {
            if (candidate.SequenceEqual(words)) return key;
            double score = Score(words, candidate);
            if (score > top) { top = score; best = key; }
        }
        return top >= 0.6 ? best : null;
    }

    internal static double Score(string[] query, string[] candidate)
    {
        var a = new HashSet<string>(query);
        var b = new HashSet<string>(candidate);
        var numbersA = a.Where(w => w.Any(char.IsDigit)).ToHashSet();
        var numbersB = b.Where(w => w.Any(char.IsDigit)).ToHashSet();
        if (!numbersA.IsSubsetOf(numbersB) && !numbersB.IsSubsetOf(numbersA)) return 0;
        int shared = a.Count(b.Contains);
        if (shared < 2) return 0;
        return (double)shared / Math.Max(a.Count, b.Count);
    }

    internal static string[] Words(string text)
    {
        var plain = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(char.ToLowerInvariant(c));
        string lower = LetterDashDigit.Replace(plain.ToString(), "$1$2");
        return Split.Split(lower).Where(w => w.Length > 0).ToArray();
    }
}

using GoodieBag.Config;
using GoodieBag.SimRacing.Setups;

namespace GoodieBag.SimRacing.Race;

internal static class SessionNames
{
    public static readonly string[] Tracks =
    {
        "Algarve International Circuit", "Atlanta Motor Speedway", "Autodromo Internazionale Enzo e Dino Ferrari", "Autodromo Nazionale Monza",
        "Autódromo Hermanos Rodríguez", "Autódromo José Carlos Pace", "Barber Motorsports Park", "Bristol Motor Speedway",
        "Brands Hatch Circuit", "Canadian Tire Motorsport Park", "Charlotte Motor Speedway", "Chicagoland Speedway", "Chili Bowl",
        "Circuit de Barcelona Catalunya", "Circuit de Nevers Magny-Cours", "Circuit de Spa-Francorchamps", "Circuit des 24 Heures du Mans",
        "Circuit Gilles Villeneuve", "Circuit of the Americas", "Circuit Park Zandvoort", "Circuit Zolder", "Daytona International Speedway",
        "Detroit Grand Prix at Belle Isle", "Donington Park Racing Circuit", "Dover Motor Speedway", "Eldora Speedway", "Fuji International Speedway",
        "Hockenheimring Baden-Württemberg", "Homestead Miami Speedway", "Hungaroring", "Indianapolis Motor Speedway", "Iowa Speedway",
        "Kansas Speedway", "Kentucky Speedway", "Knockhill Racing Circuit", "Knoxville Raceway", "Kokusai Kaihatsu Raceway",
        "Las Vegas Motor Speedway", "Lime Rock Park", "Long Beach Street Circuit", "Lucas Oil Raceway", "Martinsville Speedway",
        "Michigan International Speedway", "Mid-Ohio Sports Car Course", "Misano World Circuit Marco Simoncelli", "Motegi",
        "Mount Panorama Circuit", "MotorLand Aragón", "Nashville Superspeedway", "Navarra Circuit", "New Hampshire Motor Speedway",
        "Nürburgring Grand-Prix-Strecke", "Nürburgring Nordschleife", "Okayama International Circuit", "Oran Park Raceway", "Oulton Park Circuit",
        "Phillip Island Circuit", "Phoenix Raceway", "Pocono Raceway", "Portland International Raceway", "Red Bull Ring", "Richmond Raceway",
        "Road America", "Road Atlanta", "Rudskogen Motorsenter", "Sachsenring", "Sebring International Raceway", "Silverstone Circuit",
        "Snetterton Circuit", "Sonoma Raceway", "Suzuka International Racing Course", "Talladega Superspeedway", "Texas Motor Speedway",
        "The Bullring", "Thruxton Circuit", "Tsukuba Circuit", "Virginia International Raceway", "Watkins Glen International",
        "WeatherTech Raceway Laguna Seca", "Williams Grove Speedway", "Winton Motor Raceway", "World Wide Technology Raceway",
        "Interlagos", "Jerez", "Imola", "Lanier National Speedway", "Myrtle Beach Speedway", "Oxford Plains Speedway",
        "Rockingham Speedway", "Darlington Raceway", "North Wilkesboro Speedway", "South Boston Speedway", "Stafford Motor Speedway",
        "Thompson Speedway Motorsports Park", "USA International Speedway", "Volusia Speedway Park", "Cedar Lake Speedway",
        "Fairbury Speedway", "Limaland Motorsports Park", "Lernerville Speedway", "Federated Auto Parts Raceway at I-55",
        "Kevin Harvick's Kern Raceway", "Lankebanen", "Atlanta Rallycross", "Wild Horse Pass Motorsports Park",
    };

    public static IEnumerable<string> Classes => SetupCars.Classes.Where(c => c != SetupCars.Other);

    public static List<string> Merge(string? current, IEnumerable<string?> seen, IEnumerable<string> builtIn)
    {
        var all = seen.Concat(builtIn).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(current) && !all.Contains(current, StringComparer.OrdinalIgnoreCase)) all.Insert(0, current);
        return all;
    }

    public static bool ClassMatches(string actual, string picked)
    {
        if (string.Equals(actual, picked, StringComparison.OrdinalIgnoreCase)) return true;
        int i = actual.IndexOf(picked, StringComparison.OrdinalIgnoreCase);
        while (i >= 0)
        {
            bool start = i == 0 || !char.IsLetterOrDigit(actual[i - 1]);
            int end = i + picked.Length;
            bool stop = end >= actual.Length || !char.IsLetterOrDigit(actual[end]);
            if (start && stop) return true;
            i = actual.IndexOf(picked, i + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}

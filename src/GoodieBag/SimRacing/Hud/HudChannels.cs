using GoodieBag.Config;
using GoodieBag.SimRacing.Race;
using GoodieBag.SimRacing.Telemetry;

namespace GoodieBag.SimRacing.Hud;

internal enum ChannelKind
{
    Live,
    Session,
    Calculated,
    History,
}

internal enum ChannelUnit { None, Speed, Temp, Pressure, Fuel, Seconds, LapTime, Percent, Rpm, G, Degrees, Count }

internal sealed record HudChannel(string Id, string Label, ChannelKind Kind, ChannelUnit Unit, Func<HudView, double?> Read, int Decimals = 1);

internal static class HudChannels
{
    public static readonly HudChannel[] All =
    {
        new("speed", "Speed", ChannelKind.Live, ChannelUnit.Speed, v => v.Frame.Speed, 0),
        new("rpm", "RPM", ChannelKind.Live, ChannelUnit.Rpm, v => v.Frame.Rpm, 0),
        new("gear", "Gear", ChannelKind.Live, ChannelUnit.None, v => v.Frame.Gear, 0),
        new("throttle", "Throttle", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.Throttle, 0),
        new("brake", "Brake", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.Brake, 0),
        new("clutch", "Clutch pedal", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.Clutch is { } c ? 1 - c : null, 0),
        new("steer", "Steering angle", ChannelKind.Live, ChannelUnit.Degrees, v => v.Frame.Steering is { } s ? s * 180 / Math.PI : null, 0),
        new("fuel", "Fuel", ChannelKind.Live, ChannelUnit.Fuel, v => v.Frame.Fuel),
        new("fuelPct", "Fuel (% of tank)", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.FuelPct, 0),
        new("fuelHour", "Fuel use per hour (kg)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.FuelUsePerHour),
        new("water", "Water temperature", ChannelKind.Live, ChannelUnit.Temp, v => v.Frame.WaterTemp, 0),
        new("oil", "Oil temperature", ChannelKind.Live, ChannelUnit.Temp, v => v.Frame.OilTemp, 0),
        new("oilPress", "Oil pressure (bar)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.OilPress),
        new("fuelPress", "Fuel pressure (bar)", ChannelKind.Live, ChannelUnit.None, v => v.Frame.FuelPress),
        new("voltage", "Voltage", ChannelKind.Live, ChannelUnit.None, v => v.Frame.Voltage),
        new("bias", "Brake bias", ChannelKind.Live, ChannelUnit.None, v => v.Frame.BrakeBias),
        new("tc", "Traction control", ChannelKind.Live, ChannelUnit.None, v => v.Frame.TractionControl, 0),
        new("abs", "ABS", ChannelKind.Live, ChannelUnit.None, v => v.Frame.Abs, 0),
        new("latG", "Lateral G", ChannelKind.Live, ChannelUnit.G, v => v.Frame.LatAccel, 2),
        new("longG", "Longitudinal G", ChannelKind.Live, ChannelUnit.G, v => v.Frame.LongAccel, 2),
        new("battery", "Battery / ERS", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.BatteryPct is { } b ? (b > 1 ? b / 100 : b) : null, 0),
        new("air", "Air temperature", ChannelKind.Live, ChannelUnit.Temp, v => v.Frame.AirTemp),
        new("track", "Track temperature", ChannelKind.Live, ChannelUnit.Temp, v => v.Frame.TrackTemp),
        new("wind", "Wind speed", ChannelKind.Live, ChannelUnit.Speed, v => v.Frame.WindSpeed, 0),
        new("humidity", "Humidity", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.Humidity, 0),
        new("rain", "Precipitation", ChannelKind.Live, ChannelUnit.Percent, v => v.Frame.Precipitation, 0),
        new("lap", "Current lap time", ChannelKind.Live, ChannelUnit.LapTime, v => v.Frame.LapCurrent),
        new("last", "Last lap", ChannelKind.Live, ChannelUnit.LapTime, v => v.Frame.LapLast),
        new("best", "Best lap", ChannelKind.Live, ChannelUnit.LapTime, v => v.Frame.LapBest),
        new("deltaBest", "Delta to best lap", ChannelKind.Live, ChannelUnit.Seconds, v => v.Frame.DeltaBestOk ? v.Frame.DeltaBest : null, 2),
        new("deltaSession", "Delta to session best", ChannelKind.Live, ChannelUnit.Seconds, v => v.Frame.DeltaSessionBestOk ? v.Frame.DeltaSessionBest : null, 2),
        new("incidents", "My incidents", ChannelKind.Live, ChannelUnit.Count, v => v.Frame.Incidents, 0),
        new("position", "Position (overall)", ChannelKind.Live, ChannelUnit.Count, v => Pos(v, v.Frame.CarPosition), 0),
        new("classPosition", "Position in class", ChannelKind.Live, ChannelUnit.Count, v => v.Race.StandingOf(v.Frame.PlayerCarIdx)?.ClassPosition is > 0 and var c ? c : null, 0),
        new("timeLeft", "Session time left", ChannelKind.Live, ChannelUnit.LapTime, v => v.Frame.SessionTimeRemain, 0),
        new("lapsLeft", "Session laps left", ChannelKind.Live, ChannelUnit.Count, v => v.Frame.SessionLapsRemain, 0),
        new("trackLength", "Track length (km)", ChannelKind.Session, ChannelUnit.None, v => v.Frame.Session.TrackLengthKm, 2),
        new("pitLimit", "Pit speed limit", ChannelKind.Session, ChannelUnit.Speed, v => v.Frame.Session.PitSpeedLimitKph / 3.6, 0),
        new("tank", "Usable tank", ChannelKind.Session, ChannelUnit.Fuel, v => v.Race.Tank),
        new("incLimit", "Incident limit", ChannelKind.Session, ChannelUnit.Count, v => v.Frame.Session.IncidentLimit, 0),
        new("fuelPerLap", "Fuel per lap (average)", ChannelKind.Calculated, ChannelUnit.Fuel, v => v.Race.FuelPerLap, 2),
        new("fuelLaps", "Laps of fuel", ChannelKind.Calculated, ChannelUnit.None, v => v.Race.Fuel?.LapsOfFuel),
        new("fuelToFinish", "Fuel to the flag (estimate)", ChannelKind.Calculated, ChannelUnit.Fuel, v => v.Race.Fuel?.ToFinish),
        new("fuelSurplus", "Spare fuel at the flag (estimate)", ChannelKind.Calculated, ChannelUnit.Fuel, v => v.Race.Fuel?.Surplus),
        new("lapsToGo", "Laps to go (estimate)", ChannelKind.Calculated, ChannelUnit.None, v => v.Race.Fuel?.LapsToGo ?? v.Race.LapsToGo(v.Frame)),
        new("gapAhead", "Gap to the car ahead", ChannelKind.Calculated, ChannelUnit.Seconds, v => Neighbour(v, true), 1),
        new("gapBehind", "Gap to the car behind", ChannelKind.Calculated, ChannelUnit.Seconds, v => Neighbour(v, false), 1),
        new("pitTimer", "Time on pit road", ChannelKind.Calculated, ChannelUnit.Seconds, v => v.Race.PitTimer, 1),
        new("stint", "Stint time", ChannelKind.History, ChannelUnit.LapTime, v => v.Race.StintStart is { } s && v.Frame.SessionTime is { } t ? t - s : null, 0),
        new("stintLaps", "Stint laps", ChannelKind.History, ChannelUnit.Count, v => v.Frame.LapCompleted is { } l ? l - v.Race.StintStartLap : null, 0),
        new("avgLap", "Average lap (last 5 clean)", ChannelKind.History, ChannelUnit.LapTime, v => LapMath.Stats(v.Race.Laps.Laps, 5)?.Average),
        new("consistency", "Lap spread (std. dev.)", ChannelKind.History, ChannelUnit.Seconds, v => LapMath.Stats(v.Race.Laps.Laps, 10)?.StdDev, 3),
    };

    public static HudChannel? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    private static double? Pos(HudView v, int[] positions) =>
        (uint)v.Frame.PlayerCarIdx < TelemetryFrame.MaxCars && positions[v.Frame.PlayerCarIdx] > 0 ? positions[v.Frame.PlayerCarIdx] : null;

    private static double? Neighbour(HudView v, bool ahead)
    {
        var rows = FieldOrder.Relative(v.Frame, ahead ? 1 : 0, ahead ? 0 : 1, profile: v.Race.Profile);
        foreach (var r in rows) if (!r.IsPlayer) return r.Gap is { } g ? Math.Abs(g) : null;
        return null;
    }

    public static double Convert(double value, ChannelUnit unit, HudUnits u) => unit switch
    {
        ChannelUnit.Speed => RaceFormat.SpeedValue(value, u),
        ChannelUnit.Temp => u == HudUnits.Imperial ? value * 9 / 5 + 32 : value,
        ChannelUnit.Pressure => u == HudUnits.Imperial ? value * 0.145038 : value,
        ChannelUnit.Fuel => RaceFormat.FuelValue(value, u),
        ChannelUnit.Percent => value * 100,
        ChannelUnit.G => value / 9.81,
        _ => value,
    };

    public static string UnitText(ChannelUnit unit, HudUnits u) => unit switch
    {
        ChannelUnit.Speed => RaceFormat.SpeedUnit(u),
        ChannelUnit.Temp => u == HudUnits.Imperial ? "°F" : "°C",
        ChannelUnit.Pressure => u == HudUnits.Imperial ? "psi" : "kPa",
        ChannelUnit.Fuel => RaceFormat.FuelUnit(u),
        ChannelUnit.Seconds => "s",
        ChannelUnit.Percent => "%",
        ChannelUnit.Rpm => "rpm",
        ChannelUnit.G => "g",
        ChannelUnit.Degrees => "°",
        _ => "",
    };

    public static string KindText(ChannelKind k) => k switch
    {
        ChannelKind.Live => "live",
        ChannelKind.Session => "session",
        ChannelKind.Calculated => "calculated",
        _ => "recorded",
    };
}

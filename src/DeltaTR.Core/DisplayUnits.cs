namespace DeltaTR.Core;

/// <summary>Convert a snapshot for the dashboard and radio. The adapter and calibration keep native measurements.</summary>
public static class DisplayUnits
{
    public static RaceSnapshot? ForDisplay(RaceSnapshot? source, bool imperial)
    {
        if (source is null) return null;
        var broadcast = source.Broadcast;
        if (imperial && broadcast is { Driving: true })
            broadcast = broadcast with { Fields = broadcast.Fields.ToDictionary(p => p.Key, p =>
                p.Value.ImperialText is { } text ? p.Value with { TextValue = text } : p.Value) };
        var native = source.Native;
        if (imperial && native is { } original)
            native = original with { Read = key => { var m = original.Read(key); return m is { TextValue: null } ? Convert(m,true) : m; } };
        return source with { Native = native, Fields = source.Fields.ToDictionary(p => p.Key, p => Convert(p.Value, imperial)), Broadcast = broadcast };
    }

    private static Measurement Convert(Measurement m, bool imperial) => (m.Unit, imperial) switch
    {
        ("°C", true) => m with { Value = m.Value * 1.8 + 32, Unit = "°F" },
        ("km/h", true) => m with { Value = m.Value / 1.609344, Unit = "mph" },
        ("L", true) => m with { Value = m.Value / 4.54609, Unit = "imp gal" },
        ("L/lap", true) => m with { Value = m.Value / 4.54609, Unit = "imp gal/lap" },
        ("kg", true) => m with { Value = m.Value / 0.45359237, Unit = "lb" },
        _ => m
    };
}

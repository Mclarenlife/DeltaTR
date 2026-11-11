namespace DeltaTR.Telemetry.ACC.Mapping;

/// <summary>Distinguish seconds from milliseconds by observing the active ACC countdown.</summary>
internal sealed class SessionClockUnits
{
    private long sampleTime;
    private double? sample;
    public string? Unit { get; private set; }

    public void Reset()
    {
        sample = null;
        Unit = null;
    }

    public void Observe(double remaining, long elapsed)
    {
        if (!double.IsFinite(remaining) || remaining < 0) { Reset(); return; }
        if (Unit is not null) return;
        if (sample.HasValue)
        {
            var interval = elapsed - sampleTime;
            if (interval < 1800) return;
            var rate = (sample.Value - remaining) * 1000 / interval;
            Unit = rate switch
            {
                >= 0.4 and <= 1.6 => "s",
                >= 400 and <= 1600 => "ms",
                _ => null
            };
        }
        sample = remaining;
        sampleTime = elapsed;
    }
}

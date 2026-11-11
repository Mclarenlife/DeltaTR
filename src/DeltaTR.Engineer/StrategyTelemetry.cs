using DeltaTR.Core;

namespace DeltaTR.Engineer;

public sealed record StrategyLap(int CompletedLap, double Seconds, double? FuelUsedLitres, DateTimeOffset ObservedAt);

/// <summary>Bounded in-memory lap samples, reset on session/source changes. No recording files.</summary>
public sealed class StrategyTelemetry
{
    private readonly List<StrategyLap> laps = [];
    private (RacingGame, DataOrigin, string)? session;
    private int? completed, pendingLap;
    private DateTimeOffset pendingSince, lastFrame;
    private double? lastFuel, observedFuel;
    private bool pitDuringLap;
    public IReadOnlyList<StrategyLap> Laps => laps;

    public void Observe(RaceSnapshot? frame, DateTimeOffset now)
    {
        if (frame == null || frame.Phase != SessionPhase.Live || !RaceReports.Fresh(frame.CapturedAt, now))
        {
            completed = pendingLap = null; lastFuel = observedFuel = null; pitDuringLap = false;
            if (frame == null || session?.Item3 != frame.SessionId) { laps.Clear(); session = null; }
            return;
        }
        var identity = (frame.Game, frame.Origin, frame.SessionId);
        if (session != identity || !RaceReports.Fresh(lastFrame, now))
        {
            if (session != identity) laps.Clear();
            completed = pendingLap = null; lastFuel = observedFuel = null; pitDuringLap = false; session = identity;
        }
        lastFrame = now;
        var count = RaceReports.Number(frame, "completedLaps", now);
        var fuel = frame.Reading("fuel")?.Unit == "L" ? RaceReports.Number(frame, "fuel", now) : null;
        pitDuringLap |= RaceReports.Number(frame, "inPitLane", now) == 1 ||
            RaceReports.Number(frame, "validLap", now) == 0 || fuel > observedFuel + .02;
        observedFuel = fuel;
        if (count is not (>= 0 and <= 100000)) return;
        int lap = (int)count;
        if (completed == null) { completed = lap; return; }
        if (lap != completed)
        {
            if (lap < completed) laps.Clear();
            pendingLap = lap == completed + 1 ? lap : null;
            pendingSince = now;
            if (pendingLap == null) lastFuel = null;
            completed = lap;
        }
        // Allow the lap-time field to catch up with the completed-lap counter.
        if (pendingLap is not int finished || now - pendingSince < TimeSpan.FromMilliseconds(600)) return;
        var reading = frame.Reading("lastLapTime");
        if (reading?.IsUsable(now, Metrics.MaxAge) != true || reading.ObservedAt <= pendingSince ||
            reading.Value is not (>= 10000 and <= 3600000) || reading.Unit != "ms") return;
        double? used = !pitDuringLap && lastFuel.HasValue && fuel.HasValue && lastFuel > fuel ? lastFuel - fuel : null;
        if (!pitDuringLap)
        {
            laps.Add(new(finished, reading.Value.Value / 1000, used, reading.ObservedAt));
            if (laps.Count > 6) laps.RemoveAt(0);
        }
        lastFuel = fuel; pendingLap = null; pitDuringLap = false;
    }

    public static IReadOnlyDictionary<string, Measurement> ExtraReadings(RaceSnapshot frame, DateTimeOffset now)
    {
        string[] keys = [
            "gameFuelPerLap", "gameFuelEstimatedLaps", "missingMandatoryPits",
            "Static.PitWindowStart", "Static.PitWindowEnd", "Graphics.mandatoryPitDone",
            "Graphics.tyreCompound", "rainIntensity", "rainIn10Minutes",
            "LMU.Physics.mVirtualEnergy", "LMU.Session.mRaining",
            "LMU.Wheel.mWear.FL", "LMU.Wheel.mWear.FR", "LMU.Wheel.mWear.RL", "LMU.Wheel.mWear.RR"
        ];
        var fields = new Dictionary<string, Measurement>();
        foreach (string key in keys)
        {
            var reading = frame.Reading(key);
            if (reading != null && RaceReports.Fresh(reading.ObservedAt, now) &&
                (reading.IsUsable(now, Metrics.MaxAge) || reading.Quality == DataQuality.Estimated && reading.Value is double value && double.IsFinite(value)))
                fields[key] = reading;
        }
        return fields;
    }
}

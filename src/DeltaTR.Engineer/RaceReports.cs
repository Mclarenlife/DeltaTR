using DeltaTR.Core;

namespace DeltaTR.Engineer;

/// <summary>Per-session samples used only for explicitly labelled estimates, never fabricated live channels.</summary>
public sealed class RaceReports
{
    private sealed record Sample(double Progress, DateTimeOffset At);
    private readonly Dictionary<int, List<Sample>> paths = [];
    private (RacingGame, DataOrigin, string)? session;

    public RaceSnapshot? Observe(RaceSnapshot? frame, DateTimeOffset now)
    {
        if (frame == null || frame.Phase != SessionPhase.Live || !Fresh(frame.CapturedAt, now))
        {
            paths.Clear(); session = null;
            return frame;
        }
        var key = (frame.Game, frame.Origin, frame.SessionId);
        if (session != key) { paths.Clear(); session = key; }
        if (frame.Traffic is not { } traffic || !Fresh(traffic.ObservedAt, now)) return frame;
        var grid = new List<RaceCar>();
        foreach (var car in traffic.Cars)
        {
            var updated = car;
            if (!Fresh(car.ObservedAt, now) || car.InPits || car.Progress is not double progress || !double.IsFinite(progress))
            {
                paths.Remove(car.Id); grid.Add(car); continue;
            }
            if (!paths.TryGetValue(car.Id, out var samples)) paths[car.Id] = samples = [];
            var last = samples.LastOrDefault();
            if (last != null && (car.ObservedAt < last.At || car.ObservedAt - last.At > Metrics.MaxAge ||
                progress < last.Progress || progress - last.Progress > .1)) samples.Clear();
            if (samples.Count == 0 || car.ObservedAt > samples[^1].At)
                samples.Add(new(progress, car.ObservedAt));
            samples.RemoveAll(s => car.ObservedAt - s.At > TimeSpan.FromMinutes(2));
            // A short rolling interval suppresses network quantisation. Reject warps and pit transitions.
            var older = samples.LastOrDefault(s => car.ObservedAt - s.At >= TimeSpan.FromMilliseconds(500));
            if (car.SpeedKph == null && traffic.TrackLength is > 0 && older != null &&
                car.ObservedAt - older.At <= TimeSpan.FromSeconds(1.5))
            {
                double speed = (progress - older.Progress) * traffic.TrackLength.Value / (car.ObservedAt - older.At).TotalSeconds * 3.6;
                if (double.IsFinite(speed) && speed is >= 0 and <= 400) updated = car with { SpeedKph = speed, EstimatedSpeed = true };
            }
            grid.Add(updated);
        }
        foreach (int removed in paths.Keys.Except(grid.Select(c => c.Id)).ToArray()) paths.Remove(removed);
        return frame with { Traffic = traffic with { Cars = grid } };
    }

    public string Gap(RaceSnapshot frame, DateTimeOffset now, bool leader)
    {
        var rank = Position(frame, now)?.Value;
        if (rank == 1) return "你目前领跑。";
        string label = leader ? "头车" : "前车";
        var traffic = frame.Traffic;
        var own = traffic?.Player;
        var target = traffic?.AtPosition(leader ? 1 : (int)(rank ?? 0) - 1);
        var lapped = Number(frame, leader ? "lapsBehindLeader" : "lapsBehindAhead", now);
        if (lapped is > 0) return $"落后{label} {lapped:0} 圈。";
        if (own?.Progress is double ownProgress && target?.Progress is double targetProgress &&
            Fresh(own.ObservedAt, now) && Fresh(target.ObservedAt, now) && targetProgress - ownProgress >= 1)
            return $"落后{label}至少 {(int)(targetProgress - ownProgress)} 圈。";
        var reading = frame.Reading(leader ? "gapLeader" : "gapAhead");
        if (reading?.IsUsable(now, Metrics.MaxAge) == true && reading.Value is >= 0 && reading.Unit is "s" or "ms")
            return $"落后{label} {(reading.Unit == "ms" ? reading.Value / 1000 : reading.Value):0.0} 秒。";
        if (own?.Progress is double progress && target != null && Fresh(own.ObservedAt, now) && Fresh(target.ObservedAt, now) &&
            paths.TryGetValue(target.Id, out var samples))
        {
            for (int i = 1; i < samples.Count; i++)
            {
                var before = samples[i - 1]; var after = samples[i];
                if (before.Progress <= progress && after.Progress > progress && after.At - before.At <= Metrics.MaxAge)
                {
                    double fraction = (progress - before.Progress) / (after.Progress - before.Progress);
                    var crossing = before.At + (after.At - before.At) * fraction;
                    double gap = (own.ObservedAt - crossing).TotalSeconds;
                    if (gap is >= 0 and <= 120) return $"估计落后{label} {gap:0.0} 秒。";
                }
            }
        }
        return $"与{label}的秒差暂不可用。";
    }

    public string GapBehind(RaceSnapshot frame, DateTimeOffset now)
    {
        var own = frame.Traffic?.Player;
        var follower = own == null ? null : frame.Traffic?.AtPosition(own.Position + 1);
        if (own?.Progress is double ownProgress && follower?.Progress is double followerProgress &&
            Fresh(own.ObservedAt, now) && Fresh(follower.ObservedAt, now) && ownProgress - followerProgress >= 1)
            return $"领先后车至少 {(int)(ownProgress - followerProgress)} 圈。";
        var reading = frame.Reading("gapBehind");
        if (reading?.IsUsable(now, Metrics.MaxAge) == true && reading.Value >= 0 && reading.Unit is "s" or "ms")
            return $"领先后车 {(reading.Unit == "ms" ? reading.Value / 1000 : reading.Value):0.0} 秒。";
        return "与后车的秒差暂不可用。";
    }

    public static Measurement? Position(RaceSnapshot frame, DateTimeOffset now)
    {
        if (frame.Traffic is { } traffic && Fresh(traffic.ObservedAt, now) &&
            traffic.Player is { Position: >= 1 and <= 255 } own && Fresh(own.ObservedAt, now) &&
            traffic.AtPosition(own.Position)?.Id == own.Id)
            return new(own.Position, "position", own.ObservedAt, DataQuality.Valid, "RaceTraffic", "");
        var reading = frame.Reading("position");
        return reading?.IsUsable(now, Metrics.MaxAge) == true && reading.Value is >= 1 and <= 255 ? reading : null;
    }

    public static string SpeedDifference(RaceSnapshot frame, RaceCar? target, DateTimeOffset now, bool imperial)
    {
        if (target == null || !Fresh(target.ObservedAt, now) || target.SpeedKph is not (>= 0 and <= 600) ||
            Number(frame, "speed", now) is not double speed) return "对手速度暂不可用。";
        var reading = frame.Reading("speed")!;
        if (reading.Unit == "mph") speed *= 1.609344;
        else if (reading.Unit != "km/h") return "对手速度暂不可用。";
        double delta = (target.SpeedKph.Value - speed) / (imperial ? 1.609344 : 1);
        var prefix = target.EstimatedSpeed ? "估计对方当前" : "对方当前";
        if (Math.Abs(delta) < .5) return prefix + "与你速度相近。";
        return $"{prefix}比你{(delta > 0 ? "快" : "慢")} {Math.Abs(delta):0.0} {(imperial ? "mph" : "km/h")}。";
    }

    public static double? Number(RaceSnapshot frame, string key, DateTimeOffset now) =>
        frame.Reading(key) is { } reading && reading.IsUsable(now, Metrics.MaxAge) ? reading.Value : null;
    public static bool Fresh(DateTimeOffset stamp, DateTimeOffset now) => now >= stamp && now - stamp <= Metrics.MaxAge;
}

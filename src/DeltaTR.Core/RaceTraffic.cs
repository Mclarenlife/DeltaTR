namespace DeltaTR.Core;

// Speed is km/h; progress counts completed laps plus the fraction of the current lap.
public sealed record RaceCar(int Id, int Position, string Name, DateTimeOffset ObservedAt,
    double? SpeedKph = null, bool EstimatedSpeed = false, double? Progress = null, bool InPits = false);

public sealed record RaceTraffic(int PlayerId, IReadOnlyList<RaceCar> Cars, DateTimeOffset ObservedAt,
    bool? Racing = null, bool? Finished = null, double? TrackLength = null)
{
    public RaceCar? Player => Cars.FirstOrDefault(c => c.Id == PlayerId);
    public RaceCar? AtPosition(int position)
    {
        var matches = Cars.Where(c => c.Position == position).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}

namespace DeltaTR.Core;

// Catalogs are shared across frames. Read values on demand so a full grid of cars
// does not generate thousands of strings on every physics tick.
public sealed record NativeField(string Key, string Title, string Group, string Detail = "");
public sealed record NativeTelemetry(IReadOnlyList<NativeField> Catalog, Func<string, Measurement?> Read,
    string RaceStatus);

public static class SimulatorRace
{
    public static readonly NativeField[] Fields = [
        new("RaceFeed.standings", "比赛排名表", "赛事播报"),
        new("RaceFeed.ahead", "前车是谁", "赛事播报"),
        new("RaceFeed.behind", "后车是谁", "赛事播报"),
        new("RaceFeed.leader", "谁在领跑", "赛事播报"),
        new("RaceFeed.flags", "当前赛事旗语", "赛事播报")
    ];

    public sealed record Car(int Id, int Position, string Name);
    public static Dictionary<string, string> Reports(IEnumerable<Car> entries, int playerId, string? flags)
    {
        var cars = entries.Where(c => c.Position > 0 && c.Id >= 0).OrderBy(c => c.Position).ToArray();
        var reports = new Dictionary<string,string>();
        if (!string.IsNullOrWhiteSpace(flags)) reports["RaceFeed.flags"] = flags;
        string Label(Car c) => string.IsNullOrWhiteSpace(c.Name) ? $"车辆编号{c.Id}" : c.Name;
        if (cars.Length == 0) return reports;
        reports["RaceFeed.standings"] = string.Join("；", cars.Select(c => $"第{c.Position}名 {Label(c)}"));
        void Position(string key, int place)
        {
            var matches = cars.Where(c => c.Position == place).Take(2).ToArray();
            if (matches.Length == 1) reports["RaceFeed." + key] = $"总排名第{place}名，{Label(matches[0])}";
        }
        Position("leader",1);
        var player = cars.Where(c => c.Id == playerId).Take(2).ToArray();
        if (player.Length == 1 && cars.Count(c => c.Position == player[0].Position) == 1)
        { Position("ahead",player[0].Position - 1); Position("behind",player[0].Position + 1); }
        return reports;
    }
    public static Measurement? Text(string? text, DateTimeOffset time, string source, string unit = "") =>
        string.IsNullOrWhiteSpace(text) ? null : new Measurement(null,unit,time,DataQuality.Valid,source,"") { TextValue=text };
}

namespace DeltaTR.Core;

public sealed record BroadcastField(string Key, string Title);
public sealed record BroadcastFrame(string Status, bool Driving, IReadOnlyDictionary<string, Measurement> Fields)
{
    public RaceTraffic? Traffic { get; init; }
    public static BroadcastFrame Empty(string status) => new(status, false, new Dictionary<string, Measurement>());
}

public static class BroadcastRadio
{
    public static readonly (string Key, string Title)[] Topics = [
        ("driver", "车手姓名"), ("entry", "参赛资料"), ("roster", "车手名单"), ("position", "各项排名"),
        ("motion", "行驶状态"), ("best", "最佳圈"), ("last", "上一圈"), ("current", "当前圈")
    ];
    public static IReadOnlyList<BroadcastField> Fields { get; } = Build().ToArray();
    private static IEnumerable<BroadcastField> Build()
    {
        foreach (var (key, title) in new (string, string)[] {
            ("session", "广播赛事阶段"), ("timing", "广播赛事时间"), ("weather", "广播天气"),
            ("camera", "当前转播视角"), ("track", "广播赛道资料"), ("cameras", "可用转播视角"),
            ("hud", "可用转播界面"), ("best", "全场最快圈"), ("event", "最近赛事事件"),
            ("entries", "参赛车辆名单"), ("standings", "比赛排名表"), ("sectors", "最佳圈分段排名") })
            yield return new("UDP." + key, title);
        foreach (var (key, title) in new[] { ("self", "本车"), ("ahead", "前车"), ("behind", "后车"), ("leader", "领跑车") })
            foreach (var topic in Topics)
                yield return new($"UDP.{key}.{topic.Key}", title + topic.Title);
        for (var rank = 1; rank <= 60; rank++)
            yield return new($"UDP.rank.{rank}", $"第{rank}名的详细赛况");
    }
}

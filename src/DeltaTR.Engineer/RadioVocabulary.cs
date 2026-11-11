using DeltaTR.Core;

namespace DeltaTR.Engineer;

public sealed record RadioField(string Key, string Title, string Group, string Detail = "");

public static class RadioVocabulary
{
    public static IReadOnlyList<RadioField> Fields { get; } = [
        new("airTemperature", "气温", "赛道环境"), new("roadTemperature", "路温", "赛道环境"),
        new("rainIntensity", "当前雨量", "赛道环境"), new("rainIn10Minutes", "十分钟后雨量", "赛道环境"),
        new("rainIn30Minutes", "三十分钟后雨量", "赛道环境"), new("trackGrip", "抓地状态代码", "赛道环境"),
        new("temperature", "四轮胎温", "轮胎"), new("pressure", "四轮胎压", "轮胎"),
        .. WheelFields(),
        new("rainTyres", "使用雨胎", "轮胎"), new("tyreSet", "轮胎套组编号", "轮胎"),
        new("speed", "车速", "车辆"), new("gear", "挡位代码", "车辆"), new("rpm", "发动机转速", "车辆"),
        new("fuel", "剩余油量", "燃油"), new("gameFuelPerLap", "游戏估算每圈油耗", "燃油"),
        new("gameFuelEstimatedLaps", "游戏估算燃油圈数", "燃油"),
        new("position", "排名", "比赛"), new("completedLaps", "已完成圈数", "比赛"),
        new("lastLapTime", "上一圈用时", "比赛"), new("validLap", "当前圈有效", "比赛"),
        new("gapAhead", "前车差距", "比赛"), new("gapBehind", "后车差距", "比赛"),
        new("sessionTimeLeft", "阶段剩余时间", "比赛"), new("missingMandatoryPits", "剩余强制进站", "比赛"),
        new("inPitLane", "位于维修区", "比赛"),
        .. AccChannels.WheelGroups.Select(g => new RadioField(g.Key, "四轮" + g.Value[0].Channel.Title, g.Value[0].Channel.Group)),
        .. AccChannels.Readings.Select(r => new RadioField(r.Key, r.Title, r.Channel.Group)),
        .. BroadcastRadio.Fields.Select(f => new RadioField(f.Key, f.Title, "赛事广播"))
    ];

    public static IReadOnlyList<RadioField> For(RacingGame game, IReadOnlyList<NativeField>? current = null)
    {
        if (game == RacingGame.ACC) return Fields;
        string[] common = ["speed","fuel","gear","rpm","position","completedLaps","lastLapTime","airTemperature",
            "roadTemperature","sessionTimeLeft","inPitLane","Physics.waterTemp","gapAhead"];
        var selected = Fields.Where(f => common.Contains(f.Key)).Select(f => f.Key == "gear" ? f with { Title = "挡位" } : f).ToList();
        if (game == RacingGame.LMU)
            selected.AddRange(Fields.Where(f => f.Key is "pressure" or "temperature" ||
                f.Key.StartsWith("pressure.") || f.Key.StartsWith("temperature.") || f.Key.StartsWith("Physics.brakeTemp")));
        else
        {
            selected.Add(new("pitTemp", "四轮上次测量中部胎体温度", "轮胎报告"));
            string[] names = ["左前","右前","左后","右后"];
            for (var i = 0; i < names.Length; i++)
            {
                var w = Metrics.Wheels[i];
                selected.Add(new("coldPressure." + w, names[i] + "冷胎压", "冷胎设定"));
                selected.Add(new("pitTemp." + w, names[i] + "上次测量中部胎体温度", "轮胎报告"));
                selected.Add(new("pitTread." + w, names[i] + "上次测量胎面中部剩余比例", "轮胎报告"));
            }
        }
        var native = current ?? (game == RacingGame.LMU ? LmuSchema.Fields : IRacingChannels.ReferenceFields);
        selected.AddRange(native.Select(f=>new RadioField(f.Key,f.Title,f.Group,f.Detail)));
        return selected;
    }

    private static IEnumerable<RadioField> WheelFields()
    {
        string[] names = ["左前", "右前", "左后", "右后"];
        for (var i = 0; i < Metrics.Wheels.Length; i++)
        {
            yield return new("temperature." + Metrics.Wheels[i], names[i] + "胎温", "轮胎");
            yield return new("pressure." + Metrics.Wheels[i], names[i] + "胎压", "轮胎");
        }
    }
}

using DeltaTR.Core;

namespace DeltaTR.Engineer;

internal static class RadioPhrasebook
{
    internal static IEnumerable<(string Metric, string Phrase)> Entries()
    {
        (string Metric, string Phrases)[] groups = [
            ("rainIntensity", "现在下雨了吗 赛道在下雨吗 当前雨势 雨下得大吗 现在是什么雨量"),
            ("rainIn10Minutes", "十分钟后会下雨吗 十分钟后的天气 十分钟以后雨大吗 报一下十分钟雨情 未来十分钟的雨量"),
            ("rainIn30Minutes", "半小时后会下雨吗 三十分钟后雨大吗 半小时后的天气 报一下半小时雨情 半小时后雨量多少"),
            ("trackGrip", "赛道抓地怎么样 当前抓地状态 路面抓地如何 报一下赛道抓地 赛道抓地状态是多少"),
            ("rainTyres", "现在用的是雨胎吗 我装了雨胎吗 车上是雨胎吗 当前是否使用雨胎 现在跑的是雨胎吗"),
            ("tyreSet", "现在用第几套胎 当前是第几套轮胎 轮胎套组是多少 报一下轮胎组号 我用的是哪套胎"),
            ("gear", "现在几挡 当前挡位 我挂在几挡 报一下挡位 现在用的是什么挡"),
            ("rpm", "发动机多少转 现在转速多少 当前发动机转速 报一下转速 引擎转速是多少"),
            ("gameFuelPerLap", "每圈耗油多少 一圈用多少油 报一下单圈油耗 平均一圈几升油 每圈油耗是多少"),
            ("gameFuelEstimatedLaps", "剩下的油还能跑几圈 燃油还能撑几圈 这箱油够跑多少圈 还能跑多少圈油 报一下燃油续航圈数"),
            ("position", "我排第几名 目前排名多少 我现在排第几 报一下我的名次 我的比赛排名"),
            ("completedLaps", "已经跑了几圈 我完成了多少圈 已完成多少圈 报一下已跑圈数 跑完几圈了"),
            ("lastLapTime", "上一圈跑了多久 上一圈圈速是多少 刚才那圈几分几秒 报一下上一圈用时 我上一圈多快"),
            ("validLap", "这一圈有效吗 当前圈还有效吗 这圈成绩算数吗 我这圈有没有被取消 这圈有效不有效"),
            ("gapAhead", "我离前车几秒 距离前车多远 前面那台车领先几秒 报一下前车时间差 和前车相差多少秒"),
            ("gapBehind", "后车离我几秒 我领先后车多少秒 后面那台车差多远 报一下后车时间差 和后车相差多少秒"),
            ("sessionTimeLeft", "比赛还剩多久 这节还有多长时间 还剩几分钟 报一下剩余时间 距离结束还有多久"),
            ("missingMandatoryPits", "还差几次强制进站 强制进站还剩几次 我还要进几次站 还有强制进站吗 报一下剩余强制进站次数"),
            ("inPitLane", "我在维修区吗 当前在维修通道吗 现在是在维修区里吗 我进维修区了吗 车在维修通道里吗"),
            ("airTemperature", "外面多少度 现在空气多少度"),
            ("speed", "我现在开多快 报一下车速"), ("fuel", "油箱里还有多少油 报一下剩余燃油")
        ];
        foreach (var (metric, phrases) in groups)
            foreach (var phrase in phrases.Split(' ')) yield return (metric, phrase);

        foreach (var field in RadioVocabulary.Fields.Where(f => f.Key.StartsWith("Physics.") ||
            f.Key.StartsWith("Graphics.") || f.Key.StartsWith("Static.") || f.Key.StartsWith("UDP.")))
        {
            yield return (field.Key, field.Title);
            yield return (field.Key, field.Title + "多少");
            yield return (field.Key, "当前" + field.Title);
            yield return (field.Key, "现在的" + field.Title);
            yield return (field.Key, "报告" + field.Title);
            yield return (field.Key, "帮我查一下" + field.Title);
        }
        foreach (var entry in new (string Key, string Phrase)[] {
            ("Physics.brakeTemp", "刹车温度多少"), ("Physics.brakeTemp", "刹车温度"),
            ("Physics.waterTemp", "水温多少"), ("Physics.brakeBias", "刹车平衡是多少"),
            ("Graphics.TC", "TC几档"), ("Graphics.ABS", "ABS几档"), ("Graphics.EngineMap", "发动机映射几档"),
            ("Graphics.mfdFuelToAdd", "进站要加多少油"), ("Graphics.iBestTime", "最快圈是多少"),
            ("UDP.ahead.driver", "前车是谁"), ("UDP.behind.driver", "后车是谁"), ("UDP.ahead.last", "前车上一圈多少"),
            ("UDP.standings", "报一下排行榜"), ("UDP.sectors", "分段排名"), ("UDP.best", "谁做了最快圈"),
            ("Graphics.flag", "现在是什么旗"), ("Graphics.driverStintTimeLeft", "这段还能开多久")
        }) yield return entry;

        string[] corners = ["左前", "右前", "左后", "右后"];
        for (var i = 0; i < corners.Length; i++)
        {
            var corner = corners[i];
            var wheel = Metrics.Wheels[i];
            foreach (var phrase in new[] { $"{corner}胎几度", $"{corner}胎温多少", $"{corner}轮胎温度是多少", $"报一下{corner}胎温", $"{corner}轮现在多少度" })
                yield return ("temperature." + wheel, phrase);
            foreach (var phrase in new[] { $"{corner}胎压多少", $"{corner}轮压力是多少", $"报一下{corner}胎压", $"{corner}轮胎压怎么样", $"{corner}轮现在的胎压" })
                yield return ("pressure." + wheel, phrase);
        }
    }
}

using System.Globalization;
using DeltaTR.Core;

namespace DeltaTR.Engineer;

public sealed record EngineerReply(string Text, bool HasVerifiedData, string[] EvidenceFields);

/// <summary>Deterministic radio queries shared by text and local voice; all answers obey telemetry quality gates.</summary>
public sealed class TelemetryQueries
{
    private static readonly Dictionary<string, string> Aliases = BuildAliases();

    private static Dictionary<string, string> BuildAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["气温"] = "airTemperature", ["气温多少"] = "airTemperature", ["空气温度"] = "airTemperature", ["air temperature"] = "airTemperature",
            ["路温"] = "roadTemperature", ["赛道温度"] = "roadTemperature", ["track temperature"] = "roadTemperature",
            ["胎压"] = "pressure", ["现在胎压怎么样"] = "pressure", ["胎压多少"] = "pressure", ["胎压怎么样"] = "pressure", ["tyre pressure"] = "pressure",
            ["胎温"] = "temperature", ["轮胎温度"] = "temperature", ["轮胎温度多少"] = "temperature", ["胎温多少"] = "temperature", ["胎温怎么样"] = "temperature", ["tyre temperature"] = "temperature",
            ["油量"] = "fuel", ["还剩多少油"] = "fuel", ["fuel"] = "fuel",
            ["排名"] = "position", ["我现在第几"] = "position", ["position"] = "position",
            ["上一圈"] = "lastLapTime", ["刚才圈速"] = "lastLapTime", ["last lap"] = "lastLapTime",
            ["前车差多少"] = "gapAhead", ["gap ahead"] = "gapAhead",
            ["天气怎么样"] = "rainIntensity", ["下雨吗"] = "rainIntensity", ["weather"] = "rainIntensity",
            ["还要进站吗"] = "missingMandatoryPits",
            ["车速"] = "speed", ["现在车速多少"] = "speed",
            ["后车差多少"] = "gapBehind", ["还剩多少时间"] = "sessionTimeLeft",
            ["车辆状况"] = "vehicle", ["车辆怎么样"] = "vehicle",
            ["赛场状况"] = "race", ["比赛情况"] = "race"
        };
        AddReadings("temperature", ["胎温", "轮胎温度"], ["多少", "是多少", "怎么样", "多少度"]);
        AddReadings("pressure", ["胎压", "轮胎压力"], ["多少", "是多少", "怎么样"]);
        AddReadings("fuel", ["油量", "剩余油量"], ["多少", "是多少"]);
        AddReadings("speed", ["车速"], ["多少", "是多少"]);
        AddReadings("roadTemperature", ["路温", "赛道温度"], ["多少", "是多少", "多少度"]);
        AddReadings("airTemperature", ["气温"], ["多少", "是多少", "多少度"]);
        foreach (var field in RadioVocabulary.Fields)
            if (!field.Key.Contains('.') || field.Key.StartsWith("pressure.") || field.Key.StartsWith("temperature."))
                aliases.TryAdd(field.Title, field.Key);
        return aliases;

        void AddReadings(string metric, string[] subjects, string[] endings)
        {
            foreach (var prefix in new[] { "", "当前", "现在" })
            foreach (var subject in subjects)
            foreach (var ending in endings)
                aliases[prefix + subject + ending] = metric;
        }
    }

    public static bool IsStandardPhrase(string phrase) => Aliases.ContainsKey(phrase);
    public static IReadOnlyList<string> SpokenPhrases { get; } = new TelemetryQueries().RecognitionPhrases;

    private readonly Dictionary<string, string> lookup;
    private readonly Dictionary<string, RadioField> fields;
    private readonly RacingGame game;
    public IReadOnlyList<string> RecognitionPhrases { get; }
    public TelemetryQueries() : this(new Dictionary<string, string[]>(), "") { }

    public TelemetryQueries(IReadOnlyDictionary<string, string[]> overrides, string wakeWord, RacingGame game = RacingGame.ACC, IReadOnlyList<NativeField>? current = null)
    {
        this.game = game;
        fields = RadioVocabulary.For(game, current).ToDictionary(f => f.Key);
        lookup = Aliases.Where(kv => fields.ContainsKey(kv.Value) || kv.Value is "race" or "vehicle")
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, metric) in Aliases)
            if (overrides.ContainsKey(metric) && key.Any(c => c > 127)) lookup.Remove(key);
        foreach (var (metric, entries) in overrides)
        {
            // Custom phrases for a different iRacing car/session stay on disk for its next appearance.
            if (!fields.ContainsKey(metric) && game == RacingGame.IRacing &&
                (metric.StartsWith("IR.Var.") || metric.StartsWith("IR.Session.") || metric.StartsWith("IR.Player."))) continue;
            if (!fields.ContainsKey(metric)) throw new ArgumentException("未知的遥测项目：" + metric);
            if (entries is null || entries.Length > 100) throw new ArgumentException("每项最多可设置 100 条问法。");
            foreach (var entry in entries)
            {
                var phrase = entry?.Trim() ?? "";
                if (phrase.Length is < 1 or > 48 || phrase.Any(c => !char.IsLetterOrDigit(c)))
                    throw new ArgumentException("识别词限 1 至 48 个汉字、字母或数字，每行一条。");
                if (!lookup.TryAdd(phrase, metric)) throw new ArgumentException($"“{phrase}”重复或已用于其他项目。");
            }
        }
        // Existing custom mappings take precedence over vocabulary introduced by an update.
        foreach (var (metric, phrase) in RadioPhrasebook.Entries())
            if (fields.ContainsKey(metric) && !overrides.ContainsKey(metric) && AiRadio.AfterWakeWord(phrase, wakeWord) is null)
                lookup.TryAdd(phrase, metric);
        if (game != RacingGame.ACC)
            foreach (var f in fields.Values.Where(f => !overrides.ContainsKey(f.Key)))
                foreach (var phrase in new[] { f.Title, "报告" + f.Title, f.Title + "多少", "当前" + f.Title, "当前" + f.Title + "多少", "查一下" + f.Title })
                    if (AiRadio.AfterWakeWord(phrase, wakeWord) is null) lookup.TryAdd(phrase, f.Key);
        if (game != RacingGame.ACC)
        {
            foreach (var f in fields.Values.Where(f => f.Key.StartsWith("IR.Var.") && !overrides.ContainsKey(f.Key)))
            {
                var variable = IRacingChannels.Spoken(f.Key[7..]);
                if (variable.Length > 0 && AiRadio.AfterWakeWord(variable,wakeWord) is null) lookup.TryAdd(variable,f.Key);
            }
            foreach (var (key,phrase) in new[] {
                ("RaceFeed.standings","报一下排行榜"), ("RaceFeed.ahead","排名前面是谁"), ("RaceFeed.behind","排名后面是谁"),
                ("RaceFeed.flags","现在是什么旗"), ("RaceFeed.leader","领跑车是谁"),
                ("LMU.Physics.mVirtualEnergy","虚拟能量还剩多少"), ("LMU.Physics.mEngineOilTemp","发动机油温多少"),
                ("LMU.Physics.mStateOfCharge","电池电量多少"), ("LMU.Physics.mTC","牵引力控制几档"),
                ("LMU.Session.mRaining","降雨强度多少"), ("LMU.Timing.mNumPenalties","我有几次处罚"),
                ("IR.Var.Precipitation","下雨吗"), ("IR.Var.Precipitation","降雨强度多少"),
                ("IR.Var.WeatherDeclaredWet","可以用雨胎吗"), ("IR.Var.EnergyERSBatteryPct","电池电量多少"),
                ("IR.Var.dcTractionControl","牵引力控制几档"), ("IR.Var.dcABS","防抱死几档"),
                ("IR.Var.PitRepairLeft","还要修多久"), ("IR.Var.PlayerCarMyIncidentCount","我有几个事故点") })
                if (fields.ContainsKey(key) && !overrides.ContainsKey(key) && AiRadio.AfterWakeWord(phrase,wakeWord) is null)
                    lookup.TryAdd(phrase,key);
        }
        RecognitionPhrases = lookup.Keys.Where(k => k.Any(c => c > 127) || !Aliases.ContainsKey(k)).ToArray();
        if (RecognitionPhrases.Any(p => AiRadio.AfterWakeWord(p, wakeWord) is not null))
            throw new ArgumentException("遥测问法不能以 AI 唤醒词开头，请调整词库或唤醒词。");
    }

    public string[] FieldPhrases(string metric) => RecognitionPhrases.Where(p => lookup[p] == metric).ToArray();

    public EngineerReply Answer(string question, RaceSnapshot? state, DateTimeOffset now)
    {
        var normalized = string.Join(' ', new string(question.Trim().Where(c => !char.IsPunctuation(c)).ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        // Windows Chinese ASR may insert spaces between words; English aliases retain word boundaries.
        if (normalized.Any(c => c > 127)) normalized = normalized.Replace(" ", "");
        if (!lookup.TryGetValue(normalized, out var metric))
            return new("未匹配遥测问法。可在「自定义词库」查看或添加这个问法。", false, []);
        return AnswerMetric(metric, state, now);
    }

    internal EngineerReply AnswerMetric(string metric, RaceSnapshot? state, DateTimeOffset now)
    {
        if (state is not null && state.Game != game) return new("游戏数据源已切换，请重新提问。", false, []);
        if (metric.StartsWith("UDP.", StringComparison.Ordinal))
        {
            if (state?.Broadcast is not { } feed) return new("当前没有连接 ACC 赛事广播。", false, []);
            var reading = state.Reading(metric);
            if (!feed.Driving || state.Phase is SessionPhase.Paused or SessionPhase.Replay ||
                reading is null || !reading.IsUsable(now, Metrics.MaxAge))
                return new(feed.Driving ? "当前没有该项的有效赛事广播数据。" : feed.Status, false, []);
            return new(reading.TextValue ?? "暂无广播内容。", true, [metric]);
        }
        if (state is null || state.Phase != SessionPhase.Live || now < state.CapturedAt || now - state.CapturedAt > Metrics.MaxAge)
            return new("当前没有新鲜的驾驶遥测，请进入赛道并检查连接。", false, []);
        if (metric is "vehicle" or "race")
        {
            var questions = metric == "vehicle" ? new[] { "油量", "胎温", "胎压" } : new[] { "排名", "前车差多少", "下雨吗" };
            if (game != RacingGame.ACC)
                questions = metric == "vehicle" ? ["车速", "油量", "轮胎温度"] : ["排名", "上一圈", "气温"];
            questions = questions.Where(q => fields.ContainsKey(Aliases[q])).ToArray();
            var parts = questions.Select(q => AnswerMetric(Aliases[q], state, now)).ToArray();
            var summary = string.Join(" ", parts.Select((p, i) => (p.HasVerifiedData ? "" : questions[i] + "：") + p.Text.Replace("【演示数据】", "")));
            return new((state.Origin == DataOrigin.Synthetic ? "【演示数据】" : "") + summary,
                parts.All(p => p.HasVerifiedData), parts.SelectMany(p => p.EvidenceFields).ToArray());
        }
        var keys = metric is "pressure" or "temperature" ? Metrics.Wheels.Select(w => $"{metric}.{w}").ToArray() :
            AccChannels.WheelGroups.TryGetValue(metric, out var wheels) ? wheels.Select(w => w.Key).ToArray() : [metric];
        if (metric is "gameFuelPerLap" or "gameFuelEstimatedLaps" &&
            state.Fields.TryGetValue(metric, out var estimate) && estimate.Quality == DataQuality.Estimated &&
            estimate.Value is double number && double.IsFinite(number) && estimate.ObservedAt <= now && now - estimate.ObservedAt <= Metrics.MaxAge)
            return new((state.Origin == DataOrigin.Synthetic ? "【演示数据】" : "") + $"游戏估算值约为 {number:0.0} {estimate.Unit}。", false, keys);
        foreach (var key in keys)
        {
            var v = state.Reading(key);
            if (v is null || !v.IsUsable(now, Metrics.MaxAge))
            {
                var reason = v?.Quality switch
                {
                    DataQuality.NeedsVerification => "此字段的读数或单位仍待核验",
                    DataQuality.Invalid => "游戏返回的读数超出有效范围",
                    DataQuality.Stale or DataQuality.Valid => "该读数已过期或时间异常",
                    _ => "游戏暂未提供有效读数"
                };
                return new(reason + "，暂时不能可靠回答。", false, []);
            }
        }
        var value = state.Reading(keys[0])!;
        string Number(double? v, string format = "0.0") => v!.Value.ToString(format, CultureInfo.InvariantCulture);
        var title = fields[metric].Title;
        var text = metric switch
        {
            "pressure" or "temperature" => (metric == "pressure" ? "胎压" : "核心胎温") + "，左前、右前、左后、右后依次为 " +
                string.Join("、", keys.Select(k => Number(state.Fields[k].Value))) + " " + value.Unit + "。",
            _ when keys.Length == 4 => title + "，左前、右前、左后、右后依次为 " +
                string.Join("、", keys.Select(k => Number(state.Fields[k].Value))) + " " + value.Unit + "。",
            _ when value.TextValue is { } label => $"{title}，{label} {value.Unit}。".Replace(" 。", "。"),
            _ when value.Unit == "boolean" => title + (value.Value == 1 ? "，是。" : "，否。"),
            "Graphics.flag" when value.Value is >= 0 and <= 8 => "当前" +
                new[] { "无旗", "蓝旗", "黄旗", "黑旗", "白旗", "方格旗", "处罚旗", "绿旗", "橙旗" }[(int)value.Value.Value] + "。",
            _ when metric.StartsWith("Graphics.") && value.Unit == "ms" => $"{title} {Number(value.Value / 1000, "0.###")} 秒。",
            "airTemperature" => $"气温 {Number(value.Value)} {value.Unit}。",
            "roadTemperature" => $"赛道温度 {Number(value.Value)} {value.Unit}。",
            "fuel" => $"剩余燃油 {Number(value.Value)} {value.Unit}。",
            "position" => $"目前第 {Number(value.Value, "0")} 名。",
            "lastLapTime" => $"上一圈 {Math.Floor(value.Value!.Value / 60000):0} 分 {(value.Value.Value % 60000 / 1000).ToString("00.000", CultureInfo.InvariantCulture)} 秒。",
            "gapAhead" => $"前车时间差 {Number(value.Value / 1000, "0.00")} 秒。",
            "gapBehind" => $"后车时间差 {Number(value.Value / 1000, "0.00")} 秒。",
            "speed" => $"当前车速 {Number(value.Value, "0")} {value.Unit}。",
            "sessionTimeLeft" when value.Unit is "ms" or "s" => $"当前阶段剩余 {Number(value.Value / (value.Unit == "ms" ? 60000 : 60), "0.0")} 分钟。",
            "rainIntensity" when value.Value is >= 0 and <= 5 => "当前" + new[] { "无雨", "毛毛雨", "小雨", "中雨", "大雨", "雷暴雨" }[(int)value.Value.Value] + "。",
            "missingMandatoryPits" => $"遥测显示剩余强制进站 {Number(value.Value, "0")} 次；窗口与规则尚未验证。",
            "rainTyres" or "inPitLane" or "validLap" => RadioVocabulary.Fields.First(f => f.Key == metric).Title + (value.Value == 1 ? "：是。" : "：否。"),
            _ => $"{title} {Number(value.Value, "0.##")} {value.Unit}。"
        };
        return new((state.Origin == DataOrigin.Synthetic ? "【演示数据】" : "") + text, true, keys);
    }
}

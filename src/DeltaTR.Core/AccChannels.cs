namespace DeltaTR.Core;

public enum AccPage { Physics, Graphics, Static }
public enum AccShape { Float, Integer, Fraction, Switch, Wheels, Vector, WheelVectors, Bodywork, Text15, Text33, CarSlot }

public sealed record AccChannel(AccPage Page, string Member, string Title, string Group, AccShape Shape,
    string Unit, double Min = double.MinValue, double Max = double.MaxValue, bool LocalOnly = false);
public sealed record AccReading(AccChannel Channel, string Key, string Title, int Index, int Slot = -1)
{
    public string Source { get; } = $"ACC.{Channel.Page}.{Channel.Member}" + (Index >= 0 ? $"[{Index}]" : "");
}

/// <summary>Channels added after the original radio keys. Reader and phrasebook share the same definitions.</summary>
public static class AccChannels
{
    private static readonly string[] Corners = ["左前", "右前", "左后", "右后"];
    private static readonly string[] Panels = ["前部", "后部", "左侧", "右侧", "中央"];
    private static readonly AccChannel[] Channels = [
        // 驾驶输入
        new(AccPage.Physics, "gas", "油门开度", "驾驶输入", AccShape.Fraction, "%"),
        new(AccPage.Physics, "brake", "制动踏板开度", "驾驶输入", AccShape.Fraction, "%"),
        new(AccPage.Physics, "clutch", "离合踏板开度", "驾驶输入", AccShape.Fraction, "%"),
        new(AccPage.Physics, "steerAngle", "转向输入", "驾驶输入", AccShape.Float, "原始值"),
        new(AccPage.Physics, "autoshifterOn", "自动换挡", "驾驶输入", AccShape.Switch, "boolean"),
        new(AccPage.Physics, "isAIControlled", "电脑接管驾驶", "驾驶输入", AccShape.Switch, "boolean"),
        new(AccPage.Physics, "pitLimiterOn", "维修区限速器", "驾驶输入", AccShape.Switch, "boolean"),
        // 车辆动态
        new(AccPage.Physics, "velocity", "世界坐标速度", "车辆动态", AccShape.Vector, "m/s"),
        new(AccPage.Physics, "localVelocity", "车身坐标速度", "车辆动态", AccShape.Vector, "m/s"),
        new(AccPage.Physics, "accG", "加速度", "车辆动态", AccShape.Vector, "g"),
        new(AccPage.Physics, "localAngularVel", "车身角速度", "车辆动态", AccShape.Vector, "rad/s"),
        new(AccPage.Physics, "heading", "航向角", "车辆动态", AccShape.Float, "rad"),
        new(AccPage.Physics, "pitch", "俯仰角", "车辆动态", AccShape.Float, "rad"),
        new(AccPage.Physics, "roll", "侧倾角", "车辆动态", AccShape.Float, "rad"),
        new(AccPage.Physics, "tc", "牵引力控制介入量", "车辆动态", AccShape.Float, "原始值"),
        new(AccPage.Physics, "abs", "防抱死介入量", "车辆动态", AccShape.Float, "原始值"),
        new(AccPage.Physics, "turboBoost", "涡轮增压读数", "车辆动态", AccShape.Float, "原始值"),
        new(AccPage.Physics, "waterTemp", "冷却液温度", "车辆动态", AccShape.Float, "°C", -50, 200),
        new(AccPage.Physics, "ignitionOn", "点火开关", "车辆动态", AccShape.Switch, "boolean"),
        new(AccPage.Physics, "starterEngineOn", "启动机开关", "车辆动态", AccShape.Switch, "boolean"),
        new(AccPage.Physics, "isEngineRunning", "发动机运转状态", "车辆动态", AccShape.Switch, "boolean"),
        new(AccPage.Physics, "carDamage", "车身损伤", "车辆动态", AccShape.Bodywork, "原始值", 0),
        // 制动
        new(AccPage.Physics, "brakeTemp", "刹车温度", "制动", AccShape.Wheels, "°C", -50, 1600),
        new(AccPage.Physics, "brakeBias", "前制动平衡原始比例", "制动", AccShape.Fraction, "%"),
        new(AccPage.Physics, "brakePressure", "制动压力", "制动", AccShape.Wheels, "原始值", 0),
        new(AccPage.Physics, "frontBrakeCompound", "前轴刹车片配方", "制动", AccShape.Integer, "编号", 0),
        new(AccPage.Physics, "rearBrakeCompound", "后轴刹车片配方", "制动", AccShape.Integer, "编号", 0),
        new(AccPage.Physics, "padLife", "刹车片剩余厚度", "制动", AccShape.Wheels, "mm", 0),
        new(AccPage.Physics, "discLife", "刹车盘剩余厚度", "制动", AccShape.Wheels, "mm", 0),
        // 车轮动态
        new(AccPage.Physics, "wheelSlip", "车轮滑移", "车轮动态", AccShape.Wheels, "原始值"),
        new(AccPage.Physics, "wheelAngularSpeed", "车轮角速度", "车轮动态", AccShape.Wheels, "rad/s"),
        new(AccPage.Physics, "suspensionTravel", "悬架行程读数", "车轮动态", AccShape.Wheels, "原始值"),
        new(AccPage.Physics, "suspensionDamage", "悬架损伤", "车轮动态", AccShape.Wheels, "原始值", 0),
        new(AccPage.Physics, "slipRatio", "轮胎滑移率", "车轮动态", AccShape.Wheels, "原始值"),
        new(AccPage.Physics, "slipAngle", "轮胎侧偏角", "车轮动态", AccShape.Wheels, "rad"),
        new(AccPage.Physics, "tyreContactPoint", "轮胎接地点", "车轮动态", AccShape.WheelVectors, "m"),
        new(AccPage.Physics, "tyreContactNormal", "轮胎接触法向", "车轮动态", AccShape.WheelVectors, "分量"),
        new(AccPage.Physics, "tyreContactHeading", "轮胎接触朝向", "车轮动态", AccShape.WheelVectors, "分量"),
        // 力反馈
        new(AccPage.Physics, "finalFF", "方向盘力反馈", "力反馈", AccShape.Float, "原始值"),
        new(AccPage.Physics, "kerbVibration", "路肩振动", "力反馈", AccShape.Float, "原始值"),
        new(AccPage.Physics, "slipVibrations", "打滑振动", "力反馈", AccShape.Float, "原始值"),
        new(AccPage.Physics, "gVibrations", "加速度振动", "力反馈", AccShape.Float, "原始值"),
        new(AccPage.Physics, "absVibrations", "防抱死振动", "力反馈", AccShape.Float, "原始值"),
        // 圈速与比赛
        new(AccPage.Graphics, "status", "游戏状态", "圈速与比赛", AccShape.Integer, "代码", 0, 3),
        new(AccPage.Graphics, "session", "比赛阶段类型", "圈速与比赛", AccShape.Integer, "代码", -1, 8),
        new(AccPage.Graphics, "sessionIndex", "比赛阶段序号", "圈速与比赛", AccShape.Integer, "编号", 0),
        new(AccPage.Graphics, "iCurrentTime", "当前圈用时", "圈速与比赛", AccShape.Integer, "ms", 0),
        new(AccPage.Graphics, "iBestTime", "最快圈速", "圈速与比赛", AccShape.Integer, "ms", 1),
        new(AccPage.Graphics, "lastSectorTime", "上一计时段用时", "圈速与比赛", AccShape.Integer, "ms", 1),
        new(AccPage.Graphics, "iSplit", "最近分段计时", "圈速与比赛", AccShape.Integer, "ms", 1),
        new(AccPage.Graphics, "currentSectorIndex", "当前计时段序号", "圈速与比赛", AccShape.Integer, "索引", 0),
        new(AccPage.Graphics, "numberOfLaps", "赛段圈数设置", "圈速与比赛", AccShape.Integer, "圈", 0),
        new(AccPage.Graphics, "iDeltaLapTime", "圈速差值", "圈速与比赛", AccShape.Integer, "ms"),
        new(AccPage.Graphics, "isDeltaPositive", "圈速差正号", "圈速与比赛", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "iEstimatedLapTime", "预计圈速", "圈速与比赛", AccShape.Integer, "ms", 1),
        new(AccPage.Graphics, "distanceTraveled", "本段行驶距离", "圈速与比赛", AccShape.Float, "m", 0),
        new(AccPage.Graphics, "normalizedCarPosition", "单圈进度", "圈速与比赛", AccShape.Fraction, "%"),
        new(AccPage.Graphics, "activeCars", "赛道车辆数", "圈速与比赛", AccShape.Integer, "辆", 0, 60),
        new(AccPage.Graphics, "playerCarID", "本车车辆编号", "圈速与比赛", AccShape.Integer, "编号", 0),
        new(AccPage.Graphics, "clock", "赛道当地时间", "圈速与比赛", AccShape.Float, "s", 0, 86400),
        new(AccPage.Graphics, "tyreCompound", "当前轮胎配方", "圈速与比赛", AccShape.Text33, ""),
        new(AccPage.Graphics, "trackStatus", "赛道状态文本", "圈速与比赛", AccShape.Text33, ""),
        new(AccPage.Graphics, "currentTime", "当前圈计时文本", "圈速与比赛", AccShape.Text15, ""),
        new(AccPage.Graphics, "lastTime", "上圈计时文本", "圈速与比赛", AccShape.Text15, ""),
        new(AccPage.Graphics, "bestTime", "最快圈计时文本", "圈速与比赛", AccShape.Text15, ""),
        new(AccPage.Graphics, "split", "分段计时文本", "圈速与比赛", AccShape.Text15, ""),
        new(AccPage.Graphics, "deltaLapTime", "圈速差文本", "圈速与比赛", AccShape.Text15, ""),
        new(AccPage.Graphics, "estimatedLapTime", "预计圈速文本", "圈速与比赛", AccShape.Text15, ""),
        // 旗语与判罚
        new(AccPage.Graphics, "flag", "当前旗语", "旗语与判罚", AccShape.Integer, "代码", 0, 8),
        new(AccPage.Graphics, "penalty", "当前判罚", "旗语与判罚", AccShape.Integer, "代码", 0),
        new(AccPage.Graphics, "penaltyTime", "处罚等待时间", "旗语与判罚", AccShape.Float, "s", 0),
        new(AccPage.Graphics, "globalYellow", "全场黄旗", "旗语与判罚", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "globalYellow1", "第一段黄旗", "旗语与判罚", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "globalYellow2", "第二段黄旗", "旗语与判罚", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "globalYellow3", "第三段黄旗", "旗语与判罚", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "globalWhite", "全场白旗", "旗语与判罚", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "globalGreen", "全场绿旗", "旗语与判罚", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "globalChequered", "全场方格旗", "旗语与判罚", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "globalRed", "全场红旗", "旗语与判罚", AccShape.Switch, "boolean"),
        // 进站策略
        new(AccPage.Graphics, "isInPit", "车辆进站状态", "进站策略", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "mandatoryPitDone", "已完成强制进站", "进站策略", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "driverStintTotalTimeLeft", "车手剩余总驾驶时间", "进站策略", AccShape.Integer, "ms", 0),
        new(AccPage.Graphics, "driverStintTimeLeft", "本段剩余驾驶时间", "进站策略", AccShape.Integer, "ms", 0),
        new(AccPage.Graphics, "usedFuel", "上次加油后耗油量", "进站策略", AccShape.Float, "L", 0),
        new(AccPage.Graphics, "mfdFuelToAdd", "进站计划加油量", "进站策略", AccShape.Float, "L", 0),
        new(AccPage.Graphics, "mfdTyreSet", "进站面板轮胎组", "进站策略", AccShape.Integer, "编号", 0),
        new(AccPage.Graphics, "strategyTyreSet", "策略预选轮胎组", "进站策略", AccShape.Integer, "编号", 0),
        new(AccPage.Graphics, "mfdTyrePressureLF", "进站预设左前胎压", "进站策略", AccShape.Float, "psi", 0.1, 80),
        new(AccPage.Graphics, "mfdTyrePressureRF", "进站预设右前胎压", "进站策略", AccShape.Float, "psi", 0.1, 80),
        new(AccPage.Graphics, "mfdTyrePressureLR", "进站预设左后胎压", "进站策略", AccShape.Float, "psi", 0.1, 80),
        new(AccPage.Graphics, "mfdTyrePressureRR", "进站预设右后胎压", "进站策略", AccShape.Float, "psi", 0.1, 80),
        // 电子与环境
        new(AccPage.Graphics, "TC", "牵引力控制档位", "电子与环境", AccShape.Integer, "档", 0),
        new(AccPage.Graphics, "TCCUT", "牵引力控制削减档位", "电子与环境", AccShape.Integer, "档", 0),
        new(AccPage.Graphics, "EngineMap", "发动机映射", "电子与环境", AccShape.Integer, "档", 0),
        new(AccPage.Graphics, "ABS", "防抱死档位", "电子与环境", AccShape.Integer, "档", 0),
        new(AccPage.Graphics, "windSpeed", "风速", "电子与环境", AccShape.Float, "m/s", 0),
        new(AccPage.Graphics, "windDirection", "风向", "电子与环境", AccShape.Float, "rad"),
        new(AccPage.Graphics, "rainLights", "雨灯", "电子与环境", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "flashingLights", "闪灯", "电子与环境", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "lightsStage", "车灯档位", "电子与环境", AccShape.Integer, "档", 0),
        new(AccPage.Graphics, "wiperLV", "雨刷档位", "电子与环境", AccShape.Integer, "档", 0),
        new(AccPage.Graphics, "directionLightsLeft", "左转向灯", "电子与环境", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "directionLightsRight", "右转向灯", "电子与环境", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "idealLineOn", "辅助线路", "电子与环境", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "isSetupMenuVisible", "调校菜单状态", "电子与环境", AccShape.Switch, "boolean"),
        new(AccPage.Graphics, "mainDisplayIndex", "主仪表页", "电子与环境", AccShape.Integer, "索引", 0),
        new(AccPage.Graphics, "secondaryDisplayIndex", "辅助仪表页", "电子与环境", AccShape.Integer, "索引", 0),
        new(AccPage.Graphics, "exhaustTemperature", "排气温度", "电子与环境", AccShape.Float, "°C", 1),
        // Driver names can be queried locally but never enter a recording or API payload.
        new(AccPage.Static, "playerName", "本车车手名字", "车手资料", AccShape.Text33, "", LocalOnly: true),
        new(AccPage.Static, "playerSurname", "本车车手姓氏", "车手资料", AccShape.Text33, "", LocalOnly: true),
        new(AccPage.Static, "playerNick", "本车车手昵称", "车手资料", AccShape.Text33, "", LocalOnly: true),
        // 赛事配置
        new(AccPage.Static, "smVersion", "遥测协议版本", "赛事配置", AccShape.Text15, ""),
        new(AccPage.Static, "acVersion", "游戏版本", "赛事配置", AccShape.Text15, ""),
        new(AccPage.Static, "carModel", "本车车型", "赛事配置", AccShape.Text33, ""),
        new(AccPage.Static, "track", "赛道名称", "赛事配置", AccShape.Text33, ""),
        new(AccPage.Static, "numberOfSessions", "比赛阶段数量", "赛事配置", AccShape.Integer, "个", 0),
        new(AccPage.Static, "numCars", "参赛车辆总数", "赛事配置", AccShape.Integer, "辆", 0),
        new(AccPage.Static, "sectorCount", "赛道计时段数量", "赛事配置", AccShape.Integer, "个", 1),
        new(AccPage.Static, "maxRpm", "发动机最高转速", "赛事配置", AccShape.Integer, "rpm", 1),
        new(AccPage.Static, "maxFuel", "油箱容量", "赛事配置", AccShape.Float, "L", 1),
        new(AccPage.Static, "penaltiesEnabled", "判罚规则开关", "赛事配置", AccShape.Switch, "boolean"),
        new(AccPage.Static, "aidFuelRate", "燃油消耗倍率", "赛事配置", AccShape.Float, "倍", 0),
        new(AccPage.Static, "aidTireRate", "轮胎磨损倍率设置", "赛事配置", AccShape.Float, "倍", 0),
        new(AccPage.Static, "aidMechanicalDamage", "机械损伤倍率", "赛事配置", AccShape.Float, "倍", 0),
        new(AccPage.Static, "aidStability", "稳定辅助比例", "赛事配置", AccShape.Fraction, "%"),
        new(AccPage.Static, "aidAutoclutch", "自动离合辅助", "赛事配置", AccShape.Switch, "boolean"),
        new(AccPage.Static, "aidAutoBlip", "自动补油辅助", "赛事配置", AccShape.Switch, "boolean"),
        new(AccPage.Static, "PitWindowStart", "进站窗口开启读数", "赛事配置", AccShape.Integer, "原始值", 0),
        new(AccPage.Static, "PitWindowEnd", "进站窗口关闭读数", "赛事配置", AccShape.Integer, "原始值", 1),
        new(AccPage.Static, "isOnline", "联机比赛", "赛事配置", AccShape.Switch, "boolean"),
        new(AccPage.Static, "dryTyresName", "干胎名称", "赛事配置", AccShape.Text33, ""),
        new(AccPage.Static, "wetTyresName", "湿胎名称", "赛事配置", AccShape.Text33, ""),
        // 连接
        new(AccPage.Physics, "packetId", "物理包序号", "连接", AccShape.Integer, "序号"),
        // 连接
        new(AccPage.Graphics, "packetId", "赛况包序号", "连接", AccShape.Integer, "序号"),
        new(AccPage.Graphics, "carCoordinates", "在场车辆位置", "车辆位置", AccShape.CarSlot, "")
    ];

    public static IReadOnlyList<AccReading> Readings { get; } = Expand().ToArray();

    public static IReadOnlyDictionary<string, AccReading[]> WheelGroups { get; } = Readings
        .Where(r => r.Channel.Shape == AccShape.Wheels)
        .GroupBy(r => $"{r.Channel.Page}.{r.Channel.Member}")
        .ToDictionary(group => group.Key, group => group.ToArray());

    private static IEnumerable<AccReading> Expand()
    {
        foreach (var c in Channels)
        {
            var key = $"{c.Page}.{c.Member}";
            if (c.Shape == AccShape.CarSlot)
            {
                for (var slot = 0; slot < 60; slot++)
                    yield return new(c, $"{key}.{slot}", $"车辆槽位{slot + 1}的位置", slot, slot);
            }
            else if (c.Shape is AccShape.Wheels or AccShape.WheelVectors)
            {
                for (var wheel = 0; wheel < 4; wheel++)
                    yield return new(c, $"{key}.{Metrics.Wheels[wheel]}", Corners[wheel] + c.Title, wheel);
            }
            else if (c.Shape == AccShape.Bodywork)
            {
                for (var i = 0; i < Panels.Length; i++)
                    yield return new(c, $"{key}.{i}", Panels[i] + c.Title, i);
            }
            else yield return new(c, key, c.Title, -1);
        }
    }
}

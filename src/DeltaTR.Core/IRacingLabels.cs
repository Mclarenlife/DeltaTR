namespace DeltaTR.Core;

/// <summary>Chinese labels for SDK channels; SDK identifiers remain searchable and never renamed.</summary>
internal static class IRacingLabels
{
    private static readonly Dictionary<string, string> Labels = """
AirDensity=空气密度
AirPressure=大气压力
AirTemp=遥测气温
Alt=海拔
BrakeABScutPct=防抱死制动力削减比例
BrakeABSactive=防抱死系统介入
BrakeRaw=原始制动踏板
ClutchRaw=原始离合踏板
CarDistAhead=赛道前车距离
CarDistBehind=赛道后车距离
CarIdxSessionFlags=各车旗语与处罚标志位
CarIdxClass=各车组别编号
CarIdxGear=各车挡位
CarIdxLap=各车当前圈数
CarIdxRPM=各车发动机转速
CarIdxSteer=各车转向角
CarIdxEstTime=各车圈内估计时间
CarIdxBestLapNum=各车最佳圈所在圈数
CarIdxFastRepairsUsed=各车已用快速维修次数
CarIdxP2P_Count=各车超车加速计数
CarIdxP2P_Status=各车超车加速状态
CarIdxPaceFlags=各车安全车队列标志
CarIdxPaceLine=各车安全车队列列号
CarIdxPaceRow=各车安全车队列行号
CarIdxQualTireCompound=各车排位轮胎配方
CarIdxQualTireCompoundLocked=各车排位轮胎锁定状态
CarIdxTireCompound=各车轮胎配方
CarIdxTrackSurface=各车赛道位置类型
CarIdxTrackSurfaceMaterial=各车路面材质
CarLeftRight=左右并排车辆状态
DRS_Count=DRS使用次数
DRS_Status=DRS状态
EnergyBatteryToMGU_KLap=每圈电池输出能量
EnergyERSBattery=ERS电池储能
EnergyERSBatteryPct=ERS电池电量比例
EnergyMGU_KLapDeployPct=本圈MGUK可用部署比例
Engine0_RPM=零号发动机转速
Engine1_RPM=一号发动机转速
EngineWarnings=发动机警告标志位
FastRepairAvailable=剩余快速维修次数
FastRepairUsed=已用快速维修次数
FuelLevel=遥测剩余油量
FuelLevelPct=燃油剩余比例
FuelPress=燃油压力
Gear=遥测挡位
RPM=遥测发动机转速
Speed=遥测车速
WaterTemp=遥测水温
WaterLevel=冷却液存量
OilLevel=机油存量
ManifoldPress=进气歧管压力
Voltage=电源电压
P2P_Count=本车超车加速计数
P2P_Status=本车超车加速状态
PlayerCarSLBlinkRPM=换挡灯闪烁转速
PlayerCarSLFirstRPM=换挡灯起亮转速
PlayerCarSLLastRPM=换挡灯全亮转速
PlayerCarSLShiftRPM=换挡灯建议转速
PlayerIncidents=本车事故事件标志位
PlayerCarDriverIncidentCount=当前车手事故分
PlayerCarMyIncidentCount=我的事故分
PlayerCarTeamIncidentCount=本车车队事故分
PlayerCarPitSvStatus=本车进站服务状态
PlayerCarInPitStall=本车是否位于维修位
PlayerCarPosition=遥测本车总排名
PlayerCarIdx=本车SDK车辆索引
PlayerCarClass=本车组别编号
PlayerCarPowerAdjust=本车动力调整比例
PlayerCarWeightPenalty=本车配重处罚
PlayerCarTowTime=拖车剩余时间
PlayerCarDryTireSetLimit=本车干胎套数组限制
PlayerTireCompound=本车轮胎配方
PowerMGU_H=MGUH机械功率
PowerMGU_K=MGUK机械功率
TorqueMGU_K=MGUK机械扭矩
PushToTalk=游戏无线电讲话键状态
PushToPass=超车加速按钮状态
HandbrakeRaw=原始手刹输入
Shifter=换挡器输入状态
SolarAltitude=太阳高度角
SolarAzimuth=太阳方位角
WeatherDeclaredWet=赛事是否允许使用雨胎
WeatherVersion=天气系统版本
WeatherType=天气动态模式
TrackTemp=起终点路温
TrackTempCrew=车队报告路温
RelativeHumidity=相对湿度
FogLevel=雾浓度
Skies=天空状态
SteeringFFBEnabled=力反馈启用状态
SteeringWheelAngle=方向盘转角
SteeringWheelAngleMax=方向盘最大转角
SteeringWheelTorque=方向盘反馈扭矩
SteeringWheelPctIntensity=力反馈强度比例
SteeringWheelPctSmoothing=力反馈平滑比例
SteeringWheelPctTorque=力反馈扭矩比例
SteeringWheelPctDamper=方向盘阻尼比例
SteeringWheelMaxForceNm=方向盘最大力矩设定
SteeringWheelPeakForceNm=方向盘峰值力矩
IsGarageVisible=车库界面是否可见
IsInGarage=本车是否在车库
IsOnTrack=玩家是否正在驾驶
IsOnTrackCar=本车是否在赛道
IsReplayPlaying=回放播放状态
IsDiskLoggingActive=磁盘遥测是否正在写入
IsDiskLoggingEnabled=磁盘遥测开关
OnPitRoad=遥测本车维修区状态
PitOptRepairLeft=可选维修剩余时间
PitSvFlags=进站服务请求标志位
PitSvFuel=计划进站加油量
PitSvLFP=计划进站左前胎压
PitSvRFP=计划进站右前胎压
PitSvLRP=计划进站左后胎压
PitSvRRP=计划进站右后胎压
PitSvTireCompound=计划进站轮胎配方
PitsOpen=维修区开放状态
PitstopActive=正在进行进站服务
Lap=当前圈编号
LapCompleted=遥测已完成圈数
LapCurrentLapTime=当前圈已用时间
LapLastLapTime=遥测上一圈用时
LapDist=圈内行驶距离
LapDistPct=本车单圈进度
LapBestLap=最佳圈所在圈数
LapBestNLapLap=最佳连续圈序列起始圈
LapBestNLapTime=最佳连续圈平均用时
LapLastNLapTime=最近连续圈平均用时
LapLasNLapSeq=最近连续圈序列编号
SessionFlags=赛段旗语标志位
SessionState=比赛阶段状态
SessionTime=比赛会话时钟
SessionTimeRemain=遥测阶段剩余时间
SessionTimeTotal=阶段总时长
SessionTimeOfDay=模拟时间
SessionNum=当前阶段编号
SessionTick=比赛数据帧编号
SessionUniqueID=比赛会话唯一编号
SessionLapsRemain=剩余圈数旧通道
SessionLapsTotal=阶段总圈数
SessionOnJokerLap=本车是否在额外路线圈
SessionJokerLapsRemain=剩余额外路线圈数
RaceLaps=正赛规定圈数
PaceMode=安全车队列模式
DCDriversSoFar=已经驾驶的车手人数
DCLapStatus=车手公平驾驶圈数状态
FrameRate=画面帧率
CpuUsageBG=后台CPU用时
CpuUsageFG=前台CPU用时
GpuUsage=GPU用时
ChanLatency=网络延迟
ChanAvgLatency=平均网络延迟
ChanQuality=网络质量
ChanPartnerQuality=对端网络质量
ChanClockSkew=网络时钟偏差
MemPageFaultSec=每秒内存缺页次数
MemSoftPageFaultSec=每秒内存软缺页次数
dcABS=车内防抱死档位
dcAntiRollFront=车内前防倾杆设定
dcAntiRollRear=车内后防倾杆设定
dcBrakeBias=车内制动力分配
dcBrakeBiasFine=车内制动力分配微调
dcBrakeMisc=车内其他制动设定
dcDiffEntry=入弯差速器设定
dcDiffMiddle=弯中差速器设定
dcDiffExit=出弯差速器设定
dcDiffPreload=差速器预载设定
dcDRSToggle=车内DRS开关
dcDashPage=仪表页面
dcDashPage2=第二仪表页面
dcEngineBraking=发动机制动档位
dcEnginePower=发动机动力档位
dcFuelMixture=燃油混合档位
dcFCYToggle=全场黄旗模式开关
dcFuelCutPosition=直道末端断油设定
dcFuelNoCutToggle=直道断油开关
dcHeadlightFlash=闪灯状态
dcHysBoostHold=混动部署保持状态
dcInLapToggle=进站圈模式开关
dcLaunchRPM=起步转速设定
dcLowFuelAccept=低油量提醒确认状态
dcMGUKDeployFixed=MGUK固定部署档位
dcMGUKDeployMode=MGUK部署模式
dcMGUKRegenGain=MGUK回收增益
dcPeakBrakeBias=峰值制动力分配
dcPitSpeedLimiterToggle=维修区限速开关
dcPowerSteering=转向助力设定
dcPushToPass=车内超车加速触发状态
dcRFBrakeAttachedToggle=右前制动连接开关
dcToggleWindshieldWipers=雨刷开关
dcTriggerWindshieldWipers=单次雨刷触发
dcTractionControl=牵引力控制一号设定
dcTractionControl2=牵引力控制二号设定
dcTractionControl3=牵引力控制三号设定
dcTractionControl4=牵引力控制四号设定
dcTractionControlToggle=牵引力控制开关
dcThrottleShape=油门响应曲线
dcStarter=起动机开关
dcTearOffVisor=面罩撕膜状态
dpChargeAddKWh=进站计划补充电量
dpFuelAutoFillActive=下次进站自动加油请求
dpFuelAutoFillEnabled=自动加油系统启用
dpFuelFill=进站加油请求
dpFuelAddKg=进站计划补充燃油质量
dpLTireChange=进站更换左侧轮胎请求
dpRTireChange=进站更换右侧轮胎请求
dpTireChange=进站更换全部轮胎请求
dpPowerSteering=进站转向助力调整
dpQTape=进站排位胶带调整
dpWeightJackerLeft=进站左侧配重调整
dpWeightJackerRight=进站右侧配重调整
dpWindshieldTearoff=进站挡风玻璃撕膜请求
dpWingFront=进站前翼调整
dpWingRear=进站后翼调整
dpFastRepair=进站快速维修请求
""".Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim().Split('=', 2))
        .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

    public static string? Find(string name)
    {
        if (Labels.TryGetValue(name, out var title)) return title;
        if (name.EndsWith("_ST", StringComparison.Ordinal))
            return (Find(name[..^3]) ?? "遥测" + IRacingChannels.Spoken(name[..^3])) + "时间子采样";
        foreach (var (prefix, corner) in new[] { ("LF", "左前"), ("RF", "右前"), ("LR", "左后"), ("RR", "右后") })
        {
            if (name == "dp" + prefix + "TireColdPress") return "进站" + corner + "冷胎压设定";
            if (name == "dp" + prefix + "TireChange") return "进站" + corner + "换胎请求";
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var label = name[prefix.Length..] switch
            {
                "pressure" => "胎压原始通道", "speed" => "轮速", "rideHeight" => "车身离地高度",
                "brakeLinePress" => "制动管路压力", "odometer" => "轮胎累计里程",
                "tempL" => "左侧胎面温度原始通道", "tempM" => "中部胎面温度原始通道", "tempR" => "右侧胎面温度原始通道",
                "SHshockDefl" => "附加减震器行程", "SHshockVel" => "附加减震器速度",
                "2shockDefl" => "第二减震器行程", "2shockVel" => "第二减震器速度", _ => null
            };
            if (label is not null) return corner + label;
        }
        foreach (var prefix in new[] { "CF", "CR", "HF", "HR", "ROLLF", "ROLLR" })
            if (name == prefix + "shockDefl" || name == prefix + "shockVel")
                return prefix + (name.EndsWith("Defl", StringComparison.Ordinal) ? "减震器行程" : "减震器速度");
        if (name == "CFSRrideHeight") return "CFSR离地高度";
        return null;
    }

    public static string Group(string name) => name.StartsWith("CarIdx", StringComparison.Ordinal) ? "全场车辆数组"
        : name.StartsWith("dc", StringComparison.Ordinal) ? "车内调节"
        : name.StartsWith("dp", StringComparison.Ordinal) || name.StartsWith("Pit", StringComparison.Ordinal) ? "进站服务"
        : name.StartsWith("Energy", StringComparison.Ordinal) || name.Contains("MGU", StringComparison.Ordinal) ? "混动与动力"
        : name.Contains("shock", StringComparison.Ordinal) || name.Contains("Tire", StringComparison.Ordinal) ||
            name.StartsWith("LF", StringComparison.Ordinal) || name.StartsWith("RF", StringComparison.Ordinal) ||
            name.StartsWith("LR", StringComparison.Ordinal) || name.StartsWith("RR", StringComparison.Ordinal) ? "轮胎与悬挂"
        : name.StartsWith("Weather", StringComparison.Ordinal) || name.StartsWith("Air", StringComparison.Ordinal) ||
            name.StartsWith("Wind", StringComparison.Ordinal) || name is "Precipitation" or "TrackWetness" or "RelativeHumidity" or "FogLevel" or "Skies" ? "赛道环境"
        : name.StartsWith("Session", StringComparison.Ordinal) || name.StartsWith("Lap", StringComparison.Ordinal) ||
            name.StartsWith("Player", StringComparison.Ordinal) ? "比赛与计时" : "SDK 遥测";
}

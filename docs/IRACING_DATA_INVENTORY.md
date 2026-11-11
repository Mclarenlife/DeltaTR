# iRacing 可获取数据清单与 DeltaTR 接入说明

检索日期：2026-11-09；DeltaTR 版本：0.12.0。

这是一份带来源的已知数据清单，不是“所有赛车都能实时读取”的承诺。SDK 会随版本、赛车和会话改变；连接时的变量描述表和会话 YAML 才是本次实际可用数据。未知的新变量和新的 YAML 路径仍可自动进入词库，不受下面的离线清单限制。

## 接口范围

| 数据来源 | 获取方式与内容 | DeltaTR 本版状态 |
|---|---|---|
| 本机实时遥测 | Windows 共享内存 `Local\\IRSDKMemMapFileName`；本车物理、车辆状态、计时及全场车辆数组。变量名、类型、数组长度和单位由运行时描述表提供 | 已接入；六种类型、字符串、位掩码、数组均支持 |
| 本机会话资料 | 同一 SDK 内的 YAML；WeekendInfo、SessionInfo、DriverInfo、RadioInfo、CameraInfo、QualifyResultsInfo、SplitTimeInfo，以及动态 CarSetup | 已接入；随会话版本刷新、按路径进入词库 |
| 磁盘遥测 IBT | 文件中的圈速、驾驶输入、物理与部分受限制通道；与实时变量有重叠，但不等同 | 本版不读取文件，也不加入录制/回放功能；离线参考中含这类变量，仅实际实时公布时可查询 |
| 联网 Data API | 历史成绩、赛程、车手统计、联赛、赛车和赛道资料等 | 本次完成接口目录研究，尚未接入账号授权或网络查询；不用于实时比赛提醒 |
| SDK 控制消息 | 镜头、聊天、进站服务等操作请求 | 属于写入指令，不是新增的数据源；DeltaTR 本版保持只读 |

官方明确区分本机 SDK 与联网 /data API。后者使用 OAuth，客户端需注册并获准访问，用户授权后使用访问令牌；不能用本机遥测连接代替。[官方接口区分](https://support.iracing.com/support/solutions/articles/31000177790-oauth-client-credentials)、[官方 Data API 授权流程](https://secure-oauth.iracing.com/oauth2/book/data_api_workflow.html)。

## 本次覆盖范围与改动

- 固定版本的 SDK 参考包含 **461 个变量名**，比 DeltaTR 旧参考多 **146 个**；另保留 **22 个旧版/车型专用名称**，去重后共 **483 个变量参考**。
- 会话结构整理出 **259 条标量路径**；账号 ID 与磁盘路径等 3 条不进入无线电，剩余 **256 条**加入离线词库。运行时会话中的额外路径仍可自动发现。
- 为 **27 个 CarIdx 数组**补充按本车 CarIdx 取值的查询；保存自定义问法后，切换到不公布该数组的会话不会报错。
- 加上 24 项通用/轮胎报告问法与 5 项赛事查询，离线词库共 **795 项**。这些是查询入口数量，不是互不重复的实时传感器数量。
- 新增领域包括混动能量、DRS/P2P、实时降水、轮速、制动管路压力、更多悬挂/轮胎数据、车内调节和进站设定。常用字段补充中文名称；未翻译的字段仍可用 SDK 名称检索并自定义问法。
- 描述区区分离线参考与本次会话已公布的变量，展示类型、长度、原始单位、SDK 说明及时间子采样标记。
- 按 `WeekendInfo.Encoding` 解码 UTF-8 或旧 ISO-8859-1 会话，修复中文姓名/赛道资料乱码。2026 S4 官方更新确认完整 UTF-8 支持及 `WeekendInfo.AltAssetTag` 规则集字段。[官方更新](https://support.iracing.com/support/solutions/articles/31000179517-2026-season-4-initial-release-notes-2026-09-09-01-)
- 胎压类变量按实际单位分别将 Pa 或 kPa 换算为 psi；不会把机油/制动管路等其他压力统一误当胎压。
- 常用问法包括“下雨吗”“降雨强度多少”“可以用雨胎吗”“电池电量多少”“牵引力控制几档”“防抱死几档”“还要修多久”“我有几个事故点”。字段缺失时拒绝编造读数。

本机结构参考：[SDK 头文件](https://github.com/vipoo/irsdk/blob/9aff1bd30d8fadff646058d7cbf581817faf4b8b/irsdk_defines.h)。字段元数据和会话模型取自 [SVappsLAB/iRacingTelemetrySDK 固定提交](https://github.com/SVappsLAB/iRacingTelemetrySDK/tree/9ae151b8b1acbd8b3a3eb4a69e15f8e83a5423e3)。这是维护者整理的参考而非官方穷尽保证。派生元数据遵循 Apache-2.0，保留版权与许可证于 `docs/licenses/iracing-reference-Apache-2.0.txt`。

## 各类数据可用于什么

| 分类 | 主要内容 | 关键限制 |
|---|---|---|
| 本车运动与操作 | 速度、挡位、转速、油门、刹车、离合、转向、位置、姿态、加速度、力反馈 | 各车型变量不同；高频子采样不是更多车辆 |
| 动力与燃油 | 油量、油耗、油/水温度压力、引擎状态、MGU、能量、ERS 电量、P2P、DRS | 能量单位 J、kWh、比例不能混用；常规车型未必有混动字段 |
| 轮胎与悬挂 | 冷胎压设定、轮胎报告、制动管路压力、减震器、离地高度、轮速等 | 某些通道仅磁盘可用；不能把上次测量解释为当前实时值 |
| 本车比赛状态 | 总/组别名次、圈数、圈速、圈速差、剩余时间、事故点、旗语、维修区状态 | 部分值在未完成圈、换会话或尚无结果时无效 |
| 全场车辆 | CarIdx 排名、圈数、进度、计时、旗语、维修区、组别、轮胎配方等 | 数组按 CarIdx 索引，不按排名；未出现车辆槽可能包含无效占位 |
| 天气与赛道 | 气温、路温、空气密度/压力、湿度、风、降水、湿地等级、雨胎许可 | 初始会话天气与当前天气不同；降雨位置并非整个赛道的均匀值 |
| 进站与维修 | 加油请求、轮胎更换/压力、维修时间、快修次数、配方选择等 | 计划值、实际值和状态码分别保留；本版读取请求状态，不替用户执行操作 |
| 会话与名单 | 赛道、规则、赛段结果、报名车辆/车手、执照/iRating、无线电频道、镜头组、分段 | 会话更新频率不同于物理帧；对象列表的序号不等于 CarIdx |
| 车辆设置 | CarSetup 内当前赛车公开的设置、单位和备注 | 是动态映射；不同车型结构差异大，无法静态列成固定数量 |

## 容易误解的数据

1. **轮胎温度/磨损**：F6 轮胎报告反映上次卸下轮胎的测量，进站时更新；F5 冷胎压用于下一套轮胎。DeltaTR 将 `*tempC*`、`*wear*` 标为上次维修区测量，将 `*coldPressure`、`dp*TireColdPress` 标为冷胎设定。列表中出现 `LFpressure` 或 `LFtempL` 等名字，不代表这些通道在当前实时会话可用。[官方黑盒说明](https://support.iracing.com/support/solutions/articles/31000167257-black-box-screen-information-and-controls)
2. **排名邻车与赛道邻车**：F2 比赛计时与 F3 赛道相对位置含义不同；DeltaTR 的前后车赛事查询按总排名。涉及套圈时应报告圈差，不能把估算圈内差当成正式时间差。同一官方黑盒文档说明了 F2/F3 区别。
3. **降水**：`Precipitation` 是起终点处的当前降水比例；`WeekendInfo.TrackPrecipitation` 是会话开始时的数值，不能用于回答此刻是否下雨。湿地级别和裁判允许雨胎是另外两个字段。[官方雨天遥测更新](https://support.iracing.com/support/solutions/articles/31000172630-2024-season-2-release-notes-2024-03-05-01-)
4. **对手速度**：当前参考未提供通用的每辆对手瞬时速度数组。现有 DeltaTR 由圈进度和赛道长度估算并标明“估计”；不能声称直接读取到对手的油量、胎温、磨损等本车专属物理数据。
5. **数组与编码**：`CountAsTime=true` 表示时间子采样；表中的数组长度只代表参考采样，运行时长度可能变化。YAML 列表序号与 SDK CarIdx 是两种索引。
6. **存在不等于有效**：本版实际语音回答还需驾驶会话有效、帧持续更新及读数未过期；未进入座舱、重播、断连或缺失数据时不输出伪造值。未识别的枚举/位标志保留代码，不猜测含义。

## 461 个变量的完整参考表

原始单位直接来自参考描述；`%` 变量在 SDK 中通常为 0–1，本适配按百分数显示。类型/长度由实时描述覆盖。下表包含实时与磁盘通道，未可靠确认实时可见性的项目不做保证。英文说明保留原始语义以方便与 SDK 对照。

### 赛道环境（13）

| SDK 名称 | 类型/参考长度 | 原始单位 | 时间子采样 | SDK 说明 |
|---|---|---|---|---|
| `AirDensity` | float32 | kg/m^3 | 否 | Density of air at start/finish line |
| `AirPressure` | float32 | Pa | 否 | Pressure of air at start/finish line |
| `AirTemp` | float32 | C | 否 | Temperature of air at start/finish line |
| `FogLevel` | float32 | % | 否 | Fog level at start/finish line |
| `Precipitation` | float32 | % | 否 | Precipitation at start/finish line |
| `RelativeHumidity` | float32 | % | 否 | Relative Humidity at start/finish line |
| `Skies` | int32 | — | 否 | Skies (0=clear/1=p cloudy/2=m cloudy/3=overcast) |
| `TrackWetness` | int32 | TrackWetness | 否 | How wet is the average track surface |
| `WeatherDeclaredWet` | bool | — | 否 | The steward says rain tires can be used |
| `WeatherType` | int32 | WeatherDynamics | 否 | Weather dynamics type |
| `WeatherVersion` | int32 | WeatherVersion | 否 | Weather version |
| `WindDir` | float32 | rad | 否 | Wind direction at start/finish line |
| `WindVel` | float32 | m/s | 否 | Wind velocity at start/finish line |

### SDK 遥测（128）

| SDK 名称 | 类型/参考长度 | 原始单位 | 时间子采样 | SDK 说明 |
|---|---|---|---|---|
| `Alt` | float32 | m | 否 | Altitude in meters |
| `Brake` | float32 | % | 否 | 0=brake released to 1=max pedal force |
| `BrakeABSactive` | bool | — | 否 | true if abs is currently reducing brake force pressure |
| `BrakeABScutPct` | float32 | % | 否 | Percent of brake force reduction caused by ABS system |
| `BrakeRaw` | float32 | % | 否 | Raw brake input 0=brake released to 1=max pedal force |
| `CFSRrideHeight` | float32 | m | 否 | CFSR ride height |
| `CamCameraNumber` | int32 | — | 否 | Active camera number |
| `CamCameraState` | uint32 位掩码 | CameraState | 否 | State of camera system |
| `CamCarIdx` | int32 | — | 否 | Active camera's focus car index |
| `CamGroupNumber` | int32 | — | 否 | Active camera group number |
| `CarDistAhead` | float32 | m | 否 | Distance to first car in front of player in meters |
| `CarDistBehind` | float32 | m | 否 | Distance to first car behind player in meters |
| `CarLeftRight` | int32 | CarLeftRight | 否 | Notify if car is to the left or right of driver |
| `ChanAvgLatency` | float32 | s | 否 | Communications average latency |
| `ChanClockSkew` | float32 | s | 否 | Communications server clock skew |
| `ChanLatency` | float32 | s | 否 | Communications latency |
| `ChanPartnerQuality` | float32 | % | 否 | Partner communications quality |
| `ChanQuality` | float32 | % | 否 | Communications quality |
| `Clutch` | float32 | % | 否 | 0=disengaged to 1=fully engaged |
| `ClutchRaw` | float32 | % | 否 | Raw clutch input 0=disengaged to 1=fully engaged |
| `CpuUsageBG` | float32 | % | 否 | Percent of available tim bg thread took with a 1 sec avg |
| `CpuUsageFG` | float32 | % | 否 | Percent of available tim fg thread took with a 1 sec avg |
| `DCDriversSoFar` | int32 | — | 否 | Number of team drivers who have run a stint |
| `DCLapStatus` | int32 | — | 否 | Status of driver change lap requirements |
| `DRS_Count` | int32 |   | 否 | Drag Reduction System count of usage |
| `DRS_Status` | int32 |   | 否 | Drag Reduction System Status |
| `DisplayUnits` | int32 | — | 否 | Default units for the user interface 0 = english 1 = metric |
| `DriverMarker` | bool | — | 否 | Driver activated flag |
| `Engine0_RPM` | float32 | revs/min | 否 | Engine0Engine rpm |
| `Engine1_RPM` | float32 | revs/min | 否 | Engine1Engine rpm |
| `EngineWarnings` | uint32 位掩码 | EngineWarnings | 否 | Bitfield for warning lights |
| `EnterExitReset` | int32 | — | 否 | Indicate action the reset key will take 0 enter 1 exit 2 reset |
| `FastRepairAvailable` | int32 | — | 否 | How many fast repairs left  255 is unlimited |
| `FastRepairUsed` | int32 | — | 否 | How many fast repairs used so far |
| `FrameRate` | float32 | fps | 否 | Average frames per second |
| `FuelLevel` | float32 | l | 否 | Liters of fuel remaining |
| `FuelLevelPct` | float32 | % | 否 | Percent fuel remaining |
| `FuelPress` | float32 | bar | 否 | Engine fuel pressure |
| `FuelUsePerHour` | float32 | kg/h | 否 | Engine fuel used instantaneous |
| `Gear` | int32 | — | 否 | -1=reverse  0=neutral  1..n=current gear |
| `GpuUsage` | float32 | % | 否 | Percent of available tim gpu took with a 1 sec avg |
| `HandbrakeRaw` | float32 | % | 否 | Raw handbrake input 0=handbrake released to 1=max force |
| `IsDiskLoggingActive` | bool | — | 否 | 0=disk based telemetry file not being written  1=being written |
| `IsDiskLoggingEnabled` | bool | — | 否 | 0=disk based telemetry turned off  1=turned on |
| `IsGarageVisible` | bool | — | 否 | 1=Garage screen is visible |
| `IsInGarage` | bool | — | 否 | 1=Car in garage physics running |
| `IsOnTrack` | bool | — | 否 | 1=Car on track physics running with player in car |
| `IsOnTrackCar` | bool | — | 否 | 1=Car on track physics running |
| `IsReplayPlaying` | bool | — | 否 | 0=replay not playing  1=replay playing |
| `Lat` | float64 | deg | 否 | Latitude in decimal degrees |
| `LatAccel` | float32 | m/s^2 | 否 | Lateral acceleration (including gravity) |
| `LatAccel_ST` | float32[6] | m/s^2 | 是 | Lateral acceleration (including gravity) at 360 Hz |
| `LoadNumTextures` | bool | — | 否 | True if the car_num texture will be loaded |
| `Lon` | float64 | deg | 否 | Longitude in decimal degrees |
| `LongAccel` | float32 | m/s^2 | 否 | Longitudinal acceleration (including gravity) |
| `LongAccel_ST` | float32[6] | m/s^2 | 是 | Longitudinal acceleration (including gravity) at 360 Hz |
| `ManifoldPress` | float32 | bar | 否 | Engine manifold pressure |
| `ManualBoost` | bool | — | 否 | Hybrid manual boost state |
| `ManualNoBoost` | bool | — | 否 | Hybrid manual no boost state |
| `MemPageFaultSec` | float32 | — | 否 | Memory page faults per second |
| `MemSoftPageFaultSec` | float32 | — | 否 | Memory soft page faults per second |
| `OilLevel` | float32 | l | 否 | Engine oil level |
| `OilPress` | float32 | bar | 否 | Engine oil pressure |
| `OilTemp` | float32 | C | 否 | Engine oil temperature |
| `OkToReloadTextures` | bool | — | 否 | True if it is ok to reload car textures at this time |
| `OnPitRoad` | bool | — | 否 | Is the player car on pit road between the cones |
| `P2P_Count` | int32 | — | 否 | Push2Pass count of usage (or remaining in Race) on your car |
| `P2P_Status` | bool | — | 否 | Push2Pass active or not on your car |
| `PaceMode` | int32 | PaceMode | 否 | Are we pacing or not |
| `PushToPass` | bool | — | 否 | Push to pass button state |
| `PushToTalk` | bool | — | 否 | Push to talk button state |
| `RPM` | float32 | revs/min | 否 | Engine rpm |
| `RaceLaps` | int32 | — | 否 | Laps completed in race |
| `RadioTransmitCarIdx` | int32 | — | 否 | The car index of the current person speaking on the radio |
| `RadioTransmitFrequencyIdx` | int32 | — | 否 | The frequency index of the current person speaking on the radio |
| `RadioTransmitRadioIdx` | int32 | — | 否 | The radio index of the current person speaking on the radio |
| `ReplayFrameNum` | int32 | — | 否 | Integer replay frame number (60 per second) |
| `ReplayFrameNumEnd` | int32 | — | 否 | Integer replay frame number from end of tape |
| `ReplayPlaySlowMotion` | bool | — | 否 | 0=not slow motion  1=replay is in slow motion |
| `ReplayPlaySpeed` | int32 | — | 否 | Replay playback speed |
| `ReplaySessionNum` | int32 | — | 否 | Replay session number |
| `ReplaySessionTime` | float64 | s | 否 | Seconds since replay session start |
| `Roll` | float32 | rad | 否 | Roll orientation |
| `RollRate` | float32 | rad/s | 否 | Roll rate |
| `RollRate_ST` | float32[6] | rad/s | 是 | Roll rate at 360 Hz |
| `ShiftGrindRPM` | float32 | RPM | 否 | RPM of shifter grinding noise |
| `ShiftIndicatorPct` | float32 | % | 否 | DEPRECATED use DriverCarSLBlinkRPM instead |
| `ShiftPowerPct` | float32 | % | 否 | Friction torque applied to gears when shifting or grinding |
| `Shifter` | int32 | — | 否 | Log inputs from the players shifter control |
| `SolarAltitude` | float32 | rad | 否 | Sun angle above horizon in radians |
| `SolarAzimuth` | float32 | rad | 否 | Sun angle clockwise from north in radians |
| `Speed` | float32 | m/s | 否 | GPS vehicle speed |
| `SteeringFFBEnabled` | bool | — | 否 | Force feedback is enabled |
| `SteeringWheelAngle` | float32 | rad | 否 | Steering wheel angle |
| `SteeringWheelAngleMax` | float32 | rad | 否 | Steering wheel max angle |
| `SteeringWheelLimiter` | float32 | % | 否 | Force feedback limiter strength limits impacts and oscillation |
| `SteeringWheelMaxForceNm` | float32 | N*m | 否 | Value of strength or max force slider in Nm for FFB |
| `SteeringWheelPctDamper` | float32 | % | 否 | Force feedback % max damping |
| `SteeringWheelPctIntensity` | float32 | % | 否 | Force feedback % max intensity |
| `SteeringWheelPctSmoothing` | float32 | % | 否 | Force feedback % max smoothing |
| `SteeringWheelPctTorque` | float32 | % | 否 | Force feedback % max torque on steering shaft unsigned |
| `SteeringWheelPctTorqueSign` | float32 | % | 否 | Force feedback % max torque on steering shaft signed |
| `SteeringWheelPctTorqueSignStops` | float32 | % | 否 | Force feedback % max torque on steering shaft signed stops |
| `SteeringWheelPeakForceNm` | float32 | N*m | 否 | Peak torque mapping to direct input units for FFB |
| `SteeringWheelTorque` | float32 | N*m | 否 | Output torque on steering shaft |
| `SteeringWheelTorque_ST` | float32[6] | N*m | 是 | Output torque on steering shaft at 360 Hz |
| `SteeringWheelUseLinear` | bool | — | 否 | True if steering wheel force is using linear mode |
| `Throttle` | float32 | % | 否 | 0=off throttle to 1=full throttle |
| `ThrottleRaw` | float32 | % | 否 | Raw throttle input 0=off throttle to 1=full throttle |
| `TrackTemp` | float32 | C | 否 | Deprecated  set to TrackTempCrew |
| `TrackTempCrew` | float32 | C | 否 | Temperature of track measured by crew around track |
| `VelocityX` | float32 | m/s | 否 | X velocity |
| `VelocityX_ST` | float32[6] | m/s at 360 Hz | 是 | X velocity |
| `VelocityY` | float32 | m/s | 否 | Y velocity |
| `VelocityY_ST` | float32[6] | m/s at 360 Hz | 是 | Y velocity |
| `VelocityZ` | float32 | m/s | 否 | Z velocity |
| `VelocityZ_ST` | float32[6] | m/s at 360 Hz | 是 | Z velocity |
| `VertAccel` | float32 | m/s^2 | 否 | Vertical acceleration (including gravity) |
| `VertAccel_ST` | float32[6] | m/s^2 | 是 | Vertical acceleration (including gravity) at 360 Hz |
| `VidCapActive` | bool | — | 否 | True if video currently being captured |
| `VidCapEnabled` | bool | — | 否 | True if video capture system is enabled |
| `Voltage` | float32 | V | 否 | Engine voltage |
| `WaterLevel` | float32 | l | 否 | Engine coolant level |
| `WaterTemp` | float32 | C | 否 | Engine coolant temp |
| `Yaw` | float32 | rad | 否 | Yaw orientation |
| `YawNorth` | float32 | rad | 否 | Yaw orientation relative to north |
| `YawRate` | float32 | rad/s | 否 | Yaw rate |
| `YawRate_ST` | float32[6] | rad/s | 是 | Yaw rate at 360 Hz |

### 轮胎与悬挂（144）

| SDK 名称 | 类型/参考长度 | 原始单位 | 时间子采样 | SDK 说明 |
|---|---|---|---|---|
| `CFshockDefl` | float32 | m | 否 | CF shock deflection |
| `CFshockDefl_ST` | float32[6] | m | 是 | CF shock deflection at 360 Hz |
| `CFshockVel` | float32 | m/s | 否 | CF shock velocity |
| `CFshockVel_ST` | float32[6] | m/s | 是 | CF shock velocity at 360 Hz |
| `CRshockDefl` | float32 | m | 否 | CR shock deflection |
| `CRshockDefl_ST` | float32[6] | m | 是 | CR shock deflection at 360 Hz |
| `CRshockVel` | float32 | m/s | 否 | CR shock velocity |
| `CRshockVel_ST` | float32[6] | m/s | 是 | CR shock velocity at 360 Hz |
| `FrontTireSetsAvailable` | int32 | — | 否 | How many front tire sets are remaining  255 is unlimited |
| `FrontTireSetsUsed` | int32 | — | 否 | How many front tire sets used so far |
| `HFshockDefl` | float32 | m | 否 | HF shock deflection |
| `HFshockDefl_ST` | float32[6] | m | 是 | HF shock deflection at 360 Hz |
| `HFshockVel` | float32 | m/s | 否 | HF shock velocity |
| `HFshockVel_ST` | float32[6] | m/s | 是 | HF shock velocity at 360 Hz |
| `HRshockDefl` | float32 | m | 否 | HR shock deflection |
| `HRshockDefl_ST` | float32[6] | m | 是 | HR shock deflection at 360 Hz |
| `HRshockVel` | float32 | m/s | 否 | HR shock velocity |
| `HRshockVel_ST` | float32[6] | m/s | 是 | HR shock velocity at 360 Hz |
| `LFSHshockDefl` | float32 | m | 否 | LFSH shock deflection |
| `LFSHshockDefl_ST` | float32[6] | m | 是 | LFSH shock deflection at 360 Hz |
| `LFSHshockVel` | float32 | m/s | 否 | LFSH shock velocity |
| `LFSHshockVel_ST` | float32[6] | m/s | 是 | LFSH shock velocity at 360 Hz |
| `LFTiresAvailable` | int32 | — | 否 | How many left front tires are remaining  255 is unlimited |
| `LFTiresUsed` | int32 | — | 否 | How many left front tires used so far |
| `LFbrakeLinePress` | float32 | bar | 否 | LF brake line pressure |
| `LFcoldPressure` | float32 | kPa | 否 | LF tire cold pressure  as set in the garage |
| `LFodometer` | float32 | m | 否 | LF distance tire traveled since being placed on car |
| `LFpressure` | float32 | kPa | 否 | LF tire pressure |
| `LFrideHeight` | float32 | m | 否 | LF ride height |
| `LFshockDefl` | float32 | m | 否 | LF shock deflection |
| `LFshockDefl_ST` | float32[6] | m | 是 | LF shock deflection at 360 Hz |
| `LFshockVel` | float32 | m/s | 否 | LF shock velocity |
| `LFshockVel_ST` | float32[6] | m/s | 是 | LF shock velocity at 360 Hz |
| `LFspeed` | float32 | m/s | 否 | LF wheel speed |
| `LFtempCL` | float32 | C | 否 | LF tire left carcass temperature |
| `LFtempCM` | float32 | C | 否 | LF tire middle carcass temperature |
| `LFtempCR` | float32 | C | 否 | LF tire right carcass temperature |
| `LFtempL` | float32 | C | 否 | LF tire left surface temperature |
| `LFtempM` | float32 | C | 否 | LF tire middle surface temperature |
| `LFtempR` | float32 | C | 否 | LF tire right surface temperature |
| `LFwearL` | float32 | % | 否 | LF tire left percent tread remaining |
| `LFwearM` | float32 | % | 否 | LF tire middle percent tread remaining |
| `LFwearR` | float32 | % | 否 | LF tire right percent tread remaining |
| `LR2shockDefl` | float32 | m | 否 | LR2 shock deflection |
| `LR2shockDefl_ST` | float32[6] | m | 是 | LR2 shock deflection at 360 Hz |
| `LR2shockVel` | float32 | m/s | 否 | LR2 shock velocity |
| `LR2shockVel_ST` | float32[6] | m/s | 是 | LR2 shock velocity at 360 Hz |
| `LRSHshockDefl` | float32 | m | 否 | LRSH shock deflection |
| `LRSHshockDefl_ST` | float32[6] | m | 是 | LRSH shock deflection at 360 Hz |
| `LRSHshockVel` | float32 | m/s | 否 | LRSH shock velocity |
| `LRSHshockVel_ST` | float32[6] | m/s | 是 | LRSH shock velocity at 360 Hz |
| `LRTiresAvailable` | int32 | — | 否 | How many left rear tires are remaining  255 is unlimited |
| `LRTiresUsed` | int32 | — | 否 | How many left rear tires used so far |
| `LRbrakeLinePress` | float32 | bar | 否 | LR brake line pressure |
| `LRcoldPressure` | float32 | kPa | 否 | LR tire cold pressure  as set in the garage |
| `LRodometer` | float32 | m | 否 | LR distance tire traveled since being placed on car |
| `LRpressure` | float32 | kPa | 否 | LR tire pressure |
| `LRrideHeight` | float32 | m | 否 | LR ride height |
| `LRshockDefl` | float32 | m | 否 | LR shock deflection |
| `LRshockDefl_ST` | float32[6] | m | 是 | LR shock deflection at 360 Hz |
| `LRshockVel` | float32 | m/s | 否 | LR shock velocity |
| `LRshockVel_ST` | float32[6] | m/s | 是 | LR shock velocity at 360 Hz |
| `LRspeed` | float32 | m/s | 否 | LR wheel speed |
| `LRtempCL` | float32 | C | 否 | LR tire left carcass temperature |
| `LRtempCM` | float32 | C | 否 | LR tire middle carcass temperature |
| `LRtempCR` | float32 | C | 否 | LR tire right carcass temperature |
| `LRtempL` | float32 | C | 否 | LR tire left surface temperature |
| `LRtempM` | float32 | C | 否 | LR tire middle surface temperature |
| `LRtempR` | float32 | C | 否 | LR tire right surface temperature |
| `LRwearL` | float32 | % | 否 | LR tire left percent tread remaining |
| `LRwearM` | float32 | % | 否 | LR tire middle percent tread remaining |
| `LRwearR` | float32 | % | 否 | LR tire right percent tread remaining |
| `LeftTireSetsAvailable` | int32 | — | 否 | How many left tire sets are remaining  255 is unlimited |
| `LeftTireSetsUsed` | int32 | — | 否 | How many left tire sets used so far |
| `PlayerCarDryTireSetLimit` | int32 | — | 否 | Players dry tire set limit |
| `PlayerTireCompound` | int32 | — | 否 | Players car current tire compound |
| `RFSHshockDefl` | float32 | m | 否 | RFSH shock deflection |
| `RFSHshockDefl_ST` | float32[6] | m | 是 | RFSH shock deflection at 360 Hz |
| `RFSHshockVel` | float32 | m/s | 否 | RFSH shock velocity |
| `RFSHshockVel_ST` | float32[6] | m/s | 是 | RFSH shock velocity at 360 Hz |
| `RFTiresAvailable` | int32 | — | 否 | How many right front tires are remaining  255 is unlimited |
| `RFTiresUsed` | int32 | — | 否 | How many right front tires used so far |
| `RFbrakeLinePress` | float32 | bar | 否 | RF brake line pressure |
| `RFcoldPressure` | float32 | kPa | 否 | RF tire cold pressure  as set in the garage |
| `RFodometer` | float32 | m | 否 | RF distance tire traveled since being placed on car |
| `RFpressure` | float32 | kPa | 否 | RF tire pressure |
| `RFrideHeight` | float32 | m | 否 | RF ride height |
| `RFshockDefl` | float32 | m | 否 | RF shock deflection |
| `RFshockDefl_ST` | float32[6] | m | 是 | RF shock deflection at 360 Hz |
| `RFshockVel` | float32 | m/s | 否 | RF shock velocity |
| `RFshockVel_ST` | float32[6] | m/s | 是 | RF shock velocity at 360 Hz |
| `RFspeed` | float32 | m/s | 否 | RF wheel speed |
| `RFtempCL` | float32 | C | 否 | RF tire left carcass temperature |
| `RFtempCM` | float32 | C | 否 | RF tire middle carcass temperature |
| `RFtempCR` | float32 | C | 否 | RF tire right carcass temperature |
| `RFtempL` | float32 | C | 否 | RF tire left surface temperature |
| `RFtempM` | float32 | C | 否 | RF tire middle surface temperature |
| `RFtempR` | float32 | C | 否 | RF tire right surface temperature |
| `RFwearL` | float32 | % | 否 | RF tire left percent tread remaining |
| `RFwearM` | float32 | % | 否 | RF tire middle percent tread remaining |
| `RFwearR` | float32 | % | 否 | RF tire right percent tread remaining |
| `ROLLFshockDefl` | float32 | m | 否 | ROLLF shock deflection |
| `ROLLFshockDefl_ST` | float32[6] | m | 是 | ROLLF shock deflection at 360 Hz |
| `ROLLFshockVel` | float32 | m/s | 否 | ROLLF shock velocity |
| `ROLLFshockVel_ST` | float32[6] | m/s | 是 | ROLLF shock velocity at 360 Hz |
| `ROLLRshockDefl` | float32 | m | 否 | ROLLR shock deflection |
| `ROLLRshockDefl_ST` | float32[6] | m | 是 | ROLLR shock deflection at 360 Hz |
| `ROLLRshockVel` | float32 | m/s | 否 | ROLLR shock velocity |
| `ROLLRshockVel_ST` | float32[6] | m/s | 是 | ROLLR shock velocity at 360 Hz |
| `RRSHshockDefl` | float32 | m | 否 | RRSH shock deflection |
| `RRSHshockDefl_ST` | float32[6] | m | 是 | RRSH shock deflection at 360 Hz |
| `RRSHshockVel` | float32 | m/s | 否 | RRSH shock velocity |
| `RRSHshockVel_ST` | float32[6] | m/s | 是 | RRSH shock velocity at 360 Hz |
| `RRTiresAvailable` | int32 | — | 否 | How many right rear tires are remaining  255 is unlimited |
| `RRTiresUsed` | int32 | — | 否 | How many right rear tires used so far |
| `RRbrakeLinePress` | float32 | bar | 否 | RR brake line pressure |
| `RRcoldPressure` | float32 | kPa | 否 | RR tire cold pressure  as set in the garage |
| `RRodometer` | float32 | m | 否 | RR distance tire traveled since being placed on car |
| `RRpressure` | float32 | kPa | 否 | RR tire pressure |
| `RRrideHeight` | float32 | m | 否 | RR ride height |
| `RRshockDefl` | float32 | m | 否 | RR shock deflection |
| `RRshockDefl_ST` | float32[6] | m | 是 | RR shock deflection at 360 Hz |
| `RRshockVel` | float32 | m/s | 否 | RR shock velocity |
| `RRshockVel_ST` | float32[6] | m/s | 是 | RR shock velocity at 360 Hz |
| `RRspeed` | float32 | m/s | 否 | RR wheel speed |
| `RRtempCL` | float32 | C | 否 | RR tire left carcass temperature |
| `RRtempCM` | float32 | C | 否 | RR tire middle carcass temperature |
| `RRtempCR` | float32 | C | 否 | RR tire right carcass temperature |
| `RRtempL` | float32 | C | 否 | RR tire left surface temperature |
| `RRtempM` | float32 | C | 否 | RR tire middle surface temperature |
| `RRtempR` | float32 | C | 否 | RR tire right surface temperature |
| `RRwearL` | float32 | % | 否 | RR tire left percent tread remaining |
| `RRwearM` | float32 | % | 否 | RR tire middle percent tread remaining |
| `RRwearR` | float32 | % | 否 | RR tire right percent tread remaining |
| `RearTireSetsAvailable` | int32 | — | 否 | How many rear tire sets are remaining  255 is unlimited |
| `RearTireSetsUsed` | int32 | — | 否 | How many rear tire sets used so far |
| `RightTireSetsAvailable` | int32 | — | 否 | How many right tire sets are remaining  255 is unlimited |
| `RightTireSetsUsed` | int32 | — | 否 | How many right tire sets used so far |
| `TireLF_RumblePitch` | float32 | Hz | 否 | Players LF Tire Sound rumblestrip pitch |
| `TireLR_RumblePitch` | float32 | Hz | 否 | Players LR Tire Sound rumblestrip pitch |
| `TireRF_RumblePitch` | float32 | Hz | 否 | Players RF Tire Sound rumblestrip pitch |
| `TireRR_RumblePitch` | float32 | Hz | 否 | Players RR Tire Sound rumblestrip pitch |
| `TireSetsAvailable` | int32 | — | 否 | How many tire sets are remaining  255 is unlimited |
| `TireSetsUsed` | int32 | — | 否 | How many tire sets used so far |

### 全场车辆数组（27）

| SDK 名称 | 类型/参考长度 | 原始单位 | 时间子采样 | SDK 说明 |
|---|---|---|---|---|
| `CarIdxBestLapNum` | int32[64] | — | 否 | Cars best lap number |
| `CarIdxBestLapTime` | float32[64] | s | 否 | Cars best lap time |
| `CarIdxClass` | int32[64] | — | 否 | Cars class id by car index |
| `CarIdxClassPosition` | int32[64] | — | 否 | Cars class position in race by car index |
| `CarIdxEstTime` | float32[64] | s | 否 | Estimated time to reach current location on track |
| `CarIdxF2Time` | float32[64] | s | 否 | Race time behind leader or fastest lap time otherwise |
| `CarIdxFastRepairsUsed` | int32[64] | — | 否 | How many fast repairs each car has used |
| `CarIdxGear` | int32[64] | — | 否 | -1=reverse  0=neutral  1..n=current gear by car index |
| `CarIdxLap` | int32[64] | — | 否 | Laps started by car index |
| `CarIdxLapCompleted` | int32[64] | — | 否 | Laps completed by car index |
| `CarIdxLapDistPct` | float32[64] | % | 否 | Percentage distance around lap by car index |
| `CarIdxLastLapTime` | float32[64] | s | 否 | Cars last lap time |
| `CarIdxOnPitRoad` | bool[64] | — | 否 | On pit road between the cones by car index |
| `CarIdxP2P_Count` | int32[64] | — | 否 | Push2Pass count of usage (or remaining in Race) |
| `CarIdxP2P_Status` | bool[64] | — | 否 | Push2Pass active or not |
| `CarIdxPaceFlags` | uint32 位掩码[64] | PaceFlags | 否 | Pacing status flags for each car |
| `CarIdxPaceLine` | int32[64] | — | 否 | What line cars are pacing in  or -1 if not pacing |
| `CarIdxPaceRow` | int32[64] | — | 否 | What row cars are pacing in  or -1 if not pacing |
| `CarIdxPosition` | int32[64] | — | 否 | Cars position in race by car index |
| `CarIdxQualTireCompound` | int32[64] | — | 否 | Cars Qual tire compound |
| `CarIdxQualTireCompoundLocked` | bool[64] | — | 否 | Cars Qual tire compound is locked-in |
| `CarIdxRPM` | float32[64] | revs/min | 否 | Engine rpm by car index |
| `CarIdxSessionFlags` | uint32 位掩码[64] | SessionFlags | 否 | Session flags for each player |
| `CarIdxSteer` | float32[64] | rad | 否 | Steering wheel angle by car index |
| `CarIdxTireCompound` | int32[64] | — | 否 | Cars current tire compound |
| `CarIdxTrackSurface` | int32[64] | TrackLocation | 否 | Track surface type by car index |
| `CarIdxTrackSurfaceMaterial` | int32[64] | TrackSurface | 否 | Track surface material type by car index |

### 混动与动力（7）

| SDK 名称 | 类型/参考长度 | 原始单位 | 时间子采样 | SDK 说明 |
|---|---|---|---|---|
| `EnergyBatteryToMGU_KLap` | float32 | J | 否 | Electrical energy from battery to MGU-K per lap |
| `EnergyERSBattery` | float32 | J | 否 | Engine ERS battery charge |
| `EnergyERSBatteryPct` | float32 | % | 否 | Engine ERS battery charge as a percent |
| `EnergyMGU_KLapDeployPct` | float32 | % | 否 | Electrical energy available to MGU-K per lap as a percent |
| `PowerMGU_H` | float32 | W | 否 | Engine MGU-H mechanical power |
| `PowerMGU_K` | float32 | W | 否 | Engine MGU-K mechanical power |
| `TorqueMGU_K` | float32 | Nm | 否 | Engine MGU-K mechanical torque |

### 比赛与计时（61）

| SDK 名称 | 类型/参考长度 | 原始单位 | 时间子采样 | SDK 说明 |
|---|---|---|---|---|
| `Lap` | int32 | — | 否 | Laps started count |
| `LapBestLap` | int32 | — | 否 | Players best lap number |
| `LapBestLapTime` | float32 | s | 否 | Players best lap time |
| `LapBestNLapLap` | int32 | — | 否 | Player last lap in best N average lap time |
| `LapBestNLapTime` | float32 | s | 否 | Player best N average lap time |
| `LapCompleted` | int32 | — | 否 | Laps completed count |
| `LapCurrentLapTime` | float32 | s | 否 | Estimate of players current lap time as shown in F3 box |
| `LapDeltaToBestLap` | float32 | s | 否 | Delta time for best lap |
| `LapDeltaToBestLap_DD` | float32 | s/s | 否 | Rate of change of delta time for best lap |
| `LapDeltaToBestLap_OK` | bool | — | 否 | Delta time for best lap is valid |
| `LapDeltaToOptimalLap` | float32 | s | 否 | Delta time for optimal lap |
| `LapDeltaToOptimalLap_DD` | float32 | s/s | 否 | Rate of change of delta time for optimal lap |
| `LapDeltaToOptimalLap_OK` | bool | — | 否 | Delta time for optimal lap is valid |
| `LapDeltaToSessionBestLap` | float32 | s | 否 | Delta time for session best lap |
| `LapDeltaToSessionBestLap_DD` | float32 | s/s | 否 | Rate of change of delta time for session best lap |
| `LapDeltaToSessionBestLap_OK` | bool | — | 否 | Delta time for session best lap is valid |
| `LapDeltaToSessionLastlLap` | float32 | s | 否 | Delta time for session last lap |
| `LapDeltaToSessionLastlLap_DD` | float32 | s/s | 否 | Rate of change of delta time for session last lap |
| `LapDeltaToSessionLastlLap_OK` | bool | — | 否 | Delta time for session last lap is valid |
| `LapDeltaToSessionOptimalLap` | float32 | s | 否 | Delta time for session optimal lap |
| `LapDeltaToSessionOptimalLap_DD` | float32 | s/s | 否 | Rate of change of delta time for session optimal lap |
| `LapDeltaToSessionOptimalLap_OK` | bool | — | 否 | Delta time for session optimal lap is valid |
| `LapDist` | float32 | m | 否 | Meters traveled from S/F this lap |
| `LapDistPct` | float32 | % | 否 | Percentage distance around lap |
| `LapLasNLapSeq` | int32 | — | 否 | Player num consecutive clean laps completed for N average |
| `LapLastLapTime` | float32 | s | 否 | Players last lap time |
| `LapLastNLapTime` | float32 | s | 否 | Player last N average lap time |
| `PlayerCarClass` | int32 | — | 否 | Player car class id |
| `PlayerCarClassPosition` | int32 | — | 否 | Players class position in race |
| `PlayerCarDriverIncidentCount` | int32 | — | 否 | Teams current drivers incident count for this session |
| `PlayerCarIdx` | int32 | — | 否 | Players carIdx |
| `PlayerCarInPitStall` | bool | — | 否 | Players car is properly in their pitstall |
| `PlayerCarMyIncidentCount` | int32 | — | 否 | Players own incident count for this session |
| `PlayerCarPitSvStatus` | int32 | PitServiceStatus | 否 | Players car pit service status bits |
| `PlayerCarPosition` | int32 | — | 否 | Players position in race |
| `PlayerCarPowerAdjust` | float32 | % | 否 | Players power adjust |
| `PlayerCarSLBlinkRPM` | float32 | revs/min | 否 | Shift light blink rpm |
| `PlayerCarSLFirstRPM` | float32 | revs/min | 否 | Shift light first light rpm |
| `PlayerCarSLLastRPM` | float32 | revs/min | 否 | Shift light last light rpm |
| `PlayerCarSLShiftRPM` | float32 | revs/min | 否 | Shift light shift rpm |
| `PlayerCarTeamIncidentCount` | int32 | — | 否 | Players team incident count for this session |
| `PlayerCarTowTime` | float32 | s | 否 | Players car is being towed if time is greater than zero |
| `PlayerCarWeightPenalty` | float32 | kg | 否 | Players weight penalty |
| `PlayerFastRepairsUsed` | int32 | — | 否 | Players car number of fast repairs used |
| `PlayerIncidents` | int32 | IncidentFlags | 否 | Log incidents that the player received |
| `PlayerTrackSurface` | int32 | TrackLocation | 否 | Players car track surface type |
| `PlayerTrackSurfaceMaterial` | int32 | TrackSurface | 否 | Players car track surface material type |
| `SessionFlags` | uint32 位掩码 | SessionFlags | 否 | Session flags |
| `SessionJokerLapsRemain` | int32 | — | 否 | Joker laps remaining to be taken |
| `SessionLapsRemain` | int32 | — | 否 | Old laps left till session ends use SessionLapsRemainEx |
| `SessionLapsRemainEx` | int32 | — | 否 | New improved laps left till session ends |
| `SessionLapsTotal` | int32 | — | 否 | Total number of laps in session |
| `SessionNum` | int32 | — | 否 | Session number |
| `SessionOnJokerLap` | bool | — | 否 | Player is currently completing a joker lap |
| `SessionState` | int32 | SessionState | 否 | Session state |
| `SessionTick` | int32 | — | 否 | Current update number |
| `SessionTime` | float64 | s | 否 | Seconds since session start |
| `SessionTimeOfDay` | float32 | s | 否 | Time of day in seconds |
| `SessionTimeRemain` | float64 | s | 否 | Seconds left till session ends |
| `SessionTimeTotal` | float64 | s | 否 | Total number of seconds in session |
| `SessionUniqueID` | int32 | — | 否 | Session ID |

### 进站服务（38）

| SDK 名称 | 类型/参考长度 | 原始单位 | 时间子采样 | SDK 说明 |
|---|---|---|---|---|
| `PitOptRepairLeft` | float32 | s | 否 | Time left for optional repairs if repairs are active |
| `PitRepairLeft` | float32 | s | 否 | Time left for mandatory pit repairs if repairs are active |
| `PitSvFlags` | uint32 位掩码 | PitSvFlags | 否 | Bitfield of pit service checkboxes |
| `PitSvFuel` | float32 | l or kWh | 否 | Pit service fuel add amount |
| `PitSvLFP` | float32 | kPa | 否 | Pit service left front tire pressure |
| `PitSvLRP` | float32 | kPa | 否 | Pit service left rear tire pressure |
| `PitSvRFP` | float32 | kPa | 否 | Pit service right front tire pressure |
| `PitSvRRP` | float32 | kPa | 否 | Pit service right rear tire pressure |
| `PitSvTireCompound` | int32 | — | 否 | Pit service pending tire compound |
| `Pitch` | float32 | rad | 否 | Pitch orientation |
| `PitchRate` | float32 | rad/s | 否 | Pitch rate |
| `PitchRate_ST` | float32[6] | rad/s | 是 | Pitch rate at 360 Hz |
| `PitsOpen` | bool | — | 否 | True if pit stop is allowed for the current player |
| `PitstopActive` | bool | — | 否 | Is the player getting pit stop service |
| `dpChargeAddKWh` | float32 | kWh | 否 | Pitstop charge add amount |
| `dpFastRepair` | float32 | — | 否 | Pitstop fast repair set |
| `dpFuelAddKg` | float32 | kg | 否 | Pitstop fuel add amount |
| `dpFuelAutoFillActive` | float32 | — | 否 | Pitstop auto fill fuel next stop flag |
| `dpFuelAutoFillEnabled` | float32 | — | 否 | Pitstop auto fill fuel system enabled |
| `dpFuelFill` | float32 | — | 否 | Pitstop fuel fill flag |
| `dpLFTireChange` | float32 | — | 否 | Pitstop lf tire change request |
| `dpLFTireColdPress` | float32 | Pa | 否 | Pitstop lf tire cold pressure adjustment |
| `dpLRTireChange` | float32 | — | 否 | Pitstop lr tire change request |
| `dpLRTireColdPress` | float32 | Pa | 否 | Pitstop lr tire cold pressure adjustment |
| `dpLTireChange` | float32 | — | 否 | Pitstop left tire change request |
| `dpPowerSteering` | float32 | — | 否 | Pitstop power steering adjustment |
| `dpQTape` | float32 | — | 否 | Pitstop qualify tape adjustment |
| `dpRFTireChange` | float32 | — | 否 | Pitstop rf tire change request |
| `dpRFTireColdPress` | float32 | Pa | 否 | Pitstop rf cold tire pressure adjustment |
| `dpRRTireChange` | float32 | — | 否 | Pitstop rr tire change request |
| `dpRRTireColdPress` | float32 | Pa | 否 | Pitstop rr cold tire pressure adjustment |
| `dpRTireChange` | float32 | — | 否 | Pitstop right tire change request |
| `dpTireChange` | float32 | — | 否 | Pitstop all tire change request |
| `dpWeightJackerLeft` | float32 | — | 否 | Pitstop left wedge/weight jacker adjustment |
| `dpWeightJackerRight` | float32 | — | 否 | Pitstop right wedge/weight jacker adjustment |
| `dpWindshieldTearoff` | float32 | — | 否 | Pitstop windshield tearoff |
| `dpWingFront` | float32 | — | 否 | Pitstop front wing adjustment |
| `dpWingRear` | float32 | — | 否 | Pitstop rear wing adjustment |

### 车内调节（43）

| SDK 名称 | 类型/参考长度 | 原始单位 | 时间子采样 | SDK 说明 |
|---|---|---|---|---|
| `dcABS` | float32 | — | 否 | In car abs adjustment |
| `dcAntiRollFront` | float32 | — | 否 | In car front anti roll bar adjustment |
| `dcAntiRollRear` | float32 | — | 否 | In car rear anti roll bar adjustment |
| `dcBrakeBias` | float32 | — | 否 | In car brake bias adjustment |
| `dcBrakeBiasFine` | float32 | — | 否 | In car brake bias fine adjustment |
| `dcBrakeMisc` | float32 | — | 否 | In car brake misc adjustment |
| `dcDRSToggle` | bool | — | 否 | In car toggle DRS |
| `dcDashPage` | float32 | — | 否 | In car dash display page adjustment |
| `dcDashPage2` | float32 | — | 否 | In car second dash display page adjustment |
| `dcDiffEntry` | float32 | — | 否 | In car diff entry adjustment |
| `dcDiffExit` | float32 | — | 否 | In car diff exit adjustment |
| `dcDiffMiddle` | float32 | — | 否 | In car diff middle adjustment |
| `dcDiffPreload` | float32 | — | 否 | In car diff preload adjustment |
| `dcEngineBraking` | float32 | — | 否 | In car engine braking adjustment |
| `dcEnginePower` | float32 | — | 否 | In car engine power adjustment |
| `dcFCYToggle` | bool | — | 否 | In car toggle full course yellow mode |
| `dcFuelCutPosition` | float32 | — | 否 | In car adv end straight fuel cut |
| `dcFuelMixture` | float32 | — | 否 | In car fuel mixture adjustment |
| `dcFuelNoCutToggle` | bool | — | 否 | In car fuel cut on straight active |
| `dcHeadlightFlash` | bool | — | 否 | In car headlight flash control active |
| `dcHysBoostHold` | bool | — | 否 | In car hold HYS deploy |
| `dcInLapToggle` | bool | — | 否 | In car toggle in lap settings |
| `dcLaunchRPM` | float32 | — | 否 | In car launch rpm adjustment |
| `dcLowFuelAccept` | bool | — | 否 | In car low fuel accept button |
| `dcMGUKDeployFixed` | float32 | — | 否 | In car MGU-K fixed deployment level adjustment |
| `dcMGUKDeployMode` | float32 | — | 否 | In car MGU-K deployment mode level adjustment |
| `dcMGUKRegenGain` | float32 | — | 否 | In car MUG-K re-gen gain adjustment |
| `dcPeakBrakeBias` | float32 | — | 否 | In car peak brake bias adjustment |
| `dcPitSpeedLimiterToggle` | bool | — | 否 | In car traction control active |
| `dcPowerSteering` | bool | — | 否 | In car power steering toggle |
| `dcPushToPass` | bool | — | 否 | In car trigger push to pass |
| `dcRFBrakeAttachedToggle` | bool | — | 否 | In car Right Front Brake attached(1) or detached(0) |
| `dcStarter` | bool | — | 否 | In car trigger car starter |
| `dcTearOffVisor` | bool | — | 否 | In car tear off visor film |
| `dcThrottleShape` | float32 | — | 否 | In car throttle shape adjustment |
| `dcToggleWindshieldWipers` | bool | — | 否 | In car turn wipers on or off |
| `dcTractionControl` | float32 | — | 否 | In car traction control adjustment |
| `dcTractionControl2` | float32 | — | 否 | In car traction control 2 adjustment |
| `dcTractionControl3` | float32 | — | 否 | In car traction control 3 adjustment |
| `dcTractionControl4` | float32 | — | 否 | In car traction control 4 adjustment |
| `dcTractionControlToggle` | bool | — | 否 | In car traction control toggle |
| `dcTriggerWindshieldWipers` | bool | — | 否 | In car momentarily turn on wipers |
| `dcWeightJackerRight` | float32 | — | 否 | In car right wedge/weight jacker adjustment |

## 保留的 22 个旧版/车型专用变量

这些名称未出现在上述 461 项参考中；保留兼容，不给它们套用未知的类型/单位或保证当前可用。连接后以真实描述为准。

- `dcBoostLevel`
- `dcRevLimiter`
- `dcWeightJackerLeft`
- `dcWingFront`
- `dcWingRear`
- `dpFNOMKnobSetting`
- `dpFUFangleIndex`
- `dpFWingAngle`
- `dpFWingIndex`
- `dpLrWedgeAdj`
- `dpPSSetting`
- `dpQtape`
- `dpRBarSetting`
- `dpRFTruckarmP1Dz`
- `dpRRDamperPerchOffsetm`
- `dpRWingAngle`
- `dpRWingIndex`
- `dpRWingSetting`
- `dpRrPerchOffsetm`
- `dpRrWedgeAdj`
- `dpTruckarmP1Dz`
- `dpWedgeAdj`

## 259 条会话资料路径

类型为参考模型的解析类型，YAML 本身是文本；含单位的字符串保留单位。列表字段按路径汇总，显示列表下标。这里只列模型的标量叶子，CarSetup 和游戏新增字段继续由运行时递归发现。`SessionStartTime`、`VRS_TelemetryLogger` 等工具附加属性未当成原生 SDK 字段计入。

### WeekendInfo（91）

| 路径 | 参考类型 | 无线电词库 |
|---|---|---|
| `WeekendInfo.Encoding` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackName` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackID` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackLength` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackLengthOfficial` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackDisplayName` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackDisplayShortName` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackConfigName` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackCity` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackState` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackCountry` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackAltitude` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackLatitude` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackLongitude` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackNorthOffset` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackNumTurns` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackPitSpeedLimit` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackPaceSpeed` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackNumPitStalls` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackType` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackDirection` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackWeatherType` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackSkies` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackSurfaceTemp` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackSurfaceTempCrew` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackAirTemp` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackAirPressure` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackAirDensity` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackWindVel` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackWindDir` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackRelativeHumidity` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackFogLevel` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackPrecipitation` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackCleanup` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackDynamicTrack` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.TrackVersion` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.SeriesID` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.SeasonID` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.SessionID` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.SubSessionID` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.LeagueID` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.Official` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.RaceWeek` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.EventType` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.Category` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.SimMode` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TeamRacing` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.MinDrivers` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.MaxDrivers` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.DCRuleSet` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.QualifierMustStartRace` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.NumCarClasses` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.NumCarTypes` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.AIRosterName` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.HeatRacing` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.AltAssetTag` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.BuildType` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.BuildTarget` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.BuildVersion` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.RaceFarm` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.NumStarters` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.StartingGrid` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.QualifyScoring` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.CourseCautions` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.StandingStart` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.ShortParadeLap` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.Restarts` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.WeatherType` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.Skies` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.WindDirection` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.WindSpeed` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.WeatherTemp` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.RelativeHumidity` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.FogLevel` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.TimeOfDay` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.Date` | DateTime | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.EarthRotationSpeedupFactor` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.Unofficial` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.CommercialMode` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.NightMode` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.IsFixedSetup` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.StrictLapsChecking` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.HasOpenRegistration` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.HardcoreLevel` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.NumJokerLaps` | int | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.IncidentLimit` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.IncidentWarningInitialLimit` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.IncidentWarningSubsequentLimit` | object | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.FastRepairsLimit` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.WeekendOptions.GreenWhiteCheckeredLimit` | string | 动态接入，并提供离线参考 |
| `WeekendInfo.TelemetryOptions.TelemetryDiskFile` | string | 排除：账号标识或磁盘路径 |

### SessionInfo（41）

| 路径 | 参考类型 | 无线电词库 |
|---|---|---|
| `SessionInfo.CurrentSessionNum` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.SessionNum` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.SessionLaps` | string | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.SessionTime` | string | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.SessionNumLapsToAvg` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.SessionType` | string | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.SessionTrackRubberState` | string | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.SessionName` | string | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.SessionSubType` | string | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.SessionSkipped` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.SessionRunGroupsUsed` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.SessionEnforceTireCompoundChange` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.QualifyPositions.Position` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.QualifyPositions.ClassPosition` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.QualifyPositions.CarIdx` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.QualifyPositions.FastestLap` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.QualifyPositions.FastestTime` | float | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.Position` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.ClassPosition` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.CarIdx` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.Lap` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.Time` | float | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.FastestLap` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.FastestTime` | float | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.LastTime` | float | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.LapsLed` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.LapsComplete` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.JokerLapsComplete` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.LapsDriven` | float | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.Incidents` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.ReasonOutId` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsPositions.ReasonOutStr` | string | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsFastestLap.CarIdx` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsFastestLap.FastestLap` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsFastestLap.FastestTime` | float | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsAverageLapTime` | float | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsNumCautionFlags` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsNumCautionLaps` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsNumLeadChanges` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsLapsComplete` | int | 动态接入，并提供离线参考 |
| `SessionInfo.Sessions.ResultsOfficial` | int | 动态接入，并提供离线参考 |

### DriverInfo（88）

| 路径 | 参考类型 | 无线电词库 |
|---|---|---|
| `DriverInfo.DriverCarIdx` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverUserID` | int | 排除：账号标识或磁盘路径 |
| `DriverInfo.PaceCarIdx` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverIsAdmin` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverHeadPosX` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverHeadPosY` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverHeadPosZ` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarIsElectric` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarIdleRPM` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarRedLine` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarEngCylinderCount` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarFuelKgPerLtr` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarFuelMaxLtr` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarFuelMaxKWh` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarMaxFuelPct` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarGearNumForward` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarGearNeutral` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarGearReverse` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverGearboxType` | string | 动态接入，并提供离线参考 |
| `DriverInfo.DriverGearboxControlType` | string | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarShiftAid` | string | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarSLFirstRPM` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarSLShiftRPM` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarSLLastRPM` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarSLBlinkRPM` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarVersion` | string | 动态接入，并提供离线参考 |
| `DriverInfo.DriverPitTrkPct` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverCarEstLapTime` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverSetupName` | string | 动态接入，并提供离线参考 |
| `DriverInfo.DriverSetupIsModified` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverSetupLoadTypeName` | string | 动态接入，并提供离线参考 |
| `DriverInfo.DriverSetupPassedTech` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverIncidentCount` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverBrakeCurvingFactor` | float | 动态接入，并提供离线参考 |
| `DriverInfo.DriverTires.TireIndex` | int | 动态接入，并提供离线参考 |
| `DriverInfo.DriverTires.TireCompoundType` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarIdx` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.UserName` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.AbbrevName` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.Initials` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.UserID` | int | 排除：账号标识或磁盘路径 |
| `DriverInfo.Drivers.TeamID` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.TeamName` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarNumber` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarNumberRaw` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarPath` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarClassID` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarID` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarIsPaceCar` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarIsAI` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarIsElectric` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarScreenName` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarScreenNameShort` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarCfg` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarCfgName` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarCfgCustomPaintExt` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarClassShortName` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarClassRelSpeed` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarClassLicenseLevel` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarClassMaxFuelPct` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarClassWeightPenalty` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarClassPowerAdjust` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarClassDryTireSetLimit` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarClassColor` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarClassEstLapTime` | float | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.IRating` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.LicLevel` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.LicSubLevel` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.LicString` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.LicColor` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.IsSpectator` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarDesignStr` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.HelmetDesignStr` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.SuitDesignStr` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.BodyType` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.FaceType` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.HelmetType` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarNumberDesignStr` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarSponsor_1` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CarSponsor_2` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.ClubName` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.ClubID` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.DivisionName` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.DivisionID` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.FlairName` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.FlairID` | string | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.CurDriverIncidentCount` | int | 动态接入，并提供离线参考 |
| `DriverInfo.Drivers.TeamIncidentCount` | int | 动态接入，并提供离线参考 |

### RadioInfo（17）

| 路径 | 参考类型 | 无线电词库 |
|---|---|---|
| `RadioInfo.SelectedRadioNum` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.RadioNum` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.HopCount` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.NumFrequencies` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.TunedToFrequencyNum` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.ScanningIsOn` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.Frequencies.FrequencyNum` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.Frequencies.FrequencyName` | string | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.Frequencies.Priority` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.Frequencies.CarIdx` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.Frequencies.EntryIdx` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.Frequencies.ClubID` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.Frequencies.CanScan` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.Frequencies.CanSquawk` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.Frequencies.Muted` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.Frequencies.IsMutable` | int | 动态接入，并提供离线参考 |
| `RadioInfo.Radios.Frequencies.IsDeletable` | int | 动态接入，并提供离线参考 |

### CameraInfo（5）

| 路径 | 参考类型 | 无线电词库 |
|---|---|---|
| `CameraInfo.Groups.GroupNum` | int | 动态接入，并提供离线参考 |
| `CameraInfo.Groups.GroupName` | string | 动态接入，并提供离线参考 |
| `CameraInfo.Groups.IsScenic` | bool | 动态接入，并提供离线参考 |
| `CameraInfo.Groups.Cameras.CameraNum` | int | 动态接入，并提供离线参考 |
| `CameraInfo.Groups.Cameras.CameraName` | string | 动态接入，并提供离线参考 |

### QualifyResultsInfo（15）

| 路径 | 参考类型 | 无线电词库 |
|---|---|---|
| `QualifyResultsInfo.Results.Position` | int | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.ClassPosition` | int | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.CarIdx` | int | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.Lap` | int | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.Time` | float | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.FastestLap` | int | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.FastestTime` | float | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.LastTime` | float | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.LapsLed` | int | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.LapsComplete` | int | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.JokerLapsComplete` | int | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.LapsDriven` | float | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.Incidents` | int | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.ReasonOutId` | int | 动态接入，并提供离线参考 |
| `QualifyResultsInfo.Results.ReasonOutStr` | string | 动态接入，并提供离线参考 |

### SplitTimeInfo（2）

| 路径 | 参考类型 | 无线电词库 |
|---|---|---|
| `SplitTimeInfo.Sectors.SectorNum` | int | 动态接入，并提供离线参考 |
| `SplitTimeInfo.Sectors.SectorStartPct` | float | 动态接入，并提供离线参考 |

## 联网 Data API 目录（未接入）

基址：`https://members-ng.iracing.com`。下列 **67 个读取端点**来自 [racedirector 维护的 OpenAPI 客户端目录](https://github.com/racedirector/iracing-data-api/blob/b71d0ac384883c58028965a8c4578b034dadd045/packages/api/client/axios/README.md)，固定提交 `b71d0ac384883c58028965a8c4578b034dadd045`，不含认证和文档自省端点。这是可追溯的参考快照，未使用用户账号逐个实测，不宣称覆盖服务端所有新端点。真实方法、参数、返回字段、授权和访问范围应以获准后 `/data/doc`、`/data/doc/:service/:method` 的返回为准。

| 服务 | 数据用途 | 本版状态 |
|---|---|---|
| car / carclass / track | 赛车、组别、赛道及资源资料 | 研究目录 |
| member / stats / driver_stats_by_category | 个人资料、成绩统计、最佳圈、最近比赛、生涯/年度汇总、积分榜、世界纪录 | 研究目录 |
| results | 比赛结果、逐圈时间、圈次图表、事件日志、比赛搜索 | 研究目录；不是驾驶时实时广播 |
| series / season | 系列赛、赛季、赛程、比赛指南及观赛会话 | 研究目录 |
| league / team / hosted | 联赛、车队、成员名单、积分及自建赛事 | 研究目录 |
| lookup / constants | 国家、执照、类别及其他枚举参考 | 研究目录 |
| time_attack | Time Attack 赛季成绩 | 研究目录 |

以下只有方法与路径，未在程序里发起请求；有些响应提供后续数据链接，不能假设首次响应就是完整比赛数据。

| 方法 | 路径 |
|---|---|
| GET | `/data/car/get` |
| GET | `/data/car/assets` |
| GET | `/data/carclass/get` |
| GET | `/data/constants/categories` |
| GET | `/data/constants/divisions` |
| GET | `/data/constants/event_types` |
| GET | `/data/driver_stats_by_category/{category}` |
| GET | `/data/hosted/combined_sessions` |
| GET | `/data/hosted/sessions` |
| GET | `/data/league/get` |
| GET | `/data/league/cust_league_sessions` |
| GET | `/data/league/directory` |
| GET | `/data/league/membership` |
| GET | `/data/league/get_points_systems` |
| GET | `/data/league/roster` |
| GET | `/data/league/season_sessions` |
| GET | `/data/league/season_standings` |
| GET | `/data/league/seasons` |
| GET | `/data/lookup/get` |
| GET | `/data/lookup/countries` |
| GET | `/data/lookup/drivers` |
| GET | `/data/lookup/flairs` |
| GET | `/data/lookup/licenses` |
| GET | `/data/member/get` |
| GET | `/data/member/award_instances` |
| GET | `/data/member/awards` |
| GET | `/data/member/chart_data` |
| GET | `/data/member/info` |
| GET | `/data/member/participation_credits` |
| GET | `/data/member/profile` |
| GET | `/data/results/get` |
| GET | `/data/results/event_log` |
| GET | `/data/results/lap_chart_data` |
| GET | `/data/results/lap_data` |
| GET | `/data/results/search_hosted` |
| GET | `/data/results/search_series` |
| GET | `/data/results/season_results` |
| GET | `/data/season/list` |
| GET | `/data/season/race_guide` |
| GET | `/data/season/spectator_subsessionids` |
| GET | `/data/season/spectator_subsessionids_detail` |
| GET | `/data/series/get` |
| GET | `/data/series/assets` |
| GET | `/data/series/past_seasons` |
| GET | `/data/series/season_list` |
| GET | `/data/series/season_schedule` |
| GET | `/data/series/seasons` |
| GET | `/data/series/stats_series` |
| GET | `/data/stats/member_bests` |
| GET | `/data/stats/member_career` |
| GET | `/data/stats/member_division` |
| GET | `/data/stats/member_recap` |
| GET | `/data/stats/member_recent_races` |
| GET | `/data/stats/member_summary` |
| GET | `/data/stats/member_yearly` |
| GET | `/data/stats/season_driver_standings` |
| GET | `/data/stats/season_qualify_results` |
| GET | `/data/stats/season_supersession_standings` |
| GET | `/data/stats/season_team_standings` |
| GET | `/data/stats/season_tt_results` |
| GET | `/data/stats/season_tt_standings` |
| GET | `/data/stats/world_records` |
| GET | `/data/team/get` |
| GET | `/data/team/membership` |
| GET | `/data/time_attack/member_season_results` |
| GET | `/data/track/get` |
| GET | `/data/track/assets` |

## 验证及仍需实车核对的部分

自动化回归覆盖：动态新变量、字段表版本变化、旋转缓冲区、单位校验、旧帧不可变、UTF-8/Latin1 会话、Pa/kPa 胎压、百分数、本车 CarIdx 与越界、时间子采样描述、缺失值拒答、自定义词库保存与切换。全套测试结果见 `artifacts/validation/iracing-tests.log`。

本版按模拟共享内存和演示界面验证；没有据此声称完成真实赛车全字段比对。实际验证时只需启动 iRacing、进入驾驶座舱，在 DeltaTR 选择 iRacing 并连接；无需安装额外共享内存插件、填写单位或登录 Data API。词库会换成本次会话实际公布的目录，原始键名可用于搜索定位。

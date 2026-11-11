# ACC 字段核验矩阵

布局基线：ACC Shared Memory 文档 v1.8.12。**以下全部尚未完成真实游戏 HUD 对照。** “已实现”只代表解码/质量控制存在，不表示实机验证通过。

Physics / Graphics / Static 字段均以 little-endian 解析；四轮顺序 FL / FR / RL / RR。候选单位在诊断界面用于核对，`NeedsVerification` 时禁止用于数值回答。

| 标准键 | 原始字段 / 页 | 起始偏移（字节） | 候选单位 | 实机证据 |
|---|---|---:|---|---|
| fuel | Physics.fuel | 12 | raw；确认后 L 或 kg | 待核对 HUD / MFD / 补油变化 |
| gear | Physics.gear | 16 | ACC gear code | 待验证倒挡/空挡编码 |
| rpm | Physics.rpm | 20 | rpm | 待核对 |
| speed | Physics.speedKmh | 28 | km/h | 待核对 |
| pressure.FL/FR/RL/RR | Physics.wheelPressure[4] | 88、92、96、100 | psi | 待核对冷/热胎、轮位 |
| temperature.FL/FR/RL/RR | Physics.tyreCoreTemperature[4] | 152、156、160、164 | °C | 待核对核心温度语义 |
| airTemperature | Physics.airTemp | 288 | °C | 待核对 |
| roadTemperature | Physics.roadTemp | 292 | °C | 待核对 |
| completedLaps | Graphics.completedLaps | 132 | lap | 待核对跨线/重开 |
| position | Graphics.position | 136 | position | 待核对单人/多人 |
| lastLapTime | Graphics.iLastTime | 144 | ms | 待核对；零不当有效圈速 |
| sessionTimeLeft | Graphics.sessionTimeLeft | 152 | raw | 待确认单位，未用于策略 |
| inPitLane | Graphics.isInPitLane | 1236 | boolean | 待核对进入/离开 |
| gameFuelPerLap | Graphics.fuelXLap | 1284 | L/lap | 待核对；始终是估计 |
| rainTyres | Graphics.rainTyres | 1316 | boolean | 待核对换胎 |
| validLap | Graphics.isValidLap | 1408 | boolean | 待核对切弯无效圈 |
| gameFuelEstimatedLaps | Graphics.fuelEstimatedLaps | 1412 | lap | 待核对；始终是估计 |
| missingMandatoryPits | Graphics.missingMandatoryPits | 1484 | count | 待核对赛事规则 |
| trackGrip | Graphics.trackGripStatus | 1556 | category 0..6 | 待核对干湿变化 |
| rainIntensity | Graphics.rainIntensity | 1560 | category 0..5 | 待核对 |
| rainIn10Minutes | Graphics.rainIntensityIn10min | 1564 | category 0..5 | 仅离散预报类别 |
| rainIn30Minutes | Graphics.rainIntensityIn30min | 1568 | category 0..5 | 仅离散预报类别 |
| tyreSet | Graphics.currentTyreSet | 1572 | set code | 待确认编号和换胎 |
| gapAhead / gapBehind | Graphics.gapAhead / gapBehind | 1580 / 1584 | ms | 待核对同圈/套圈；零暂禁用 |
| SharedMemoryVersion | Static.smVersion[15] | 0 | UTF-16 文本 | 版本元数据 |
| GameVersion | Static.acVersion[15] | 30 | UTF-16 文本 | 版本元数据 |
| CarModel | Static.carModel[33] | 68 | UTF-16 文本 | 标识 |
| Track | Static.track[33] | 134 | UTF-16 文本 | 标识 |

## 0.6.0 接入范围

上表保留基础字段的偏移。扩展字段见 `src/DeltaTR.Core/AccChannels.cs`。物理页读取 800 字节、赛况页 1588 字节、静态页 820 字节。包括制动温度与寿命、油门和踏板、车辆姿态、电子设定、进站预设、圈速、赛道旗语、辅助设置及活动车辆编号与坐标。单轮和四轮汇总共用同一批读数。仅解释有依据的单位，车型仪表偏移和未经确认的换算保留为原始读数。轮胎磨损倍率属于比赛设置。

布局里的未使用项（包括轮胎磨损、三段胎面温度、KERS/DRS 等）只占位。车手名字、姓氏和昵称只在本地内存中提供给词库问答，诊断文件和 AI 请求均排除这些个人字段。物理、赛况页分别以自己的包序号判定新鲜度。静态资料仅在驾驶会话有效时用于问答。

广播按 SDK v4 解码注册、赛段实时、单车实时、参赛名单、车辆及车手资料、赛道资料和事件。连接只发送注册、请求资料和注销。赛道槽位、车辆 ID、车号与总排名分别处理。前后车按总排名相邻确定，本车须以同赛段、同赛道的共享内存车 ID 匹配。分段比较取各车有效最佳圈的分段记录，按有数据的车辆排序。车手姓名保留在本地，不随 API 请求或诊断快照导出。

依据为 [ACC 共享内存规范 v1.8.12](https://github.com/SolusDev-sys/CppAccSharedMemory/blob/main/ACCSharedMemoryDocumentationV1.8.12.pdf) 和 [ACC Broadcasting SDK v4 通信实现](https://github.com/framf0rt/ACCBroadcastingTool-Modified/blob/master/ksBroadcastingNetwork/BroadcastingNetworkProtocol.cs)。

## 核验记录模板

每次真实实验另建本地记录，并在校准 JSON 的 `evidence` 中引用：

| 实验时间 | 游戏版本 / smVersion | 车型 / 赛道 | 字段 | 原始值 | HUD 值 / 单位 | 暂停/进站/重连结果 | 证据文件 | 结论 |
|---|---|---|---|---|---|---|---|---|
| 待执行 | | | | | | | | |

计划书 G0 要求至少 10 个高优先级字段实测、燃油单位明确；当前 G0 为 **未通过（缺实机证据）**。

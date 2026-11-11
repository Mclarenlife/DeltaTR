# 快照与录制协议 v1

一行 JSON 是一个 `RaceSnapshot`，编码 UTF-8。使用 camelCase 键和字符串枚举。当前只接受 `schemaVersion: 1`，未知版本拒绝加载，不默默猜测。

| 快照字段 | 含义 |
|---|---|
| sessionId | 本地会话 ID；断线快照可为空 |
| capturedAt | 本次采样的 UTC 时间 |
| elapsedMilliseconds | 本次数据源实例启动后的单调时间；录制内必须非递减 |
| phase | Disconnected / Connecting / Idle / Live / Paused / Replay / Faulted |
| origin | Live（真实采集尝试）/ Synthetic（人工合成）|
| gameVersion / sharedMemoryVersion | 来自 Static 页面；演示为 DEMO |
| carModel / track | 车辆和赛道标识 |
| physicsPacketId / graphicsPacketId | 各页包号；无法获取则 null |
| statusMessage | 可诊断的状态说明 |
| fields | 标准键到 Measurement 的映射 |

每个 Measurement 包含 `value / unit / observedAt / quality / source / gameVersion / isEstimated`。`value` 为可空 double；NaN/Infinity 以 null 标记，不写非法 JSON 数字。`observedAt` 是来源页面最近更新被观察到的时间，不是每次轮询时间。

## 质量语义

| 状态 | 语义 | 当前问答是否使用 |
|---|---|---|
| Valid | 已按匹配上下文校准、值域合理且新鲜 | 是；查询时再检查时效 |
| Estimated | 来自游戏或未来算法的推断 | 否；后续独立估计接口 |
| NeedsVerification | 能解码但单位/上下文未校准 | 否 |
| Stale | 来源页面超两秒未更新 | 否 |
| Unavailable | 不处于可用驾驶会话 | 否 |
| Invalid | 非有限值、超范围或非法枚举 | 否 |

`origin: Synthetic` 可拥有 Valid 字段，表示合成测试数据内部合法，**不表示真实游戏测量**。UI、CLI 和文本答案都会单独标识。

## 录制/回放约束

- 序列化保存测量值及元数据，不保存全部原始页；玩家个人字段从不导出。
- 录制 CreateNew，已有文件报错；容量 512 的队列满时丢弃新录制帧并计数。
- 切换数据源会结束当前录制；新数据源须创建新文件，避免混合时间基准。
- 回放保留原始质量与观测时间；以 `elapsedMilliseconds` 控制播放节奏，支持 0.1–100 倍速（CLI）。
- 损坏行会报告行号并停止；不静默跳过损坏行。程序异常终止留下的半行需由用户备份后处理。
- CSV 使用长表，一行一个测量值，保留 session、origin、phase、unit、quality、source、时间等，适合之后的实验分析。
- V0.1 不自动清除历史文件。存储/保留策略将在 SQLite 阶段实现。

## V0.1.1 核验产物

遥测 JSONL 仍为 schemaVersion 1，兼容旧录制。新增两类独立 JSON：

- `CalibrationEvidence`：上下文、实验说明和逐项 `HudComparison[]`；每项含 metric/rawValue/hudValue/unit/tolerance/source/observedAt/frozenAt/sessionId/note。校准文件新增可选 `evidenceSha256`；存在时验证同目录证据文件的 SHA256，不存在时兼容旧手工配置。
- `RecordingQualityReport`：帧数、来源和阶段计数、录制跨度、真实 Live 覆盖区间、长间隔次数、字段出现/缺失/新鲜/已校准计数，以及明确不自动验收的 gateStatus。

报告的字段比例是按记录帧计数，不是时间加权百分比；schemaVersion 也不能视为已通过 ACC 布局核验。

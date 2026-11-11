# 架构与实现边界

## 依赖方向

```text
Core ← Telemetry.Abstractions ← Telemetry.ACC
Core ← Storage
Core ← Engineer
Engineer ← Voice (System.Speech + NAudio)
Telemetry.ACC + Storage/CalibrationBundle + Engineer + Voice ← App
Telemetry.ACC + Storage + Engineer ← Diagnostics (开发工具)
```

`Core` 不引用 UI、共享内存、数据库或语音供应商。`IGameAdapter.Read()` 提供统一快照，`StateStore` 通过原子引用发布最新值。发布后不再修改快照及其字段字典；使用者将其视为只读对象。

V0.1.1 的 `Core/Validation` 承载 HUD 样本冻结、比较、双样本门禁和证据模型；`Storage/CalibrationBundle` 负责证据先写入、再发布校准配置；`Storage/RecordingAnalyzer` 流式统计录制质量，不缓存全部帧。0.2.0 桌面只保留校准窗口，录制质量报告窗口已移除。UI 不承担数值授权逻辑。

`AccLayout` 声明字段顺序、Windows UTF-16 字符宽度和 4 字节对齐。读取所需前缀：Physics 296 字节、Graphics 1588 字节、Static 420 字节。没有将这些前缀误写为完整 ACC 结构大小。静态页面内姓名所在空间只用于跳过偏移，不解码也不写入快照。

`ReadOnlyPage` 只用 `MemoryMappedFileRights.Read` / `MemoryMappedFileAccess.Read`。Physics / Graphics 前后比较 packetId，最多重试四次；Static 连读两次比较。该协议没有跨页事务或完整 seqlock，因此这只是尽力一致性检查，不能证明 Physics 和 Graphics 来自同一物理帧。

## 质量门禁

1. 检查唯一 `AC2-Win64-Shipping` 进程；读不到映射、权限不足或页不一致时释放全部句柄并等待重连。
2. 解码并限制数值范围；非有限浮点数变为 `null / Invalid`，不进入 JSON 数字或回答。
3. 判断 Live / Idle / Paused / Replay 等阶段；非 Live 数据只能诊断显示。
4. 按页的 packetId 跟踪更新，使用 `Stopwatch` 判断两秒过期。重复帧保留上次观测时间，不把轮询时间伪装为数据更新时间。
5. 验证校准上下文完全匹配，且字段单位属于允许的单位；否则 `NeedsVerification`。
6. 游戏自身的油耗/剩余圈数仍标记为 `Estimated`；当前文本问答只接收 `Valid`。
7. 查询再次检查整帧和字段时间，防止采样任务停止后复用旧数据。

燃油允许经验证后标记 `L` 或 `kg`，没有隐含换算。剩余会话时间单位未经验证时为 `raw`，尚不进入策略计算。

## 会话与线程

- 版本、赛道、车型、session type/index 变化，圈数回退、packetId 回退，Idle/Replay 回到 Live，以及断线重连会重新分配会话 ID。
- 暂停与恢复保持会话 ID，但暂停期间禁止实时问答。
- 这是启发式会话识别：同一进程、同一上下文且所有计数保持递增的服务器重置，可能无法仅靠共享内存识别。广播单独跟踪赛事、赛段和重播状态，变化时清空对手、圈速及事件缓存。
- WPF 后台任务每 50ms 采样，Dispatcher 每 200ms 更新；数据不通过 UI 线程采集。
- 历史开发探针（非桌面功能）：录制有界队列 512 帧，独立异步写磁盘，拒收计数可见。每 10 帧或关闭时刷新。突然掉电时尾部数据仍可能丢失；这是 JSONL 原型的边界。
- 停止、切换数据源、关闭窗口时取消语音与遥测采集，等待任务退出并清除最新状态。桌面不再录制。

## 回放与隐私

JSONL 保存标准化测量值、原始候选数值、质量与来源；不是完整内存页转储。按单调毫秒时间差调度回放，保留 UTC 原始观测时间。回放问答显式使用录制时钟；历史 CLI 标记 FILE REPLAY；桌面不包含回放，不用于当前比赛决策。

原始音频、车手姓名与密钥不写入遥测文件。广播通过本机 UDP 注册，只请求资料与实时更新。设置中的启用按钮可写入 ACC 广播端口。远端 AI 仅在用户配置并使用唤醒词时调用。合成数据具有独立 Origin；实时连接不会在失败后偷偷切到演示。

## 与研究计划的阶段性取舍

- 先采用第 15 节允许的 JSONL / CSV，SQLite 会话/事件存储进入下一阶段。
- 使用无 NuGet 测试工具以确保当前环境可构建；后续迁移 xUnit。
- 目前只有文本诊断，没有 ASR、TTS 和麦克风伪实现。
- 策略、UDP、LLM 需要新的实际实现及相应数据证据，不在当前版本中承诺已可用。


## 0.2.0 本地语音

独立 Voice 项目承载内存音频、SAPI ASR/TTS 和识别门禁。App/Input 轮询 F8 边沿，ViewModels/MainViewModel.Voice 维护聆听、识别与播报状态；语音请求代次阻止切换数据源后迟到结果覆盖新状态。识别完成时读取当前 StateStore，而不是使用按键时的旧快照。细节和限制见 VOICE_COMMANDS.md。

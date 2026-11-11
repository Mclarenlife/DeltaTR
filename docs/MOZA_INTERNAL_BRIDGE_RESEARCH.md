# Pit House 内部桥接研究

研究日期：2026-11-10。对象：本机 Pit House 1.4.2.3（x86）、R16 Ultra、KS Pro。

本文件记录研究证据。**16:04–16:05 固定文字复测、16:25–16:26 DeltaTR 真实问答演示联调均获用户实屏确认。** 0.13.1 已提供实验回答桥；真实比赛共存仍未验证，运行驾驶程序时暂停写入。

## 已找到的可调用入口

Pit House 的 `RacingSimulator 1.0` QML 模块暴露了：

```text
TestSendTelemetryData.sendTelemetryData(QVariantMap data)
```

它与界面中的 `isSwitch` 压力测试开关是两个不同入口。本次没有开启压力测试，也没有修改 `sendInterval`。

本机二进制静态元对象信息显示该方法的参数为 Qt 类型 8（QVariantMap），公开方法标记为 2。实际运行时只读检查确认 `typeof TestSendTelemetryData.sendTelemetryData === "function"`；两个开关属性分别是 boolean、number。

继续追踪元调用发现，该方法将传入的 map 转换后提交至 `TelemetryDataInputBuffer` 的更新路径。它不是已经隔离的屏幕专用写接口。原先的 `TelemetryDataOutputBuffer` 和 `TelemetryServer` 等名字没有在外部 QML 单例上下文中直接暴露。

候选链路：

```text
DeltaTR 本机消息源（0.13.1 已实现，带鉴权和租期）
  → Pit House QML 内部桥接（0.13.1 已实现，可备份恢复）
  → TestSendTelemetryData.sendTelemetryData
  → TelemetryDataInputBuffer
  → Pit House 后续遥测处理 / 显示服务（实屏文字已确认；比赛共存未验证）
  → KS Pro 仪表的 PlayerName 绑定
```

## 本轮试验

使用现有外部单例 `bin/CustomQmlPlugin/PageManager.qml` 临时加载探测代码。修改前备份原文件，安装和恢复均要求 SHA-256 匹配；没有修改 EXE、DLL 或固件。

1. 两轮一次性只读日志，分别检查可见对象名称、已知方法与属性类型。没有调用设备或遥测写方法。
2. 确认没有驾驶模拟器进程后，进行限时文字试验。只传入一个键：

   ```json
   {"v1/gameData/PlayerName":"reply|内部桥接中文验证 #1"}
   ```

3. 每两秒请求更新一次，编号递增，最多 75 条测试文字，最后写入 `idle|` 并停止。Qt 主线程繁忙可能使实际间隔延长。不开启 `isSwitch`，不提交速度、转速、档位、踏板等字段。
4. 测试期间通过已知 UDP PacketId 4 读接口查询转角，返回两个 540°，证明当时 Pit House 仍能与基座通信。这不能代替力反馈、灯效或实屏验证。
5. 探测载入后立即恢复原始安装文件，避免下次启动自动重复；测试结束后需正常重启 Pit House，移除内存中的临时代码和测试遥测。

15:54:32–15:57:25 日志确认 75 条测试文字及一次 `idle|` 提交均返回，随后记录自动停止，无 QML 调用异常。实屏结果等待用户确认，不能仅凭提交日志判定显示成功。

15:58 正常重启完成；原始 QML 文件校验一致，新会话没有探测日志，15:58:41 基座读接口再次返回两个 540°。Pit House 已恢复普通运行。DeltaTR 的正式方向盘功能未改动，仍保留独占串口冲突保护。

## 用户在场复测：实屏通过

用户最初反馈当前屏幕显示「请在 DeltaTR 开启方向盘屏显」，随后说明并未留意上一轮运行期间。这句话是当前仪表在 `PlayerName` 不含协议分隔符时的默认文案，并不能证明仪表是旧版，也不能用测试结束后的画面判定之前失败。

读取第一轮方向盘自身的渲染诊断还发现：测试期间 `video:4.frame` 停止增长，`video:6.frame` 持续增长，发送 `idle|` 后切回前者。这与仪表的动画状态切换相符，是链路可能生效的旁证，不能单独证明中文内容正确。提取结果保存在 `wheel-render-during-bridge.json`。

用户明确表示准备好观察后，使用同一文字入口复测，缩短为 30 条测试文字：

- Pit House 启动批次：`2026_1110_160411`；未运行驾驶模拟器，DeltaTR 独占屏显保持关闭。
- 测试期间用户直接确认：**「中文正常，编号持续递增」**。
- 16:05:37 的已知 UDP 读请求同时返回两个 540°，Pit House 继续保持基座通信。
- 16:05:49 完成第 30 条文字及最终 `idle|`，自动停止；安装文件已恢复，随后正常重启移除临时会话。
- 没有重新上传仪表，没有更改固件、力反馈参数或压力测试开关。

因此研究结论更新为：**Pit House 内部文字入口 → KS Pro 动态中文显示，已在无游戏运行的场景实测通过。** 正式 DeltaTR 回答桥尚未接入，真实游戏字段竞争、灯效、踏板效果和驾驶时的力反馈共存仍待验证。恢复普通 Pit House 后再次出现默认提示是预期行为。

## 正式接入前仍需解决

- **真实比赛覆盖**：此入口进入共享遥测输入缓冲区，游戏刷新可能覆盖 PlayerName。不得靠提高发送频率与游戏争抢，也不能伪造整组比赛数据。
- **专用通道**：优先验证自定义文字通道；若固件只接收已知通道，需要证明屏幕发送层可以局部覆盖，或明确限制 PlayerName 占用范围。
- **外部消息桥（已实现）**：本机 JSON 消息源、随机访问凭据、消息序号、过期时间、单请求轮询和断开回退；不执行传入脚本或任意方法名。
- **生命周期（已实现基础部分）**：关闭桥接、退出 DeltaTR 时停止更新；客户端在租期超时后释放自己的文字。实际硬件断连恢复仍需长时验证。
- **版本约束（已实现）**：固定 EXE 与原始 QML 哈希、备份恢复，遇到更新或文件变化拒绝自动覆盖。
- **共存验收**：分别验证真实游戏遥测、原有灯效和踏板效果、设置操作、力反馈、中文与动画、长时运行及恢复。

## 0.13.1 实际问答联调

已实现 `PitHouseBridge.cs` 本机服务、`PitHouseBridge.qml` 接收端、`PitHouseBridgeInstaller.cs` 版本锁定安装器。桌面屏显页提供连接方式、安装与恢复按钮。关闭/切换游戏/退出统一清理两个连接模式。

16:25:14–16:26:38：隔离运行 DeltaTR 演示模式，经原有 `SubmitQuestionAsync` 连续提问两轮胎温、油量、排名。回答数值随演示更新；Pit House 日志记录接收消息序号，客户端确认回传正常。用户实屏确认「回答、形象和气泡都正常」。应用正常完成并退出，绑定日志无错误；之后已知基座读请求仍正常返回。证据：`artifacts/moza-bridge-app-smoke/wheel-smoke.log`、同目录截图及绑定日志、`artifacts/moza-coexist/during-app-bridge.json`。

此验证没有采集麦克风，也没有启动真实比赛。游戏暂停写入、鉴权、旧会话确认拒绝、翻页和消息过期通过回归中的本机 HTTP 测试；不能把模拟的游戏检测条件当作实际驾驶共存验收。

当前保留已安装的实验桥接，Pit House 继续运行。DeltaTR 默认不自动开启桥接；卸载可通过屏显页恢复原始文件后重启 Pit House。

交付检查：120/120 回归通过；发布包全界面 129 项通过，绑定日志为空；`Test-BridgeClient.mjs` 对实际接收端处理代码验证乱序、过期、游戏阻断、关闭、重启、宿主失联和请求超时。安装器实际完成原始文件恢复→状态检查→重装→哈希检查。发布包位于 `artifacts/releases/0.13.1/app`。

## 可复核证据

- 工具：`tools/MozaScreenProbe/Inspect-QmlRuntime.py`，默认只读；`--probe-kind text` 才生成限时文字测试。
- 文件备份与校验：`artifacts/moza-coexist/runtime-probe*/manifest.json`。
- 运行时名称/类型日志：Pit House 的 15:47、15:50 启动批次 Common.log。
- 文字提交日志：`artifacts/moza-coexist/internal-text-bridge.log`。
- 同时保持设备通信：`artifacts/moza-coexist/during-text-bridge.json`。
- 恢复后设备通信：`artifacts/moza-coexist/after-internal-bridge-restore.json`。
- 用户在场复测提交日志：`artifacts/moza-coexist/internal-text-bridge-confirmed.log`。
- 用户在场复测期间设备通信：`artifacts/moza-coexist/during-confirmed-text-bridge.json`。
- 复测恢复后设备通信：`artifacts/moza-coexist/after-confirmed-bridge-restore.json`。
- 本机 EXE SHA-256：`7A76BE6E4052404395D1B65BB052CB324D30D1012B5BC409F4F0C78092001B94`。
- 原始 PageManager.qml SHA-256：`a313a50c94c4efaeb4d72c14f5cc9a4db1c8265da8e24d7eaa725e9db1355927`。

研究产物不随 DeltaTR 发布；没有把厂商 QML 或反汇编内容加入应用源码。

## 2026-11-10 专用字段验证：iRacing 座舱共存通过

为避免占用游戏的 `PlayerName`，本轮仅验证新字符串字段 `v1/gameData/DeltaTRRadio`，尚未修改正式应用的桥接和游戏阻断策略。

- 工具：`tools/MozaScreenProbe/Test-CustomField.py`。为当前 Pit House 建立两份文件的哈希备份：`CustomQmlPlugin/PageManager.qml` 与 `GameConfigs/Telemetry.json`；前者的两处文字写入/清理改用专用字段，后者只追加一条字符串定义，原有字段内容保持不变。
- 临时仪表 `DeltaTR Field Probe` 使用 780×248 屏幕；中央绑定专用字段，底部只读绑定原有游戏转速 `Rpm`。测试没有写入转速、车手姓名或车辆控制字段。
- 每轮前 30 秒约每三秒更新中文编号，后 30 秒保持相同消息序号。保持阶段 QML 不再提交新文字，因此能检查停止重复写入后是否被游戏刷新覆盖；本机服务继续续租以区别断联超时。
- 第一阶段无驾驶会话，用户实屏确认「中文编号变化，保持测试文字也能保留」。只读基座查询正常。
- 第二阶段用户进入 iRacing 单人练习座舱并停在维修区，Pit House 保持运行，DeltaTR 原方向盘屏显关闭。检测到 `iRacingSim64DX11` 与 Pit House 同时运行。用户实屏确认「编号递增，保持文字正常，游戏转速有读数且设备正常」。17:41:15 的已知只读基座查询再次成功，返回两个 540°。

结论：**在本机当前 Pit House、KS Pro 固件和 iRacing 维修区场景，新增专用文字字段可与真实游戏遥测共存，且保持阶段无需持续覆盖写入。** 客户端确认只证明调用返回；上述实屏结论来自用户观察。此试验没有证明所有游戏、长时间比赛、力反馈与所有灯效/踏板效果均已兼容，也没有直接测量旧 `PlayerName` 字段的实际覆盖频率。

证据保存在 `artifacts/moza-custom-field/`：`manifest.json`、`bench.stdout.log`、`iracing.stdout.log`、对应错误日志，以及 `bench-device-read.json`、`iracing-device-read.json`。备份 QML 含本机桥接凭据，不应对外分享。

正式接入应同步迁移桥接、仪表绑定与安装器；对遥测定义文件也做版本校验、备份和一致恢复，确认新字段安装成功后再解除对应游戏阻断。保留旧安装的迁移路径；不能仅删除当前游戏检测保护。

收尾已完成：三分钟测试发送器自动退出，最终确认序号 33；用户退出驾驶会话后，切回 `DeltaTR KS Pro Wave`，正常退出 Pit House，按备份逐字节恢复桥接 QML 和遥测定义，两者哈希均匹配测试前文件，随后重新启动 Pit House。首页正常显示 KS Pro、R16 Ultra 和 SRP2；恢复后的只读基座查询记录在 `artifacts/moza-custom-field/restored-device-read.json`。测试仪表保留在仪表库中但未启用，DeltaTR 的方向盘屏显保持关闭。此次没有发布解除游戏阻断的新应用版本。

## 0.13.3 专用字段正式接入

在上述独立验证通过后，应用、QML 客户端、声波仪表与独立串口目录解析统一使用 `v1/gameData/DeltaTRRadio`。不再按赛车进程阻断专用字段写入。桥接协议升级为 2，服务端要求客户端声明版本与字段；旧 QML 返回 409 并提示升级和重启，新 QML 拒绝旧服务或错误字段。此检查不等于自动识别当前实屏仪表，仍需在 Pit House 应用 `DeltaTR KS Pro Wave Radio`。

安装器新增遥测定义的版本哈希与备份，支持 V1 原位迁移，先验证所有目标/备份，再保存恢复记录并替换两份文件。部分安装不能开启；部分安装、部分恢复与升级中断均可恢复原始文件；检测到外部改动则拒绝覆盖。已在本机实际完成旧桥接升级 → 恢复两份原始文件 → 重新安装，并重启 Pit House。新仪表已上传且确认处于应用状态，原游戏字段逐项比较无变化，只有一个新增字段。

验证：编译无警告无错误；121/121 回归通过（含安装迁移、损坏拒绝、中断恢复、协议隔离）；QML 处理器测试通过；144 帧声波资源与专用字段绑定检查通过；发布包界面验证无绑定错误。18:04:05–18:05:29 使用正式发布包的隔离演示问答流程，连续两轮胎温、油量、排名均收到 Pit House 确认。用户实屏确认「中文回答切换和声波均正常」。18:05:10 基座只读查询正常。结束后验证进程自动退出，端口释放。

证据：`artifacts/moza-radio-0133/` 下的 `installed-field-audit.json`、`shell-smoke.log`、`during-answer-device-read.json`，以及 `wheel/wheel-smoke.log`、`wheel/radio.bindings.log`（空）。回归日志为 `artifacts/moza-custom-field/regression-0133.log`。发布目录 `artifacts/releases/0.13.3/app`。当前保留已安装的 V2 桥接、新仪表和运行中的 Pit House；自动切换旧 DeltaTR 窗口遇到窗口标识错误，旧 0.13.2 窗口未自动关闭，需手动改用新入口。

范围：专用字段的真实 iRacing 共存证据来自前一阶段维修区验证；0.13.3 正式问答实屏使用演示遥测。本轮未进行真实麦克风采集、比赛驾驶、ACC / LMU 共存及长时稳定性验收。

QML/C++ 元对象互操作的框架背景参考 [Qt 官方文档](https://doc.qt.io/qt-6/qtqml-cppintegration-exposecppattributes.html)；具体 Pit House 方法及行为来自本机检查，不能从 Qt 文档推定 MOZA 对该入口提供官方支持。

## 0.13.4 仪表切换接入（2026-11-10）

新增同一讲话键的短按/长按判定：短按切换画面，长按 500 ms 开始语音、松开发送。赛事提醒和自动化沿用实际消息展示入口，等待语音及文字阅读时间结束返回；手动打开保持显示。记录原仪表 ID，恢复时重新查找索引；外部手动切换让出控制，超时不连续重试，失联及关闭尝试恢复本次会话替换的仪表。

桥接协议升级至 3，携带显示请求及当前仪表状态。设备发现使用 Pit House 自身的 ValidBaseControllerProxyListModel / ValidHubControllerProxyListModel 和 ScreenProgramManager；首次尝试引用主窗口局部 mozaDeviceManager 失败，已修复。DashboardListModel 按唯一标题解析已上传的 Wave Radio，兼容 Pit House 导入时重生成 GUID；仅调用现有 manager.applyDashboard(index)，未发送车辆控制或力反馈指令。安装器支持旧 V2 桥接升级并保留原始双文件备份。

验证：123/123 回归、125 项界面检查、QML 协议及 JS 切换状态测试通过；无界面绑定错误。18:36:56–18:37:49 真实桥接测试以 Core 为比赛仪表，先手动打开并显示演示胎温回答、返回，再分别显示合成黄旗和自动化测试消息并返回，七次状态回读与最终 PASS 已记录。测试结束释放显示连接；18:38:30 Pit House 的 PacketId 4 只读基座查询正常。

证据在 artifacts/moza-switch-0134/：regression-final.log、shell/shell-smoke.log、hardware-discovery/dashboard-switch.log、device-health.json。首次失败诊断保留在 hardware 与 hardware-diagnostic，不能将这些旧失败记录当成最终结果。用户于 2026-11-11 确认切换功能已通过实屏验证；物理按钮及麦克风、真实驾驶中事件触发未在本轮验证。当前 V3 桥接保持安装，Pit House 运行并已返回 Core；使用新发布包 artifacts/releases/0.13.4/app。

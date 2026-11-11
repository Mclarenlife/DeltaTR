# DeltaTR 语音识别选型与验证（2026-11-07）

本次接入 SenseVoiceSmall 的 int8 ONNX 模型，通过项目已有的 sherpa-onnx 2.0.1 Windows x64 CPU 运行库推理。录音仍使用 NAudio，按住讲话键采集，松开后将整句送入 SenseVoice；语音只保留在内存中。新安装默认 SenseVoice，旧版配置保留原选择，可在设置中切换。

## GitHub 项目对比

星数为调研当天读取的近似值，会变化。星数不代表赛车场景识别率。

| 项目 | GitHub 热度 | 与 DeltaTR 的关系 |
| --- | --- | --- |
| [whisper.cpp](https://github.com/ggml-org/whisper.cpp) | 约 5.5 万星 | 通用语音识别备选；本次未集成，也未在本机跑对比，不能据此判断优劣 |
| [FunASR](https://github.com/modelscope/FunASR) | 约 2.1 万星 | 中文识别、VAD、热词等工具生态；不同模型能力不同 |
| [SenseVoice](https://github.com/QwenAudio/SenseVoice) | 约 9700 星 | 本次选用的整句识别模型，面向中文短句，CPU 部署，无需 Python 服务 |
| [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) | 约 1.6 万星 | 项目已有推理运行库，同时承载 Zipformer 与新增 SenseVoice |
| [NAudio](https://github.com/naudio/NAudio) | 约 6300 星 | 已在使用的 .NET 音频采集库；换录音库本身不保证识别改善 |

选择依据是中文识别覆盖、按键讲话的整句输入方式、既有 Windows 运行库和本地资源成本。[官方部署说明](https://k2-fsa.github.io/sherpa/onnx/sense-voice/pretrained.html)提供 SenseVoice int8 模型，模型文件约 239 MB；本次固定为 2 个 CPU 推理线程。中文模型提示固定为 zh，当前产品不提供语言选择。

## 实现边界

- SenseVoice 不提供本次可用的原生热词加权；自定义词库通过既有整句匹配与赛车同音纠错生效。
- 唤醒词仍须出现在句首，随后才调用设置中的 AI API。识别自身不访问网络。
- 不伪造置信度，也不使用 Windows 的置信度阈值过滤 SenseVoice。
- 噪声导致无法区分轮位时拒绝匹配，避免把错误轮位当成正确遥测问题。
- 每句输入有长度上限，串行访问模型；取消或切换引擎后不再提交旧识别结果。原生推理不能中途打断，完成后检查取消；释放模型等待当前调用结束。
- 模型版本、模型与词元 SHA-256 固定在 scripts/install-sensevoice.ps1。模型与代码许可分别记录在 THIRD_PARTY.md。

## 小样本验证

测试使用 Windows 中文合成语音，无真人录音、无扬声器输出。7 个输入包含 4 条遥测问句、1 条自定义唤醒词 AI 问句、2 条应拒绝的非命令；另测纯静音。带噪版本将语音幅度降为四分之一，再加入固定种子的白噪声与 80 Hz 低频噪声。两条链路沿用实际产品行为：SenseVoice 使用整句音量归一化，Zipformer 接收流式采样。因此结果比较完整识别链路，并非严格控制前处理的纯模型榜单。

| 正确路由或拒绝 | SenseVoice | Zipformer |
| --- | --- | --- |
| 干净合成语音 | 7/7 | 6/7 |
| 低音量 + 合成噪声 | 6/7 | 5/7 |

干净“胎温”：SenseVoice 成功匹配；Zipformer 输出“台湾”并拒绝。带噪“右后胎压多少”：两者输出“用后胎压多少”，均未匹配，没有查询错误轮位。此失败保留在报告中，不为通过测试添加不可靠替换规则。

本机本轮 SenseVoice 干净组平均约 194 ms（含首次模型加载），带噪组平均约 66 ms（已热启动）。这些数值不含麦克风采集及播报，不代表其他硬件性能。测试规模小，不能推断真人、口音、真实游戏声或所有设备下的识别率。

验证产物：`artifacts/validation/isolated-5c71059c3ced42be82c1ee5f18adde85/speech-benchmark.json`。

复现：

```powershell
.\scripts\install-sensevoice.ps1
.\scripts\install-zipformer.ps1
.\scripts\validate.ps1 -SpeechBenchmark
```

验证需要系统中文合成语音；报告写入新的 artifacts/validation 目录。应用发布目录包含模型，使用时无需再次下载。

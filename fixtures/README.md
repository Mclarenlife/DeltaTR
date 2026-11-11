# 回放样本

`synthetic-session.jsonl` 为程序 SyntheticAdapter 人工生成的 4 帧数据，Origin 为 Synthetic。可以在界面“打开回放”中加载，也可以用探针回放。

这些值不来自真实 ACC，不可作为 HUD 单位校准、燃油模型训练或任何实机验收证据。协议字节级样本在 `tests/DeltaTR.Tests/Program.cs` 中使用独立的字面量偏移构造。

真实录制存放在被版本控制忽略的 `recordings/` 目录。纳入测试仓库前应获授权并去除个人信息。

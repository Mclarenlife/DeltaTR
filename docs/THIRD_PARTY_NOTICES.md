# 协议来源与依赖

本版生产代码只依赖 .NET 基础类库和 WPF，没有引入社区采集实现、语音模型、Python 运行时或云服务 SDK。

协议字段顺序依据计划书引用的 **ACC Shared Memory Documentation v1.8.12**；资料标题、作者体系及布局应与用户本地安装版本继续核对。

- [ACC 协议文档镜像](https://github.com/rrennoir/PyAccSharedMemory/blob/main/ACCSharedMemoryDocumentationV1.8.12.pdf)：用于字段顺序、轮位、枚举和单位歧义的研究依据。运行程序不依赖此文件，发布目录不包含该 PDF。
- [Microsoft MemoryMappedFile.OpenExisting](https://learn.microsoft.com/en-us/dotnet/api/system.io.memorymappedfiles.memorymappedfile.openexisting?view=net-10.0)：本地只读命名内存映射 API。
- [微软 .NET 10 下载页](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)：开发 SDK 与运行时。项目 bootstrap 固定经过校验的 SDK 版本，不自动追踪最新版。
- [微软 SDK 发布元数据](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json)：bootstrap 中 10.0.103 下载 URL 和 SHA512 的来源。

本版不复制社区仓库实现代码，不将参考资料可访问性解释为游戏 SDK、语音权重、声音或商业发布许可。后续加入第三方包和模型时逐项记录版本、许可证和分发范围。

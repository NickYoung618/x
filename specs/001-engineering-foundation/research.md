# 工程决策依据

日期：2026-09-15。范围：基础工程，不推断现场性能。

| Decision | Rationale | Alternatives considered |
| --- | --- | --- |
| .NET 10 SDK 10.0.401 | V1.3 指定 .NET 10；官方元数据当前稳定 SDK；本地独立目录安装、验证 SHA512 | 已有 10.0.302 可用但旧；不修改其他项目 SDK |
| Spec Kit v1.0.7 Codex skills 模式 | 使用官方 init 生成的模板和脚本，版本固定 | 不用手写目录冒充已安装；不调用旧版 --ai 参数 |
| GitHub 托管 Linux/Windows 核心 CI | 仓库当前无 self-hosted runner，可先验证跨平台核心 | 现有 Windows Server 2019 后续用于实际部署与桌面；当前不假设已装 SDK/WebView2 |
| 纯规则计划与只读 Host 为第一个特性 | 不依赖未冻结 PLC 点表，可检查普通件批采次序并供前端预览 | 不先实现假协议、不用预览冒充动作闭环 |
| 包含翻面/重扫屏障、不生成坐标 | V1.3 第 5.3 节要求每次翻面后重新定位 | 下一面坐标不能在翻面前一次性写死 |
| 按现有三个责任边界建项目 | 清楚引用方向并保持实际内容 | 不为 15 个逻辑模块创建 15 个空进程/工程 |

官方参考（2026-09-15 查询）：

- [Spec Kit 使用和流程](https://github.com/github/spec-kit)
- [.NET 10 官方发布元数据](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json)
- [GitHub 的 .NET 构建测试说明](https://docs.github.com/en/actions/tutorials/build-and-test-code/net)

本增量无待研究的阻断参数。设备协议编码、真实相机/算法和机械边界的确认时点见仓库 docs。

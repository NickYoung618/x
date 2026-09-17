# Research: VirtualPlc 全量测试验证

## Decision 1: 使用 Spec Kit 本地 generic 集成

**Decision**: 使用已安装的 Specify CLI 0.12.10，在 `pj1` 内生成 `.specify`、`.speckit` 和
`specs/001-validate-virtual-plc`。

**Rationale**: CLI 的模板随安装包内置，不需要网络。默认 Codex 集成会写只读的
`.agents/skills`，generic 集成允许把命令放入可写的 `.speckit/commands`，仍是官方流程。

**Alternatives considered**: 手工模拟 Spec Kit 会缺少官方脚本和模板；写到用户目录违反
用户的目录约束。

## Decision 2: 保留生产 .NET 10，增加测试兼容主机

**Decision**: 不修改 `src/VirtualPlc/VirtualPlc.csproj` 和 `global.json`。测试目录提供一个
`net8.0` Web 项目，链接相同的 `.cs`、`appsettings.json` 和 `wwwroot` 文件。

**Rationale**: 当前主机只有 SDK/runtime 8.0.129，无法执行 net10 构建。链接源文件能证明
当前源码可在共同 API 子集上编译并运行，同时把 .NET 10 构建诚实记录为 NOT RUN。

**Alternatives considered**: 降级生产项目会改变用户交付目标；运行既有 net8 二进制不能
确认与当前源码一致；联网安装 SDK 会引入目录外写入和外部依赖。

## Decision 3: 黑盒系统测试优先

**Decision**: 通过 TCP 和 HTTP 完成所有断言，验证器不引用生产程序集。

**Rationale**: 这与上位机真实接入方式一致，能够同时覆盖服务托管、序列化、协议解析、
状态机和静态页面。

**Alternatives considered**: 单元测试私有方法覆盖面看似更细，但不能证明线上的 Modbus
报文和 HTTP 合同可用。

## Decision 4: 无测试框架依赖的可执行验证器

**Decision**: 使用标准库编写控制台 runner，集中注册检查并输出 JSON。

**Rationale**: `NuGet.Config` 明确清空包源；自包含 runner 离线可编译，退出码和诊断信息
也适合 CI 或现场复验。

**Alternatives considered**: xUnit/MSTest 需要额外 NuGet 包，在当前离线约束下不可复现。

## Decision 5: 随机兜底只验证集合与策略

**Decision**: 对 Camera、Barcode、Algorithm、Recipe、Storage、Mes、Other 的失败，仅断言
继续流程、不报设备错误、启用 fallback，且 decision 属于 OK/NG/Pending。

**Rationale**: 权重随机输出不是确定性合同；强行断言某个值会造成偶发失败。

**Alternatives considered**: 固定随机种子需要修改生产实现，不符合黑盒验证边界。

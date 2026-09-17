# Implementation Plan: VirtualPlc 全量测试验证

**Branch**: `001-validate-virtual-plc` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/001-validate-virtual-plc/spec.md`

## Summary

增加一个无第三方包的黑盒系统验证器和单命令脚本。脚本先识别 SDK 环境；在当前只有
.NET 8 的机器上，用测试专用 Web 项目链接 VirtualPlc 的同一组源文件和静态资源，启动
兼容主机，再通过真实 TCP/HTTP 接口执行点表、协议、动作、互锁、故障、复位与流程兜底
测试。验证器输出 JSON，脚本汇总为 Markdown，并将 .NET 10 生产构建作为独立环境门禁。

## Technical Context

**Language/Version**: C#；生产目标 .NET 10，当前行为验证使用本机 .NET 8 SDK 8.0.129  
**Primary Dependencies**: ASP.NET Core shared framework；无外部 NuGet 包  
**Storage**: 无持久化；JSON/Markdown 报告写入 `VirtualPlc/test-results/`  
**Testing**: 自包含控制台验证器，原始 Modbus TCP 客户端，`HttpClient`，shell 编排  
**Target Platform**: Linux loopback；设计上可在具有 .NET 10 SDK 的项目环境复跑  
**Project Type**: Web 服务加系统验证工具  
**Performance Goals**: 单次全量验证少于 60 秒  
**Constraints**: 全部写入 `pj1`；离线；不更改生产 `net10.0`；不依赖真实硬件  
**Scale/Scope**: 29 个点位、6 个 Modbus 功能码、9 个故障、11 个流程分类

## Constitution Check

*GATE: Passed before research and rechecked after design.*

- **Traceability**: 每个验证组在代码和报告中关联 FR 编号；任务表覆盖 FR-001～FR-014。
- **Protocol fidelity**: 所有行为检查均走公开 TCP/HTTP 接口；不引用业务程序集内部类型。
- **End-to-end**: 脚本负责构建、启动、等待就绪、执行、停止和生成报告。
- **Safety/fault semantics**: 9 种故障均有独立场景，动作故障检查一次性消费和第二次恢复。
- **Reproducible evidence**: 无第三方包，结果在 `VirtualPlc/test-results/`；.NET 10 单列门禁。
- **Constraint exception**: 测试兼容主机以 .NET 8 编译同源文件，仅用于当前环境行为验证，
  不改变或替代生产项目的 .NET 10 构建。该例外由 constitution 明确允许。

## Project Structure

### Documentation (this feature)

```text
specs/001-validate-virtual-plc/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── validation-contract.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
VirtualPlc/
├── src/VirtualPlc/                         # unchanged production service
├── tests/VirtualPlc.SystemValidation/
│   ├── VirtualPlc.CompatibilityHost.csproj # linked-source behavior host
│   ├── VirtualPlc.SystemValidation.csproj  # black-box runner
│   └── Program.cs
├── scripts/
│   └── validate.sh                         # one-command orchestration/reporting
└── test-results/                           # generated reports/logs
```

**Structure Decision**: 将测试代码与生产项目分离。兼容主机只链接生产源文件，确保当前
环境能编译正在检查的代码，同时保留生产 `VirtualPlc.csproj` 的 `net10.0` 声明不变。

## Phase 0: Research Decisions

研究结论见 [research.md](research.md)。所有技术未知项均已解决：Spec Kit 0.12.10 可离线
初始化；项目无外部包；本机无 .NET 10；原始 TCP 客户端可完整观察 Modbus 异常响应。

## Phase 1: Design and Contracts

- [data-model.md](data-model.md) 定义检查、运行、点位、故障场景和环境门禁。
- [contracts/validation-contract.md](contracts/validation-contract.md) 固定测试入口、JSON 输出、
  退出码，以及 Modbus/HTTP 验证边界。
- [quickstart.md](quickstart.md) 给出唯一推荐命令和预期结果。

设计后 Constitution Check 仍通过；没有新增例外或未解决澄清项。

## Complexity Tracking

| Decision | Why Needed | Simpler Alternative Rejected Because |
|----------|------------|--------------------------------------|
| 测试专用兼容主机 | 当前环境没有 .NET 10，但必须验证当前源文件而非过期二进制 | 直接运行已有 net8 DLL 无法证明它与当前源码一致 |
| 自实现小型 Modbus 客户端 | 必须校验异常码、事务号和畸形 PDU，且项目要求离线无依赖 | 现有冒烟客户端会把异常统一包装为 IOException，证据粒度不足 |

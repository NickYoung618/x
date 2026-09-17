# Implementation Plan: V1.3 整体软件框架

**Branch**: `framework-v13-foundation` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)

## Summary

按 V1.3 第 3、10、13、18 节把现有后端整理为可逐工位扩展的骨架：保留 Domain/Application/Infrastructure/唯一 Host，增加不含业务逻辑的外部合同项目；Application 提供十五责任模块与未实现能力清单，Host 向前端报告真实状态并对未来任务入口默认拒绝。独立文档索引架构书所有必测场景与工位 PR 门槛。现有内存工作流和旧协议探针不接生产入口。

## Technical Context

- **Language/Version**: C# / .NET 10，锁定 SDK 10.0.401；外部 Vue/WPF/Python 仍按 V1.3 规划，由后续负责同学实现。
- **Dependencies**: 现有四工程及 xUnit；新增纯 BCL `Inspection.Contracts`，Host 引用它；Application 仍只依赖 Domain，Domain 无外部引用。
- **Storage**: 无新业务表、媒体或生产日志写入；框架状态为只读静态交付事实。
- **Testing**: HTTP 合同、项目依赖方向、已发布 Host 冒烟、原 42 项核心加新增架构检查、旧协议两进程回归；CI Linux/Windows。
- **Target Platform**: Linux 开发、Linux/Windows CI；不声明 WPF 实机或真实设备通过。
- **Constraints**: 默认无设备绑定，生产命令无实现时 HTTP 501；生产就绪派生为 false；能力状态不冒充设备健康。
- **Scale/Scope**: 十五责任模块和未来的工位/横向能力边界；框架 PR 零新增物理动作。

## Constitution Check

设计前：V1.3 优先；单体分层和唯一 Host；Domain 无 SDK/HTTP；机械单通道及未知动作不改；所有未实现行为 fail closed；旧协议、V1.3 和整盘证据分开。设计后复核项目引用、状态/命令合同、旧测试持续通过、无默认合成到位。无章程豁免。

## Project Structure

```text
backend/src/Inspection.Contracts/          外部状态/错误 DTO，无业务状态机
backend/src/Inspection.Domain/             现有纯工艺规则
backend/src/Inspection.Application/        模块/能力目录、既有流程框架及端口
backend/src/Inspection.Infrastructure/     既有旧协议工程适配；正式适配后增
backend/src/Inspection.Host/               唯一组合根、状态/拒绝路由
backend/tests/Inspection.Host.Tests/        架构 HTTP 与边界测试
docs/architecture/                           模块图和 §18 验收映射
specs/007-v13-framework-foundation/         Spec Kit 与证据
```

`frontend/`、`desktop/`、`algorithms/`、数据库部署工具等由对应阶段建立实际可运行项目；框架文档给出接入点，不以空目录冒充交付。

## Design Decisions

状态 DTO 保留旧 `architectureVersion/stage/productionReady/unavailableCapabilities` 字段并追加 `runtimeMode/modules`。模块 State 为 ContractOnly/FrameworkOnly/LegacyEngineering/Implemented；本轮不出现 Implemented。Host `/api/jobs/prepare` 返回稳定 501 问题结构，不生成任务；原 `/api/jobs/start` 保持 404。十五模块是静态责任映射，后续 PR 更新必须附实际能力证据，生产 Ready 不由配置开关指定。验收映射覆盖 V1.3 §18 的全部必测条目，每条有责任增量、层级与证据；当前不可能实测的均标 NOT RUN。

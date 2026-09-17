# Implementation Plan: 普通整盘中台流程框架

**Branch**: `003-ordinary-workflow-foundation` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)

## Summary
以原OrdinaryTrayPlanner作为顺序来源；Application增加事件驱动的唯一TrayWorkflow状态所有者和带界限的单运动通道。Workflow只发效果、接带关联ID的事件，不在事件处理中等待机械或算法。测试用确定性端口替身演练两件两面AB及关键故障。

## Technical Context

- **Language/Version**: C# / .NET 10，精确SDK10.0.401。
- **Primary Dependencies**: 仅BCL和现有xUnit；Domain无外部引用。
- **Storage**: 本轮进程内状态，关键持久化和恢复下一增量。
- **Testing**: xUnit Application测试及现有Gaode.slnx构建/回归；接口级模拟。
- **Target Platform**: Linux本机实测，CI矩阵沿用现有Linux/Windows核心作业；未在Windows本地实测。
- **Project Type**: 模块化单体Host中的Application/Domain规则，不新增服务进程。
- **Performance Goals**: 每个状态事件同步短处理；运动/采集/算法完成通过后续事件，不在状态所有者中长等待。
- **Constraints**: 无协议假定，无随机质量结果，不声称持久安全或完整整盘。
- **Scale/Scope**: 首样例2件2面AB、8个检测Capture；工艺按配置扩展，不写死成生产方案。

## Constitution Check

设计前/后：V1.3为唯一业务基准；唯一业务状态所有者；Domain保持纯规则；Motion单通道；未知动作禁重发；翻面重扫保留PartId；普通项失败明确终态；不把未保存/未判定任务放行。无章程豁免。

## Project Structure

- `backend/src/Inspection.Application/Workflow/`：端口、事件、效果、状态协调器。
- `backend/src/Inspection.Application/Motion/`：单设备动作通道，不感知点表。
- `backend/tests/Inspection.Application.Tests/`：确定性完整顺序与故障反例。
- `specs/003-ordinary-workflow-foundation/`：规格、计划、研究、契约、任务和验证。
- `docs/update-goals.md`、`docs/testing.md`：更新目标与测试证据。

## Complexity Tracking

无。状态机只推进至AwaitingDecision，不提供对外启动业务端点。端口实现留以后续设备/算法增量。

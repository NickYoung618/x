# Implementation Plan: 首工位——初始 3D 定位

**Branch**: `station-01-initial-3d` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)

## Summary

在已有 `TrayWorkflow`、`MotionExecutionLane` 上补齐公共移至 3D 位、受理/完成关联和首轮 3D 定位。由 Host 统一组合工作流与独立合成设备/定位适配器；合成入口显式受限。通过状态/错误/轨迹证明一条工位链可运行，终点为 WaitingTrayCode，不执行 F。实际 PLC 点表和 3D SDK 仍由端口隔离。

本分支已合入框架 PR #5 的基线 `d030684`：复用 `Inspection.Contracts` 的对外状态/错误合同和十五模块能力目录；未实现能力保持默认拒绝。首工位实现时只将有独立证据的 S01 能力从 `ContractOnly`/`FrameworkOnly` 改为实际状态，不把整个平台标为生产就绪。

## Technical Context

- **Language/Version**: C#/.NET 10，SDK 10.0.401，仓库锁定依赖。
- **Dependencies**: 现有 Application、Motion 和 ASP.NET Core Host；不新增生产设备 SDK 假实现。
- **Storage**: 本工位需要记录诊断/动作意图与反馈的可靠边界；先明确恢复语义，必要时复用现有持久化能力或提交独立小型实现，不以进程内布尔值声称断电安全。
- **Testing**: Application 状态测试、Host 受限入口及真实进程合成联调；Linux/Windows CI 构建与测试，旧 V6 回归独立报告。
- **Target Platform**: Linux 开发与 CI；Windows CI 编译/核心测试。真机、WPF、SDK 暂不验收。
- **Constraints**: 不默认真实设备使用合成到位；不复用旧点表表示 V1.3；只有一个运动任务；超时后先对账。
- **Scale/Scope**: 两槽合成样例及工位故障矩阵；无配方、F 扫码、质量/整盘闭环。

## Constitution Check

设计前：V1.3 §5.2 顺序、唯一状态所有者、单运动通道、未知动作禁止盲重发、合成与实物来源隔离、分层证据满足章程。设计后需在实现 PR 复核：Host 只有一个业务入口，Domain 无 SDK/HTTP，关键意图/反馈可靠保存或将恢复能力明确标为未交付，不把单元测试冒称真机或整盘通过。无预设豁免。

## Project Structure

- `backend/src/Inspection.Application/Workflow/`：工位状态与端口/事件合同。
- `backend/src/Inspection.Application/Motion/`：复用唯一运动通道和未知状态锁定。
- `backend/src/Inspection.Infrastructure/`：合成定位与设备适配、诊断/必要持久化的实现；实际 SDK 另增量接入。
- `backend/src/Inspection.Host/`：组合根、受限工程测试入口、状态查询与诊断输出。
- `backend/tests/`、`tests/integration/`：状态、故障和 Host/独立进程链路验证；确切目录可随现有工程选择，但测试必须真正执行。
- `specs/005-initial-3d-station/`：本阶段规格、合同、矩阵、验证和独立证据。

## Implementation Sequence

先锁定契约和失败矩阵，再补工作流移位状态与事件关联；随后接合成端口和 Host 工程入口，最后按正常/异常分别跑通并记录双边轨迹。若保存边界比首工位范围大，可把测试模式与生产恢复能力明确分级，但不得开放未具备恢复的生产入口。生产点表/3D SDK 到位后另建对接任务与证据，不修改本阶段合成通过的定义。

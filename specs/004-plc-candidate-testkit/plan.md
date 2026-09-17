# Implementation Plan: 虚拟下位机候选版本测试包

**Branch**: `004-plc-candidate-testkit` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)

## Summary

沿用 002 的 .NET 10 双进程测试入口，为 003 新增的测试程序集修正动态门禁；加入显式旧协议候选 DLL 模式，使下位机同学可用自己发布的独立模拟器运行同一组真实 Modbus 联调，保留来源和失败证据。正式 V1.3 协议测试不借旧档案冒充。

## Technical Context

**Language/Version**: Python 3.12+；.NET SDK 10.0.401 / net10.0
**Primary Dependencies**: Python 标准库；现有 ASP.NET Core Host、独立 VirtualPlc
**Storage**: 每次运行唯一的 `simulator/artifacts/<run-id>/`
**Testing**: 锁定还原、Release build/test、旧13检查、两进程5场景、错误输入负向检查
**Target Platform**: Linux 本机；CI Ubuntu 24.04 / Windows 2022
**Project Type**: 开发和联调 CLI 测试工具
**Constraints**: 只启动回环软件模拟器；旧协议须显式选择；不写生产 PLC；不得以 NOT RUN 判为 PASS

## Constitution Check

- V1.3 基准：旧兼容测试与正式契约分列；通过。
- 唯一业务状态所有者：Host 探针不调用模拟器业务决策；通过。
- 未知动作无盲重试：保持已有 Host 探针与独立动作计数门禁；通过。
- 分层证据：单元、旧协议、跨进程、V1.3、整盘分开；通过。

## Project Structure

```text
scripts/validate-virtual-plc.py           # 默认基线及候选 DLL 模式
specs/004-plc-candidate-testkit/           # 规格、合同、指南、任务和验证
docs/contracts/virtual-plc-alignment.md   # 已有未决接口列表；不填假定点位
```

## Design

从 `Gaode.slnx` 找当前正式测试项目清单；逐份 TRX 校验真实通过及其程序集身份，拒绝缺失、重复、失败和跳过。候选模式要求 `--candidate-dll` 与精确 `--contract`，验证候选 `runtimeconfig.json` 的 net10.0 目标并记录 SHA-256。脚本继续构建本仓库生产目标，候选二进制本身的构建来源由下位机同学提供，不把本仓库 build 记为候选 build。启动候选时隔离动态端口和进程、同样调用 Host 探针和旧驱动，保留全部请求响应、状态、设备日志。

## Research and Alternatives

见 [research.md](research.md)。曾考虑让脚本连接任意外部 PLC 地址；这会把旧协议主动动作发到未知设备，且不能隔离状态，所以本阶段只接本机 DLL。曾考虑把测试程序集数改为4；后续新增测试项目会再次坏，改为按解决方案和 TRX 对账。

## Design Artifacts

- [数据模型](data-model.md)
- [候选测试合同](contracts/candidate-run.md)
- [运行指南](quickstart.md)

设计复核：所有候选动作仅对脚本启动的回环模拟器发出；真实设备和未确认 V1.3 点表不进入运行路径。

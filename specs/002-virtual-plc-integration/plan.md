# Implementation Plan: 虚拟下位机接入与 V1.3 对齐

**Branch**: `002-virtual-plc-integration` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)

## Summary
复用校验后的模拟器，固定 .NET 10 构建；增加 Infrastructure Modbus TCP 客户端和显式 Host 工程探针，用独立设备进程验证兼容握手。只支持观测到的 legacy-v6-u16，不自定V1.3新点表。

## Technical Context
- Language/Version: C# / .NET SDK 10.0.401；原13组测试驱动 net10.0 + C#12 保持原断言。
- Primary Dependencies: .NET BCL TcpClient；沿用现有 xUnit，不新增通信包。
- Storage: JSON/原始报文和进程日志；本轮无业务数据库。
- Testing: xUnit独立报文测试 + Python3启动隔离发布进程 + 原13组检查。
- Target Platform: Linux x64本机；CI Ubuntu24.04/Windows2022。Windows结果仅按实际CI记录。
- Project Type: 单体Host + 独立模拟服务。
- Performance Goals: 每次I/O和整场景有界；真实墙钟测试，不宣称生产节拍。
- Constraints: 一个连接串行请求；动作等待间隔执行心跳；不自动重连/重发；显式本地模拟工程模式。
- Scope: 两个连续移动和异常处理；不翻面/分拣、不输出质量。

## Constitution Check
设计前后均通过：Domain不依赖通信；唯一Host组合根；设备状态由独立模拟器持有；失败/Unknown区分；不接业务随机兜底；不声称持久恢复/生产安全。完整Motion调度、停止优先级、持久化与V1.3新契约留下一增量。

## Project Structure
- simulator/VirtualPlc/：原设备源码、文档、规格、历史报告与原验证入口
- simulator/source-manifest.json：校验来源及引入说明
- backend/src/Inspection.Infrastructure/Plc/：真实Modbus客户端、兼容动作适配
- backend/src/Inspection.Host/：仅显式 --plc-probe 模式执行工程探针
- backend/tests/Inspection.Infrastructure.Tests/：独立已知报文/异常测试
- scripts/validate-virtual-plc.py：生产构建、旧检查、双进程测试、清理和证据
- .github/workflows/ci.yml：新增两平台作业
- specs/002-virtual-plc-integration/：规格、研究、模型、契约、任务、验证

## Complexity Tracking
无章程豁免。工程探针不注册通用生产运动API，正常Host保持计划预览阶段。

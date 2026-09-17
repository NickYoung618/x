# Implementation Plan: PLC 接口按 9 月 11 日协议对齐

**Branch**: `plc-protocol-20260911-alignment` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md)

## Summary

从 `pj/HousingInspection` 评审统一设备接口，将旧 V6 适配的隐含单字坐标、角度翻面和十进制点号拆离；新端口按 9 月 11 日文档提供双字坐标、三 Z、高度和面号/报警快照，并只对独立虚拟服务开放显式动作。Host 的新协议工程检查默认只读；原 V6 探针保留。流程责任与顺序见 11 图及 V1.3。

## Technical Context

**Language/Version**: .NET 10 / C#
**Primary Dependencies**: 现有串行 Modbus TCP 客户端；无新增生产 NuGet 包
**Storage**: 无
**Testing**: xUnit；独立 TCP 测试服务；锁定还原、Release 构建、全解决方案测试及旧 V6 回归
**Target Platform**: Linux 开发/CI；Windows 现场待验
**Project Type**: 单体 Host + Application 端口 + Infrastructure 适配
**Constraints**: 真实动作禁用；协议未定义动作不能猜测；未知不重发；50 ms 轮询与 2 s 动作期限只作为虚拟测试目标
**Scale/Scope**: 接口框架和最小独立 Modbus 报文闭环，不覆盖整盘和真机

## Constitution Check

- V1.3 与图优先：PASS。流程图顺序和重扫要求写入合同，旧 V6 留历史回归。
- 单体分层/唯一业务状态：PASS。Application 只定义端口，Infrastructure 编解码，Host 仅工程只读组合；设备适配不作配方决策。
- Accepted/Completed/Unknown：PASS，设备运行状态和实际位置分开观察，未知禁重发。
- 独立设备/分层证据：PASS，测试服务独立持有寄存器状态；真机与整盘标未运行。
- 关键持久化：此特性不启动业务任务，后续每站事务持久化仍是独立门槛。

## Project Structure

```text
backend/src/Inspection.Application/Plc/IPlcDevice.cs
backend/src/Inspection.Infrastructure/Plc/{ModbusTcpClient,PlcDeviceFactory,Protocol20260911Map,Protocol20260911PlcDevice,LegacyPlcProbe}.cs
backend/src/Inspection.Host/Program.cs
backend/tests/Inspection.Infrastructure.Tests/Protocol20260911Tests.cs
docs/contracts/{source/PLC与上位机通信接口协议-20260911.docx,plc-upper-20260911.md}
specs/009-plc-protocol-alignment/
```

## Design gates

真实地址含字母，所以测试口径解析为十六进制一基点号；厂商真实口径仍需确认。Float32 的四种常见排列有编解码，但必须显式配置。真实配置拒绝动作。新版模拟器与独立完整工位链未交付，不在本 PR 宣称其通过。

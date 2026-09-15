# Implementation Plan: 开发基础与普通整盘采集计划预览

**Branch**: `main` | **Date**: 2026-09-15 | **Spec**: [spec.md](spec.md)

**Input**: `specs/001-engineering-foundation/spec.md`

## Summary

建立 Domain → Application → Host 的可编译基础，提供只读工程状态和测试采集计划。
Domain 实现整盘相机批采顺序与翻面/重扫屏障；Application 提供样例查询；Host 负责 HTTP 及配置。
初始 CI 在 GitHub 托管 Linux、Windows runner 上执行相同核心测试，并分别发布可启动的 Host 文件。
虚拟设备契约先输出评审草案；不写假寄存器、不重复建设同学负责的模拟器。

## Technical Context

**Language/Version**: C# / .NET 10，SDK 10.0.401，global.json 禁止自动跨版本漂移。

**Primary Dependencies**: ASP.NET Core 10.0.12 随 SDK；测试使用 xUnit、Test SDK、MVC Testing，版本和锁文件入库。

**Storage**: 本增量无业务写入；SQLite/EF Core 与 Database.Deployment.Tool 为后续闭环必需项。

**Testing**: Domain 顺序及无效输入测试；Host TestServer 验证 HTTP 契约；CI 发布后真实进程存活检查。

**Target Platform**: 核心 Linux x64 / Windows x64；WPF 后续单独在 Windows 验证。

**Project Type**: 模块化单体后端及纯规则库。

**Performance Goals**: 本次验证可运行性和规则正确性，不承诺真实节拍与检出率。

**Constraints**: 默认回环地址；只读接口；生产就绪恒为 false；无设备/算法/数据库连接；无凭据需求。

**Scale/Scope**: 三名开发者，一个托盘测试计划。2 件×2 面×2 相机是可替换配置。

## Constitution Check

研究前与设计后检查均通过：

| 原则 | 本增量落点 |
| --- | --- |
| V1.3 基准 | 原文快照与 SHA256；用户确认记录；不继承历史 Qt/自动对焦 |
| 分层与唯一状态 | Domain 纯 BCL；Application 仅引用 Domain；Host 为唯一 HTTP 入口 |
| 机械和身份 | 仅生成计划；翻面/重扫为屏障；不伪造坐标、运动或结果 |
| 有界与终态 | 本次无异步执行队列；后续验收矩阵明确相应要求 |
| 保存和维护 | 本次无业务库；完整闭环必须另实现持久化与独立维护工具 |
| 测试证据 | 分开记录本地、CI、协议、UI、真机结果；未执行不记通过 |

## Project Structure

### Documentation (this feature)

`specs/001-engineering-foundation/` 包含 spec、plan、research、data-model、contracts/http.md、
quickstart、checklists/requirements、tasks 与 validation；仓库 docs 保存架构、分工、协议草案、后续闭环及测试矩阵。

### Source Code (repository root)

```text
backend/src/Inspection.Domain/             # 普通整盘计划与输入约束
backend/src/Inspection.Application/        # 只读工程样例查询
backend/src/Inspection.Host/               # HTTP、配置与装配
backend/tests/Inspection.Domain.Tests/
backend/tests/Inspection.Host.Tests/
.github/workflows/ci.yml
scripts/smoke-host.ps1
```

**Structure Decision**: 先建立有实际内容的三个工程。Infrastructure、算法 Worker、桌面与数据库部署工具
随具体特性加入；不为了凑 15 模块而生成 15 个空工程。完整目标结构见 V1.3 第 17 节。

## Delivery Phases

1. 固定基准、原则、SDK 和项目引用方向。
2. 先写顺序及边界测试，实现不可变计划；Host 契约测试后实现只读接口。
3. 编写独立虚拟设备契约草案与包含数据库维护的测试矩阵。
4. 本地验证，提交基线并触发 GitHub 两平台 CI，记录实际结果。

## Complexity Tracking

无需要豁免的架构规则。真实 PLC 协议、桌面、算法及数据库部署都列为明确后续任务。

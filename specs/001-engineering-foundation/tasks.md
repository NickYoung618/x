# Tasks: 开发基础与普通整盘采集计划预览

**Input**: 同目录 spec、plan、research、data-model、contracts、quickstart。

**Tests**: 用户明确要求测试与 CI；计划规则和 HTTP 契约先有失败检查，再实现。

## Phase 1: Setup

- [x] T001 保存架构原文、SHA256 和确认记录至 docs/architecture/ 与 docs/decisions.md（FR-001）。
- [x] T002 完成 .specify/memory/constitution.md 及本特性的规格、计划、设计与工具版本记录（FR-001）。

## Phase 2: Foundational

- [x] T003 固定 global.json、Directory.Build.props、NuGet.config 与 .gitignore（FR-002）。
- [x] T004 建立 Gaode.slnx、backend/src 三个工程和 backend/tests 两个工程，保持引用方向（FR-002）。

## Phase 3: US1 共同工程基线

独立验证：无设备启动 Host，存活可查询、生产就绪为否。

- [x] T005 [US1] 先写 backend/tests/Inspection.Host.Tests/HostContractTests.cs 的存活和状态契约，确认失败（FR-003）。
- [x] T006 [US1] 实现 backend/src/Inspection.Host/Program.cs 的只读状态入口与默认回环绑定（FR-003）。
- [x] T007 [US1] 在 .github/workflows/ci.yml、scripts/smoke-host.ps1 配置两平台构建测试、发布与进程检查（FR-002；SC-001）。

## Phase 4: US2 普通整盘采集计划

独立验证：2×2×2 样例顺序、可替换 C/D、翻面重扫、输入约束和快照不可变。

- [x] T008 [US2] 先写 backend/tests/Inspection.Domain.Tests/OrdinaryTrayPlannerTests.cs；覆盖“非空、无首尾空白、不可重复”标识、“只能为 AB 或 CD”相机组、列表至少一项与快照隔离（FR-004/005/006；SC-002/003）。
- [x] T009 [US2] 实现 backend/src/Inspection.Domain/Planning/OrdinaryTrayPlanner.cs 的规则及不可变模型（FR-004/005/006）。
- [x] T010 [US2] 在 backend/src/Inspection.Application/Engineering/、Host/appsettings.json、Host/Program.cs 实现可配置测试计划查询并扩展 HostContractTests.cs（FR-007）。

## Phase 5: US3 对接准备

独立验证：逐条对照 V1.3，协议状态和仿真/真实测试边界明确。

- [x] T011 [US3] 编写 docs/contracts/virtual-device-v0.1-draft.md，明确整体动作、序号、超时、重连及故障注入（FR-008）。
- [x] T012 [US3] 编写 docs/testing.md 和 docs/ordinary-tray-roadmap.md，包含数据库部署与多面全虚拟闭环验收（FR-008；SC-004）。

## Phase 6: Validation and Delivery

- [x] T013 按 quickstart 本地执行锁定还原、构建、测试与真实进程存活检查，结果写入 specs/001-engineering-foundation/validation.md（FR-002；SC-001/002/003）。
- [x] T014 在 README.md 说明运行、职责和下一步；提交并推送已检查基线，核对 GitHub 两平台结果与测试产物，更新 validation.md（FR-001/002；SC-001/004）。

## User Scope Update 2026-09-15

用户在实现中指定新的界面原型并要求真实 Windows 流程测试方案；新增以下任务，不追改原验收标准。

- [x] T015 检查原型源码与浏览器流程，保存 docs/ui-prototype/review.md、清单、截图及结果，明确与 V1.3 的差异。
- [x] T016 编写 docs/windows-acceptance.md 和 .github/workflows/windows-staging.yml；区分当前 Host 部署冒烟与后续 WPF 全流程验收。

## Dependencies & Execution Order

T001–T004 是共同基础；US1 按 T005→T006→T007，US2 按 T008→T009→T010；
T010 的 HTTP 接入依赖 T006。T011/T012 在基础后可与代码开发独立进行。T013/T014 最后执行。
并行机会只表示不同开发者可独立工作，本轮由同一助手按依赖推进，不额外派生代理。

## Implementation Strategy

本特性的最小交付是 US1；本轮计划完成 US1–US3。每个故事单独检查，最终仅报告实测范围。
下一个特性负责完整普通整盘全虚拟执行闭环，不把本特性完成当作整个项目完成。

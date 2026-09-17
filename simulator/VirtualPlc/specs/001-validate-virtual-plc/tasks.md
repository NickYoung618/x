# Tasks: VirtualPlc 全量测试验证

**Input**: Design documents from `specs/001-validate-virtual-plc/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: 本功能本身是测试交付，所有用户故事均由可执行黑盒检查实现。

**Organization**: 任务按用户故事分组；US1～US3 为 P1，US4 汇总并交付证据。

## Phase 1: Setup

- [X] T001 创建离线系统验证项目 `VirtualPlc/tests/VirtualPlc.SystemValidation/VirtualPlc.SystemValidation.csproj`
- [X] T002 创建链接生产源码的兼容主机 `VirtualPlc/tests/VirtualPlc.SystemValidation/VirtualPlc.CompatibilityHost.csproj`
- [X] T003 [P] 核对 `VirtualPlc/.gitignore` 已排除 bin、obj 和 test-results 生成物

---

## Phase 2: Foundational Infrastructure

- [X] T004 在 `VirtualPlc/tests/VirtualPlc.SystemValidation/Program.cs` 实现检查注册、需求映射、计时、容错继续和 JSON 报告模型
- [X] T005 在 `VirtualPlc/tests/VirtualPlc.SystemValidation/Program.cs` 实现原始 Modbus TCP 客户端、事务校验和异常码观察
- [X] T006 在 `VirtualPlc/tests/VirtualPlc.SystemValidation/Program.cs` 实现 HTTP 辅助、状态轮询、复位、心跳应答和动作触发辅助

**Checkpoint**: 验证器可连接外部启动的 VirtualPlc 并记录结构化结果。

---

## Phase 3: User Story 1 - 验证公开接口和点表 (Priority: P1)

**Goal**: 证明点表、HTTP 公开面和全部 Modbus 功能码/异常响应正确。

**Independent Test**: 启动服务后仅运行接口与协议检查，不依赖动作场景。

- [X] T007 [US1] 实现 CSV、address-map、state 三方 29 点一致性检查（FR-001）
- [X] T008 [P] [US1] 实现 health、dashboard、JS/CSS 和 HTTP 非法参数检查（FR-004）
- [X] T009 [US1] 实现 FC01/03/05/06/15/16 成功路径和边界值检查（FR-002）
- [X] T010 [US1] 实现非法功能、地址、数据、Unit Id、方向和多写原子性检查（FR-003）

**Checkpoint**: US1 可独立给出公开接口和协议 PASS/FAIL。

---

## Phase 4: User Story 2 - 验证完整动作流程和互锁 (Priority: P1)

**Goal**: 证明正常流程、命令握手和安全互锁按文档执行。

**Independent Test**: 每个场景从 reset 开始并维持有效心跳。

- [X] T011 [US2] 实现心跳、系统就绪、区域配置、托盘锁紧/解锁检查（FR-005）
- [X] T012 [US2] 实现移动 1～5、坐标边界、重复命令和 Retry_Cmd 检查（FR-006）
- [X] T013 [US2] 实现 90/180 度翻转、正常分拣、满盘命令和槽位必填检查（FR-006）
- [X] T014 [US2] 实现未就绪、未配置、软停、非法命令和动作互斥检查（FR-007）

**Checkpoint**: US2 可独立完成正常动作和互锁验证。

---

## Phase 5: User Story 3 - 验证故障、兜底和恢复 (Priority: P1)

**Goal**: 覆盖 9 种故障、11 类流程决策和 reset 恢复。

**Independent Test**: 每个故障场景先 reset，动作故障再执行第二次证明一次性消费。

- [X] T015 [US3] 实现 MoveTimeout、FlipFailure、FlipAngleMismatch、SortingFailure、FullPallet、PalletLockFailure 检查（FR-008）
- [X] T016 [US3] 实现 EmergencyAlarm、ManualZoneOccupied、PauseHeartbeat/通信超时与恢复检查（FR-008）
- [X] T017 [P] [US3] 实现 11 类 flow-decision 成功/失败策略检查（FR-009）
- [X] T018 [US3] 实现 reset 清除故障、PC 值和动作状态的检查（FR-010）

**Checkpoint**: US3 对每个公开故障和流程分类都有确定性证据。

---

## Phase 6: User Story 4 - 可复跑验证证据 (Priority: P2)

**Goal**: 一条命令完成构建、运行、报告和清理。

**Independent Test**: 连续执行脚本两次并核对退出码及 latest.json/latest.md。

- [X] T019 [US4] 创建 `VirtualPlc/scripts/validate.sh`，实现 SDK 检测、离线构建、空闲端口、主机生命周期和退出码（FR-011, FR-014）
- [X] T020 [US4] 在脚本中生成 Markdown 报告并保留 JSON/host.log，明确 PASS/FAIL/NOT RUN/规范限制（FR-012, FR-013）
- [X] T021 [US4] 更新 `VirtualPlc/README.md`，增加全量验证命令、证据路径和 .NET 10 门禁说明（FR-011, FR-013）

---

## Phase 7: Polish & Cross-Cutting Validation

- [X] T022 运行 Spec Kit 前置检查并核对 FR-001～FR-014 任务覆盖
- [X] T023 执行 `bash VirtualPlc/scripts/validate.sh`，修复全部自动检查失败
- [X] T024 连续第二次执行验证，确认无残留进程/端口并满足 60 秒目标（SC-004）
- [X] T025 核对所有新增、构建和报告文件均在 `/home/ubuntu/disk/pj1`（SC-006）
- [X] T026 完成 `specs/001-validate-virtual-plc/validation-report.md` 并将所有已完成任务标记为 `[X]`

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 → US1/US2/US3 → US4 → Phase 7。
- US1、US2、US3 共享一个有状态 Modbus 连接，代码内顺序执行；每个场景通过 reset 隔离。
- T008 和 T017 只使用 HTTP，可与同阶段设计工作并行，但最终 runner 仍串行输出确定性报告。
- T019 依赖 T001～T018；T023 依赖全部实现任务；T026 依赖所有验证完成。

## Requirement Coverage

| Requirement | Tasks |
|---|---|
| FR-001 | T007 |
| FR-002 | T009 |
| FR-003 | T010 |
| FR-004 | T008 |
| FR-005 | T011 |
| FR-006 | T012, T013 |
| FR-007 | T014 |
| FR-008 | T015, T016 |
| FR-009 | T017 |
| FR-010 | T018 |
| FR-011 | T019, T021 |
| FR-012 | T020 |
| FR-013 | T020, T021 |
| FR-014 | T019 |

## Implementation Strategy

先完成可启动、可连接、可记录失败的最小 runner；再按 US1→US2→US3 增加检查。每个场景
复位服务状态并使用轮询截止时间。最后由 shell 脚本编排全生命周期和证据生成，连续运行
两次作为幂等验收。

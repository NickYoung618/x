# Tasks: PLC 接口按 9 月 11 日协议对齐

## Phase 1: Setup

- [x] T001 在 `docs/contracts/source/PLC与上位机通信接口协议-20260911.docx` 保存校验后的接口原件，并记录来源哈希。
- [x] T002 在 `specs/009-plc-protocol-alignment/` 建规格、计划、研究和合同。

## Phase 2: Foundation

- [x] T003 在 `backend/src/Inspection.Application/Plc/IPlcDevice.cs` 定义不混用角度与面号的新语义端口。
- [x] T004 在 `backend/src/Inspection.Infrastructure/Plc/Protocol20260911Map.cs` 实现十六进制一基映射及显式 Float32 排列。

## Phase 3: User Story 1 - 准确读取新协议

- [x] T005 [US1] 在 `backend/src/Inspection.Infrastructure/Plc/Protocol20260911PlcDevice.cs` 实现完整安全/位置/报警只读快照。
- [x] T006 [US1] 在 `backend/tests/Inspection.Infrastructure.Tests/Protocol20260911Tests.cs` 以独立 TCP 服务检验新点位与双字值。

## Phase 4: User Story 2 - 虚拟动作闭环

- [x] T007 [US2] 在 `backend/src/Inspection.Infrastructure/Plc/Protocol20260911PlcDevice.cs` 实现显式虚拟模式的准备、夹紧、区域配置、移动/翻面受理与完成。
- [x] T008 [US2] 在 `backend/tests/Inspection.Infrastructure.Tests/Protocol20260911Tests.cs` 检验写入顺序、本轮运行、物理完成和重复 ID 锁定。

## Phase 5: User Story 3 - 未定义动作阻断

- [x] T009 [US3] 在 `backend/src/Inspection.Infrastructure/Plc/PlcDeviceFactory.cs` 使新旧合同互斥、真机写入拒绝、未配字序拒绝。
- [x] T010 [US3] 在 `backend/src/Inspection.Host/Program.cs` 把新协议工程入口限制为只读，并保留旧 `--plc-probe`。
- [x] T011 [US3] 在 `backend/src/Inspection.Infrastructure/Plc/Protocol20260911PlcDevice.cs` 阻断缺完整取放参数的分拣，并禁止未知动作自动重发。

## Phase 6: Verification and handoff

- [x] T012 在 `docs/contracts/plc-upper-20260911.md` 和 `docs/contracts/virtual-plc-alignment.md` 完成点表/时序差异、签认清单和测试分层。
- [x] T013 在 `specs/009-plc-protocol-alignment/validation.md` 记录锁定还原、全解构建/测试、旧 V6 回归、新协议虚拟/真机/整盘结果。
- [x] T014 在 `docs/update-goals.md` 登记本次实际结果与后续范围，提交并推送独立分支。

依赖：T001–T004 → T005–T011 → T012–T014。US1 可独立只读验收；US2 仅隔离虚拟服务可验；US3 的阻断可无需设备验。下位机正式模拟器和真机签认属于下一增量，不将其任务假装勾选。

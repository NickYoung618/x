# Tasks: 虚拟下位机接入与 V1.3 对齐（第一增量）

Input: spec.md、plan.md、research.md、data-model.md、contracts/legacy-v6.md。

## Phase 1: Setup
- [x] T001 登记唯一主要目标、证据与失败条件 docs/update-goals.md
- [x] T002 创建独立分支及规格 specs/002-virtual-plc-integration/spec.md

## Phase 2: Foundation
- [x] T003 核对协议并记录双方后续决定 docs/contracts/virtual-plc-alignment.md

## Phase 3: US1 可复现组件
独立验收：生产net10构建实际成功且旧13组执行。
- [x] T004 [US1] 引入来源校验快照及保留原规格 simulator/VirtualPlc/ 与 simulator/source-manifest.json
- [x] T005 [US1] 将原驱动固定net10/C#12并保留原断言 simulator/VirtualPlc/tests/VirtualPlc.SystemValidation/VirtualPlc.SystemValidation.csproj
- [x] T006 [US1] 实现无兼容回退的构建/运行入口 scripts/validate-virtual-plc.py

## Phase 4: US2 中台实际联调
独立验收：两个发布进程、不同目标连续动作、旧反馈拒绝、失败/超时不继续。
- [x] T007 [US2] 增加独立已知报文及错误/超时测试 backend/tests/Inspection.Infrastructure.Tests/ModbusTcpClientTests.cs
- [x] T008 [US2] 实现串行且有界Modbus客户端 backend/src/Inspection.Infrastructure/Plc/ModbusTcpClient.cs
- [x] T009 [US2] 实现ushort(0..65535)兼容握手及新Busy条件 backend/src/Inspection.Infrastructure/Plc/LegacyPlcProbe.cs
- [x] T010 [US2] 添加显式工程模式与默认无运动验证 backend/src/Inspection.Host/Program.cs
- [x] T011 [US2] 加入双进程场景与设备日志/状态核对 scripts/validate-virtual-plc.py

## Phase 5: US3 评审证据
独立验收：CI真实执行并分列三类结果。
- [x] T012 [P] [US3] 接入Linux/Windows作业 .github/workflows/ci.yml
- [x] T013 [P] [US3] 更新使用与测试范围 README.md、docs/testing.md
- [x] T014 [US3] 执行实际验证并记录全部尝试 specs/002-virtual-plc-integration/validation.md

## Phase 6: Finish
- [x] T015 复核差异与任务映射，登记未决与下一增量 docs/update-goals.md

## Dependencies and strategy
T001→T002→T003→US1→US2→US3→T015。MVP为US1；本次授权涵盖US1—3全部。
US1内先引入再构建；US2先报文测试、通信、动作、Host，再联调。
T012与T013文件独立可并行；其他按依赖顺序。不需等待后续生产协议确认来完成本次兼容范围。

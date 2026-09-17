# Tasks: 普通整盘中台流程框架

Input: spec.md、plan.md、research.md、data-model.md、contracts/application-events.md。

## Phase 1: Setup
- [x] T001 建立分支、台账与Spec Kit规格 specs/003-ordinary-workflow-foundation/spec.md、docs/update-goals.md
- [x] T002 完成计划、研究、数据模型和接口合同 specs/003-ordinary-workflow-foundation/plan.md

## Phase 2: Foundational
- [x] T003 建立事件/效果/端口/状态类型 backend/src/Inspection.Application/Workflow/WorkflowContracts.cs

## Phase 3: US1 公共准备
独立测试：初始3D→F扫码→唯一方案；失败不运动。
- [x] T004 [US1] 实现公共准备和冻结身份/方案 backend/src/Inspection.Application/Workflow/TrayWorkflow.cs
- [x] T005 [US1] 用独立准备失败反例验证 backend/tests/Inspection.Application.Tests/TrayWorkflowTests.cs

## Phase 4: US2 顺序与运动
独立测试：2件2面AB八采集、两次翻面、重扫及旧反馈阻断。
- [x] T006 [US2] 完成分面/翻面/坐标批次迁移 backend/src/Inspection.Application/Workflow/TrayWorkflow.cs
- [x] T007 [US2] 实现单设备有界执行通道 backend/src/Inspection.Application/Motion/MotionExecutionLane.cs
- [x] T008 [US2] 测试顺序、单动作及Unknown锁定 backend/tests/Inspection.Application.Tests/TrayWorkflowTests.cs、MotionExecutionLaneTests.cs

## Phase 5: US3 技术终态
独立测试：普通缺帧继续；算法乱序、迟到、截止收敛。
- [x] T009 [US3] 实现采集/算法关联和待判定状态 backend/src/Inspection.Application/Workflow/TrayWorkflow.cs
- [x] T010 [US3] 添加混合成功与失败检查 backend/tests/Inspection.Application.Tests/TrayWorkflowTests.cs

## Phase 6: Validation
- [x] T011 将新测试工程加入解决方案并保留锁定还原 Gaode.slnx、backend/tests/Inspection.Application.Tests/packages.lock.json
- [x] T012 实测构建/测试并记录证据与限制 specs/003-ordinary-workflow-foundation/validation.md、docs/testing.md
- [x] T013 对照规格核查任务/实际范围，结项台账 docs/update-goals.md

依赖：T003→T004→T005→T006→T007→T008→T009→T010→T011→T012→T013。T004/6/9同文件顺序实现；测试只验证行为，不把假端口通过冒称协议或整盘实测。

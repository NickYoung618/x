# Tasks: 虚拟下位机候选版本测试包

**Input**: [spec.md](spec.md)、[plan.md](plan.md)、[合同](contracts/candidate-run.md)。

## Phase 1: Setup

- [x] T001 建立 004 分支、规格与开工台账 `specs/004-plc-candidate-testkit/spec.md`、`docs/update-goals.md`
- [x] T002 编写设计、候选合同和可运行指南 `specs/004-plc-candidate-testkit/plan.md`、`contracts/candidate-run.md`、`quickstart.md`

## Phase 2: Foundational

- [x] T003 核对现有三种联调输出和四个测试项目 `Gaode.slnx`、`scripts/validate-virtual-plc.py`

## Phase 3: US1 动态测试门禁

独立验收：当前解决方案所有测试项目各有一份真实通过的TRX，旧检查及双进程仍通过。

- [x] T004 [US1] 从解决方案枚举测试程序集并核对TRX中的身份/数量/跳过，同时验证负向门禁 `scripts/validate-virtual-plc.py`、`scripts/tests/test_validate_virtual_plc.py`、`.github/workflows/ci.yml`
- [x] T005 [US1] 运行默认入口并保存构建、测试、旧协议及联调结果 `specs/004-plc-candidate-testkit/validation.md`

## Phase 4: US2 候选模拟器

独立验收：另行发布的当前模拟器以候选模式运行，候选身份可核实且五场景覆盖；错误输入动作前拒绝。

- [x] T006 [US2] 增加显式协议候选DLL、目标框架与哈希门禁 `scripts/validate-virtual-plc.py`
- [x] T007 [US2] 确保所有场景实际启动候选DLL并保留来源/设备证据 `scripts/validate-virtual-plc.py`
- [x] T008 [US2] 运行候选正向及错误输入负向，记录所有尝试 `specs/004-plc-candidate-testkit/validation.md`

## Phase 5: Delivery

- [x] T009 更新同学使用入口和测试分层 `simulator/README.md`、`README.md`、`docs/testing.md`
- [x] T010 复核规格、差异及台账，完成最终检查 `specs/004-plc-candidate-testkit/validation.md`、`docs/update-goals.md`

依赖：T003→T004→T005；T006→T007→T008；T005与T008后才做T010。T004与T006都改同一脚本，顺序执行。

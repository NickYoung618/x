# Tasks: 首工位——初始 3D 定位

**Input**: [spec.md](spec.md)、[plan.md](plan.md)、[research.md](research.md)、[data-model.md](data-model.md)、[工位合同](contracts/station-boundary.md)、[测试矩阵](test-matrix.md)。`[x]` 是开工准备已完成；`[ ]` 是首工位实现 PR 必须交付，不表示已测试。

## Phase 1: 开工准备

- [x] T001 从合并后的 main 建独立 `station-01-initial-3d` 分支并登记目标 `docs/update-goals.md`
- [x] T002 对照 V1.3、现有 Workflow/Host/Motion 与旧协议证据，形成规格、合同、计划和测试矩阵 `specs/005-initial-3d-station/`
- [x] T003 确定首工位终点 WaitingTrayCode，列清前端/下位机待填接口及分层验收

## Phase 2: 基础合同与状态（实现开始）

- [ ] T004 扩展 `backend/src/Inspection.Application/Workflow/WorkflowContracts.cs` 的 MoveTo3D、ReadyObservation、定位来源/关联与稳定错误状态；保持现有调用者编译
- [ ] T005 在 `TrayWorkflow.cs` 实现 WaitingReady → MovingTo3D → Scanning3D → WaitingTrayCode，仅匹配受理+完成后定位；旧反馈和超时不推进
- [ ] T006 在 `backend/tests/Inspection.Application.Tests/` 固定 N01、P01–P02、M01–M04b、S01–S04b 的状态断言，先观察失败再完成实现

## Phase 3: 正常合成工位链（US1/US2）

- [ ] T007 复用 `MotionExecutionLane`；增加可控的独立合成运动设备和 3D 端口，提供双槽样例、来源元数据及故障注入；不得调用业务状态机生成设备答案
- [ ] T008 在 `backend/src/Inspection.Host/` 组合工位运行入口与查询快照，测试模式显式启用且只允许回环访问；生产模式不绑定合成到位
- [ ] T009 增加 Host/独立适配器联调，核对两侧轨迹及 1 次移位、1 次扫描、2 件、Epoch=1、等待 F；测试不能只向 TrayWorkflow 直接塞完成事件

## Phase 4: 故障、诊断与恢复边界（US3）

- [ ] T010 实现稳定错误码、中文说明、结构化日志及 RunId/OperationId/RequestId/来源/时间关联；补 O01 诊断/关键保存失败行为
- [ ] T011 实现或明确限制动作意图/反馈保存与重启对账边界；未知动作禁止自动补发，关闭不具备恢复保证的生产入口
- [ ] T012 将矩阵中所有反例接入 Host/设备替身测试，特别核查断线、迟到、错 ID、重复和故障后的零后续动作

## Phase 5: 验证与交接

- [ ] T013 运行锁定还原、Linux/Windows CI 构建及核心测试、旧 V6 回归和首工位合成联调；证据按层分别记录 `specs/005-initial-3d-station/validation.md`
- [ ] T014 将状态/错误样例交前端同学，动作/反馈与独立轨迹要求交下位机同学；接口确认后另测 V1.3，勿将未确认项打勾
- [ ] T015 检查规格覆盖、台账结项、PR 审查与合并；合并后 main 回归，再安排第二工位 F 读码

依赖：T004→T005/T006→T007/T008→T009→T010–T012→T013–T015。首工位 PR 未实现前保持 Draft；旧 V6、V1.3、真实 SDK、完整整盘各自独立报告。

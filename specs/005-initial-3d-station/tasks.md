# Tasks: 首工位——初始 3D 定位

**Input**: [spec.md](spec.md)、[plan.md](plan.md)、[research.md](research.md)、[data-model.md](data-model.md)、[工位合同](contracts/station-boundary.md)、[测试矩阵](test-matrix.md)。`[x]` 是开工准备已完成；`[ ]` 是首工位实现 PR 必须交付，不表示已测试。

## Phase 1: 开工准备

- [x] T001 从合并后的 main 建独立 `station-01-initial-3d` 分支并登记目标 `docs/update-goals.md`
- [x] T002 对照 V1.3、现有 Workflow/Host/Motion 与旧协议证据，形成规格、合同、计划和测试矩阵 `specs/005-initial-3d-station/`
- [x] T003 确定首工位终点 WaitingTrayCode，列清前端/下位机待填接口及分层验收

## Phase 2: 基础合同与状态（实现开始）

- [x] T004 扩展 `backend/src/Inspection.Application/Workflow/WorkflowContracts.cs` 的 MoveTo3D、ReadyObservation、定位来源/关联与稳定错误状态；保持现有调用者编译
- [x] T005 在 `TrayWorkflow.cs` 实现 WaitingReady → MovingTo3D → Scanning3D → WaitingTrayCode，仅匹配受理+完成后定位；旧反馈和超时不推进
- [x] T006 在 `backend/tests/Inspection.Application.Tests/` 固定 N01、P01–P02、M01–M04b、S01–S04b 的状态断言，先观察失败再完成实现

## Phase 3: 正常合成工位链（US1/US2）

- [ ] T007 复用 `MotionExecutionLane`；下位机同学按全工位合同交付独立 PLC 与 3D 相机接口模拟进程、可读取固定合成帧、来源元数据和采集故障注入；中台交付采集客户端及从固定帧生成双槽的合成定位器；设备模拟不得直接生成槽位或调用业务状态机；真实缺依赖时不得回退合成
- [x] T008 在 `backend/src/Inspection.Host/` 组合工位运行入口与查询快照，测试模式显式启用且只允许回环访问；生产模式不绑定合成到位
- [ ] T009 增加 Host/独立适配器联调，核对两侧轨迹及 1 次移位、1 次扫描、2 件、Epoch=1、等待 F；测试不能只向 TrayWorkflow 直接塞完成事件

## Phase 4: 故障、诊断与恢复边界（US3）

- [x] T010 实现稳定错误码、中文说明、结构化日志及 RunId/OperationId/RequestId/来源/时间关联；补 O01 诊断失败行为；生产关键保存仍按 T011 关闭
- [x] T011 明确限制动作意图/反馈保存与重启对账边界；未知动作禁止自动补发，未具备恢复保证的生产入口继续返回 501，工程入口仅内存防重复
- [ ] T012 将矩阵中所有反例接入 Host/设备替身测试，特别核查断线、迟到、错 ID、重复和故障后的零后续动作

## Phase 5: 验证与交接

- [ ] T013 运行锁定还原、Linux/Windows CI 构建及核心测试、旧 V6 回归和首工位合成联调；CI 验证非零用例/无 skipped、独立轨迹及同一 head SHA，并从 PR head 产出带 manifest/SHA-256 的 Windows 候选包；证据按层分别记录 `specs/005-initial-3d-station/validation.md`
- [ ] T014 将状态/错误样例交前端同学，动作/反馈与独立轨迹要求交下位机同学；接口确认后另测 V1.3，勿将未确认项打勾
- [ ] T015 检查规格覆盖、台账结项、实现 PR 审查与合并；合并后 main 对合并 SHA 回归，软件可合并与真机未验收分开结论

## Phase 6: 同包 Windows 验证与真机准入（不以软件 PR 合并自动打勾）

- [ ] T016 从成功的 main 合并 SHA 再生成 Windows S01 候选包，核对 manifest 与 SHA-256；修正 `windows-staging.yml` 的旧阶段校验并扩展 S01 合成冒烟，缺自有 runner 时提供人工执行入口与报告模板
- [ ] T017 在用户实际 Windows 电脑校验并运行同一候选包，用独立虚拟设备做 S01 正常、关键故障、重启/日志测试，保留软件、设备两侧轨迹；WPF 未接入则标 `NOT RUN`
- [ ] T018 与下位机负责人及设备供方/现场核对并记录点表、会话/动作关联、公共 3D 位、停止/对账、相机型号/SDK/坐标系/单位/标定及测试件预期；下位机同学另交独立 3D 模拟轨迹；SDK 单独到手仅做适配/离线回放合同测试；任何真机准入缺项阻止真实设备动作，不阻止已证实的软件结论
- [ ] T019 现场人员按 [Windows 真机规程](windows-real-device.md)执行 W0–W5；保存包、环境、PLC、3D、中台三方证据，未获批准的物理故障记 `NOT RUN` 而非 PASS
- [ ] T020 汇总首次失败、归因、修复 PR 与同包复测；分别给出软件、Windows 虚拟设备、V1.3 真机、WPF、整盘结论，真实设备通过后才更新现场验收状态

依赖：T004→T005/T006→T007/T008→T009→T010–T012→T013–T015（软件合并）；T016→T017（Windows 同包），T018→T019→T020（真机）。首工位实现 PR 未完成 T004–T015 前保持 Draft；旧 V6、合成 S01、V1.3 真机、真实 SDK、WPF 与完整整盘各自独立报告。PR #4/#6 合并的是准备文档；当前调用侧代码在 Draft PR #7，T007/T009/T012 等仍需独立设备模拟证据。

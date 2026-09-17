# Implementation Plan: 首工位——初始 3D 定位

**Implementation branch**: `station-01-implementation` from main `1519cfc` | **Date**: 2026-09-17 | **Spec**: [spec.md](spec.md). PR #4 merged preparation documents only; the implementation needs its own PR.

## Summary

在已有 `TrayWorkflow`、`MotionExecutionLane` 上补齐公共移至 3D 位、受理/完成关联和首轮 3D 定位。由 Host 统一组合工作流与独立合成设备/定位适配器；合成入口显式受限。通过状态/错误/轨迹证明一条工位链可运行，终点为 WaitingTrayCode，不执行 F。实际 PLC 点表和 3D SDK 仍由端口隔离。

本分支已合入框架 PR #5 的基线 `d030684`：复用 `Inspection.Contracts` 的对外状态/错误合同和十五模块能力目录；未实现能力保持默认拒绝。首工位实现时只将有独立证据的 S01 能力从 `ContractOnly`/`FrameworkOnly` 改为实际状态，不把整个平台标为生产就绪。

## Technical Context

- **Language/Version**: C#/.NET 10，SDK 10.0.401，仓库锁定依赖。
- **Dependencies**: 现有 Application、Motion 和 ASP.NET Core Host；不新增生产设备 SDK 假实现。
- **Storage**: 本工位需要记录诊断/动作意图与反馈的可靠边界；先明确恢复语义，必要时复用现有持久化能力或提交独立小型实现，不以进程内布尔值声称断电安全。
- **Testing**: Application 状态测试、Host 受限入口及真实进程合成联调；Linux/Windows CI 构建与测试，旧 V6 回归独立报告。
- **Target Platform**: Linux 开发与双进程合成联调；GitHub Ubuntu/Windows CI；同一候选包在实际 Windows 电脑做虚拟设备冒烟，接口/标定/现场准入后再做真实 PLC/3D 测试。WPF 仍单列未交付。
- **Constraints**: 不默认真实设备使用合成到位；不复用旧点表表示 V1.3；只有一个运动任务；超时后先对账。
- **Scale/Scope**: 两槽合成样例及工位故障矩阵；无配方、F 扫码、质量/整盘闭环。

## Constitution Check

设计前：V1.3 §5.2 顺序、唯一状态所有者、单运动通道、未知动作禁止盲重发、合成与实物来源隔离、分层证据满足章程。设计后需在实现 PR 复核：Host 只有一个业务入口，Domain 无 SDK/HTTP，关键意图/反馈可靠保存或将恢复能力明确标为未交付，不把单元测试冒称真机或整盘通过。无预设豁免。

## Project Structure

- `backend/src/Inspection.Application/Workflow/`：工位状态与端口/事件合同。
- `backend/src/Inspection.Application/Motion/`：复用唯一运动通道和未知状态锁定。
- `backend/src/Inspection.Infrastructure/`：合成定位与设备适配、诊断/必要持久化的实现；实际 SDK 另增量接入。
- `backend/src/Inspection.Host/`：组合根、受限工程测试入口、状态查询与诊断输出。
- `backend/tests/`、`tests/integration/`：状态、故障和 Host/独立进程链路验证；确切目录可随现有工程选择，但测试必须真正执行。
- `specs/005-initial-3d-station/`：本阶段规格、合同、矩阵、验证和独立证据。

## Implementation Sequence

先锁定契约和失败矩阵，再补工作流移位状态与事件关联；随后接合成端口和 Host 工程入口，最后按正常/异常分别跑通并记录双边轨迹。若保存边界比首工位范围大，可把测试模式与生产恢复能力明确分级，但不得开放未具备恢复的生产入口。生产点表/3D SDK 到位后另建对接任务与证据，不修改本阶段合成通过的定义。

## 两个不同的交付结论

**S01 软件增量可合并**须通过本机、GitHub 双平台 CI、独立合成设备跨进程联调，并证明 PR head 能产出带来源和校验值的 Windows 候选包。合并后另从成功 main SHA 产出供现场使用的包。**S01 真机验收**须在现场以该 main 候选包验证真实 PLC、3D SDK/标定和三方轨迹。软件 PR 可在真机 `NOT RUN/BLOCKED` 时合并，但不能标记首工位已在现场交付。F 读码、配方、算法、WPF 全流程及完整整盘均不在本 PR 中冒充通过。

## 从服务器到现场的门槛

| 门槛 | 环境 | 可核对的退出条件 |
| --- | --- | --- |
| G0 合同/独立期望 | Linux 服务器 | [工位合同](contracts/station-boundary.md)、[矩阵](test-matrix.md)对齐端口、阶段、错误码；未定设备地址和参数保持待确认。 |
| G1 规则与单端口测试 | Linux 服务器 | 先写独立期望，再实现 Ready→MoveTo3D→Scan3D→WaitingTrayCode；受理不等于到位，旧/乱序/迟到反馈不推进；故障后无后续动作。 |
| G2 可复现合成联调 | Linux 服务器 | Host 工程模式显式启用且仅回环；中台经正式应用端口连接独立运动与 3D 测试进程；N01 和适用故障行均由双方轨迹验证，不能只给 `TrayWorkflow` 直接灌完成事件。 |
| G3 实现 PR | GitHub | Ubuntu/Windows 锁定还原、Release 构建、核心/HTTP、发布 Host、S01 状态与跨进程矩阵及旧 V6 回归有非零用例和独立证据；从 PR head 产 Windows 候选包并核对 manifest。前端若改动则运行类型/组件/离线构建。失败修正后同一 PR 复测，不用 skipped 或旧 SHA 的绿色检查。 |
| G4 main 与候选包 | GitHub | 评审合并后对合并 SHA 重跑；Windows CI 产 S01 候选包，记录提交、run ID、运行依赖、组件、SHA-256。修复现有 staging 工作流的旧 `EngineeringFoundation` 阶段检查并扩展 S01 冒烟；同一字节包供 Windows 测试，不在现场重编。 |
| G5 Windows 真实系统 | 用户实际 Windows 电脑 | 独立目录解压，查系统/依赖/路径/端口；先以独立虚拟设备跑 N01、关键反例、日志与重启检查。当前无专用 runner，可手动执行脚本并回传报告；Windows 托管 runner 不等于这台电脑。 |
| G6 Windows 真实设备 | 设备现场 | 满足[现场准入](windows-real-device.md)后，由现场人员分步核对 PLC/3D：只读状态→停止/互锁→批准的单次移动→到位后定位→批准的异常。中台/PLC/3D 三方轨迹一致；缺点表、标定、设备或操作窗口则 `BLOCKED/NOT RUN`。 |

G3 为软件合并门槛；G4/G5 为同版本交接门槛；G6 才能称真机验收。G6 发现缺陷时，在新修复 PR 重跑受影响的 G1–G5，重新生成候选包再测。已有 `.github/workflows/windows-staging.yml` 只测基础 Host 且仓库目前无自有 runner，不能直接视为 G5/G6 已执行。

若正式 V1.3 点表和 3D SDK 在软件 PR 合并前到位，可将已确认的真实适配与合同测试纳入同一 PR；若在合并后才到位，应另开 **S01 设备对接 PR**，从 main 构建新的候选包并重跑 G3–G6。不能拿只有 synthetic Provider 的旧包执行 G6，也不能把这项遗留悄悄推给 S02。

## 责任与受控接口

下位机同学提供版本化点表、Ready/夹紧/互锁新鲜度、公共 3D 位与到位语义、动作关联、停止和重连对账及 PLC 独立轨迹。3D/设备同学提供 SDK、坐标系/单位、标定版本、样件和空盘规则及 3D 独立轨迹。前端同学按状态/错误合同验证当前页面，WPF 壳到位后另测实际桌面。中台负责唯一业务流程、日志、测试驱动、候选包来源和分层报告。未确认这些外部输入不阻止 G0–G4，但阻止 G6。

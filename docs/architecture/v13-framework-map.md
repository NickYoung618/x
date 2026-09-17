# V1.3 整体框架与逐工位接入图

本图是 V1.3 §3、§10、§13、§17 的实现归属表，不把目录或接口当成已交付功能。当前一个正式 `Inspection.Host`，Domain → Application → Infrastructure 方向不变；`Inspection.Contracts` 只放外部 DTO。十五责任模块是职责，不是十五个新项目或进程。

| 模块 ID / 层 | 当前状态 | 代码归属 / 后续接入点 | 负责人 |
| --- | --- | --- | --- |
| Presentation / L1 | ContractOnly | `frontend/` Vue 与 `desktop/` WPF 后续接入；只经 Host API | 前端 |
| Api / L2 | FrameworkOnly | `Inspection.Host` 唯一组合根，当前健康/状态/工程预览及拒绝入口 | 中台 |
| Jobs / L3 | ContractOnly | `Inspection.Application/Jobs` 未来承接任务准入、暂停/恢复与幂等 | 中台 |
| Recipes / L3 | ContractOnly | `Inspection.Application/Recipes` 未来发布/冻结版本；当前 demo plan 不是生产配方 | 中台 |
| Workflow / L3 | FrameworkOnly | 现有 `TrayWorkflow` 是进程内流程框架，未挂 Host 生产入口 | 中台 |
| Motion / L3 | FrameworkOnly | 现有单运动通道；实际动作/资源对账随工位和 PLC 合同接入 | 中台/下位机 |
| Acquisition / L3 | ContractOnly | 后续 3D/F/A/B/C/D/E 采集协调与容量准入 | 中台/设备 |
| AlgorithmRuntime / L4 | ContractOnly | 后续 Python Worker 调度与依赖终态；当前无真实算法 | 中台/算法 |
| Quality / L4 | ContractOnly | 后续 OK/NG/Pending、必检项完整性与判定修订 | 中台/工艺 |
| Traceability / L5 | ContractOnly | 后续 SQLite 单写、查询、关键保存与恢复 | 中台 |
| Media / L5 | ContractOnly | 后续帧/点云引用、暂存/归档与容量 | 中台 |
| DeviceAdapters / L5 | LegacyEngineering | 现有 Modbus 客户端与旧 V6 工程探针；V1.3 点表/真实设备未接 | 下位机/中台 |
| Diagnostics / 横向 | ContractOnly | 后续稳定报警码、结构化日志、诊断包 | 中台 |
| ModelManagement / 横向 | ContractOnly | 后续模型导入/发布/回退，任务边界激活 | 算法/中台 |
| Mes / 横向 | ContractOnly | 预留，当前核心任务无 MES 依赖 | 后定 |

`ContractOnly` 表示仅合同；`FrameworkOnly` 表示可运行的局部框架、尚无生产闭环；`LegacyEngineering` 仅旧协议工程验证；`Implemented` 需在对应 PR 附运行证据后才能使用。系统状态只反映交付能力，不是某台设备的实时 Ready；实时状态在后续设备接口单独提供。

## 接入流向与运行模式

每个工位使用同一条流向：前端命令 → Host API → Application 唯一工作流所有者 → 应用端口 → Infrastructure 设备/算法/数据适配 → 带关联 ID 的事件返回 → 状态查询/通知。长动作区分受理、物理完成和业务完成；异常按 V1.3 §14 定位与处置，未知运动先对账。Host 是唯一生产装配点，工程单步和自动流程共用动作/采集用例。

运行模式词汇为 FullSimulation、ImageReplay、HybridCommissioning、ManualHandoff、Production；当前默认 Unconfigured，不启动任何业务动作。全模拟/回放/混合/真实共用 Application 规则，Provider 来源随请求和结果记录；混合切换以设备或完整工艺边界进行，人工交接记录现场确认。旧 V6 探针不作为模式切换实现。生产模式绝不装入无物理证据的“自动成功”适配器。

下一步先在本框架 PR 建立上述模块/状态/拒绝合同并运行 P0 结构测试。V1.3 §18 的完整 P0 退出还需要前端同学接入最小 Vue 状态页，以及三方核对身份、端口与点表；当前保持未完成。首工位 S01 Draft PR #4 基于框架合并后的 main 继续实现。之后 F、A、B、C、D、翻面重扫、E 等各自单独 PR；每个 PR 才补本工位的端口、适配、状态、错误、正常/故障及跨进程证据。判定、保存、分拣和数据库维护是横向/收尾增量，不伪装成某台相机工位。

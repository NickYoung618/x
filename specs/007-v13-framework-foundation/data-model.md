# Data Model: 框架元信息

| 对象 | 字段 | 规则 |
| --- | --- | --- |
| ModuleDescriptor | Id、Layer、Owner、State | 对应 V1.3 十五个责任模块；Id 唯一稳定，State 为 ContractOnly/FrameworkOnly/LegacyEngineering/Implemented 的一种；Implemented 需要实际能力证据 |
| CapabilityDescriptor | Id、Available、EvidenceLevel、Reason | 具体能力独立于模块目录；框架期所有生产动作均不可用；原因可供前端展示 |
| SystemStatus | ArchitectureVersion、Stage、RuntimeMode、ProductionReady、Modules、UnavailableCapabilities | `ProductionReady` 不可由手工开关宣称；默认 RuntimeMode=Unconfigured；保留旧状态字段 |
| OperationProblem | Code、Title、Detail、CorrelationId | 未实现命令返回非 2xx；稳定码 `CAPABILITY_NOT_IMPLEMENTED`，不泄露内部异常 |
| AcceptanceCase | Id、ArchitectureSection、DeliveryPhase、Layer、ExpectedEvidence、Status | 未运行与失败/通过分开；只有真实执行记录可改为通过 |

本增量不建立 TrayRun、Recipe、Frame、AlgorithmResult 或数据库表；已有进程内类型保留，业务模型随工位/横向能力增量完善。

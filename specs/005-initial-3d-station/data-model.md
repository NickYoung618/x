# Data Model: 首工位

| 对象 | 最少字段 | 约束与转移 |
| --- | --- | --- |
| StationRun | TrayRunId, ScenarioId, Stage, SessionId, StartedAt | 同一时间只有一个状态所有者；运行中不得替换来源/会话 |
| ReadyObservation | SessionId, ObservedAt, Ready, FixtureClamped, InterlockClear, Source | 必须新鲜且匹配当前会话；过期/缺失阻止动作 |
| MoveTo3DOperation | OperationId, TrayRunId, TargetRef, Status, AcceptedAt, CompletedAt | Pending → Accepted → Completed/Failed/Unknown；Unknown 只允许对账，不自动重发 |
| PlacementRequest | RequestId, TrayRunId, ProposedEpoch=1, Source | 仅在 MoveTo3D Completed 后产生；每次请求唯一 |
| PlacementResult | RequestId, TrayRunId, Epoch, Source, CalibrationVersion, Frame, Unit, Slots, AcquiredAt | 匹配请求与来源；真实结果需有效标定/坐标系，合成结果明确标记 |
| LocatedSlot | SlotId, PositionRef, Pose/Validity | 唯一非空槽，位置可信；真实数值的单位/范围待硬件合同决定 |
| PartBinding | PartId, SlotId, CoordinateEpoch | 首次有效定位后生成；PartId 后续保持不变 |
| StationDiagnostic | Code, Stage, TrayRunId, OperationId/RequestId, Source, UtcTime, Duration, Detail | 错误码稳定；保存失败不得默默变成功 |

阶段：`WaitingReady → MovingTo3D → Scanning3D → WaitingTrayCode`；前置状态/定位明确失败转 `Failed`，运动结果未知转 `RecoveryRequired`。本阶段 `WaitingTrayCode` 是交付终点，不代表托盘业务完成。

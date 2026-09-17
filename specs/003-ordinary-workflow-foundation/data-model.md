# Data Model

- `TrayRunId`：本次运行唯一，由上层创建，不等于可复用托盘码。`ScenarioId`在启动前确定。
- `PartId`：由首轮3D槽位形成，跨面稳定。SlotId唯一且新3D必须恰好重新绑定这些槽位，缺/多/重复进入准备或定位失败。
- `RecipeSnapshot`：匹配的唯一方案版本与面列表；冻结后不可变。两个及以上匹配视为歧义。
- `CoordinateEpoch`：初扫为1，每次翻面后重扫成功再加1。所有移动请求携带当前Epoch；旧完成事件不更新新Epoch。
- `MotionOperation`：OperationId、动作Kind、PartId/FaceId、Epoch和状态Pending/Accepted/Completed/Failed/Unknown。至多一个未完成动作；未知保留对账。
- `CaptureWork`：PartId/FaceId/Camera/Attempt(首轮1)、技术状态Waiting/FrameReady/Failed；FrameReady生成独立AlgorithmTask。
- `AlgorithmTask`：同CaptureWork的键，技术状态Waiting/Completed/Failed/DependencyFailed/TimedOut。已达终态后迟到或重复结果不修改当前状态。
- `TrayStage`：AwaitingReady→AwaitingInitialScan→AwaitingTrayCode→AwaitingRecipe→Capture/Flip/Rescan→AwaitingAlgorithms→AwaitingDecision；严重失败为Failed或RecoveryRequired。AwaitingDecision不是OK/NG/放行。

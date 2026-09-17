# Application事件与端口合同（框架版）

唯一`TrayWorkflow.Handle(event)`同步转换当前状态并返回`WorkflowEffect[]`；调用方排队执行效果，设备/算法稍后以相同OperationId或CaptureKey归还事件。

- 输入事件：`DeviceReady`、`ScanCompleted/Failed`、`TrayCodeRead/Failed`、`RecipeMatched`（候选0/1/多）、`MotionAccepted/Completed/Failed/Unknown`、`CaptureCompleted/Failed`、`AlgorithmCompleted/Failed`、`AlgorithmDeadlineReached`。
- 效果：`RequestScan`、`RequestTrayCode`、`ResolveRecipe`、`RequestMotion`、`RequestCapture`、`RequestAlgorithm`。效果只说明下一步，不能表示物理成功或已保存。
- 运动动作：`PositionForCapture`和`FlipPart`；每个动作带OperationId与CoordinateEpoch。本框架不编码PLC地址/Float32。
- 完成/失败/未知仅匹配当前OperationId。`Accepted`不能推进；未知进入RecoveryRequired。新坐标批次前不能发下一面位置动作。
- CaptureKey含PartId、FaceId、Camera、Attempt；算法结果可乱序，不得改变不匹配或已终态任务。
- 端口均由Application定义，Infrastructure/设备同学后续实现。本轮假设备只在测试使用。
- Motion通道的队列有界，只执行一个动作；动作等待有可配置期限且同OperationId重复提交拒绝；被未知状态锁定后，后续提交拒绝。事件交付失败也锁住运动通道并公开错误；现场对账/恢复命令尚无实现。

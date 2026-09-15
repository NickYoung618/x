# 基础采集计划模型

## Inputs

- `PartId`：非空、无首尾空白的字符串；同一输入列表中不可重复（Ordinal 区分大小写）。
  此处为工程标识，不是生产条码或槽位。
- `FaceDefinition`：`FaceId` 同样非空、无首尾空白、不可重复；`CameraGroup` 只能为 AB 或 CD。
- 零件和面列表必须各至少一项；输入对象本身不可为 null。
- 本增量每个面每相机一次采集，多曝光/多视角留后续检测方案特性。

## Outputs

- `OrdinaryTrayPlan`：有序的不可变 `Faces`。
- `FaceCapturePlan`：`FaceId`；首面 `RequiresFlipBefore=false`，其余 true；
  所有面 `RequiresScanBefore=true`；不可变 `Captures` 按相机再按零件排列。
- `CaptureTarget`：`PartId`、`Camera`（A/B/C/D）。面关联来自所属 FaceCapturePlan。
- 不包含坐标、动作已完成状态、执行时间或质量结果。扫描屏障必须由后续 Workflow 执行并校验。
- 生成后复制输入并使用只读集合，调用者修改原列表不能改变计划。

## Application / HTTP

`DemoPlanView`：`IsTestFixture=true`、`ExecutionEnabled=false`、计划。
固定配置在 Host 启动时读取并验证；无效配置启动失败，不悄悄回退到默认生产方案。
系统状态 `Stage=EngineeringFoundation`、`ProductionReady=false`，列明未实现能力。

## Future relationships

运行时另建 TrayRunId、CaptureSetId、FrameId、Attempt、CoordinateEpoch、DecisionRevision 等关联。
本模型仅表达计划，不把示例 PartId 当成全局生产身份方案，不宣称已完成坐标版本校验。

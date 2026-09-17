# 首工位协作合同（待实现，非 PLC 点表）

本合同描述中台、前端、运动和 3D 端口边界。它不是双方已冻结的 Modbus 点表；正式设备编码仍以 `docs/contracts/virtual-plc-alignment.md` 对齐。

## 交互顺序

```text
Prepare(TrayRunId, ScenarioId)
  → FreshReady(SessionId, Clamped, InterlockClear)
  → MoveTo3D(OperationId, CommonTargetRef)
  ← Accepted(OperationId)
  ← Completed(OperationId)          # 到位；Accepted 不能触发定位
  → Locate3D(RequestId, TrayRunId, ProposedEpoch=1)
  ← PlacementResult(RequestId, Slots, Source, Frame/Unit/Calibration)
  → WaitingTrayCode + RequestF       # F 本阶段不执行
```

任一 ID、会话或来源不匹配的反馈都不推进。执行结果未知时锁定运动通道，显示需人工对账；已确定拒绝保留拒绝原因。3D 失败不发 F。跨进程设备测试需同时保存中台轨迹与独立模拟器轨迹。

## 状态/错误供前端联调

| 阶段 | 给用户的含义 | 允许的下一步 |
| --- | --- | --- |
| WaitingReady | 等待本次就绪和夹紧 | 刷新状态/取消 |
| MovingTo3D | 3D 移位未确认完成 | 等待、停止；不可手动标完成 |
| Scanning3D | 已到位、定位中 | 等待、停止 |
| WaitingTrayCode | 初始坐标已建立 | 下一工位 F 读码 |
| Failed | 明确前置/定位失败 | 查看原因，满足条件后新会话重试 |
| RecoveryRequired | 运动状态不明 | 现场确认位置并按恢复流程对账 |

稳定错误码建议以 `ST01_` 命名：`READY_STALE`、`NOT_CLAMPED`、`INTERLOCK`、`MOVE_REJECTED`、`MOVE_UNKNOWN`、`SCAN_TIMEOUT`、`SCAN_INVALID`、`SCAN_SOURCE_MISMATCH`、`DIAGNOSTIC_WRITE_FAILED`。实现时在测试中固定完整映射；不要把异常堆栈当用户文案。查询至少给阶段、错误码/中文说明、RunId、请求 ID、坐标批次和 synthetic/real 来源。测试入口与生产入口分开，测试入口不得在真实设备模式启用。

## 给下位机/3D 同学的待填项

下位机：本次 Ready/夹紧/联锁的采样与新鲜度；公共移至 3D 的动作编码、受理/完成/失败信号；OperationId 或兼容握手的关联方式；停止、超时、重连和未知动作对账。3D：定位调用、点云/结果引用、SlotId 稳定规则、坐标系/单位、标定版本、姿态/置信度及超时。双方给出的测试轨迹应能证明实体到位先于定位触发。未提交前保持抽象端口。

# S01 3D 定位调用侧合同 v0.1（待下位机同学确认传输方式）

此合同描述中台 `IPlacementLocator` 与下位机同学交付的**独立 3D 设备接口模拟进程**之间的语义。它不是已确认的 HTTP/Modbus/SDK 线路协议；传输、端点、错误码映射和版本由双方签认后实现。中台目前只实现端口消费和受限 Host 入口，未把测试替身当独立设备联调。

| 方向 | 必填字段 | 约束 |
| --- | --- | --- |
| 请求 | RequestId、TrayRunId、ProposedEpoch、Initial、ExpectedSource | RequestId 唯一，首次 Epoch=1；只有匹配的 MoveTo3D 完成后发一次 |
| 成功结果 | 原 RequestId、TrayRunId、CoordinateEpoch、Source、Frame、Unit、AcquiredAtUtc、Slots | `synthetic` 结果给稳定 SlotId/PositionRef，结果来源不得伪装 `real`；真实结果另需 CalibrationVersion 与有限的毫米位置 |
| 槽位 | SlotId、PositionRef；真实结果增加 X/Y/Z | SlotId 唯一且非空；坐标批次属于本盘；不以 PositionRef 代替真实毫米坐标 |
| 错误 | 原 RequestId、稳定错误类别、设备侧时间及详情 | 超时、断线、旧/错 ID、空/重复/坏槽可确定性触发；中台必须保留设备侧与 Host 双轨迹 |

固定正常样例为 `tray-1`、首次 Epoch=1、两个合成槽位 `s1/s2`。模拟器不自行推进中台业务状态；其控制/故障设置接口只改变设备事实，业务请求必须经中台调用端口。设备侧轨迹至少记录请求 ID、源、接收/返回时间、场景、槽位计数、实际触发次数和错误类别。回调晚于超时、另一盘、错批次或错来源都不得推进本次任务。

签认前待定：传输与端点、进程启动/健康检查、超时和断线表现、错误码映射、时间戳时钟约定、是否回传原始点云引用以及真实 SDK 的版本/标定取得方式。不得从旧 V6 PLC 点表推导这些字段。联调通过需在 Linux 与 GitHub Windows 上启动独立进程，逐行对照 [S01 矩阵](../test-matrix.md)的 N01、S01–S04b；只有内存 fake 的测试单列为调用侧测试。

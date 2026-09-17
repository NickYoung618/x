# Data model

| 实体 | 字段/约束 | 转移 |
| --- | --- | --- |
| `PlcConnectionOptions` | Provider、合同、主机/端口、UnitId、地址口径、Float32 排列、坐标系/单位、动作开关；新合同和排列必须显式给出 | Disabled → Virtual/Real；Real 永久只读直到另一次签认版本 |
| `PlcSnapshot` | 来源、PC 观察时间、心跳/Ready/Auto/Fault/人工区/夹紧/区域 ACK、XY/Z/翻面/分拣状态、三轴 Float32、当前面、报警位/等级 | 每次为独立采样；不同区段非 PLC 原子快照 |
| `PlcMoveTarget` | 有限 Float32 X/Y/Z、坐标系、单位、1–5 移动用途、Camera/Scan/Grab Z 类别 | 参数检查 → 写入 → 运行反馈 → 完成且实际值匹配 |
| `PlcResult` | OperationId、Ready/Accepted/Completed/Failed/Unknown/RecoveryRequired/Unsupported、来源和原因 | 未知/失败后需要人工对账；不得自动重放 |
| `PlcSortRequest` | 槽位、取料与放料目标、去向 | 协议无完整字段，本版本恒 `Unsupported` |

虚拟动作仅在回环地址和显式测试开关下可用。翻面 `targetFace` 为面编号，不是角度。设备无 OperationId 回显，因此运行/完成仅可作为隔离测试中的有限归属证据。

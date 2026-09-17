# Data Model: VirtualPlc Validation

## ValidationRun

| Field | Type | Rules |
|---|---|---|
| startedAt / finishedAt | timestamp | UTC，finishedAt 不早于 startedAt |
| environment | object | 记录 OS、实际 SDK、生产目标和运行模式 |
| checks | ValidationCheck[] | 至少一个；名称在一次执行内唯一 |
| gates | EnvironmentGate[] | 至少包含生产 .NET 10 构建门禁 |
| limitations | string[] | 规范缺口，不计为自动检查失败 |
| verdict | PASS / FAIL | 任一 check FAIL 时为 FAIL；NOT RUN gate 单独展示 |

## ValidationCheck

| Field | Type | Rules |
|---|---|---|
| id | string | 稳定编号，如 `MODBUS-03` |
| requirements | string[] | 一个或多个 `FR-###` |
| name | string | 人类可读且唯一 |
| status | PASS / FAIL | 自动检查必须二选一 |
| durationMs | integer | 非负 |
| detail | string | 通过时给出关键证据，失败时给出异常原因 |

状态转换：`pending -> PASS` 或 `pending -> FAIL`。失败被记录后继续执行独立检查，最终统一
计算 verdict。

## PlcPoint

| Field | Type | Rules |
|---|---|---|
| area | Coil / HoldingRegister | 与地址前缀对应 |
| documentAddress | string | `0xNNNN` 或 `4xNNNN` |
| pduOffset | integer | document number - 1 |
| name | string | 在全点表唯一 |
| direction | PLC->PC / PC->PLC | 决定写权限 |

同一 PlcPoint 来自 CSV、HTTP address-map 和 state 三个视图；三方关键字段必须一致。

## FaultScenario

| Field | Type | Rules |
|---|---|---|
| fault | enum | 9 个公开 SimulationFault 之一 |
| trigger | action | 注入后用于观察结果的动作 |
| expectedState | value set | 明确的寄存器、线圈或 snapshot 状态 |
| recovery | reset / manual confirmation / one-shot | 必须能自动验证 |

## EnvironmentGate

| Field | Type | Rules |
|---|---|---|
| name | string | 门禁名称唯一 |
| status | PASS / FAIL / NOT RUN | 缺少 SDK 时为 NOT RUN，不伪装 PASS |
| reason | string | NOT RUN 和 FAIL 必填 |

## Relationships

- ValidationRun 聚合多个 ValidationCheck 和 EnvironmentGate。
- ValidationCheck 通过 requirements 关联 feature spec。
- 地址表检查比较三个来源的 PlcPoint。
- 每个 FaultScenario 至少由一个 ValidationCheck 覆盖。

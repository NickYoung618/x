# Validation Contract

## Command Contract

从 `pj1` 根目录执行：

```bash
bash VirtualPlc/scripts/validate.sh
```

脚本 MUST：

1. 只在 `pj1` 内还原、构建和生成结果；
2. 自动选择空闲的 HTTP 和 Modbus 回环端口；
3. 启动同源验证主机并等待 `/health`；
4. 执行全部检查，即使某个独立检查失败；
5. 无论成功失败都停止子进程；
6. 写入 `VirtualPlc/test-results/latest.json`、`latest.md` 和主机日志；
7. 检查全通过返回 0，否则返回非 0。

## Machine-readable Result

`latest.json` 顶层字段：

```json
{
  "startedAt": "timestamp",
  "finishedAt": "timestamp",
  "environment": {
    "sdkVersion": "string",
    "productionTarget": "net10.0",
    "executionTarget": "net8.0|net10.0",
    "mode": "compatibility-host|production"
  },
  "checks": [
    {
      "id": "string",
      "requirements": ["FR-001"],
      "name": "string",
      "status": "PASS|FAIL",
      "durationMs": 0,
      "detail": "string"
    }
  ],
  "gates": [
    { "name": "Production .NET 10 build", "status": "PASS|FAIL|NOT RUN", "reason": "string" }
  ],
  "limitations": ["string"],
  "verdict": "PASS|FAIL"
}
```

## HTTP Boundary

验证覆盖：`/health`、`/`、`/app.js`、`/styles.css`、`/api/simulator/state`、
`/api/simulator/address-map`、`/api/simulator/reset`、`/api/simulator/faults/{fault}`、
`/api/simulator/flow-decision`。无效 fault/category MUST 返回 400。

## Modbus Boundary

- 成功功能码：01、03、05、06、0F、10。
- 文档地址一基；PDU offset 零基。
- 预期异常：01 Illegal Function、02 Illegal Data Address、03 Illegal Data Value、
  0B Gateway Target Device Failed to Respond（错误 Unit Id）。
- 多点写入 MUST 原子校验整个区间；任何未定义或方向不符的点使整次写入失败。

## Timing Contract

- 轮询断言使用不超过 5 秒的显式截止时间。
- 心跳暂停/超时场景允许超过默认 3 秒超时，但完整运行 MUST 小于 60 秒。
- 不使用固定长等待来判断动作完成；除心跳时间窗外均轮询明确状态。

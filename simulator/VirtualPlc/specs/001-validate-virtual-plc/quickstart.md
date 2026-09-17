# Quickstart: VirtualPlc 全量验证

## Prerequisites

- 在 `/home/ubuntu/disk/pj1` 工作区执行。
- 已安装 .NET SDK 8 或更高版本。
- 默认回环端口可由脚本自动分配。

## Run

```bash
cd /home/ubuntu/disk/pj1
bash VirtualPlc/scripts/validate.sh
```

预期控制台末尾显示检查总数、PASS/FAIL 数量、生产构建门禁和报告路径。所有自动检查
通过时退出码为 0。

## Evidence

- `VirtualPlc/test-results/latest.md`: 人类可读验证报告。
- `VirtualPlc/test-results/latest.json`: 机器可读逐项结果。
- `VirtualPlc/test-results/host.log`: 被测服务日志。

当前机器若仍只有 .NET 8，行为测试使用 compatibility host，报告中的 `Production .NET 10
build` 应为 `NOT RUN`。这不影响黑盒行为检查的 PASS/FAIL，但不能作为 .NET 10 构建通过
的证据。

## Re-run Check

连续执行两次同一命令。第二次也应通过，且 `latest.*` 被新一次结果替换；服务进程和端口
不会残留。

## Failure Triage

先查看 `latest.md` 中首个失败检查，再查看 `host.log` 的同一时间段。协议类失败可根据
[validation-contract.md](contracts/validation-contract.md) 的异常码定位；动作类失败结合
`/api/simulator/state` 中的 `activeAction`、`activeFaults` 和状态点定位。

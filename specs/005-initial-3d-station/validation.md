# S01 首次上位机实现验证（2026-09-17）

本记录只验收**中台调用侧第一增量**，不验收下位机同学尚未交付的独立 PLC/3D 接口模拟、正式 V1.3 点表或 Windows 真机。基线为 `station-01-implementation` 的 `0bc0ddc`；该文档 PR #6 已合并至 main，实际实现另见 `station-01-upper-implementation` 的 [Draft PR #7](https://github.com/NickYoung618/x/pull/7)。测试前工作区含本轮代码，旧协议脚本证据中的 `workingTreeDirty=true` 为真实状态。

| 层级 | 实际结果 | 证据与边界 |
| --- | --- | --- |
| 锁定依赖 | PASS | SDK 10.0.401；`dotnet restore Gaode.slnx --locked-mode` |
| Linux 解决方案 | PASS | `dotnet test Gaode.slnx -c Release --no-restore`：Domain 15、Application 37、Infrastructure 11、Host 13，合计 76/76、跳过 0；含旧 003 流程回归 |
| S01 调用侧定向 | PASS | Application 25/25、Host 5/5，合计 30/30、跳过 0；TRX 保存在本机 `/tmp/gaode-station01-current/`，CI 作业另存 artifact；这些测试使用测试端口替身，不是设备独立进程 |
| 旧 V6 协议与旧模拟器 | PASS，独立计数 | `scripts/validate-virtual-plc.py` 的 [最终本地证据](../../simulator/artifacts/20260917T090346Z-36f3c31e/summary.json)：核心 76/76、原 13/13、旧双进程 5/5；合同为 `legacy-v6-u16-snapshot-20260917`，不能替代 S01/V1.3 |
| GitHub Ubuntu/Windows S01 调用侧 | PASS（提交 `7d2eabc`） | [CI 运行 35203413415](https://github.com/NickYoung618/x/actions/runs/35203413415) 八作业成功；两平台 S01 Application 25/25、Host 5/5。此结果只覆盖该提交，后续提交须重跑 CI |
| 独立 PLC + 3D 设备接口双进程 S01 | BLOCKED | 下位机同学 D1/D2/D2b/D3 尚未交付；当前 Host 不注册设备端口，启用工程开关也返回 `ST01_PROVIDER_UNAVAILABLE` |
| V1.3 真正点表、3D SDK/标定、实际 Windows、完整整盘 | NOT RUN | 接口/设备/现场与后续工位尚未齐备，旧协议及端口测试不折算通过 |

## 已验证的调用侧行为

本轮将就绪与夹紧/互锁新鲜度检查置于 MoveTo3D 前；同一 OperationId 的 Accepted 与 Completed 必须先后到达，才发一次 3D 请求。3D 结果按 RequestId、TrayRunId、Epoch、来源、时间、槽位及真实坐标的单位/标定核对；成功建两个 PartId、Epoch=1，发出一次 F 请求但不执行 F。错误覆盖未就绪/过期、运动拒绝/未知、错/早/重复完成、扫描超时/异常、空/重复/坏槽、错盘/批次/请求、迟到及来源/坐标不可信。断线未知停在 `RecoveryRequired`，无后续 3D/F。Host 工程入口默认关闭、仅本机、仅合成来源，缺端口或重复盘次拒绝；日志失败返回 `ST01_DIAGNOSTIC_WRITE_FAILED` 且保留需对账状态。生产 `/api/jobs/prepare` 仍为 501，因为动作意图/反馈无持久化和重启对账实现。

## 未通过与下一增量

现有测试端口替身没有独立设备轨迹，因此不覆盖实际 Modbus 编解码、3D 模拟进程调用、真实重连时序或跨重启去重。下位机同学需按[派任书](../../docs/assignments/s01-plc-work-order-20260917.md)提交 D1–D3（含 D2b）；中台随后实现已签认的调用适配、以真实中台入口跑完整 [矩阵](test-matrix.md)，核对设备侧/Host 两份轨迹并生成 S01 Windows 候选包。完成前 PR #7 保持 Draft，G2/G3 不判通过。

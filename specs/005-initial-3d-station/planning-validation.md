# S01 开发与跨环境测试方案核验（2026-09-17）

本记录只验收**方案可追溯和可执行性**，不验收 S01 代码或真实设备。对照 main=`1519cfccb68ddb9a85607154d3cbfd41be9e704f`；PR #4 合并的是准备文档。

| 检查 | 本轮结果 | 实际证据与限制 |
| --- | --- | --- |
| Spec Kit 前置 | PASS | `check-prerequisites.sh --json --require-spec --require-tasks --include-tasks` 指向 `specs/005-initial-3d-station`，列出研究、模型、合同、快速启动和任务 |
| V1.3 与章程对照 | PASS（方案） | §5.2 顺序、§11 动作关联/恢复、§13 双级模拟、§14 错误、§18 分层验收均映射到 [方案](plan.md)、[矩阵](test-matrix.md)、[现场规程](windows-real-device.md) |
| 环境现状 | 已核实 | `main` 干净；GitHub 仓库自有 runner 列表 0；`windows-staging.yml` 仍比较旧 `EngineeringFoundation`，与当前 CI manifest `V13FrameworkFoundation` 不一致，须在实现 PR 修正 |
| 文档相对链接、空白错误 | PASS | 修改文档的本地相对链接均存在；`git diff --check` 通过 |
| 本地 .NET/前端/旧协议/S01/Windows 真机 | 本地 NOT RUN | 本轮仅文档设计，没有新增代码、运行设备或发布候选包；PR CI 若触发另按该 SHA 判定，历史 CI 不能充作本轮 S01 实测 |

软件实现门槛 G1–G4 和现场门槛 G5–G6 尚待后续任务。缺正式点表/3D SDK/标定时仅阻止真机准入，合成链路和诊断/打包工作仍可推进。无外部付费调用，费用 0 元。

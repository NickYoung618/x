# S01 开工准备核验（2026-09-17）

本记录只验收开工准备，**首工位尚未实现或测试通过**。

| 项目 | 实际结果 | 证据/边界 |
| --- | --- | --- |
| 既有 PR 合并 | #1、#2、#3 依次合并，main=`09904fd8fe195fe2665e030d8b76b806e4c2f3ae` | 各 PR 合并前四项检查成功；后一个 PR 调整 base 后核对合并树与已测 head 树相同 |
| main CI | Linux/Windows Core 与 Virtual PLC framework 四作业 SUCCESS | [运行 35187291769](https://github.com/NickYoung618/x/actions/runs/35187291769)；验证现有 .NET10 框架/旧协议，不含首工位 |
| Spec Kit 前置 | PASS，FEATURE_DIR 指向 `specs/005-initial-3d-station`，包含 research/data-model/contracts/quickstart/tasks | `.specify/scripts/bash/check-prerequisites.sh --json --require-spec --require-tasks --include-tasks`；指针是本地忽略文件 |
| 范围与合同 | 首工位终点 WaitingTrayCode；已列状态、错误、责任人待填项与精确故障矩阵 | [规格](spec.md)、[合同](contracts/station-boundary.md)、[矩阵](test-matrix.md)、[任务](tasks.md) |
| 本轮源码/首工位测试 | 未改源码；首工位测试 NOT RUN | 开工准备不冒充工位实现 |
| V1.3 正式 PLC、真实 3D、完整整盘 | NOT RUN | 缺正式点表、SDK/标定、生产配方与后续阶段 |

下一步在本分支完成 T004–T015，于同一个首工位 Draft PR 内补齐可运行证据后再评审、合并。前端与下位机同学拿此版本合同准备输入和独立轨迹；正式协议决定未到位时先跑合成工位链，不填入伪造的真机参数。

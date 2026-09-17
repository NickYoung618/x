# Validation: V1.3 设备模拟

**状态**：合同阶段。此文件只记录已经核对的事实；构建和运行结果须在执行后附命令、提交号、原始证据路径及通过/失败/NOT RUN。

| 范围 | 当前结果 | 证据/备注 |
| --- | --- | --- |
| 008 未提交工作保护 | PASS | `stash@{0}` 与 `/home/ubuntu/disk/pj/.housinginspection-backups/008-before-v13-20260917T142027Z`；尚未恢复到 008 分支 |
| PR #9 起点 | PASS | 功能分支从 `3bed65e` 创建；合同提交 `9d382ee` |
| 合同链接/空白检查 | PASS | 本地链接已核对；`git diff --cached --check` 在合同提交前无错误 |
| .NET 10 锁定还原、Release 构建、全解决方案测试 | 当前增量 PASS | `dotnet restore Gaode.slnx --locked-mode`；Release 构建 0 警告/错误；`dotnet test Gaode.slnx --no-build -c Release` 57/57。后续代码提交后须重跑 |
| 旧 V6 单独回归 | 当前增量 PASS | [报告](evidence/legacy-v6-latest.md)：独立脚本 13/13；先前脚本引用过期 `net8.0` 路径导致未运行，随后修复；首次完整跑因从二进制目录启动导致静态资源 404，修正内容根目录后完整复跑通过 |
| 9 月 11 日独立 Modbus TCP 基础双进程 | 限定范围 PASS | [Host 原始报文](evidence/base-host-read-only.json)、[模拟器原始报文/事件](evidence/base-simulator-trace.json)、[最终状态](evidence/base-simulator-state.json)：新点表只读、Float32 双字、夹紧、人工区故障及未定义命令拒绝；**未执行 3D/F 运动** |
| 9 月 11 日独立 Modbus TCP 运动正常与故障 | NOT RUN | 待确认虚拟扩展并实施 |
| Host/PLC/3D 三进程 S01 | NOT RUN | 待确认虚拟扩展和 3D 传输 |
| Windows 真机、完整整盘 | NOT RUN | 无现场设备或完整业务证据 |

远端：[Draft PR #10](https://github.com/NickYoung618/x/pull/10) 已建立，base 为 `plc-protocol-20260911-alignment`，未合并；GitHub CI 在建立时运行中，结果未据此记 PASS。

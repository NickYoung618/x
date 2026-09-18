# Validation: PLC 接口按 9 月 11 日协议对齐

**状态**：Linux 本机接口框架验证完成；独立分支 `plc-protocol-20260911-alignment`，首提交 `6bfa1de`，[Draft PR #9](https://github.com/NickYoung618/x/pull/9)。正式新版虚拟 PLC、Windows 真机与整盘仍未验收；PR CI 待复核。

| 层级 | 当前事实 |
| --- | --- |
| .NET 10 工程 | SDK 10.0.401；锁定还原 PASS、Release 构建 0 警告/0 错误、四个后端测试程序集 57/57、跳过 0；旧脚本最终汇总见 `simulator/artifacts/20260917T131635Z-b05dfc1d/summary.json`。 |
| 旧 V6 探针/模拟器 | `scripts/validate-virtual-plc.py` PASS：历史协议 13/13，Host↔独立 V6 模拟器正常、旧状态拒绝、设备失败、动作超时、未知重连共 5 个场景；只证明旧协议。 |
| 新协议定向 TCP 服务 | `Protocol20260911Tests` 11/11：四种 Float32 排列、新地址、三轴/面号/报警只读、虚拟双字动作、错误面号、遗留 Ready/命令拒绝和超时不重发；同属上述 57 项。服务为独立 TCP 对端，但由测试进程启动，不是下位机同学正式模拟器。 |
| 新协议 Host 跨进程只读 | `scripts/check-plc-20260911-host.py` PASS：真实 Host 进程通过 Modbus TCP 连接独立测试服务，4 次 FC01/FC03 读、0 写；显式动作开关被拒绝；报告 `simulator/artifacts/plc-20260911-host-readonly-final.json`。未执行完整 3D/F/分拣。 |
| 正式新版独立虚拟 PLC | NOT RUN；`pj/Virtual PLC` 仍为 V6 点表。 |
| Windows 真实设备 | NOT RUN；真实动作配置明确阻断。 |
| 完整整盘 | NOT RUN；无生产配方、相机/算法链及全站设备协议。 |

首次本机验证中曾把 `--locked-mode` 误用于 `dotnet build` 且试了不存在的旧解决方案路径，均属命令错误；增加 Infrastructure→Application 项目引用后首次锁定还原因 lock 文件未更新而按预期失败，随后 `dotnet restore Gaode.slnx --force-evaluate` 更新锁文件，最终锁定还原/构建/测试通过。未把这些前置失败隐去或当作设备故障。

测试上限：新协议虚拟动作仅与确定性 TCP 对端验证报文与有限握手；没有设备命令序号，不能证明重启/断线后结果归属。`pj/Virtual PLC` 与仓库正式模拟器仍是 V6；Windows 现场、真实机械互锁、完整整盘与特殊件均未运行。

# 首工位准备与后续验证入口

当前分支仅有规格与测试计划。不要把以下基线命令通过当成首工位已实现。

基线已包含框架 PR #5：后端的十五模块状态与默认拒绝、前端只读状态页和旧协议回归。首工位完成后，需为新增能力补对应的后端/Host/前端状态测试，不能只沿用框架检查。

```sh
dotnet restore Gaode.slnx --locked-mode
dotnet build Gaode.slnx --no-restore
dotnet test Gaode.slnx --no-build
python3 scripts/validate-virtual-plc.py
```

实现 PR 内需补充一个可复现的首工位合成双进程/Host 测试命令，并输出 RunId、来源、独立运动轨迹、3D 端口轨迹、状态与错误码。正常双槽和故障矩阵逐项判定。旧协议验证单列，不能充作本工位对接；正式 V1.3 点表、真实 3D SDK、配方和完整整盘维持 `NOT RUN` 直到有各自证据。

前端同学可先按 [状态合同](contracts/station-boundary.md) 准备显示/操作样例；下位机同学可按同一合同补全动作与反馈语义及自己的独立轨迹。接口未确认时不得编造真实毫米坐标或完成位。

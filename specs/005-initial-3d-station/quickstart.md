# 首工位准备与后续验证入口

当前实现分支已有上位机状态机、受限 Host 入口及端口合同测试，**独立 PLC/3D 设备接口模拟仍由下位机同学交付**。不要把以下基线命令通过当成双进程首工位已验收。

基线已包含框架 PR #5：后端的十五模块状态与默认拒绝、前端只读状态页和旧协议回归。首工位完成后，需为新增能力补对应的后端/Host/前端状态测试，不能只沿用框架检查。

```sh
export PATH=/home/ubuntu/.local/share/gaode-dotnet-10.0.401:$PATH
dotnet restore Gaode.slnx --locked-mode
dotnet build Gaode.slnx -c Release --no-restore
dotnet test Gaode.slnx -c Release --no-build
python3 scripts/validate-virtual-plc.py
```

上述脚本最后一项仍只验证旧 V6 协议。实现 PR 内需新增一个固定的 S01 入口（建议 `python3 scripts/validate-station-01.py --mode synthetic --output <独立目录>`；**此命令尚不存在，不能现在运行或写 PASS**），启动发布 Host 与独立运动/3D 测试进程，逐行输出 RunId、来源、设备与中台各自轨迹、最终状态/错误及真实计数。正常双槽和[故障矩阵](test-matrix.md)逐项判定，失败/跳过返回非零并保留本轮产物。

下位机同学交付独立模拟进程前，可运行上位机的实际调用侧用例：

```sh
dotnet test backend/tests/Inspection.Application.Tests -c Release --filter 'FullyQualifiedName~Station01'
dotnet test backend/tests/Inspection.Host.Tests -c Release --filter 'FullyQualifiedName~Station01'
```

Host 工程入口 `POST /api/engineering/station-01/prepare` 默认关闭；仅在显式 `Station01:EngineeringEnabled=true`、本机请求、外部端口 `IReadyObserver`、`IMotionDevice`、`IPlacementLocator` 与来源描述 `IStation01ProviderIdentity` 均已注入且标识为 `synthetic` 时运行。当前正式组合根没有注入任何设备端口，故启用开关后仍返回 `ST01_PROVIDER_UNAVAILABLE`，不能靠 HTTP 直接伪造到位。成功工程响应只停在 `WaitingTrayCode`，`FRequests=1` 而 `FExecuted=false`。工程盘次在本进程内禁止重复；该记忆不耐重启，所以生产 `/api/jobs/prepare` 继续返回 501。独立模拟器/传输未交付前，GitHub 新增的 `S01 upper-port contract` 作业只验调用侧，不判 G2/G5 通过。

PR 阶段在 GitHub 核对同一 head SHA 的 Ubuntu/Windows Core、UI、旧协议和新增 S01 作业；修正后重跑，不拿之前 SHA 的绿色结果。合并后从成功 main 构建取得带 manifest/SHA-256 的 Windows S01 候选包。当前 `.github/workflows/windows-staging.yml` 只含基础 Host 冒烟，阶段名与现有包不一致，且仓库无专用 runner；它须在实现 PR 修正和扩展后才可作为现场入口。

在用户实际 Windows 电脑，先核对 `Get-FileHash <候选包.zip> -Algorithm SHA256` 与 manifest，解压至每次独立目录，运行同一包的合成 S01 冒烟并保存日志/报告。随后只在[现场准入](windows-real-device.md)全部满足时测试真实 PLC/3D；缺设备、点表、标定或现场批准则该层为 `BLOCKED/NOT RUN`。旧协议验证、合成 S01、V1.3 正式点表、真实 3D、WPF 与完整整盘各自单列，不互相替代。

前端同学可先按 [状态合同](contracts/station-boundary.md) 准备显示/操作样例；下位机同学可按同一合同补全动作与反馈语义及自己的独立轨迹。接口未确认时不得编造真实毫米坐标或完成位。

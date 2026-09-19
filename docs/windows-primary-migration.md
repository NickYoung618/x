# Windows 主工作区迁移与停用 Linux 检查表

**决定日期：2026-09-19。** 后续开发、联调和最终运行改到 Windows 服务器/设备；当前 Linux 服务器不再作为必需开发环境。GitHub 私有仓库仍是代码与评审的共同来源。GitHub 托管的 Ubuntu 作业可作为额外可移植性检查继续存在，它不依赖本机 Linux，也不代表软件部署到 Linux。

## 1. 迁移包是什么

迁移包名称为 `Gaode-Windows-Development-Migration-<日期>-<main短SHA>.zip`。这是**开发工作区和资料迁移包**，不是可运行的 FullSim、生产安装包或整盘验收包。包内包括：

- `repository/gaode-repository.bundle`：Git 仓库、提交和已取得的分支引用，可在无网络时恢复；恢复后仍把 GitHub 设为 `origin`。
- `authoritative/`：[三类权威资料](authoritative-sources.md)及 SHA-256 来源说明。
- `operations/`：工位顺序、测试门禁、全虚拟覆盖、Windows 验收和团队发包规则。
- `scripts/`：Windows 环境检查与恢复脚本。
- `continuity/`：生成时明确传入的本机恢复材料，例如旧 008 stash/备份和 VirtualPlc 原始快照。
- `archive-not-authoritative/`：为防停机丢失而保存的 `pj/资料`，不参与接口或配方签认。
- `manifest.json` 与 `checksums.sha256`：来源提交、文件角色和逐文件哈希。

包内不放 API 密钥、GitHub 凭据、Windows 密码、相机许可证或生产数据库。GitHub 私有仓库权限和设备授权在 Windows 上通过各自的安全入口重新配置。

## 2. Windows 必需环境

| 项目 | 当前固定要求 | 说明 |
| --- | --- | --- |
| 操作系统 | 设备/相机/PLC 厂商共同支持的 64 位 Windows | Windows Server 能否用于交互桌面、驱动、GPU 和采集卡必须由厂商确认 |
| Git | 可运行 `git clone/fetch/worktree` | 私有仓库访问另行登录 |
| PowerShell | PowerShell 7 (`pwsh`) | CI 与仓库脚本统一入口 |
| .NET SDK | `global.json` 固定的 **10.0.401** | `rollForward=disable`，不能由另一 SDK 冒充 |
| Node.js | `frontend/package.json` 要求 `>=24.12.0` | 使用锁文件执行 `npm ci` |
| Python | 3.12 或更高 | 虚拟 PLC 和证据门禁脚本需要 |
| WebView2 / 相机与 PLC 工具 | 对应候选包和真实设备阶段安装 | 当前尚无 WPF 完整包；未安装时不能宣布桌面/真机通过 |

过去记录的 Windows Server 2019、2 核/8 GB、无已确认 GPU/SDK 只属于旧机器观察，不能自动套用到新服务器。正式使用前记录系统版本、CPU、内存、磁盘、GPU/驱动、WebView2、.NET、Node、Python、设备 SDK/固件及网络配置。

## 3. Windows 恢复步骤

1. 将 ZIP 和另行提供的 ZIP SHA-256 放入 Windows 本地磁盘；先核对外层哈希，再解压到新的只读迁移目录。
2. 在该目录运行：

   ```powershell
   pwsh -File .\scripts\Restore-GaodeWorkspace.ps1 -Destination C:\Gaode
   ```

3. 脚本逐项核对 `checksums.sha256`，从 bundle 恢复 `C:\Gaode\repo`，把 GitHub URL 设置为 `origin`，但不会自动执行设备动作或写生产配置。
4. 进入仓库运行：

   ```powershell
   pwsh -File .\scripts\Test-WindowsDevelopmentEnvironment.ps1
   dotnet restore Gaode.slnx --locked-mode
   dotnet build Gaode.slnx -c Release --no-restore
   dotnet test Gaode.slnx -c Release --no-build
   Push-Location frontend
   npm ci
   npm run typecheck
   npm test
   npm run build
   Pop-Location
   python scripts\validate-virtual-plc.py
   ```

5. 执行 `git fetch origin --prune`，核对 main 和开放 PR 分支；每项开发继续使用独立 `git worktree`，不要三个人共用同一目录。
6. 先跑虚拟模式，保持生产 `/api/jobs/prepare` 和真实 PLC 动作禁用。只有正式点表、设备准入和本站测试通过后，才按[Windows 现场方案](windows-acceptance.md)逐步替换真实 Provider。

## 4. 停用当前 Linux 前的门槛

只有以下各项都有 Windows 侧证据后，才删除或关闭 Linux 上的原工作目录：

- ZIP 外层 SHA-256 与交付记录一致，包内 `checksums.sha256` 全部通过。
- bundle 能在新目录 clone，`git fsck --full` 通过，main 提交与 `manifest.json` 一致。
- Windows 能访问 GitHub 私有仓库，开放 PR 分支可 fetch；本机 008 stash/备份和 VirtualPlc 快照已在 `continuity/` 核对。
- 固定 SDK/Node/Python 检查通过，锁定还原、Release 构建、测试和已实现的独立模拟器检查在 Windows 实际运行。
- 三份权威资料的哈希在 Windows 重新计算一致。
- 将 Windows 的首次失败、修复和复测保存到仓库规格或外部验收记录；不能拿 Linux 历史 PASS 代替。

当前尚未完成 V1.3 运动双进程、Host/PLC/3D 三进程 S01、WPF FullSim、实际 Windows 设备和完整整盘。迁移成功只表示工作可在 Windows 继续，不改变这些 `NOT RUN/BLOCKED` 结论。

## 5. 后续发包方式

开发迁移包只使用一次。日常协作继续走 GitHub 分支/PR/CI；用于现场的软件必须由成功的 main 提交生成带 `build-manifest.json` 和 `checksums.sha256` 的 `win-x64` 候选包。全组件尚未完成前，不创建空目录并命名为 FullSim。具体结构和桌面验收继续遵守[团队发包指南](team-development-and-release.md)、[测试流程](adaptive-test-workflow.md)和[Windows 验收方案](windows-acceptance.md)。

# Gaode 制冷机零件缺陷检测软件

技术及测试以 [总体架构 V1.3](docs/architecture/architecture-v1.3.md) 为准；
首版界面以用户最新 [原型核查记录](docs/ui-prototype/review.md) 为准。

当前具备可运行后端、普通零件整盘计划预览、旧协议独立虚拟PLC最小联调、进程内流程框架及候选测试入口。
**尚未实现正式V1.3设备接入、完整检测闭环、WPF、算法和业务保存；生产就绪为 false。**
V1.3 先交付[整体框架](docs/architecture/v13-framework-map.md)和[架构书测试映射](docs/architecture/v13-acceptance-map.md)，后续每个工位独立实现、测试、修正并提交 PR。框架 Host 只报告模块/能力状态，未实现的任务准备请求明确拒绝，不会启动设备。

## 开发与测试

安装 .NET SDK 10.0.401，从仓库根目录运行：

```sh
dotnet restore Gaode.slnx --locked-mode
dotnet build Gaode.slnx -c Release --no-restore
dotnet test Gaode.slnx -c Release --no-build
dotnet run --project backend/src/Inspection.Host --no-launch-profile
```

访问 `http://127.0.0.1:5000/health/live`、`/api/system/status`、`/api/engineering/demo-plan`；`POST /api/jobs/prepare` 在框架期返回 501 和 `CAPABILITY_NOT_IMPLEMENTED`。
默认 2 件、2 面、每面 A/B 各一张，在 Host/appsettings.json 可替换；仅计划预览，不执行动作。

详见 [运行指南](specs/001-engineering-foundation/quickstart.md)、
[实际验证记录](specs/001-engineering-foundation/validation.md)、[GitHub Actions](https://github.com/NickYoung618/x/actions)。

## 团队与后续开发

- 前端同学：Vue、WPF/WebView2、页面与桌面测试。
- 虚拟下位机同学：独立模拟器、设备协议与对接测试。
- 用户：其余中台、流程、算法、数据、数据库部署和工程 CI。

[已确认决策](docs/decisions.md) · [普通整盘全虚拟闭环路线](docs/ordinary-tray-roadmap.md) ·
[虚拟设备契约草案](docs/contracts/virtual-device-v0.1-draft.md) ·
[CI 与完整测试矩阵](docs/testing.md) · [实际 Windows 部署/流程验收](docs/windows-acceptance.md)

最新协作入口：[三人协作、Spec Kit、CI 与 Windows 部署包](docs/team-development-and-release.md)。
同服务器新对话从 [项目交接](docs/handover-20260917.md) 和 [可复制提示词](docs/prompts/next-codex-session.md) 开始。
2026-09-17 已取得并审查同学的虚拟下位机副本，前端仍为 HTML 原型；
旧协议兼容联调已加入工程测试，正式 V1.3 接入差异见 [审查记录](docs/reviews/virtual-plc-20260917/review.md)。

## Spec Kit

已使用官方 v1.0.7 初始化 Codex skills（`.agents/skills/speckit-*`）。
原则在 `.specify/memory/constitution.md`；第一个规格位于 `specs/001-engineering-foundation/`。
在该仓库打开 Codex 可使用 `$speckit-specify`、`$speckit-plan`、`$speckit-tasks`、`$speckit-implement`。
每个后续功能按规格先明确可验收行为，再写计划、任务及实现；不要将当前基础特性当成完整首版已完成。

如需恢复当前特性的本机指针，创建未跟踪文件 `.specify/feature.json`：

```json
{"feature_directory":"specs/001-engineering-foundation"}
```

## CI 与部署入口

- `ci.yml`：每次 PR/main push，两平台还原、构建、测试和发布进程检查，上传报告及基础 Host。
- `windows-staging.yml`：手动输入成功 main Core CI 的运行 ID，校验来源后将相同 Windows 包
  下载到自有 `gaode-staging` runner 的独立目录，执行实际进程检查。
- 当前该 self-hosted runner 尚未注册；先按 Windows 文档配置环境，再触发部署作业。
- 完整 WPF/虚拟设备/SQLite 流程验收尚待对应实现接入，不能用基础 Host 冒烟替代。


## 虚拟下位机框架（002）

复用已校验的独立模拟器，详见[来源](simulator/README.md)、[本轮规格](specs/002-virtual-plc-integration/spec.md)和[接口差异](docs/contracts/virtual-plc-alignment.md)。
安装global.json指定SDK和Python3.12+后，在仓库根运行：

```sh
python3 scripts/validate-virtual-plc.py
```

Windows可用`python`。统一入口无.NET8回退，输出唯一运行目录及summary.json。
执行原13组旧协议检查、真实Host工程探针与独立PLC进程的最小联调。默认Host保持计划预览，不自动运动。
新版点表由下位机同学后续确认；本轮先搭框架。这里不是整盘执行、V1.3完整协议或Windows FullSim包。

## 普通整盘中台流程框架（003）

[规格与验证](specs/003-ordinary-workflow-foundation/validation.md)覆盖公共3D/F准备、唯一方案、A/B分面顺序、翻面重扫、单运动通道及采集/算法技术终态。用`dotnet test Gaode.slnx -c Release`运行；默认Host仍仅提供计划预览。正式设备、保存/判定/分拣待后续增量接入。

## 下位机候选版本测试入口（004）

当前统一脚本可随新增测试工程对账TRX，并可对同学独立发布的旧协议兼容模拟器运行真实Host↔模拟器联调。使用命令、需要回传的证据及V1.3边界见[同学运行指南](specs/004-plc-candidate-testkit/quickstart.md)。这是工程测试包，不是正式整盘或Windows安装包。

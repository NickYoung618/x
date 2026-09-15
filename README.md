# Gaode 制冷机零件缺陷检测软件

技术及测试以 [总体架构 V1.3](docs/architecture/architecture-v1.3.md) 为准；
首版界面以用户最新 [原型核查记录](docs/ui-prototype/review.md) 为准。

当前增量：可运行后端、普通零件整盘采集计划预览、基础测试和 CI。
**尚未实现完整检测闭环、WPF、真实/独立虚拟设备对接、算法和业务保存；生产就绪为 false。**

## 开发与测试

安装 .NET SDK 10.0.401，从仓库根目录运行：

```sh
dotnet restore Gaode.slnx --locked-mode
dotnet build Gaode.slnx -c Release --no-restore
dotnet test Gaode.slnx -c Release --no-build
dotnet run --project backend/src/Inspection.Host --no-launch-profile
```

访问 `http://127.0.0.1:5000/health/live`、`/api/system/status`、`/api/engineering/demo-plan`。
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

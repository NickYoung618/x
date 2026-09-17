# 框架验收入口

本机 SDK 使用 `global.json` 的 10.0.401，正式命令仍从仓库根目录运行：

```sh
dotnet restore Gaode.slnx --locked-mode
dotnet build Gaode.slnx -c Release --no-restore
dotnet test Gaode.slnx -c Release --no-build
python3 scripts/validate-virtual-plc.py
```

发布的 Host `GET /api/system/status` 应包含 15 个模块、`runtimeMode=Unconfigured`、`productionReady=false`；`POST /api/jobs/prepare` 必须 501 和 `CAPABILITY_NOT_IMPLEMENTED`。旧 V6 双进程脚本继续通过，但仍只表示旧兼容档案。V1.3 工位、完整整盘、真实算法/Windows 桌面和真机保留 NOT RUN。

最小 UI 用 Node 24.12+，先运行 Host，再在 `frontend/` 执行：

```sh
npm ci
npm run typecheck
npm test
npm run build
npm run dev
```

Vite 的 `/api` 代理指向本机 Host 5000。页面只读，断线显示错误；`npm run build` 检查入口资源仅引用本地资产。实际浏览器与 WPF 打包由后续前端增量验收。

后续每工位按 [验收映射](../../docs/architecture/v13-acceptance-map.md) 选取本阶段用例，保存输入、独立设备轨迹、Host 状态/日志、首次失败与修正后复测结果，并在单独 PR 评审。不能只把 Spec Kit task 打勾当作测试通过。

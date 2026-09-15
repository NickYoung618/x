# 基础工程验证

前置：Git、.NET SDK 10.0.401。首次 restore 需要 NuGet 网络，离线交付包另行制作。

仓库根目录：

```sh
dotnet restore Gaode.slnx --locked-mode
dotnet build Gaode.slnx -c Release --no-restore
dotnet test Gaode.slnx -c Release --no-build --logger trx --results-directory artifacts/test-results
dotnet run --project backend/src/Inspection.Host --no-launch-profile
```

另开终端访问 `http://127.0.0.1:5000/health/live`、`/api/system/status` 和 `/api/engineering/demo-plan`。
预期存活、生产未就绪，以及仅预览的 8 次采集计划。Linux 可用 curl，Windows 可用 Invoke-RestMethod。

修改 `backend/src/Inspection.Host/appsettings.json` 中 DemoTray 的零件/面配置并重启，可验证计划替换。
无效配置必须启动失败。演示标识不可复用为生产条码或真实坐标。

发布与真实进程检查（PowerShell 7，两平台通用）：

```powershell
dotnet publish backend/src/Inspection.Host -c Release --no-restore -o artifacts/host
pwsh -File scripts/smoke-host.ps1 -HostDirectory artifacts/host
```

发布输出依赖目标机器安装兼容的 .NET 10 ASP.NET Core Runtime；不是完整离线设备安装包。
CI 验证核心跨平台，不验证 WPF 桌面运行、WebView2、相机 SDK、GPU 或真实机械。

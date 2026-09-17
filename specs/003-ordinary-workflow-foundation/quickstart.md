# Quickstart / validation

环境：仓库`global.json`的.NET SDK 10.0.401。本机可使用`/home/ubuntu/.local/share/gaode-dotnet-10.0.401/dotnet`。

```sh
dotnet restore Gaode.slnx --locked-mode
dotnet build Gaode.slnx -c Release --no-restore
dotnet test Gaode.slnx -c Release --no-build --logger trx --results-directory artifacts/workflow-tests
```

重点核对`Inspection.Application.Tests`里的两件两面AB顺序、初扫→F→方案、翻面重扫、普通缺帧、乱序/迟到、未知动作和截止终态。默认Host仍报告`productionReady=false`；本轮未从Host启动检测。

# Validation quickstart

在仓库根目录，使用 `global.json` 指定的 .NET 10 SDK：

```bash
dotnet restore Gaode.slnx --locked-mode
dotnet build Gaode.slnx -c Release --no-restore
dotnet test Gaode.slnx -c Release --no-build
```

定向测试：`dotnet test backend/tests/Inspection.Infrastructure.Tests/Inspection.Infrastructure.Tests.csproj -c Release --no-build --filter FullyQualifiedName~Protocol20260911Tests`。该测试用独立 TCP 服务模拟新点表，检查原始 FC01/03/05/06/16 请求、Float32 与设备反馈；它不是已交付的完整虚拟 PLC。旧 V6 联调按 `scripts/validate-virtual-plc.py` 单独执行和命名。Host `--plc-device-check` 在新合同下只读，必须显式提供 `Plc:Contract`、`AddressConvention`、`Float32ByteOrder`、`CoordinateFrame`、`Unit`、`Host`、`Port`、`Report`，真实端不得开启 `AllowVirtualActions`。

Host 对独立测试服务的真实 Modbus 只读联调：`python3 scripts/check-plc-20260911-host.py --dotnet /path/to/dotnet`；预期 4 次 FC01/FC03 读取、0 次动作写入，报告显示三轴坐标、面号和报警。

新协议正式模拟器、Windows 真机和整盘测试均要另行记录；不能由上述定向测试推断通过。

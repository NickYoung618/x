# 下位机同学运行指南

从本仓库根目录运行。需要 `global.json` 指定的 .NET SDK 10.0.401、Python 3.12+。先用当前仓库内模拟器验证环境：

```sh
python3 scripts/validate-virtual-plc.py
```

若要测试你另行发布的**旧协议兼容**模拟器，先在自己的源码目录发布 net10.0 并保留其提交号/构建日志，再运行：

```sh
python3 scripts/validate-virtual-plc.py --candidate-dll /absolute/path/to/publish/VirtualPlc.dll --contract legacy-v6-u16-snapshot-20260917
```

本机 SDK 不在 PATH 时加 `--dotnet /absolute/path/to/dotnet`。测试会创建唯一证据目录，正常需要四个测试程序集、旧协议13组、五个双进程场景全部通过。发给中台评审时附 `summary.json`、Host 报文、设备日志/状态、候选 DLL 哈希和候选源码构建记录。先看 `summary.json` 的 `error`、`candidate`、`coreTests`、`legacy`、`integration`、`v13FullContract`、`fullTray`。

如果你的新版本改为 V1.3 点表，**不要用旧兼容档案判定它**。将点表和动作/反馈语义填入 [接口差异表](../../docs/contracts/virtual-plc-alignment.md)并提交对应适配及独立 V1.3 测试；此入口目前会拒绝未声明的合同。

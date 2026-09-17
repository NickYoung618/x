# Quickstart

前提：global.json指定.NET SDK10.0.401、Python3.12+；无需.NET8。NuGet只在恢复现有中台依赖时需要。
本机SDK未在PATH时：`export PATH=/home/ubuntu/.local/share/gaode-dotnet-10.0.401:$PATH`。

```sh
python3 scripts/validate-virtual-plc.py
```

脚本生成simulator/artifacts/<唯一运行ID>/，执行locked restore、Release构建/测试、两个Host的发布；运行原13组及独立中台进程场景，保存summary.json、原始报文与设备日志。任何失败返回非零；失败目录保留，不复用旧产物。
CI使用同一命令，证据按平台上传；不产生FullSim发布包。

独立基础回归：`dotnet test Gaode.slnx -c Release --logger trx`。

旧协议13组、兼容中台联调、V1.3完整契约和整盘验收分列；后两项未实现不能计PASS。

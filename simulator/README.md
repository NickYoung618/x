# 独立虚拟 PLC

来源与原始逐文件摘要见source-manifest.json；无有效上游Git提交，按归档SHA标识。
VirtualPlc内保留同学原文档、规格、报告、脚本；旧README/validate.sh属于历史用法，可能回退net8，不作为本仓库验收入口。

当前入口从仓库根运行：`python3 scripts/validate-virtual-plc.py`。固定.NET10，实际生产构建失败即失败。
原13组断言原样保留，驱动使用net10.0/C#12；POLICY-01是旧策略，不表示V1.3业务通过。
主程序不引用中台；中台只经Modbus控制，测试布置及状态读取用HTTP。

## 给下位机同学的独立候选测试

将候选模拟器发布为独立的`net10.0`目录，保留源码提交及构建日志。当前只支持明确声明的旧V6单寄存器合同：

```sh
python3 scripts/validate-virtual-plc.py --candidate-dll /absolute/path/to/publish/VirtualPlc.dll --contract legacy-v6-u16-snapshot-20260917
```

脚本只在本机回环地址启动该候选，运行原13组及中台真实Modbus客户端的5个联调场景，生成独立目录`simulator/artifacts/<run-id>/`。`summary.json`记录候选DLL与整个发布目录哈希、全部四个中台测试程序集、设备动作轨迹检查和未运行的V1.3/整盘项。候选源码构建另由提供者给出；本入口的仓库构建成功不等于候选构建已验证。详见[004测试指南](../specs/004-plc-candidate-testkit/quickstart.md)。新V1.3点表尚未确认，不使用本旧合同测试它。

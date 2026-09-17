# 独立虚拟 PLC

来源与原始逐文件摘要见source-manifest.json；无有效上游Git提交，按归档SHA标识。
VirtualPlc内保留同学原文档、规格、报告、脚本；旧README/validate.sh属于历史用法，可能回退net8，不作为本仓库验收入口。

当前入口从仓库根运行：`python3 scripts/validate-virtual-plc.py`。固定.NET10，实际生产构建失败即失败。
原13组断言原样保留，驱动使用net10.0/C#12；POLICY-01是旧策略，不表示V1.3业务通过。
主程序不引用中台；中台只经Modbus控制，测试布置及状态读取用HTTP。

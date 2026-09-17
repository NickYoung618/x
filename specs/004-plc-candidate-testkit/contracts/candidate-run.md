# 候选联调入口合同

```text
python3 scripts/validate-virtual-plc.py
python3 scripts/validate-virtual-plc.py --candidate-dll /absolute/path/VirtualPlc.dll --contract legacy-v6-u16-snapshot-20260917
```

第二种模式只适用于旧 V6 快照兼容模拟器。候选目录须含 `VirtualPlc.dll`、相邻的 `VirtualPlc.runtimeconfig.json` 及运行依赖；目标框架须是 net10.0。入口不会连接外部地址，而是由脚本在回环随机端口启动候选，并在结束时关闭它。候选自身构建由提供者留存记录。

输出在 `simulator/artifacts/<run-id>/summary.json`；`candidate` 指向输入二进制的路径、DLL及发布目录哈希、目标框架以及“候选源码构建未由本入口验证”。旧驱动的“生产构建”门禁在候选模式中亦为 NOT RUN，不能借仓库内置模拟器的构建冒称候选构建。候选发布目录应只含本次包文件，避免动态日志改变哈希。日志含实际 Host 命令、报文和模拟器独立状态。退出码0仅表示本次旧协议框架检查通过；V1.3 与整盘保持 NOT RUN。非零退出仍写总结和已得到的日志。

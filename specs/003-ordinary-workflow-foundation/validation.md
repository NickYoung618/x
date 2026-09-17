# 003 中台流程框架验证

日期2026-09-17。基线85dafc3（002虚拟PLC框架）；当前功能分支`003-ordinary-workflow-foundation`。本轮终点为进程内`AwaitingDecision`，没有生产启动入口或客户可用整盘闭环。

## 改变与证据

- `TrayWorkflow`采用同步事件→效果的短处理；初始3D→F扫码→唯一方案冻结后才发首个定位运动。每次运动只按当前OperationId处理，Accepted不等于Completed。
- 原`OrdinaryTrayPlanner`负责面/相机/件顺序。测试得到A1/A2/B1/B2、逐件2次翻面、重扫、A1/A2/B1/B2共8次检测采集；新扫描使CoordinateEpoch从1变2，扫描期间为0，PartId保留。
- `MotionExecutionLane`单设备、有界队列；只允许一个设备动作执行，重复OperationId拒绝，动作等待有期限。未知时锁定后续动作，不重发。端口没有绑定旧V6寄存器或假定新V1.3点表。
- 普通缺帧进入CaptureFailed/AlgorithmDependencyFailed而继续；算法失败、截止超时、乱序/迟到结果按CaptureKey/Attempt收敛。最终状态`AwaitingDecision`只含技术事实，不输出OK/NG/Pending或放行。

## Linux实际执行

环境：Linux x64；.NET SDK10.0.401。工具路径`/home/ubuntu/.local/share/gaode-dotnet-10.0.401/dotnet`。命令从仓库根执行：

```sh
/home/ubuntu/.local/share/gaode-dotnet-10.0.401/dotnet restore Gaode.slnx --locked-mode -m:1 -nr:false
/home/ubuntu/.local/share/gaode-dotnet-10.0.401/dotnet build Gaode.slnx -c Release --no-restore -m:1 -nr:false
/home/ubuntu/.local/share/gaode-dotnet-10.0.401/dotnet test Gaode.slnx -c Release --no-build --logger trx --results-directory artifacts/workflow-tests-final-3
```

最终结果记录于[evidence/](evidence/)中的`restore-locked.txt`、`build-final.txt`、`tests-final.txt`和`trx-final/`四个TRX。构建0警告0错误；四个测试程序集共42/42通过、0失败0跳过（Application新增12，Domain15，Host4，Infrastructure11），逐项见最终日志和四份TRX。并行默认构建随后重跑也成功，见`evidence/build-parallel-recheck.txt`。未运行Windows和GitHub新CI；不能引用上一提交的CI作为本次结果。

## 失败尝试与处理

1. 首次并行`dotnet build Gaode.slnx -c Release --no-restore`返回MSB4166，子节点提前退出；临时诊断目录随后已不存在，不能确定是内存/进程还是其他外部原因。单构建进程可重复执行后暴露真实编译错误；最终相同源码的默认并行构建也通过，因此此MSB4166未复现，保留未决而不把它当源代码根因。[原日志](evidence/build-first-failure.txt)。
2. 单进程首轮编译因xUnit2013/xUnit2031两处测试断言风格与TreatWarningsAsErrors失败；只改断言，下一轮构建通过。[日志](evidence/build-analyzer-failure.txt)。
3. 首次完整测试卡在新运动期限用例；假设备忽略CancellationToken，通道原本只传令牌无法强制返回。测试中断，不能计通过。改为通道自有`WaitAsync(deadline.Token)`，到期Unknown并锁通道；9项Application测试随后通过。[首次日志](evidence/tests-timeout-first.txt)、[修后](evidence/application-tests-after-timeout-fix.txt)。
4. 复核发现非法枚举值原本可能落入Completed分支，已将非明确Completed结果视为失败，并补反例；方案快照在计划有效后才冻结。最终构建/全量测试重新执行。
5. 结项前发现事件接收端若抛异常，通道原本会停止消费者但继续受理队列；修订为锁住通道、公开异常并拒绝新动作，补了接收端故障测试。最终42项再次全量通过。

## 验收分层与剩余边界

| 层次 | 本轮结果 |
| --- | --- |
| Domain/Application接口级测试 | 已实际执行，验证顺序、身份、坐标批次、异常终态和单动作通道 |
| 旧协议13组/真实Modbus最小联调 | 002增量独立证据；本轮未改模拟器/PLC适配，也未重跑两进程套件 |
| V1.3完整上下位机对接 | 未实现：正式点表、受理/完成关联、恢复仍需下位机同学确认 |
| 普通整盘全虚拟 | 未实现：扫码/3D/相机/算法真实端口、保存、质量/分拣、放行、Windows桌面 |

本轮使用的是进程内假端口。运动结束后不会自动恢复Unknown；没有持久意图/反馈，进程重启后不能对账。翻面后重扫当前仅允许原槽位集合保持一致，真实工件跨槽位映射需与设备/工艺方确认。帧引用在此仅为测试字符串，没有媒体落盘或容量预留。

下一增量可在不触碰PLC新点表的条件下增加Host中的真实任务用例、独立3D/F/相机/算法模拟适配、容量预留与关键保存接口；当下位机同学确认契约，再将Motion端口接入真实协议并做跨进程整盘验证。

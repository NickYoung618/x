# 002 框架增量验证

2026-09-17，Linux x64（内核6.8.0-136、glibc2.39），SDK10.0.401，运行时10.0.12。
范围依据用户后续确认：先搭框架；上下位机正式接口决定交下位机同学处理，本轮不等待、不代其冻结。

## 本轮结论

**框架/旧协议最小联调通过；V1.3完整协议与普通整盘没有通过，也未执行。**

| 层次 | 实际结果 | 证据 |
| --- | --- | --- |
| 生产目标构建/发布 | net10.0 Release 0警告0错误；真实发布模拟器、Host、驱动 | [build](evidence/20260917T031858Z-0003f9be/build.txt)、publish-*.txt |
| 原基础规则/HTTP | 19/19（Domain15、Host4） | [test log](evidence/20260917T031858Z-0003f9be/core-tests.txt)、同目录trx/ |
| 新中台通信测试 | 11/11；已知独立字节、分包、错误事务/协议/Unit/功能/长度、异常、EOF、超时、写坐标 | 同上；并非11个整盘场景 |
| 原模拟器旧协议检查 | 13/13；测试Program.cs逐字未改，net10.0/C#12驱动 | [legacy.json](evidence/20260917T031858Z-0003f9be/legacy.json) |
| 中台真实客户端↔独立模拟器 | 5/5场景，见下表 | [summary](evidence/20260917T031858Z-0003f9be/summary.json)、Host完整请求响应JSON、独立状态和设备日志 |
| 默认发布Host | 实际另进程启动，live、productionReady=false、8次计划采集且executionEnabled=false | [smoke](evidence/20260917T031858Z-0003f9be/default-host-smoke.json) |
| V1.3完整线上对接 | NOT RUN：新点表/Float32、会话序号、完整取放、停止恢复未定 | [差异与责任](../../docs/contracts/virtual-plc-alignment.md) |
| 普通整盘 | NOT RUN：无3D/F扫码、8次真实采集、翻面重扫、算法/保存/放行闭环 | 保持原计划预览范围 |
| Windows及GitHub Actions | 本轮未执行；已配置同一脚本的Ubuntu/Windows矩阵 | .github/workflows/ci.yml；不借用旧CI成功结果 |

## 双进程实际结果

| 场景 | Host终态 | 独立设备证据 |
| --- | --- | --- |
| normal | Completed；每次先观察新Busy；两次目标123/456/78→321/654/87 | Move启动2次，最终XY321/654，锁紧状态1 |
| reconnect-stale | RecoveryRequired，只有读请求 | 前一场景设备状态不清除；无新增Move |
| device-failure | Failed；原MoveTimeout注入实际返回XY=2/Z=3，是设备明确失败 | Move启动1次，无重发/下一动作 |
| action-timeout | Unknown；动作时长5000ms，客户端等待700ms到期 | Move启动1次；未宣称设备已停 |
| reconnect-unknown | RecoveryRequired，只有读请求 | 无新增Move；随后仅测试清理结束模拟器进程 |

正常通信只用Modbus；脚本HTTP用于health、fault布置及state证据。中台不引用模拟器源码、不调用flow-decision。
独立设备“action started”日志可支持本轮Move次数核对，但没有持久会话号/完整位置历史，不能据此宣布V1.3跨重启去重完成。
旧POLICY-01继续检验扫码/保存失败随机兜底，属于旧断言；其通过不是V1.3业务失败策略通过。
兼容新Busy握手依赖固定10ms扫描及可观察Busy；没有观察到则Unknown，不推定完成。
没有测试真实PLC、丢完成后的物理翻面/分拣、运动停止优先级、现场安全或节拍。

## 执行命令与所有尝试

统一可复现入口：

```sh
export PATH=/home/ubuntu/.local/share/gaode-dotnet-10.0.401:$PATH
python3 scripts/validate-virtual-plc.py
```

实际用等效`--dotnet /home/ubuntu/.local/share/gaode-dotnet-10.0.401/dotnet`。
脚本日志保留每条完整命令：locked restore → Release build → dotnet test → publish → 原驱动 → Host工程探针。
每次新目录，退出码、13组数量、三份TRX实际30项结果均检查。构建/测试未运行不能通过。

| 尝试 | 实际结果 | 处理 |
| --- | --- | --- |
| 初始开发restore/build | PASS，生成新增锁文件，0警告0错误 | 随后统一入口locked restore再次通过 |
| [031744首次统一入口](evidence/20260917T031744Z-38ba64c2/summary.json) | 构建及30测试成功；驱动publish MSB1050，旧13组/联调NOT RUN，整体FAIL | 原目录两个csproj，目录式选择有歧义；改为明确SystemValidation.csproj |
| [031858修正后](evidence/20260917T031858Z-0003f9be/summary.json) | 构建/30测试/旧13组/5场景全部PASS | 所有本次子进程已停止 |
| [032016负向门禁](evidence/20260917T032016Z-e624d6b2/summary.json) | 指定不存在SDK，返回1；build/tests全部NOT RUN，整体FAIL | 预期拒绝，无子进程；证明不回退或使用旧产物 |
| 发布Host默认模式冒烟 | PASS | 单独Python子进程检查3个GET，随后清理 |

首次失败归因：命令未明确项目产生歧义；编写时漏核同目录CompatibilityHost；运行门禁正确检出并停止，没有把后续NOT RUN标为PASS；修正项目路径后已以完整重跑验证。
本轮未发现需重试才能掩盖的设备失败；设备失败/超时是有意测试的预期终态。

## 修改范围和可追溯性

- `simulator/VirtualPlc/`：36个原始文件有来源摘要，保留同学原规格/报告。原global.json更名global.upstream.json，继承根目录精确SDK；仅测试csproj改net10/C#12。主程序及原测试Program.cs未改。
- `backend/src/Inspection.Infrastructure/`、`backend/tests/Inspection.Infrastructure.Tests/`：新实际Modbus客户端、旧协议工程适配和11项测试；不引入额外NuGet通信库。
- `backend/src/Inspection.Host/Program.cs`及csproj/锁文件：显式`--plc-probe`模式；正常模式仍是工程基础。
- `Gaode.slnx`、`scripts/validate-virtual-plc.py`、`.github/workflows/ci.yml`：构建、隔离进程、结果门禁与双平台CI配置。
- `specs/002-virtual-plc-integration/`、`docs/contracts/virtual-plc-alignment.md`、`docs/update-goals.md`、`docs/testing.md`、README：规格/计划/15任务/契约/证据/使用。
- 运行时HEAD为f6f7078且工作区有本轮修改，报告如实标dirty；[实测输入哈希](evidence/tested-inputs.json)标识已测试的源码/项目/脚本，无伪造干净提交测试声明。
- 归档SHA：0780781b7c09f44b6cbf547181094ed445dc06cc87cebdb82e23e9f5e5814010。先校验，再解压到`/home/ubuntu/dzk/gaode/virtual-plc-002-vnl2yu6i`；最终逐文件比对仅上述csproj不同。

## 下一增量

先在Application建立Workflow/Motion/采集/算法端口及任务终态、资源互斥、坐标批次框架，使用明确的接口级模拟验证流程，保持设备协议可替换。
下位机同学确认新契约后，再接会话/动作反馈、完整取放与独立轨迹。随后实现3D→F选方案→2件2面AB→翻面重扫→判定/关键保存闭环。
接口级测试、实际协议联调、Windows包与整盘验收继续分开记录；本轮没有FullSim包或PR/发布。

静态核查：本轮新增/修改代码与文档（排除逐字保留的上游目录）git diff --check通过；上游两份Markdown共12处行末双空格为原有换行写法，保留原字节。首次清单脚本因git默认对中文路径加引号误报缺文件，改用ls-files -z后36项全部匹配；不是源文件遗漏。测试输入摘要复核全部一致。

# 004 下位机候选测试包验证

日期：2026-09-17。Linux x64，.NET SDK 10.0.401。分支 `004-plc-candidate-testkit`，基线提交 `542c894`（003流程框架）；验证时工作区含本轮尚未提交的改动。被测最终脚本 SHA-256 为 `4bfb06b5fdaa883f004b27cc0fc5513abae88bbc37547d3786a4e2649bd55948`。Python门禁测试脚本 SHA-256 为 `9890a2a581cae3f8fd9fe078db9222d0f13891f6d61ec142a19ade9a74b1870d`。

## 已完成的范围

- `Gaode.slnx`目前四个正式测试程序集。统一入口按解决方案枚举，逐份TRX读取实际程序集与计数，少测、重复、跳过或失败都拒绝；不再固定旧的“三份”。
- 候选模式必须同时给出本机`VirtualPlc.dll`和精确旧合同；检查相邻runtimeconfig为net10.0，记录DLL和整个发布目录哈希。脚本启动候选软件模拟器的回环端口，正常通信仍走中台真实Modbus客户端。
- 原13组旧检查、5个双进程场景及设备独立动作日志继续作为旧协议兼容回归。正式V1.3点表与整盘保持NOT RUN。
- CI `virtual-plc`作业增加四项门禁单元测试；PR Ubuntu/Windows 运行结果见下文。候选DLL参数路径仅在Linux本地执行，CI默认仍测试仓库快照。

## 实测结果

| 运行 | 命令/输入 | 结果与证据 |
| --- | --- | --- |
| Python门禁 | `python3 -m unittest discover -s scripts/tests -p 'test_*.py' -v` | 4/4通过：正常四工程、缺失、重复、跳过/失败；[日志](evidence/gate-unit-tests.txt) |
| 默认基线最终代码 | `python3 scripts/validate-virtual-plc.py --dotnet /home/ubuntu/.local/share/gaode-dotnet-10.0.401/dotnet` | 仓库源locked restore/Release构建/发布PASS；四工程42/42、旧13/13、双进程5/5；生产构建门禁PASS。[总结](evidence/default-summary.json)、[旧检查门禁](evidence/default-legacy.json) |
| 外部候选最终代码 | 同命令加`--candidate-dll /home/ubuntu/dzk/gaode/repo/simulator/artifacts/20260917T041902Z-ac796b0f/plc/VirtualPlc.dll --contract legacy-v6-u16-snapshot-20260917` | 仓库源构建/发布PASS；四工程42/42、旧13/13、双进程5/5；候选源码构建门禁NOT RUN、无跳过、所有子进程停止。[总结](evidence/candidate-final/summary.json)、[旧检查门禁](evidence/candidate-final/legacy.json)、[Host报文](evidence/candidate-final/normal.json)、[独立设备状态](evidence/candidate-final/normal-device.json)、[设备日志](evidence/candidate-final/normal-simulator.txt)、[TRX](evidence/candidate-final/trx/) |
| 未声明合同 | 候选DLL但无`--contract` | 返回1、动作前失败；[总结](evidence/reject-missing-contract.json) |
| 缺少候选文件 | 不存在的DLL路径及旧合同 | 返回1、未回退仓库模拟器；[总结](evidence/reject-missing-dll.json) |
| 冒称V1.3 | 候选DLL及`--contract v1.3` | 返回1、动作前拒绝；[总结](evidence/reject-v13-as-legacy.json) |

候选来自默认基线运行中真实发布的独立模拟器，用作已知旧协议正样本；DLL SHA-256 `60004ac00a026b26a28544c0f2a0bed031fa0e44f5aee7ef67bb4e20626d4166`，17文件发布目录哈希 `eb77497b547e02e08f36904d621b4f5a6776d4da9e81ac09f1ac38d457f3206c`。候选测试入口**没有验证候选源码自身的构建过程**；本例可由默认基线的`publish-plc`日志旁证，外部同学须另附其构建记录。

## GitHub PR CI

- [草稿PR #1](https://github.com/NickYoung618/x/pull/1)以`002-virtual-plc-integration`为head、`main`为base。[运行35182340667](https://github.com/NickYoung618/x/actions/runs/35182340667)四作业均SUCCESS；下载的两平台虚拟PLC证据分别记录30/30测试、旧13/13、双进程5/5。其余Core作业还完成发布Host冒烟。
- [草稿PR #2](https://github.com/NickYoung618/x/pull/2)以`004-plc-candidate-testkit`为head、002为base。[运行35182357122](https://github.com/NickYoung618/x/actions/runs/35182357122)四作业均SUCCESS；下载的Ubuntu/Windows `summary.json`分别记录SDK10.0.401、四工程42/42、旧13/13、双进程5/5、子进程已停止，V1.3和整盘仍为NOT RUN。CI检出的是PR合成提交`f369617`，父提交含002的`85dafc3`和004的`91758e2`；合成提交树`7e2d6c5`与本地004提交的树相同。
- 下载的四份CI总结与旧驱动报告，以及004两平台的TRX，保存在[evidence/ci/](evidence/ci/)；Windows文本副本仅将CRLF规范为LF以便Git核查，原字节仍在GitHub运行产物中。GitHub原运行另提供完整设备日志和Host报文。002的[Ubuntu](evidence/ci/002/ubuntu/summary.json)/[Windows](evidence/ci/002/windows/summary.json)与004的[Ubuntu](evidence/ci/004/ubuntu/summary.json)/[Windows](evidence/ci/004/windows/summary.json)总结可长期对照。
- GitHub 托管 `windows-2022` 作业不是客户实际Windows电脑的交互桌面测试。PR仍为草稿、未合并；本次CI与下载证据证明代码分支测试，不证明发布包已验收。

完整原运行目录分别在`simulator/artifacts/20260917T041902Z-ac796b0f/`（最初默认）、`20260917T042018Z-37316d16/`（最初候选）、`20260917T042234Z-3d7d259e/`（增补目录哈希后的候选）、`20260917T042649Z-9e242fe2/`（最终候选）、`20260917T042741Z-1030510f/`（最终默认）及三个拒绝目录；本规格的`evidence/`保存可入库的关键日志和TRX。

复核增补目录哈希后的候选报告时发现：旧驱动被传入`Production .NET 10 build=PASS`，但该构建只覆盖仓库内置模拟器，不能证明候选源码已构建。产生环节是原脚本复用了固定`PASS`参数；未防止环节是候选模式新增时没有按被测对象调整门禁；复核发现后将候选门禁改为`NOT RUN`、模式标为`candidate-legacy`并核对驱动输出。旧报告见[修正前总结](evidence/candidate-pre-gate-fix/summary.json)与[门禁](evidence/candidate-pre-gate-fix/legacy.json)；最终候选和默认模式均完整重跑，分别得到`NOT RUN`与`PASS`。这不改变旧协议13组行为检查的通过事实，但纠正了构建证据归属。

## 未覆盖与下一步

- V1.3正式点表、Float32字序/单位、会话序号、停止及重连对账、翻面与完整取放参数仍按[差异表](../../docs/contracts/virtual-plc-alignment.md)由下位机同学确认后接独立测试档案；旧V6主动探针不能测试新表。
- 当前独立设备日志只能检查本轮Move启动次数；缺持久会话/动作序号，不能证明跨重启严格去重。003的Application测试是假端口；Host仍无生产整盘启动、质量判定、关键保存、分拣/放行及Windows FullSim。
- 本轮候选DLL模式只在Linux本地实测；仓库快照模式在GitHub Ubuntu/Windows矩阵已运行，真实Windows交互电脑仍未验收。候选DLL的来源和整个发布目录哈希能识别输入，不能单独证明同学的源码构建或生产硬件行为。

下一增量由下位机同学提交版本化接入点表和动作/反馈语义，在本测试包增加正式V1.3合同、独立设备动作轨迹与故障场景；中台再实现`IMotionDevice`正式适配并接到003流程框架。当前测试包可先供其旧协议基线回归和测试环境自检。

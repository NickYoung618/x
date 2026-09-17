# 新 Codex 对话可直接复制的提示词

请在**当前这台 Linux 服务器**继续高德缺陷检测软件中台项目。项目仓库是
`/home/ubuntu/dzk/gaode/repo`；`/home/ubuntu/disk/dzk` 属于另一个项目，勿在其中修改高德代码。
用户负责中台、算法调度、数据和 CI；另一位同学负责 Vue/WPF 前端；第三位同学负责虚拟下位机与对接。

你的第一项工作是**推进虚拟下位机接入的首个可评审增量**：
基于现有 Spec Kit 工程，整理并实现独立虚拟 PLC 的构建/测试接入，
核对它与 V1.3 的协议差异，并让中台真实 Modbus 客户端的最小双进程握手具备测试入口。
有实质性未决协议项时集中提问，同时推进不依赖这些决定的部分。

开始前请按顺序读取：

1. `git status`，保留已有工作；`docs/handover-20260917.md` 和 `docs/team-development-and-release.md`。
2. `docs/architecture/architecture-v1.3.md`、`.specify/memory/constitution.md`、`docs/decisions.md`。
3. `docs/reviews/virtual-plc-20260917/review.md`、`docs/contracts/virtual-device-v0.1-draft.md`、`docs/testing.md`。
4. `specs/001-engineering-foundation/`、`.github/workflows/ci.yml` 和相关中台代码。

同学的源码已保存在本机：
`/home/ubuntu/dzk/gaode/交接资料/VirtualPlc-20260917-source.tar.gz`。
SHA-256 是 `0780781b7c09f44b6cbf547181094ed445dc06cc87cebdb82e23e9f5e5814010`。
先核验哈希，再解压到隔离目录阅读；无需再次索取服务器密码。
审查报告中有本轮 .NET 10 发布、原有 13 组测试及失败策略的实际证据。

项目当前只有普通整盘**计划预览**和后端基础 CI。已有 CI 在 Linux、Windows 各通过 19 个基础用例；
虚拟 PLC 尚未与中台联调，前端仍是 HTML 原型，Windows 完整包尚不存在。
不要把历史测试的绿色结果写成普通整盘闭环或 V1.3 协议验收通过。

按已有 Spec Kit 流程创建接入规格、计划、任务和必要的契约对照；
在一个独立功能分支完成可分阶段评审的实现和真实测试。
尽量复用模拟器现有 Modbus、故障注入和测试代码。
新增 CI 应明确验证 .NET 10 主程序构建成功及测试实际运行，失败或 NOT RUN 不得作为发布通过。
联调时使用真实中台 Modbus 客户端连接独立模拟器；测试控制 HTTP 仅用于场景布置、故障注入和读取证据。
保留虚拟 PLC 自己的设备状态，不让它决定任务、检测面和质量结果。

请特别核对：旧 V6.0 点表的单寄存器 16 位坐标，与本地另一份协议的双寄存器 Float32 不同；
动作缺会话/序号/受理与完成关联；`/api/simulator/flow-decision` 对扫码、方案、保存失败
以及动作超时返回继续，违反 V1.3。不能让正式 Workflow 依赖这种随机兜底。
按 V1.3，首版整盘应先初始 3D、在 F 读取托盘码并结合场景匹配唯一检测方案，
再按两件、两面、每面 A/B 检测；翻面后重扫，原 PartId 保留。
涉及旧反馈、丢反馈、重连和分拣，需要独立设备轨迹核对实际执行次数。

完成本增量时请给出：

- 修改过的文件与对应规格/任务；
- 实际执行的构建、单元及两进程测试命令、平台、结果和证据位置；
- V1.3 差异中已解决项、仍需双方确认的少数关键决定；
- PR 或本地分支地址，以及下一增量的清晰边界。

不要将未实现的 Vue/WPF、扫码/3D/相机/算法、数据库和 Windows FullSim 包标为完成。
不要在仓库、报告或提示词里写入任何登录密码或密钥。

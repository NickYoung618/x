# 三人协作、Spec Kit、CI 与 Windows 部署包

更新：2026-09-17。本文是基于当前代码的执行方案；标为“建议”“待接入”的内容尚未实现。
架构与测试以 [V1.3](architecture/architecture-v1.3.md) 为准。

2026-09-17 顺序调整：先在独立 PR 建 [P0 整体框架](architecture/v13-framework-map.md)与[§18 验收映射](architecture/v13-acceptance-map.md)，再在首工位 Draft PR #4 及后续逐工位 PR 内实现具体功能。每阶段完成正常/故障/跨端口测试，保留失败和修正复测证据，评审合并后在 main 对同一提交回归。P0 不接设备动作、配方或算法；旧 V6 测试仅为回归基线。

## 1. 当前事实与下一条交付链

| 事项 | 当前状态及证据 |
| --- | --- |
| 中台 | 已有普通整盘 Application 内存流程、V1.3 十五模块/能力状态与默认拒绝合同；Host 仍无工位执行链，按 [工位交付顺序](station-delivery-order.md) 逐站接入 |
| 前端 | 原 HTML 原型仍是完整页面依据；已接入 Vue/TypeScript/Pinia 只读状态页，WPF 与完整业务页面未实现 |
| 虚拟下位机 | 已从获授权服务器的 `/home/ubuntu/disk/pj1/VirtualPlc` 取得独立审查副本；为 .NET 10 + Modbus TCP 实现，按旧 V6.0 资料开发；与 V1.3 存在差异，见 [接入审查](reviews/virtual-plc-20260917/review.md) |
| 接口资料 | 本地已有 2026-09-11《PLC与上位机通信接口协议.docx》；仓库已有 [虚拟设备 v0.1 草案](contracts/virtual-device-v0.1-draft.md)，无需重新起草整套协议 |
| CI | 合并后 main 的 [CI 35192471611](https://github.com/NickYoung618/x/actions/runs/35192471611) Linux/Windows Core、Framework UI、Virtual PLC framework 六作业成功；仍不覆盖正式 V1.3 PLC/工位与整盘 |
| 主分支 | PR #1/#2/#3/#5 已合并，当前框架基线 `d03068490de58ad7511cf692e805867bf8330d7b`；尚无首工位执行链 |
| 强制合并规则 | rulesets API 返回 403，明确要求升级 GitHub Pro 或公开仓库；尚未建立强制检查门槛 |
| 下载包 | Actions 提供基础 Host 产物，保留 14 天；没有完整软件包，Release 列表为空 |
| 自有 Windows 自动测试 | 仓库 runner 数量为 0；用户已经在另一台 Windows 电脑解压基础包，但尚未返回该机验收报告 |

后续每工位各有单独 PR 与验证记录。

推荐交付链：

```text
共同规格与接口版本
    → 各自功能分支开发
    → PR（合并请求）自动测试与另一位开发者评审
    → 合并 main
    → main 集成回归与 Windows 候选包
    → 使用该包在 Windows 桌面验收
    → 同一包发布至 GitHub Releases
    → 同学下载、解压、启动、测试并回传报告
```

PR 的测试包可以供开发联调，必须标注来源。正式提供团队使用的版本来自已验证的 main 提交。
不能拿旧提交的绿色结果批准新代码，也不能在验收后重新编译另一份包冒充同一产物。

## 2. 已有接口怎么沿用

本地原协议位置：`/home/ubuntu/dzk/gaode/文档/PLC与上位机通信接口协议.docx`。
2026-09-11 版本已经规定 Modbus TCP、点位和部分时序。V1.3 第 11 节明确要求对齐旧表，
所以“有约定”与“现有实现已经通过 V1.3 联调”是两项不同的事实。

2026-09-17 实现核对发现：同学的模拟器依据的是另一份 2026-09-08 V6.0 资料，
其坐标采用单个 16 位寄存器，而本地 2026-09-11 协议写的是 Float32/两个寄存器。
不能只比较名称就认为同一接口；接入应以 V1.3 的语义要求为准，明确本轮共同采用的点表版本。

本轮发现的具体核对项：

- 原协议第 1.7 节仍要求双方确认 Float32 字序；CI 必须能使用固定编码和已知数值验证。
- 原协议的到位/完成位，需要核对模拟器如何识别“本次动作”。V1.3 要求会话、命令序号、
  参数锁存、受理及完成关联，旧完成不能推进新动作。
- 原协议存在 PLC 内部坐标表/配方或面号驱动的表述；按 V1.3 第 7、11 节，
  应对齐 PC 下发完整取放参数、PLC 执行整体动作的边界。
- 旧文档中的超时重试不能直接套用：执行结果未知时先查询，禁止盲目重发抓取、翻面、分拣。

做法是让同学将**现有实现及其实际点表**提交成 PR，逐项填写“已有资料 → 实现位置 → 差异 → 测试”。
已经一致的条目直接沿用，只补缺口。若使用 V1.3 允许的单动作/清零握手兼容方案，
明确能力边界和断线后人工对账要求，不宣称跨重启严格去重。

### 2.1 要固定的是两种接口

| 接口 | 负责什么 | 至少要有的约定 |
| --- | --- | --- |
| 运行通信接口 | Host 的实际 Modbus 客户端与模拟器通信 | 协议版本、寄存器地址基准、功能码、类型/字序/单位、动作参数、命令反馈关联、心跳、停止及重连 |
| 自动测试入口 | CI 启动和控制模拟器 | 构建与启动命令、支持的系统、可配置端口、就绪判据、场景加载、故障注入、独立轨迹导出、退出方式与返回码 |

自动测试入口不限定必须是 HTTP；同学已有 CLI、配置文件或其他稳定接口即可评估复用。
正常业务不能绕过 Modbus，直接调用测试入口来假装设备完成动作。
模拟器可以有自己的内部代码结构，无须与中台使用相同语言。

现有实现已经具备：Modbus `127.0.0.1:1502`、Unit Id 1；HTTP `127.0.0.1:5080`；
`GET /health`、`GET /api/simulator/state`、`GET /api/simulator/address-map`、
`POST /api/simulator/reset`、`POST /api/simulator/faults/{fault}`。
可用配置改变端口并关闭启动时自动打开浏览器。直接复用这些已存在的能力。
现有 `/api/simulator/flow-decision` 把扫码/方案/保存失败替换为随机质量结果、把设备等待超时判为继续，
与 V1.3 冲突，不接入正式业务判定。它与上述设备通信能力应分别验收。

每个场景有固定编号、输入和预期；使用随机行为时固定种子。每次测试使用独立端口、目录和设备状态。
协议契约应同时用于编码/解码测试和两进程联调，预期值不能完全由同一个生产编码器生成。

### 2.2 虚拟 PLC 完成后，还缺哪些模拟

虚拟 PLC 负责轴、夹紧、抓取、翻面、分拣及故障状态。完整软件仿真还需要：

- F 托盘码、按方案使用的 E 零件码；
- 初始 3D 与翻面后 3D 定位结果；
- A/B 或 C/D 相机帧、触发及丢帧情形；
- 算法结果、超时、失败及迟到结果；
- 真实的任务状态机、保存、判定和界面联动。

按现有分工，这些采集/算法模拟及业务能力由中台负责人协调实现。先检查同学是否已提供部分能力，避免重复。
它们必须经过正式应用端口，不能用“模拟模式直接成功”的业务分支替代。
本轮取得的 VirtualPlc 没有实现真实采集端口的帧/3D/扫码输入；随机流程结果接口不能补足这些能力。

## 3. 三个人如何分工与提交

建议继续使用一个主仓库 `NickYoung618/x`。目录为建议布局，尚未存在的目录随具体功能创建：

```text
frontend/                 Vue 页面、组件和浏览器测试：前端同学
desktop/                  WPF/WebView2 及桌面测试：前端同学
simulator/                独立虚拟下位机及自身测试：下位机同学
backend/                  中台、设备适配、流程、算法调度、数据：你负责
tests/integration/        跨进程契约与流程：你牵头，两边共同核对
tests/e2e/                浏览器及 Windows 全流程：你与前端同学共同验收
docs/contracts/           三方共用的版本化接口
specs/                    每个功能的规格、计划、任务、验证记录
.github/workflows/        你维护，相关模块负责人提供可执行测试入口
```

“下位机同学负责软件对接”继续有效：他负责说明模拟器行为并共同调试适配与协议测试，
涉及 backend 的修改也可提交，但由你评审中台边界。
若同学已有独立仓库，不必先搬迁才能联调；可固定其提交或有校验值的发布版本作为依赖，
记录版本来源。首次接入不依赖每次变化的 `latest` 或浮动分支。

### 日常提交步骤

1. 从最新 main 创建一个短期功能分支，如 `feat/simulator-integration`、`feat/desktop-shell`。
2. 在该分支写/更新对应规格，引用共享契约；涉及接口变更先让使用方核对。
3. 开发、运行本模块测试和受影响的集成测试；提交规格、实现和测试，保留验证记录。
4. 推送分支并建立 PR，写清问题、变化、验收命令、实际结果和未覆盖部分。
5. CI 执行；失败修复后更新同一 PR。更新 main 后重新确认兼容性，不用过期检查结果合并。
6. 另一位开发者评审；中台/共享接口通常由你把关，你的跨边界变更由相关同学评审。
7. 合并 main，再执行组合后的测试与发包流程。

同一功能可以多次 PR，缺功能应明确写出；组件能独立通过自己的验收就可以逐步合入。
缺少完整闭环时不发布“普通整盘已完成”的标签，也不添加空的端到端测试使它显示绿色。

## 4. Spec Kit 如何共用

仓库已经安装 Spec Kit v1.0.7。三人 clone 同一个仓库，沿用：

- `.specify/memory/constitution.md`：共同架构与工程规则；
- `docs/architecture/architecture-v1.3.md`：技术与流程基准；
- `docs/contracts/`：双方共用协议；
- `.agents/skills/speckit-*`：同一套 Spec Kit 技能。

不要在各自模块里再次初始化互相矛盾的章程。已有模拟器规格可以保留并引用；
主仓库新增“接入与验收”规格即可，不要求重写已完成的模拟器。

以下编号只作排期建议，由你在开始前分配以免三人同时生成同号目录：

| 建议规格 | 主要交付 | 依赖 |
| --- | --- | --- |
| 002 虚拟下位机接入 | 实现与既有协议核对、可自动启停、真实 Modbus 两进程测试 | 同学现有代码及点表 |
| 003 界面与桌面壳 | HTML 原型转 Vue、离线资源、WPF、共用 HTTP/SignalR 契约 | 原型；界面读写边界 |
| 004 普通整盘全虚拟流程 | 公共准备、F 扫码选方案、分面采集、翻面重扫、判定/分拣/保存 | 设备与采集/算法适配；可分增量交付 |
| 005 Windows 包与验收 | 各组件组合、初始化/启动/测试脚本、版本证据、Release 流程 | 对应组件达到本次包声明的能力 |

每人对自己功能依次在仓库里的 Codex 使用以下技能；这是操作说明，本轮没有执行新功能的实现流程：

```text
$speckit-specify <本功能的用户目标、范围与验收行为；引用现有共同契约>
$speckit-clarify <只解决现有资料未说明、会影响实现的问题>
$speckit-plan <V1.3 技术约束、对接方式与测试环境>
$speckit-tasks <明确生成本功能需要的规则、契约及集成测试任务>
$speckit-analyze
$speckit-implement
```

明确的已有约定直接引用，不反复提问。发现有影响的冲突先修订对应规格或计划，再继续实现。
Spec Kit 帮助把需求转成开发任务，CI 则自动运行提交的测试代码；仅在 tasks.md 打勾不能通过功能验收。
依据：[官方工作流说明](https://github.github.com/spec-kit/reference/agentic-sdd.html)。

在每个 clone/worktree 明确 `.specify/feature.json` 指向自己的规格，该文件已被忽略，不提交到仓库。
不同人不要共用同一工作目录运行不同功能。此安装采用 Bash 脚本；Windows 开发者运行 Spec Kit 前
需验证 Git Bash 可用且技能脚本可执行。Windows 的 WPF 构建与运行仍使用 Windows 工具链。
不要通过重新初始化覆盖团队的技能和模板。

## 5. CI 分层接入与合并门槛

CI 应逐层增加真实用例；相应测试实现并跑通后，再将它设置为必需检查。

| 作业 | 主要环境 | 合并前检查 |
| --- | --- | --- |
| 现有 Core CI | Linux + Windows | 编译、19 项基础规则/HTTP、发布进程检查 |
| Frontend | Linux 或 Windows | Vue 类型检查、组件、构建；Playwright 页面操作；离线资源 |
| Simulator | 模拟器实际支持的系统 | 构建启动、设备状态机、协议编码及故障行为 |
| Device integration | 模拟器实际支持的系统 | Host 实际客户端 + 独立模拟器；动作、状态、断连、重复/迟到反馈 |
| FullSim flow | Linux 可运行部分 + Windows 组合 | F 扫码选方案、整盘多面、保存及异常；核对独立设备轨迹 |
| Desktop build/package | Windows | WPF 编译、WebView2 资源、组合包、解压后进程检查 |
| Windows desktop acceptance | 可交互 Windows 桌面 | 实际 WPF 内页面、原生窗口/对话框、部署路径、DPI、完整操作链 |

当前前端仍是 HTML：可先做静态资源、页面打开和链接检查。已有核查发现跳转缺失页面、外网依赖、
随机演示编号，详见 [原型评阅](ui-prototype/review.md)。这些检查不能代替登录、权限和业务验收。
前端迁入 Vue 后按原型保留布局，业务状态和身份由 Host 提供，不沿用随机假数据作为正式业务。

组件测试可以使用约定的假响应；集成与完整流程必须运行真实测试 Host。
浏览器通过不代表 WPF 通过。Playwright 可连接实际 WebView2 页面，原生窗口/文件选择另用
Windows UI Automation；桌面测试需可交互会话。
依据：[Playwright WebView2](https://playwright.dev/docs/webview2)、
[Microsoft 桌面测试环境说明](https://learn.microsoft.com/en-us/azure/devops/pipelines/test/ui-testing-considerations?view=azure-devops)。

### 5.1 第一组贯通用例

- 加载包含托盘码、场景和唯一方案映射的测试配置，模拟实体就绪/夹紧。
- 公共准备：初始 3D → F 读取托盘码 → 按码和场景匹配方案并固定版本。
- 2 件 × 2 面 × AB：面 1 A1/A2/B1/B2 → 完成整盘一轮翻面 → 重新 3D → 面 2 A1/A2/B1/B2。
- 一共 8 次 A/B 检测采集；3D 和 F/E 扫码另计，不算进这 8 次。
- 原 PartId 保留，旧坐标不再用于新动作；普通算法可在后台完成。
- 测试输入预设质量结果，检查判定、NG/Pending 分拣、必要保存和放行。
- 再覆盖未知/歧义托盘码、旧完成位、执行后丢反馈、定位失败、丢帧、算法超时和保存失败。

检查 UI、Host 状态、数据库/媒体和模拟器独立轨迹是否一致。
特别检查：模拟器实际执行一次，上位机不能因反馈丢失再做一次物理动作。
完整案例与更多故障见 [测试矩阵](testing.md)。

### 5.2 GitHub 必须另外设置合并规则

工作流本身只报告结果。要由平台强制拦截，还需对 main 设置：必须经 PR、至少一位其他开发者评审、
必需 CI 检查通过、分支与 main 同步、未解决讨论关闭。共享接口及 workflow 的修改须相关负责人评审。
检查名称应唯一；若增加总验收作业，必须核对各必需作业真实成功，不能让漏跑/跳过变成全通过。

本仓库当前是私有仓库，rulesets API 已明确返回套餐限制。私有仓库保护能力需匹配 GitHub 套餐，
参见 [GitHub 分支保护说明](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches)。
升级由仓库所有者自行选择，本轮未变更套餐、可见性或权限。
暂不升级时仍可 CI + 人工评审合并，但不能声称有平台强制保障。

## 6. 同学从 GitHub 下载什么包

开发过程用 Actions → 某次运行 → Artifacts；目前这里的 `foundation-host-windows-2022`
只有后端基础功能，且工作流设置 14 天保留。
团队可部署版本建议放 [Releases](https://github.com/NickYoung618/x/releases) 的 Assets，
以版本、平台、模式命名，如 `Gaode-0.1.0-rc.1-win-x64-FullSim.zip`。
这是建议名称，当前不存在该包。不要把 GitHub 自动生成的 `Source code (zip)` 当安装包。
拥有私有仓库读取权限的同学可以下载 Release 资产，登录 GitHub 本身不等于已获该仓库权限。
依据：[GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)。

### 6.1 FullSim 完整包目标结构

以下是待实现的逻辑布局，具体可执行文件名随项目确定；不创建空目录假装能力齐全：

```text
Gaode-<版本>-win-x64-FullSim/
  启动模拟检测.cmd          检查依赖、按顺序启动设备/Worker/Host/桌面
  测试部署.cmd              调用测试驱动，输出成功或失败及证据目录
  停止模拟检测.cmd          只关闭本次部署启动的进程
  初始化测试环境.cmd        显式调用独立数据部署工具，创建专用测试数据
  desktop/                 WPF/WebView2 壳
  host/                    ASP.NET Core Host，包含已编译的 Vue 静态资源
  simulator/               固定版本的独立虚拟下位机
  workers/                 本次 FullSim 所需的算法测试实现及运行依赖
  deployment/              独立数据库初始化/升级/验证工具
  config/                  明确 FullSim 的端点及模式配置
  samples/                 托盘码、方案映射、两件两面样例及必要帧/3D 数据
  prerequisites/           可分发的运行依赖，或匹配的离线依赖包清单
  tests/                   包验收脚本和锁定的测试驱动依赖
  docs/                    使用说明、支持的 Windows 版本、测试与限制
  build-manifest.json      提交、构建、模式、各组件及接口/数据库版本
  checksums.sha256         产物校验值
```

数据库、媒体、日志和本机配置保存在独立可写数据根，升级不覆盖旧数据。
Host 启动不静默建库或迁移；首次初始化由显式部署步骤调用独立工具。
当前基础 Host 尚无数据库，这些是后续完整包的验收要求。

推荐 WPF 与 Host 采用 `win-x64` self-contained 发布，包包含匹配的 .NET 运行时，
使用者无需为了运行软件安装开发 SDK。此发布方式不自动包含 WebView2、Python 或相机原生 SDK：
按实际组件预检和随包/离线依赖包交付，系统与架构兼容性须在目标 Windows 验证。
如果仍选择 framework-dependent，就明确所需 Desktop/ASP.NET Core Runtime。
依据：[.NET 发布方式](https://learn.microsoft.com/en-us/dotnet/core/deploying/)、
[WebView2 Runtime 分发](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)。

完整包不依赖现场 npm install、联网下载字体或临时 pip install。
测试脚本若使用 PowerShell 7，包装入口先检查并提供安装/离线依赖说明；不得默认 Windows 自带它。
正式运行入口不要求安装测试驱动；一键验收入口需明确并带齐其依赖。
FullSim 配置及界面明确标记模拟，生产配置不会在设备失败时自动回退为模拟成功。

### 6.2 发布和 Windows 验收

1. main 对应提交的必需检查通过后构建候选包，生成组件清单和文件校验值。
2. 在 Windows 用独立目录解压该包，先查依赖、启动、路径、端口，再做整盘与故障流程。
3. 记录包校验值、提交、环境、场景、实际结果；失败保留日志、截图、轨迹、数据库及媒体核对结果。
4. 验收通过后，将同一字节内容的 ZIP 提升为对应 tag 的 Release 资产，并附报告/校验值。
5. 同学从 Assets 下载具名部署 ZIP，校验、解压、显式初始化测试环境、启动和测试。
6. 未通过桌面验收的版本可明确标记候选/预发布，不能混称为完整验收版。

初期 Windows 验收可以由同学下载运行脚本并回传报告，不要求先注册 self-hosted runner。
之后如需自动化，可在可交互 Windows 会话注册专用 runner，由工作流下载指定候选包验收。
现有 windows-staging 工作流仅做 Host 基础冒烟，尚不能替代此完整流程。

## 7. 下一步按这个顺序做

1. 你与虚拟下位机同学：以已取得的 VirtualPlc 源码和 [审查结果](reviews/virtual-plc-20260917/review.md) 为基础，建立接入规格及 PR，保留其已有 Spec Kit 资料。
2. 你与同学：沿用已有约定，补齐 V1.3 差异；将其现有独立测试与中台实际客户端集成测试接入 CI，上传双方轨迹。
3. 前端同学：在独立分支将原型迁入 Vue/WPF，先打通真实 Host 状态展示和离线启动；共同固定 HTTP/SignalR 契约。
4. 你：实现普通整盘执行链以及扫码、3D、图像、算法测试适配；按上述整盘用例贯通。
5. 三人：CI 加入对应组件与集成检查，生成 Windows FullSim 候选包，在 Windows 验收后发布 Release。

本轮更新协作和验收文档，并对远端源码的独立副本进行审查和 Linux 验证。
未修改远端项目、GitHub 权限/工作流，未将模拟器合入主分支，未发布完整部署包。

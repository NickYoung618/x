# 真实 Windows 环境：部署与流程验收方案

## 1. 两种“真实”分别检查

**真实 Windows + 虚拟设备**：真实运行发布后的 WPF/WebView2、ASP.NET Core Host、Worker、SQLite、文件系统，
设备动作由独立模拟器承担。这是当前最先要做到的完整软件验收。

**真实 Windows + 真实设备/真实算法**：保持业务代码，逐个替换适配器，在硬件现场验证坐标、互锁、
采集、识别效果及节拍。第一种通过不能代替第二种。

现有服务器已查为 Windows Server 2019、2 核/8GB、未发现可用 GPU；未发现 .NET SDK、WebView2 或
GitHub runner 注册。远程桌面会话曾显示断开，不能据此声称桌面自动化可用。
它适合先验证 CPU 全虚拟软件流程；实际工控机版本和 SDK/GPU 性能另做环境基准。

## 2. 测试环境配置

- 创建 gaode 专用测试目录和独立测试身份；构建下载、数据、日志、WebView2 用户目录分别放置。
- 安装与发布方式匹配的 .NET 10 运行库（框架依赖 WPF 需 Desktop Runtime，Host 需 ASP.NET Core Runtime）、
  WebView2 Runtime、PowerShell 7；测试驱动按各锁文件安装。仅在本机需要构建时安装 SDK。
- Python 版本和依赖与 Worker 包匹配，独立环境；不借用其他项目的 Python/数据库。
- 注册仓库范围 runner，标签建议 `self-hosted, Windows, X64, gaode-staging`。
  runner 主动连 GitHub 取任务；无需把 SSH 密码写进 workflow 或公开 WebView2 调试端口。
- 桌面 E2E 使用已登录且可交互的专用会话，runner 在该会话运行；仅服务模式或 SSH 登录不足以证明有可用桌面。
- 每台桌面测试机一次一个套件，固定分辨率/DPI基线，再测变化；不与人工桌面操作抢焦点。

基础部署冒烟可无桌面；完整 WPF 验收前增加交互会话与 WebView2 启动预检。
当前还没有注册/配置这些项目专用 runner，本文件不表示已完成服务器部署。

## 3. 从 CI 到同一个发布包

1. PR 执行核心、前端与协议测试；通过后由 main 构建候选包。
2. 记录提交 SHA、构建 ID、架构/接口/数据库版本、文件校验值与依赖清单。
3. Windows 测试取指定 CI 运行产生的包，校验来自成功的受信 main 构建；不在服务器临时重编译另一份代码。
4. 解压到本次运行专用目录，创建新的测试数据根。已存在数据的升级使用独立部署工具，先备份。
5. 先做进程、路径、运行库、接口冒烟，再做桌面操作和异常恢复套件。
6. 失败保存现场；只终止该次启动的进程。成功保留结果及安装清单，实际生产切换另按发布流程进行。

仓库 `windows-staging.yml` 先提供**基础 Host 包**的手动部署冒烟入口。
它尚不包含 WPF/Worker/数据库，也不会把基础冒烟标成完整设备安装或全流程验收。

## 4. 如何自动操作真实桌面

- **Vue 页面**：先用 Playwright 在浏览器测试状态、弹窗和表单，尽可能通过 role/label 或稳定 data-testid 定位。
- **WPF 中的页面**：在仅测试会话开启 WebView2 本地 CDP，Playwright `connectOverCDP` 连接
  实际 WPF 进程内的 WebView2。不能启动另一个普通浏览器充当“WPF 已测”。
- **WPF 原生部分**：窗口启动/关闭、原生文件对话框、系统操作等使用 Windows UI Automation，
  为控件提供 AutomationId。可选具体库需前端同学接入时验证兼容；不用 DOM 测试冒充原生交互覆盖。
- 每轮独立 WebView2 用户目录、测试账号和数据根；结束清理测试进程，保留失败证据。
- HTTP/SignalR 用真实测试 Host；本层不把整个后端替换成页面 mock。
- CDP 仅绑定回环地址、仅测试进程启用；正式包默认不开放。

## 5. 第一条完整流程：两件、两面、AB

| 步骤 | 界面动作/观察 | 界面之外的核对 |
| --- | --- | --- |
| 1 | 打开 WPF → 登录 | 后端认证及角色；初始无自动运动；账户不能自选管理员获得权限 |
| 2 | 选择普通件测试检测方案 | 两件/两面/AB，冻结版本；设备模式明确为全模拟 |
| 3 | 点击启动 | 只有一个 TrayRunId；关键任务/版本快照已保存 |
| 4 | 看准备和第一面进度 | 公共准备扫码/3D；A1/A2/B1/B2 顺序；FrameRef/身份关联 |
| 5 | 看翻面与下一面 | 独立设备轨迹确认整盘轮次完成；再次 3D；新 CoordinateEpoch，原 PartId 不变 |
| 6 | 第二面完成 | A1/A2/B1/B2；旧面合法算法可并行完成；总计 8 次采集 |
| 7 | 看判定和分拣 | 测试场景预先规定结果，例如 p1=OK、p2=NG；Quality 输出与设备实际分拣轨迹一致 |
| 8 | 看整盘完成 | 必检任务有终态；意图/动作反馈/结果和必需媒体都已保存；符合放行条件 |
| 9 | 打开数据页和报告 | 同一 PartId/TrayRunId；统计与数据库一致；原图可打开；重复详情不换编号 |
| 10 | 导出、退出再启动查询 | 文件实际存在且内容/筛选范围一致；历史数据仍可用；不自动恢复运动 |

在步骤4注入普通算法超时：没有可靠NG的受影响零件进入Pending，另一件继续；
在步骤5注入定位失败：停止依赖新坐标的动作；
在分拣后丢反馈：动作次数不能变为2，无法确认时进入Unknown。
再覆盖暂停/恢复、UI断连、Worker退出、磁盘满、维护互斥及离线资源。

## 6. 判定和交付证据

每轮至少保存：安装包/提交/环境版本、场景/种子、屏幕录制或失败截图、Playwright trace、
Host/Worker/模拟器日志、独立设备事件轨迹、数据库与媒体核对报告、导出样本、测试结果。
“点击后显示完成”不足以通过。必须让 UI、业务快照、已提交数据、媒体文件、设备动作五者对应。

现阶段可做原型交互检查和基础 Host 的双平台检查；完整桌面流程缺 WPF、执行链、保存及模拟器契约。
这些是明确的开发依赖，不能通过自动生成一些空测试消除。

## 官方依据

- [GitHub 注册 self-hosted runner](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners)
- [Playwright 自动化 WebView2](https://playwright.dev/docs/webview2)
- [Microsoft 桌面 UI 测试的会话要求](https://learn.microsoft.com/en-us/azure/devops/pipelines/test/ui-testing-considerations?view=azure-devops)

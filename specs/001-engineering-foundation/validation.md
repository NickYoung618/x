# 验证记录：工程基础与原型/Windows 测试准备

日期：2026-09-15。实际平台：Ubuntu 24.04 x64，.NET SDK 10.0.401 / Runtime 10.0.12。

## 实际完成

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| Spec Kit v1.0.7 官方初始化及特性前置检查 | 完成，无扩展 hooks；规格质量清单通过 | .specify/ 与本规格目录 |
| 规格—计划—任务一致性 | FR-001 至 FR-008 均有对应任务，SC-001 至 SC-004 有验证入口；无阻断本增量的未答问题 | spec.md / plan.md / tasks.md |
| V1.3 原文快照 | SHA256 与 yyh 源文件一致 | docs/architecture/source.json |
| 实现前检查 | 15 条领域检查失败、HTTP 3 失败/1 通过，原因是规则未实现及入口404 | local-test-summary.json 的 red-tests |
| 实现后领域规则 | 15/15 通过，无跳过 | local-test-summary.json；本地 artifacts/test-results/*.trx |
| 实现后 HTTP 契约 | 4/4 通过，无跳过 | 同上 |
| 锁定依赖还原、Release 编译/发布 | 成功；依赖锁文件入库 | backend/**/packages.lock.json |
| 发布文件真实进程 | PowerShell 7.6.6 启动已发布 Host，检查存活/状态/8次计划，成功并退出 | scripts/smoke-host.ps1；本地 artifacts/host/smoke-*.log |
| 两份 GitHub workflow 静态检查 | actionlint 1.7.12 通过 | .github/workflows/ 与 .github/actionlint.yaml |
| 原型页面与交互 | 已查三页与两弹窗；发现登录404、随机编号及离线脚本失败，未称全流程通过 | docs/ui-prototype/browser-review.json、review.md、screenshots/ |

## 实施中发现并处理

- 第一次 Specify init 因空仓库已有 .git 而要求 --force；确认仅 .git 后重试成功，未覆盖业务源码。
- Windows workflow 的初稿在 job env 使用 runner.temp，静态检查不允许该上下文；
  改为 step 中读取 RUNNER_TEMP 并写 GITHUB_ENV，复查通过；声明自定义 runner 标签供 actionlint 校验。
- 本地无 PATH 中可用的当前 SDK/PowerShell；使用项目独立工具目录安装并核对官方包摘要，未更改其他项目依赖。

## GitHub / 自有 Windows

首次提交前本地验证完成；GitHub 运行结果将在推送后核对并补充。
自有 Windows runner 尚未注册；Windows staging workflow 未触发，未在该服务器执行本次新程序。

## 不在本次通过范围

采集计划仅声明翻面/扫描屏障，未实现状态机对真实动作和坐标版本的运行时校验。
未实现或验收：完整整盘执行、独立虚拟下位机协议、图像/算法、SQLite/媒体保存、数据库部署工具、
Vue/WPF/WebView2、真实设备、真实缺陷检出率、GPU/节拍、现场安全。
对应工作及退出标准见 docs/ordinary-tray-roadmap.md、docs/testing.md、docs/windows-acceptance.md。

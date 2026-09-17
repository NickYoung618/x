# V1.3 P0 框架验证记录

**范围**：本分支仅重构框架合同、模块目录、Host 状态/拒绝和测试映射；不实现具体工位。对照 `main` 提交 `09904fd8fe195fe2665e030d8b76b806e4c2f3ae`。本地验证在提交前的工作树运行，跨平台 PR CI 需以最终提交另行核对。

Draft PR：[#5 V1.3 整体框架](https://github.com/NickYoung618/x/pull/5)。Linux/Windows 的最终提交检查见 PR Checks；本地结果与 CI 结果分别判断。

| 检查 | 实际结果 | 证据/限制 |
| --- | --- | --- |
| Spec Kit 前置 | PASS | `check-prerequisites.sh --json --require-spec --require-tasks --include-tasks` 指向本 007 目录，列出研究/数据/合同/快速启动/任务 |
| 锁定依赖还原 | PASS | .NET SDK 10.0.401，新增 `Inspection.Contracts/packages.lock.json`；`dotnet restore Gaode.slnx --locked-mode` |
| Linux Release 构建 | PASS，0 Warning、0 Error | `dotnet build Gaode.slnx -c Release --no-restore` |
| 核心测试 | 最终 46/46 PASS，0 skipped | Domain 15、Application 12、Infrastructure 11、Host 8；新增 4 项 Host/架构检查 |
| 已发布 Host 进程 | PASS | 真进程 HTTP：V1.3、15 模块、Unconfigured、生产未就绪、旧状态 ID 保留、`POST /api/jobs/prepare`=501；本机无 pwsh，CI 再执行 PowerShell 冒烟 |
| 旧 V6 兼容与双进程 | 13/13、5/5 PASS | [运行摘要](evidence/legacy-framework-summary.json)；Host 实际 Modbus 客户端连接独立模拟器；只证明旧合同 |
| 最小 Vue UI | Linux `npm ci`、类型检查、3/3 组件测试、离线构建 PASS | Vite 与发布版 Host 两进程 `/api` 代理取到 15 模块/非生产状态；真实浏览器与 WPF 打包 NOT RUN |
| V1.3 PLC/工位、算法、完整整盘、Windows 桌面、真机 | NOT RUN | 点表/SDK/业务能力未接；不能由本轮核心或旧 13/5 推出通过 |

测试过程保留了失败：新 Host 合同测试初跑 2 项失败（状态仍为 `EngineeringFoundation`、准备命令 404），接入状态和拒绝后剩 1 项旧阶段名断言失败，修正该断言后 7/7 通过。中途一致性审阅发现旧 `DeviceIntegration`、`Algorithms` 状态 ID 可能被现有调用方使用，保留为兼容项；随后重新执行 Host 7/7、完整 45/45、发布进程冒烟通过。再补“无运动/3D/采集/算法 Provider 绑定”检查，最终 Host 8/8、完整 46/46。旧协议脚本在最后两项只读/测试补充前运行，因此其摘要仍记 45 项；PR CI 将对最终提交重跑旧链。

前端首次锁定依赖时 TypeScript 7.0.2 与 vue-tsc 3.3.11 不兼容，`vue-tsc` 报 `ERR_PACKAGE_PATH_NOT_EXPORTED`；改为精确锁定 TypeScript 5.9.3 并重新 `npm ci` 后，类型检查、3/3 组件测试和构建/本地资源检查均通过。补全 API 响应字段校验时类型检查又报过宽断言，改为逐字段构造返回值后再次通过。两次失败与修正均记录，不以首次构建通过冒充。

PR #5 首次最终提交 [CI 35191181805](https://github.com/NickYoung618/x/actions/runs/35191181805)：旧协议 Linux/Windows 两作业 PASS，Core 两作业在发布进程的 PowerShell 拒绝断言失败，UI 两作业因锁文件带本机腾讯镜像 `resolved` URL、托管 runner 无法解析镜像域名而在 `npm ci` 失败。前者旧日志只给合并断言，没有 HTTP 状态/响应字段，不能凭错误名认定 Host 未拒绝；本地真实进程的 POST 为 501。修正为官方 npm registry 锁定 URL，并用空缓存从官方 registry `npm ci` 成功；PowerShell 冒烟改用可同时取得解析体与状态码的 `Invoke-RestMethod -StatusCodeVariable`，在失败时分别输出状态/码/关联是否存在。需以新 CI 复核，不把此修改本身当作修复通过。

本次仍未达到 V1.3 §18 的完整 P0 退出：最小 Vue 状态页已实现并做组件/构建验证，但实际浏览器联通、WPF 包及三方接口核对仍未交付。后续场景已在[验收映射](../../docs/architecture/v13-acceptance-map.md)标责任增量。正式 3D/F/相机/算法/配方、数据库部署和恢复保持独立任务；S01 Draft PR #4 需基于此框架合并后的 main 继续实现。无付费模型调用，费用 0 元。

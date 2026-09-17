# V1.3 §18 测试归属与退出门槛

`NOT RUN` 表示未运行相应层级的实际测试；已有 003 内存状态/002、004 旧 V6 协议结果只能另列为基线。每项未来由责任 PR 把输入、独立设备/Worker 轨迹、预期、实际状态、首次失败、修正及复测证据填入验证记录。缺少生产配方或硬件时使用明确标记的开发样例，不能声称真实零件验收。

| ID / V1.3 §18 场景 | 首次责任增量 | 必要测试层与独立证据 | 当前 |
| --- | --- | --- | --- |
| P0-01 五层/十五模块、唯一 Host、默认拒绝 | 框架 | 项目依赖、HTTP 合同、发布 Host 进程 | Linux 本地 PASS；PR #5 与合并后 main 的 Linux/Windows CI PASS，见 `specs/007-v13-framework-foundation/validation.md` |
| P0-02 身份/状态机/设备与算法端口、统一点表草案 | 已有 003 框架；后续工位/PLC 合同 | 进程内身份与状态测试；三方核对版本化接口 | 部分框架 PASS；正式接口 NOT RUN |
| P0-03 Host/UI 最小页面与三方解释一致 | 框架 + 前端后续 PR | Vue 组件取 Host 状态、离线构建、浏览器/Windows 查看 | Vue 组件/离线构建的 Linux/Windows CI PASS；本机双进程代理 PASS；真实浏览器、WPF 与三方解释仍 NOT RUN |
| P0-04 来源模式和 Provider 替换 | 框架合同；S01 起逐步接入 | 同一业务规则在合成/真实适配上运行，来源可追溯 | NOT RUN |
| A01 A 已采、B 延迟/漏帧、缓存不死锁 | A/B、Media | 双相机组容量/归档/算法消费者独立轨迹 | NOT RUN |
| A02 算法乱序/重复/迟到、跨面/盘、重拍 | A/B、AlgorithmRuntime | Worker 回放、身份与 Attempt 对账 | NOT RUN |
| A03 算法慢、数据库慢、磁盘满、GPU 内存不足 | AlgorithmRuntime、Traceability、Media | 背压/终态/告警/控制通道压测 | NOT RUN |
| D01 PLC 执行后丢回复、不重复动作 | V1.3 PLC 接入、首个运动工位 | Host Modbus 报文加独立模拟器动作轨迹 | NOT RUN |
| D02 动作/文件/事务/分拣边界中断与恢复 | Traceability、Motion、分拣 | 故障注入副本、恢复页与事实对账 | NOT RUN |
| D03 翻面后旧坐标禁止动作、重扫保身份 | 翻面/3D 重定位 | 两次定位与独立运动轨迹、PartId/Epoch | NOT RUN |
| R01 UI 重启、Worker 卡死、DeviceHost 崩溃 | 前端、AlgorithmRuntime、DeviceHost | 进程级故障、心跳/停止与重连快照 | NOT RUN |
| R02 工程单步/自动冲突、模式切换、人工介入 | Motion、工程入口 | 资源占用与来源/人工确认轨迹 | NOT RUN |
| R03 全虚拟与真实 Provider 共用合同 | 各设备接入；混合联调 | 同一合同用例、独立真实/虚拟结果 | NOT RUN |
| Q01 普通算法失败/Worker 崩溃继续独立项 | AlgorithmRuntime、Quality | 在途唯一终态、下一件继续 | NOT RUN |
| Q02 A 失败 B 成功、融合缺输入、混合终态 | A/B、Quality | CaptureSet/依赖/汇合轨迹 | NOT RUN |
| Q03 判定截止、Pending/可靠 NG、特殊件移出 | Quality、特殊旋转/分拣 | 判定证据、可用取放路径与独立设备轨迹 | NOT RUN |
| Q04 分拣冻结前后迟到/后台重试 | Quality、Traceability、分拣 | DecisionRevision 与去向/结果修订记录 | NOT RUN |
| Q05 同源持续失败、Pending 区满、保存失败升级 | Diagnostics、Quality、Traceability | 容量/准入/报警与现场安全状态 | NOT RUN |
| Q06 3D 关键身份失败与普通算法失败区分 | S01、AlgorithmRuntime | 零后续不安全动作 vs 独立项继续 | NOT RUN |
| P4-01 短测到连续长测、节拍/质量指标 | 整机 P4 | 真实连续生产数据与确认的验收口径 | NOT RUN |

V1.3 §18 的数据库部署专项单列，不因框架能启动 Host 就认为数据库可用：

| ID / 数据库部署专项 | 责任增量 | 必要证据 | 当前 |
| --- | --- | --- | --- |
| DB-01 空库/重复/误选生产目录初始化 | Database.Deployment.Tool | 真实副本数据与工具日志 | NOT RUN |
| DB-02 支持旧版/当前版/过新版/缺迁移路径 | Database.Deployment.Tool | 版本矩阵、失败拒绝 | NOT RUN |
| DB-03 Host/查询/双工具/自动重启互斥 | Database.Deployment.Tool、Host | 多进程互斥与设备停机记录 | NOT RUN |
| DB-04 备份失败/满盘/权限/迁移中断 | Database.Deployment.Tool | 注入副本、旧库与日志保全 | NOT RUN |
| DB-05 工具重启后的实际步骤核对 | Database.Deployment.Tool | 中断点和幂等/受控恢复 | NOT RUN |
| DB-06 旧备份恢复与新增数据/版本影响 | Database.Deployment.Tool | 程序/库/配方/模型兼容核查 | NOT RUN |
| DB-07 换盘/换机/媒体缺失/仅数据库备份 | Database.Deployment.Tool、Media | 路径、引用和源数据保全 | NOT RUN |
| DB-08 Host/设备不可用时独立工具恢复 | Database.Deployment.Tool | 离线启动、操作与不自动运动 | NOT RUN |

每个工位 PR：先固定 Spec Kit 验收案例与独立期望 → 实现同一 Application 流程上的能力 → 本工位正常/错误/日志/恢复测试 → 修正并保存首次失败及复测 → Linux/Windows 核心与影响范围回归 → Draft PR 补证据并评审 → 合并 main 后复测同一提交。随后让前端/下位机同学用该提交或同一测试包复测。完整整盘、真实算法、Windows 交互桌面、真机和性能结果各自独立报告，任何一层的通过不替代下一层。

# 框架外部合同 v1

`GET /health/live` 只表示进程可响应。`GET /api/system/status` 保留 `architectureVersion`、`stage`、`productionReady`、`unavailableCapabilities`，增加 `runtimeMode` 和 `modules[]`。`modules[]` 恰有 V1.3 十五个唯一 ID；其 `state` 只能表达已有证据，不能因目录存在而为 Implemented。默认 `runtimeMode=Unconfigured`、`productionReady=false`。

`POST /api/jobs/prepare` 是未来任务入口的预留合同：框架期返回 HTTP 501，Problem JSON 至少含 `code=CAPABILITY_NOT_IMPLEMENTED`、中文 `title`、`correlationId`。不能生成 TrayRun 或设备动作；未来工位功能 PR 替换此拒绝时，必须同步增加授权、幂等、状态和设备测试。现有 `/api/jobs/start` 保持 404，不创建第二套入口。

`GET /api/engineering/demo-plan` 继续返回 `isTestFixture=true`、`executionEnabled=false`；`--plc-probe` 只接受显式旧协议工程合同。未来前端首次进入及断线恢复先请求状态快照，再订阅通知；SignalR 仅推送变化，不当可靠任务队列。正式 PLC/算法/数据库合同不由本文件伪填具体字段。

运行模式词汇：Unconfigured、FullSimulation、ImageReplay、HybridCommissioning、ManualHandoff、Production。框架只报告 Unconfigured，具体模式切换/来源标记在对应设备与工位 PR 实现；全模拟与真实 Provider 必须共用业务合同。

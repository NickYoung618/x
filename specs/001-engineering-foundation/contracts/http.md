# HTTP 基础契约 v0.1

本契约仅覆盖本次只读工程接口。后续命令、快照 Revision 与 SignalR 待完整流程特性定义。
默认地址 `http://127.0.0.1:5000`，JSON 字段 camelCase，枚举使用大小写明确的字符串。

| 请求 | HTTP 200 返回 | 含义 |
| --- | --- | --- |
| GET /health/live | `{"status":"alive"}` | 进程能够处理请求，不表示可生产 |
| GET /api/system/status | `architectureVersion:"1.3"`、`stage:"EngineeringFoundation"`、`productionReady:false`、`unavailableCapabilities` 数组 | 当前工程能力；不返回部署凭据或机器私有路径 |
| GET /api/engineering/demo-plan | `isTestFixture:true`、`executionEnabled:false`、`plan.faces` | 测试采集计划，查询无设备/业务写入副作用 |

`plan.faces[]`：`faceId`、`requiresFlipBefore`、`requiresScanBefore`、`captures[]`。
`captures[]`：`partId`、`camera`，camera 为 A/B/C/D。顺序为面、相机、零件。
样例配置有 2 面，每面 4 个 capture，第二面要求先翻面再重扫。

本次没有“开始检测”入口。后续未实现的请求返回标准 404，不接受后返回假的成功。
界面首次可联调这三个查询；不能把它们当完整操作员界面协议。

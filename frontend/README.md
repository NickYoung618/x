# V1.3 最小只读状态页

这是 P0 框架页面，不是原型界面的完整迁移。它从 Host `/api/system/status` 显示十五模块与未就绪能力；没有检测启动、停止、配方编辑或质量判断。前端同学后续以 [原型核查](../docs/ui-prototype/review.md)和 [V1.3](../docs/architecture/architecture-v1.3.md) 扩展页面、权限和 Windows 壳。

使用 Node 24.12+，在仓库根目录先运行 Host（默认回环 5000），再执行：

```sh
cd frontend
npm ci
npm run dev
```

Vite 开发服务器将 `/api` 代理到本机 Host。`npm run typecheck`、`npm test`、`npm run build` 分别检查类型、组件与离线静态包；`dist/` 不入库。当前尚未把页面打入 WPF/Host 发布包，也未做 Windows 交互桌面验收；该打包集成由前端/交付增量完成。任何断线或数据格式错误只显示错误，不产生默认成功状态。

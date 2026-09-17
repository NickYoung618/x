# Tasks: V1.3 整体软件框架

**Input**: [spec.md](spec.md)、[plan.md](plan.md)、[research.md](research.md)、[data-model.md](data-model.md)、[合同](contracts/system-framework.md)。

## Phase 1: Setup

- [x] T001 从合并后的 main 建 `framework-v13-foundation`，登记 `docs/update-goals.md`
- [x] T002 创建 Spec Kit 规格、检查表、研究、数据模型、合同与计划 `specs/007-v13-framework-foundation/`

## Phase 2: Foundational

- [x] T003 建立无业务逻辑的 `backend/src/Inspection.Contracts/Inspection.Contracts.csproj` 与状态/问题 DTO，将它加入 `Gaode.slnx` 并完成锁定还原
- [x] T004 在 `backend/src/Inspection.Application/Architecture/` 建立十五模块和未实现生产能力的唯一目录，固定模块 ID、层、责任、状态且无 Implemented 状态
- [x] T005 建立 `docs/architecture/v13-framework-map.md`，对照十五模块写清已存在代码、将来端口/适配/Host 归属和运行模式边界

## Phase 3: User Story 1 - 真实能力状态与明确拒绝 (Priority: P1)

- [x] T006 [US1] 先在 `backend/tests/Inspection.Host.Tests/HostContractTests.cs` 增加 15 模块、旧字段兼容、默认模式/非生产及 501 未受理断言
- [x] T007 [US1] 在 `backend/src/Inspection.Host/Program.cs` 从 Application 目录映射外部 DTO，提供 `/api/system/status` 和 `/api/jobs/prepare` 明确拒绝；保留工程计划与旧探针
- [x] T008 [US1] 在 `scripts/smoke-host.ps1` 核对已发布进程的模块数、模式和拒绝合同，不只测源码内存对象

## Phase 4: User Story 2 - 分层接入路径 (Priority: P1)

- [x] T009 [US2] 在 `backend/tests/Inspection.Host.Tests/` 增加项目依赖方向检查，证明 Domain/Contracts 无业务或设备依赖、Application 不依赖 Host/Infrastructure
- [x] T010 [US2] 在 `docs/architecture/v13-framework-map.md` 固定首工位及后续每工位的 Application 端口、Infrastructure 适配、Host 装配、外部合同和独立设备轨迹要求；不写具体点表

## Phase 5: User Story 3 - 分层测试与 PR 门槛 (Priority: P2)

- [x] T011 [US3] 建 `docs/architecture/v13-acceptance-map.md`，逐条映射 V1.3 §18 必测及数据库部署专项，标责任阶段、证据、当前 NOT RUN/旧基线
- [x] T012 [US3] 更新 `docs/testing.md`、`README.md` 和协作文档，明确框架 PR → S01 → 逐工位 PR 的正常/故障/修正/复测/合并后回归门槛

## Phase 6: User Story 1 - P0 最小只读状态页

- [x] T013 [US1] 在 `frontend/package.json`、`package-lock.json`、`vite.config.ts`、`tsconfig.json` 建 Vue 3/TypeScript/Pinia 锁定项目及 Host `/api` 开发代理
- [x] T014 [US1] 在 `frontend/src/api/system.ts`、`frontend/src/stores/system.ts`、`frontend/src/App.vue` 从状态接口展示模块/能力与非生产/连接失败，禁止业务按钮
- [x] T015 [US1] 在 `frontend/src/App.test.ts` 以独立响应/断线输入验证页面，并在 `.github/workflows/ci.yml` 增 Linux/Windows 前端安装、类型检查、测试和构建
- [x] T016 [US1] 在 `frontend/README.md`、`docs/architecture/v13-framework-map.md` 明确最小页面的运行/打包边界与前端同学后续责任

## Phase 7: Validation

- [x] T017 锁定还原、Release 构建/全部测试、发布 Host 冒烟、旧 V6 独立模拟器脚本、前端 `npm ci`/测试/构建实测并写 `specs/007-v13-framework-foundation/validation.md`
- [ ] T018 Spec Kit 前置与跨文档一致性检查、`git diff --check`，结项 `docs/update-goals.md`；创建 Draft PR，不合并未实现的 S01

依赖：T003/T004 → T006/T007/T008；T005 → T010/T011/T012；T013/T014 → T015/T016；T017 检查所有前置。框架 PR 只兑现 P0 最小骨架，不把 §18 的后续场景打勾。

# Research: 首工位实现前的证据与决定

## 已核实的基线

- V1.3 §5.2 的顺序是新鲜就绪/夹紧 → 移至 3D → 定位 → F 读盘码 → 唯一配方；首轮定位/扫码使用公共准备路径，不依赖配方。
- `TrayWorkflow.OnReady` 当前直接发 `RequestScan`；`IPlacementLocator` 只返回 `LocatedSlot(SlotId, PositionRef)`；`MotionKind` 尚无 MoveTo3D；Host 尚未组合/驱动 `TrayWorkflow`。现有 Workflow 测试只在内存里发送事件。
- `MotionExecutionLane` 已能串行投递、分离受理/完成、未知结果锁定；应复用，不能再造平行运动入口。它目前不提供动作意图持久化或重启后对账。
- 002/004 的真实 Modbus 双进程测试验证的是旧 V6 兼容合同，与 V1.3 工位动作/坐标不等价。接口差异见 `docs/contracts/virtual-plc-alignment.md`。

## 本阶段选择

1. 先扩展 Application 的 MoveTo3D 效果和阶段，由一个 Host 工程测试入口驱动合成运动与 3D 适配器；入口默认关闭/限制本机及测试环境，不形成生产动作路径。
2. 3D 请求单独关联本盘、本次请求和目标坐标批次；合成定位返回来源及位置元数据。实际单位/标定未知时仅作不透明位置引用，禁止计算真机运动坐标。
3. 把状态来源/故障码与结构化诊断纳入首工位验收。出错时分清明确拒绝、执行未知、采集失败和结果不可信；未知运动由人工对账后恢复。
4. 首工位 PR 只验收到 WaitingTrayCode。F/配方是下一工位的独立阶段，算法和整盘不进入此 PR 验收。

## 待确认但不阻塞本阶段

正式点表（地址基准、字序/单位）、会话/动作序号和重连对账、公共 3D 位置及到位判据由下位机同学提供；实际 3D SDK、坐标系、标定版本和空盘业务规则由设备/工艺方提供。确认前测试替身不能被配置为生产设备。

## 服务器—GitHub—Windows 方案决定

1. **决定：软件合并与现场验收分成两个结论。** 理由：GitHub 托管 Windows 作业有 Windows 系统但没有现场 PLC/3D；Linux 合成替身验证规则，不证明实体到位。备选“六项 CI 全绿就称真机通过”会错配证据；真机按 [现场准入](windows-real-device.md)单列。
2. **决定：现场使用成功 main 构建的同一 Windows 候选包及 SHA-256。** 理由：当前仓库已有 Host 产物与 build manifest，可扩展 S01 包；重新编译会使修复与现场结果无法归因。`.NET publish` 需要明确 `win-x64` 和自包含/框架依赖模式；原生 3D SDK、驱动和 WebView2 不因 .NET publish 自动打包。[.NET 发布命令](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish)。
3. **决定：先人工现场验收，专用 runner 后接。** 理由：查询仓库 runner 列表为 0，`windows-staging.yml` 仅有基础 Host 冒烟且仍比较旧阶段名；当前不能声称自动现场测试。自建 runner 可接现场硬件，但要由现场管理系统和软件；注册后用受控手动工作流，不让每个 PR 自动驱动物理设备。[GitHub 自建 runner](https://docs.github.com/en/actions/concepts/runners/self-hosted-runners)。
4. **决定：Windows 真机按只读→单次动作→3D 定位递进。** 理由：V1.3 §7/§11 把硬件互锁留给 PLC、未知动作要求人工对账；本阶段不做 F、配方或整盘。故障中的断线/重复/迟到先在独立模拟器注入，真实设备只执行现场批准的安全项目；物理测试预期与负责人记录在同一报告。
5. **决定：WPF 与 3D/PLC 设备链分别验收。** 理由：目前只有最小 Vue 只读页、无 WPF 工程。可先用受控 Host 工程入口证明真实设备链，待前端壳接入后补实际桌面；另开普通浏览器不算 WPF 通过。

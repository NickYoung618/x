# 制冷机零件缺陷检测虚拟 PLC（.NET 10）

本项目依据 `资料/制冷机零件缺陷检测SRS_PLC接口设计规范报告.docx` V6.0 实现一个可独立运行的 Modbus TCP 虚拟下位机。

## 2026-09-11 隔离合成协议（开发中）

默认仍启动上述 V6。显式设置 `Simulation__ProtocolProfile=Protocol20260911Synthetic` 后，进程改用 [全工位合同草案](../../docs/contracts/plc-v13-all-stations-v0.1.md)对应的 9 月 11 日十六进制一基测试点表；只能在回环地址启动。基础版本提供 Float32 双字读写、心跳、Ready、夹紧、区域配置、人工区/报警等确定性事实，并在 `GET /api/v13/state` 与 `GET /api/v13/trace` 留存设备侧状态及原始 Modbus 报文。`POST /api/v13/faults/{fault}?active=true|false` 仅预置故障。

此版本尚无已确认的 `MoveTo3D/MoveToF` 线缆命令，收到非零原始运动命令会返回 Modbus 异常；不把已写入的 XY 目标当作机构运行。独立 3D 采集与 Host S01 运动联调仍在后续增量。生产真实地址、字序、坐标和动作协议均未签认；[当前验证记录](../../specs/010-v13-device-simulator/validation.md)将旧 V6、合成新点表、真机和整盘分开报告。

基础跨进程验证入口：先按 .NET 10 构建解决方案，再运行 `python3 simulator/VirtualPlc/scripts/validate-protocol20260911-base.py --dotnet /path/to/dotnet`。脚本启动独立虚拟 PLC 和 PR #9 Host 只读客户端，另以原始 Modbus 测夹紧、Float32、故障和未定义命令拒绝；证据写入 `specs/010-v13-device-simulator/evidence/`。

它用于上位机在真实 PLC、SDK 和机械条件就绪前进行流程联调。上位机以后仍然使用同一组线圈、4X 地址和握手方式，只需要把连接目标从虚拟 PLC 换成真实 PLC。

## 当前实现范围

- Modbus TCP 从站（Slave/Server），默认监听 `127.0.0.1:1502`，Unit Id 为 `1`；上位机是主站（Master/Client）。
- 支持功能码：01、03、05、06、15、16。
- 实现规范中的全部线圈和 4X 保持寄存器。
- 模拟心跳、XY/Z 到位、翻转、分拣、托盘锁紧、区域配置确认。
- 提供动作未到位、PLC 急报警、人工介入等故障注入。
- 提供模拟流程兜底接口。明确的 PLC/下位机动作失败和安全类故障阻断；XY/Z、翻面等待超时记录错误后继续；其他失败返回“继续流程”，并按需生成随机 OK/NG/Pending。

虚拟 PLC 只负责设备动作与反馈。任务、零件身份、检测面、算法判定和流程推进仍属于上位机。

## 最新启动方式：启动虚拟下位机并自动展示监控界面

项目主程序和冒烟测试均以 `net10.0` 为目标框架。先安装 .NET 10 SDK，以及 VS Code 的 C# Dev Kit 扩展。项目根目录的 `global.json` 会选择 .NET 10 SDK。

### 首次运行准备

在包含 `VirtualPlc` 文件夹的目录打开终端并进入项目：

```bash
cd VirtualPlc
code .
```

在 VS Code 中打开“终端 → 新建终端”，确认 SDK 并还原项目。以下命令只需在首次运行、依赖变化或清理构建文件后执行：

```bash
dotnet --version
dotnet restore src/VirtualPlc/VirtualPlc.csproj
dotnet restore tools/VirtualPlc.SmokeTest/VirtualPlc.SmokeTest.csproj
```

`dotnet --version` 应输出 `10.0.x`。

### 日常启动

以后启动虚拟下位机只需在 `VirtualPlc` 目录执行：

```bash
dotnet run --project src/VirtualPlc/VirtualPlc.csproj
```

这一条命令会同时完成以下工作：

1. 启动 Modbus TCP 虚拟 PLC，从站地址为 `127.0.0.1:1502`，Unit Id 为 `1`。
2. 启动模拟器管理接口和状态接口，监听 `http://127.0.0.1:5080`。
3. 启动 0x/4x 动态监控页面。
4. 在有图形桌面的电脑上自动使用系统默认浏览器打开监控页面。

可用地址：

- Modbus TCP：`127.0.0.1:1502`
- 模拟器管理接口：`http://127.0.0.1:5080`
- 实时监控页面：`http://127.0.0.1:5080/`
- 状态查看：`GET http://127.0.0.1:5080/api/simulator/state`

看到终端输出 `Virtual PLC Modbus TCP listening` 和 `Now listening on: http://127.0.0.1:5080`，表示虚拟下位机和监控页面均已启动。请保持该终端运行；按 `Ctrl+C` 会同时停止 Modbus 服务和监控页面。

没有图形桌面的服务器环境会跳过自动打开，并在日志中输出页面地址，此时手动访问 `http://127.0.0.1:5080/` 即可。如需关闭自动打开浏览器，将 `appsettings.json` 中的配置改为：

```json
{
  "Dashboard": {
    "OpenBrowserOnStart": false,
    "Url": "http://127.0.0.1:5080/"
  }
}
```

实时监控页面每 100ms 读取一次模拟器状态。主监控区按通信方向分为两栏：左侧固定显示“PLC → 上位机”的状态与反馈，右侧固定显示“上位机 → PLC”的命令与参数；每栏再分别显示 0x 线圈和 4x 保持寄存器。数值变化时，对应数值会短暂闪烁并写入最近变化记录。页面为只读监控，不会向 PLC 写值。

PLC 心跳默认每 1000ms 翻转一次。`PLC_Heartbeat_Req` 和 `PC_Heartbeat_Resp` 仍在地址表中实时显示为灰色 0 / 高亮 1，但心跳变化不触发闪烁、不计入变化总数，也不写入“最近变化”，避免持续心跳淹没实际命令和反馈记录。

显示规则按信号类型区分：所有线圈以及明确只有 0/1 的保持寄存器使用二值样式，0 显示为灰色、1 显示为高亮；具有多个枚举值的状态/命令以及坐标、角度、槽位和数量，始终以原始数值为主，并在规范已定义时显示对应中文含义。数值发生变化时只闪烁数值区域，不会用同一种整行高亮掩盖不同状态值。

当前约定中，`XY_Move_Cmd`、`Flip_Trigger_Cmd`、`Sorting_Cmd`、`Zone_Config_Ready` 和 `Retry_Cmd` 为 0 时表示空闲并等待上位机指令。目标坐标、PLC 实际坐标、翻转结果角度、分拣槽位号和两个区域数量的底层初始值仍为 0，但监控页面显示为“—/空”，直到上位机写入或 PLC 反馈非零值。

`XY_Pos_Confirmed`（4x0002）采用上下文显示：虚拟 PLC 刚启动且尚未执行移动时，原始值 0 显示为“—/空”；移动动作已经开始时，同一个原始值 0 显示为“运动中”。上位机应结合自身是否已下发移动命令判断该状态，不能脱离动作上下文单独解释 0。

页面不写死点位数量。后续在 `PlcAddressMap` 的统一地址表中增加线圈或保持寄存器后，状态接口和监控表格会自动增加对应行；前端结构无需同步修改。若希望新点位显示额外的中文说明或枚举含义，可在 `wwwroot/app.js` 的可选说明映射中补充，但不补充也不影响地址、方向和原始值显示。

### 可选：运行联调自检

保持虚拟下位机终端运行，再新建第二个终端执行完整的 Modbus 主从交互验证：

```bash
dotnet run --project tools/VirtualPlc.SmokeTest/VirtualPlc.SmokeTest.csproj
```

看到 `SMOKE TEST PASSED` 表示地址、读写方向、心跳、互锁、动作反馈和故障状态验证通过。也可以在浏览器打开 `http://127.0.0.1:5080/health` 检查服务状态。按 `Ctrl+C` 停止虚拟 PLC。

### 运行 Spec Kit 全量测试验证

在包含 `VirtualPlc` 的 `pj1` 目录执行：

```bash
bash VirtualPlc/scripts/validate.sh
```

该命令自动构建并启动隔离的验证实例，覆盖 29 个点位、Modbus 功能码与异常响应、HTTP
和监控资源、正常动作、互锁、全部故障注入、流程兜底及复位。结果写入：

- `VirtualPlc/test-results/latest.md`：人类可读报告；
- `VirtualPlc/test-results/latest.json`：机器可读逐项结果；
- `VirtualPlc/test-results/host.log`：验证实例日志。

生产项目和验证程序现均以 .NET 10 为目标；脚本要求安装 .NET 10 SDK，并使用锁定还原。
对应 Spec Kit 需求、计划、合同和任务记录位于 `../specs/001-validate-virtual-plc/`。

这里的角色固定为：虚拟 PLC 是 Modbus TCP Slave/Server，上位机或测试工具是 Master/Client。两端通过 Modbus TCP 通信，不要求使用相同的 .NET 运行时。

如需让另一台电脑访问，将 `appsettings.json` 中 Modbus 地址和 `urls` 改为实际网卡地址或 `0.0.0.0`，并按现场网络策略开放对应端口。

## 地址解释

规范里的 `0x` 表示线圈区，`4x` 表示保持寄存器区；其后数字按十进制点号理解。例如：

- 文档 `0x0001` 对应 Modbus PDU 线圈偏移 `0`。
- 文档 `0x0010` 对应 Modbus PDU 线圈偏移 `9`。
- 文档 `4x0001` 对应 Modbus PDU 保持寄存器偏移 `0`。
- 文档 `4x0029` 对应 Modbus PDU 保持寄存器偏移 `28`。

这是常见的“文档一基地址、报文零基偏移”方式。上位机使用的 Modbus 库可能让调用者填写 `1`，也可能填写 `0`，接入时应确认该库的地址口径。

完整点表见 [docs/plc-address-map.csv](docs/plc-address-map.csv)。

点表及行为与 V6.0 资料的逐项核对结论见 [docs/PLC信号交互核对.md](docs/PLC信号交互核对.md)。

## 上位机最小联调顺序

1. 循环读取 `PLC_Heartbeat_Req`，将相同值写入 `PC_Heartbeat_Resp`。
2. 写 `PC_System_Ready = 1`。
3. 写入区域数量，将 `Zone_Config_Ready` 从 0 写为 1，等待 `Zone_Config_Ack = 1`。
4. 写入 X/Y/Z 目标；先确保 `XY_Move_Cmd=0`，再写动作值，等待 `XY_Pos_Confirmed=1` 且 `Z_Axis_Move_Status=2`。
5. 上位机执行相机和算法模拟。需要算法兜底时调用随机结果辅助接口。
6. 翻面时将 `Flip_Trigger_Cmd` 从 0 写为 1 或 2，等待 `Flip_Status=2` 并校验角度。
7. 分拣时写入槽位号；将 `Sorting_Cmd` 从 0 写为 1，等待 `Sorting_Exec_Status=2`。

动作寄存器采用“0 → 非0”的新命令触发方式。同一动作再次执行前，先把命令写回 0，再写下一次命令值。这样可以重复执行同一种移动或分拣动作。

## 哪些情况应该阻断模拟流程

按照本轮要求，初版把错误分成两类：

### 设备级错误：上位机应报警并阻断

- `PLC_System_Fault=1`，包括 PLC 已明确判定的 XY/Z 故障。
- 翻面明确失败或角度不符：`Flip_Status=3` 或角度校验不通过。
- 分拣失败/抓空/满盘：`Sorting_Exec_Status=3/4`。
- 托盘锁紧失败：`Pallet_Lock_Status=2`。
- PLC 急报警、安全门/人工介入、心跳超时。

### 其余所有错误：模拟测试继续

- XY/Z 或翻面等待超时，包括 `XY_Pos_Confirmed=2`：上位机记录超时错误，然后继续下一步。
- `Z_Axis_Move_Status=3` 且 `PLC_System_Fault=0` 时，模拟模式暂按超时处理并继续；两者同时出现时按明确故障阻断。

相机采集、扫码、算法、配方、数据保存、MES 和其他上位机软件步骤失败，在当前模拟联调策略下一律记录为兜底事件并继续。需要质量结果时生成随机 OK/NG/Pending；不需要质量结果的步骤返回模拟成功。虚拟 PLC 不把这些失败映射成 PLC 故障线圈。

这个策略仅用于打通模拟流程。真实生产质量判定策略仍需按正式验收规则配置，不能把随机结果当作生产结论。

## 通用流程兜底接口

```http
GET /api/simulator/flow-decision?category=Algorithm&stepSucceeded=false
GET /api/simulator/flow-decision?category=Camera&stepSucceeded=false
GET /api/simulator/flow-decision?category=Storage&stepSucceeded=false
GET /api/simulator/flow-decision?category=DeviceTimeout&stepSucceeded=false
```

返回示例：

```json
{
  "continueFlow": true,
  "shouldReportError": false,
  "decision": "NG",
  "isFallback": true,
  "message": "非设备故障，模拟模式使用兜底结果继续流程"
}
```

当 `category=DeviceTimeout` 且步骤失败时，接口返回 `continueFlow=true`、`shouldReportError=true`，供 XY/Z 和翻面等待超时记录错误后继续。`DeviceAction`、`PlcSafety` 或 `PlcCommunication` 的失败返回 `continueFlow=false`、`shouldReportError=true`。翻转角度不一致归入 `DeviceAction`，仍然阻断。其他分类失败返回 `continueFlow=true`、`shouldReportError=false`。随机结果权重在 `appsettings.json` 中配置。该接口是模拟联调辅助能力，不属于 PLC Modbus 点表。

## 故障注入

```http
POST /api/simulator/faults/MoveTimeout
POST /api/simulator/faults/FlipFailure
POST /api/simulator/faults/FlipAngleMismatch
POST /api/simulator/faults/SortingFailure
POST /api/simulator/faults/FullPallet
POST /api/simulator/faults/PalletLockFailure
POST /api/simulator/faults/EmergencyAlarm
POST /api/simulator/faults/ManualZoneOccupied
POST /api/simulator/faults/PauseHeartbeat
```

动作类故障默认只影响下一次对应动作。状态类故障保持到执行复位：

```http
POST /api/simulator/reset
```

复位会清除模拟故障和动作状态，但不会代替真实项目中的人工安全确认。

## 已识别的规范待确认项

1. X/Y/Z 各只有一个 16 位寄存器，规范未定义单位、正负数、比例和溢出方式。模拟器原样保存 16 位值；生产联调前需由 PLC 与上位机团队冻结编码方式。
2. 规范要求 PLC 返回故障码 01～05，但地址表没有分配故障码寄存器。初版严格保留现有点表，只通过 `PLC_System_Fault` 线圈提供通用故障，并在管理接口显示详细模拟故障。正式点表建议后续增加明确的只读故障码地址。
3. 规范没有定义动作命令清零责任、重复命令序号和确认位。初版采用 0→非0 触发；真实 PLC 联调前应把握手细节写入双方 ICD。
4. 文档写“相机人工固定调焦”，因此本模拟器只把 Z 当作绝对目标高度，不实现自动搜索对焦。

## 目录

```text
VirtualPlc/
  README.md
  docs/
    plc-address-map.csv       规范点表与 Modbus 偏移
    上位机接入说明.md          轮询、动作与错误策略
  src/VirtualPlc/
    Program.cs                服务入口和管理 API
    PlcAddressMap.cs          单一地址定义
    PlcDataStore.cs           线程安全寄存器/线圈存储
    ModbusTcpServer.cs        Modbus TCP 协议服务
    VirtualPlcEngine.cs       设备状态与动作模拟
    SimulationModels.cs       配置、快照和故障类型
    DashboardLauncher.cs      启动后自动打开实时监控页面
    wwwroot/                  0x/4x 地址实时监控页面
```

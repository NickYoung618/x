# VirtualPlc Spec Kit 验证结论

**日期**: 2026-09-17  
**自动验证结论**: PASS  
**自动检查**: 13/13 PASS  
**连续复跑**: PASS（至少两次连续通过，无残留服务或监听端口）  
**最近一次行为测试耗时**: 约 21 秒（低于 60 秒目标）

## 已验证范围

| 范围 | 结果 | 证据 |
|---|---:|---|
| CSV、HTTP 地址表、运行状态中的 29 个点位 | PASS | 8 个线圈、21 个保持寄存器逐点一致 |
| Modbus FC01/03/05/06/15/16 | PASS | 成功路径、事务回显与回读通过 |
| Modbus 异常和写权限 | PASS | 异常码 01/02/03/0B、多写原子性、PLC 所有权通过 |
| HTTP 与监控资源 | PASS | health、首页、JS、CSS、非法 fault/category 通过 |
| 正常流程 | PASS | 心跳、区域配置、托盘锁、移动 1～5、翻转、分拣、重试通过 |
| 互锁 | PASS | 未就绪、未配置、软停、非法命令、动作互斥、零槽位通过 |
| 9 种故障 | PASS | 6 种一次性动作故障及 3 种安全/通信故障通过 |
| 11 类流程决策 | PASS | 成功与失败策略、随机结果集合通过 |
| reset | PASS | 故障、动作、PC 可写量和状态清除通过 |
| 证据合同 | PASS | 单入口、pj1 目录边界、JSON/Markdown、目标框架门禁通过 |

逐项证据见 [latest.md](../../VirtualPlc/test-results/latest.md)，机器结果见
[latest.json](../../VirtualPlc/test-results/latest.json)，服务日志见
[host.log](../../VirtualPlc/test-results/host.log)。

## 环境门禁

| 门禁 | 状态 | 说明 |
|---|---:|---|
| .NET 10 生产项目构建 | NOT RUN | 当前机器只有 SDK 8.0.129；未安装 .NET 10 SDK |
| 同源黑盒行为验证 | PASS | 测试专用 net8.0 兼容主机链接并编译生产源文件 |

生产 `VirtualPlc.csproj` 仍为 `net10.0`，没有为测试降级。兼容主机证明当前源文件在本机
可执行并通过公开接口行为验证，但不替代 .NET 10 的正式构建门禁。

## 已知规范限制

1. V6.0 没有故障码寄存器。
2. 没有分拣目标区域和区域占用量，无法闭环 NG/Pending 满盘判断。
3. 没有实际抓取槽位反馈，无法验证命令槽位与实际槽位冲突。
4. X/Y/Z 坐标单位、比例、符号和溢出规则未定义。
5. Retry_Cmd 的参数保留、确认与断线幂等规则未定义。
6. 虚拟进程不能替代真实编码器断电位置保持验收。

这些限制来自接口规范表达能力，不计为当前 VirtualPlc 自动行为测试失败。

## 复跑命令

```bash
cd /home/ubuntu/disk/pj1
bash VirtualPlc/scripts/validate.sh
```

脚本将 .NET CLI、NuGet、临时文件、构建文件和报告全部约束在 `pj1/VirtualPlc` 下。

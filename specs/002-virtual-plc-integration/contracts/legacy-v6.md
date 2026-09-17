# legacy-v6-u16-snapshot-20260917

这是本轮兼容适配标识，不代表PLC可在线报告版本，也不是双方已冻结的V1.3点表。

Modbus TCP Unit1；文档地址一基，PDU偏移=地址-1；每寄存器大端ushort，无物理单位。

| 区域 | 文档地址 | 本轮用途 |
| --- | --- | --- |
| Coil | 1/2/3 | PLC心跳/PC回显/PC就绪 |
| Coil | 4/5/6/10 | 故障/自动/软停/人工区占用 |
| Holding | 1/2 | XY命令/反馈(0等待,1完成,2失败) |
| Holding | 3/4/5 | X/Y/Z原始目标 |
| Holding | 6/7/8 | Z状态(0初态,1忙,2完成,3失败)/实际X/Y |
| Holding | 23/24 | 锁托盘命令/状态(1锁紧,2失败) |
| Holding | 25/26/27/28 | NG/Pending容量/区域提交/确认 |

仅支持隔离模拟器初态。新连接检查所有动作命令、反馈、PC就绪、故障、自动、软停；非初态RecoveryRequired，不能自行清故障或自动恢复。
置Ready，区域容量配置→提交→等待ACK，锁托盘→等待锁紧，然后两个顺序移动。
每次命令先0并等待50ms（固定模拟器10ms扫描），写全部XYZ，再写cmd=1；Modbus写ACK不是动作受理。
只有实际读到XY=0且Z=1才认为本轮已开始，然后XY=1、Z=2且X/Y匹配才完成。错过Busy即使done也不认可，最终Unknown。
轮询50ms并回显心跳；I/O超时关闭连接，任何故障/取消/超时均停止后续命令，无重连、无Retry_Cmd。
终态Unknown不代表已停，必须对账；本轮不提供解除对账或生产恢复入口。
正常结束不解锁，保留原反馈用于检查新连接拒绝。
工程探针只接受回环地址及显式协议标识；禁止调用flow-decision。

入口：Inspection.Host.dll --plc-probe --Plc:Contract legacy-v6-u16-snapshot-20260917 --Plc:Host 127.0.0.1 --Plc:Port PORT --Plc:Report PATH
可配置Plc:ActionTimeoutMs（默认3000），Plc:IoTimeoutMs（默认1000）；仅测试预算。
退出码Completed=0；其他终态=2；错误配置/启动失败非零。默认Host无设备连接。

# Data Model

- Modbus exchange: 单连接事务号、单元号、请求/响应十六进制；响应协议ID=0、事务/单元/功能必须匹配，长度有界。事务号不是物理去重号。
- Legacy target: X/Y/Z均为ushort原始量（0..65535），无单位、无真实机械坐标承诺。
- Probe result: contract、mode、outcome、reason、moves、exchanges；终态Completed/Failed/Unknown/RecoveryRequired。未发送动作不能标Completed。
- Move observation: 序号仅本地日志关联；ObservedBusy之后才允许Completed，并要求XY=1、Z=2、X/Y匹配。
- 状态: InitialCheck→Ready→ZoneConfigured→Locked→MoveSubmitted→BusyObserved→Completed；任一异常终止探针，不下发下一动作。
- 来源：archiveSha256 + files(path,sha256)，没有上游Git提交时不编造。

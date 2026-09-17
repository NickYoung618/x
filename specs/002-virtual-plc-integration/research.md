# Research

## R1 来源与运行栈
Decision: 引入已校验快照至simulator/VirtualPlc，保留原规格及旧报告；主仓库固定SDK10.0.401。
Rationale: 现有主机已是net10.0；旧驱动net8.0/C#默认12；用net10.0+C#12运行原断言即可消除环境适配。
Alternatives: 同装8/10增加依赖；直接升C#14会触发原测试Span跨await的CS4007，且本轮无需改断言。

## R2 兼容与风险
Decision: 仅legacy-v6-u16-snapshot-20260917显式模式，数值为原始ushort、无物理单位；新连接遇非初态拒绝。
Rationale: 旧清零不清反馈，且扫描需实际看到零；清零等待50ms只对已固定10ms扫描模拟器成立。新Busy未观察则Unknown，不能猜已完成。
Alternatives: 自动Retry清状态可能掩盖在途/重复动作；直接接受done会把旧反馈当新动作；二者均不用。

## R3 验证证据
Decision: 原13组与新增Host进程场景独立汇总；构建退出码和报告组数均验证，NOT RUN不放行。
Rationale: 原驱动总评只看检查，生产门禁须外层实际执行。
Alternatives: 原validate.sh保持历史，但不作为新CI入口；它能回退net8并将生产NOT RUN列为PASS。

## R4 协议研究
通过speckit-plan独立只读研究核对源DOCX、实际点表与V1.3，结论纳入contracts/legacy-v6.md及docs/contracts/virtual-plc-alignment.md。
本轮技术决定已解决；后续生产字序、会话及动作参数待双方确认，不通过猜测填充。

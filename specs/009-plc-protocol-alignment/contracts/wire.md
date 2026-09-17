# Wire contract

冻结的来源副本与逐点对照见 [PLC/上位机 2026-09-11 合同](../../../docs/contracts/plc-upper-20260911.md)。

- 新合同标识 `plc-upper-20260911-hex1-f32`；旧 V6 仅 `--plc-probe`。
- 虚拟测试口径：十六进制一基点号，PDU 偏移为点号减 1。示例 `4x000B → 0x000A`。
- Float32 两字，顺序必须显式选 ABCD/CDAB/BADC/DCBA；三个目标 Z 分别写入，不与实际 Z 混用。
- 真实配置只读；虚拟写入需显式开启，分拣整体动作等缺参数动作返回 `Unsupported`。

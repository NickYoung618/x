namespace VirtualPlc;

/// <summary>Raw Modbus memory boundary shared by isolated simulator profiles.</summary>
public interface IModbusDataStore
{
    bool[] ReadCoils(int pduOffset, int count);
    ushort[] ReadHoldingRegisters(int pduOffset, int count);
    bool TryWriteCoilsFromPc(int pduOffset, IReadOnlyList<bool> values);
    bool TryWriteHoldingRegistersFromPc(int pduOffset, IReadOnlyList<ushort> values);
}

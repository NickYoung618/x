namespace VirtualPlc;

public interface IModbusTraceSink
{
    void RecordExchange(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response);
}

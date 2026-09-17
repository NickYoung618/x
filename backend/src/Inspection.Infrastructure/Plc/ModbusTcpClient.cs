using System.Buffers.Binary;
using System.Net.Sockets;

namespace Inspection.Infrastructure.Plc;

public sealed record ModbusExchange(DateTimeOffset ObservedAtUtc, string Request,
    string? Response, string? Error);

/// <summary>One serialized TCP session. Addresses are zero-based PDU offsets. Never retries writes.</summary>
public sealed class ModbusTcpClient(string host, int port, byte unit, TimeSpan ioTimeout) : IAsyncDisposable
{
    private readonly TcpClient socket = new() { NoDelay = true };
    private readonly SemaphoreSlim gate = new(1);
    private readonly List<ModbusExchange> exchanges = [];
    private readonly object exchangeSync = new();
    private ushort transaction;
    private bool connected;
    private bool unusable;
    public IReadOnlyList<ModbusExchange> Exchanges { get { lock (exchangeSync) return exchanges.ToArray(); } }

    public async Task<ushort[]> ReadRegistersAsync(ushort offset, ushort count, CancellationToken ct = default)
    {
        if (count is < 1 or > 125 || (uint)offset + count > 65536) throw new ArgumentOutOfRangeException(nameof(count));
        var response = await ExchangeAsync(Pair(3, offset, count), ct);
        var values = new ushort[count];
        for (var i = 0; i < count; i++) values[i] = U16(response, 2 + i * 2);
        return values;
    }

    public async Task<bool[]> ReadCoilsAsync(ushort offset, ushort count, CancellationToken ct = default)
    {
        if (count is < 1 or > 2000 || (uint)offset + count > 65536) throw new ArgumentOutOfRangeException(nameof(count));
        var response = await ExchangeAsync(Pair(1, offset, count), ct);
        return Enumerable.Range(0, count).Select(i => (response[2 + i / 8] & (1 << (i % 8))) != 0).ToArray();
    }

    public async Task WriteCoilAsync(ushort offset, bool value, CancellationToken ct = default) =>
        _ = await ExchangeAsync(Pair(5, offset, value ? (ushort)0xFF00 : (ushort)0), ct);

    public async Task WriteRegisterAsync(ushort offset, ushort value, CancellationToken ct = default) =>
        _ = await ExchangeAsync(Pair(6, offset, value), ct);

    public async Task WriteRegistersAsync(ushort offset, ushort[] values, CancellationToken ct = default)
    {
        if (values.Length is < 1 or > 123 || (uint)offset + values.Length > 65536) throw new ArgumentOutOfRangeException(nameof(values));
        var pdu = new byte[6 + values.Length * 2];
        Pair(16, offset, (ushort)values.Length).CopyTo(pdu, 0);
        pdu[5] = (byte)(values.Length * 2);
        for (var i = 0; i < values.Length; i++) Put(pdu, 6 + i * 2, values[i]);
        _ = await ExchangeAsync(pdu, ct);
    }

    private async Task<byte[]> ExchangeAsync(byte[] pdu, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(ioTimeout);
        await gate.WaitAsync(deadline.Token);
        byte[]? request = null;
        byte[]? response = null;
        try
        {
            if (unusable) throw new InvalidOperationException("Connection requires reconciliation; automatic reconnect is disabled.");
            if (!connected)
            {
                await socket.ConnectAsync(host, port, deadline.Token);
                connected = true;
            }
            request = new byte[7 + pdu.Length];
            Put(request, 0, unchecked(++transaction));
            Put(request, 4, (ushort)(pdu.Length + 1));
            request[6] = unit;
            pdu.CopyTo(request, 7);
            var stream = socket.GetStream();
            await stream.WriteAsync(request, deadline.Token);
            var header = new byte[7];
            await stream.ReadExactlyAsync(header, deadline.Token);
            response = header;
            var length = U16(header, 4);
            if (U16(header, 0) != transaction || U16(header, 2) != 0 || header[6] != unit || length is < 2 or > 254)
                throw new IOException("Invalid Modbus MBAP header.");
            var body = new byte[length - 1];
            await stream.ReadExactlyAsync(body, deadline.Token);
            response = [.. header, .. body];
            if (body[0] == (pdu[0] | 0x80) && body.Length == 2)
                throw new IOException($"Modbus exception {body[1]:X2} for function {pdu[0]:X2}.");
            if (body[0] != pdu[0]) throw new IOException("Mismatched Modbus function.");
            if (pdu[0] is 1 or 3)
            {
                var bytes = pdu[0] == 1 ? (U16(pdu, 3) + 7) / 8 : U16(pdu, 3) * 2;
                if (body.Length != bytes + 2 || body[1] != bytes) throw new IOException("Invalid Modbus read byte count.");
            }
            else if (!body.SequenceEqual(pdu.Take(5))) throw new IOException("Invalid Modbus write acknowledgement.");
            lock (exchangeSync) exchanges.Add(new(DateTimeOffset.UtcNow,
                Convert.ToHexString(request), Convert.ToHexString(response), null));
            return body;
        }
        catch (Exception e)
        {
            unusable = true;
            socket.Dispose();
            lock (exchangeSync) exchanges.Add(new(DateTimeOffset.UtcNow,
                request is null ? "" : Convert.ToHexString(request),
                response is null ? null : Convert.ToHexString(response),
                $"{e.GetType().Name}: {e.Message}"));
            if (e is OperationCanceledException && !ct.IsCancellationRequested) throw new TimeoutException("Modbus I/O deadline exceeded; outcome may be unknown.", e);
            throw;
        }
        finally { gate.Release(); }
    }

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
    private static void Put(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset, 2), value);
    private static byte[] Pair(byte function, ushort offset, ushort value)
    {
        var bytes = new byte[5]; bytes[0] = function; Put(bytes, 1, offset); Put(bytes, 3, value); return bytes;
    }
    public ValueTask DisposeAsync() { socket.Dispose(); gate.Dispose(); return ValueTask.CompletedTask; }
}

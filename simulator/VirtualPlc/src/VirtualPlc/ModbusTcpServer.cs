using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace VirtualPlc;

public sealed class ModbusTcpServer : BackgroundService
{
    private readonly IModbusDataStore _store;
    private readonly ModbusOptions _options;
    private readonly ILogger<ModbusTcpServer> _logger;
    private TcpListener? _listener;
    private volatile bool _stopRequested;

    public ModbusTcpServer(
        IModbusDataStore store,
        IOptions<ModbusOptions> options,
        ILogger<ModbusTcpServer> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var address = IPAddress.Parse(_options.ListenAddress);
        _listener = new TcpListener(address, _options.Port);
        _listener.Start();
        _logger.LogInformation(
            "Virtual PLC Modbus TCP listening on {Address}:{Port}, UnitId={UnitId}",
            address, _options.Port, _options.UnitId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientSafelyAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (stoppingToken.IsCancellationRequested || _stopRequested)
        {
        }
        finally
        {
            _listener.Stop();
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _stopRequested = true;
        _listener?.Stop();
        return base.StopAsync(cancellationToken);
    }

    private async Task HandleClientSafelyAsync(TcpClient client, CancellationToken stoppingToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogInformation("Modbus client connected: {Remote}", remote);

        using (client)
        {
            client.NoDelay = true;
            try
            {
                await HandleClientAsync(client, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (EndOfStreamException)
            {
            }
            catch (IOException exception)
            {
                _logger.LogDebug(exception, "Modbus client disconnected: {Remote}", remote);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Modbus client failed: {Remote}", remote);
            }
        }

        _logger.LogInformation("Modbus client disconnected: {Remote}", remote);
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        var header = new byte[7];

        while (!cancellationToken.IsCancellationRequested)
        {
            await ReadExactlyAsync(stream, header, cancellationToken);

            var transactionId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
            var protocolId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
            var unitId = header[6];

            if (protocolId != 0 || length < 2 || length > 254)
            {
                throw new IOException("Invalid Modbus TCP header.");
            }

            var pdu = new byte[length - 1];
            await ReadExactlyAsync(stream, pdu, cancellationToken);

            byte[] responsePdu;
            if (unitId != _options.UnitId)
            {
                responsePdu = ExceptionResponse(pdu[0], 0x0B);
            }
            else
            {
                responsePdu = ProcessRequest(pdu);
            }

            var response = new byte[7 + responsePdu.Length];
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0, 2), transactionId);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), (ushort)(responsePdu.Length + 1));
            response[6] = unitId;
            responsePdu.CopyTo(response, 7);
            await stream.WriteAsync(response, cancellationToken);
            if (_store is IModbusTraceSink traceSink)
                traceSink.RecordExchange([.. header, .. pdu], response);
        }
    }

    private byte[] ProcessRequest(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length == 0)
        {
            return ExceptionResponse(0, 0x03);
        }

        try
        {
            return pdu[0] switch
            {
                0x01 => ReadCoils(pdu),
                0x03 => ReadHoldingRegisters(pdu),
                0x05 => WriteSingleCoil(pdu),
                0x06 => WriteSingleRegister(pdu),
                0x0F => WriteMultipleCoils(pdu),
                0x10 => WriteMultipleRegisters(pdu),
                _ => ExceptionResponse(pdu[0], 0x01)
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return ExceptionResponse(pdu[0], 0x02);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Invalid Modbus request, function {Function}", pdu[0]);
            return ExceptionResponse(pdu[0], 0x03);
        }
    }

    private byte[] ReadCoils(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length != 5)
        {
            return ExceptionResponse(0x01, 0x03);
        }

        var start = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
        var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
        if (count is < 1 or > 2000)
        {
            return ExceptionResponse(0x01, 0x03);
        }

        var values = _store.ReadCoils(start, count);
        var byteCount = (values.Length + 7) / 8;
        var response = new byte[2 + byteCount];
        response[0] = 0x01;
        response[1] = (byte)byteCount;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i])
            {
                response[2 + i / 8] |= (byte)(1 << (i % 8));
            }
        }

        return response;
    }

    private byte[] ReadHoldingRegisters(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length != 5)
        {
            return ExceptionResponse(0x03, 0x03);
        }

        var start = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
        var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
        if (count is < 1 or > 125)
        {
            return ExceptionResponse(0x03, 0x03);
        }

        var values = _store.ReadHoldingRegisters(start, count);
        var response = new byte[2 + values.Length * 2];
        response[0] = 0x03;
        response[1] = (byte)(values.Length * 2);
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2 + i * 2, 2), values[i]);
        }

        return response;
    }

    private byte[] WriteSingleCoil(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length != 5)
        {
            return ExceptionResponse(0x05, 0x03);
        }

        var offset = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
        var encoded = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
        if (encoded is not (0x0000 or 0xFF00))
        {
            return ExceptionResponse(0x05, 0x03);
        }

        if (!_store.TryWriteCoilsFromPc(offset, new[] { encoded == 0xFF00 }))
        {
            return ExceptionResponse(0x05, 0x02);
        }

        return pdu.ToArray();
    }

    private byte[] WriteSingleRegister(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length != 5)
        {
            return ExceptionResponse(0x06, 0x03);
        }

        var offset = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
        var value = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
        if (!_store.TryWriteHoldingRegistersFromPc(offset, new[] { value }))
        {
            return ExceptionResponse(0x06, 0x02);
        }

        return pdu.ToArray();
    }

    private byte[] WriteMultipleCoils(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length < 6)
        {
            return ExceptionResponse(0x0F, 0x03);
        }

        var start = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
        var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
        var byteCount = pdu[5];
        if (count is < 1 or > 1968 || byteCount != (count + 7) / 8 || pdu.Length != 6 + byteCount)
        {
            return ExceptionResponse(0x0F, 0x03);
        }

        var values = new bool[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = (pdu[6 + i / 8] & (1 << (i % 8))) != 0;
        }

        if (!_store.TryWriteCoilsFromPc(start, values))
        {
            return ExceptionResponse(0x0F, 0x02);
        }

        return WriteAcknowledgement(0x0F, start, count);
    }

    private byte[] WriteMultipleRegisters(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length < 6)
        {
            return ExceptionResponse(0x10, 0x03);
        }

        var start = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(1, 2));
        var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(3, 2));
        var byteCount = pdu[5];
        if (count is < 1 or > 123 || byteCount != count * 2 || pdu.Length != 6 + byteCount)
        {
            return ExceptionResponse(0x10, 0x03);
        }

        var values = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(6 + i * 2, 2));
        }

        if (!_store.TryWriteHoldingRegistersFromPc(start, values))
        {
            return ExceptionResponse(0x10, 0x02);
        }

        return WriteAcknowledgement(0x10, start, count);
    }

    private static byte[] WriteAcknowledgement(byte function, ushort start, ushort count)
    {
        var response = new byte[5];
        response[0] = function;
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(1, 2), start);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(3, 2), count);
        return response;
    }

    private static byte[] ExceptionResponse(byte function, byte exceptionCode) =>
        new[] { (byte)(function | 0x80), exceptionCode };

    private static async Task ReadExactlyAsync(
        NetworkStream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken);
            if (count == 0)
            {
                throw new EndOfStreamException();
            }

            read += count;
        }
    }
}

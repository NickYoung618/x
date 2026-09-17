using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;

using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5080") };
var resetResponse = await http.PostAsync("/api/simulator/reset", null);
resetResponse.EnsureSuccessStatusCode();
await AssertMonitorDashboardAsync(http);

await using var client = new ModbusSmokeClient("127.0.0.1", 1502, 1);
await client.ConnectAsync();

using var heartbeatCancellation = new CancellationTokenSource();
var heartbeatTask = EchoHeartbeatAsync(client, heartbeatCancellation.Token);

try
{
    await client.WriteCoilsAsync(2, new[] { false, true }); // heartbeat response + ready, FC15

    await client.WriteRegisterAsync(25, 4);  // NG_Zone_Count
    await client.WriteRegisterAsync(26, 3);  // Pending_Zone_Count
    await PulseRegisterAsync(client, 27, 1); // Zone_Config_Ready
    await WaitForRegisterAsync(client, 28, 1, "Zone_Config_Ack");

    await client.WriteRegisterAsync(23, 1); // Pallet_Lock_Cmd
    await WaitForRegisterAsync(client, 24, 1, "Pallet_Lock_Status");

    await client.WriteRegistersAsync(3, new ushort[] { 123, 456, 78 }); // X/Y/Z, FC16
    await PulseRegisterAsync(client, 1, 2);  // XY_Move_Cmd: inspection
    await WaitForRegisterAsync(client, 2, 1, "XY_Pos_Confirmed");
    await WaitForRegisterAsync(client, 6, 2, "Z_Axis_Move_Status");
    AssertEqual((ushort)123, await client.ReadRegisterAsync(7), "Machine_Current_Pos_X");
    AssertEqual((ushort)456, await client.ReadRegisterAsync(8), "Machine_Current_Pos_Y");

    await PulseRegisterAsync(client, 10, 2); // Flip_Trigger_Cmd: 180 degrees
    await WaitForRegisterAsync(client, 11, 2, "Flip_Status");
    AssertEqual((ushort)180, await client.ReadRegisterAsync(12), "Flip_Result_Angle");

    await client.WriteRegisterAsync(20, 5); // Sorting_Part_Index
    await PulseRegisterAsync(client, 21, 1); // Sorting_Cmd
    await WaitForRegisterAsync(client, 22, 2, "Sorting_Exec_Status");

    foreach (var category in new[] { "Camera", "Barcode", "Algorithm", "Recipe", "Storage", "Mes", "Other" })
    {
        await AssertFlowPolicyAsync(http, category, expectedContinue: true, expectedError: false);
    }

    await AssertFlowPolicyAsync(http, "DeviceAction", expectedContinue: false, expectedError: true);
    await AssertFlowPolicyAsync(http, "DeviceTimeout", expectedContinue: true, expectedError: true);
    await AssertFlowPolicyAsync(http, "PlcSafety", expectedContinue: false, expectedError: true);
    await AssertFlowPolicyAsync(http, "PlcCommunication", expectedContinue: false, expectedError: true);

    var faultResponse = await http.PostAsync("/api/simulator/faults/MoveTimeout", null);
    faultResponse.EnsureSuccessStatusCode();
    await PulseRegisterAsync(client, 1, 2);
    await WaitForRegisterAsync(client, 2, 2, "XY_Pos_Confirmed timeout");
    await WaitForRegisterAsync(client, 6, 3, "Z_Axis_Move_Status timeout");

    await AssertWriteRejectedAsync(client);

    resetResponse = await http.PostAsync("/api/simulator/reset", null);
    resetResponse.EnsureSuccessStatusCode();
    await client.WriteCoilAsync(3, true); // PC_System_Ready

    await client.WriteRegistersAsync(3, new ushort[] { 10, 20, 30 });
    await PulseRegisterAsync(client, 1, 2);
    await WaitForRegisterAsync(client, 2, 2, "move rejected before Zone_Config_Ack");

    await client.WriteRegisterAsync(25, 4);
    await client.WriteRegisterAsync(26, 3);
    await PulseRegisterAsync(client, 27, 1);
    await WaitForRegisterAsync(client, 28, 1, "Zone_Config_Ack after reset");

    await PulseRegisterAsync(client, 1, 2);
    await WaitForRegisterAsync(client, 6, 1, "Z_Axis_Move_Status moving");
    await client.WriteCoilAsync(6, true); // Soft_Stop_Cmd
    await WaitForRegisterAsync(client, 2, 2, "XY_Pos_Confirmed after soft stop");
    await WaitForRegisterAsync(client, 6, 3, "Z_Axis_Move_Status after soft stop");
    await client.WriteCoilAsync(6, false);

    await client.WriteRegisterAsync(20, 0); // Sorting_Part_Index is required for command 1
    await PulseRegisterAsync(client, 21, 1);
    await WaitForRegisterAsync(client, 22, 3, "sorting rejected for zero part index");

    var emergencyResponse = await http.PostAsync("/api/simulator/faults/EmergencyAlarm", null);
    emergencyResponse.EnsureSuccessStatusCode();
    var plcFault = await client.ReadCoilAsync(4);
    if (!plcFault)
    {
        throw new InvalidOperationException("PLC_System_Fault was not raised for EmergencyAlarm.");
    }

    Console.WriteLine("PASS PLC_System_Fault=1 for emergency alarm");

    Console.WriteLine("SMOKE TEST PASSED: address map, heartbeat, actions, failures and flow policy are valid.");
}
finally
{
    heartbeatCancellation.Cancel();
    try
    {
        await heartbeatTask;
    }
    catch (OperationCanceledException)
    {
    }
}

static async Task EchoHeartbeatAsync(ModbusSmokeClient client, CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        var heartbeat = await client.ReadCoilAsync(1, cancellationToken);
        await client.WriteCoilAsync(2, heartbeat, cancellationToken);
        await Task.Delay(100, cancellationToken);
    }
}

static async Task PulseRegisterAsync(ModbusSmokeClient client, int documentAddress, ushort value)
{
    await client.WriteRegisterAsync(documentAddress, 0);
    await Task.Delay(30);
    await client.WriteRegisterAsync(documentAddress, value);
}

static async Task WaitForRegisterAsync(
    ModbusSmokeClient client,
    int documentAddress,
    ushort expected,
    string name)
{
    var deadline = DateTime.UtcNow.AddSeconds(3);
    while (DateTime.UtcNow < deadline)
    {
        var actual = await client.ReadRegisterAsync(documentAddress);
        if (actual == expected)
        {
            Console.WriteLine($"PASS {name}={actual}");
            return;
        }

        await Task.Delay(25);
    }

    throw new InvalidOperationException(
        $"Timed out waiting for {name} (4x{documentAddress:0000})={expected}.");
}

static void AssertEqual(ushort expected, ushort actual, string name)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}.");
    }

    Console.WriteLine($"PASS {name}={actual}");
}

static async Task AssertFlowPolicyAsync(
    HttpClient client,
    string category,
    bool expectedContinue,
    bool expectedError)
{
    var json = await client.GetStringAsync(
        $"/api/simulator/flow-decision?category={category}&stepSucceeded=false");
    using var document = JsonDocument.Parse(json);
    var actual = document.RootElement.GetProperty("continueFlow").GetBoolean();
    var actualError = document.RootElement.GetProperty("shouldReportError").GetBoolean();
    if (actual != expectedContinue || actualError != expectedError)
    {
        throw new InvalidOperationException(
            $"Flow policy {category}: expected continue/error={expectedContinue}/{expectedError}, " +
            $"actual={actual}/{actualError}.");
    }

    Console.WriteLine($"PASS flow policy {category}: continue={actual}, reportError={actualError}");
}

static async Task AssertMonitorDashboardAsync(HttpClient client)
{
    var dashboard = await client.GetStringAsync("/");
    if (!dashboard.Contains("Virtual PLC 实时监控", StringComparison.Ordinal) ||
        !dashboard.Contains("线圈区", StringComparison.Ordinal) ||
        !dashboard.Contains("保持寄存器区", StringComparison.Ordinal) ||
        !dashboard.Contains("左侧 · 状态与反馈", StringComparison.Ordinal) ||
        !dashboard.Contains("右侧 · 命令与参数", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Virtual PLC monitor dashboard was not served correctly.");
    }

    var stateJson = await client.GetStringAsync("/api/simulator/state");
    using var state = JsonDocument.Parse(stateJson);
    var coils = state.RootElement.GetProperty("coils");
    var registers = state.RootElement.GetProperty("holdingRegisters");
    if (coils.GetArrayLength() == 0 || registers.GetArrayLength() == 0)
    {
        throw new InvalidOperationException("Monitor state did not contain both 0x and 4x points.");
    }

    foreach (var point in coils.EnumerateArray().Concat(registers.EnumerateArray()))
    {
        if (!point.TryGetProperty("address", out _) ||
            !point.TryGetProperty("direction", out var direction) ||
            string.IsNullOrWhiteSpace(direction.GetString()))
        {
            throw new InvalidOperationException("Monitor point is missing address or direction metadata.");
        }
    }

    Console.WriteLine(
        $"PASS monitor dashboard: {coils.GetArrayLength()} coils, " +
        $"{registers.GetArrayLength()} holding registers");
}

static async Task AssertWriteRejectedAsync(ModbusSmokeClient client)
{
    try
    {
        await client.WriteRegisterAsync(2, 99); // PLC-owned XY_Pos_Confirmed
    }
    catch (IOException)
    {
        Console.WriteLine("PASS PLC-owned register write rejected");
        return;
    }

    throw new InvalidOperationException("Writing a PLC-owned register should have been rejected.");
}

internal sealed class ModbusSmokeClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly byte _unitId;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly TcpClient _client = new();
    private NetworkStream? _stream;
    private ushort _transactionId;

    public ModbusSmokeClient(string host, int port, byte unitId)
    {
        _host = host;
        _port = port;
        _unitId = unitId;
    }

    public async Task ConnectAsync()
    {
        await _client.ConnectAsync(_host, _port);
        _client.NoDelay = true;
        _stream = _client.GetStream();
    }

    public async Task<bool> ReadCoilAsync(int documentAddress, CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(
            0x01,
            EncodeAddressAndValue(documentAddress - 1, 1),
            cancellationToken);
        return (response[2] & 0x01) != 0;
    }

    public async Task<ushort> ReadRegisterAsync(int documentAddress, CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(
            0x03,
            EncodeAddressAndValue(documentAddress - 1, 1),
            cancellationToken);
        return BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2, 2));
    }

    public Task WriteCoilAsync(
        int documentAddress,
        bool value,
        CancellationToken cancellationToken = default) =>
        RequestAsync(
            0x05,
            EncodeAddressAndValue(documentAddress - 1, value ? 0xFF00 : 0x0000),
            cancellationToken);

    public Task WriteCoilsAsync(
        int startDocumentAddress,
        IReadOnlyList<bool> values,
        CancellationToken cancellationToken = default)
    {
        var byteCount = (values.Count + 7) / 8;
        var payload = new byte[5 + byteCount];
        BinaryPrimitives.WriteUInt16BigEndian(
            payload.AsSpan(0, 2), checked((ushort)(startDocumentAddress - 1)));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), checked((ushort)values.Count));
        payload[4] = checked((byte)byteCount);
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i])
            {
                payload[5 + i / 8] |= (byte)(1 << (i % 8));
            }
        }

        return RequestAsync(0x0F, payload, cancellationToken);
    }

    public Task WriteRegisterAsync(
        int documentAddress,
        ushort value,
        CancellationToken cancellationToken = default) =>
        RequestAsync(
            0x06,
            EncodeAddressAndValue(documentAddress - 1, value),
            cancellationToken);

    public Task WriteRegistersAsync(
        int startDocumentAddress,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken = default)
    {
        var payload = new byte[5 + values.Count * 2];
        BinaryPrimitives.WriteUInt16BigEndian(
            payload.AsSpan(0, 2), checked((ushort)(startDocumentAddress - 1)));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), checked((ushort)values.Count));
        payload[4] = checked((byte)(values.Count * 2));
        for (var i = 0; i < values.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(5 + i * 2, 2), values[i]);
        }

        return RequestAsync(0x10, payload, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        _client.Dispose();
        _requestLock.Dispose();
    }

    private async Task<byte[]> RequestAsync(
        byte function,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            var transactionId = unchecked(++_transactionId);
            var request = new byte[8 + payload.Length];
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transactionId);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), (ushort)(payload.Length + 2));
            request[6] = _unitId;
            request[7] = function;
            payload.CopyTo(request, 8);

            await _stream.WriteAsync(request, cancellationToken);
            var header = new byte[7];
            await ReadExactlyAsync(_stream, header, cancellationToken);
            var responseTransaction = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
            if (responseTransaction != transactionId || length < 2)
            {
                throw new IOException("Invalid Modbus response header.");
            }

            var response = new byte[length - 1];
            await ReadExactlyAsync(_stream, response, cancellationToken);
            if ((response[0] & 0x80) != 0)
            {
                throw new IOException($"Modbus exception {response[1]} for function {function}.");
            }

            if (response[0] != function)
            {
                throw new IOException("Unexpected Modbus function in response.");
            }

            return response;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static byte[] EncodeAddressAndValue(int pduOffset, int value)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), checked((ushort)pduOffset));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), checked((ushort)value));
        return payload;
    }

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

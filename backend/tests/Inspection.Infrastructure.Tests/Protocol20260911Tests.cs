using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Inspection.Application.Plc;
using Inspection.Infrastructure.Plc;

namespace Inspection.Infrastructure.Tests;

public class Protocol20260911Tests
{
    [Theory]
    [InlineData(Float32ByteOrder.Abcd, 0x42F6, 0xE979)]
    [InlineData(Float32ByteOrder.Cdab, 0xE979, 0x42F6)]
    [InlineData(Float32ByteOrder.Badc, 0xF642, 0x79E9)]
    [InlineData(Float32ByteOrder.Dcba, 0x79E9, 0xF642)]
    public void Float32_has_two_words_and_explicit_calibrated_order(Float32ByteOrder order,
        ushort first, ushort second)
    {
        Assert.Equal(new[] { first, second }, Protocol20260911Float32.Encode(123.456f, order));
        Assert.Equal(123.456f, Protocol20260911Float32.Decode(first, second, order));
    }

    [Fact]
    public void Protocol_addresses_do_not_overlap_two_word_coordinates()
    {
        Assert.Equal(0x0A, Protocol20260911Map.Offset(Protocol20260911Map.GrabTargetZ));
        Assert.Equal(0x0C, Protocol20260911Map.Offset(Protocol20260911Map.CurrentX));
        Assert.Equal(0x14, Protocol20260911Map.Offset(Protocol20260911Map.FlipTargetFace));
        Assert.Equal(0x1F, Protocol20260911Map.Offset(Protocol20260911Map.SortPartIndex));
        Assert.Equal(0x4F, Protocol20260911Map.Offset(Protocol20260911Map.AlarmBits));
    }

    [Fact]
    public async Task Factory_requires_new_contract_and_blocks_real_writes()
    {
        await using var disabled = PlcDeviceFactory.Create(new PlcConnectionOptions());
        await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.ReadSnapshotAsync());
        var options = Virtual(1502);
        options.Contract = "srs-plc-v6.0-20260908-u16";
        Assert.Throws<ArgumentException>(() => PlcDeviceFactory.Create(options));
        options.Contract = Protocol20260911Map.Contract;
        options.Float32ByteOrder = null;
        Assert.Throws<ArgumentException>(() => PlcDeviceFactory.Create(options));
        options.Float32ByteOrder = Float32ByteOrder.Abcd;
        options.AddressConvention = "DecimalOneBased";
        Assert.Throws<ArgumentException>(() => PlcDeviceFactory.Create(options));
        options.AddressConvention = Protocol20260911Map.AddressConvention;
        options.Host = "192.0.2.1";
        Assert.Throws<ArgumentException>(() => PlcDeviceFactory.Create(options));
        options.Host = "127.0.0.1";
        options.Provider = "Real";
        Assert.Throws<ArgumentException>(() => PlcDeviceFactory.Create(options));
        options.AllowVirtualActions = false;
        await using var real = PlcDeviceFactory.Create(options);
        Assert.Equal(PlcPhase.Unsupported,
            (await real.SubmitMoveAsync(Guid.NewGuid(), Target())).Phase);
    }

    [Fact]
    public async Task Readonly_snapshot_uses_new_points_and_reports_face_alarm_and_three_axes()
    {
        await using var simulator = new WireSimulator();
        simulator.Coils[0] = true;
        simulator.Coils[4] = true;
        simulator.Coils[7] = true;
        simulator.Registers[1] = 1;
        simulator.Registers[18] = 2;
        simulator.SetFloat(12, 123.5f);
        simulator.SetFloat(14, -2.25f);
        simulator.SetFloat(16, 7.75f);
        simulator.Registers[21] = 2;
        simulator.Registers[22] = 3;
        simulator.Registers[0x21] = 2;
        simulator.Registers[0x23] = 1;
        simulator.Registers[0x27] = 1;
        simulator.Registers[0x4F] = (1 << 5) | (1 << 10);
        simulator.Registers[0x50] = 2;
        await using var device = PlcDeviceFactory.Create(Virtual(simulator.Port, allowActions: false));

        var snapshot = await device.ReadSnapshotAsync();

        Assert.True(snapshot.PlcReady);
        Assert.True(snapshot.PalletLocked);
        Assert.True(snapshot.ZoneConfigured);
        Assert.Equal((ushort)1, snapshot.PalletLockStatusCode);
        Assert.Equal((ushort)2, snapshot.ZStatusCode);
        Assert.Equal((123.5f, -2.25f, 7.75f), (snapshot.ActualX, snapshot.ActualY, snapshot.ActualZ));
        Assert.Equal((ushort)3, snapshot.CurrentFace);
        Assert.Equal((ushort)((1 << 5) | (1 << 10)), snapshot.AlarmBits);
        Assert.Equal((ushort)2, snapshot.AlarmSeverity);
        Assert.Equal(new[] { (byte)1, (byte)3, (byte)3, (byte)3 }, simulator.Functions);
        Assert.Equal(new[] { (ushort)0, (ushort)0, (ushort)0x1F, (ushort)0x4F }, simulator.Offsets);
        Assert.Empty(simulator.Writes);
    }

    [Fact]
    public async Task Virtual_move_writes_float32_targets_and_waits_for_matching_physical_feedback()
    {
        await using var simulator = new WireSimulator { SimulateMove = true };
        simulator.Coils[4] = true; // auto mode
        simulator.Coils[7] = true; // ready after PC ready
        simulator.Registers[0x23] = 1; // pallet locked
        await using var device = PlcDeviceFactory.Create(Virtual(simulator.Port, allowActions: true));

        Assert.Equal(PlcPhase.Ready, (await device.SetPcReadyAsync()).Phase);
        var id = Guid.NewGuid();
        var accepted = await device.SubmitMoveAsync(id, Target());
        Assert.Equal(PlcPhase.Accepted, accepted.Phase);
        Assert.True(accepted.ObservedBusy);
        Assert.Equal(PlcPhase.Completed, (await device.WaitForMoveAsync(id)).Phase);

        var x = Protocol20260911Float32.Encode(123.5f, Float32ByteOrder.Abcd);
        var y = Protocol20260911Float32.Encode(-2.25f, Float32ByteOrder.Abcd);
        var z = Protocol20260911Float32.Encode(7.75f, Float32ByteOrder.Abcd);
        Assert.Contains(simulator.Writes, w => w.Offset == 2 && w.Values.SequenceEqual([.. x, .. y]));
        Assert.Contains(simulator.Writes, w => w.Offset == 6 && w.Values.SequenceEqual(z));
        Assert.Contains(simulator.Writes, w => w.Offset == 0 && w.Values.SequenceEqual([(ushort)2]));
        Assert.Contains(((byte)16, (ushort)2), simulator.Functions.Zip(simulator.Offsets));
        Assert.Contains(((byte)16, (ushort)6), simulator.Functions.Zip(simulator.Offsets));
        Assert.Equal(PlcPhase.RecoveryRequired, (await device.SubmitMoveAsync(id, Target())).Phase);
        Assert.Equal(PlcPhase.Unsupported,
            (await device.SubmitSortAsync(Guid.NewGuid(), new PlcSortRequest(1, Target(), Target(), "NG"))).Phase);
    }

    [Fact]
    public async Task Virtual_flip_uses_face_number_and_rejects_wrong_actual_face()
    {
        await using var simulator = new WireSimulator { SimulateMove = true, SimulateFlip = true, ReturnWrongFace = true };
        simulator.Coils[4] = true;
        simulator.Coils[7] = true;
        simulator.Registers[0x23] = 1;
        await using var device = PlcDeviceFactory.Create(Virtual(simulator.Port));
        Assert.Equal(PlcPhase.Ready, (await device.SetPcReadyAsync()).Phase);
        var move = Guid.NewGuid();
        var flipPosition = Target() with { Purpose = PlcMovePurpose.FlipOrScan };
        Assert.Equal(PlcPhase.Accepted, (await device.SubmitMoveAsync(move, flipPosition)).Phase);
        Assert.Equal(PlcPhase.Completed, (await device.WaitForMoveAsync(move)).Phase);

        var flip = Guid.NewGuid();
        Assert.Equal(PlcPhase.Accepted, (await device.SubmitFlipAsync(flip, 3)).Phase);
        var mismatch = await device.WaitForFlipAsync(flip);
        Assert.Equal(PlcPhase.Failed, mismatch.Phase);
        Assert.Contains("requested 3, reported 2", mismatch.Reason);
        Assert.Contains(simulator.Writes, w => w.Offset == 0x14 && w.Values.SequenceEqual([(ushort)3]));
        Assert.DoesNotContain(simulator.Writes, w => w.Offset == 0x14 && w.Values.SequenceEqual([(ushort)90]));
        Assert.Equal(PlcPhase.RecoveryRequired, (await device.SubmitMoveAsync(Guid.NewGuid(), Target())).Phase);
    }

    [Fact]
    public async Task Virtual_move_without_running_feedback_becomes_unknown_and_is_not_replayed()
    {
        await using var simulator = new WireSimulator();
        simulator.Coils[4] = true;
        simulator.Coils[7] = true;
        simulator.Registers[0x23] = 1;
        var options = Virtual(simulator.Port);
        options.ActionTimeoutMs = 200;
        await using var device = PlcDeviceFactory.Create(options);
        Assert.Equal(PlcPhase.Ready, (await device.SetPcReadyAsync()).Phase);
        var id = Guid.NewGuid();
        Assert.Equal(PlcPhase.Unknown, (await device.SubmitMoveAsync(id, Target())).Phase);
        Assert.Equal(PlcPhase.RecoveryRequired, (await device.SubmitMoveAsync(Guid.NewGuid(), Target())).Phase);
        Assert.Equal(1, simulator.Writes.Count(w => w.Offset == 0 && w.Values.SequenceEqual([(ushort)2])));
    }

    [Fact]
    public async Task Stale_pc_ready_or_command_requires_reconciliation_before_any_motion()
    {
        await using (var staleReady = new WireSimulator())
        {
            staleReady.Coils[2] = true;
            staleReady.Coils[4] = true;
            await using var device = PlcDeviceFactory.Create(Virtual(staleReady.Port));
            Assert.Equal(PlcPhase.RecoveryRequired, (await device.SetPcReadyAsync()).Phase);
            Assert.Empty(staleReady.Writes);
        }
        await using (var staleCommand = new WireSimulator())
        {
            staleCommand.Coils[4] = true;
            staleCommand.Coils[7] = true;
            staleCommand.Registers[0] = 2;
            staleCommand.Registers[0x23] = 1;
            await using var device = PlcDeviceFactory.Create(Virtual(staleCommand.Port));
            Assert.Equal(PlcPhase.Ready, (await device.SetPcReadyAsync()).Phase);
            var result = await device.SubmitMoveAsync(Guid.NewGuid(), Target());
            Assert.Equal(PlcPhase.RecoveryRequired, result.Phase);
            Assert.Contains("Stale XY_Move_Cmd=2", result.Reason);
            Assert.DoesNotContain(staleCommand.Writes, w => w.Offset == 0);
        }
    }

    private static PlcMoveTarget Target() => new(123.5f, -2.25f, 7.75f,
        "synthetic-machine", "synthetic-mm", PlcMovePurpose.Inspect, PlcZTarget.Camera);

    private static PlcConnectionOptions Virtual(int port, bool allowActions = true) => new()
    {
        Provider = "Virtual", Contract = Protocol20260911Map.Contract,
        AddressConvention = Protocol20260911Map.AddressConvention,
        Host = "127.0.0.1", Port = port, UnitId = 1, IoTimeoutMs = 1000,
        ActionTimeoutMs = 2000, Float32ByteOrder = Float32ByteOrder.Abcd,
        CoordinateFrame = "synthetic-machine", Unit = "synthetic-mm",
        AllowVirtualActions = allowActions
    };

    private sealed class WireSimulator : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Task server;
        private int coreReadsAfterCommand;
        private int coreReadsAfterFlip;
        public bool[] Coils { get; } = new bool[64];
        public ushort[] Registers { get; } = new ushort[128];
        public List<byte> Functions { get; } = [];
        public List<ushort> Offsets { get; } = [];
        public List<(ushort Offset, ushort[] Values)> Writes { get; } = [];
        public bool SimulateMove { get; set; }
        public bool SimulateFlip { get; set; }
        public bool ReturnWrongFace { get; set; }
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public WireSimulator()
        {
            listener.Start();
            server = Task.Run(ServeAsync);
        }

        public void SetFloat(int offset, float value)
        {
            var words = Protocol20260911Float32.Encode(value, Float32ByteOrder.Abcd);
            Registers[offset] = words[0];
            Registers[offset + 1] = words[1];
        }

        private async Task ServeAsync()
        {
            try
            {
                using var peer = await listener.AcceptTcpClientAsync();
                var stream = peer.GetStream();
                while (true)
                {
                    var header = new byte[7];
                    await stream.ReadExactlyAsync(header);
                    var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
                    var request = new byte[length - 1];
                    await stream.ReadExactlyAsync(request);
                    var function = request[0];
                    var offset = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(1, 2));
                    Functions.Add(function);
                    Offsets.Add(offset);
                    var count = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(3, 2));
                    byte[] body;
                    if (function == 1)
                    {
                        body = new byte[2 + (count + 7) / 8];
                        body[0] = 1;
                        body[1] = (byte)(body.Length - 2);
                        for (var i = 0; i < count; i++)
                            if (Coils[offset + i]) body[2 + i / 8] |= (byte)(1 << (i % 8));
                    }
                    else if (function == 3)
                    {
                        body = new byte[2 + count * 2];
                        body[0] = 3;
                        body[1] = (byte)(count * 2);
                        for (var i = 0; i < count; i++)
                            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(2 + i * 2, 2), Registers[offset + i]);
                        if (SimulateMove && offset == 0 && Registers[0] != 0 && ++coreReadsAfterCommand == 1)
                        {
                            Registers[1] = 1;
                            Registers[18] = 2;
                            Array.Copy(Registers, 2, Registers, 12, 6);
                        }
                        if (SimulateFlip && offset == 0 && Registers[0x14] != 0 && ++coreReadsAfterFlip == 1)
                        {
                            Registers[21] = 2;
                            Registers[22] = ReturnWrongFace ? (ushort)2 : Registers[0x14];
                        }
                    }
                    else if (function == 5)
                    {
                        Coils[offset] = count == 0xFF00;
                        Writes.Add((offset, [count]));
                        body = request[..5];
                    }
                    else if (function == 6)
                    {
                        Registers[offset] = count;
                        Writes.Add((offset, [count]));
                        if (SimulateMove && offset == 0 && count != 0)
                        {
                            Registers[1] = 0;
                            Registers[18] = 1;
                        }
                        if (SimulateFlip && offset == 0x14 && count != 0)
                            Registers[21] = 1;
                        body = request[..5];
                    }
                    else if (function == 16)
                    {
                        var values = new ushort[count];
                        for (var i = 0; i < count; i++)
                            values[i] = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(6 + i * 2, 2));
                        Array.Copy(values, 0, Registers, offset, count);
                        Writes.Add((offset, values));
                        body = request[..5];
                    }
                    else throw new InvalidOperationException($"Unexpected function {function}.");
                    var response = new byte[7 + body.Length];
                    header.CopyTo(response, 0);
                    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), (ushort)(body.Length + 1));
                    body.CopyTo(response, 7);
                    await stream.WriteAsync(response);
                }
            }
            catch (Exception error) when (error is EndOfStreamException or IOException or SocketException or ObjectDisposedException) { }
        }

        public async ValueTask DisposeAsync()
        {
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }
}

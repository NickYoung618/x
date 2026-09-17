using System.Net;
using System.Net.Sockets;

namespace Inspection.Infrastructure.Plc;

public sealed record LegacyMove(ushort X, ushort Y, ushort Z, bool ObservedBusy);
public sealed record LegacyProbeResult(string Contract, string Mode, string Outcome, string Reason,
    IReadOnlyList<LegacyMove> Moves, IReadOnlyList<ModbusExchange> Exchanges);

/// <summary>Isolated engineering probe for the pinned simulator, not a production motion service.</summary>
public sealed class LegacyPlcProbe
{
    public const string Contract = "legacy-v6-u16-snapshot-20260917";
    private readonly ModbusTcpClient client;
    private readonly int actionTimeoutMs;
    private readonly List<LegacyMove> moves = [];
    private bool heartbeatValue;
    private long heartbeatEdge;

    public LegacyPlcProbe(ModbusTcpClient client, int actionTimeoutMs)
    {
        if (actionTimeoutMs is < 100 or > 10000) throw new ArgumentOutOfRangeException(nameof(actionTimeoutMs));
        this.client = client;
        this.actionTimeoutMs = actionTimeoutMs;
    }

    public static void ValidateEndpoint(string contract, string host, int port, int ioTimeoutMs)
    {
        if (contract != Contract) throw new ArgumentException("Explicit legacy simulator contract is required.");
        if (!IPAddress.TryParse(host, out var ip) || !IPAddress.IsLoopback(ip)) throw new ArgumentException("Probe requires a loopback simulator endpoint.");
        if (port is < 1024 or > 65535 || ioTimeoutMs is < 50 or > 5000) throw new ArgumentOutOfRangeException(nameof(port));
    }

    public async Task<LegacyProbeResult> RunAsync(CancellationToken ct = default)
    {
        try
        {
            var coils = await client.ReadCoilsAsync(0, 11, ct);
            var registers = await client.ReadRegistersAsync(0, 29, ct);
            int[] cleanOffsets = [0, 1, 5, 9, 10, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28];
            if (coils[2] || coils[3] || !coils[4] || coils[5] || coils[9] || coils[10] || cleanOffsets.Any(i => registers[i] != 0))
                return Result("RecoveryRequired", "Non-initial legacy device state; reconcile externally before starting. No writes sent.");
            heartbeatValue = coils[0]; heartbeatEdge = Environment.TickCount64;
            await client.WriteCoilAsync(1, heartbeatValue, ct);
            await client.WriteCoilAsync(2, true, ct);
            await client.WriteRegistersAsync(24, [2, 2], ct);
            await client.WriteRegisterAsync(26, 0, ct);
            await Task.Delay(50, ct);
            await client.WriteRegisterAsync(26, 1, ct);
            await WaitAsync(async token => (await client.ReadRegistersAsync(27, 1, token))[0] == 1, ct);
            await client.WriteRegisterAsync(22, 1, ct);
            await WaitAsync(async token =>
            {
                var status = (await client.ReadRegistersAsync(23, 1, token))[0];
                if (status == 2) throw new DeviceFailureException("Pallet lock failed.");
                return status == 1;
            }, ct);
            foreach (var target in new[] { new LegacyMove(123, 456, 78, false), new LegacyMove(321, 654, 87, false) })
            {
                await PulseHeartbeatAsync(ct);
                await client.WriteRegisterAsync(0, 0, ct);
                await Task.Delay(50, ct); // pinned 10ms scan: write ACK alone does not re-arm the edge
                await client.WriteRegistersAsync(2, [target.X, target.Y, target.Z], ct);
                await client.WriteRegisterAsync(0, 1, ct);
                var observedBusy = false;
                await WaitAsync(async token =>
                {
                    var state = await client.ReadRegistersAsync(1, 7, token);
                    if (state[0] == 2 || state[4] == 3) throw new DeviceFailureException("PLC reported move failure.");
                    if (state[0] == 0 && state[4] == 1) observedBusy = true;
                    if (!observedBusy || state[0] != 1 || state[4] != 2) return false;
                    if (state[5] != target.X || state[6] != target.Y) throw new IOException("Completed coordinates mismatch; reconciliation required.");
                    return true;
                }, ct);
                moves.Add(target with { ObservedBusy = observedBusy });
            }
            return Result("Completed", "Two legacy simulated moves completed after fresh busy observations; tray remains locked.");
        }
        catch (DeviceFailureException e) { return Result("Failed", e.Message); }
        catch (Exception e) when (e is IOException or SocketException or TimeoutException or OperationCanceledException)
        {
            return Result("Unknown", $"{e.GetType().Name}: {e.Message} No automatic retry, reconnect or next action; device is not confirmed stopped.");
        }
    }

    private async Task PulseHeartbeatAsync(CancellationToken ct)
    {
        var state = await client.ReadCoilsAsync(0, 11, ct);
        if (state[3] || !state[4] || state[5] || state[9]) throw new DeviceFailureException("PLC interlock/fault prohibits continuation.");
        if (state[0] != heartbeatValue) { heartbeatValue = state[0]; heartbeatEdge = Environment.TickCount64; }
        if (Environment.TickCount64 - heartbeatEdge > 3000) throw new TimeoutException("PLC heartbeat stopped changing.");
        await client.WriteCoilAsync(1, state[0], ct);
    }

    private async Task WaitAsync(Func<CancellationToken, Task<bool>> completed, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(actionTimeoutMs);
        while (true)
        {
            await PulseHeartbeatAsync(deadline.Token);
            if (await completed(deadline.Token)) return;
            await Task.Delay(50, deadline.Token);
        }
    }
    private LegacyProbeResult Result(string outcome, string reason) => new(Contract, "SimulatedEngineering", outcome, reason, moves, client.Exchanges);
    private sealed class DeviceFailureException(string message) : Exception(message);
}

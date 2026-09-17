using Inspection.Infrastructure.Plc;
using Microsoft.Extensions.Options;

namespace VirtualPlc;

public enum Protocol20260911Fault
{
    ButtonNotPressed,
    NotClamped,
    ManualZoneOccupied,
    Alarm,
    PauseHeartbeat,
    ClampFailure
}

public sealed record Protocol20260911Snapshot(
    string Contract, DateTimeOffset ObservedAtUtc, string[] Faults,
    bool PcReady, bool PlcReady, bool PhysicalStartPressed, bool PalletLocked,
    bool ZoneConfigured, bool ManualZoneOccupied, bool PlcFault,
    ushort AlarmBits, ushort AlarmSeverity, ushort[] CurrentX,
    ushort[] CurrentY, ushort[] CurrentZ);

/// <summary>Physical facts for the isolated 2026-09-11 synthetic profile.</summary>
public sealed class Protocol20260911Engine : BackgroundService
{
    private readonly Protocol20260911Store store;
    private readonly SimulationOptions options;
    private readonly object gate = new();
    private readonly HashSet<Protocol20260911Fault> faults = [];
    private long nextHeartbeat;
    private long lockDue;
    private long zoneDue;
    private bool requestedLock;

    public Protocol20260911Engine(Protocol20260911Store store, IOptions<SimulationOptions> options)
    {
        this.store = store;
        this.options = options.Value;
        store.CoilWritten += OnCoilWritten;
        store.RegisterWritten += OnRegisterWritten;
    }

    public Protocol20260911Snapshot Snapshot()
    {
        lock (gate)
        {
            ushort[] Position(ushort point) => store.ReadHoldingRegisters(Protocol20260911Map.Offset(point), 2);
            bool Coil(ushort point) => store.ReadCoils(Protocol20260911Map.Offset(point), 1)[0];
            ushort Reg(ushort point) => store.ReadHoldingRegisters(Protocol20260911Map.Offset(point), 1)[0];
            return new(Protocol20260911Map.Contract, DateTimeOffset.UtcNow,
                faults.Select(x => x.ToString()).OrderBy(x => x).ToArray(),
                Coil(Protocol20260911Map.PcReady), Coil(Protocol20260911Map.PlcReady),
                !faults.Contains(Protocol20260911Fault.ButtonNotPressed),
                Reg(Protocol20260911Map.PalletLockStatus) == 1,
                Reg(Protocol20260911Map.ZoneConfigAck) == 1,
                Coil(Protocol20260911Map.ManualZoneOccupied), Coil(Protocol20260911Map.PlcFault),
                Reg(Protocol20260911Map.AlarmBits), Reg(Protocol20260911Map.AlarmSeverity),
                Position(Protocol20260911Map.CurrentX), Position(Protocol20260911Map.CurrentY),
                Position(Protocol20260911Map.CurrentZ));
        }
    }

    public void SetFault(Protocol20260911Fault fault, bool active)
    {
        lock (gate)
        {
            if (active) faults.Add(fault);
            else faults.Remove(fault);
            store.Record("Fault", $"{fault} active={active}.");
            UpdateSafety();
            if (fault == Protocol20260911Fault.NotClamped && active)
                store.SetRegisterFromPlc(Protocol20260911Map.PalletLockStatus, 0);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        nextHeartbeat = Environment.TickCount64 + options.HeartbeatPeriodMs;
        while (!stoppingToken.IsCancellationRequested)
        {
            lock (gate)
            {
                var now = Environment.TickCount64;
                if (now >= nextHeartbeat)
                {
                    if (!faults.Contains(Protocol20260911Fault.PauseHeartbeat))
                    {
                        var current = store.ReadCoils(Protocol20260911Map.Offset(Protocol20260911Map.HeartbeatReq), 1)[0];
                        store.SetCoilFromPlc(Protocol20260911Map.HeartbeatReq, !current);
                    }
                    nextHeartbeat = now + options.HeartbeatPeriodMs;
                }
                if (lockDue != 0 && now >= lockDue)
                {
                    var status = faults.Contains(Protocol20260911Fault.ClampFailure) ||
                        faults.Contains(Protocol20260911Fault.ButtonNotPressed) ||
                        faults.Contains(Protocol20260911Fault.ManualZoneOccupied) ||
                        faults.Contains(Protocol20260911Fault.Alarm) ? (ushort)2 :
                        faults.Contains(Protocol20260911Fault.NotClamped) ? (ushort)0 :
                        requestedLock ? (ushort)1 : (ushort)0;
                    store.SetRegisterFromPlc(Protocol20260911Map.PalletLockStatus, status);
                    store.Record("Clamp", $"Physical clamp status={status}.");
                    lockDue = 0;
                }
                if (zoneDue != 0 && now >= zoneDue)
                {
                    var counts = store.ReadHoldingRegisters(Protocol20260911Map.Offset(Protocol20260911Map.NgZoneCount), 2);
                    store.SetRegisterFromPlc(Protocol20260911Map.ZoneConfigAck,
                        counts[0] > 0 && counts[1] > 0 ? (ushort)1 : (ushort)0);
                    store.Record("Zone", $"Configuration ACK; NG={counts[0]}, Pending={counts[1]}.");
                    zoneDue = 0;
                }
                UpdateSafety();
            }
            await Task.Delay(Math.Max(1, options.ScanPeriodMs), stoppingToken);
        }
    }

    private void OnCoilWritten(int offset, bool value)
    {
        lock (gate)
        {
            if (offset == Protocol20260911Map.Offset(Protocol20260911Map.SoftStop) && value)
            {
                store.Record("Stop", "PC soft-stop requested; actual motion stop remains unverified.");
                lockDue = 0;
                zoneDue = 0;
            }
            else if (offset == Protocol20260911Map.Offset(Protocol20260911Map.PcReady))
                store.Record("Ready", $"PC_System_Ready={value}.");
            UpdateSafety();
        }
    }

    private void OnRegisterWritten(int offset, ushort value)
    {
        lock (gate)
        {
            if (offset == Protocol20260911Map.Offset(Protocol20260911Map.PalletLockCommand))
            {
                requestedLock = value == 1;
                lockDue = Environment.TickCount64 + Math.Max(1, options.PalletLockDurationMs);
                store.Record("Clamp", $"Lock command={value}; acceptance and physical completion are separate.");
            }
            else if (offset == Protocol20260911Map.Offset(Protocol20260911Map.ZoneConfigReady))
            {
                if (value == 0)
                {
                    zoneDue = 0;
                    store.SetRegisterFromPlc(Protocol20260911Map.ZoneConfigAck, 0);
                }
                else if (value == 1)
                    zoneDue = Environment.TickCount64 + Math.Max(1, options.ZoneConfigDurationMs);
            }
        }
    }

    private void UpdateSafety()
    {
        var manual = faults.Contains(Protocol20260911Fault.ManualZoneOccupied);
        var alarm = faults.Contains(Protocol20260911Fault.Alarm);
        store.SetCoilFromPlc(Protocol20260911Map.ManualZoneOccupied, manual);
        store.SetCoilFromPlc(Protocol20260911Map.PlcFault, alarm);
        store.SetRegisterFromPlc(Protocol20260911Map.AlarmBits, alarm ? (ushort)1 : (ushort)0);
        store.SetRegisterFromPlc(Protocol20260911Map.AlarmSeverity, alarm ? (ushort)3 : (ushort)0);
        var pcReady = store.ReadCoils(Protocol20260911Map.Offset(Protocol20260911Map.PcReady), 1)[0];
        var stopped = store.ReadCoils(Protocol20260911Map.Offset(Protocol20260911Map.SoftStop), 1)[0];
        store.SetCoilFromPlc(Protocol20260911Map.PlcReady, pcReady && !manual && !alarm && !stopped);
    }
}

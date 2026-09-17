using System.Net.Sockets;
using Inspection.Application.Plc;

namespace Inspection.Infrastructure.Plc;

/// <summary>
/// 2026-09-11 wire profile. Physical writes are deliberately limited to a loopback virtual
/// endpoint; the published contract lacks action IDs and several mechanism parameters.
/// </summary>
public sealed class Protocol20260911PlcDevice : IPlcDevice
{
    private readonly PlcConnectionOptions options;
    private readonly ModbusTcpClient transport;
    private readonly SemaphoreSlim gate = new(1);
    private readonly CancellationTokenSource heartbeatStop = new();
    private readonly HashSet<Guid> usedOperations = [];
    private Task? heartbeatTask;
    private Guid? activeOperation;
    private PlcMoveTarget? activeMove;
    private ushort activeFace;
    private PlcMovePurpose? lastCompletedPurpose;
    private long deadline;
    private bool pcReadySet;
    private volatile bool recoveryRequired;
    private string? lastDeviceError;
    private bool? heartbeatValue;
    private long heartbeatEdge;

    internal Protocol20260911PlcDevice(PlcConnectionOptions options, ModbusTcpClient transport)
    {
        this.options = options;
        this.transport = transport;
    }

    public string Source => options.Provider;
    public string Contract => Protocol20260911Map.Contract;
    public IReadOnlyList<ModbusExchange> Exchanges => transport.Exchanges;
    private bool CanWrite => options.Provider == "Virtual" && options.AllowVirtualActions;
    private ushort O(ushort point) => Protocol20260911Map.Offset(point);
    private Float32ByteOrder Order => options.Float32ByteOrder!.Value;

    public async Task<PlcSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var coils = await transport.ReadCoilsAsync(0, 16, cancellationToken);
            var core = await transport.ReadRegistersAsync(0, 0x17, cancellationToken);
            var sort = await transport.ReadRegistersAsync(0x1F, 9, cancellationToken);
            var alarms = await transport.ReadRegistersAsync(O(Protocol20260911Map.AlarmBits), 2, cancellationToken);
            float Position(ushort point) => Protocol20260911Float32.Decode(core[O(point)], core[O(point) + 1], Order);
            return new PlcSnapshot(Source, DateTimeOffset.UtcNow,
                coils[O(Protocol20260911Map.HeartbeatReq)], coils[O(Protocol20260911Map.PlcReady)],
                coils[O(Protocol20260911Map.AutoMode)], coils[O(Protocol20260911Map.PlcFault)],
                coils[O(Protocol20260911Map.ManualZoneOccupied)],
                sort[O(Protocol20260911Map.PalletLockStatus) - 0x1F] == 1,
                sort[O(Protocol20260911Map.ZoneConfigAck) - 0x1F] == 1,
                XyState(core[O(Protocol20260911Map.XyPositionConfirmed)]),
                ZState(core[O(Protocol20260911Map.ZMoveStatus)]),
                Position(Protocol20260911Map.CurrentX), Position(Protocol20260911Map.CurrentY),
                Position(Protocol20260911Map.CurrentZ),
                ActionState(core[O(Protocol20260911Map.FlipStatus)]),
                core[O(Protocol20260911Map.FlipCurrentFace)],
                SortState(sort[O(Protocol20260911Map.SortStatus) - 0x1F]),
                alarms[0], alarms[1], options.CoordinateFrame, options.Unit)
            {
                PcReadyObserved = coils[O(Protocol20260911Map.PcReady)],
                MoveCommandCode = core[O(Protocol20260911Map.MoveCommand)],
                FlipTargetFaceCode = core[O(Protocol20260911Map.FlipTargetFace)],
                XyStatusCode = core[O(Protocol20260911Map.XyPositionConfirmed)],
                ZStatusCode = core[O(Protocol20260911Map.ZMoveStatus)],
                FlipStatusCode = core[O(Protocol20260911Map.FlipStatus)],
                SortStatusCode = sort[O(Protocol20260911Map.SortStatus) - 0x1F],
                PalletLockStatusCode = sort[O(Protocol20260911Map.PalletLockStatus) - 0x1F],
                ZoneConfigAckCode = sort[O(Protocol20260911Map.ZoneConfigAck) - 0x1F]
            };
        }
        catch (Exception error) when (IsUncertain(error))
        {
            recoveryRequired = true;
            lastDeviceError = $"ReadSnapshot: {error.GetType().Name}: {error.Message}";
            throw;
        }
    }

    public async Task<PlcResult> SetPcReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanWrite) return Unsupported("PC readiness write requires the explicit virtual-action profile.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (recoveryRequired || pcReadySet) return Recovery("Prior state requires reconciliation or PC is already ready.");
            var initial = await ReadSnapshotAsync(cancellationToken);
            if (initial.PcReadyObserved)
                return RequireReconciliation("PC_System_Ready was already set before this session; verify prior actions.");
            if (initial.Fault || !initial.AutoMode || initial.ManualZoneOccupied || initial.AlarmBits != 0 || initial.AlarmSeverity >= 2)
                return Fail($"PLC readiness blocked: Fault={initial.Fault}, Auto={initial.AutoMode}, " +
                    $"ManualZone={initial.ManualZoneOccupied}, AlarmBits=0x{initial.AlarmBits:X4}, " +
                    $"AlarmSeverity={initial.AlarmSeverity}.");
            await transport.WriteCoilAsync(O(Protocol20260911Map.HeartbeatResp), initial.PlcHeartbeat, cancellationToken);
            heartbeatValue = initial.PlcHeartbeat;
            heartbeatEdge = Environment.TickCount64;
            await transport.WriteCoilAsync(O(Protocol20260911Map.PcReady), true, cancellationToken);
            await PollAsync(async ct => (await ReadSnapshotAsync(ct)).PlcReady, cancellationToken);
            pcReadySet = true;
            heartbeatTask = RunHeartbeatAsync(heartbeatStop.Token);
            return Result(Guid.Empty, PlcPhase.Ready, "PLC_Ready_State observed after PC_System_Ready.");
        }
        catch (Exception error) when (IsUncertain(error)) { return Unknown(Guid.Empty, error); }
        finally { gate.Release(); }
    }

    public async Task<PlcResult> SetPalletLockAsync(bool locked, CancellationToken cancellationToken = default)
    {
        if (!CanWrite) return Unsupported("Pallet lock write requires the explicit virtual-action profile.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!pcReadySet || recoveryRequired || activeOperation is not null)
                return Recovery("Device is not ready or another action may be active.");
            await transport.WriteRegisterAsync(O(Protocol20260911Map.PalletLockCommand), locked ? (ushort)1 : (ushort)0, cancellationToken);
            await PollAsync(async ct =>
            {
                var status = (await transport.ReadRegistersAsync(O(Protocol20260911Map.PalletLockStatus), 1, ct))[0];
                if (status == 2) throw new DeviceFailureException("Pallet clamp failed.");
                return status == (locked ? 1 : 0);
            }, cancellationToken);
            return Result(Guid.Empty, PlcPhase.Completed, locked ? "Pallet clamp confirmed." : "Pallet release confirmed.");
        }
        catch (DeviceFailureException error) { return Fail(error.Message); }
        catch (Exception error) when (IsUncertain(error)) { return Unknown(Guid.Empty, error); }
        finally { gate.Release(); }
    }

    public async Task<PlcResult> ConfigureZonesAsync(ushort ngCapacity, ushort pendingCapacity,
        CancellationToken cancellationToken = default)
    {
        if (!CanWrite) return Unsupported("Zone configuration requires the explicit virtual-action profile.");
        if (ngCapacity == 0 || pendingCapacity == 0) throw new ArgumentOutOfRangeException(nameof(ngCapacity));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!pcReadySet || recoveryRequired || activeOperation is not null)
                return Recovery("Device is not ready or another action may be active.");
            await transport.WriteRegisterAsync(O(Protocol20260911Map.ZoneConfigReady), 0, cancellationToken);
            await PollAsync(async ct =>
                (await transport.ReadRegistersAsync(O(Protocol20260911Map.ZoneConfigAck), 1, ct))[0] == 0,
                cancellationToken);
            await transport.WriteRegistersAsync(O(Protocol20260911Map.NgZoneCount), [ngCapacity, pendingCapacity], cancellationToken);
            await transport.WriteRegisterAsync(O(Protocol20260911Map.ZoneConfigReady), 1, cancellationToken);
            await PollAsync(async ct =>
                (await transport.ReadRegistersAsync(O(Protocol20260911Map.ZoneConfigAck), 1, ct))[0] == 1,
                cancellationToken);
            return Result(Guid.Empty, PlcPhase.Completed, "Fresh zone configuration acknowledgement observed.");
        }
        catch (Exception error) when (IsUncertain(error)) { return Unknown(Guid.Empty, error); }
        finally { gate.Release(); }
    }

    public async Task<PlcResult> SubmitMoveAsync(Guid operationId, PlcMoveTarget target,
        CancellationToken cancellationToken = default)
    {
        if (!CanWrite) return Unsupported("Motion is enabled only for a loopback virtual PLC.", operationId);
        ValidateOperation(operationId);
        ValidateTarget(target);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!pcReadySet || recoveryRequired || activeOperation is not null || !usedOperations.Add(operationId))
                return Recovery("Device is not ready, has an active/unknown action, or OperationId was reused.", operationId);
            var before = await ReadSnapshotAsync(cancellationToken);
            if (before.MoveCommandCode != 0)
                return RequireReconciliation($"Stale XY_Move_Cmd={before.MoveCommandCode} must be reconciled.", operationId);
            if (!SafeForMotion(before) || before.ZState == PlcMotionState.Running || before.XyState == PlcMotionState.Failed)
                return Fail($"PLC motion blocked: Ready={before.PlcReady}, Auto={before.AutoMode}, " +
                    $"Fault={before.Fault}, ManualZone={before.ManualZoneOccupied}, Clamp={before.PalletLockStatusCode}, " +
                    $"AlarmBits=0x{before.AlarmBits:X4}, Severity={before.AlarmSeverity}, " +
                    $"XY={before.XyStatusCode}, Z={before.ZStatusCode}.", operationId);
            activeOperation = operationId;
            activeMove = target;
            deadline = Environment.TickCount64 + options.ActionTimeoutMs;
            await transport.WriteRegisterAsync(O(Protocol20260911Map.MoveCommand), 0, cancellationToken);
            var x = Protocol20260911Float32.Encode(target.X, Order);
            var y = Protocol20260911Float32.Encode(target.Y, Order);
            var z = Protocol20260911Float32.Encode(target.Z, Order);
            await transport.WriteRegistersAsync(O(Protocol20260911Map.CameraTargetX), [.. x, .. y], cancellationToken);
            var zPoint = target.ZTarget switch
            {
                PlcZTarget.Camera => Protocol20260911Map.CameraTargetZ,
                PlcZTarget.Scan => Protocol20260911Map.ScanTargetZ,
                PlcZTarget.Grab => Protocol20260911Map.GrabTargetZ,
                _ => throw new ArgumentOutOfRangeException(nameof(target))
            };
            await transport.WriteRegistersAsync(O(zPoint), z, cancellationToken);
            await transport.WriteRegisterAsync(O(Protocol20260911Map.MoveCommand), (ushort)target.Purpose, cancellationToken);
            await PollAsync(async ct =>
            {
                var state = await ReadSnapshotAsync(ct);
                if (!SafeForMotion(state) || state.XyState == PlcMotionState.Failed || state.ZState == PlcMotionState.Failed)
                    throw new DeviceFailureException("PLC reported unsafe or failed motion.");
                return state.XyState == PlcMotionState.Running && state.ZState == PlcMotionState.Running;
            }, cancellationToken);
            return Result(operationId, PlcPhase.Accepted, "Fresh XY/Z running states observed in this virtual session.", true);
        }
        catch (DeviceFailureException error) { return Fail(error.Message, operationId); }
        catch (Exception error) when (IsUncertain(error)) { return Unknown(operationId, error); }
        finally { gate.Release(); }
    }

    public async Task<PlcResult> WaitForMoveAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (!CanWrite) return Unsupported("Motion is enabled only for a loopback virtual PLC.", operationId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (recoveryRequired || activeOperation != operationId || activeMove is null)
                return Recovery("No matching accepted move exists.", operationId);
            var target = activeMove;
            await PollAsync(async ct =>
            {
                var state = await ReadSnapshotAsync(ct);
                if (!SafeForMotion(state) || state.XyState == PlcMotionState.Failed || state.ZState == PlcMotionState.Failed)
                    throw new DeviceFailureException("PLC reported unsafe or failed motion.");
                if (state.XyState != PlcMotionState.Completed || state.ZState != PlcMotionState.Completed)
                    return false;
                if (state.ActualX != target.X || state.ActualY != target.Y || state.ActualZ != target.Z)
                    throw new IOException($"Physical coordinate mismatch: requested ({target.X}, {target.Y}, {target.Z}), " +
                        $"reported ({state.ActualX}, {state.ActualY}, {state.ActualZ}).");
                return true;
            }, cancellationToken);
            lastCompletedPurpose = target.Purpose;
            await transport.WriteRegisterAsync(O(Protocol20260911Map.MoveCommand), 0, cancellationToken);
            activeOperation = null;
            activeMove = null;
            return Result(operationId, PlcPhase.Completed, "XY/Z completion and matching coordinates observed.", true);
        }
        catch (DeviceFailureException error) { return Fail(error.Message, operationId); }
        catch (Exception error) when (IsUncertain(error)) { return Unknown(operationId, error); }
        finally { gate.Release(); }
    }

    public async Task<PlcResult> SubmitFlipAsync(Guid operationId, ushort targetFace,
        CancellationToken cancellationToken = default)
    {
        if (!CanWrite) return Unsupported("Flip is enabled only for a loopback virtual PLC.", operationId);
        ValidateOperation(operationId);
        if (targetFace == 0) throw new ArgumentOutOfRangeException(nameof(targetFace));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!pcReadySet || recoveryRequired || activeOperation is not null || !usedOperations.Add(operationId))
                return Recovery("Device is not ready or OperationId was reused.", operationId);
            if (lastCompletedPurpose != PlcMovePurpose.FlipOrScan)
                return Fail("A completed move to the flip/scan station is required.", operationId);
            var before = await ReadSnapshotAsync(cancellationToken);
            if (before.FlipTargetFaceCode != 0)
                return RequireReconciliation($"Stale Flip_Target_Face={before.FlipTargetFaceCode} must be reconciled.", operationId);
            if (!SafeForMotion(before) || before.FlipState == PlcMotionState.Running)
                return Fail("PLC interlock or prior flip prohibits a new flip.", operationId);
            activeOperation = operationId;
            activeFace = targetFace;
            deadline = Environment.TickCount64 + options.ActionTimeoutMs;
            await transport.WriteRegisterAsync(O(Protocol20260911Map.FlipTargetFace), 0, cancellationToken);
            await transport.WriteRegisterAsync(O(Protocol20260911Map.FlipTargetFace), targetFace, cancellationToken);
            await PollAsync(async ct =>
            {
                var state = await ReadSnapshotAsync(ct);
                if (!SafeForMotion(state) || state.FlipState == PlcMotionState.Failed)
                    throw new DeviceFailureException("PLC reported unsafe or failed flip.");
                return state.FlipState == PlcMotionState.Running;
            }, cancellationToken);
            return Result(operationId, PlcPhase.Accepted, "Fresh flip-running state observed.", true);
        }
        catch (DeviceFailureException error) { return Fail(error.Message, operationId); }
        catch (Exception error) when (IsUncertain(error)) { return Unknown(operationId, error); }
        finally { gate.Release(); }
    }

    public async Task<PlcResult> WaitForFlipAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (!CanWrite) return Unsupported("Flip is enabled only for a loopback virtual PLC.", operationId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (recoveryRequired || activeOperation != operationId || activeFace == 0)
                return Recovery("No matching accepted flip exists.", operationId);
            await PollAsync(async ct =>
            {
                var state = await ReadSnapshotAsync(ct);
                if (!SafeForMotion(state) || state.FlipState == PlcMotionState.Failed)
                    throw new DeviceFailureException("PLC reported unsafe or failed flip.");
                if (state.FlipState != PlcMotionState.Completed) return false;
                if (state.CurrentFace != activeFace)
                    throw new DeviceFailureException($"PLC face mismatch: requested {activeFace}, reported {state.CurrentFace}.");
                return true;
            }, cancellationToken);
            await transport.WriteRegisterAsync(O(Protocol20260911Map.FlipTargetFace), 0, cancellationToken);
            activeOperation = null;
            activeFace = 0;
            lastCompletedPurpose = null; // V1.3 requires another 3D scan before detecting this face.
            return Result(operationId, PlcPhase.Completed, "Matching face completion observed; 3D rescan remains required.", true);
        }
        catch (DeviceFailureException error) { return Fail(error.Message, operationId); }
        catch (Exception error) when (IsUncertain(error)) { return Unknown(operationId, error); }
        finally { gate.Release(); }
    }

    public Task<PlcResult> SubmitSortAsync(Guid operationId, PlcSortRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateOperation(operationId);
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(Unsupported("Protocol defines a slot index but no destination/pickup/drop command or whole-action acknowledgement; sort is blocked.", operationId));
    }

    public async Task<PlcResult> RequestSoftStopAsync(CancellationToken cancellationToken = default)
    {
        if (!CanWrite) return Unsupported("Soft-stop write requires the explicit virtual-action profile.");
        recoveryRequired = true;
        try
        {
            await transport.WriteCoilAsync(O(Protocol20260911Map.SoftStop), true, cancellationToken);
            return Result(Guid.Empty, PlcPhase.Accepted, "Soft-stop request delivered; physical stop is not confirmed.");
        }
        catch (Exception error) when (IsUncertain(error)) { return Unknown(Guid.Empty, error); }
    }

    public async Task<PlcResult> ConfirmManualFlipAsync(CancellationToken cancellationToken = default)
    {
        if (!CanWrite) return Unsupported("Manual-flip confirmation requires the explicit virtual-action profile.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (activeOperation is not null) return Recovery("Active action requires reconciliation.");
            var state = await ReadSnapshotAsync(cancellationToken);
            if (!state.ManualZoneOccupied) return Fail("No manual-zone occupancy is present.");
            await transport.WriteCoilAsync(O(Protocol20260911Map.ManualFlipComplete), true, cancellationToken);
            await PollAsync(async ct => !(await ReadSnapshotAsync(ct)).ManualZoneOccupied, cancellationToken);
            recoveryRequired = true;
            return Result(Guid.Empty, PlcPhase.Completed, "Manual-zone signal cleared; operator reconciliation is still required.");
        }
        catch (Exception error) when (IsUncertain(error)) { return Unknown(Guid.Empty, error); }
        finally { gate.Release(); }
    }

    private async Task PollAsync(Func<CancellationToken, Task<bool>> predicate, CancellationToken cancellationToken)
    {
        var until = activeOperation is null ? Environment.TickCount64 + options.ActionTimeoutMs : deadline;
        while (true)
        {
            var remaining = until - Environment.TickCount64;
            if (remaining <= 0) throw new TimeoutException("PLC action deadline exceeded; physical state is unknown.");
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(TimeSpan.FromMilliseconds(remaining));
            try
            {
                if (await predicate(bounded.Token)) return;
                await Task.Delay(25, bounded.Token);
            }
            catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("PLC action deadline exceeded; physical state is unknown.", error);
            }
        }
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var heartbeat = (await transport.ReadCoilsAsync(O(Protocol20260911Map.HeartbeatReq), 1, cancellationToken))[0];
                if (heartbeatValue != heartbeat)
                {
                    heartbeatValue = heartbeat;
                    heartbeatEdge = Environment.TickCount64;
                }
                if (Environment.TickCount64 - heartbeatEdge > 3000)
                    throw new TimeoutException("PLC heartbeat stopped changing.");
                await transport.WriteCoilAsync(O(Protocol20260911Map.HeartbeatResp), heartbeat, cancellationToken);
                await Task.Delay(50, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            lastDeviceError = $"Heartbeat: {error.GetType().Name}: {error.Message}";
            recoveryRequired = true;
        }
    }

    private bool SafeForMotion(PlcSnapshot state) => pcReadySet && state.PlcReady &&
        state.AutoMode && !state.Fault && !state.ManualZoneOccupied && state.PalletLocked &&
        state.AlarmBits == 0 && state.AlarmSeverity < 2 && !recoveryRequired;

    private void ValidateTarget(PlcMoveTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y) || !float.IsFinite(target.Z))
            throw new ArgumentOutOfRangeException(nameof(target), "Coordinates must be finite Float32 values.");
        if (target.CoordinateFrame != options.CoordinateFrame || target.Unit != options.Unit)
            throw new ArgumentException("Coordinate frame and unit must match the calibrated profile.", nameof(target));
        if (!Enum.IsDefined(target.Purpose) || !Enum.IsDefined(target.ZTarget))
            throw new ArgumentOutOfRangeException(nameof(target), "Move purpose and Z target must be defined.");
    }

    private static void ValidateOperation(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("OperationId is required.", nameof(id));
    }

    private PlcResult Result(Guid id, PlcPhase phase, string reason, bool busy = false) =>
        new(id, phase, Source, reason, busy);
    private PlcResult Unsupported(string reason, Guid id = default) => Result(id, PlcPhase.Unsupported, reason);
    private PlcResult Recovery(string reason, Guid id = default) => Result(id, PlcPhase.RecoveryRequired,
        lastDeviceError is null ? reason : $"{reason} Last device error: {lastDeviceError}");
    private PlcResult RequireReconciliation(string reason, Guid id = default)
    {
        recoveryRequired = true;
        lastDeviceError = reason;
        return Result(id, PlcPhase.RecoveryRequired, reason);
    }
    private PlcResult Fail(string reason, Guid id = default)
    {
        recoveryRequired = true;
        lastDeviceError = reason;
        return Result(id, PlcPhase.Failed, reason);
    }
    private PlcResult Unknown(Guid id, Exception error)
    {
        recoveryRequired = true;
        lastDeviceError = $"{error.GetType().Name}: {error.Message}";
        return Result(id, PlcPhase.Unknown, lastDeviceError);
    }
    private static bool IsUncertain(Exception error) => error is IOException or SocketException or
        TimeoutException or OperationCanceledException or InvalidOperationException or InvalidDataException;
    private static PlcMotionState XyState(ushort code) => code switch
    {
        0 => PlcMotionState.Running, 1 => PlcMotionState.Completed, 2 => PlcMotionState.Failed,
        _ => PlcMotionState.Inconsistent
    };
    private static PlcMotionState ZState(ushort code) => code switch
    {
        0 => PlcMotionState.Idle, 1 => PlcMotionState.Running, 2 => PlcMotionState.Completed,
        3 => PlcMotionState.Failed, _ => PlcMotionState.Inconsistent
    };
    private static PlcMotionState ActionState(ushort code) => code switch
    {
        0 => PlcMotionState.Idle, 1 => PlcMotionState.Running, 2 => PlcMotionState.Completed,
        3 => PlcMotionState.Failed, _ => PlcMotionState.Inconsistent
    };
    private static PlcSortState SortState(ushort code) => code switch
    {
        0 => PlcSortState.Idle, 1 => PlcSortState.Running, 2 => PlcSortState.Completed,
        3 => PlcSortState.PickFailed, 4 => PlcSortState.Full, _ => PlcSortState.Inconsistent
    };

    public async ValueTask DisposeAsync()
    {
        heartbeatStop.Cancel();
        if (heartbeatTask is not null) await heartbeatTask;
        await transport.DisposeAsync();
        gate.Dispose();
        heartbeatStop.Dispose();
    }

    private sealed class DeviceFailureException(string message) : Exception(message);
}

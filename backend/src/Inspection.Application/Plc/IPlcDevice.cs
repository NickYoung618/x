namespace Inspection.Application.Plc;

/// <summary>Observed device facts; each sample carries its source and observation time.</summary>
public sealed record PlcSnapshot(
    string Source, DateTimeOffset ObservedAtUtc,
    bool PlcHeartbeat, bool PlcReady, bool AutoMode, bool Fault,
    bool ManualZoneOccupied, bool PalletLocked, bool ZoneConfigured,
    PlcMotionState XyState, PlcMotionState ZState,
    float ActualX, float ActualY, float ActualZ,
    PlcMotionState FlipState, ushort CurrentFace, PlcSortState SortState,
    ushort AlarmBits, ushort AlarmSeverity, string CoordinateFrame, string Unit)
{
    public bool PcReadyObserved { get; init; }
    public ushort MoveCommandCode { get; init; }
    public ushort FlipTargetFaceCode { get; init; }
    public ushort XyStatusCode { get; init; }
    public ushort ZStatusCode { get; init; }
    public ushort FlipStatusCode { get; init; }
    public ushort SortStatusCode { get; init; }
    public ushort PalletLockStatusCode { get; init; }
    public ushort ZoneConfigAckCode { get; init; }
}

public enum PlcMotionState { Idle, Running, Completed, Failed, Inconsistent }
public enum PlcSortState { Idle, Running, Completed, PickFailed, Full, Inconsistent }
public enum PlcPhase { Ready, Accepted, Completed, Failed, Unknown, RecoveryRequired, Unsupported }
public enum PlcMovePurpose { Load = 1, Inspect = 2, FlipOrScan = 3, Unload = 4, Scan = 5 }
public enum PlcZTarget { Camera, Scan, Grab }

/// <summary>The three heights occupy distinct addresses in the 2026-09-11 protocol.</summary>
public sealed record PlcMoveTarget(float X, float Y, float Z, string CoordinateFrame,
    string Unit, PlcMovePurpose Purpose, PlcZTarget ZTarget);

/// <summary>Sorting needs both physical endpoints even though the published wire map lacks them.</summary>
public sealed record PlcSortRequest(ushort PartSlotIndex, PlcMoveTarget Pickup,
    PlcMoveTarget Drop, string Destination);

public sealed record PlcResult(Guid OperationId, PlcPhase Phase, string Source, string Reason,
    bool ObservedBusy = false);

/// <summary>
/// Application-owned port for the 2026-09-11 contract. Modbus write acknowledgement is not
/// physical completion. Legacy V6 probing remains a separate engineering-only path.
/// </summary>
public interface IPlcDevice : IAsyncDisposable
{
    string Source { get; }
    string Contract { get; }
    Task<PlcSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default);
    Task<PlcResult> SetPcReadyAsync(CancellationToken cancellationToken = default);
    Task<PlcResult> SetPalletLockAsync(bool locked, CancellationToken cancellationToken = default);
    Task<PlcResult> ConfigureZonesAsync(ushort ngCapacity, ushort pendingCapacity,
        CancellationToken cancellationToken = default);
    Task<PlcResult> SubmitMoveAsync(Guid operationId, PlcMoveTarget target,
        CancellationToken cancellationToken = default);
    Task<PlcResult> WaitForMoveAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<PlcResult> SubmitFlipAsync(Guid operationId, ushort targetFace,
        CancellationToken cancellationToken = default);
    Task<PlcResult> WaitForFlipAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<PlcResult> SubmitSortAsync(Guid operationId, PlcSortRequest request,
        CancellationToken cancellationToken = default);
    Task<PlcResult> RequestSoftStopAsync(CancellationToken cancellationToken = default);
    Task<PlcResult> ConfirmManualFlipAsync(CancellationToken cancellationToken = default);
}

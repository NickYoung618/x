namespace VirtualPlc;

public sealed class ModbusOptions
{
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1502;
    public byte UnitId { get; set; } = 1;
}

public sealed class SimulationOptions
{
    public int ScanPeriodMs { get; set; } = 10;
    public int HeartbeatPeriodMs { get; set; } = 1000;
    public int HeartbeatTimeoutMs { get; set; } = 3000;
    public int MotionDurationMs { get; set; } = 350;
    public int FlipDurationMs { get; set; } = 450;
    public int SortingDurationMs { get; set; } = 400;
    public int PalletLockDurationMs { get; set; } = 250;
    public int ZoneConfigDurationMs { get; set; } = 100;
    public Dictionary<string, int> AlgorithmResultWeights { get; set; } = new()
    {
        ["OK"] = 70,
        ["NG"] = 20,
        ["Pending"] = 10
    };
}

public sealed class DashboardOptions
{
    public bool OpenBrowserOnStart { get; set; } = true;
    public string Url { get; set; } = "http://127.0.0.1:5080/";
}

public enum SimulationFault
{
    MoveTimeout,
    FlipFailure,
    FlipAngleMismatch,
    SortingFailure,
    FullPallet,
    PalletLockFailure,
    EmergencyAlarm,
    ManualZoneOccupied,
    PauseHeartbeat
}

public sealed record RegisterValue(
    string Address,
    int PduOffset,
    string Name,
    string Direction,
    ushort RawValue,
    short SignedValue);

public sealed record CoilValue(
    string Address,
    int PduOffset,
    string Name,
    string Direction,
    bool Value);

public sealed record SimulatorSnapshot(
    DateTimeOffset Timestamp,
    bool CommunicationTimedOut,
    string? ActiveAction,
    string[] ActiveFaults,
    IReadOnlyList<CoilValue> Coils,
    IReadOnlyList<RegisterValue> HoldingRegisters);

public enum FlowFailureCategory
{
    DeviceAction,
    DeviceTimeout,
    PlcSafety,
    PlcCommunication,
    Camera,
    Barcode,
    Algorithm,
    Recipe,
    Storage,
    Mes,
    Other
}

public sealed record FlowSimulationDecision(
    bool ContinueFlow,
    bool ShouldReportError,
    string? Decision,
    bool IsFallback,
    string Message,
    DateTimeOffset Timestamp);

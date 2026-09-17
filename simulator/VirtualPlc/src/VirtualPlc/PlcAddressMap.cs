namespace VirtualPlc;

public enum PlcDirection
{
    PcToPlc,
    PlcToPc
}

public sealed record PlcPoint(
    int DocumentNumber,
    string DocumentAddress,
    string Name,
    PlcDirection Direction);

public static class PlcAddressMap
{
    // The specification uses one-based document addresses. Modbus PDU offsets are zero-based.
    public static int ToPduOffset(int documentNumber) => documentNumber - 1;

    public static class Coils
    {
        public const int PlcHeartbeatReq = 1;
        public const int PcHeartbeatResp = 2;
        public const int PcSystemReady = 3;
        public const int PlcSystemFault = 4;
        public const int PlcModeAuto = 5;
        public const int SoftStopCmd = 6;
        public const int ManualZoneOccupied = 10;
        public const int ManualFlipComplete = 11;
    }

    public static class HoldingRegisters
    {
        public const int XyMoveCmd = 1;
        public const int XyPosConfirmed = 2;
        public const int CameraTargetX = 3;
        public const int CameraTargetY = 4;
        public const int CameraTargetZ = 5;
        public const int ZAxisMoveStatus = 6;
        public const int MachineCurrentPosX = 7;
        public const int MachineCurrentPosY = 8;
        public const int FlipTriggerCmd = 10;
        public const int FlipStatus = 11;
        public const int FlipResultAngle = 12;
        public const int SortingPartIndex = 20;
        public const int SortingCmd = 21;
        public const int SortingExecStatus = 22;
        public const int PalletLockCmd = 23;
        public const int PalletLockStatus = 24;
        public const int NgZoneCount = 25;
        public const int PendingZoneCount = 26;
        public const int ZoneConfigReady = 27;
        public const int ZoneConfigAck = 28;
        public const int RetryCmd = 29;
    }

    public static readonly IReadOnlyDictionary<int, PlcPoint> CoilPoints =
        new Dictionary<int, PlcPoint>
        {
            [Coils.PlcHeartbeatReq] = Coil(Coils.PlcHeartbeatReq, "PLC_Heartbeat_Req", PlcDirection.PlcToPc),
            [Coils.PcHeartbeatResp] = Coil(Coils.PcHeartbeatResp, "PC_Heartbeat_Resp", PlcDirection.PcToPlc),
            [Coils.PcSystemReady] = Coil(Coils.PcSystemReady, "PC_System_Ready", PlcDirection.PcToPlc),
            [Coils.PlcSystemFault] = Coil(Coils.PlcSystemFault, "PLC_System_Fault", PlcDirection.PlcToPc),
            [Coils.PlcModeAuto] = Coil(Coils.PlcModeAuto, "PLC_Mode_Auto", PlcDirection.PlcToPc),
            [Coils.SoftStopCmd] = Coil(Coils.SoftStopCmd, "Soft_Stop_Cmd", PlcDirection.PcToPlc),
            [Coils.ManualZoneOccupied] = Coil(Coils.ManualZoneOccupied, "Manual_Zone_Occupied", PlcDirection.PlcToPc),
            [Coils.ManualFlipComplete] = Coil(Coils.ManualFlipComplete, "Manual_Flip_Complete", PlcDirection.PcToPlc)
        };

    public static readonly IReadOnlyDictionary<int, PlcPoint> HoldingRegisterPoints =
        new Dictionary<int, PlcPoint>
        {
            [HoldingRegisters.XyMoveCmd] = Register(HoldingRegisters.XyMoveCmd, "XY_Move_Cmd", PlcDirection.PcToPlc),
            [HoldingRegisters.XyPosConfirmed] = Register(HoldingRegisters.XyPosConfirmed, "XY_Pos_Confirmed", PlcDirection.PlcToPc),
            [HoldingRegisters.CameraTargetX] = Register(HoldingRegisters.CameraTargetX, "Camera_Target_X", PlcDirection.PcToPlc),
            [HoldingRegisters.CameraTargetY] = Register(HoldingRegisters.CameraTargetY, "Camera_Target_Y", PlcDirection.PcToPlc),
            [HoldingRegisters.CameraTargetZ] = Register(HoldingRegisters.CameraTargetZ, "Camera_Target_Z", PlcDirection.PcToPlc),
            [HoldingRegisters.ZAxisMoveStatus] = Register(HoldingRegisters.ZAxisMoveStatus, "Z_Axis_Move_Status", PlcDirection.PlcToPc),
            [HoldingRegisters.MachineCurrentPosX] = Register(HoldingRegisters.MachineCurrentPosX, "Machine_Current_Pos_X", PlcDirection.PlcToPc),
            [HoldingRegisters.MachineCurrentPosY] = Register(HoldingRegisters.MachineCurrentPosY, "Machine_Current_Pos_Y", PlcDirection.PlcToPc),
            [HoldingRegisters.FlipTriggerCmd] = Register(HoldingRegisters.FlipTriggerCmd, "Flip_Trigger_Cmd", PlcDirection.PcToPlc),
            [HoldingRegisters.FlipStatus] = Register(HoldingRegisters.FlipStatus, "Flip_Status", PlcDirection.PlcToPc),
            [HoldingRegisters.FlipResultAngle] = Register(HoldingRegisters.FlipResultAngle, "Flip_Result_Angle", PlcDirection.PlcToPc),
            [HoldingRegisters.SortingPartIndex] = Register(HoldingRegisters.SortingPartIndex, "Sorting_Part_Index", PlcDirection.PcToPlc),
            [HoldingRegisters.SortingCmd] = Register(HoldingRegisters.SortingCmd, "Sorting_Cmd", PlcDirection.PcToPlc),
            [HoldingRegisters.SortingExecStatus] = Register(HoldingRegisters.SortingExecStatus, "Sorting_Exec_Status", PlcDirection.PlcToPc),
            [HoldingRegisters.PalletLockCmd] = Register(HoldingRegisters.PalletLockCmd, "Pallet_Lock_Cmd", PlcDirection.PcToPlc),
            [HoldingRegisters.PalletLockStatus] = Register(HoldingRegisters.PalletLockStatus, "Pallet_Lock_Status", PlcDirection.PlcToPc),
            [HoldingRegisters.NgZoneCount] = Register(HoldingRegisters.NgZoneCount, "NG_Zone_Count", PlcDirection.PcToPlc),
            [HoldingRegisters.PendingZoneCount] = Register(HoldingRegisters.PendingZoneCount, "Pending_Zone_Count", PlcDirection.PcToPlc),
            [HoldingRegisters.ZoneConfigReady] = Register(HoldingRegisters.ZoneConfigReady, "Zone_Config_Ready", PlcDirection.PcToPlc),
            [HoldingRegisters.ZoneConfigAck] = Register(HoldingRegisters.ZoneConfigAck, "Zone_Config_Ack", PlcDirection.PlcToPc),
            [HoldingRegisters.RetryCmd] = Register(HoldingRegisters.RetryCmd, "Retry_Cmd", PlcDirection.PcToPlc)
        };

    private static PlcPoint Coil(int number, string name, PlcDirection direction) =>
        new(number, $"0x{number:0000}", name, direction);

    private static PlcPoint Register(int number, string name, PlcDirection direction) =>
        new(number, $"4x{number:0000}", name, direction);
}

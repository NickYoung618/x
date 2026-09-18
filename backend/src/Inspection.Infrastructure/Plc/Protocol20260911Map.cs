using System.Buffers.Binary;

namespace Inspection.Infrastructure.Plc;

/// <summary>
/// Point labels from the 2026-09-11 PLC/PC protocol. Hex digits are parsed as hexadecimal,
/// then one-based point numbers are converted to zero-based Modbus PDU offsets. This is an
/// explicit engineering profile, not proof of a real PLC vendor's address convention.
/// </summary>
public static class Protocol20260911Map
{
    public const string Contract = "plc-upper-20260911-hex1-f32";
    public const string AddressConvention = "HexOneBased";

    public const ushort HeartbeatReq = 0x0001;
    public const ushort HeartbeatResp = 0x0002;
    public const ushort PcReady = 0x0003;
    public const ushort PlcFault = 0x0004;
    public const ushort AutoMode = 0x0005;
    public const ushort SoftStop = 0x0006;
    public const ushort PlcReady = 0x0008;
    public const ushort ManualZoneOccupied = 0x0010;
    public const ushort ManualFlipComplete = 0x0011;

    public const ushort MoveCommand = 0x0001;
    public const ushort XyPositionConfirmed = 0x0002;
    public const ushort CameraTargetX = 0x0003;
    public const ushort CameraTargetY = 0x0005;
    public const ushort CameraTargetZ = 0x0007;
    public const ushort ScanTargetZ = 0x0009;
    public const ushort GrabTargetZ = 0x000B;
    public const ushort CurrentX = 0x000D;
    public const ushort CurrentY = 0x000F;
    public const ushort CurrentZ = 0x0011;
    public const ushort ZMoveStatus = 0x0013;
    public const ushort FlipTargetFace = 0x0015;
    public const ushort FlipStatus = 0x0016;
    public const ushort FlipCurrentFace = 0x0017;
    public const ushort SortPartIndex = 0x0020;
    public const ushort SortCommand = 0x0021;
    public const ushort SortStatus = 0x0022;
    public const ushort PalletLockCommand = 0x0023;
    public const ushort PalletLockStatus = 0x0024;
    public const ushort NgZoneCount = 0x0025;
    public const ushort PendingZoneCount = 0x0026;
    public const ushort ZoneConfigReady = 0x0027;
    public const ushort ZoneConfigAck = 0x0028;
    public const ushort AlarmBits = 0x0050;
    public const ushort AlarmSeverity = 0x0051;

    public static ushort Offset(ushort point) => point == 0
        ? throw new ArgumentOutOfRangeException(nameof(point)) : (ushort)(point - 1);
}

/// <summary>Byte order of the four Float32 bytes on two Modbus registers.</summary>
public enum Float32ByteOrder { Abcd, Cdab, Badc, Dcba }

public static class Protocol20260911Float32
{
    public static ushort[] Encode(float value, Float32ByteOrder order)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        Span<byte> canonical = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(canonical, value);
        var wire = Reorder(canonical, order);
        return [BinaryPrimitives.ReadUInt16BigEndian(wire.AsSpan(0, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(wire.AsSpan(2, 2))];
    }

    public static float Decode(ushort first, ushort second, Float32ByteOrder order)
    {
        Span<byte> wire = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(wire[..2], first);
        BinaryPrimitives.WriteUInt16BigEndian(wire[2..], second);
        // Every supported permutation is its own inverse.
        var canonical = Reorder(wire, order);
        var value = BinaryPrimitives.ReadSingleBigEndian(canonical);
        if (!float.IsFinite(value)) throw new InvalidDataException("PLC returned a non-finite Float32 coordinate.");
        return value;
    }

    private static byte[] Reorder(ReadOnlySpan<byte> bytes, Float32ByteOrder order) => order switch
    {
        Float32ByteOrder.Abcd => [bytes[0], bytes[1], bytes[2], bytes[3]],
        Float32ByteOrder.Cdab => [bytes[2], bytes[3], bytes[0], bytes[1]],
        Float32ByteOrder.Badc => [bytes[1], bytes[0], bytes[3], bytes[2]],
        Float32ByteOrder.Dcba => [bytes[3], bytes[2], bytes[1], bytes[0]],
        _ => throw new ArgumentOutOfRangeException(nameof(order))
    };
}

using Inspection.Infrastructure.Plc;

namespace VirtualPlc;

/// <summary>
/// Isolated 2026-09-11 synthetic memory. The production address convention is still unsigned;
/// offsets here deliberately match Protocol20260911Map's hex, one-based test profile.
/// </summary>
public sealed record Protocol20260911Trace(DateTimeOffset ObservedAtUtc, string Kind,
    string Detail, string? Request = null, string? Response = null);

public sealed class Protocol20260911Store : IModbusDataStore, IModbusTraceSink
{
    private readonly object gate = new();
    private readonly bool[] coils = new bool[0x40];
    private readonly ushort[] registers = new ushort[0x80];
    private readonly List<Protocol20260911Trace> traces = [];
    private static readonly HashSet<int> PcCoils =
    [
        O(Protocol20260911Map.HeartbeatResp), O(Protocol20260911Map.PcReady),
        O(Protocol20260911Map.SoftStop), O(Protocol20260911Map.ManualFlipComplete)
    ];
    private static readonly HashSet<int> PcScalarRegisters =
    [
        O(Protocol20260911Map.MoveCommand), O(Protocol20260911Map.FlipTargetFace),
        O(Protocol20260911Map.SortPartIndex), O(Protocol20260911Map.SortCommand),
        O(Protocol20260911Map.PalletLockCommand), O(Protocol20260911Map.NgZoneCount),
        O(Protocol20260911Map.PendingZoneCount), O(Protocol20260911Map.ZoneConfigReady)
    ];
    private static readonly HashSet<int> PcFloatStarts =
    [
        O(Protocol20260911Map.CameraTargetX), O(Protocol20260911Map.CameraTargetY),
        O(Protocol20260911Map.CameraTargetZ), O(Protocol20260911Map.ScanTargetZ),
        O(Protocol20260911Map.GrabTargetZ)
    ];

    public event Action<int, bool>? CoilWritten;
    public event Action<int, ushort>? RegisterWritten;

    public IReadOnlyList<Protocol20260911Trace> Traces
    {
        get { lock (gate) return traces.ToArray(); }
    }

    public Protocol20260911Store() => coils[O(Protocol20260911Map.AutoMode)] = true;

    public bool[] ReadCoils(int pduOffset, int count)
    {
        ValidateRange(pduOffset, count, coils.Length);
        lock (gate) return coils.AsSpan(pduOffset, count).ToArray();
    }

    public ushort[] ReadHoldingRegisters(int pduOffset, int count)
    {
        ValidateRange(pduOffset, count, registers.Length);
        lock (gate) return registers.AsSpan(pduOffset, count).ToArray();
    }

    public bool TryWriteCoilsFromPc(int pduOffset, IReadOnlyList<bool> values)
    {
        if (!ValidRange(pduOffset, values.Count, coils.Length) ||
            Enumerable.Range(pduOffset, values.Count).Any(index => !PcCoils.Contains(index))) return false;
        lock (gate)
        {
            for (var i = 0; i < values.Count; i++) coils[pduOffset + i] = values[i];
        }
        for (var i = 0; i < values.Count; i++) CoilWritten?.Invoke(pduOffset + i, values[i]);
        return true;
    }

    public bool TryWriteHoldingRegistersFromPc(int pduOffset, IReadOnlyList<ushort> values)
    {
        if (!ValidRange(pduOffset, values.Count, registers.Length) || !ValidRegisterWrite(pduOffset, values))
            return false;
        lock (gate)
        {
            for (var i = 0; i < values.Count; i++) registers[pduOffset + i] = values[i];
        }
        for (var i = 0; i < values.Count; i++) RegisterWritten?.Invoke(pduOffset + i, values[i]);
        return true;
    }

    public void SetCoilFromPlc(ushort point, bool value)
    {
        lock (gate) coils[O(point)] = value;
    }

    public void SetRegisterFromPlc(ushort point, ushort value)
    {
        lock (gate) registers[O(point)] = value;
    }

    public void SetFloatFromPlc(ushort point, float value, Float32ByteOrder order)
    {
        var words = Protocol20260911Float32.Encode(value, order);
        lock (gate)
        {
            registers[O(point)] = words[0];
            registers[O(point) + 1] = words[1];
        }
    }

    public void RecordExchange(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response) =>
        Record("Modbus", "Request and response on independent simulator process.",
            Convert.ToHexString(request), Convert.ToHexString(response));

    public void Record(string kind, string detail, string? request = null, string? response = null)
    {
        lock (gate)
        {
            if (traces.Count == 10000) traces.RemoveAt(0);
            traces.Add(new(DateTimeOffset.UtcNow, kind, detail, request, response));
        }
    }

    private static bool ValidRegisterWrite(int start, IReadOnlyList<ushort> values)
    {
        var count = values.Count;
        // The published protocol has no S01 3D/F command identity. Reject raw motion
        // commands until a separately versioned synthetic extension is selected.
        if (count == 1 && values[0] != 0 &&
            (start == O(Protocol20260911Map.MoveCommand) ||
             start == O(Protocol20260911Map.FlipTargetFace) ||
             start == O(Protocol20260911Map.SortCommand))) return false;
        if (count == 1 && start == O(Protocol20260911Map.PalletLockCommand) && values[0] > 1)
            return false;
        if (count == 1 && start == O(Protocol20260911Map.ZoneConfigReady) && values[0] > 1)
            return false;
        if (count == 1) return PcScalarRegisters.Contains(start);
        if (count is not (2 or 4)) return false;
        for (var at = start; at < start + count; at += 2)
            if (!PcFloatStarts.Contains(at)) return count == 2 &&
                PcScalarRegisters.Contains(start) && PcScalarRegisters.Contains(start + 1);
        return true;
    }

    private static int O(ushort point) => Protocol20260911Map.Offset(point);
    private static bool ValidRange(int start, int count, int length) =>
        start >= 0 && count > 0 && (long)start + count <= length;
    private static void ValidateRange(int start, int count, int length)
    {
        if (!ValidRange(start, count, length)) throw new ArgumentOutOfRangeException(nameof(start));
    }
}

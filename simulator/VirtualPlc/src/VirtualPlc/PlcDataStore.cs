namespace VirtualPlc;

public enum PlcArea
{
    Coil,
    HoldingRegister
}

public sealed record PcWriteEvent(PlcArea Area, int DocumentNumber, ushort Value);

public sealed class PlcDataStore
{
    private readonly object _gate = new();
    private readonly bool[] _coils = CreateStorage<bool>(PlcAddressMap.CoilPoints);
    private readonly ushort[] _holdingRegisters = CreateStorage<ushort>(PlcAddressMap.HoldingRegisterPoints);

    public event Action<PcWriteEvent>? PcValueWritten;

    public PlcDataStore()
    {
        SetCoilFromPlc(PlcAddressMap.Coils.PlcModeAuto, true);
        SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.XyPosConfirmed, 0);
        SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZAxisMoveStatus, 0);
        SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.FlipStatus, 0);
        SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.SortingExecStatus, 0);
        SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.PalletLockStatus, 0);
        SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZoneConfigAck, 0);
    }

    public bool[] ReadCoils(int pduOffset, int count)
    {
        ValidateRange(pduOffset, count, _coils.Length);
        lock (_gate)
        {
            var result = new bool[count];
            Array.Copy(_coils, pduOffset, result, 0, count);
            return result;
        }
    }

    public ushort[] ReadHoldingRegisters(int pduOffset, int count)
    {
        ValidateRange(pduOffset, count, _holdingRegisters.Length);
        lock (_gate)
        {
            var result = new ushort[count];
            Array.Copy(_holdingRegisters, pduOffset, result, 0, count);
            return result;
        }
    }

    public bool ReadCoilByDocumentNumber(int documentNumber)
    {
        lock (_gate)
        {
            return _coils[PlcAddressMap.ToPduOffset(documentNumber)];
        }
    }

    public ushort ReadHoldingRegisterByDocumentNumber(int documentNumber)
    {
        lock (_gate)
        {
            return _holdingRegisters[PlcAddressMap.ToPduOffset(documentNumber)];
        }
    }

    public bool TryWriteCoilsFromPc(int pduOffset, IReadOnlyList<bool> values)
    {
        if (!IsValidRange(pduOffset, values.Count, _coils.Length))
        {
            return false;
        }

        var points = new PlcPoint[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var documentNumber = pduOffset + i + 1;
            if (!PlcAddressMap.CoilPoints.TryGetValue(documentNumber, out var point) ||
                point.Direction != PlcDirection.PcToPlc)
            {
                return false;
            }

            points[i] = point;
        }

        lock (_gate)
        {
            for (var i = 0; i < values.Count; i++)
            {
                _coils[pduOffset + i] = values[i];
            }
        }

        for (var i = 0; i < values.Count; i++)
        {
            PcValueWritten?.Invoke(new PcWriteEvent(
                PlcArea.Coil, points[i].DocumentNumber, values[i] ? (ushort)1 : (ushort)0));
        }

        return true;
    }

    public bool TryWriteHoldingRegistersFromPc(int pduOffset, IReadOnlyList<ushort> values)
    {
        if (!IsValidRange(pduOffset, values.Count, _holdingRegisters.Length))
        {
            return false;
        }

        var points = new PlcPoint[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var documentNumber = pduOffset + i + 1;
            if (!PlcAddressMap.HoldingRegisterPoints.TryGetValue(documentNumber, out var point) ||
                point.Direction != PlcDirection.PcToPlc)
            {
                return false;
            }

            points[i] = point;
        }

        lock (_gate)
        {
            for (var i = 0; i < values.Count; i++)
            {
                _holdingRegisters[pduOffset + i] = values[i];
            }
        }

        for (var i = 0; i < values.Count; i++)
        {
            PcValueWritten?.Invoke(new PcWriteEvent(
                PlcArea.HoldingRegister, points[i].DocumentNumber, values[i]));
        }

        return true;
    }

    public void SetCoilFromPlc(int documentNumber, bool value)
    {
        EnsurePlcOwned(PlcAddressMap.CoilPoints, documentNumber);
        lock (_gate)
        {
            _coils[PlcAddressMap.ToPduOffset(documentNumber)] = value;
        }
    }

    public void SetHoldingRegisterFromPlc(int documentNumber, ushort value)
    {
        EnsurePlcOwned(PlcAddressMap.HoldingRegisterPoints, documentNumber);
        lock (_gate)
        {
            _holdingRegisters[PlcAddressMap.ToPduOffset(documentNumber)] = value;
        }
    }

    public void ResetPcWritableValues()
    {
        lock (_gate)
        {
            foreach (var point in PlcAddressMap.CoilPoints.Values.Where(x => x.Direction == PlcDirection.PcToPlc))
            {
                _coils[PlcAddressMap.ToPduOffset(point.DocumentNumber)] = false;
            }

            foreach (var point in PlcAddressMap.HoldingRegisterPoints.Values.Where(x => x.Direction == PlcDirection.PcToPlc))
            {
                _holdingRegisters[PlcAddressMap.ToPduOffset(point.DocumentNumber)] = 0;
            }
        }
    }

    public SimulatorSnapshot CreateSnapshot(
        bool communicationTimedOut,
        string? activeAction,
        IEnumerable<SimulationFault> activeFaults)
    {
        lock (_gate)
        {
            var coils = PlcAddressMap.CoilPoints.Values
                .OrderBy(x => x.DocumentNumber)
                .Select(x => new CoilValue(
                    x.DocumentAddress,
                    PlcAddressMap.ToPduOffset(x.DocumentNumber),
                    x.Name,
                    x.Direction.ToString(),
                    _coils[PlcAddressMap.ToPduOffset(x.DocumentNumber)]))
                .ToArray();

            var registers = PlcAddressMap.HoldingRegisterPoints.Values
                .OrderBy(x => x.DocumentNumber)
                .Select(x =>
                {
                    var raw = _holdingRegisters[PlcAddressMap.ToPduOffset(x.DocumentNumber)];
                    return new RegisterValue(
                        x.DocumentAddress,
                        PlcAddressMap.ToPduOffset(x.DocumentNumber),
                        x.Name,
                        x.Direction.ToString(),
                        raw,
                        unchecked((short)raw));
                })
                .ToArray();

            return new SimulatorSnapshot(
                DateTimeOffset.UtcNow,
                communicationTimedOut,
                activeAction,
                activeFaults.Select(x => x.ToString()).OrderBy(x => x).ToArray(),
                coils,
                registers);
        }
    }

    private static T[] CreateStorage<T>(IReadOnlyDictionary<int, PlcPoint> points) =>
        new T[Math.Max(128, points.Keys.DefaultIfEmpty(1).Max())];

    private static bool IsValidRange(int pduOffset, int count, int capacity) =>
        pduOffset >= 0 && count > 0 && pduOffset + count <= capacity;

    private static void ValidateRange(int pduOffset, int count, int capacity)
    {
        if (!IsValidRange(pduOffset, count, capacity))
        {
            throw new ArgumentOutOfRangeException(nameof(pduOffset));
        }
    }

    private static void EnsurePlcOwned(
        IReadOnlyDictionary<int, PlcPoint> points,
        int documentNumber)
    {
        if (!points.TryGetValue(documentNumber, out var point) ||
            point.Direction != PlcDirection.PlcToPc)
        {
            throw new InvalidOperationException($"Point {documentNumber} is not PLC-owned.");
        }
    }
}

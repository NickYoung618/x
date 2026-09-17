using Microsoft.Extensions.Options;

namespace VirtualPlc;

public sealed class VirtualPlcEngine : BackgroundService
{
    private enum ActionKind
    {
        Move,
        Flip,
        Sort,
        PalletLock,
        ZoneConfig
    }

    private sealed record PendingAction(
        ActionKind Kind,
        long DueAtTick,
        ushort Command,
        ushort TargetX = 0,
        ushort TargetY = 0,
        ushort TargetZ = 0);

    private readonly object _gate = new();
    private readonly PlcDataStore _store;
    private readonly SimulationOptions _options;
    private readonly ILogger<VirtualPlcEngine> _logger;
    private readonly HashSet<SimulationFault> _faults = new();

    private PendingAction? _activeAction;
    private long _nextHeartbeatTick;
    private long _lastValidHeartbeatResponseTick;
    private bool _communicationTimedOut;
    private bool _xyCommandArmed = true;
    private bool _flipCommandArmed = true;
    private bool _sortCommandArmed = true;
    private bool _zoneConfigArmed = true;
    private bool _retryCommandArmed = true;
    private bool _manualCompleteArmed = true;
    private ushort _lastPalletLockCommand;

    public VirtualPlcEngine(
        PlcDataStore store,
        IOptions<SimulationOptions> options,
        ILogger<VirtualPlcEngine> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
        _store.PcValueWritten += OnPcValueWritten;
    }

    public SimulatorSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _store.CreateSnapshot(
                _communicationTimedOut,
                _activeAction?.Kind.ToString(),
                _faults);
        }
    }

    public (bool Accepted, string Message) InjectFault(SimulationFault fault)
    {
        lock (_gate)
        {
            _faults.Add(fault);

            switch (fault)
            {
                case SimulationFault.EmergencyAlarm:
                    _store.SetCoilFromPlc(PlcAddressMap.Coils.PlcSystemFault, true);
                    FailActiveAction();
                    break;
                case SimulationFault.ManualZoneOccupied:
                    _store.SetCoilFromPlc(PlcAddressMap.Coils.ManualZoneOccupied, true);
                    FailActiveAction();
                    break;
            }

            _logger.LogWarning("Simulation fault injected: {Fault}", fault);
            return (true, $"Fault {fault} injected.");
        }
    }

    public void ResetSimulation()
    {
        lock (_gate)
        {
            _faults.Clear();
            _communicationTimedOut = false;
            _activeAction = null;
            _store.ResetPcWritableValues();
            _store.SetCoilFromPlc(PlcAddressMap.Coils.PlcSystemFault, false);
            _store.SetCoilFromPlc(PlcAddressMap.Coils.ManualZoneOccupied, false);
            _store.SetCoilFromPlc(PlcAddressMap.Coils.PlcModeAuto, true);
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.XyPosConfirmed, 0);
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZAxisMoveStatus, 0);
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.FlipStatus, 0);
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.FlipResultAngle, 0);
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.SortingExecStatus, 0);
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.PalletLockStatus, 0);
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZoneConfigAck, 0);

            _xyCommandArmed = true;
            _flipCommandArmed = true;
            _sortCommandArmed = true;
            _zoneConfigArmed = true;
            _retryCommandArmed = true;
            _manualCompleteArmed = true;
            _lastPalletLockCommand = 0;
            _lastValidHeartbeatResponseTick = Environment.TickCount64;
            _nextHeartbeatTick = Environment.TickCount64 + _options.HeartbeatPeriodMs;
        }

        _logger.LogInformation("Virtual PLC reset completed.");
    }

    public FlowSimulationDecision ResolveFlowDecision(
        FlowFailureCategory category,
        bool stepSucceeded)
    {
        if (stepSucceeded)
        {
            return new FlowSimulationDecision(
                true,
                false,
                null,
                false,
                "步骤成功，继续流程",
                DateTimeOffset.UtcNow);
        }

        if (category == FlowFailureCategory.DeviceTimeout)
        {
            return new FlowSimulationDecision(
                true,
                true,
                null,
                true,
                "XY/Z 或翻面动作等待超时，记录错误后继续模拟流程",
                DateTimeOffset.UtcNow);
        }

        if (category is FlowFailureCategory.DeviceAction or
            FlowFailureCategory.PlcSafety or
            FlowFailureCategory.PlcCommunication)
        {
            return new FlowSimulationDecision(
                false,
                true,
                null,
                false,
                "PLC/下位机动作或安全故障，停止流程并上报错误",
                DateTimeOffset.UtcNow);
        }

        return new FlowSimulationDecision(
            true,
            false,
            NextRandomDecision(),
            true,
            "非 PLC/下位机故障，模拟模式使用兜底结果继续流程",
            DateTimeOffset.UtcNow);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ResetSimulation();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.ScanPeriodMs));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Tick();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void Tick()
    {
        lock (_gate)
        {
            var now = Environment.TickCount64;
            ProcessHeartbeat(now);
            ProcessManualConfirmation();
            ProcessRetryCommand();
            ProcessActionCompletion(now);
            ProcessZoneConfiguration(now);
            ProcessPalletLock(now);
            ProcessMoveCommand(now);
            ProcessFlipCommand(now);
            ProcessSortCommand(now);
        }
    }

    private void ProcessHeartbeat(long now)
    {
        if (!_faults.Contains(SimulationFault.PauseHeartbeat) && now >= _nextHeartbeatTick)
        {
            var current = _store.ReadCoilByDocumentNumber(PlcAddressMap.Coils.PlcHeartbeatReq);
            _store.SetCoilFromPlc(PlcAddressMap.Coils.PlcHeartbeatReq, !current);
            _nextHeartbeatTick = now + _options.HeartbeatPeriodMs;
        }

        var pcReady = _store.ReadCoilByDocumentNumber(PlcAddressMap.Coils.PcSystemReady);
        if (pcReady && now - _lastValidHeartbeatResponseTick > _options.HeartbeatTimeoutMs)
        {
            if (!_communicationTimedOut)
            {
                _communicationTimedOut = true;
                _store.SetCoilFromPlc(PlcAddressMap.Coils.PlcSystemFault, true);
                FailActiveAction();
                _logger.LogWarning("PC heartbeat timed out; virtual PLC entered soft-stop fault state.");
            }
        }
    }

    private void ProcessManualConfirmation()
    {
        var value = _store.ReadCoilByDocumentNumber(PlcAddressMap.Coils.ManualFlipComplete);
        if (!value)
        {
            _manualCompleteArmed = true;
            return;
        }

        if (!_manualCompleteArmed)
        {
            return;
        }

        _manualCompleteArmed = false;
        if (_store.ReadCoilByDocumentNumber(PlcAddressMap.Coils.ManualZoneOccupied))
        {
            _faults.Remove(SimulationFault.ManualZoneOccupied);
            _store.SetCoilFromPlc(PlcAddressMap.Coils.ManualZoneOccupied, false);
            if (!_communicationTimedOut && !_faults.Contains(SimulationFault.EmergencyAlarm))
            {
                _store.SetCoilFromPlc(PlcAddressMap.Coils.PlcSystemFault, false);
            }
        }
    }

    private void ProcessRetryCommand()
    {
        var value = _store.ReadHoldingRegisterByDocumentNumber(PlcAddressMap.HoldingRegisters.RetryCmd);
        if (value == 0)
        {
            _retryCommandArmed = true;
            return;
        }

        if (!_retryCommandArmed)
        {
            return;
        }

        _retryCommandArmed = false;
        if (value is 1 or 2)
        {
            ResetActionStatuses();
        }
        else if (value == 3)
        {
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.SortingExecStatus, 0);
        }
    }

    private void ProcessActionCompletion(long now)
    {
        if (_activeAction is null || now < _activeAction.DueAtTick)
        {
            return;
        }

        var action = _activeAction;
        _activeAction = null;

        switch (action.Kind)
        {
            case ActionKind.Move:
                if (ConsumeFault(SimulationFault.MoveTimeout))
                {
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.XyPosConfirmed, 2);
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZAxisMoveStatus, 3);
                }
                else
                {
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.MachineCurrentPosX, action.TargetX);
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.MachineCurrentPosY, action.TargetY);
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.XyPosConfirmed, 1);
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZAxisMoveStatus, 2);
                }

                break;

            case ActionKind.Flip:
                if (ConsumeFault(SimulationFault.FlipFailure))
                {
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.FlipStatus, 3);
                }
                else
                {
                    var requestedAngle = action.Command == 1 ? (ushort)90 : (ushort)180;
                    if (ConsumeFault(SimulationFault.FlipAngleMismatch))
                    {
                        requestedAngle = requestedAngle == 90 ? (ushort)180 : (ushort)90;
                    }

                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.FlipResultAngle, requestedAngle);
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.FlipStatus, 2);
                }

                break;

            case ActionKind.Sort:
                if (ConsumeFault(SimulationFault.FullPallet) || action.Command == 2)
                {
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.SortingExecStatus, 4);
                }
                else if (ConsumeFault(SimulationFault.SortingFailure))
                {
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.SortingExecStatus, 3);
                }
                else
                {
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.SortingExecStatus, 2);
                }

                break;

            case ActionKind.PalletLock:
                if (ConsumeFault(SimulationFault.PalletLockFailure))
                {
                    _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.PalletLockStatus, 2);
                }
                else
                {
                    _store.SetHoldingRegisterFromPlc(
                        PlcAddressMap.HoldingRegisters.PalletLockStatus,
                        action.Command == 1 ? (ushort)1 : (ushort)0);
                }

                break;

            case ActionKind.ZoneConfig:
                _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZoneConfigAck, 1);
                break;
        }
    }

    private void ProcessZoneConfiguration(long now)
    {
        var value = _store.ReadHoldingRegisterByDocumentNumber(PlcAddressMap.HoldingRegisters.ZoneConfigReady);
        if (value == 0)
        {
            _zoneConfigArmed = true;
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZoneConfigAck, 0);
            return;
        }

        if (_zoneConfigArmed && value == 1)
        {
            _zoneConfigArmed = false;
            TryStartAction(new PendingAction(
                ActionKind.ZoneConfig,
                now + _options.ZoneConfigDurationMs,
                value));
        }
    }

    private void ProcessPalletLock(long now)
    {
        var value = _store.ReadHoldingRegisterByDocumentNumber(PlcAddressMap.HoldingRegisters.PalletLockCmd);
        if (value == _lastPalletLockCommand)
        {
            return;
        }

        _lastPalletLockCommand = value;
        if (value is 0 or 1)
        {
            TryStartAction(new PendingAction(
                ActionKind.PalletLock,
                now + _options.PalletLockDurationMs,
                value));
        }
    }

    private void ProcessMoveCommand(long now)
    {
        var value = _store.ReadHoldingRegisterByDocumentNumber(PlcAddressMap.HoldingRegisters.XyMoveCmd);
        if (value == 0)
        {
            _xyCommandArmed = true;
            return;
        }

        if (!_xyCommandArmed)
        {
            return;
        }

        _xyCommandArmed = false;
        if (value is < 1 or > 5)
        {
            SetActionFailed(ActionKind.Move);
            return;
        }

        var action = new PendingAction(
            ActionKind.Move,
            now + _options.MotionDurationMs,
            value,
            _store.ReadHoldingRegisterByDocumentNumber(PlcAddressMap.HoldingRegisters.CameraTargetX),
            _store.ReadHoldingRegisterByDocumentNumber(PlcAddressMap.HoldingRegisters.CameraTargetY),
            _store.ReadHoldingRegisterByDocumentNumber(PlcAddressMap.HoldingRegisters.CameraTargetZ));

        if (TryStartAction(action))
        {
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.XyPosConfirmed, 0);
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZAxisMoveStatus, 1);
        }
    }

    private void ProcessFlipCommand(long now)
    {
        var value = _store.ReadHoldingRegisterByDocumentNumber(PlcAddressMap.HoldingRegisters.FlipTriggerCmd);
        if (value == 0)
        {
            _flipCommandArmed = true;
            return;
        }

        if (!_flipCommandArmed)
        {
            return;
        }

        _flipCommandArmed = false;
        if (value is not (1 or 2))
        {
            SetActionFailed(ActionKind.Flip);
            return;
        }

        if (TryStartAction(new PendingAction(
                ActionKind.Flip,
                now + _options.FlipDurationMs,
                value)))
        {
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.FlipStatus, 1);
        }
    }

    private void ProcessSortCommand(long now)
    {
        var value = _store.ReadHoldingRegisterByDocumentNumber(PlcAddressMap.HoldingRegisters.SortingCmd);
        if (value == 0)
        {
            _sortCommandArmed = true;
            return;
        }

        if (!_sortCommandArmed)
        {
            return;
        }

        _sortCommandArmed = false;
        if (value is not (1 or 2))
        {
            SetActionFailed(ActionKind.Sort);
            return;
        }

        if (value == 1 &&
            _store.ReadHoldingRegisterByDocumentNumber(PlcAddressMap.HoldingRegisters.SortingPartIndex) == 0)
        {
            SetActionFailed(ActionKind.Sort);
            _logger.LogWarning("Sorting command rejected because Sorting_Part_Index is 0.");
            return;
        }

        if (TryStartAction(new PendingAction(
                ActionKind.Sort,
                now + _options.SortingDurationMs,
                value)))
        {
            _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.SortingExecStatus, 1);
        }
    }

    private bool TryStartAction(PendingAction action)
    {
        if (!CanExecuteAction(action.Kind) || _activeAction is not null)
        {
            SetActionFailed(action.Kind);
            return false;
        }

        _activeAction = action;
        _logger.LogInformation("Virtual PLC action started: {Action}", action.Kind);
        return true;
    }

    private bool CanExecuteAction(ActionKind action)
    {
        var baseInterlocksSatisfied =
            _store.ReadCoilByDocumentNumber(PlcAddressMap.Coils.PcSystemReady) &&
            _store.ReadCoilByDocumentNumber(PlcAddressMap.Coils.PlcModeAuto) &&
            !_store.ReadCoilByDocumentNumber(PlcAddressMap.Coils.PlcSystemFault) &&
            !_store.ReadCoilByDocumentNumber(PlcAddressMap.Coils.SoftStopCmd) &&
            !_store.ReadCoilByDocumentNumber(PlcAddressMap.Coils.ManualZoneOccupied) &&
            !_communicationTimedOut;

        if (!baseInterlocksSatisfied)
        {
            return false;
        }

        return action is ActionKind.PalletLock or ActionKind.ZoneConfig ||
            _store.ReadHoldingRegisterByDocumentNumber(PlcAddressMap.HoldingRegisters.ZoneConfigAck) == 1;
    }

    private void OnPcValueWritten(PcWriteEvent write)
    {
        if (write.Area != PlcArea.Coil)
        {
            return;
        }

        lock (_gate)
        {
            if (write.DocumentNumber == PlcAddressMap.Coils.PcHeartbeatResp)
            {
                var expected = _store.ReadCoilByDocumentNumber(PlcAddressMap.Coils.PlcHeartbeatReq);
                if ((write.Value != 0) == expected)
                {
                    _lastValidHeartbeatResponseTick = Environment.TickCount64;
                }
            }
            else if (write.DocumentNumber == PlcAddressMap.Coils.SoftStopCmd && write.Value != 0)
            {
                FailActiveAction();
                _logger.LogWarning("Soft_Stop_Cmd received; active virtual PLC action stopped.");
            }
        }
    }

    private void FailActiveAction()
    {
        if (_activeAction is null)
        {
            return;
        }

        SetActionFailed(_activeAction.Kind);
        _activeAction = null;
    }

    private void SetActionFailed(ActionKind action)
    {
        switch (action)
        {
            case ActionKind.Move:
                _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.XyPosConfirmed, 2);
                _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZAxisMoveStatus, 3);
                break;
            case ActionKind.Flip:
                _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.FlipStatus, 3);
                break;
            case ActionKind.Sort:
                _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.SortingExecStatus, 3);
                break;
            case ActionKind.PalletLock:
                _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.PalletLockStatus, 2);
                break;
            case ActionKind.ZoneConfig:
                _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZoneConfigAck, 0);
                break;
        }
    }

    private void ResetActionStatuses()
    {
        _activeAction = null;
        _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.XyPosConfirmed, 0);
        _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.ZAxisMoveStatus, 0);
        _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.FlipStatus, 0);
        _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.SortingExecStatus, 0);
        _store.SetHoldingRegisterFromPlc(PlcAddressMap.HoldingRegisters.PalletLockStatus, 0);
    }

    private bool ConsumeFault(SimulationFault fault)
    {
        if (!_faults.Contains(fault))
        {
            return false;
        }

        _faults.Remove(fault);
        return true;
    }

    private string NextRandomDecision()
    {
        var weights = _options.AlgorithmResultWeights
            .Where(x => x.Value > 0)
            .ToArray();
        if (weights.Length == 0)
        {
            return "Pending";
        }

        var total = weights.Sum(x => x.Value);
        var value = Random.Shared.Next(total);
        foreach (var item in weights)
        {
            if (value < item.Value)
            {
                return item.Key;
            }

            value -= item.Value;
        }

        return weights[^1].Key;
    }
}

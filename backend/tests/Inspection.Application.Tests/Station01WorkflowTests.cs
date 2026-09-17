using Inspection.Application.Workflow;

namespace Inspection.Application.Tests;

public class Station01WorkflowTests
{
    private static DeviceReady Ready(string runId = "tray-1", bool ready = true, bool clamped = true,
        bool interlockClear = true, string source = "synthetic", DateTimeOffset? observedAt = null) =>
        new(runId, ready, clamped, interlockClear, observedAt ?? DateTimeOffset.UtcNow, source);

    private static ScanCompleted Scan(RequestScan request, IReadOnlyList<LocatedSlot>? slots = null,
        string? runId = null, string? source = null, string frame = "synthetic", string unit = "opaque",
        string? calibration = null, int? epoch = null) =>
        new(request.RequestId, new PlacementScan(request.RequestId, new PlacementMetadata(
            runId ?? request.TrayRunId, epoch ?? request.ProposedEpoch,
            source ?? request.ExpectedSource, frame, unit, calibration, DateTimeOffset.UtcNow),
            slots ?? [new LocatedSlot("s1", "position-s1"), new LocatedSlot("s2", "position-s2")]));

    private static (TrayWorkflow Flow, RequestMotion Move) StartMove()
    {
        var flow = new TrayWorkflow("tray-1", "scene");
        var move = Assert.IsType<RequestMotion>(Assert.Single(flow.Handle(Ready())));
        Assert.Equal(MotionKind.MoveTo3D, move.Kind);
        Assert.Equal("S01_COMMON_3D", move.PositionRef);
        Assert.Equal("tray-1", move.TrayRunId);
        Assert.Equal(TrayStage.MovingTo3D, flow.Snapshot().Stage);
        return (flow, move);
    }

    private static (TrayWorkflow Flow, RequestScan Scan) StartScan()
    {
        var (flow, move) = StartMove();
        Assert.Empty(flow.Handle(new MotionAccepted(move.OperationId)));
        var scan = Assert.IsType<RequestScan>(Assert.Single(flow.Handle(
            new MotionFinished(move.OperationId, MotionOutcome.Completed))));
        Assert.Equal(TrayStage.Scanning3D, flow.Snapshot().Stage);
        return (flow, scan);
    }

    [Fact]
    public void N01_reaches_waiting_F_only_after_matching_move_and_scan()
    {
        var (flow, move) = StartMove();
        Assert.Empty(flow.Handle(new MotionAccepted(move.OperationId)));
        Assert.Equal(TrayStage.MovingTo3D, flow.Snapshot().Stage);
        var scan = Assert.IsType<RequestScan>(Assert.Single(flow.Handle(
            new MotionFinished(move.OperationId, MotionOutcome.Completed))));
        var code = Assert.IsType<RequestTrayCode>(Assert.Single(flow.Handle(Scan(scan))));
        Assert.NotEqual(Guid.Empty, code.RequestId);
        Assert.Equal(TrayStage.WaitingTrayCode, flow.Snapshot().Stage);
        Assert.Equal(1, flow.Snapshot().CoordinateEpoch);
        Assert.Equal("synthetic", flow.Snapshot().Source);
        Assert.Equal(["tray-1:s1", "tray-1:s2"], flow.Snapshot().Parts.Select(p => p.PartId));
    }

    [Theory]
    [InlineData(false, true, true, "ST01_NOT_READY")]
    [InlineData(true, false, true, "ST01_NOT_CLAMPED")]
    [InlineData(true, true, false, "ST01_INTERLOCK")]
    public void P01_P02_unsafe_ready_prevents_any_motion(bool ready, bool clamped,
        bool interlockClear, string code)
    {
        var flow = new TrayWorkflow("tray-1", "scene");
        Assert.Equal(TrayStage.WaitingReady, flow.Snapshot().Stage);
        Assert.Empty(flow.Handle(Ready(ready: ready, clamped: clamped, interlockClear: interlockClear)));
        Assert.Equal(TrayStage.Failed, flow.Snapshot().Stage);
        Assert.Equal(code, flow.Snapshot().FailureCode);
    }

    [Fact]
    public void P02_stale_or_wrong_session_ready_prevents_motion()
    {
        var stale = new TrayWorkflow("tray-1", "scene");
        Assert.Empty(stale.Handle(Ready(observedAt: DateTimeOffset.UtcNow.AddSeconds(-20))));
        Assert.Equal("ST01_READY_STALE", stale.Snapshot().FailureCode);
        var wrong = new TrayWorkflow("tray-1", "scene");
        Assert.Empty(wrong.Handle(Ready(runId: "another-tray")));
        Assert.Equal("ST01_READY_SESSION", wrong.Snapshot().FailureCode);
    }

    [Fact]
    public void M01_M02_rejection_and_unknown_are_distinct_and_never_scan()
    {
        var (rejected, rejectedMove) = StartMove();
        Assert.Empty(rejected.Handle(new MotionFinished(rejectedMove.OperationId, MotionOutcome.Failed)));
        Assert.Equal(TrayStage.Failed, rejected.Snapshot().Stage);
        Assert.Equal("ST01_MOVE_REJECTED", rejected.Snapshot().FailureCode);

        var (unknown, unknownMove) = StartMove();
        unknown.Handle(new MotionAccepted(unknownMove.OperationId));
        Assert.Empty(unknown.Handle(new RequestTimedOut(unknownMove.OperationId)));
        Assert.Equal(TrayStage.RecoveryRequired, unknown.Snapshot().Stage);
        Assert.Equal("ST01_MOVE_UNKNOWN", unknown.Snapshot().FailureCode);
        Assert.Empty(unknown.Handle(new MotionFinished(unknownMove.OperationId, MotionOutcome.Completed)));
    }

    [Fact]
    public void M03_M04_wrong_early_and_duplicate_completion_never_trigger_extra_scan()
    {
        var (flow, move) = StartMove();
        Assert.Empty(flow.Handle(new MotionFinished(Guid.NewGuid(), MotionOutcome.Completed)));
        Assert.Empty(flow.Handle(new MotionFinished(move.OperationId, MotionOutcome.Completed)));
        Assert.Equal(TrayStage.MovingTo3D, flow.Snapshot().Stage);
        flow.Handle(new MotionAccepted(move.OperationId));
        var scan = Assert.IsType<RequestScan>(Assert.Single(flow.Handle(
            new MotionFinished(move.OperationId, MotionOutcome.Completed))));
        Assert.Empty(flow.Handle(new MotionFinished(move.OperationId, MotionOutcome.Completed)));
        Assert.Equal(scan.RequestId, flow.Snapshot().PendingRequestId);
        Assert.Equal(TrayStage.Scanning3D, flow.Snapshot().Stage);
    }

    [Fact]
    public void M03a_disconnection_after_acceptance_is_unknown_and_never_scans()
    {
        var (flow, move) = StartMove();
        flow.Handle(new MotionAccepted(move.OperationId));
        Assert.Empty(flow.Handle(new MotionFinished(move.OperationId, MotionOutcome.Unknown, "ConnectionLost")));
        Assert.Equal(TrayStage.RecoveryRequired, flow.Snapshot().Stage);
        Assert.Equal("ST01_MOVE_UNKNOWN", flow.Snapshot().FailureCode);
        Assert.Equal(0, flow.Snapshot().CoordinateEpoch);
    }

    [Fact]
    public void M03b_wrong_completion_id_stays_moving_until_its_deadline()
    {
        var (flow, move) = StartMove();
        flow.Handle(new MotionAccepted(move.OperationId));
        Assert.Empty(flow.Handle(new MotionFinished(Guid.NewGuid(), MotionOutcome.Completed)));
        Assert.Equal(TrayStage.MovingTo3D, flow.Snapshot().Stage);
        Assert.Empty(flow.Handle(new RequestTimedOut(move.OperationId)));
        Assert.Equal(TrayStage.RecoveryRequired, flow.Snapshot().Stage);
    }

    [Fact]
    public void S01_S04_timeout_invalid_identity_and_late_results_do_not_create_parts()
    {
        var (flow, request) = StartScan();
        Assert.Empty(flow.Handle(Scan(request, runId: "other-tray")));
        Assert.Empty(flow.Handle(Scan(request, epoch: 0)));
        Assert.Equal(TrayStage.Scanning3D, flow.Snapshot().Stage);
        Assert.Empty(flow.Handle(new RequestTimedOut(request.RequestId)));
        Assert.Equal("ST01_SCAN_TIMEOUT", flow.Snapshot().FailureCode);
        Assert.Empty(flow.Handle(Scan(request)));
        Assert.Empty(flow.Snapshot().Parts);
        Assert.Equal(0, flow.Snapshot().CoordinateEpoch);
    }

    [Fact]
    public void S04a_wrong_request_id_does_not_bind_slots_before_deadline()
    {
        var (flow, request) = StartScan();
        var valid = Scan(request);
        Assert.Empty(flow.Handle(valid with { RequestId = Guid.NewGuid() }));
        Assert.Equal(TrayStage.Scanning3D, flow.Snapshot().Stage);
        Assert.Empty(flow.Snapshot().Parts);
        flow.Handle(new RequestTimedOut(request.RequestId));
        Assert.Equal("ST01_SCAN_TIMEOUT", flow.Snapshot().FailureCode);
    }

    [Fact]
    public void Scan_source_cannot_switch_from_synthetic_to_real_mid_run()
    {
        var (flow, request) = StartScan();
        Assert.Empty(flow.Handle(Scan(request, source: "real")));
        Assert.Equal("ST01_SCAN_SOURCE_MISMATCH", flow.Snapshot().FailureCode);
        Assert.Empty(flow.Snapshot().Parts);
    }

    [Fact]
    public void S02_empty_duplicate_and_blank_slots_fail_closed()
    {
        foreach (IReadOnlyList<LocatedSlot> slots in new IReadOnlyList<LocatedSlot>[]
                 {
                     [], [new("s", "p"), new("s", "q")], [new(" ", "p")], [new("s", " ")]
                 })
        {
            var (flow, request) = StartScan();
            Assert.Empty(flow.Handle(Scan(request, slots)));
            Assert.Equal("ST01_SCAN_INVALID", flow.Snapshot().FailureCode);
            Assert.Empty(flow.Snapshot().Parts);
        }
    }

    [Fact]
    public void S03_real_result_requires_machine_millimetres_finite_coordinates_and_calibration()
    {
        var flow = new TrayWorkflow("tray-1", "scene");
        var move = Assert.IsType<RequestMotion>(Assert.Single(flow.Handle(Ready(source: "real"))));
        flow.Handle(new MotionAccepted(move.OperationId));
        var request = Assert.IsType<RequestScan>(Assert.Single(flow.Handle(
            new MotionFinished(move.OperationId, MotionOutcome.Completed))));
        var invalid = Scan(request, [new LocatedSlot("s1", "position", new Position3D(double.NaN, 1, 2))],
            frame: "machine", unit: "mm", calibration: "cal-1");
        Assert.Empty(flow.Handle(invalid));
        Assert.Equal("ST01_SCAN_COORDINATE", flow.Snapshot().FailureCode);
        Assert.Empty(flow.Snapshot().Parts);
    }

    [Theory]
    [InlineData("camera", "mm", "cal-1")]
    [InlineData("machine", "pixel", "cal-1")]
    [InlineData("machine", "mm", null)]
    public void S03_wrong_frame_unit_or_missing_calibration_fails(string frame, string unit,
        string? calibration)
    {
        var flow = new TrayWorkflow("tray-1", "scene");
        var move = Assert.IsType<RequestMotion>(Assert.Single(flow.Handle(Ready(source: "real"))));
        flow.Handle(new MotionAccepted(move.OperationId));
        var request = Assert.IsType<RequestScan>(Assert.Single(flow.Handle(
            new MotionFinished(move.OperationId, MotionOutcome.Completed))));
        var result = Scan(request,
            [new LocatedSlot("s1", "position", new Position3D(1, 2, 3))],
            frame: frame, unit: unit, calibration: calibration);
        Assert.Empty(flow.Handle(result));
        Assert.Equal("ST01_SCAN_COORDINATE", flow.Snapshot().FailureCode);
    }

    [Fact]
    public void S01_camera_error_has_stable_code_and_no_F_request()
    {
        var (flow, scan) = StartScan();
        Assert.Empty(flow.Handle(new ScanFailed(scan.RequestId, "device unavailable")));
        Assert.Equal("ST01_SCAN_FAILED", flow.Snapshot().FailureCode);
        Assert.Empty(flow.Snapshot().Parts);
    }
}

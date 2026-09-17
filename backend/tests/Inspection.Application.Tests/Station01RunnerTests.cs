using Inspection.Application.Motion;
using Inspection.Application.Workflow;

namespace Inspection.Application.Tests;

public class Station01RunnerTests
{
    [Fact]
    public async Task Host_driver_ports_reach_waiting_F_without_executing_F()
    {
        var motion = new MotionPort(MotionOutcome.Completed);
        var locator = new PlacementPort();
        var runner = NewRunner(motion, locator);
        var result = await runner.RunAsync("tray-1", "scene");

        Assert.Equal(TrayStage.WaitingTrayCode, result.Snapshot.Stage);
        Assert.Equal((1, 1, 1), (result.MoveRequests, result.ScanRequests, result.FRequests));
        Assert.Equal(1, motion.SubmitCount);
        Assert.Equal(1, locator.CallCount);
        Assert.Equal(["ready-observed", "move-request", "move-accepted", "move-completed",
            "scan-request", "scan-result", "F-request-not-executed"], result.Trace.Select(t => t.Event));
        Assert.Equal(["tray-1:s1", "tray-1:s2"], result.Snapshot.Parts.Select(p => p.PartId));
    }

    [Fact]
    public async Task Unknown_motion_stops_before_camera_and_requires_reconciliation()
    {
        var motion = new MotionPort(MotionOutcome.Unknown);
        var locator = new PlacementPort();
        var result = await NewRunner(motion, locator).RunAsync("tray-1", "scene");
        Assert.Equal(TrayStage.RecoveryRequired, result.Snapshot.Stage);
        Assert.Equal("ST01_MOVE_UNKNOWN", result.Snapshot.FailureCode);
        Assert.Equal((1, 0, 0), (result.MoveRequests, result.ScanRequests, result.FRequests));
        Assert.Equal(0, locator.CallCount);
    }

    [Fact]
    public async Task Wrong_tray_result_is_not_accepted_or_sent_to_F()
    {
        var locator = new PlacementPort(wrongTray: true);
        var result = await NewRunner(new MotionPort(MotionOutcome.Completed), locator)
            .RunAsync("tray-1", "scene");
        Assert.Equal(TrayStage.Failed, result.Snapshot.Stage);
        Assert.Equal("ST01_SCAN_TIMEOUT", result.Snapshot.FailureCode);
        Assert.Equal((1, 1, 0), (result.MoveRequests, result.ScanRequests, result.FRequests));
        Assert.Empty(result.Snapshot.Parts);
    }

    [Fact]
    public async Task Wrong_camera_request_id_is_not_rebound_to_the_current_request()
    {
        var result = await NewRunner(new MotionPort(MotionOutcome.Completed),
                new PlacementPort(wrongRequest: true))
            .RunAsync("tray-1", "scene");
        Assert.Equal(TrayStage.Failed, result.Snapshot.Stage);
        Assert.Equal("ST01_SCAN_TIMEOUT", result.Snapshot.FailureCode);
        Assert.Equal(0, result.FRequests);
        Assert.Empty(result.Snapshot.Parts);
    }

    [Fact]
    public async Task Engineering_driver_rejects_real_source_before_motion()
    {
        var motion = new MotionPort(MotionOutcome.Completed);
        var locator = new PlacementPort();
        var runner = new Station01Runner(new RealReadyPort(), motion, locator);
        var result = await runner.RunAsync("tray-1", "scene");
        Assert.Equal(TrayStage.Failed, result.Snapshot.Stage);
        Assert.Equal("ST01_REAL_SOURCE_BLOCKED", result.Snapshot.FailureCode);
        Assert.Equal((0, 0, 0), (result.MoveRequests, result.ScanRequests, result.FRequests));
        Assert.Equal(0, motion.SubmitCount);
    }

    [Fact]
    public async Task Disconnected_motion_port_is_unknown_and_never_calls_3D()
    {
        var locator = new PlacementPort();
        var result = await NewRunner(new DisconnectedMotionPort(), locator)
            .RunAsync("tray-1", "scene");
        Assert.Equal(TrayStage.RecoveryRequired, result.Snapshot.Stage);
        Assert.Equal("ST01_MOVE_UNKNOWN", result.Snapshot.FailureCode);
        Assert.Equal(0, locator.CallCount);
        Assert.Contains(result.Trace, t => t.Event == "move-unknown" && t.Detail == "IOException");
    }

    [Fact]
    public async Task Camera_port_exception_fails_without_F()
    {
        var result = await NewRunner(new MotionPort(MotionOutcome.Completed), new FailingPlacementPort())
            .RunAsync("tray-1", "scene");
        Assert.Equal(TrayStage.Failed, result.Snapshot.Stage);
        Assert.Equal("ST01_SCAN_FAILED", result.Snapshot.FailureCode);
        Assert.Equal((1, 1, 0), (result.MoveRequests, result.ScanRequests, result.FRequests));
        Assert.Contains(result.Trace, t => t.Event == "scan-error" && t.Detail == "IOException");
    }

    private static Station01Runner NewRunner(IMotionDevice motion, IPlacementLocator locator) =>
        new(new ReadyPort(), motion, locator, motionTimeout: TimeSpan.FromSeconds(2));

    private sealed class ReadyPort : IReadyObserver
    {
        public Task<DeviceReady> ObserveAsync(string trayRunId, CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceReady(trayRunId, true, true, true,
                DateTimeOffset.UtcNow, "synthetic"));
    }

    private sealed class RealReadyPort : IReadyObserver
    {
        public Task<DeviceReady> ObserveAsync(string trayRunId, CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceReady(trayRunId, true, true, true,
                DateTimeOffset.UtcNow, "real"));
    }

    private sealed class MotionPort(MotionOutcome outcome) : IMotionDevice
    {
        public int SubmitCount { get; private set; }
        public Task<MotionDeviceReceipt> SubmitAsync(RequestMotion request, CancellationToken cancellationToken)
        {
            SubmitCount++;
            Assert.Equal(MotionKind.MoveTo3D, request.Kind);
            return Task.FromResult(new MotionDeviceReceipt(true, null));
        }
        public Task<MotionDeviceCompletion> WaitForCompletionAsync(Guid operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MotionDeviceCompletion(outcome, null));
    }

    private sealed class PlacementPort(bool wrongTray = false, bool wrongRequest = false) : IPlacementLocator
    {
        public int CallCount { get; private set; }
        public Task<PlacementScan> LocateAsync(RequestScan request, CancellationToken cancellationToken)
        {
            CallCount++;
            var metadata = new PlacementMetadata(wrongTray ? "other-tray" : request.TrayRunId,
                request.ProposedEpoch, request.ExpectedSource, "synthetic", "opaque", null,
                DateTimeOffset.UtcNow);
            return Task.FromResult(new PlacementScan(wrongRequest ? Guid.NewGuid() : request.RequestId, metadata,
                [new LocatedSlot("s1", "position-1"), new LocatedSlot("s2", "position-2")]));
        }
    }

    private sealed class DisconnectedMotionPort : IMotionDevice
    {
        public Task<MotionDeviceReceipt> SubmitAsync(RequestMotion request,
            CancellationToken cancellationToken) =>
            throw new IOException("link lost");
        public Task<MotionDeviceCompletion> WaitForCompletionAsync(Guid operationId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("must not wait after failed submit");
    }

    private sealed class FailingPlacementPort : IPlacementLocator
    {
        public Task<PlacementScan> LocateAsync(RequestScan request,
            CancellationToken cancellationToken) =>
            throw new IOException("camera link lost");
    }
}

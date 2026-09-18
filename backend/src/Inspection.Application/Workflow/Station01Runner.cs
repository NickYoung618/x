using System.Threading.Channels;
using Inspection.Application.Motion;

namespace Inspection.Application.Workflow;

public sealed record Station01Trace(DateTimeOffset AtUtc, string Event, Guid? CorrelationId,
    TrayStage Stage, string? FailureCode, string? Detail);

public sealed record Station01ExecutionResult(WorkflowSnapshot Snapshot, int MoveRequests,
    int ScanRequests, int FRequests, IReadOnlyList<Station01Trace> Trace);

/// <summary>
/// Engineering-only S01 driver. Device ports are supplied by another component; this class
/// never implements a PLC or camera simulator and never executes the F request.
/// </summary>
public sealed class Station01Runner
{
    private readonly IReadyObserver ready;
    private readonly IMotionDevice motion;
    private readonly IPlacementLocator placement;
    private readonly TimeProvider clock;
    private readonly TimeSpan readyTimeout;
    private readonly TimeSpan motionTimeout;
    private readonly TimeSpan scanTimeout;

    public Station01Runner(IReadyObserver ready, IMotionDevice motion, IPlacementLocator placement,
        TimeProvider? clock = null, TimeSpan? readyTimeout = null,
        TimeSpan? motionTimeout = null, TimeSpan? scanTimeout = null)
    {
        this.ready = ready;
        this.motion = motion;
        this.placement = placement;
        this.clock = clock ?? TimeProvider.System;
        this.readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(3);
        this.motionTimeout = motionTimeout ?? TimeSpan.FromSeconds(5);
        this.scanTimeout = scanTimeout ?? TimeSpan.FromSeconds(5);
        if (this.readyTimeout <= TimeSpan.Zero || this.motionTimeout <= TimeSpan.Zero || this.scanTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(readyTimeout), "Station timeouts must be positive.");
    }

    public async Task<Station01ExecutionResult> RunAsync(string trayRunId, string scenarioId,
        CancellationToken cancellationToken = default)
    {
        var flow = new TrayWorkflow(trayRunId, scenarioId, clock);
        var trace = new List<Station01Trace>();
        var moveCount = 0;
        var scanCount = 0;
        var fCount = 0;
        void Record(string name, Guid? id = null, string? detail = null)
        {
            var snapshot = flow.Snapshot();
            trace.Add(new Station01Trace(clock.GetUtcNow(), name, id, snapshot.Stage,
                snapshot.FailureCode, detail));
        }

        DeviceReady observation;
        try
        {
            observation = await ready.ObserveAsync(trayRunId, cancellationToken)
                .WaitAsync(readyTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            flow.Handle(new ReadyFailed("timeout", TimedOut: true));
            Record("ready-timeout");
            return Finish();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            flow.Handle(new ReadyFailed(error.GetType().Name));
            Record("ready-error", detail: error.GetType().Name);
            return Finish();
        }

        if (observation.Source != "synthetic")
        {
            flow.Handle(new ReadyFailed("engineering entry accepts synthetic source only",
                Code: "ST01_REAL_SOURCE_BLOCKED"));
            Record("real-source-blocked");
            return Finish();
        }
        var effects = flow.Handle(observation);
        Record("ready-observed");
        if (effects.SingleOrDefault() is not RequestMotion move) return Finish();
        moveCount++;
        Record("move-request", move.OperationId, move.PositionRef);

        var eventQueue = Channel.CreateUnbounded<WorkflowEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        await using (var lane = new MotionExecutionLane(motion, new QueueSink(eventQueue.Writer),
                         capacity: 1, actionTimeout: motionTimeout))
        {
            if (!lane.TrySubmit(move))
            {
                flow.Handle(new MotionDispatchFailed(move.OperationId, "lane rejected request"));
                Record("move-dispatch-failed", move.OperationId);
                return Finish();
            }
            var motionDeadline = clock.GetUtcNow() + motionTimeout + TimeSpan.FromSeconds(1);
            while (flow.Snapshot().Stage == TrayStage.MovingTo3D)
            {
                WorkflowEvent feedback;
                try
                {
                    var remaining = motionDeadline - clock.GetUtcNow();
                    feedback = await eventQueue.Reader.ReadAsync(cancellationToken)
                        .AsTask().WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromTicks(1), cancellationToken);
                }
                catch (TimeoutException)
                {
                    flow.Handle(new RequestTimedOut(move.OperationId));
                    Record("move-timeout", move.OperationId);
                    break;
                }
                catch (OperationCanceledException)
                {
                    flow.Handle(new RequestTimedOut(move.OperationId));
                    Record("move-cancelled-unknown", move.OperationId);
                    break;
                }
                effects = flow.Handle(feedback);
                Record(feedback switch
                {
                    MotionAccepted => "move-accepted",
                    MotionFinished finished => $"move-{finished.Outcome.ToString().ToLowerInvariant()}",
                    _ => "move-feedback"
                }, move.OperationId, (feedback as MotionFinished)?.Reason);
                if (effects.SingleOrDefault() is RequestScan) break;
            }
        }

        if (effects.SingleOrDefault() is not RequestScan scan ||
            flow.Snapshot().Stage != TrayStage.Scanning3D)
            return Finish();

        scanCount++;
        Record("scan-request", scan.RequestId);
        try
        {
            var result = await placement.LocateAsync(scan, cancellationToken)
                .WaitAsync(scanTimeout, cancellationToken);
            effects = flow.Handle(new ScanCompleted(result.RequestId, result));
            Record("scan-result", result.RequestId,
                $"source={result?.Metadata?.Source};device={result?.Metadata?.DeviceId};version={result?.Metadata?.DeviceVersion};frame={result?.Metadata?.Frame};unit={result?.Metadata?.Unit};calibration={result?.Metadata?.CalibrationVersion};slots={result?.Slots?.Count}");
            if (flow.Snapshot().Stage == TrayStage.Scanning3D)
            {
                // A completed provider call produced only an unrelated result; no more result can arrive.
                effects = flow.Handle(new RequestTimedOut(scan.RequestId));
                Record("scan-unmatched-timeout", scan.RequestId);
            }
        }
        catch (TimeoutException)
        {
            effects = flow.Handle(new RequestTimedOut(scan.RequestId));
            Record("scan-timeout", scan.RequestId);
        }
        catch (OperationCanceledException)
        {
            effects = flow.Handle(new RequestTimedOut(scan.RequestId));
            Record("scan-cancelled", scan.RequestId);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            effects = flow.Handle(new ScanFailed(scan.RequestId, error.GetType().Name));
            Record("scan-error", scan.RequestId, error.GetType().Name);
        }
        if (effects.SingleOrDefault() is RequestTrayCode code)
        {
            fCount++;
            Record("F-request-not-executed", code.RequestId);
        }
        return Finish();

        Station01ExecutionResult Finish() => new(flow.Snapshot(), moveCount, scanCount, fCount,
            trace.AsReadOnly());
    }

    private sealed class QueueSink(ChannelWriter<WorkflowEvent> writer) : IMotionEventSink
    {
        public ValueTask PublishAsync(WorkflowEvent result, CancellationToken cancellationToken) =>
            writer.WriteAsync(result, cancellationToken);
    }
}

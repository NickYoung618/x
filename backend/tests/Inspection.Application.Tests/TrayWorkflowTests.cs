using Inspection.Application.Workflow;
using Inspection.Domain.Planning;

namespace Inspection.Application.Tests;

public class TrayWorkflowTests
{
    private static readonly LocatedSlot[] InitialSlots = [new("s1", "epoch1-s1"), new("s2", "epoch1-s2")];
    private static readonly FaceDefinition[] TwoFaces = [new("front", CameraGroup.AB), new("back", CameraGroup.AB)];

    private static (TrayWorkflow Flow, RequestMotion Motion) Prepared()
    {
        var flow = new TrayWorkflow("run-1", "ordinary");
        var scan = Assert.IsType<RequestScan>(Assert.Single(flow.Handle(new DeviceReady(true))));
        Assert.True(scan.Initial);
        var code = Assert.IsType<RequestTrayCode>(Assert.Single(flow.Handle(new ScanCompleted(scan.RequestId, InitialSlots))));
        var resolve = Assert.IsType<ResolveRecipe>(Assert.Single(flow.Handle(new TrayCodeRead(code.RequestId, "tray-7"))));
        Assert.Equal("ordinary", resolve.ScenarioId);
        Assert.Equal("tray-7", resolve.TrayCode);
        var motion = Assert.IsType<RequestMotion>(Assert.Single(flow.Handle(new RecipeResolved(resolve.RequestId,
            [new RecipeSnapshot("recipe", "v7", TwoFaces)]))));
        return (flow, motion);
    }

    [Fact]
    public void Full_framework_order_preserves_part_identity_and_blocks_old_coordinates()
    {
        var (flow, first) = Prepared();
        var parts = flow.Snapshot().Parts.Select(p => p.PartId).ToArray();
        var motions = new List<RequestMotion>();
        var captures = new List<RequestCapture>();
        var scans = new List<RequestScan>();
        var jobs = new List<RequestAlgorithm>();
        var pending = new Queue<WorkflowEffect>([first]);
        while (pending.Count > 0)
        {
            switch (pending.Dequeue())
            {
                case RequestMotion motion:
                    motions.Add(motion);
                    Assert.Empty(flow.Handle(new MotionAccepted(Guid.NewGuid()))); // stale acceptance
                    Assert.Empty(flow.Handle(new MotionAccepted(motion.OperationId)));
                    foreach (var effect in flow.Handle(new MotionFinished(motion.OperationId, MotionOutcome.Completed))) pending.Enqueue(effect);
                    break;
                case RequestCapture capture:
                    captures.Add(capture);
                    foreach (var effect in flow.Handle(new CaptureFinished(capture.RequestId, capture.Key, true, $"frame-{captures.Count}"))) pending.Enqueue(effect);
                    break;
                case RequestAlgorithm algorithm:
                    jobs.Add(algorithm); // algorithm is free to finish after later mechanical work
                    break;
                case RequestScan scan:
                    scans.Add(scan);
                    Assert.False(scan.Initial);
                    Assert.Equal(2, scan.ProposedEpoch);
                    Assert.Equal(0, flow.Snapshot().CoordinateEpoch); // old epoch is invalid while scanning
                    foreach (var effect in flow.Handle(new ScanCompleted(scan.RequestId,
                        [new LocatedSlot("s1", "epoch2-s1"), new LocatedSlot("s2", "epoch2-s2")]))) pending.Enqueue(effect);
                    break;
                default: throw new InvalidOperationException("Unexpected workflow effect.");
            }
        }
        Assert.Equal(8, captures.Count);
        Assert.Equal(["A:s1", "A:s2", "B:s1", "B:s2", "A:s1", "A:s2", "B:s1", "B:s2"],
            captures.Select(c => $"{c.Key.Camera}:{c.Key.PartId.Split(':')[1]}"));
        Assert.Equal(2, motions.Count(m => m.Kind == MotionKind.FlipPart));
        Assert.Single(scans);
        Assert.Equal(8, jobs.Count);
        Assert.Equal(Enumerable.Repeat(1, 6).Concat(Enumerable.Repeat(2, 4)), motions.Select(m => m.CoordinateEpoch));
        Assert.Equal(parts, flow.Snapshot().Parts.Select(p => p.PartId));
        Assert.Equal("v7", flow.Snapshot().Recipe!.Version);
        Assert.Equal(TrayStage.AwaitingAlgorithms, flow.Snapshot().Stage);
        foreach (var job in jobs.AsEnumerable().Reverse().Take(7))
            flow.Handle(new AlgorithmFinished(job.Key, AlgorithmOutcome.Completed));
        Assert.Empty(flow.Handle(new AlgorithmDeadlineReached()));
        Assert.Equal(TrayStage.AwaitingDecision, flow.Snapshot().Stage);
        Assert.Single(flow.Snapshot().Algorithms, kv => kv.Value == AlgorithmStatus.TimedOut);
        Assert.Empty(flow.Handle(new AlgorithmFinished(jobs[0].Key, AlgorithmOutcome.Completed)));
        Assert.Equal(AlgorithmStatus.TimedOut, flow.Snapshot().Algorithms[jobs[0].Key]);
    }

    [Fact]
    public void Preparation_requires_fresh_3d_f_code_and_unique_recipe()
    {
        var unclamped = new TrayWorkflow("r", "scene");
        Assert.Empty(unclamped.Handle(new DeviceReady(false)));
        Assert.Equal(TrayStage.Failed, unclamped.Snapshot().Stage);

        var missing3d = new TrayWorkflow("r", "scene");
        var scan = Assert.IsType<RequestScan>(Assert.Single(missing3d.Handle(new DeviceReady(true))));
        Assert.Empty(missing3d.Handle(new ScanFailed(scan.RequestId, "camera unavailable")));
        Assert.Equal(TrayStage.Failed, missing3d.Snapshot().Stage);

        var codeFail = new TrayWorkflow("r", "scene");
        scan = Assert.IsType<RequestScan>(Assert.Single(codeFail.Handle(new DeviceReady(true))));
        var code = Assert.IsType<RequestTrayCode>(Assert.Single(codeFail.Handle(new ScanCompleted(scan.RequestId, InitialSlots))));
        Assert.Empty(codeFail.Handle(new TrayCodeRead(code.RequestId, null)));
        Assert.Equal(TrayStage.Failed, codeFail.Snapshot().Stage);

        foreach (var candidates in new[] { Array.Empty<RecipeSnapshot>(), new[] {
                     new RecipeSnapshot("one", "v1", TwoFaces), new RecipeSnapshot("two", "v2", TwoFaces) } })
        {
            var flow = new TrayWorkflow("r", "scene");
            scan = Assert.IsType<RequestScan>(Assert.Single(flow.Handle(new DeviceReady(true))));
            code = Assert.IsType<RequestTrayCode>(Assert.Single(flow.Handle(new ScanCompleted(scan.RequestId, InitialSlots))));
            var resolve = Assert.IsType<ResolveRecipe>(Assert.Single(flow.Handle(new TrayCodeRead(code.RequestId, "T"))));
            Assert.Empty(flow.Handle(new RecipeResolved(resolve.RequestId, candidates)));
            Assert.Equal(TrayStage.Failed, flow.Snapshot().Stage);
        }
    }

    [Fact]
    public void Motion_acceptance_is_not_completion_and_unknown_never_retries()
    {
        var (flow, motion) = Prepared();
        Assert.Empty(flow.Handle(new MotionFinished(motion.OperationId, MotionOutcome.Completed))); // no acceptance
        Assert.Equal(TrayStage.WaitingMotion, flow.Snapshot().Stage);
        Assert.Empty(flow.Handle(new MotionAccepted(motion.OperationId)));
        Assert.Empty(flow.Handle(new MotionFinished(Guid.NewGuid(), MotionOutcome.Completed)));
        Assert.Equal(TrayStage.WaitingMotion, flow.Snapshot().Stage);
        Assert.Empty(flow.Handle(new MotionFinished(motion.OperationId, MotionOutcome.Unknown)));
        Assert.Equal(TrayStage.RecoveryRequired, flow.Snapshot().Stage);
        Assert.Empty(flow.Handle(new MotionFinished(motion.OperationId, MotionOutcome.Completed)));
        Assert.Equal(TrayStage.RecoveryRequired, flow.Snapshot().Stage);
    }

    [Fact]
    public void Missing_frame_marks_dependency_failure_but_next_safe_capture_continues()
    {
        var (flow, motion) = Prepared();
        flow.Handle(new MotionAccepted(motion.OperationId));
        var capture = Assert.IsType<RequestCapture>(Assert.Single(flow.Handle(new MotionFinished(motion.OperationId, MotionOutcome.Completed))));
        Assert.Empty(flow.Handle(new CaptureFinished(Guid.NewGuid(), capture.Key, true, "wrong-id")));
        var next = Assert.IsType<RequestMotion>(Assert.Single(flow.Handle(new CaptureFinished(capture.RequestId, capture.Key, false, null))));
        Assert.Equal(MotionKind.PositionForCapture, next.Kind);
        Assert.Equal(CaptureStatus.Failed, flow.Snapshot().Captures[capture.Key]);
        Assert.Equal(AlgorithmStatus.DependencyFailed, flow.Snapshot().Algorithms[capture.Key]);
    }

    [Fact]
    public void Rescan_missing_original_slot_stops_before_next_face_motion()
    {
        var (flow, first) = Prepared();
        WorkflowEffect current = first;
        for (var steps = 0; steps < 15; steps++)
        {
            if (current is RequestMotion motion)
            {
                flow.Handle(new MotionAccepted(motion.OperationId));
                current = Assert.Single(flow.Handle(new MotionFinished(motion.OperationId, MotionOutcome.Completed)));
            }
            else if (current is RequestCapture capture)
            {
                var effects = flow.Handle(new CaptureFinished(capture.RequestId, capture.Key, false, null));
                current = Assert.Single(effects);
            }
            else if (current is RequestScan scan)
            {
                Assert.Equal(0, flow.Snapshot().CoordinateEpoch);
                Assert.Empty(flow.Handle(new ScanCompleted(scan.RequestId, [new LocatedSlot("s1", "p")] )));
                Assert.Equal(TrayStage.Failed, flow.Snapshot().Stage);
                return;
            }
        }
        throw new InvalidOperationException("Expected rescan was never requested.");
    }

    [Fact]
    public void Mixed_missing_frame_and_algorithm_failure_finish_as_technical_facts()
    {
        var (flow, first) = Prepared();
        var pending = new Queue<WorkflowEffect>([first]);
        var captures = new List<RequestCapture>();
        while (pending.Count > 0)
        {
            switch (pending.Dequeue())
            {
                case RequestMotion motion:
                    flow.Handle(new MotionAccepted(motion.OperationId));
                    foreach (var effect in flow.Handle(new MotionFinished(motion.OperationId, MotionOutcome.Completed))) pending.Enqueue(effect);
                    break;
                case RequestCapture capture:
                    captures.Add(capture);
                    var available = captures.Count != 2;
                    foreach (var effect in flow.Handle(new CaptureFinished(capture.RequestId, capture.Key,
                                 available, available ? $"frame-{captures.Count}" : null))) pending.Enqueue(effect);
                    break;
                case RequestAlgorithm algorithm:
                    if (algorithm.Key == captures[0].Key)
                        flow.Handle(new AlgorithmFinished(algorithm.Key, AlgorithmOutcome.Failed));
                    // Other results remain pending until the decision deadline.
                    break;
                case RequestScan scan:
                    foreach (var effect in flow.Handle(new ScanCompleted(scan.RequestId,
                                 [new LocatedSlot("s1", "new-s1"), new LocatedSlot("s2", "new-s2")]))) pending.Enqueue(effect);
                    break;
            }
        }
        Assert.Equal(8, captures.Count);
        Assert.Equal(TrayStage.AwaitingAlgorithms, flow.Snapshot().Stage);
        flow.Handle(new AlgorithmDeadlineReached());
        var state = flow.Snapshot();
        Assert.Equal(TrayStage.AwaitingDecision, state.Stage);
        Assert.Equal(8, state.Captures.Count);
        Assert.Equal(8, state.Algorithms.Count);
        Assert.Equal(AlgorithmStatus.Failed, state.Algorithms[captures[0].Key]);
        Assert.Equal(AlgorithmStatus.DependencyFailed, state.Algorithms[captures[1].Key]);
        Assert.Equal(6, state.Algorithms.Values.Count(x => x == AlgorithmStatus.TimedOut));
        Assert.DoesNotContain(state.Algorithms.Values, x => x is AlgorithmStatus.Waiting or AlgorithmStatus.Completed);
    }

    [Fact]
    public void Invalid_motion_outcome_cannot_be_treated_as_completion()
    {
        var (flow, motion) = Prepared();
        flow.Handle(new MotionAccepted(motion.OperationId));
        Assert.Empty(flow.Handle(new MotionFinished(motion.OperationId, (MotionOutcome)99)));
        Assert.Equal(TrayStage.Failed, flow.Snapshot().Stage);
    }

    [Fact]
    public void Old_algorithm_can_finish_after_motion_failure_and_deadline_closes_rest()
    {
        var (flow, first) = Prepared();
        flow.Handle(new MotionAccepted(first.OperationId));
        var capture = Assert.IsType<RequestCapture>(Assert.Single(flow.Handle(new MotionFinished(first.OperationId, MotionOutcome.Completed))));
        var effects = flow.Handle(new CaptureFinished(capture.RequestId, capture.Key, true, "frame"));
        var next = Assert.IsType<RequestMotion>(effects.Single(e => e is RequestMotion));
        flow.Handle(new MotionAccepted(next.OperationId));
        flow.Handle(new MotionFinished(next.OperationId, MotionOutcome.Unknown));
        Assert.Equal(TrayStage.RecoveryRequired, flow.Snapshot().Stage);
        flow.Handle(new AlgorithmFinished(capture.Key, AlgorithmOutcome.Completed));
        Assert.Equal(AlgorithmStatus.Completed, flow.Snapshot().Algorithms[capture.Key]);
        Assert.Equal(TrayStage.RecoveryRequired, flow.Snapshot().Stage);
    }
}

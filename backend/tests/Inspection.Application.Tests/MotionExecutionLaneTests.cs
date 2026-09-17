using System.Collections.Concurrent;
using Inspection.Application.Motion;
using Inspection.Application.Workflow;

namespace Inspection.Application.Tests;

public class MotionExecutionLaneTests
{
    [Fact]
    public async Task Single_lane_runs_only_one_device_action_and_unknown_blocks_queued_action()
    {
        var device = new ControlledDevice();
        var sink = new CollectingSink();
        await using var lane = new MotionExecutionLane(device, sink, capacity: 1);
        var first = NewMotion(); var second = NewMotion(); var third = NewMotion();
        Assert.True(lane.TrySubmit(first));
        await device.FirstSubmitted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(lane.TrySubmit(second));
        Assert.False(lane.TrySubmit(third)); // bounded queue, active action plus one waiting
        Assert.Equal(1, device.SubmissionCount);
        device.FinishFirst.SetResult(new MotionDeviceCompletion(MotionOutcome.Unknown, "feedback lost"));
        await sink.TwoFinishes.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(lane.RecoveryRequired);
        Assert.Equal(1, device.SubmissionCount); // second never went to physical device
        Assert.False(lane.TrySubmit(NewMotion()));
        Assert.Equal(2, sink.Events.OfType<MotionFinished>().Count());
        Assert.All(sink.Events.OfType<MotionFinished>(), e => Assert.Equal(MotionOutcome.Unknown, e.Outcome));
        Assert.Single(sink.Events.OfType<MotionAccepted>());
    }

    [Fact]
    public async Task Two_completed_actions_are_serial_and_report_acceptance_before_completion()
    {
        var device = new ControlledDevice();
        var sink = new CollectingSink();
        await using var lane = new MotionExecutionLane(device, sink, capacity: 2);
        var first = NewMotion(); var second = NewMotion();
        Assert.True(lane.TrySubmit(first)); Assert.False(lane.TrySubmit(first)); // duplicate physical command id
        Assert.True(lane.TrySubmit(second));
        await device.FirstSubmitted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, device.SubmissionCount);
        device.FinishFirst.SetResult(new MotionDeviceCompletion(MotionOutcome.Completed, null));
        await sink.TwoFinishes.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, device.SubmissionCount);
        Assert.False(lane.RecoveryRequired);
        Assert.Equal(new[] { typeof(MotionAccepted), typeof(MotionFinished), typeof(MotionAccepted), typeof(MotionFinished) },
            sink.Events.Select(e => e.GetType()));
    }

    [Fact]
    public async Task Motion_deadline_marks_unknown_without_replaying_device_command()
    {
        var device = new ControlledDevice();
        var sink = new CollectingSink();
        await using var lane = new MotionExecutionLane(device, sink, capacity: 1,
            actionTimeout: TimeSpan.FromMilliseconds(150));
        var first = NewMotion();
        Assert.True(lane.TrySubmit(first));
        await device.FirstSubmitted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await sink.OneFinish.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(lane.RecoveryRequired);
        Assert.Equal(1, device.SubmissionCount);
        Assert.Equal(MotionOutcome.Unknown, Assert.Single(sink.Events.OfType<MotionFinished>()).Outcome);
    }

    [Fact]
    public async Task Lost_event_sink_stops_admitting_motion_and_exposes_failure()
    {
        var device = new ControlledDevice();
        await using var lane = new MotionExecutionLane(device, new FailingSink(), capacity: 1);
        Assert.True(lane.TrySubmit(NewMotion()));
        await device.FirstSubmitted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!lane.RecoveryRequired && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(lane.RecoveryRequired);
        Assert.IsType<IOException>(lane.EventDispatchFailure);
        Assert.False(lane.TrySubmit(NewMotion()));
        Assert.Equal(1, device.SubmissionCount);
    }

    private static RequestMotion NewMotion() => new(Guid.NewGuid(), MotionKind.PositionForCapture, "p", "front", 1, "xy");

    private sealed class FailingSink : IMotionEventSink
    {
        public ValueTask PublishAsync(WorkflowEvent result, CancellationToken ct) =>
            throw new IOException("Completion channel unavailable.");
    }

    private sealed class ControlledDevice : IMotionDevice
    {
        private int count;
        public int SubmissionCount => Volatile.Read(ref count);
        public TaskCompletionSource FirstSubmitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<MotionDeviceCompletion> FinishFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<MotionDeviceReceipt> SubmitAsync(RequestMotion request, CancellationToken ct)
        {
            if (Interlocked.Increment(ref count) == 1) FirstSubmitted.SetResult();
            return Task.FromResult(new MotionDeviceReceipt(true, null));
        }
        public Task<MotionDeviceCompletion> WaitForCompletionAsync(Guid id, CancellationToken ct) =>
            SubmissionCount == 1 ? FinishFirst.Task : Task.FromResult(new MotionDeviceCompletion(MotionOutcome.Completed, null));
    }

    private sealed class CollectingSink : IMotionEventSink
    {
        private readonly ConcurrentQueue<WorkflowEvent> events = new();
        private int finished;
        public WorkflowEvent[] Events => events.ToArray();
        public TaskCompletionSource OneFinish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TwoFinishes { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask PublishAsync(WorkflowEvent result, CancellationToken ct)
        {
            events.Enqueue(result);
            if (result is MotionFinished)
            {
                var count = Interlocked.Increment(ref finished);
                if (count == 1) OneFinish.TrySetResult();
                if (count == 2) TwoFinishes.TrySetResult();
            }
            return ValueTask.CompletedTask;
        }
    }
}

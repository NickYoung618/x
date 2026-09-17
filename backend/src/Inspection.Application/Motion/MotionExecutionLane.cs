using System.Threading.Channels;
using Inspection.Application.Workflow;

namespace Inspection.Application.Motion;

public sealed record MotionDeviceReceipt(bool Accepted, string? Reason);
public sealed record MotionDeviceCompletion(MotionOutcome Outcome, string? Reason);

public interface IMotionDevice
{
    Task<MotionDeviceReceipt> SubmitAsync(RequestMotion request, CancellationToken cancellationToken);
    Task<MotionDeviceCompletion> WaitForCompletionAsync(Guid operationId, CancellationToken cancellationToken);
}

public interface IMotionEventSink
{
    ValueTask PublishAsync(WorkflowEvent result, CancellationToken cancellationToken);
}

/// <summary>One bounded device lane. Physical execution is never retried here.</summary>
public sealed class MotionExecutionLane : IAsyncDisposable
{
    private readonly Channel<RequestMotion> queue;
    private readonly IMotionDevice device;
    private readonly IMotionEventSink sink;
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private readonly TimeSpan actionTimeout;
    private readonly object submitGate = new();
    private readonly HashSet<Guid> submittedIds = [];
    private volatile bool recoveryRequired;
    private volatile Exception? eventDispatchFailure;

    public MotionExecutionLane(IMotionDevice device, IMotionEventSink sink, int capacity = 16,
        TimeSpan? actionTimeout = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.actionTimeout = actionTimeout ?? TimeSpan.FromSeconds(5);
        if (this.actionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(actionTimeout));
        this.device = device;
        this.sink = sink;
        queue = Channel.CreateBounded<RequestMotion>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
        });
        worker = RunAsync();
    }

    public bool RecoveryRequired => recoveryRequired;
    public Exception? EventDispatchFailure => eventDispatchFailure;
    public bool TrySubmit(RequestMotion request)
    {
        if (request.OperationId == Guid.Empty || request.CoordinateEpoch < 1 || string.IsNullOrWhiteSpace(request.PositionRef))
            return false;
        lock (submitGate)
        {
            if (recoveryRequired || submittedIds.Contains(request.OperationId) || !queue.Writer.TryWrite(request)) return false;
            submittedIds.Add(request.OperationId);
            return true;
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var request in queue.Reader.ReadAllAsync(stop.Token))
            {
                if (recoveryRequired)
                {
                    if (!await PublishSafelyAsync(new MotionFinished(request.OperationId, MotionOutcome.Unknown))) break;
                    continue;
                }
                MotionOutcome outcome;
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    deadline.CancelAfter(actionTimeout);
                    var receipt = await device.SubmitAsync(request, deadline.Token).WaitAsync(deadline.Token);
                    if (!receipt.Accepted) outcome = MotionOutcome.Failed;
                    else
                    {
                        if (!await PublishSafelyAsync(new MotionAccepted(request.OperationId))) break;
                        var completed = await device.WaitForCompletionAsync(request.OperationId, deadline.Token).WaitAsync(deadline.Token);
                        outcome = completed.Outcome;
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception) { outcome = MotionOutcome.Unknown; }
                if (outcome == MotionOutcome.Unknown) recoveryRequired = true;
                if (!await PublishSafelyAsync(new MotionFinished(request.OperationId, outcome))) break;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private async ValueTask<bool> PublishSafelyAsync(WorkflowEvent result)
    {
        try
        {
            await sink.PublishAsync(result, stop.Token);
            return true;
        }
        catch (Exception error)
        {
            eventDispatchFailure = error;
            recoveryRequired = true;
            queue.Writer.TryComplete();
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
        stop.Cancel();
        try { await worker; } finally { stop.Dispose(); }
    }
}

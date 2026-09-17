using Inspection.Domain.Planning;

namespace Inspection.Application.Workflow;

public enum TrayStage
{
    WaitingReady, WaitingInitialScan, WaitingTrayCode, WaitingRecipe,
    WaitingMotion, WaitingCapture, WaitingRescan, AwaitingAlgorithms,
    AwaitingDecision, Failed, RecoveryRequired
}
public enum MotionKind { PositionForCapture, FlipPart }
public enum MotionOutcome { Completed, Failed, Unknown }
public enum CaptureStatus { Waiting, FrameReady, Failed }
public enum AlgorithmStatus { Waiting, Completed, Failed, DependencyFailed, TimedOut }
public enum AlgorithmOutcome { Completed, Failed }

public sealed record LocatedSlot(string SlotId, string PositionRef);
public sealed record RecipeSnapshot(string RecipeId, string Version, IReadOnlyList<FaceDefinition> Faces);
public sealed record CaptureKey(string PartId, string FaceId, Camera Camera, int Attempt);
public sealed record PartBinding(string PartId, string SlotId);
public sealed record WorkflowSnapshot(
    string TrayRunId, string ScenarioId, TrayStage Stage, string? TrayCode,
    RecipeSnapshot? Recipe, int CoordinateEpoch, IReadOnlyList<PartBinding> Parts,
    IReadOnlyDictionary<CaptureKey, CaptureStatus> Captures,
    IReadOnlyDictionary<CaptureKey, AlgorithmStatus> Algorithms,
    Guid? PendingRequestId, string? Failure);

public abstract record WorkflowEvent;
public sealed record DeviceReady(bool FixtureClamped) : WorkflowEvent;
public sealed record ScanCompleted(Guid RequestId, IReadOnlyList<LocatedSlot> Slots) : WorkflowEvent;
public sealed record ScanFailed(Guid RequestId, string Reason) : WorkflowEvent;
public sealed record TrayCodeRead(Guid RequestId, string? Code) : WorkflowEvent;
public sealed record RecipeResolved(Guid RequestId, IReadOnlyList<RecipeSnapshot> Matches) : WorkflowEvent;
public sealed record MotionAccepted(Guid OperationId) : WorkflowEvent;
public sealed record MotionFinished(Guid OperationId, MotionOutcome Outcome) : WorkflowEvent;
public sealed record CaptureFinished(Guid RequestId, CaptureKey Key, bool FrameAvailable, string? FrameRef) : WorkflowEvent;
public sealed record AlgorithmFinished(CaptureKey Key, AlgorithmOutcome Outcome) : WorkflowEvent;
public sealed record RequestTimedOut(Guid RequestId) : WorkflowEvent;
public sealed record AlgorithmDeadlineReached : WorkflowEvent;

public abstract record WorkflowEffect;
public sealed record RequestScan(Guid RequestId, int ProposedEpoch, bool Initial) : WorkflowEffect;
public sealed record RequestTrayCode(Guid RequestId) : WorkflowEffect;
public sealed record ResolveRecipe(Guid RequestId, string ScenarioId, string TrayCode) : WorkflowEffect;
public sealed record RequestMotion(Guid OperationId, MotionKind Kind, string PartId,
    string FaceId, int CoordinateEpoch, string PositionRef) : WorkflowEffect;
public sealed record RequestCapture(Guid RequestId, CaptureKey Key, int CoordinateEpoch) : WorkflowEffect;
public sealed record RequestAlgorithm(CaptureKey Key, string FrameRef) : WorkflowEffect;

// Adapters receive effects and return events. Interfaces are Application-owned; no PLC point table leaks here.
public interface IPlacementLocator
{
    Task<IReadOnlyList<LocatedSlot>> LocateAsync(RequestScan request, CancellationToken cancellationToken);
}
public interface ITrayCodeReader
{
    Task<string?> ReadFAsync(RequestTrayCode request, CancellationToken cancellationToken);
}
public interface IRecipeResolver
{
    Task<IReadOnlyList<RecipeSnapshot>> ResolveAsync(ResolveRecipe request, CancellationToken cancellationToken);
}
public interface ICapturePort
{
    Task<string?> CaptureAsync(RequestCapture request, CancellationToken cancellationToken);
}
public interface IAlgorithmPort
{
    Task<AlgorithmOutcome> AnalyzeAsync(RequestAlgorithm request, CancellationToken cancellationToken);
}

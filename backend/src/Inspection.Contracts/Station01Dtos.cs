namespace Inspection.Contracts;

public sealed record Station01PrepareDto(string TrayRunId, string ScenarioId);
public sealed record Station01TraceDto(DateTimeOffset AtUtc, string Event, Guid? CorrelationId,
    string Stage, string? FailureCode, string? Detail);
public sealed record Station01RunDto(string TrayRunId, string ScenarioId, string Stage,
    string? Source, string? FailureCode, string? Failure, Guid? PendingRequestId,
    int CoordinateEpoch, IReadOnlyList<string> PartIds, int MoveRequests, int ScanRequests,
    int FRequests, bool FExecuted, IReadOnlyList<Station01TraceDto> Trace);

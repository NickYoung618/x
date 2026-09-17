namespace Inspection.Contracts;

/// <summary>Stable, read-only status exposed to the UI and engineering tools.</summary>
public sealed record ModuleStatusDto(string Id, string Layer, string Owner, string State);

public sealed record CapabilityStatusDto(string Id, bool Available, string EvidenceLevel, string Reason);

public sealed record SystemStatusDto(
    string ArchitectureVersion,
    string Stage,
    bool ProductionReady,
    string RuntimeMode,
    IReadOnlyList<string> UnavailableCapabilities,
    IReadOnlyList<ModuleStatusDto> Modules,
    IReadOnlyList<CapabilityStatusDto> Capabilities);

/// <summary>Machine-readable rejection; no business command is accepted by this DTO.</summary>
public sealed record OperationProblemDto(string Code, string Title, string CorrelationId);

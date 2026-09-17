namespace Inspection.Application.Architecture;

public enum ModuleDeliveryState { ContractOnly, FrameworkOnly, LegacyEngineering, Implemented }
public enum InspectionRuntimeMode { Unconfigured, FullSimulation, ImageReplay, HybridCommissioning, ManualHandoff, Production }

public sealed record ModuleDefinition(string Id, string Layer, string Owner, ModuleDeliveryState State);
public sealed record CapabilityDefinition(string Id, bool Available, string EvidenceLevel, string Reason);

/// <summary>
/// Delivery facts for the V1.3 skeleton. A module's existence never implies a usable
/// production capability. Later station PRs must update these facts with evidence.
/// </summary>
public static class V13Architecture
{
    public const string Version = "1.3";
    public const string Stage = "V13FrameworkFoundation";
    public static InspectionRuntimeMode RuntimeMode => InspectionRuntimeMode.Unconfigured;

    public static IReadOnlyList<ModuleDefinition> Modules { get; } = Array.AsReadOnly<ModuleDefinition>(
    [
        new("Presentation", "L1", "Frontend", ModuleDeliveryState.FrameworkOnly),
        new("Api", "L2", "Backend", ModuleDeliveryState.FrameworkOnly),
        new("Jobs", "L3", "Backend", ModuleDeliveryState.ContractOnly),
        new("Recipes", "L3", "Backend", ModuleDeliveryState.ContractOnly),
        new("Workflow", "L3", "Backend", ModuleDeliveryState.FrameworkOnly),
        new("Motion", "L3", "Backend", ModuleDeliveryState.FrameworkOnly),
        new("Acquisition", "L3", "Backend", ModuleDeliveryState.ContractOnly),
        new("AlgorithmRuntime", "L4", "Backend/Algorithm", ModuleDeliveryState.ContractOnly),
        new("Quality", "L4", "Backend", ModuleDeliveryState.ContractOnly),
        new("Traceability", "L5", "Backend", ModuleDeliveryState.ContractOnly),
        new("Media", "L5", "Backend", ModuleDeliveryState.ContractOnly),
        new("DeviceAdapters", "L5", "Backend/PLC", ModuleDeliveryState.LegacyEngineering),
        new("Diagnostics", "CrossCutting", "Backend", ModuleDeliveryState.ContractOnly),
        new("ModelManagement", "CrossCutting", "Backend/Algorithm", ModuleDeliveryState.ContractOnly),
        new("Mes", "CrossCutting", "Backend", ModuleDeliveryState.ContractOnly)
    ]);

    public static IReadOnlyList<CapabilityDefinition> Capabilities { get; } = Array.AsReadOnly<CapabilityDefinition>(
    [
        Unavailable("TrayExecution"),
        Unavailable("DeviceIntegration"),
        Unavailable("V13Plc"),
        Unavailable("Placement3D"),
        Unavailable("TrayCodeF"),
        Unavailable("CaptureABCD"),
        Unavailable("PartCodeE"),
        Unavailable("AlgorithmRuntime"),
        Unavailable("Algorithms"),
        Unavailable("Traceability"),
        Unavailable("Desktop"),
        Unavailable("DatabaseDeployment")
    ]);

    public static bool ProductionReady => RuntimeMode == InspectionRuntimeMode.Production &&
        Capabilities.All(capability => capability.Available && capability.EvidenceLevel == "ProductionValidated");

    private static CapabilityDefinition Unavailable(string id) =>
        new(id, false, "NotRun", "该能力尚未按 V1.3 实现和验证。");
}

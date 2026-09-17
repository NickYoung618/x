using System.Collections.ObjectModel;
using Inspection.Domain.Planning;

namespace Inspection.Application.Workflow;

/// <summary>Single logical state owner. Handle only changes state and emits work; it never awaits devices.</summary>
public sealed class TrayWorkflow
{
    private readonly object gate = new();
    private readonly string runId;
    private readonly string scenarioId;
    private readonly List<PartBinding> parts = [];
    private readonly Dictionary<string, string> positions = new(StringComparer.Ordinal);
    private readonly Dictionary<CaptureKey, CaptureStatus> captures = [];
    private readonly Dictionary<CaptureKey, AlgorithmStatus> algorithms = [];
    private OrdinaryTrayPlan? plan;
    private RecipeSnapshot? recipe;
    private string? trayCode;
    private TrayStage stage = TrayStage.WaitingReady;
    private string? failure;
    private Guid? pendingRequest;
    private MotionKind? pendingMotionKind;
    private CaptureKey? pendingCapture;
    private bool motionAccepted;
    private int faceIndex;
    private int captureIndex;
    private int flipIndex;
    private int epoch;
    private int lastEpoch;

    public TrayWorkflow(string trayRunId, string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(trayRunId) || string.IsNullOrWhiteSpace(scenarioId))
            throw new ArgumentException("TrayRunId and ScenarioId are required.");
        runId = trayRunId;
        this.scenarioId = scenarioId;
    }

    public WorkflowSnapshot Snapshot()
    {
        lock (gate)
        {
            return new WorkflowSnapshot(runId, scenarioId, stage, trayCode, recipe, epoch,
                Array.AsReadOnly(parts.ToArray()),
                new ReadOnlyDictionary<CaptureKey, CaptureStatus>(new Dictionary<CaptureKey, CaptureStatus>(captures)),
                new ReadOnlyDictionary<CaptureKey, AlgorithmStatus>(new Dictionary<CaptureKey, AlgorithmStatus>(algorithms)),
                pendingRequest, failure);
        }
    }

    public IReadOnlyList<WorkflowEffect> Handle(WorkflowEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock (gate)
        {
            if (stage is TrayStage.Failed or TrayStage.RecoveryRequired)
                return input switch
                {
                    AlgorithmFinished analyzed => OnAlgorithm(analyzed),
                    AlgorithmDeadlineReached => OnAlgorithmDeadline(),
                    _ => []
                };
            if (stage == TrayStage.AwaitingDecision) return [];
            return input switch
            {
                DeviceReady ready => OnReady(ready),
                ScanCompleted scan => OnScan(scan),
                ScanFailed scan => OnScanFailed(scan),
                TrayCodeRead code => OnCode(code),
                RecipeResolved resolved => OnRecipe(resolved),
                MotionAccepted accepted => OnMotionAccepted(accepted),
                MotionFinished finished => OnMotionFinished(finished),
                CaptureFinished captured => OnCapture(captured),
                AlgorithmFinished analyzed => OnAlgorithm(analyzed),
                RequestTimedOut timedOut => OnTimedOut(timedOut),
                AlgorithmDeadlineReached => OnAlgorithmDeadline(),
                _ => []
            };
        }
    }

    private IReadOnlyList<WorkflowEffect> OnReady(DeviceReady input)
    {
        if (stage != TrayStage.WaitingReady) return [];
        if (!input.FixtureClamped) return Fail("Device is not ready and clamped.");
        return RequestNewScan(initial: true);
    }

    private IReadOnlyList<WorkflowEffect> RequestNewScan(bool initial)
    {
        pendingRequest = Guid.NewGuid();
        stage = initial ? TrayStage.WaitingInitialScan : TrayStage.WaitingRescan;
        return [new RequestScan(pendingRequest.Value, lastEpoch + 1, initial)];
    }

    private IReadOnlyList<WorkflowEffect> OnScan(ScanCompleted input)
    {
        if (pendingRequest != input.RequestId || stage is not (TrayStage.WaitingInitialScan or TrayStage.WaitingRescan)) return [];
        if (input.Slots is null || input.Slots.Count == 0 || input.Slots.Any(s => s is null || string.IsNullOrWhiteSpace(s.SlotId) || string.IsNullOrWhiteSpace(s.PositionRef)) ||
            input.Slots.Select(s => s.SlotId).Distinct(StringComparer.Ordinal).Count() != input.Slots.Count)
            return Fail("3D returned empty, duplicate, or invalid slots.");
        if (stage == TrayStage.WaitingInitialScan)
        {
            foreach (var slot in input.Slots) parts.Add(new PartBinding($"{runId}:{slot.SlotId}", slot.SlotId));
        }
        else if (parts.Count != input.Slots.Count ||
                 !parts.Select(p => p.SlotId).ToHashSet(StringComparer.Ordinal).SetEquals(input.Slots.Select(s => s.SlotId)))
            return Fail("Rescan cannot bind all original PartIds to the same slots.");

        positions.Clear();
        foreach (var slot in input.Slots) positions[slot.SlotId] = slot.PositionRef;
        epoch = ++lastEpoch;
        pendingRequest = null;
        if (stage == TrayStage.WaitingRescan) return NextCapture();
        pendingRequest = Guid.NewGuid();
        stage = TrayStage.WaitingTrayCode;
        return [new RequestTrayCode(pendingRequest.Value)];
    }

    private IReadOnlyList<WorkflowEffect> OnScanFailed(ScanFailed input) =>
        pendingRequest == input.RequestId && stage is TrayStage.WaitingInitialScan or TrayStage.WaitingRescan
            ? Fail($"3D failed: {input.Reason}") : [];

    private IReadOnlyList<WorkflowEffect> OnCode(TrayCodeRead input)
    {
        if (stage != TrayStage.WaitingTrayCode || pendingRequest != input.RequestId) return [];
        if (string.IsNullOrWhiteSpace(input.Code)) return Fail("F tray code is missing.");
        trayCode = input.Code;
        pendingRequest = Guid.NewGuid();
        stage = TrayStage.WaitingRecipe;
        return [new ResolveRecipe(pendingRequest.Value, scenarioId, trayCode)];
    }

    private IReadOnlyList<WorkflowEffect> OnRecipe(RecipeResolved input)
    {
        if (stage != TrayStage.WaitingRecipe || pendingRequest != input.RequestId) return [];
        if (input.Matches is null || input.Matches.Count != 1 || input.Matches[0] is null) return Fail("Tray code and scenario did not select exactly one recipe.");
        var match = input.Matches[0];
        if (string.IsNullOrWhiteSpace(match.RecipeId) || string.IsNullOrWhiteSpace(match.Version) || match.Faces is null)
            return Fail("Matched recipe lacks an identity or version.");
        var candidate = new RecipeSnapshot(match.RecipeId, match.Version, Array.AsReadOnly(match.Faces.ToArray()));
        try
        {
            plan = OrdinaryTrayPlanner.Build(parts.Select(p => p.PartId), candidate.Faces);
        }
        catch (ArgumentException e) { return Fail($"Invalid recipe plan: {e.Message}"); }
        recipe = candidate;
        pendingRequest = null;
        faceIndex = 0;
        captureIndex = 0;
        return NextCapture();
    }

    private IReadOnlyList<WorkflowEffect> NextCapture()
    {
        if (plan is null) return Fail("No frozen plan.");
        if (epoch == 0) return Fail("No current coordinate epoch.");
        if (captureIndex >= plan.Faces[faceIndex].Captures.Count)
        {
            if (faceIndex == plan.Faces.Count - 1)
            {
                stage = algorithms.Values.Any(s => s == AlgorithmStatus.Waiting) ? TrayStage.AwaitingAlgorithms : TrayStage.AwaitingDecision;
                return [];
            }
            flipIndex = 0;
            return NextFlip();
        }
        var face = plan.Faces[faceIndex];
        var target = face.Captures[captureIndex];
        var part = parts.Single(p => p.PartId == target.PartId);
        return RequestNewMotion(MotionKind.PositionForCapture, part, face.FaceId);
    }

    private IReadOnlyList<WorkflowEffect> NextFlip()
    {
        if (plan is null) return Fail("No frozen plan.");
        if (flipIndex < parts.Count)
            return RequestNewMotion(MotionKind.FlipPart, parts[flipIndex], plan.Faces[faceIndex].FaceId);
        // Flip completion invalidates all old positions. No next-face motion before a fresh scan.
        positions.Clear();
        epoch = 0;
        faceIndex++;
        captureIndex = 0;
        return RequestNewScan(initial: false);
    }

    private IReadOnlyList<WorkflowEffect> RequestNewMotion(MotionKind kind, PartBinding part, string faceId)
    {
        if (!positions.TryGetValue(part.SlotId, out var position) || epoch == 0) return Fail("Position is missing for current epoch.");
        pendingRequest = Guid.NewGuid();
        pendingMotionKind = kind;
        motionAccepted = false;
        stage = TrayStage.WaitingMotion;
        return [new RequestMotion(pendingRequest.Value, kind, part.PartId, faceId, epoch, position)];
    }

    private IReadOnlyList<WorkflowEffect> OnMotionAccepted(MotionAccepted input)
    {
        if (stage == TrayStage.WaitingMotion && pendingRequest == input.OperationId) motionAccepted = true;
        return [];
    }

    private IReadOnlyList<WorkflowEffect> OnMotionFinished(MotionFinished input)
    {
        if (stage != TrayStage.WaitingMotion || pendingRequest != input.OperationId) return [];
        if (input.Outcome == MotionOutcome.Unknown)
        {
            stage = TrayStage.RecoveryRequired;
            failure = "Motion result is unknown; physical reconciliation required.";
            return [];
        }
        if (input.Outcome != MotionOutcome.Completed) return Fail("Motion device reported failure or invalid outcome.");
        if (!motionAccepted) return []; // A completion without this operation's acceptance is not authoritative.
        pendingRequest = null;
        if (pendingMotionKind == MotionKind.FlipPart)
        {
            flipIndex++;
            return NextFlip();
        }
        var face = plan!.Faces[faceIndex];
        var target = face.Captures[captureIndex];
        var key = new CaptureKey(target.PartId, face.FaceId, target.Camera, 1);
        captures[key] = CaptureStatus.Waiting;
        algorithms[key] = AlgorithmStatus.Waiting;
        pendingCapture = key;
        pendingRequest = Guid.NewGuid();
        stage = TrayStage.WaitingCapture;
        return [new RequestCapture(pendingRequest.Value, key, epoch)];
    }

    private IReadOnlyList<WorkflowEffect> OnCapture(CaptureFinished input)
    {
        if (stage != TrayStage.WaitingCapture || pendingRequest != input.RequestId || pendingCapture != input.Key) return [];
        var key = input.Key;
        pendingRequest = null;
        pendingCapture = null;
        List<WorkflowEffect> effects = [];
        if (input.FrameAvailable && !string.IsNullOrWhiteSpace(input.FrameRef))
        {
            captures[key] = CaptureStatus.FrameReady;
            effects.Add(new RequestAlgorithm(key, input.FrameRef));
        }
        else
        {
            captures[key] = CaptureStatus.Failed;
            algorithms[key] = AlgorithmStatus.DependencyFailed;
        }
        captureIndex++;
        effects.AddRange(NextCapture());
        return effects;
    }

    private IReadOnlyList<WorkflowEffect> OnAlgorithm(AlgorithmFinished input)
    {
        if (!algorithms.TryGetValue(input.Key, out var status) || status != AlgorithmStatus.Waiting ||
            captures[input.Key] != CaptureStatus.FrameReady) return [];
        algorithms[input.Key] = input.Outcome == AlgorithmOutcome.Completed ? AlgorithmStatus.Completed : AlgorithmStatus.Failed;
        if (stage == TrayStage.AwaitingAlgorithms && algorithms.Values.All(s => s != AlgorithmStatus.Waiting))
            stage = TrayStage.AwaitingDecision;
        return [];
    }

    private IReadOnlyList<WorkflowEffect> OnAlgorithmDeadline()
    {
        if (stage is not (TrayStage.AwaitingAlgorithms or TrayStage.Failed or TrayStage.RecoveryRequired)) return [];
        foreach (var key in algorithms.Keys.ToArray())
            if (algorithms[key] == AlgorithmStatus.Waiting) algorithms[key] = AlgorithmStatus.TimedOut;
        if (stage == TrayStage.AwaitingAlgorithms) stage = TrayStage.AwaitingDecision;
        return [];
    }

    private IReadOnlyList<WorkflowEffect> OnTimedOut(RequestTimedOut input)
    {
        if (pendingRequest != input.RequestId) return [];
        if (stage == TrayStage.WaitingMotion)
        {
            stage = TrayStage.RecoveryRequired;
            failure = "Motion timed out; physical result is unknown.";
            return [];
        }
        if (stage == TrayStage.WaitingCapture && pendingCapture is not null)
            return OnCapture(new CaptureFinished(input.RequestId, pendingCapture, false, null));
        if (stage is TrayStage.WaitingInitialScan or TrayStage.WaitingRescan or TrayStage.WaitingTrayCode or TrayStage.WaitingRecipe)
            return Fail("Critical preparation request timed out.");
        return [];
    }

    private IReadOnlyList<WorkflowEffect> Fail(string message)
    {
        stage = TrayStage.Failed;
        failure = message;
        pendingRequest = null;
        return [];
    }
}

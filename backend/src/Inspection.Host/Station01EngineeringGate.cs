using Inspection.Contracts;

/// <summary>Engineering-only in-memory duplicate guard. It is not a production restart ledger.</summary>
public sealed class Station01EngineeringGate
{
    private readonly object gate = new();
    private readonly HashSet<string> usedRunIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Station01RunDto> completed = new(StringComparer.Ordinal);
    private bool active;

    public bool TryStart(string runId)
    {
        lock (gate)
        {
            if (active || !usedRunIds.Add(runId)) return false;
            active = true;
            return true;
        }
    }

    public void Finish(string runId, Station01RunDto? result)
    {
        lock (gate)
        {
            if (result is not null) completed[runId] = result;
            active = false;
        }
    }

    public Station01RunDto? Get(string runId)
    {
        lock (gate) return completed.GetValueOrDefault(runId);
    }
}

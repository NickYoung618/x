using Inspection.Domain.Planning;

namespace Inspection.Application.Engineering;

public sealed record DemoPlanView(OrdinaryTrayPlan Plan)
{
    public bool IsTestFixture => true;
    public bool ExecutionEnabled => false;
}

public sealed class DemoPlanService(IEnumerable<string> partIds, IEnumerable<FaceDefinition> faces)
{
    private readonly DemoPlanView view = new(OrdinaryTrayPlanner.Build(partIds, faces));
    public DemoPlanView GetPlan() => view;
}

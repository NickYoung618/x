using Inspection.Application.Architecture;
using Inspection.Contracts;
using Inspection.Domain.Planning;

namespace Inspection.Host.Tests;

public class ArchitectureBoundaryTests
{
    [Fact]
    public void Core_assemblies_keep_v13_dependency_direction()
    {
        var domainReferences = typeof(OrdinaryTrayPlanner).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        var applicationReferences = typeof(V13Architecture).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        var contractReferences = typeof(SystemStatusDto).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.DoesNotContain("Inspection.Application", domainReferences);
        Assert.DoesNotContain("Inspection.Infrastructure", domainReferences);
        Assert.DoesNotContain("Inspection.Host", domainReferences);
        Assert.DoesNotContain("Inspection.Infrastructure", applicationReferences);
        Assert.DoesNotContain("Inspection.Host", applicationReferences);
        Assert.DoesNotContain("Inspection.Domain", contractReferences);
        Assert.DoesNotContain("Inspection.Application", contractReferences);
        Assert.DoesNotContain("Inspection.Infrastructure", contractReferences);
    }
}

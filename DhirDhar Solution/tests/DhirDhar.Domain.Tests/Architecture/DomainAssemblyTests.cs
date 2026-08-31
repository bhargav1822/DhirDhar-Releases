using DhirDhar.Domain;

namespace DhirDhar.Domain.Tests.Architecture;

public class DomainAssemblyTests
{
    [Fact]
    public void DomainAssembly_CanLoad()
    {
        Assert.NotNull(DomainAssemblyMarker.Assembly);
    }

    [Fact]
    public void Domain_DoesNotReference_Application()
    {
        Assert.False(GetReferencedAssemblyNames().Contains("DhirDhar.Application"));
    }

    [Fact]
    public void Domain_DoesNotReference_Infrastructure()
    {
        Assert.False(GetReferencedAssemblyNames().Contains("DhirDhar.Infrastructure"));
    }

    [Fact]
    public void Domain_DoesNotReference_Desktop()
    {
        Assert.False(GetReferencedAssemblyNames().Contains("DhirDhar.Desktop"));
    }

    private static IReadOnlyCollection<string> GetReferencedAssemblyNames()
    {
        return DomainAssemblyMarker.Assembly
            .GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
    }
}

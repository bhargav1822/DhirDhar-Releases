using DhirDhar.Application;
using DhirDhar.Application.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace DhirDhar.Application.Tests;

public class ApplicationRegistrationTests
{
    [Fact]
    public void ApplicationAssembly_CanLoad()
    {
        Assert.NotNull(ApplicationAssemblyMarker.Assembly);
    }

    [Fact]
    public void Application_DoesNotReference_Infrastructure()
    {
        var names = GetReferencedAssemblyNames();

        Assert.False(names.Contains("DhirDhar.Infrastructure"));
    }

    [Fact]
    public void Application_DoesNotReference_Desktop()
    {
        var names = GetReferencedAssemblyNames();

        Assert.False(names.Contains("DhirDhar.Desktop"));
    }

    [Fact]
    public void AddApplication_CanBeRegistered_AndProviderConstructed()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider);
    }

    private static IReadOnlyCollection<string> GetReferencedAssemblyNames()
    {
        return ApplicationAssemblyMarker.Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
    }
}

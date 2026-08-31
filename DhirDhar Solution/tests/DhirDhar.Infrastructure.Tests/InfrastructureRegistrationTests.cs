using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Abstractions.Persistence.Repositories;
using DhirDhar.Application.Abstractions.Services;
using DhirDhar.Infrastructure;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.DependencyInjection;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Tests;

public class InfrastructureRegistrationTests
{
    [Fact]
    public void InfrastructureAssembly_CanLoad()
    {
        Assert.NotNull(InfrastructureAssemblyMarker.Assembly);
    }

    [Fact]
    public void Infrastructure_DoesNotReference_Desktop()
    {
        var names = InfrastructureAssemblyMarker.Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        Assert.False(names.Contains("DhirDhar.Desktop"));
    }

    [Fact]
    public async Task AddInfrastructure_CanBeRegistered_AndProviderConstructed()
    {
        using var temp = new TempDatabase();
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(temp.CreateDatabaseOptions());

        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IDatabaseInitializer>());
        Assert.NotNull(provider.GetService<IDateTimeService>());
    }

    [Fact]
    public async Task AddInfrastructure_RegistersPersistenceServices()
    {
        using var temp = new TempDatabase();
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(temp.CreateDatabaseOptions());

        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IDatabasePathService>());
        Assert.NotNull(provider.GetService<IDatabaseInitializer>());
        Assert.NotNull(provider.GetService<IDatabaseHealthService>());
        Assert.NotNull(provider.GetService<IUnitOfWork>());
        Assert.NotNull(provider.GetService(typeof(IRepository<TestPersistenceEntity>)));
    }
}

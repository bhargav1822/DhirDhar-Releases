using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Tests.Persistence;

/// <summary>
/// Builds a service provider for infrastructure tests using the manual options overload,
/// with logging disabled.
/// </summary>
public static class TestServiceProvider
{
    public static ServiceProvider Build(DatabaseOptions options)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(options);

        return services.BuildServiceProvider();
    }
}

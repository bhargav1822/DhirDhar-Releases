using Microsoft.Extensions.DependencyInjection;

namespace DhirDhar.Application.DependencyInjection;

/// <summary>
/// Registers application-layer services in the dependency injection container.
/// The application layer depends only on the domain layer.
/// </summary>
public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Application-layer service implementations are provided by the infrastructure layer.
        // This registration point is reserved for future application-layer-only services.
        return services;
    }
}

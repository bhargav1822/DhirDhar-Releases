using System;
using DhirDhar.Application.DependencyInjection;
using DhirDhar.Desktop.DependencyInjection;
using DhirDhar.Desktop.Logging;
using DhirDhar.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DhirDhar.Desktop.Configuration;

/// <summary>
/// Builds and binds the centralized application configuration.
/// </summary>
public static class ConfigurationExtensions
{
    public static IConfigurationRoot BuildConfiguration()
    {
        var basePath = AppContext.BaseDirectory;

        return new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: false)
            .Build();
    }

    public static AppOptions LoadAppOptions(this IConfiguration configuration)
    {
        var options = configuration.GetSection(AppOptions.SectionName).Get<AppOptions>()
            ?? new AppOptions();

        return options;
    }

    public static IServiceCollection AddDesktopServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IConfiguration>(configuration);

        return services
            .AddDesktopLogging(configuration)
            .AddApplication()
            .AddInfrastructure(configuration)
            .AddDesktop();
    }
}

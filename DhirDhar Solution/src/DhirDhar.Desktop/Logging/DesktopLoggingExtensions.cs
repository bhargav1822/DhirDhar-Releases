using System;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.Logging;

/// <summary>
/// Configures the desktop logging pipeline. Minimum levels can differ between
/// Development and Production via configuration, and sensitive financial data
/// must never be logged (see logging guidance in the architecture docs).
/// The log directory is resolved centrally by <see cref="IDatabasePathService"/>.
/// </summary>
public static class DesktopLoggingExtensions
{
    public static IServiceCollection AddDesktopLogging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var minimumLevel = configuration.GetSection("Logging:MinimumLevel").Value ?? "Information";
        if (!Enum.TryParse<LogLevel>(minimumLevel, ignoreCase: true, out var level))
        {
            level = LogLevel.Information;
        }

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(level);
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddDebug();
        });

        services.AddSingleton<ILoggerProvider>(serviceProvider =>
        {
            var pathService = serviceProvider.GetRequiredService<IDatabasePathService>();
            return new FileLoggerProvider(pathService.LogDirectory);
        });

        return services;
    }
}

using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;

namespace DhirDhar.Infrastructure.Persistence;

/// <summary>
/// Centralizes EF Core options construction so the runtime dependency injection registration
/// and the design-time factory apply the same SQLite configuration.
/// </summary>
public static class DbContextOptionsFactory
{
    public static DbContextOptions<DhirDharDbContext> Create(IDatabasePathService pathService, DatabaseOptions options)
    {
        var builder = new DbContextOptionsBuilder<DhirDharDbContext>();
        Apply(builder, pathService, options);
        return builder.Options;
    }

    public static void Apply(DbContextOptionsBuilder builder, IDatabasePathService pathService, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(options);

        var dbPath = pathService.DatabasePath;
        builder.UseSqlite($"Data Source={dbPath};Pooling=False", sqlite =>
        {
            if (options.CommandTimeout is int commandTimeout)
            {
                sqlite.CommandTimeout(commandTimeout);
            }
        });

        if (options.EnableSensitiveDataLogging)
        {
            builder.EnableSensitiveDataLogging();
        }
    }
}

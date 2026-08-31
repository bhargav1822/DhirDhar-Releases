using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Persistence;

/// <summary>
/// Checks the local database: file presence, SQLite connectivity, migration status,
/// schema consistency against the EF Core model, and a basic read.
/// Returns structured health information; raw database exceptions are logged and never
/// exposed to the UI.
/// </summary>
public sealed class DatabaseHealthService : IDatabaseHealthService
{
    private readonly IDatabasePathService _pathService;
    private readonly IDbContextFactory<DhirDharDbContext> _dbContextFactory;
    private readonly ILogger<DatabaseHealthService> _logger;

    public DatabaseHealthService(
        IDatabasePathService pathService,
        IDbContextFactory<DhirDharDbContext> dbContextFactory,
        ILogger<DatabaseHealthService> logger)
    {
        _pathService = pathService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<DatabaseHealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var databasePath = _pathService.DatabasePath;
        var fileExists = File.Exists(databasePath);

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);

            var canRead = false;
            var migrationsAreApplied = false;
            var schemaIsValid = false;
            string? healthError = null;

            if (canConnect)
            {
                canRead = await CanReadAsync(dbContext, cancellationToken).ConfigureAwait(false);

                try
                {
                    var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
                    migrationsAreApplied = pending.Count == 0;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Could not determine migration status during health check.");
                    migrationsAreApplied = false;
                }

                if (canRead && migrationsAreApplied)
                {
                    (schemaIsValid, healthError) = await ValidateSchemaConsistencyAsync(dbContext, cancellationToken).ConfigureAwait(false);
                }
                else if (!migrationsAreApplied)
                {
                    healthError = "Pending database migrations exist.";
                }
                else if (!canRead)
                {
                    healthError = "Database read validation failed.";
                }
            }
            else
            {
                healthError = "Cannot connect to database.";
            }

            var isHealthy = canConnect && migrationsAreApplied && canRead && schemaIsValid;
            var result = new DatabaseHealthResult(
                isHealthy,
                databasePath,
                fileExists,
                canConnect,
                migrationsAreApplied,
                canRead,
                isHealthy ? null : (healthError ?? "Database is not fully healthy."));

            _logger.LogInformation(
                "Database health: healthy={Healthy}, fileExists={FileExists}, canConnect={CanConnect}, migrationsApplied={MigrationsApplied}, canRead={CanRead}, schemaValid={SchemaValid}.",
                result.IsHealthy,
                result.FileExists,
                result.CanConnect,
                result.MigrationsAreApplied,
                result.CanRead,
                schemaIsValid);

            return result;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Database health check failed.");
            return new DatabaseHealthResult(false, databasePath, fileExists, false, false, false, "Database health check failed.");
        }
    }

    private static async Task<(bool IsValid, string? Error)> ValidateSchemaConsistencyAsync(DhirDharDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!reader.IsDBNull(0))
                    {
                        existingTables.Add(reader.GetString(0));
                    }
                }
            }

            // Derive all expected entity tables from the EF Core model
            var requiredTables = dbContext.Model.GetEntityTypes()
                .Select(t => t.GetTableName())
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();

            var missingTables = requiredTables.Where(t => !existingTables.Contains(t)).ToList();
            if (missingTables.Count > 0)
            {
                return (false, $"Required tables missing from database: {string.Join(", ", missingTables)}");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Schema validation failed: {ex.Message}");
        }
    }

    private async Task<bool> CanReadAsync(DhirDharDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(scalar) == 1;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Basic database read failed during health check.");
            return false;
        }
    }
}

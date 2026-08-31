using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DhirDhar.Infrastructure.Tests.Persistence;

/// <summary>
/// Creates an isolated, temporary SQLite database for a single test and cleans it up
/// afterward. Automated tests must never touch the production database.
/// </summary>
public sealed class TempDatabase : IDisposable
{
    public string DirectoryPath { get; } =
        Path.Combine(Path.GetTempPath(), $"dhirdhar-test-{Guid.NewGuid():N}");

    public string FilePath => Path.Combine(DirectoryPath, "test.db");

    public TempDatabase()
    {
        Directory.CreateDirectory(DirectoryPath);
    }

    public DbContextOptions<DhirDharDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<DhirDharDbContext>()
            .UseSqlite($"Data Source={FilePath}")
            .Options;
    }

    public DatabaseOptions CreateDatabaseOptions()
    {
        return new DatabaseOptions
        {
            Provider = "Sqlite",
            DatabasePath = FilePath,
            CommandTimeout = 30,
            EnableSensitiveDataLogging = false
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; a leftover temp folder must not fail a test.
        }
    }
}

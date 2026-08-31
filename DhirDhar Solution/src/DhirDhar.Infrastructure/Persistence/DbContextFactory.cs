using DhirDhar.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;

namespace DhirDhar.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core tooling (for example <c>dotnet ef migrations</c>).
/// It resolves the same database location used at runtime so migrations target the real database.
/// </summary>
public sealed class DbContextFactory : IDesignTimeDbContextFactory<DhirDharDbContext>
{
    public DhirDharDbContext CreateDbContext(string[] args)
    {
        var databaseOptions = new DatabaseOptions();
        var pathService = new DatabasePathService(
            Options.Create(databaseOptions),
            Options.Create(new BackupOptions()));

        return new DhirDharDbContext(DbContextOptionsFactory.Create(pathService, databaseOptions));
    }
}

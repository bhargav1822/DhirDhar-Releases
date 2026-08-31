using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DhirDhar.Infrastructure.Tests;

public class DhirDharDbContextTests
{
    [Fact]
    public void CanCreateContext()
    {
        using var temp = new TempDatabase();
        using var context = new DhirDharDbContext(temp.CreateOptions());

        Assert.NotNull(context);
    }

    [Fact]
    public async Task CanOpenSqliteConnection_AndCreatesDatabaseFile()
    {
        using var temp = new TempDatabase();
        using var context = new DhirDharDbContext(temp.CreateOptions());

        await context.Database.OpenConnectionAsync();

        Assert.True(File.Exists(temp.FilePath));
    }

    [Fact]
    public async Task CanConnect_WhenFileExists_ReturnsTrue()
    {
        using var temp = new TempDatabase();
        using var context = new DhirDharDbContext(temp.CreateOptions());

        await context.Database.OpenConnectionAsync();
        await context.Database.CloseConnectionAsync();

        Assert.True(await context.Database.CanConnectAsync());
    }

    [Fact]
    public async Task CanApplyMigrations()
    {
        using var temp = new TempDatabase();
        using var context = new DhirDharDbContext(temp.CreateOptions());

        await context.Database.MigrateAsync();

        var applied = await context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, migration => migration.EndsWith("InitialCreate"));
    }
}

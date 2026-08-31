using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DhirDhar.Infrastructure.Tests;

public class DatabaseHealthServiceTests
{
    [Fact]
    public async Task CheckAsync_BeforeInitialization_IsNotHealthy()
    {
        using var temp = new TempDatabase();
        using var provider = TestServiceProvider.Build(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var health = scope.ServiceProvider.GetRequiredService<IDatabaseHealthService>();

        var result = await health.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Equal(temp.FilePath, result.DatabasePath);
    }

    [Fact]
    public async Task CheckAsync_AfterInitialization_IsHealthy()
    {
        using var temp = new TempDatabase();
        using var provider = TestServiceProvider.Build(temp.CreateDatabaseOptions());

        using (var scope = provider.CreateScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            Assert.True((await initializer.InitializeAsync()).IsSuccess);
        }

        using (var scope = provider.CreateScope())
        {
            var health = scope.ServiceProvider.GetRequiredService<IDatabaseHealthService>();
            var result = await health.CheckAsync();

            Assert.True(result.IsHealthy);
            Assert.True(result.FileExists);
            Assert.True(result.CanConnect);
            Assert.True(result.MigrationsAreApplied);
            Assert.True(result.CanRead);
        }
    }

    [Fact]
    public async Task CheckAsync_MissingDatabaseDirectory_IsNotHealthy()
    {
        using var temp = new TempDatabase();
        using var provider = TestServiceProvider.Build(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var health = scope.ServiceProvider.GetRequiredService<IDatabaseHealthService>();

        var result = await health.CheckAsync();

        Assert.False(result.IsHealthy);
    }
}

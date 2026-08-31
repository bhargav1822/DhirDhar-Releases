using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DhirDhar.Infrastructure.Tests;

public class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_WithValidOptions_ReturnsSuccess_AndCreatesDatabase()
    {
        using var temp = new TempDatabase();
        using var provider = TestServiceProvider.Build(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();

        var result = await initializer.InitializeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(temp.FilePath, result.DatabasePath);
        Assert.True(Directory.Exists(temp.DirectoryPath));
        Assert.True(File.Exists(temp.FilePath));
    }

    [Fact]
    public async Task InitializeAsync_AppliesMigrations()
    {
        using var temp = new TempDatabase();
        using var provider = TestServiceProvider.Build(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();

        var result = await initializer.InitializeAsync();
        Assert.True(result.IsSuccess);

        await using var verificationContext = new DhirDharDbContext(temp.CreateOptions());
        var applied = await verificationContext.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, migration => migration.EndsWith("InitialCreate"));
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        using var temp = new TempDatabase();
        using var provider = TestServiceProvider.Build(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();

        var first = await initializer.InitializeAsync();
        var second = await initializer.InitializeAsync();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        await using var verificationContext = new DhirDharDbContext(temp.CreateOptions());
        var applied = (await verificationContext.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.NotEmpty(applied);
        Assert.Contains(applied, migration => migration.EndsWith("InitialCreate"));
    }

    [Fact]
    public async Task InitializeAsync_DoesNotRecreateExistingDatabase()
    {
        using var temp = new TempDatabase();
        using var provider = TestServiceProvider.Build(temp.CreateDatabaseOptions());

        using (var scope = provider.CreateScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            Assert.True((await initializer.InitializeAsync()).IsSuccess);
        }

        var sizeBefore = new FileInfo(temp.FilePath).Length;

        using (var scope = provider.CreateScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            Assert.True((await initializer.InitializeAsync()).IsSuccess);
        }

        Assert.Equal(sizeBefore, new FileInfo(temp.FilePath).Length);
    }

    [Fact]
    public async Task InitializeAsync_WithMissingProvider_ReturnsFailure()
    {
        using var temp = new TempDatabase();
        var options = temp.CreateDatabaseOptions();
        options.Provider = string.Empty;

        using var provider = TestServiceProvider.Build(options);
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();

        var result = await initializer.InitializeAsync();

        Assert.True(result.IsFailure);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task InitializeAsync_WithUnsupportedProvider_ReturnsFailure()
    {
        using var temp = new TempDatabase();
        var options = temp.CreateDatabaseOptions();
        options.Provider = "MySql";

        using var provider = TestServiceProvider.Build(options);
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();

        var result = await initializer.InitializeAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task InitializeAsync_WithMissingDatabasePath_ReturnsFailure()
    {
        using var provider = TestServiceProvider.Build(new DatabaseOptions { Provider = "Sqlite", DatabasePath = string.Empty });
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();

        var result = await initializer.InitializeAsync();

        Assert.True(result.IsFailure);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task InitializeAsync_WithExistingAppDataDatabase_SucceedsWithoutError()
    {
        var appDataDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DhirDhar Solution",
            "Data",
            "DhirDhar.db");

        if (File.Exists(appDataDbPath))
        {
            var options = new DatabaseOptions { Provider = "Sqlite", DatabasePath = appDataDbPath };
            using var provider = TestServiceProvider.Build(options);
            using var scope = provider.CreateScope();
            var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();

            var result = await initializer.InitializeAsync();
            Assert.True(result.IsSuccess, $"Failed with error: {result.Error}");
        }
    }
}

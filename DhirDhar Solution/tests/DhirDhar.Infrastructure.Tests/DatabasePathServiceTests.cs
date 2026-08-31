using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace DhirDhar.Infrastructure.Tests;

public class DatabasePathServiceTests
{
    [Fact]
    public void ApplicationDataDirectory_ResolvesUnderLocalAppData()
    {
        var service = CreatePathService();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expected = Path.Combine(localAppData, DatabasePathService.ApplicationFolderName);

        Assert.Equal(expected, service.ApplicationDataDirectory);
    }

    [Fact]
    public void ApplicationDataDirectory_DoesNotContainHardcodedDrive()
    {
        var service = CreatePathService();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, service.ApplicationDataDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatabaseDirectory_IsUnderApplicationDataDirectory()
    {
        var service = CreatePathService();

        Assert.Equal(Path.Combine(service.ApplicationDataDirectory, DatabasePathService.DataFolderName), service.DatabaseDirectory);
    }

    [Fact]
    public void DatabasePath_WithRelativeFileName_IsUnderDataDirectory()
    {
        var service = CreatePathService(new DatabaseOptions { DatabasePath = "DhirDhar.db" });

        Assert.Equal(Path.Combine(service.DatabaseDirectory, "DhirDhar.db"), service.DatabasePath);
    }

    [Fact]
    public void DatabasePath_WithAbsolutePath_UsesGivenPath()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "custom.db");
        var service = CreatePathService(new DatabaseOptions { DatabasePath = absolute });

        Assert.Equal(Path.GetFullPath(absolute), service.DatabasePath);
    }

    [Fact]
    public void DatabasePath_WhenNotConfigured_ResolvesDefaultUnderDataDirectory()
    {
        var service = CreatePathService(new DatabaseOptions { DatabasePath = string.Empty });

        Assert.Equal(Path.Combine(service.DatabaseDirectory, "DhirDhar.db"), service.DatabasePath);
    }

    [Fact]
    public void BackupDirectory_DefaultsUnderApplicationDataDirectory()
    {
        var service = CreatePathService();

        Assert.Equal(Path.Combine(service.ApplicationDataDirectory, DatabasePathService.BackupFolderName), service.BackupDirectory);
    }

    [Fact]
    public void BackupDirectory_UsesConfiguredDirectory()
    {
        var service = CreatePathService(backupDirectory: "MyBackups");

        Assert.Equal(Path.Combine(service.ApplicationDataDirectory, "MyBackups"), service.BackupDirectory);
    }

    [Fact]
    public void LogDirectory_IsUnderApplicationDataDirectory()
    {
        var service = CreatePathService();

        Assert.Equal(Path.Combine(service.ApplicationDataDirectory, DatabasePathService.LogFolderName), service.LogDirectory);
    }

    private static DatabasePathService CreatePathService(DatabaseOptions? databaseOptions = null, string? backupDirectory = null)
    {
        return new DatabasePathService(
            Options.Create(databaseOptions ?? new DatabaseOptions()),
            Options.Create(new BackupOptions { Directory = backupDirectory ?? string.Empty }));
    }
}

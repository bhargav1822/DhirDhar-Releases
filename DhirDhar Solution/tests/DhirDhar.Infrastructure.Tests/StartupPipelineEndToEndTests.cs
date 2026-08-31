using System;
using System.IO;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Licensing;
using DhirDhar.Application.Security.Keys;
using DhirDhar.Application.Settings;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.DependencyInjection;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class StartupPipelineEndToEndTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public StartupPipelineEndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DD_StartupTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "Data", "DhirDhar.db");
    }

    [Fact]
    public async Task FullInfrastructureStartupPipeline_ExecutesSuccessfully_WithoutFailure()
    {
        var options = new DatabaseOptions
        {
            Provider = "Sqlite",
            DatabasePath = _dbPath
        };

        using var provider = TestServiceProvider.Build(options);
        using var scope = provider.CreateScope();

        // 1. Stage: License Manager Initialization (Offline RSA Check)
        var licenseManager = scope.ServiceProvider.GetRequiredService<ILicenseManager>();
        var licenseInitResult = await licenseManager.InitializeAsync();
        Assert.NotNull(licenseInitResult);

        // 2. Stage: Master Key and Encryption Initialization
        var keyService = scope.ServiceProvider.GetRequiredService<IKeyManagementService>();
        await keyService.InitializeMasterKeyAsync();
        Assert.True(keyService.IsMasterKeyInitialized());
        Assert.NotNull(keyService.GetMasterKey());
        Assert.NotNull(keyService.GetFieldEncryptionKey());

        // 3. Stage: Database Initialization & Migrations (88% mark)
        var dbInitializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
        var dbResult = await dbInitializer.InitializeAsync();
        Assert.True(dbResult.IsSuccess, $"Database initialization failed: {dbResult.Error}");
        Assert.True(File.Exists(_dbPath), "Database file should exist.");

        // 4. Stage: Database Health Check (92% mark)
        var healthService = scope.ServiceProvider.GetRequiredService<IDatabaseHealthService>();
        var health = await healthService.CheckAsync();
        Assert.True(health.IsHealthy, $"Database health check failed: {health.Error}");
        Assert.True(health.CanConnect);
        Assert.True(health.MigrationsAreApplied);
        Assert.True(health.CanRead);

        // 5. Stage: Settings Service Startup (95% mark)
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        await settingsService.ApplySettingsOnStartupAsync();
        var settings = await settingsService.GetSettingsAsync();
        Assert.NotNull(settings);

        // 6. Verify DbContext, Migrations & Composite Indices (UserTextTranslations, etc.)
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(appliedMigrations);

        // Add and retrieve a record in UserTextTranslations to verify composite index works without reflection error
        var translation = new UserTextTranslation(
            "Borrower Name",
            "en",
            "gu",
            "ખાતાદારનું નામ");

        dbContext.UserTextTranslations.Add(translation);
        await dbContext.SaveChangesAsync();

        var retrieved = await dbContext.UserTextTranslations
            .FirstOrDefaultAsync(t => t.SourceText == "Borrower Name" && t.TargetLanguage == "gu");
        Assert.NotNull(retrieved);
        Assert.Equal("ખાતાદારનું નામ", retrieved.TranslatedText);

        // 7. Verify Loan, Transaction, Borrower with Money ValueObject mapping
        var borrower = new Borrower("Test Borrower", "9876543210", "Test Address", "Test Notes");
        dbContext.Borrowers.Add(borrower);
        await dbContext.SaveChangesAsync();

        var loan = new Loan(
            borrower.Id,
            Money.Create(15000.50m),
            1.5m,
            DhirDhar.Domain.Enums.InterestFrequency.Monthly,
            DateTime.UtcNow);
        dbContext.Loans.Add(loan);
        await dbContext.SaveChangesAsync();

        var period = new FinancialPeriod("FY 2026-2027", DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow.AddMonths(11));
        dbContext.FinancialPeriods.Add(period);
        await dbContext.SaveChangesAsync();

        var txn = new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(5000m),
            DhirDhar.Domain.Enums.TransactionType.Deposit,
            DateTime.UtcNow,
            "Initial Deposit");
        dbContext.Transactions.Add(txn);
        await dbContext.SaveChangesAsync();

        var retrievedLoan = await dbContext.Loans.FirstOrDefaultAsync(l => l.Id == loan.Id);
        Assert.NotNull(retrievedLoan);
        Assert.Equal(15000.50m, retrievedLoan.Principal.Amount);

        var retrievedTxn = await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == txn.Id);
        Assert.NotNull(retrievedTxn);
        Assert.Equal(5000m, retrievedTxn.Amount.Amount);
    }

    [Fact]
    public async Task ExistingProductionDatabase_InitializesAndPassesHealthChecks()
    {
        var appDataDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DhirDhar Solution",
            "Data",
            "DhirDhar.db");

        if (!File.Exists(appDataDbPath))
        {
            return;
        }

        var options = new DatabaseOptions
        {
            Provider = "Sqlite",
            DatabasePath = appDataDbPath
        };

        using var provider = TestServiceProvider.Build(options);
        using var scope = provider.CreateScope();
        var healthService = scope.ServiceProvider.GetRequiredService<IDatabaseHealthService>();

        var health = await healthService.CheckAsync();
        Assert.True(health.IsHealthy, $"Database health check failed: {health.Error}");
        Assert.True(health.CanConnect);
        Assert.True(health.MigrationsAreApplied);
        Assert.True(health.CanRead);
    }

    [Fact]
    public async Task ReleaseBuildDirectory_ContainsValidIntegrityManifest_PassesIntegrityCheck()
    {
        var releaseBinDir = @"D:\DhirDhar\DhirDhar Solution\src\DhirDhar.Desktop\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64";
        var exePath = Path.Combine(releaseBinDir, "DhirDhar.Desktop.exe");
        var manifestPath = Path.Combine(releaseBinDir, "app_integrity.sig");
        if (!Directory.Exists(releaseBinDir) || !File.Exists(exePath) || !File.Exists(manifestPath))
        {
            return;
        }

        var integrityService = new DhirDhar.Infrastructure.Security.Integrity.ApplicationIntegrityService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DhirDhar.Infrastructure.Security.Integrity.ApplicationIntegrityService>.Instance,
            releaseBinDir);

        var result = await integrityService.VerifyApplicationIntegrityAsync();
        Assert.True(result.IsValid, $"Integrity check on actual release build output failed: {result.StatusMessage}");
        Assert.Empty(result.TamperedFiles);
        Assert.Empty(result.MissingFiles);
        Assert.True(result.TotalFilesScanned >= 15);
        Assert.Equal(DhirDhar.Application.Security.Integrity.IntegrityFailureType.None, result.FailureType);
    }

    [Fact]
    public async Task ReleaseBuild_ApplicationIntegrityStage_CompletesWithZeroFailures()
    {
        var releaseBinDir = @"D:\DhirDhar\DhirDhar Solution\src\DhirDhar.Desktop\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64";
        var exePath = Path.Combine(releaseBinDir, "DhirDhar.Desktop.exe");
        var manifestPath = Path.Combine(releaseBinDir, "app_integrity.sig");
        if (!Directory.Exists(releaseBinDir) || !File.Exists(exePath) || !File.Exists(manifestPath))
        {
            return;
        }

        var integrityService = new DhirDhar.Infrastructure.Security.Integrity.ApplicationIntegrityService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DhirDhar.Infrastructure.Security.Integrity.ApplicationIntegrityService>.Instance,
            releaseBinDir);

        var progressList = new System.Collections.Generic.List<DhirDhar.Application.Security.Integrity.IntegrityScanProgress>();
        var progress = new Progress<DhirDhar.Application.Security.Integrity.IntegrityScanProgress>(p => progressList.Add(p));

        var result = await integrityService.VerifyApplicationIntegrityAsync(progress);

        Assert.True(result.IsValid);
        Assert.Equal(DhirDhar.Application.Security.Integrity.IntegrityFailureType.None, result.FailureType);
        Assert.Contains(progressList, p => p.Category == DhirDhar.Application.Security.Integrity.IntegrityScanCategory.Completed);
    }

    [Fact]
    public async Task DatabaseInitializer_CreatesCanonicalIndexes_IncludingAuditEntriesTimestamp_WithoutOccurredAtError()
    {
        var options = new DatabaseOptions
        {
            Provider = "Sqlite",
            DatabasePath = _dbPath
        };

        using var provider = TestServiceProvider.Build(options);
        using var scope = provider.CreateScope();

        var dbInitializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
        var dbResult = await dbInitializer.InitializeAsync();
        Assert.True(dbResult.IsSuccess);

        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
        var conn = dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND tbl_name = 'AuditEntries';";
        using var reader = await cmd.ExecuteReaderAsync();

        var indexNames = new System.Collections.Generic.List<string>();
        while (await reader.ReadAsync())
        {
            indexNames.Add(reader.GetString(0));
        }

        Assert.Contains(indexNames, idx => idx.Contains("Timestamp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(indexNames, idx => idx.Contains("OccurredAt", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}

using System;
using System.Threading.Tasks;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Settings;
using DhirDhar.Domain.Entities;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Settings;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

[Collection("LocalizationStateSyncTests")]
public sealed class SettingsServiceTests
{
    private static async Task<DhirDharDbContext> CreateDbContextAsync(TempDatabase temp)
    {
        var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.MigrateAsync();
        return context;
    }

    [Fact]
    public async Task GetSettingsAsync_ReturnsDefaultSettings_WhenDatabaseIsEmpty()
    {
        using var temp = new TempDatabase();
        var dbContext = await CreateDbContextAsync(temp);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        var provider = services.BuildServiceProvider();

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var localization = new LocalizationService();
        var dateLocalization = new DateLocalizationService();

        var service = new SettingsService(scopeFactory, localization, dateLocalization, NullLogger<SettingsService>.Instance);

        var settings = await service.GetSettingsAsync();

        Assert.Equal(localization.CurrentLanguage, settings.Language);
        Assert.Equal("DD-MM-YYYY", settings.DateFormat);
        Assert.Equal("INR", settings.Currency);
        Assert.Equal("Default", settings.Theme);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsSettingsToDatabase_AndAppliesToLocalizationServices()
    {
        using var temp = new TempDatabase();
        var dbContext = await CreateDbContextAsync(temp);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        var provider = services.BuildServiceProvider();

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var localization = new LocalizationService();
        var dateLocalization = new DateLocalizationService();

        var service = new SettingsService(scopeFactory, localization, dateLocalization, NullLogger<SettingsService>.Instance);

        var newSettings = new AppSettingsModel
        {
            Language = "gu-IN",
            DateFormat = "YYYY-MM-DD",
            Currency = "INR",
            Theme = "Dark",
            BusinessName = "Dwiti Jewellers"
        };

        await service.SaveSettingsAsync(newSettings);

        var persistedSettings = await service.GetSettingsAsync();
        Assert.Equal("gu-IN", persistedSettings.Language);
        Assert.Equal("YYYY-MM-DD", persistedSettings.DateFormat);
        Assert.Equal("Dark", persistedSettings.Theme);
        Assert.Equal("Dwiti Jewellers", persistedSettings.BusinessName);
        Assert.Equal("DJ", persistedSettings.BorrowerNumberPrefix);

        Assert.Equal("gu-IN", localization.CurrentLanguage);
        Assert.Equal("yyyy-MM-dd", dateLocalization.DateFormatPattern);
    }

    [Fact]
    public async Task ResetSettingsAsync_RestoresDefaultsInDatabase()
    {
        using var temp = new TempDatabase();
        var dbContext = await CreateDbContextAsync(temp);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        var provider = services.BuildServiceProvider();

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var localization = new LocalizationService();
        var dateLocalization = new DateLocalizationService();

        var service = new SettingsService(scopeFactory, localization, dateLocalization, NullLogger<SettingsService>.Instance);

        await service.SaveSettingsAsync(new AppSettingsModel { Language = "hi-IN", DateFormat = "DD/MM/YYYY", Theme = "Light" });
        await service.ResetSettingsAsync();

        var settings = await service.GetSettingsAsync();
        Assert.Equal(localization.CurrentLanguage, settings.Language);
        Assert.Equal("DD-MM-YYYY", settings.DateFormat);
        Assert.Equal("Default", settings.Theme);
    }

    [Fact]
    public async Task SaveSettingsAsync_WithNullProperties_DoesNotThrowSQLiteNotNullConstraint_AndPersistsSafely()
    {
        using var temp = new TempDatabase();
        var dbContext = await CreateDbContextAsync(temp);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        var provider = services.BuildServiceProvider();

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var localization = new LocalizationService();
        var dateLocalization = new DateLocalizationService();

        var service = new SettingsService(scopeFactory, localization, dateLocalization, NullLogger<SettingsService>.Instance);

        // AppSettingsModel with completely null fields
        var modelWithNulls = new AppSettingsModel
        {
            Language = null!,
            DateFormat = null!,
            Currency = null!,
            Theme = null!,
            BackupFrequency = null!,
            LastAutomaticBackupTime = null,
            NextScheduledBackupTime = null,
            BusinessName = null!
        };

        // Must succeed without throwing SQLite Error 19 'NOT NULL constraint failed: ApplicationSettings.Value'
        await service.SaveSettingsAsync(modelWithNulls);

        // Verify that database has valid non-null values for all settings
        var allSettings = await dbContext.ApplicationSettings.ToListAsync();
        Assert.NotEmpty(allSettings);
        foreach (var setting in allSettings)
        {
            Assert.NotNull(setting.Key);
            Assert.NotNull(setting.Value);
        }

        // Verify retrieval
        var retrieved = await service.GetSettingsAsync();
        Assert.NotNull(retrieved.Language);
        Assert.NotNull(retrieved.DateFormat);
        Assert.NotNull(retrieved.Currency);
        Assert.NotNull(retrieved.Theme);
        Assert.NotNull(retrieved.BackupFrequency);
        Assert.NotNull(retrieved.BusinessName);
    }

    [Fact]
    public async Task SaveSettingsAsync_RepeatedUpdates_DoesNotCreateDuplicatesOrFail()
    {
        using var temp = new TempDatabase();
        var dbContext = await CreateDbContextAsync(temp);

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        var provider = services.BuildServiceProvider();

        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var localization = new LocalizationService();
        var dateLocalization = new DateLocalizationService();

        var service = new SettingsService(scopeFactory, localization, dateLocalization, NullLogger<SettingsService>.Instance);

        for (int i = 0; i < 5; i++)
        {
            var settings = await service.GetSettingsAsync();
            settings.Language = i % 2 == 0 ? "hi-IN" : "gu-IN";
            settings.Theme = i % 2 == 0 ? "Dark" : "Light";
            await service.SaveSettingsAsync(settings);
        }

        var finalSettings = await service.GetSettingsAsync();
        Assert.Equal("hi-IN", finalSettings.Language);
        Assert.Equal("Dark", finalSettings.Theme);

        var allSettings = await dbContext.ApplicationSettings.ToListAsync();
        foreach (var setting in allSettings)
        {
            Assert.NotNull(setting.Value);
        }
    }
}

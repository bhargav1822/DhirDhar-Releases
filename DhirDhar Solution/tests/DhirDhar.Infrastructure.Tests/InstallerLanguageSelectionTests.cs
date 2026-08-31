using System;
using System.IO;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Settings;
using DhirDhar.Infrastructure.Configuration;
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
public sealed class InstallerLanguageSelectionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public InstallerLanguageSelectionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DD_InstallerLangTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "DhirDhar.db");
        LanguageConfigurationReader.CustomBaseDirectory = _tempDir;
    }

    public void Dispose()
    {
        LanguageConfigurationReader.CustomBaseDirectory = null;
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task CleanInstall_StartsInEnglish_DefaultLanguage()
    {
        var options = new DatabaseOptions { Provider = "Sqlite", DatabasePath = _dbPath };
        using var provider = TestServiceProvider.Build(options);

        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var locService = provider.GetRequiredService<ILocalizationService>();
        var dateLocService = provider.GetRequiredService<IDateLocalizationService>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var settingsService = new SettingsService(scopeFactory, locService, dateLocService, NullLogger<SettingsService>.Instance);
        await settingsService.ApplySettingsOnStartupAsync();

        Assert.Equal("en-IN", locService.CurrentLanguage);
        Assert.Equal("Dashboard", locService.GetString("Dashboard"));
        Assert.Equal("Borrowers", locService.GetString("Borrowers"));

        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var saved = await db.ApplicationSettings.FirstOrDefaultAsync(s => s.Key == SettingsService.LanguageKey);
            Assert.NotNull(saved);
            Assert.Equal("en-IN", saved.Value);
        }
    }

    [Fact]
    public async Task SettingsLanguageChange_ToGujarati_PersistsAndLoadsOnRestart()
    {
        var options = new DatabaseOptions { Provider = "Sqlite", DatabasePath = _dbPath };
        using var provider = TestServiceProvider.Build(options);

        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var locService = provider.GetRequiredService<ILocalizationService>();
        var dateLocService = provider.GetRequiredService<IDateLocalizationService>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var settingsService = new SettingsService(scopeFactory, locService, dateLocService, NullLogger<SettingsService>.Instance);
        await settingsService.ApplySettingsOnStartupAsync();
        Assert.Equal("en-IN", locService.CurrentLanguage);

        // 1. User changes to Gujarati in Settings
        var currentSettings = await settingsService.GetSettingsAsync();
        currentSettings.Language = "gu-IN";
        await settingsService.SaveSettingsAsync(currentSettings);

        Assert.Equal("gu-IN", locService.CurrentLanguage);
        Assert.Equal("ડૅશબોર્ડ", locService.GetString("Dashboard"));
        Assert.Equal("ખાતાધારકો", locService.GetString("Borrowers"));

        // Verify Gujarati transliteration
        var transliteratedPalak = OfflineGujaratiTransliteration.Transliterate("palak");
        Assert.Equal("પલક", transliteratedPalak);

        // 2. Simulate application restart
        var restartedLocService = new LocalizationService();
        var restartedDateLocService = new DateLocalizationService();
        var restartedSettingsService = new SettingsService(scopeFactory, restartedLocService, restartedDateLocService, NullLogger<SettingsService>.Instance);

        await restartedSettingsService.ApplySettingsOnStartupAsync();

        Assert.Equal("gu-IN", restartedLocService.CurrentLanguage);
        Assert.Equal("gu-IN", restartedSettingsService.LanguageSettings.CurrentLanguage);
        Assert.Equal("gu-IN", restartedSettingsService.LanguageSettings.SavedApplicationLanguage);
    }

    [Fact]
    public async Task SettingsLanguageChange_ToHindi_PersistsAndLoadsOnRestart()
    {
        var options = new DatabaseOptions { Provider = "Sqlite", DatabasePath = _dbPath };
        using var provider = TestServiceProvider.Build(options);

        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var locService = provider.GetRequiredService<ILocalizationService>();
        var dateLocService = provider.GetRequiredService<IDateLocalizationService>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var settingsService = new SettingsService(scopeFactory, locService, dateLocService, NullLogger<SettingsService>.Instance);
        await settingsService.ApplySettingsOnStartupAsync();

        // 1. User changes to Hindi in Settings
        var currentSettings = await settingsService.GetSettingsAsync();
        currentSettings.Language = "hi-IN";
        await settingsService.SaveSettingsAsync(currentSettings);

        Assert.Equal("hi-IN", locService.CurrentLanguage);
        Assert.Equal("डैशबोर्ड", locService.GetString("Dashboard"));
        Assert.Equal("खाताधारक", locService.GetString("Borrowers"));

        // Script translation to Hindi
        var hindiText = ScriptTranslator.ToHindi("palak");
        Assert.Equal("पलक", hindiText);

        // 2. Simulate application restart
        var restartedLocService = new LocalizationService();
        var restartedDateLocService = new DateLocalizationService();
        var restartedSettingsService = new SettingsService(scopeFactory, restartedLocService, restartedDateLocService, NullLogger<SettingsService>.Instance);

        await restartedSettingsService.ApplySettingsOnStartupAsync();

        Assert.Equal("hi-IN", restartedLocService.CurrentLanguage);
        Assert.Equal("hi-IN", restartedSettingsService.LanguageSettings.CurrentLanguage);
        Assert.Equal("hi-IN", restartedSettingsService.LanguageSettings.SavedApplicationLanguage);
    }

    [Fact]
    public async Task ResetSettings_RestoresEnglishDefault()
    {
        var options = new DatabaseOptions { Provider = "Sqlite", DatabasePath = _dbPath };
        using var provider = TestServiceProvider.Build(options);

        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var locService = provider.GetRequiredService<ILocalizationService>();
        var dateLocService = provider.GetRequiredService<IDateLocalizationService>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var settingsService = new SettingsService(scopeFactory, locService, dateLocService, NullLogger<SettingsService>.Instance);
        await settingsService.ApplySettingsOnStartupAsync();

        // Switch to Gujarati
        var currentSettings = await settingsService.GetSettingsAsync();
        currentSettings.Language = "gu-IN";
        await settingsService.SaveSettingsAsync(currentSettings);
        Assert.Equal("gu-IN", locService.CurrentLanguage);

        // Reset settings
        await settingsService.ResetSettingsAsync();

        // Language restores to default English
        Assert.Equal("en-IN", locService.CurrentLanguage);
        var loadedSettings = await settingsService.GetSettingsAsync();
        Assert.Equal("en-IN", loadedSettings.Language);
    }
}

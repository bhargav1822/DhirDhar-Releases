using System;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Settings;
using DhirDhar.Domain.Common;
using DhirDhar.Domain.Entities;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Settings;

public sealed class SettingsService : ISettingsService
{
    public const string LanguageKey = "General.Language";
    public const string DateFormatKey = "General.DateFormat";
    public const string CurrencyKey = "General.Currency";
    public const string ThemeKey = "Appearance.Theme";
    public const string AutoCheckKey = "Updates.AutoCheckEnabled";
    public const string AutoInstallKey = "Updates.AutoInstallEnabled";
    public const string AutoBackupEnabledKey = "Backup.AutomaticBackupEnabled";
    public const string BackupFrequencyKey = "Backup.BackupFrequency";
    public const string RetentionCountKey = "Backup.RetentionCount";
    public const string LastAutoBackupTimeKey = "Backup.LastAutomaticBackupTime";
    public const string NextAutoBackupTimeKey = "Backup.NextScheduledBackupTime";
    public const string BusinessNameKey = "Business.Name";
    public const string PaperSizeKey = "Printing.PaperSize";
    public const string CustomPaperWidthMmKey = "Printing.CustomPaperWidthMm";
    public const string AutoCutPaperKey = "Printing.AutoCutPaper";
    public const string SelectedPrinterKey = "Printing.SelectedPrinter";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILocalizationService _localizationService;
    private readonly IDateLocalizationService _dateLocalizationService;
    private readonly ILogger<SettingsService> _logger;
    private ApplicationLanguageSettings _languageSettings;

    public SettingsService(
        IServiceScopeFactory scopeFactory,
        ILocalizationService localizationService,
        IDateLocalizationService dateLocalizationService,
        ILogger<SettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _localizationService = localizationService;
        _dateLocalizationService = dateLocalizationService;
        _logger = logger;

        _languageSettings = new ApplicationLanguageSettings
        {
            CurrentLanguage = _localizationService.CurrentLanguage ?? LanguageConfigurationReader.SafeFallbackLanguage,
            InstallerLanguage = null,
            SavedApplicationLanguage = null,
            IsLanguageInitialized = false
        };
    }

    public ApplicationLanguageSettings LanguageSettings => _languageSettings;

    public event EventHandler<AppSettingsModel>? SettingsChanged;

    public async Task<AppSettingsModel> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var langSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == LanguageKey, cancellationToken).ConfigureAwait(false);
            var dateSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == DateFormatKey, cancellationToken).ConfigureAwait(false);
            var currSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == CurrencyKey, cancellationToken).ConfigureAwait(false);
            var themeSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == ThemeKey, cancellationToken).ConfigureAwait(false);
            var autoCheckSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == AutoCheckKey, cancellationToken).ConfigureAwait(false);
            var autoInstallSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == AutoInstallKey, cancellationToken).ConfigureAwait(false);
            var autoBackupSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == AutoBackupEnabledKey, cancellationToken).ConfigureAwait(false);
            var freqSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == BackupFrequencyKey, cancellationToken).ConfigureAwait(false);
            var retentionSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == RetentionCountKey, cancellationToken).ConfigureAwait(false);
            var lastBackupSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == LastAutoBackupTimeKey, cancellationToken).ConfigureAwait(false);
            var nextBackupSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == NextAutoBackupTimeKey, cancellationToken).ConfigureAwait(false);
            var businessNameSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == BusinessNameKey, cancellationToken).ConfigureAwait(false);
            var paperSizeSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == PaperSizeKey, cancellationToken).ConfigureAwait(false);
            var customWidthSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == CustomPaperWidthMmKey, cancellationToken).ConfigureAwait(false);
            var autoCutSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == AutoCutPaperKey, cancellationToken).ConfigureAwait(false);
            var printerSetting = await dbContext.ApplicationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SelectedPrinterKey, cancellationToken).ConfigureAwait(false);

            string resolvedLanguage;
            if (!string.IsNullOrWhiteSpace(langSetting?.Value))
            {
                resolvedLanguage = LocalizationService.NormalizeLanguageCode(langSetting.Value);
            }
            else
            {
                resolvedLanguage = LanguageConfigurationReader.SafeFallbackLanguage;
            }

            return new AppSettingsModel
            {
                Language = resolvedLanguage,
                DateFormat = string.IsNullOrWhiteSpace(dateSetting?.Value) ? "DD-MM-YYYY" : dateSetting.Value.Trim(),
                Currency = string.IsNullOrWhiteSpace(currSetting?.Value) ? "INR" : currSetting.Value.Trim(),
                Theme = string.IsNullOrWhiteSpace(themeSetting?.Value) ? "Default" : themeSetting.Value.Trim(),
                UpdatesAutoCheckEnabled = ParseBool(autoCheckSetting?.Value, true),
                UpdatesAutoInstallEnabled = ParseBool(autoInstallSetting?.Value, true),
                AutomaticBackupEnabled = ParseBool(autoBackupSetting?.Value, true),
                BackupFrequency = string.IsNullOrWhiteSpace(freqSetting?.Value) ? "Daily" : freqSetting.Value.Trim(),
                RetentionCount = Math.Max(1, int.TryParse(retentionSetting?.Value, out int r) ? r : 7),
                LastAutomaticBackupTime = DateTime.TryParse(lastBackupSetting?.Value, out DateTime l) ? l : null,
                NextScheduledBackupTime = DateTime.TryParse(nextBackupSetting?.Value, out DateTime n) ? n : null,
                BusinessName = string.IsNullOrWhiteSpace(businessNameSetting?.Value) ? BusinessProfileHelper.DefaultBusinessName : businessNameSetting.Value.Trim(),
                PaperSize = string.IsNullOrWhiteSpace(paperSizeSetting?.Value) ? "A4" : paperSizeSetting.Value.Trim(),
                CustomPaperWidthMm = double.TryParse(customWidthSetting?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cw) && cw > 0 ? cw : 80.0,
                AutoCutPaper = ParseBool(autoCutSetting?.Value, true),
                SelectedPrinter = string.IsNullOrWhiteSpace(printerSetting?.Value) ? null : printerSetting.Value.Trim(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load application settings from database. Using safe defaults.");
            return new AppSettingsModel();
        }
    }

    public async Task SaveSettingsAsync(AppSettingsModel settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var safeLanguage = string.IsNullOrWhiteSpace(settings.Language)
                ? LanguageConfigurationReader.SafeFallbackLanguage
                : LocalizationService.NormalizeLanguageCode(settings.Language);

            var safeDateFormat = string.IsNullOrWhiteSpace(settings.DateFormat)
                ? "DD-MM-YYYY"
                : settings.DateFormat.Trim();

            var safeCurrency = string.IsNullOrWhiteSpace(settings.Currency)
                ? "INR"
                : settings.Currency.Trim();

            var safeTheme = string.IsNullOrWhiteSpace(settings.Theme)
                ? "Default"
                : settings.Theme.Trim();

            var safeAutoCheck = settings.UpdatesAutoCheckEnabled ? "true" : "false";
            var safeAutoInstall = settings.UpdatesAutoInstallEnabled ? "true" : "false";
            var safeAutoBackup = settings.AutomaticBackupEnabled ? "true" : "false";
            var safeBackupFrequency = string.IsNullOrWhiteSpace(settings.BackupFrequency) ? "Daily" : settings.BackupFrequency.Trim();
            var safeRetentionCount = Math.Max(1, settings.RetentionCount).ToString();
            var safeLastBackupTime = settings.LastAutomaticBackupTime?.ToString("o") ?? string.Empty;
            var safeNextBackupTime = settings.NextScheduledBackupTime?.ToString("o") ?? string.Empty;
            var safeBusinessName = string.IsNullOrWhiteSpace(settings.BusinessName) ? BusinessProfileHelper.DefaultBusinessName : settings.BusinessName.Trim();
            var safePaperSize = string.IsNullOrWhiteSpace(settings.PaperSize) ? "A4" : settings.PaperSize.Trim();
            var safeCustomWidth = settings.CustomPaperWidthMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            var safeAutoCut = settings.AutoCutPaper ? "true" : "false";
            var safePrinter = settings.SelectedPrinter ?? string.Empty;

            await SaveOrUpdateSettingAsync(dbContext, LanguageKey, safeLanguage, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, DateFormatKey, safeDateFormat, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, CurrencyKey, safeCurrency, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, ThemeKey, safeTheme, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, AutoCheckKey, safeAutoCheck, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, AutoInstallKey, safeAutoInstall, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, AutoBackupEnabledKey, safeAutoBackup, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, BackupFrequencyKey, safeBackupFrequency, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, RetentionCountKey, safeRetentionCount, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, LastAutoBackupTimeKey, safeLastBackupTime, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, NextAutoBackupTimeKey, safeNextBackupTime, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, BusinessNameKey, safeBusinessName, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, PaperSizeKey, safePaperSize, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, CustomPaperWidthMmKey, safeCustomWidth, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, AutoCutPaperKey, safeAutoCut, cancellationToken).ConfigureAwait(false);
            await SaveOrUpdateSettingAsync(dbContext, SelectedPrinterKey, safePrinter, cancellationToken).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Also persist to language.json for consistent installer/launcher state
            try
            {
                LanguageConfigurationReader.WriteInstallerLanguage(safeLanguage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write language configuration file.");
            }

            _languageSettings = new ApplicationLanguageSettings
            {
                CurrentLanguage = safeLanguage,
                InstallerLanguage = null,
                SavedApplicationLanguage = safeLanguage,
                IsLanguageInitialized = true
            };

            ApplySettingsInternal(settings);
            SettingsChanged?.Invoke(this, settings);

            _logger.LogInformation("Application settings persisted to database successfully (Language={Language}).", safeLanguage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save application settings.");
            throw;
        }
    }

    public async Task ResetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var defaultLang = LanguageConfigurationReader.SafeFallbackLanguage;

        var defaults = new AppSettingsModel
        {
            Language = defaultLang
        };
        await SaveSettingsAsync(defaults, cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplySettingsOnStartupAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

        var langSetting = await dbContext.ApplicationSettings
            .FirstOrDefaultAsync(s => s.Key == LanguageKey, cancellationToken)
            .ConfigureAwait(false);

        string currentLanguage;
        string? savedLanguage = null;
        string? installerLanguage = null;

        if (langSetting != null && !string.IsNullOrWhiteSpace(langSetting.Value))
        {
            // Priority 1: Explicit DhirDhar user language setting from database
            savedLanguage = LocalizationService.NormalizeLanguageCode(langSetting.Value);
            currentLanguage = savedLanguage;
        }
        else
        {
            // Priority 2: Installer language from language.json if available
            var installerLang = LanguageConfigurationReader.ReadInstallerLanguage();
            if (!string.IsNullOrWhiteSpace(installerLang))
            {
                installerLanguage = LocalizationService.NormalizeLanguageCode(installerLang);
                currentLanguage = installerLanguage;
            }
            else
            {
                // Priority 3: Safe default English
                currentLanguage = LanguageConfigurationReader.SafeFallbackLanguage;
            }

            await SaveOrUpdateSettingAsync(dbContext, LanguageKey, currentLanguage, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _localizationService.SetLanguage(currentLanguage);

        _languageSettings = new ApplicationLanguageSettings
        {
            CurrentLanguage = currentLanguage,
            InstallerLanguage = installerLanguage,
            SavedApplicationLanguage = savedLanguage,
            IsLanguageInitialized = true
        };

        var settings = await GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        settings.Language = currentLanguage;
        ApplySettingsInternal(settings);
    }

    private void ApplySettingsInternal(AppSettingsModel settings)
    {
        try
        {
            _localizationService.SetLanguage(settings.Language);
            _dateLocalizationService.SetDateFormatPattern(settings.DateFormat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying settings to services.");
        }
    }

    private async Task SaveOrUpdateSettingAsync(DhirDharDbContext dbContext, string key, string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        var safeValue = value ?? string.Empty;

        var setting = await dbContext.ApplicationSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (setting is null)
        {
            _logger.LogDebug("Adding new ApplicationSetting: {Key} = '{Value}'", key, safeValue);
            dbContext.ApplicationSettings.Add(new ApplicationSetting(key, safeValue));
        }
        else
        {
            if (setting.Value != safeValue)
            {
                _logger.LogDebug("Updating ApplicationSetting: {Key} = '{Value}' (was '{OldValue}')", key, safeValue, setting.Value);
                setting.UpdateValue(safeValue);
            }
        }
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }
}

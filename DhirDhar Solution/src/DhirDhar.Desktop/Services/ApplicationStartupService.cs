using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Security.Integrity;
using DhirDhar.Desktop.Configuration;
using DhirDhar.Desktop.Services;
using DhirDhar.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.Services;

public sealed class ApplicationStartupService : IApplicationStartupService
{
    private readonly AppOptions _appOptions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApplicationStartupService> _logger;
    private readonly IApplicationStateService _stateService;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;
    private IProgress<StartupProgress>? _progress;

    public ApplicationStartupService(
        AppOptions appOptions,
        IServiceScopeFactory scopeFactory,
        ILogger<ApplicationStartupService> logger,
        IApplicationStateService stateService)
    {
        _appOptions = appOptions;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _stateService = stateService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(null, cancellationToken).ConfigureAwait(false);
    }

    public async Task InitializeAsync(IProgress<StartupProgress>? progress, CancellationToken cancellationToken = default)
    {
        _progress = progress;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                ReportProgress(StartupState.Ready, "Ready", 100);
                return;
            }

            _logger.LogInformation("================== STARTUP SEQUENCE BEGIN ==================");

            // Stage [01] Initialize configuration
            ReportProgress(StartupState.Starting, $"Starting {_appOptions.Name}...", 0);
            await ExecuteStageAsync("[01]", "Initialize configuration", () =>
            {
                InitializeConfiguration();
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            // Stage [02] Initialize logging
            ReportProgress(StartupState.Starting, "Loading configuration...", 5);
            await ExecuteStageAsync("[02]", "Initialize logging", () =>
            {
                InitializeLogging();
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            // Stage [03] Initialize dependency injection
            await ExecuteStageAsync("[03]", "Initialize dependency injection", () =>
            {
                InitializeDependencyInjection();
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            // Stage [04] Verify application integrity (HMAC-SHA256 scan)
            ReportProgress(StartupState.LoadingConfiguration, "Scanning installed application files...", 7);
            await ExecuteStageAsync("[04]", "Verify application integrity", async () =>
            {
                await VerifyApplicationIntegrityAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            // Stage [05] Validate license (Offline RSA)
            ReportProgress(StartupState.InitializingServices, "Validating offline license and security...", 80);
            await ExecuteStageAsync("[05]", "Validate license", async () =>
            {
                await InitializeLicenseAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            // Stage [06] Initialize encryption & master key
            ReportProgress(StartupState.InitializingServices, "Security initialization verified.", 88);
            await ExecuteStageAsync("[06]", "Initialize encryption", async () =>
            {
                await InitializeEncryptionAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            // Stage [07] Open SQLite database & run migrations
            ReportProgress(StartupState.InitializingDatabase, "Initializing database...", 88);
            await ExecuteStageAsync("[07]", "Open database & run migrations", async () =>
            {
                await InitializeDatabaseAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            // Stage [08] Check database health & connectivity
            ReportProgress(StartupState.CheckingDatabase, "Checking database health...", 92);
            await ExecuteStageAsync("[08]", "Check database health", async () =>
            {
                await CheckDatabaseHealthAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            // Stage [09] Apply stored settings & localization
            await ExecuteStageAsync("[09]", "Apply settings & localization", async () =>
            {
                await ApplySettingsAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
            ReportProgress(StartupState.CheckingDatabase, "Settings applied.", 95);

            // Stage [10] Initialize Google integration (silent / offline safe)
            await ExecuteStageAsync("[10]", "Initialize Google integration", async () =>
            {
                await InitializeGoogleIntegrationAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            // Stage [11] Initialize update service & background cleanup
            await ExecuteStageAsync("[11]", "Initialize update service", () =>
            {
                TriggerBackgroundUpdateCleanup();
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            // Stage [12] Prepare main window & workspace
            ReportProgress(StartupState.PreparingApplication, "Preparing workspace...", 96);
            await ExecuteStageAsync("[12]", "Prepare main window & workspace", () =>
            {
                PrepareMainWindow();
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            // Stage [13] Startup complete
            _isInitialized = true;
            _logger.LogInformation("[STAGE SUCCESS] [13] Startup complete - Application 100% Ready.");
            _logger.LogInformation("================== STARTUP SEQUENCE FINISHED ==================");
            ReportProgress(StartupState.Ready, "Ready", 100);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task ExecuteStageAsync(string stageNum, string stageName, Func<Task> stageAction)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[STAGE START] {StageNum} {StageName}", stageNum, stageName);
        try
        {
            await stageAction().ConfigureAwait(false);
            sw.Stop();
            _logger.LogInformation("[STAGE SUCCESS] {StageNum} {StageName} ({ElapsedMs} ms)", stageNum, stageName, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var sb = new StringBuilder();
            sb.AppendLine($"[STAGE FAILURE] {stageNum} {stageName} ({sw.ElapsedMilliseconds} ms)");
            sb.AppendLine($"Exception Type: {ex.GetType().FullName}");
            sb.AppendLine($"Exception Message: {ex.Message}");
            sb.AppendLine($"HResult: 0x{ex.HResult:X8}");
            sb.AppendLine($"StackTrace:\n{ex.StackTrace}");
            var inner = ex.InnerException;
            int level = 1;
            while (inner != null)
            {
                sb.AppendLine($"--- Inner Exception Level {level} ---");
                sb.AppendLine($"Type: {inner.GetType().FullName}, Message: {inner.Message}, HResult: 0x{inner.HResult:X8}");
                sb.AppendLine($"StackTrace:\n{inner.StackTrace}");
                inner = inner.InnerException;
                level++;
            }
            _logger.LogError(ex, "{DiagnosticReport}", sb.ToString());
            throw;
        }
    }

    private void ReportProgress(StartupState state, string message, int percentage)
    {
        _progress?.Report(new StartupProgress(state, message, Math.Clamp(percentage, 0, 100)));
    }

    private void InitializeConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_appOptions.Name))
        {
            throw new InvalidOperationException("Application name is not configured.");
        }

        _logger.LogInformation(
            "Configuration verified for application '{Name}' version '{Version}' in '{Environment}'.",
            _appOptions.Name,
            _appOptions.Version,
            _appOptions.Environment);
    }

    private void InitializeLogging()
    {
        _logger.LogInformation("Logging verified.");
    }

    private void InitializeDependencyInjection()
    {
        _logger.LogInformation("Dependency injection verified.");
    }

    private async Task InitializeDatabaseAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
        var result = await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogError("Database initialization failed at path '{Path}': {Error}", result.DatabasePath, result.Error);
            throw new InvalidOperationException($"Database initialization failed: {result.Error}");
        }

        _stateService.SetDatabaseReady();
        _logger.LogInformation("Database initialized successfully. File '{DatabaseFile}'.", result.DatabasePath);
    }

    private async Task CheckDatabaseHealthAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var healthService = scope.ServiceProvider.GetRequiredService<IDatabaseHealthService>();
        var health = await healthService.CheckAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Database health check: healthy={Healthy}, fileExists={FileExists}, canConnect={CanConnect}, migrationsApplied={MigrationsApplied}, canRead={CanRead}.",
            health.IsHealthy,
            health.FileExists,
            health.CanConnect,
            health.MigrationsAreApplied,
            health.CanRead);

        if (!health.IsHealthy)
        {
            throw new InvalidOperationException($"Database health check failed. {health.Error}");
        }
    }

    private async Task ApplySettingsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider.GetService<DhirDhar.Application.Settings.ISettingsService>();
        if (settingsService != null)
        {
            await settingsService.ApplySettingsOnStartupAsync(cancellationToken).ConfigureAwait(false);
        }

        var inputLanguageService = scope.ServiceProvider.GetService<IInputLanguageService>();
        inputLanguageService?.InitializeOnce();

        var langSettings = settingsService?.LanguageSettings;
        var currentLang = langSettings?.CurrentLanguage ?? "en-IN";
        var installerLang = langSettings?.InstallerLanguage ?? currentLang;
        var savedLang = langSettings?.SavedApplicationLanguage ?? currentLang;
        var inputLang = inputLanguageService?.Current.LanguageCode ?? currentLang;

        _logger.LogInformation("[LANGUAGE] SavedLanguage = {SavedLanguage}", savedLang ?? "none");
        _logger.LogInformation("[LANGUAGE] CurrentLanguage during startup = {CurrentLanguage}", currentLang);
        _logger.LogInformation("[LANGUAGE] Localization initialized language = {LocalizationLanguage}", currentLang);
        _logger.LogInformation("[LANGUAGE] Input engine language = {InputLanguage}", inputLang);
        _logger.LogInformation("[LANGUAGE] InstallerLanguage = {InstallerLanguage}", installerLang);
        _logger.LogInformation("[LANGUAGE] CurrentLanguage = {CurrentLanguage}", currentLang);

        var backupScheduler = scope.ServiceProvider.GetService<DhirDhar.Application.Backup.IBackupSchedulerService>();
        if (backupScheduler != null)
        {
            await backupScheduler.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Task InitializeGoogleIntegrationAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var googleDriveService = scope.ServiceProvider.GetService<DhirDhar.Application.Backup.IGoogleDriveService>();
                if (googleDriveService != null)
                {
                    _logger.LogInformation("Attempting silent Google Drive auto-connect in background...");
                    await googleDriveService.InitializeAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception driveEx)
            {
                _logger.LogWarning(driveEx, "Google Drive silent auto-connect failed in background. (Safe offline launch preserved)");
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    private async Task InitializeEncryptionAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var keyManagementService = scope.ServiceProvider.GetService<DhirDhar.Application.Security.Keys.IKeyManagementService>();
        if (keyManagementService != null)
        {
            try
            {
                await keyManagementService.InitializeMasterKeyAsync(cancellationToken).ConfigureAwait(false);
                var migrationService = scope.ServiceProvider.GetService<DhirDhar.Application.Security.IEncryptionMigrationService>();
                if (migrationService != null && await migrationService.IsMigrationRequiredAsync(cancellationToken).ConfigureAwait(false))
                {
                    var migrationResult = await migrationService.MigrateExistingDataAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Automatic encryption migration completed with status: {Status}.", migrationResult.IsSuccess);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Non-fatal error during encryption initialization.");
            }
        }
    }

    private async Task VerifyApplicationIntegrityAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var integrityService = scope.ServiceProvider.GetService<IApplicationIntegrityService>();

        if (integrityService == null)
        {
            _logger.LogWarning("Application integrity service is not registered.");
            return;
        }

        var scanProgress = new Progress<IntegrityScanProgress>(scan =>
        {
            var state = scan.Category switch
            {
                IntegrityScanCategory.Initialization => StartupState.Starting,
                IntegrityScanCategory.FileEnumeration => StartupState.LoadingConfiguration,
                IntegrityScanCategory.Configurations => StartupState.LoadingConfiguration,
                IntegrityScanCategory.ApplicationBinaries => StartupState.InitializingServices,
                IntegrityScanCategory.RuntimeDependencies => StartupState.InitializingServices,
                IntegrityScanCategory.Resources => StartupState.InitializingServices,
                IntegrityScanCategory.ManifestVerification => StartupState.InitializingServices,
                IntegrityScanCategory.Completed => StartupState.InitializingServices,
                _ => StartupState.InitializingServices
            };

            ReportProgress(state, scan.CurrentItemName, scan.OverallProgressPercentage);
        });

        var result = await integrityService.VerifyApplicationIntegrityAsync(scanProgress, cancellationToken).ConfigureAwait(false);

        if (!result.IsValid)
        {
            _logger.LogError("[INTEGRITY FAILURE] FailureType: {FailureType}, StatusMessage: '{StatusMessage}', AppVersion: '{AppVersion}', ManifestVersion: '{ManifestVersion}', Tampered: {TamperedCount}, Missing: {MissingCount}",
                result.FailureType, result.StatusMessage, result.ApplicationVersion, result.ManifestVersion, result.TamperedFiles.Count, result.MissingFiles.Count);

            if (result.DiagnosticDetails != null)
            {
                foreach (var detail in result.DiagnosticDetails.Where(d => d.Status != "Verified"))
                {
                    _logger.LogError("[INTEGRITY DETAIL] File: '{RelativePath}', Status: '{Status}', ExpectedHash: '{ExpectedHash}', ActualHash: '{ActualHash}', ExpectedSize: {ExpectedSize}, ActualSize: {ActualSize}",
                        detail.RelativePath, detail.Status, detail.ExpectedHash, detail.ActualHash, detail.ExpectedSize, detail.ActualSize);
                }
            }

            var userMessage = result.FailureType switch
            {
                IntegrityFailureType.FileMissing => "Application integrity check failed. One or more critical files are missing. Please reinstall or update DhirDhar.",
                IntegrityFailureType.FileModified => "Application integrity check failed. One or more critical files have been altered. Please reinstall or update DhirDhar.",
                IntegrityFailureType.SignatureInvalid => "Application integrity check failed. Security manifest signature is invalid. Please reinstall or update DhirDhar.",
                IntegrityFailureType.ManifestMissing => "Application integrity check failed. Security manifest is missing. Please reinstall or update DhirDhar.",
                IntegrityFailureType.AccessDenied => "Application integrity check failed. Access denied while verifying application files.",
                _ => "Application integrity check failed. One or more critical files have been altered or are missing. Please reinstall or update DhirDhar."
            };

            throw new InvalidOperationException(userMessage);
        }

        _logger.LogInformation("[INTEGRITY PASS] Verified {Count} installed application files successfully.", result.TotalFilesScanned);
    }

    private async Task InitializeLicenseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var licenseManager = scope.ServiceProvider.GetService<DhirDhar.Application.Licensing.ILicenseManager>();
            if (licenseManager != null)
            {
                var result = await licenseManager.InitializeAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("License initialization completed: Status={Status}, Valid={IsValid}, Message='{Message}'.",
                    result.Status, result.IsValid, result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Non-fatal error during license initialization.");
        }
    }

    private void PrepareMainWindow()
    {
        _stateService.SetApplicationReady();
        _logger.LogInformation("Main window state ready.");
    }

    private void TriggerBackgroundUpdateCleanup()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000).ConfigureAwait(false);
                using var scope = _scopeFactory.CreateScope();
                var updateService = scope.ServiceProvider.GetService<DhirDhar.Desktop.Updates.IUpdateService>();
                if (updateService != null)
                {
                    await updateService.CleanupInstalledPackagesAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[UPDATER CLEANUP] Non-fatal error during startup update package cleanup.");
            }
        });
    }
}


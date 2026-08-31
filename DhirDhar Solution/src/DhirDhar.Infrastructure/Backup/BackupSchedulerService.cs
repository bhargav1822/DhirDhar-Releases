using System;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Settings;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Backup;

public sealed class BackupSchedulerService : IBackupSchedulerService, IPausableDatabaseWorker, IDisposable
{
    private readonly IBackupService _backupService;
    private readonly IGoogleDriveService _googleDriveService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<BackupSchedulerService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private Timer? _timer;
    private volatile bool _isPaused;
    private bool _disposed;

    public string WorkerName => "BackupSchedulerService";

    public event EventHandler? ScheduledBackupCompleted;

    public BackupSchedulerService(
        IBackupService backupService,
        IGoogleDriveService googleDriveService,
        ISettingsService settingsService,
        ILogger<BackupSchedulerService> logger,
        IDatabaseLifecycleService? lifecycleService = null)
    {
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _googleDriveService = googleDriveService ?? throw new ArgumentNullException(nameof(googleDriveService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        lifecycleService?.RegisterPausableWorker(this);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _timer?.Dispose();
        _isPaused = false;
        _timer = new Timer(OnTimerCallback, null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
        _logger.LogInformation("BackupSchedulerService started successfully.");
        return Task.CompletedTask;
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        _isPaused = true;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("[SCHEDULER] BackupSchedulerService paused. Waiting for any active backup execution to finish...");
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        _semaphore.Release();
        _logger.LogInformation("[SCHEDULER] BackupSchedulerService is fully idle.");
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        _isPaused = false;
        _timer?.Change(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
        _logger.LogInformation("[SCHEDULER] BackupSchedulerService resumed.");
        return Task.CompletedTask;
    }

    private void OnTimerCallback(object? state)
    {
        if (_isPaused) return;
        _ = TriggerBackupCheckAsync();
    }

    public async Task TriggerBackupCheckAsync(CancellationToken cancellationToken = default)
    {
        if (_isPaused) return;
        if (!await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return; // Backup already in progress
        }

        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTime.UtcNow;
            if (settings.NextScheduledBackupTime.HasValue && now < settings.NextScheduledBackupTime.Value)
            {
                return; // Not due yet
            }

            _logger.LogInformation("Automatic backup is due. Starting background backup execution...");

            var localMetadata = await _backupService.CreateBackupAsync(null, cancellationToken).ConfigureAwait(false);

            // Cloud backup upload if connected
            if (_googleDriveService.IsConnected)
            {
                try
                {
                    await _googleDriveService.UploadBackupAsync(localMetadata.Location, null, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Automatic backup uploaded to Google Drive successfully.");
                }
                catch (Exception cloudEx)
                {
                    _logger.LogWarning(cloudEx, "Automatic backup created locally, but Google Drive upload failed.");
                }
            }

            // Automatic cleanup of local backups
            try
            {
                await _backupService.CleanupOldBackupsAsync(1, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception localCleanEx)
            {
                _logger.LogWarning(localCleanEx, "Automatic cleanup of local backups encountered an error.");
            }

            // Automatic cleanup of Google Drive backups if connected
            if (_googleDriveService.IsConnected)
            {
                try
                {
                    await _googleDriveService.CleanupOldCloudBackupsAsync(1, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception cloudCleanEx)
                {
                    _logger.LogWarning(cloudCleanEx, "Automatic cleanup of Google Drive backups encountered an error.");
                }
            }

            settings.AutomaticBackupEnabled = true;
            settings.LastAutomaticBackupTime = now;
            settings.NextScheduledBackupTime = CalculateNextBackupTime(now, settings.BackupFrequency);

            await _settingsService.SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Automatic backup completed successfully. Next scheduled run: {NextRun}", settings.NextScheduledBackupTime);

            ScheduledBackupCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic backup execution failed.");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public static DateTime CalculateNextBackupTime(DateTime fromTime, string frequency)
    {
        return frequency?.Trim().ToLowerInvariant() switch
        {
            "weekly" => fromTime.AddDays(7),
            "monthly" => fromTime.AddMonths(1),
            _ => fromTime.AddDays(1)
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        _semaphore.Dispose();
    }
}

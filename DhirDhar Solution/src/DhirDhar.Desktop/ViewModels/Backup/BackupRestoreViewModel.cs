using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Backup.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Settings;
using DhirDhar.Desktop.ViewModels;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.ViewModels.Backup;

public sealed record BackupFrequencyOption(string Value, string Label);

public sealed class BackupRestoreViewModel : ViewModelBase, IDisposable
{
    private readonly IBackupService _backupService = null!;
    private readonly IGoogleDriveService _googleDriveService = null!;
    private readonly ILocalizationService _localizationService = null!;
    private readonly ILogger<BackupRestoreViewModel> _logger = null!;
    private readonly ISettingsService? _settingsService;
    private readonly IBackupSchedulerService? _schedulerService;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly SemaphoreSlim _historyLock = new(1, 1);
    private CancellationTokenSource? _historyCts;
    private bool _disposed;

    private readonly EventHandler _languageChangedHandler;
    private readonly EventHandler _driveStateChangedHandler;
    private readonly EventHandler<int> _driveProgressHandler;
    private readonly EventHandler<int> _driveDownloadProgressHandler;
    private readonly EventHandler _scheduledBackupHandler;

    private string _backupDirectory = string.Empty;
    private bool _automaticBackupEnabled = true;
    private string _backupFrequency = "Daily";
    private int _retentionCount = 1;
    private DateTime? _lastAutomaticBackupTime;
    private DateTime? _nextScheduledBackupTime;
    private bool _isBackupRunning;
    private bool _isRestoreRunning;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private ObservableCollection<BackupHistoryEntry> _backupHistory = new();

    public BackupRestoreViewModel(
        IBackupService backupService,
        IGoogleDriveService googleDriveService,
        ILocalizationService localizationService,
        ILogger<BackupRestoreViewModel> logger,
        ISettingsService? settingsService = null,
        IBackupSchedulerService? schedulerService = null)
    {
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _googleDriveService = googleDriveService ?? throw new ArgumentNullException(nameof(googleDriveService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService ?? App.ServiceProvider?.GetService(typeof(ISettingsService)) as ISettingsService;
        _schedulerService = schedulerService ?? App.ServiceProvider?.GetService(typeof(IBackupSchedulerService)) as IBackupSchedulerService;

        _languageChangedHandler = (s, e) => OnPropertyChanged(string.Empty);
        _driveStateChangedHandler = (s, e) => RunOnUiThread(async () =>
        {
            UpdateDriveProperties();
            await LoadBackupHistoryAsync();
        });
        _driveProgressHandler = (s, percent) => RunOnUiThread(() =>
        {
            OnPropertyChanged(nameof(IsGoogleDriveUploading));
            OnPropertyChanged(nameof(GoogleDriveUploadProgressPercent));
            OnPropertyChanged(nameof(FormattedUploadProgress));
        });
        _driveDownloadProgressHandler = (s, percent) => RunOnUiThread(() =>
        {
            if (IsRestoreRunning && percent > 0)
            {
                StatusMessage = $"Downloading Google Backup from Google Drive... ({percent}%)";
            }
        });
        _scheduledBackupHandler = (s, e) => RunOnUiThread(async () =>
        {
            await LoadBackupHistoryAsync();
            await LoadSettingsAsync();
        });

        if (_localizationService != null)
        {
            _localizationService.LanguageChanged += _languageChangedHandler;
        }

        if (_googleDriveService != null)
        {
            _googleDriveService.ConnectionStateChanged += _driveStateChangedHandler;
            _googleDriveService.UploadProgressChanged += _driveProgressHandler;
            _googleDriveService.DownloadProgressChanged += _driveDownloadProgressHandler;
        }

        if (_schedulerService != null)
        {
            _schedulerService.ScheduledBackupCompleted += _scheduledBackupHandler;
        }

        BackupNowCommand = new RelayCommand(async () => await BackupNowAsync(), () => !IsBackupRunning && !IsRestoreRunning);
        RestoreCommand = new RelayCommand(async () => await RestoreAsync(), () => !IsBackupRunning && !IsRestoreRunning);
        RestoreSpecificBackupCommand = new RelayCommand<BackupHistoryEntry>(async (entry) => await RestoreSpecificBackupAsync(entry), (entry) => !IsBackupRunning && !IsRestoreRunning);
        RefreshCommand = new RelayCommand(async () => await LoadBackupHistoryAsync(), () => !IsBackupRunning && !IsRestoreRunning);
        ConnectGoogleDriveCommand = new RelayCommand(async () => await ConnectGoogleDriveAsync(), () => !IsGoogleDriveConnecting && !IsBackupRunning && !IsRestoreRunning);
        DisconnectGoogleDriveCommand = new RelayCommand(async () => await DisconnectGoogleDriveAsync(), () => !IsGoogleDriveConnecting && !IsGoogleDriveUploading && !IsBackupRunning && !IsRestoreRunning);
        BackupToCloudCommand = new RelayCommand(async () => await BackupToCloudAsync(), () => GoogleDriveConnected && !IsGoogleDriveUploading && !IsBackupRunning && !IsRestoreRunning);
        RestoreFromCloudCommand = new RelayCommand(async () => await RestoreFromCloudAsync(), () => GoogleDriveConnected && !IsRestoreRunning && !IsBackupRunning);
    }

    public string BackupDirectory
    {
        get => _backupDirectory;
        set => SetProperty(ref _backupDirectory, value);
    }

    public bool AutomaticBackupEnabled
    {
        get => true;
        set
        {
            _automaticBackupEnabled = true;
            OnPropertyChanged(nameof(AutomaticBackupEnabled));
            OnPropertyChanged(nameof(NextScheduledBackupText));
            _ = SaveAutomaticBackupSettingsAsync();
        }
    }

    public string BackupFrequency
    {
        get => _backupFrequency;
        set
        {
            if (SetProperty(ref _backupFrequency, value))
            {
                OnPropertyChanged(nameof(NextScheduledBackupText));
                _ = SaveAutomaticBackupSettingsAsync();
            }
        }
    }

    public int RetentionCount
    {
        get => _retentionCount;
        set => SetProperty(ref _retentionCount, 1);
    }

    public bool GoogleDriveConnected => _googleDriveService?.IsConnected ?? false;
    public bool IsGoogleDriveConnecting => _googleDriveService?.IsConnecting ?? false;
    public bool IsGoogleDriveUploading => _googleDriveService?.IsUploading ?? false;
    public int GoogleDriveUploadProgressPercent => _googleDriveService?.UploadProgressPercent ?? 0;
    public string FormattedUploadProgress => $"{GoogleDriveUploadProgressPercent}%";
    public string? GoogleDriveEmail => _googleDriveService?.ConnectedEmail;
    public string? GoogleDriveLastBackupTime => _googleDriveService?.LastBackupTime;
    public string? GoogleDriveLastBackupStatus => _googleDriveService?.LastBackupStatus;
    public string? GoogleDriveErrorMessage => _googleDriveService?.ErrorMessage;

    public bool IsBackupRunning
    {
        get => _isBackupRunning;
        private set
        {
            if (SetProperty(ref _isBackupRunning, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public bool IsRestoreRunning
    {
        get => _isRestoreRunning;
        private set
        {
            if (SetProperty(ref _isRestoreRunning, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string PageTitle => _localizationService.GetString("BackupRestore");
    public string PageSubtitle => _localizationService.GetString("BackupSubtitle");
    public string RefreshText => _localizationService.GetString("Refresh");
    public string RetryLabel => _localizationService.GetString("Retry");
    public string LocalBackupLabel => _localizationService.GetString("LocalBackup");
    public string BackupNowLabel => _localizationService.GetString("BackupNow");
    public string RestoreLabel => _localizationService.GetString("Restore");
    public string GoogleDriveLabel => "Google Drive";
    public string StatusPrefixLabel => _localizationService.GetString("StatusPrefix");
    public string ConnectLabel => _localizationService.GetString("Connect");
    public string DisconnectLabel => _localizationService.GetString("Disconnect");
    public string AutomaticBackupLabel => _localizationService.GetString("AutomaticBackup");
    public string FrequencyLabel => _localizationService.GetString("Frequency");
    public string RetentionLabel => _localizationService.GetString("Retention");
    public string CleanupLabel => _localizationService.GetString("Cleanup");
    public string BackupHistoryLabel => _localizationService.GetString("BackupHistory");
    public string DateColumnLabel => _localizationService.GetString("Date");
    public string TypeColumnLabel => _localizationService.GetString("Type");
    public string LocationColumnLabel => _localizationService.GetString("Location");
    public string SizeColumnLabel => _localizationService.GetString("Size");
    public string StatusColumnLabel => _localizationService.GetString("Status");

    public ObservableCollection<BackupFrequencyOption> BackupFrequencyOptions => new()
    {
        new("Daily", _localizationService.GetString("Daily")),
        new("Weekly", _localizationService.GetString("Weekly")),
        new("Manual Only", _localizationService.GetString("ManualOnly"))
    };

    public bool HasHistory => _backupHistory != null && _backupHistory.Count > 0;

    public string ActionsColumnLabel => _localizationService.GetString("Actions");
    public string NoBackupsTitle => _localizationService.GetString("NoBackupsAvailable");
    public string NoBackupsSubtitle => _localizationService.GetString("NoBackupsSubtitle");

    public string ConnectingToGoogleDriveLabel => _localizationService.GetString("ConnectingToGoogleDrive");
    public string ConnectedStatusLabel => _localizationService.GetString("ConnectedStatus");
    public string NotConnectedStatusLabel => _localizationService.GetString("NotConnectedStatus");
    public string LastBackupLabel => _localizationService.GetString("LastBackup");
    public string StatusLabel => _localizationService.GetString("Status");
    public string UploadingBackupPackageLabel => _localizationService.GetString("UploadingBackupPackage");
    public string BackupToGoogleDriveLabel => _localizationService.GetString("BackupToGoogleDrive");
    public string RestoreFromGoogleDriveLabel => _localizationService.GetString("RestoreFromGoogleDrive");
    public string AlwaysOnLabel => _localizationService.GetString("AlwaysOn");
    public string LastAutomaticBackupLabel => _localizationService.GetString("LastAutomaticBackup");
    public string NextScheduledBackupLabel => _localizationService.GetString("NextScheduledBackup");

    public string LastAutomaticBackupText => _backupHistory.FirstOrDefault()?.BackupDate.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt")
        ?? _lastAutomaticBackupTime?.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt")
        ?? "Never";

    public string NextScheduledBackupText => _nextScheduledBackupTime?.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt")
        ?? (BackupFrequency switch
        {
            "Weekly" => DateTime.Now.AddDays(7).ToString("dd MMM yyyy, hh:mm tt"),
            "Monthly" => DateTime.Now.AddMonths(1).ToString("dd MMM yyyy, hh:mm tt"),
            _ => DateTime.Now.AddDays(1).ToString("dd MMM yyyy, hh:mm tt")
        });

    public ObservableCollection<BackupHistoryEntry> BackupHistory
    {
        get => _backupHistory;
        private set
        {
            if (SetProperty(ref _backupHistory, value))
            {
                OnPropertyChanged(nameof(HasHistory));
                OnPropertyChanged(nameof(LastAutomaticBackupText));
            }
        }
    }

    public RelayCommand BackupNowCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand<BackupHistoryEntry> RestoreSpecificBackupCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ConnectGoogleDriveCommand { get; }
    public RelayCommand DisconnectGoogleDriveCommand { get; }
    public RelayCommand BackupToCloudCommand { get; }
    public RelayCommand RestoreFromCloudCommand { get; }

    public Func<string, Task<bool>>? ConfirmRestoreCallback { get; set; }
    public Func<Task<string?>>? PickBackupFileCallback { get; set; }
    public Func<string, Task<string?>>? PromptPasswordOrRecoveryKeyCallback { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settingsTask = LoadSettingsAsync(cancellationToken);
            var historyTask = LoadBackupHistoryAsync(cancellationToken);
            await Task.WhenAll(settingsTask, historyTask).ConfigureAwait(false);

            if (_googleDriveService != null && !_googleDriveService.IsConnected && !_googleDriveService.IsConnecting)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        bool connected = await _googleDriveService.InitializeAsync().ConfigureAwait(false);
                        if (connected)
                        {
                            await LoadBackupHistoryAsync().ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background Google Drive silent initialization error.");
                    }
                    finally
                    {
                        RunOnUiThread(UpdateDriveProperties);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Backup & Restore page.");
            SetPageError(ex);
        }
    }

    private async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_settingsService == null) return;
        try
        {
            var settings = await _settingsService.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            _automaticBackupEnabled = settings.AutomaticBackupEnabled;
            _backupFrequency = settings.BackupFrequency;
            _retentionCount = 1;
            _lastAutomaticBackupTime = settings.LastAutomaticBackupTime;
            _nextScheduledBackupTime = settings.NextScheduledBackupTime;

            RunOnUiThread(() =>
            {
                OnPropertyChanged(nameof(AutomaticBackupEnabled));
                OnPropertyChanged(nameof(BackupFrequency));
                OnPropertyChanged(nameof(RetentionCount));
                OnPropertyChanged(nameof(LastAutomaticBackupText));
                OnPropertyChanged(nameof(NextScheduledBackupText));
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load automatic backup settings.");
        }
    }

    private async Task SaveAutomaticBackupSettingsAsync()
    {
        if (_settingsService == null) return;
        try
        {
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            settings.AutomaticBackupEnabled = true;
            settings.BackupFrequency = BackupFrequency;
            settings.RetentionCount = 1;
            settings.NextScheduledBackupTime = DhirDhar.Infrastructure.Backup.BackupSchedulerService.CalculateNextBackupTime(DateTime.UtcNow, BackupFrequency);

            await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(false);

            _lastAutomaticBackupTime = settings.LastAutomaticBackupTime;
            _nextScheduledBackupTime = settings.NextScheduledBackupTime;

            RunOnUiThread(() =>
            {
                OnPropertyChanged(nameof(LastAutomaticBackupText));
                OnPropertyChanged(nameof(NextScheduledBackupText));
            });

            if (_schedulerService != null)
            {
                await _schedulerService.TriggerBackupCheckAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save automatic backup settings.");
        }
    }

    public void SetPageError(Exception exception)
    {
        _logger.LogError(exception, "Backup & Restore page error reported.");
        RunOnUiThread(() =>
        {
            HasError = true;
            ErrorMessage = exception?.Message ?? "Backup & Restore operation failed.";
        });
    }

    public async Task LoadBackupHistoryAsync(CancellationToken cancellationToken = default)
    {
        _historyCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _historyCts = cts;

        try
        {
            await _historyLock.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var localEntries = await _backupService.GetBackupHistoryAsync(cts.Token).ConfigureAwait(false);
            var localBackup = localEntries.FirstOrDefault();

            BackupHistoryEntry? activeBackup = null;

            if (_googleDriveService != null && _googleDriveService.IsConnected)
            {
                var cloudEntries = await _googleDriveService.ListCloudBackupsAsync(cts.Token).ConfigureAwait(false);
                var cloudBackup = cloudEntries.FirstOrDefault();

                if (cloudBackup != null && string.Equals(cloudBackup.Status, "Successful", StringComparison.OrdinalIgnoreCase))
                {
                    // Google Backup has priority when Google Drive is linked and backup is valid
                    activeBackup = new BackupHistoryEntry(
                        "DhirDhar_Google_Backup.ddbackup",
                        cloudBackup.BackupDate,
                        "Google Backup",
                        "Google Drive",
                        cloudBackup.Size,
                        "Successful",
                        "Verified");
                }
                else if (localBackup != null)
                {
                    // Fallback to valid Local Backup if cloud backup is not ready or failed
                    activeBackup = new BackupHistoryEntry(
                        "DhirDhar_Local_Backup.ddbackup",
                        localBackup.BackupDate,
                        "Local Backup",
                        "Local",
                        localBackup.Size,
                        "Successful",
                        "Verified");
                }
            }
            else
            {
                // Google Drive is NOT linked: show ONLY Local Backup
                if (localBackup != null)
                {
                    activeBackup = new BackupHistoryEntry(
                        "DhirDhar_Local_Backup.ddbackup",
                        localBackup.BackupDate,
                        "Local Backup",
                        "Local",
                        localBackup.Size,
                        "Successful",
                        "Verified");
                }
            }

            RunOnUiThread(() =>
            {
                // Check if current history already matches activeBackup to avoid unnecessary UI changes
                if (activeBackup == null)
                {
                    if (_backupHistory.Count > 0)
                    {
                        _backupHistory.Clear();
                        OnPropertyChanged(nameof(HasHistory));
                        OnPropertyChanged(nameof(LastAutomaticBackupText));
                    }
                }
                else
                {
                    if (_backupHistory.Count == 1 &&
                        _backupHistory[0].BackupId == activeBackup.BackupId &&
                        _backupHistory[0].BackupDate == activeBackup.BackupDate &&
                        _backupHistory[0].Type == activeBackup.Type &&
                        _backupHistory[0].Location == activeBackup.Location &&
                        _backupHistory[0].Size == activeBackup.Size &&
                        _backupHistory[0].Status == activeBackup.Status)
                    {
                        // Perfectly identical - do not touch collection!
                        return;
                    }

                    _backupHistory.Clear();
                    _backupHistory.Add(activeBackup);
                    OnPropertyChanged(nameof(HasHistory));
                    OnPropertyChanged(nameof(LastAutomaticBackupText));
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load backup history.");
            RunOnUiThread(() =>
            {
                HasError = true;
                ErrorMessage = _localizationService.GetString("BackupHistoryLoadFailed");
            });
        }
        finally
        {
            _historyLock.Release();
        }
    }

    public async Task ConnectGoogleDriveAsync()
    {
        if (!await _operationLock.WaitAsync(0).ConfigureAwait(false)) return;

        try
        {
            RunOnUiThread(() =>
            {
                StatusMessage = _localizationService.GetString("ConnectingToGoogleDrive");
                ErrorMessage = string.Empty;
                HasError = false;
            });

            bool success = await _googleDriveService.ConnectAsync().ConfigureAwait(false);
            if (success)
            {
                // Trigger initial Google Backup if none exists
                var cloudBackups = await _googleDriveService.ListCloudBackupsAsync().ConfigureAwait(false);
                if (cloudBackups.Count == 0)
                {
                    RunOnUiThread(() => StatusMessage = _localizationService.GetString("InitialGoogleBackupRunning"));
                    try
                    {
                        var localMetadata = await _backupService.CreateBackupAsync().ConfigureAwait(false);
                        await _googleDriveService.UploadBackupAsync(localMetadata.Location).ConfigureAwait(false);
                        RunOnUiThread(() => StatusMessage = _localizationService.GetString("GoogleDriveConnectedBackupSuccess"));
                    }
                    catch (Exception initialBackupEx)
                    {
                        _logger.LogWarning(initialBackupEx, "Initial cloud backup attempt failed.");
                        RunOnUiThread(() => StatusMessage = _localizationService.GetString("GoogleDriveConnectedBackupFailed"));
                    }
                }
                else
                {
                    RunOnUiThread(() => StatusMessage = _localizationService.GetString("GoogleDriveConnectedSuccess"));
                }

                await LoadBackupHistoryAsync().ConfigureAwait(false);
            }
            else
            {
                RunOnUiThread(() =>
                {
                    if (_googleDriveService.State == GoogleDriveOAuthState.AuthorizationCancelled)
                    {
                        StatusMessage = _localizationService.GetString("GoogleDriveAuthCancelled");
                    }
                    else if (_googleDriveService.State == GoogleDriveOAuthState.Offline)
                    {
                        StatusMessage = _localizationService.GetString("GoogleDriveOffline");
                    }
                    else if (_googleDriveService.State == GoogleDriveOAuthState.ReauthRequired)
                    {
                        StatusMessage = _localizationService.GetString("GoogleDriveReauthRequired");
                    }
                    else if (!string.IsNullOrEmpty(_googleDriveService.ErrorMessage))
                    {
                        StatusMessage = _googleDriveService.ErrorMessage;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect Google Drive.");
            RunOnUiThread(() => StatusMessage = $"{_localizationService.GetString("GoogleDrive")}: {ex.Message}");
        }
        finally
        {
            _operationLock.Release();
            RunOnUiThread(UpdateDriveProperties);
        }
    }

    public async Task DisconnectGoogleDriveAsync()
    {
        if (!await _operationLock.WaitAsync(0).ConfigureAwait(false)) return;

        try
        {
            await _googleDriveService.DisconnectAsync().ConfigureAwait(false);

            // Automatically use Local Backup upon disconnection
            var localHistory = await _backupService.GetBackupHistoryAsync().ConfigureAwait(false);
            if (localHistory.Count == 0)
            {
                try
                {
                    await _backupService.CreateBackupAsync().ConfigureAwait(false);
                }
                catch (Exception createEx)
                {
                    _logger.LogWarning(createEx, "Failed to create fallback local backup on Google Drive disconnect.");
                }
            }

            await LoadBackupHistoryAsync().ConfigureAwait(false);
            RunOnUiThread(() => StatusMessage = _localizationService.GetString("GoogleDriveDisconnectedSwitchedLocal"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disconnect Google Drive.");
        }
        finally
        {
            _operationLock.Release();
            RunOnUiThread(UpdateDriveProperties);
        }
    }

    private async Task BackupNowAsync()
    {
        if (!await _operationLock.WaitAsync(0).ConfigureAwait(false)) return;

        RunOnUiThread(() =>
        {
            IsBackupRunning = true;
            HasError = false;
            StatusMessage = _localizationService.GetString("CreatingBackup");
        });

        try
        {
            var localMetadata = await _backupService.CreateBackupAsync().ConfigureAwait(false);

            if (_googleDriveService.IsConnected)
            {
                try
                {
                    RunOnUiThread(() => StatusMessage = _localizationService.GetString("UploadingGoogleBackup"));
                    var googleMetadata = await _backupService.CreateGoogleBackupAsync(_googleDriveService.ConnectedEmail).ConfigureAwait(false);
                    await _googleDriveService.UploadBackupAsync(googleMetadata.Location).ConfigureAwait(false);
                    RunOnUiThread(() => StatusMessage = _localizationService.GetString("GoogleBackupCreatedAndUploaded"));
                }
                catch (Exception cloudEx)
                {
                    _logger.LogWarning(cloudEx, "Google Backup upload failed. Falling back to Local Backup.");
                    RunOnUiThread(() => StatusMessage = _localizationService.GetString("LocalBackupCreatedGoogleFailed"));
                }
            }
            else
            {
                RunOnUiThread(() => StatusMessage = _localizationService.GetString("LocalBackupCreatedSuccess"));
            }

            await LoadBackupHistoryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup creation failed.");
            RunOnUiThread(() =>
            {
                HasError = true;
                ErrorMessage = _localizationService.GetString("BackupFailed");
                StatusMessage = _localizationService.GetString("BackupCreationFailed");
            });
        }
        finally
        {
            RunOnUiThread(() =>
            {
                IsBackupRunning = false;
                UpdateDriveProperties();
            });
            _operationLock.Release();
        }
    }

    private async Task RestoreAsync()
    {
        if (!await _operationLock.WaitAsync(0).ConfigureAwait(false)) return;

        try
        {
            string? targetPath = null;

            if (PickBackupFileCallback != null)
            {
                try
                {
                    targetPath = await PickBackupFileCallback().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PickBackupFileCallback failed.");
                }
            }

            // If no custom file was picked, resolve active backup automatically
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                var activeEntry = _backupHistory.FirstOrDefault();
                if (activeEntry == null)
                {
                    RunOnUiThread(() =>
                    {
                        HasError = true;
                        ErrorMessage = _localizationService.GetString("NoActiveBackupFound");
                        StatusMessage = _localizationService.GetString("NoBackupAvailableToRestore");
                    });
                    return;
                }

                if (string.Equals(activeEntry.Type, "Google Backup", StringComparison.OrdinalIgnoreCase) && _googleDriveService.IsConnected)
                {
                    if (ConfirmRestoreCallback != null)
                    {
                        bool confirmed = await ConfirmRestoreCallback(_localizationService.GetString("ConfirmRestoreGooglePrompt")).ConfigureAwait(false);
                        if (!confirmed)
                        {
                            RunOnUiThread(() => StatusMessage = _localizationService.GetString("RestoreCancelled"));
                            return;
                        }
                    }

                    RunOnUiThread(() =>
                    {
                        IsRestoreRunning = true;
                        HasError = false;
                        StatusMessage = _localizationService.GetString("DownloadingGoogleBackup");
                    });

                    await PerformRestoreInternalAsync("DhirDhar_Google_Backup.ddbackup", isCloud: true, cloudFileId: "DhirDhar_Google_Backup.ddbackup").ConfigureAwait(false);
                    return;
                }
                else
                {
                    targetPath = "DhirDhar_Local_Backup.ddbackup";
                }
            }

            if (ConfirmRestoreCallback != null)
            {
                var fileName = Path.GetFileName(targetPath);
                bool confirmed = await ConfirmRestoreCallback($"{_localizationService.GetString("ConfirmRestoreLocalPromptPrefix")} '{fileName}' {_localizationService.GetString("ConfirmRestorePromptSuffix")}").ConfigureAwait(false);
                if (!confirmed)
                {
                    RunOnUiThread(() => StatusMessage = _localizationService.GetString("RestoreCancelled"));
                    return;
                }
            }

            RunOnUiThread(() =>
            {
                IsRestoreRunning = true;
                HasError = false;
                StatusMessage = _localizationService.GetString("RestoringBackup");
            });

            await PerformRestoreInternalAsync(targetPath, isCloud: false, cloudFileId: string.Empty).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed.");
            var friendlyMsg = FormatRestoreErrorMessage(ex);
            RunOnUiThread(() =>
            {
                HasError = true;
                ErrorMessage = friendlyMsg;
                StatusMessage = friendlyMsg;
            });
        }
        finally
        {
            RunOnUiThread(() =>
            {
                IsRestoreRunning = false;
                UpdateDriveProperties();
            });
            _operationLock.Release();
        }
    }

    public async Task RestoreSpecificBackupAsync(BackupHistoryEntry? entry)
    {
        if (entry == null) return;
        if (!await _operationLock.WaitAsync(0).ConfigureAwait(false)) return;

        try
        {
            bool isGoogleBackup = string.Equals(entry.Type, "Google Backup", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(entry.Location, "Google Drive", StringComparison.OrdinalIgnoreCase);

            if (ConfirmRestoreCallback != null)
            {
                string sourceDesc = isGoogleBackup ? _localizationService.GetString("GoogleBackup") : _localizationService.GetString("LocalBackup");
                bool confirmed = await ConfirmRestoreCallback($"{_localizationService.GetString("ConfirmRestorePromptPrefix")} {sourceDesc} ({entry.BackupDate.ToLocalTime():dd MMM yyyy, hh:mm tt}) {_localizationService.GetString("ConfirmRestorePromptSuffix")}").ConfigureAwait(false);
                if (!confirmed)
                {
                    RunOnUiThread(() => StatusMessage = _localizationService.GetString("RestoreCancelled"));
                    return;
                }
            }

            RunOnUiThread(() =>
            {
                IsRestoreRunning = true;
                HasError = false;
                ErrorMessage = string.Empty;
                StatusMessage = _localizationService.GetString("PreparingRestore");
            });

            if (isGoogleBackup && _googleDriveService.IsConnected)
            {
                RunOnUiThread(() => StatusMessage = _localizationService.GetString("DownloadingGoogleBackup"));
                await PerformRestoreInternalAsync(entry.BackupId, isCloud: true, cloudFileId: entry.BackupId).ConfigureAwait(false);
            }
            else
            {
                RunOnUiThread(() => StatusMessage = _localizationService.GetString("RestoringLocalBackup"));
                await PerformRestoreInternalAsync(entry.Location, isCloud: false, cloudFileId: string.Empty).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RestoreSpecificBackup failed: {Message}", ex.Message);
            var friendlyMsg = FormatRestoreErrorMessage(ex);
            RunOnUiThread(() =>
            {
                HasError = true;
                ErrorMessage = friendlyMsg;
                StatusMessage = friendlyMsg;
            });
        }
        finally
        {
            RunOnUiThread(() =>
            {
                IsRestoreRunning = false;
                UpdateDriveProperties();
            });
            _operationLock.Release();
        }
    }

    private async Task PerformRestoreInternalAsync(string targetPath, bool isCloud, string cloudFileId)
    {
        var downloadProgress = new Progress<int>(p =>
        {
            RunOnUiThread(() =>
            {
                StatusMessage = $"{_localizationService.GetString("DownloadingGoogleBackup")} ({p}%)";
            });
        });

        var statusProgress = new Progress<string>(status =>
        {
            RunOnUiThread(() =>
            {
                StatusMessage = status;
            });
        });

        if (isCloud && !string.IsNullOrEmpty(cloudFileId))
        {
            // Direct restore for Google Drive backups authorized via Google OAuth (Zero recovery key / password prompt)
            await _googleDriveService.RestoreFromCloudAsync(cloudFileId, null, downloadProgress, statusProgress).ConfigureAwait(false);
            await LoadBackupHistoryAsync().ConfigureAwait(false);
            RunOnUiThread(() =>
            {
                HasError = false;
                ErrorMessage = string.Empty;
                StatusMessage = _localizationService.GetString("RestoreComplete");
            });
            return;
        }

        string? password = null;
        bool completed = false;
        int promptAttempts = 0;

        while (!completed)
        {
            try
            {
                var result = await _backupService.RestoreBackupAsync(targetPath, password, statusProgress).ConfigureAwait(false);
                RunOnUiThread(() =>
                {
                    HasError = false;
                    ErrorMessage = string.Empty;
                    StatusMessage = _localizationService.GetString("RestoreComplete");
                });

                await LoadBackupHistoryAsync().ConfigureAwait(false);
                completed = true;
            }
            catch (Exception ex) when (promptAttempts < 3 &&
                                       PromptPasswordOrRecoveryKeyCallback != null &&
                                       (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                                        ex.Message.Contains("recovery key", StringComparison.OrdinalIgnoreCase) ||
                                        ex.Message.Contains("decryption failed", StringComparison.OrdinalIgnoreCase) ||
                                        ex.Message.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase) ||
                                        ex.Message.Contains("decrypt", StringComparison.OrdinalIgnoreCase)))
            {
                promptAttempts++;
                _logger.LogInformation("Local restore required credentials (attempt {Attempt}). Requesting password via UI prompt callback.", promptAttempts);

                string promptText = promptAttempts > 1
                    ? _localizationService.GetString("DecryptionFailedRetryPrompt")
                    : _localizationService.GetString("BackupPasswordRequiredPrompt");

                var promptResult = await PromptPasswordOrRecoveryKeyCallback(promptText).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(promptResult))
                {
                    RunOnUiThread(() =>
                    {
                        StatusMessage = _localizationService.GetString("RestoreCancelled");
                    });
                    return;
                }

                password = promptResult;
            }
        }
    }

    private async Task BackupToCloudAsync()
    {
        if (!_googleDriveService.IsConnected) return;
        if (!await _operationLock.WaitAsync(0).ConfigureAwait(false)) return;

        RunOnUiThread(() =>
        {
            IsBackupRunning = true;
            HasError = false;
            StatusMessage = _localizationService.GetString("CreatingAndUploadingGoogleBackup");
        });

        try
        {
            var googleMetadata = await _backupService.CreateGoogleBackupAsync(_googleDriveService.ConnectedEmail).ConfigureAwait(false);
            await _googleDriveService.UploadBackupAsync(googleMetadata.Location).ConfigureAwait(false);
            RunOnUiThread(() => StatusMessage = _localizationService.GetString("GoogleBackupCreatedAndUploaded"));
            await LoadBackupHistoryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup to Google Drive failed: {Message}", ex.Message);
            RunOnUiThread(() =>
            {
                HasError = true;
                ErrorMessage = $"{_localizationService.GetString("GoogleDrive")}: {ex.Message}";
                StatusMessage = $"{_localizationService.GetString("GoogleDrive")}: {ex.Message}";
            });
        }
        finally
        {
            RunOnUiThread(() =>
            {
                IsBackupRunning = false;
                UpdateDriveProperties();
            });
            _operationLock.Release();
        }
    }

    private async Task RestoreFromCloudAsync()
    {
        if (!await _operationLock.WaitAsync(0).ConfigureAwait(false)) return;

        try
        {
            RunOnUiThread(() =>
            {
                IsRestoreRunning = true;
                HasError = false;
                ErrorMessage = string.Empty;
            });

            // Step 1: Check Google Drive connection. If not connected, authenticate via Google OAuth.
            if (!_googleDriveService.IsConnected)
            {
                RunOnUiThread(() => StatusMessage = _localizationService.GetString("AuthenticatingWithGoogle"));
                bool connectSuccess = await _googleDriveService.ConnectAsync().ConfigureAwait(false);
                if (!connectSuccess || !_googleDriveService.IsConnected)
                {
                    RunOnUiThread(() =>
                    {
                        if (_googleDriveService.State == GoogleDriveOAuthState.AuthorizationCancelled)
                        {
                            StatusMessage = _localizationService.GetString("GoogleDriveAuthCancelled");
                        }
                        else
                        {
                            HasError = true;
                            ErrorMessage = _googleDriveService.ErrorMessage ?? _localizationService.GetString("GoogleDriveAuthCancelled");
                            StatusMessage = _googleDriveService.ErrorMessage ?? _localizationService.GetString("GoogleDriveAuthCancelled");
                        }
                    });
                    return;
                }
            }

            // Step 2: Search Google Drive for the official DhirDhar backup file.
            RunOnUiThread(() => StatusMessage = _localizationService.GetString("FindingDhirDharBackup"));
            var cloudBackups = await _googleDriveService.ListCloudBackupsAsync().ConfigureAwait(false);
            var latestCloudBackup = cloudBackups.FirstOrDefault();
            if (latestCloudBackup == null)
            {
                RunOnUiThread(() =>
                {
                    HasError = true;
                    ErrorMessage = _localizationService.GetString("NoGoogleBackupFound");
                    StatusMessage = _localizationService.GetString("NoGoogleBackupFound");
                });
                return;
            }

            // Step 3: User confirmation
            if (ConfirmRestoreCallback != null)
            {
                bool confirmed = await ConfirmRestoreCallback($"{_localizationService.GetString("ConfirmRestorePromptPrefix")} {_localizationService.GetString("GoogleBackup")} ({latestCloudBackup.BackupDate.ToLocalTime():dd-MM-yyyy hh:mm tt}) {_localizationService.GetString("ConfirmRestorePromptSuffix")}").ConfigureAwait(false);
                if (!confirmed)
                {
                    RunOnUiThread(() => StatusMessage = _localizationService.GetString("RestoreCancelled"));
                    return;
                }
            }

            // Step 4: Download and perform authenticated restore
            RunOnUiThread(() => StatusMessage = _localizationService.GetString("DownloadingBackup"));
            await PerformRestoreInternalAsync(latestCloudBackup.BackupId, isCloud: true, cloudFileId: latestCloudBackup.BackupId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore from Google Drive failed: {Message}", ex.Message);
            var friendlyMsg = FormatRestoreErrorMessage(ex);
            RunOnUiThread(() =>
            {
                HasError = true;
                ErrorMessage = friendlyMsg;
                StatusMessage = friendlyMsg;
            });
        }
        finally
        {
            RunOnUiThread(() =>
            {
                IsRestoreRunning = false;
                UpdateDriveProperties();
            });
            _operationLock.Release();
        }
    }

    private void UpdateDriveProperties()
    {
        OnPropertyChanged(nameof(GoogleDriveConnected));
        OnPropertyChanged(nameof(IsGoogleDriveConnecting));
        OnPropertyChanged(nameof(IsGoogleDriveUploading));
        OnPropertyChanged(nameof(GoogleDriveUploadProgressPercent));
        OnPropertyChanged(nameof(FormattedUploadProgress));
        OnPropertyChanged(nameof(GoogleDriveEmail));
        OnPropertyChanged(nameof(GoogleDriveLastBackupTime));
        OnPropertyChanged(nameof(GoogleDriveLastBackupStatus));
        OnPropertyChanged(nameof(GoogleDriveErrorMessage));
        UpdateCommandStates();
    }

    private void UpdateCommandStates()
    {
        BackupNowCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
        ConnectGoogleDriveCommand.RaiseCanExecuteChanged();
        DisconnectGoogleDriveCommand.RaiseCanExecuteChanged();
        BackupToCloudCommand.RaiseCanExecuteChanged();
        RestoreFromCloudCommand.RaiseCanExecuteChanged();
    }

    private static string FormatRestoreErrorMessage(Exception ex)
    {
        var msg = ex.Message;
        var full = ex.ToString();
        if (full.Contains("used by another process", StringComparison.OrdinalIgnoreCase) ||
            full.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase) ||
            full.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
            full.Contains("Restore could not safely access the DhirDhar database", StringComparison.OrdinalIgnoreCase))
        {
            return "Restore could not safely access the DhirDhar database. Please close any other DhirDhar window and try again.";
        }

        if (full.Contains("DhirDhar is already running", StringComparison.OrdinalIgnoreCase))
        {
            return "DhirDhar is already running. Close the other DhirDhar instance before restoring.";
        }

        return msg.StartsWith("Restore failed:", StringComparison.OrdinalIgnoreCase) ? msg : $"Restore failed: {msg}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_localizationService != null)
        {
            _localizationService.LanguageChanged -= _languageChangedHandler;
        }

        if (_googleDriveService != null)
        {
            _googleDriveService.ConnectionStateChanged -= _driveStateChangedHandler;
            _googleDriveService.UploadProgressChanged -= _driveProgressHandler;
            _googleDriveService.DownloadProgressChanged -= _driveDownloadProgressHandler;
        }

        if (_schedulerService != null)
        {
            _schedulerService.ScheduledBackupCompleted -= _scheduledBackupHandler;
        }

        _historyCts?.Cancel();
        _historyCts?.Dispose();
        _historyLock.Dispose();
        _operationLock.Dispose();
    }
}

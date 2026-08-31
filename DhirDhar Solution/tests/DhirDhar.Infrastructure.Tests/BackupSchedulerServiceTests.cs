using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Settings;
using DhirDhar.Infrastructure.Backup;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class BackupSchedulerServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dataDir;
    private readonly string _backupDir;
    private readonly string _dbPath;
    private readonly TestPathService _pathService;
    private readonly BackupService _backupService;
    private readonly FakeGoogleDriveService _googleDriveService;
    private readonly FakeSettingsService _settingsService;

    public BackupSchedulerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dhirdhar-scheduler-tests-" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_tempDir, "Data");
        _backupDir = Path.Combine(_tempDir, "Backup");
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_backupDir);

        _dbPath = Path.Combine(_dataDir, "DhirDhar.db");
        CreateSampleDatabase(_dbPath);

        _pathService = new TestPathService(_tempDir, _dbPath, _backupDir);
        var backupOptions = Options.Create(new BackupOptions { Directory = _backupDir, RetentionCount = 1 });
        var cryptoService = new DhirDhar.Infrastructure.Security.Cryptography.CryptoService(NullLogger<DhirDhar.Infrastructure.Security.Cryptography.CryptoService>.Instance);
        var keyService = new DhirDhar.Infrastructure.Security.Keys.KeyManagementService(cryptoService, _pathService, new SecurityEncryptionTests.MockAuditService(), NullLogger<DhirDhar.Infrastructure.Security.Keys.KeyManagementService>.Instance);
        keyService.InitializeMasterKeyAsync().GetAwaiter().GetResult();

        _backupService = new BackupService(_pathService, keyService, cryptoService, backupOptions, NullLogger<BackupService>.Instance);
        _googleDriveService = new FakeGoogleDriveService();
        _settingsService = new FakeSettingsService();
    }

    private static void CreateSampleDatabase(string path)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS Test (Id INTEGER PRIMARY KEY);";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task TriggerBackupCheckAsync_WhenAutomaticBackupEnabled_CreatesBackupAndUpdatesNextScheduledTime()
    {
        var settings = await _settingsService.GetSettingsAsync();
        settings.AutomaticBackupEnabled = true;
        settings.BackupFrequency = "Daily";
        settings.NextScheduledBackupTime = DateTime.UtcNow.AddMinutes(-5); // due now
        await _settingsService.SaveSettingsAsync(settings);

        using var scheduler = new BackupSchedulerService(
            _backupService,
            _googleDriveService,
            _settingsService,
            NullLogger<BackupSchedulerService>.Instance);

        bool completedEventFired = false;
        scheduler.ScheduledBackupCompleted += (s, e) => completedEventFired = true;

        await scheduler.TriggerBackupCheckAsync();

        Assert.True(completedEventFired);

        var updatedSettings = await _settingsService.GetSettingsAsync();
        Assert.NotNull(updatedSettings.LastAutomaticBackupTime);
        Assert.NotNull(updatedSettings.NextScheduledBackupTime);
        Assert.True(updatedSettings.NextScheduledBackupTime.Value > DateTime.UtcNow);

        // Verify single physical local backup exists
        var backups = Directory.GetFiles(_backupDir, "*.ddbackup");
        Assert.Single(backups);
        Assert.Equal(BackupService.LocalBackupFileName, Path.GetFileName(backups[0]));
    }

    [Fact]
    public async Task TriggerBackupCheckAsync_AlwaysExecutes_EvenIfSettingHadFalse_AndPerformsAutomaticRetentionCleanup()
    {
        var settings = await _settingsService.GetSettingsAsync();
        settings.AutomaticBackupEnabled = false; // Even if false, scheduler treats it as always ON
        settings.BackupFrequency = "Daily";
        settings.RetentionCount = 1;
        settings.NextScheduledBackupTime = DateTime.UtcNow.AddMinutes(-5); // due now
        await _settingsService.SaveSettingsAsync(settings);

        using var scheduler = new BackupSchedulerService(
            _backupService,
            _googleDriveService,
            _settingsService,
            NullLogger<BackupSchedulerService>.Instance);

        bool completedEventFired = false;
        scheduler.ScheduledBackupCompleted += (s, e) => completedEventFired = true;

        await scheduler.TriggerBackupCheckAsync();

        Assert.True(completedEventFired);

        var updatedSettings = await _settingsService.GetSettingsAsync();
        Assert.True(updatedSettings.AutomaticBackupEnabled);
        Assert.NotNull(updatedSettings.LastAutomaticBackupTime);
        Assert.NotNull(updatedSettings.NextScheduledBackupTime);
        Assert.True(updatedSettings.NextScheduledBackupTime.Value > DateTime.UtcNow);

        // Verify single physical backup exists
        var backups = Directory.GetFiles(_backupDir, "*.ddbackup");
        Assert.Single(backups);
        Assert.Equal(BackupService.LocalBackupFileName, Path.GetFileName(backups[0]));
    }

    [Fact]
    public void CalculateNextBackupTime_ComputesCorrectIntervals()
    {
        var now = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

        var daily = BackupSchedulerService.CalculateNextBackupTime(now, "Daily");
        Assert.Equal(now.AddDays(1), daily);

        var weekly = BackupSchedulerService.CalculateNextBackupTime(now, "Weekly");
        Assert.Equal(now.AddDays(7), weekly);

        var monthly = BackupSchedulerService.CalculateNextBackupTime(now, "Monthly");
        Assert.Equal(now.AddMonths(1), monthly);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        private AppSettingsModel _settings = new();

        public ApplicationLanguageSettings LanguageSettings { get; } = new();

        public event EventHandler<AppSettingsModel>? SettingsChanged;

        public Task<AppSettingsModel> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_settings);
        }

        public Task SaveSettingsAsync(AppSettingsModel settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            SettingsChanged?.Invoke(this, settings);
            return Task.CompletedTask;
        }

        public Task ResetSettingsAsync(CancellationToken cancellationToken = default)
        {
            _settings = new AppSettingsModel();
            SettingsChanged?.Invoke(this, _settings);
            return Task.CompletedTask;
        }

        public Task ApplySettingsOnStartupAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGoogleDriveService : IGoogleDriveService
    {
        public GoogleDriveOAuthState State => GoogleDriveOAuthState.NotConnected;
        public bool IsConnected => false;
        public bool IsConnecting => false;
        public bool IsUploading => false;
        public bool IsDownloading => false;
        public int UploadProgressPercent => 0;
        public int DownloadProgressPercent => 0;
        public string? ConnectedEmail => null;
        public string? LastBackupTime => null;
        public string? LastBackupStatus => null;
        public string? ErrorMessage => null;
        public bool CleanupCalled { get; private set; }

        public event EventHandler? ConnectionStateChanged { add { } remove { } }
        public event EventHandler<int>? UploadProgressChanged { add { } remove { } }
        public event EventHandler<int>? DownloadProgressChanged { add { } remove { } }

        public Task<bool> InitializeAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DhirDhar.Application.Backup.Models.BackupMetadata> UploadBackupAsync(string localBackupPath, IProgress<int>? progress = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<System.Collections.Generic.IReadOnlyList<DhirDhar.Application.Backup.Models.BackupHistoryEntry>> ListCloudBackupsAsync(CancellationToken cancellationToken = default) => Task.FromResult<System.Collections.Generic.IReadOnlyList<DhirDhar.Application.Backup.Models.BackupHistoryEntry>>(Array.Empty<DhirDhar.Application.Backup.Models.BackupHistoryEntry>());
        public Task<string> DownloadBackupAsync(string cloudFileId, string destinationFileName, IProgress<int>? progress = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DhirDhar.Application.Backup.Models.BackupMetadata> RestoreFromCloudAsync(string cloudFileId, string? password = null, IProgress<int>? downloadProgress = null, IProgress<string>? statusProgress = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task CleanupOldCloudBackupsAsync(int? retentionCount = null, CancellationToken cancellationToken = default)
        {
            CleanupCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestPathService : IDatabasePathService
    {
        public TestPathService(string appDataDir, string dbPath, string backupDir)
        {
            ApplicationDataDirectory = appDataDir;
            DatabasePath = dbPath;
            BackupDirectory = backupDir;
            DatabaseDirectory = Path.GetDirectoryName(dbPath)!;
            LogDirectory = Path.Combine(appDataDir, "Logs");
        }

        public string ApplicationDataDirectory { get; }
        public string DatabaseDirectory { get; }
        public string DatabasePath { get; }
        public string BackupDirectory { get; }
        public string LogDirectory { get; }
    }
}

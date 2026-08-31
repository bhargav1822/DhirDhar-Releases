using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Backup.Models;

namespace DhirDhar.Application.Backup;

public enum GoogleDriveOAuthState
{
    NotConnected,
    Connecting,
    WaitingForGoogle,
    Authorizing,
    Connected,
    AuthorizationCancelled,
    AuthorizationFailed,
    TokenExpired,
    ReauthRequired,
    Disconnected,
    Offline
}

public interface IGoogleDriveService
{
    GoogleDriveOAuthState State { get; }
    bool IsConnected { get; }
    bool IsConnecting { get; }
    bool IsUploading { get; }
    bool IsDownloading { get; }
    int UploadProgressPercent { get; }
    int DownloadProgressPercent { get; }
    string? ConnectedEmail { get; }
    string? LastBackupTime { get; }
    string? LastBackupStatus { get; }
    string? ErrorMessage { get; }

    event EventHandler? ConnectionStateChanged;
    event EventHandler<int>? UploadProgressChanged;
    event EventHandler<int>? DownloadProgressChanged;

    Task<bool> InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<BackupMetadata> UploadBackupAsync(string localBackupPath, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupHistoryEntry>> ListCloudBackupsAsync(CancellationToken cancellationToken = default);
    Task<string> DownloadBackupAsync(string cloudFileId, string destinationFileName, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    Task<BackupMetadata> RestoreFromCloudAsync(string cloudFileId, string? password = null, IProgress<int>? downloadProgress = null, IProgress<string>? statusProgress = null, CancellationToken cancellationToken = default);
    Task CleanupOldCloudBackupsAsync(int? retentionCount = null, CancellationToken cancellationToken = default);
}

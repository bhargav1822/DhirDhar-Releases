using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Backup.Models;

namespace DhirDhar.Application.Backup;

public interface IBackupService
{
    Task<BackupMetadata> CreateBackupAsync(string? password = null, CancellationToken cancellationToken = default);

    Task<BackupMetadata> CreateGoogleBackupAsync(string? accountEmail = null, CancellationToken cancellationToken = default);

    Task<BackupMetadata> RestoreBackupAsync(string backupPath, string? password = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task<bool> VerifyBackupAsync(string backupPath, CancellationToken cancellationToken = default);

    bool IsEncryptedBackup(string backupPath);

    Task<IReadOnlyList<BackupHistoryEntry>> GetBackupHistoryAsync(CancellationToken cancellationToken = default);

    Task<BackupMetadata> CreateSafetyBackupAsync(CancellationToken cancellationToken = default);

    Task CleanupOldBackupsAsync(int? retentionCount = null, CancellationToken cancellationToken = default);
}

using System;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Security.Models;

namespace DhirDhar.Application.Security.Keys;

public interface IKeyManagementService
{
    bool IsMasterKeyInitialized();

    Task InitializeMasterKeyAsync(CancellationToken cancellationToken = default);

    byte[] GetMasterKey();

    byte[] GetFieldEncryptionKey();

    byte[] GetSearchIndexKey();

    byte[] GetPhotoEncryptionKey();

    byte[] GetBackupMasterKey();

    Task<RecoveryKeyDetails> GenerateOrGetRecoveryKeyAsync(CancellationToken cancellationToken = default);

    string? GetCurrentRecoveryKey();

    Task<RecoveryKeyDetails> RotateRecoveryKeyAsync(CancellationToken cancellationToken = default);

    Task<bool> RecoverMasterKeyWithRecoveryKeyAsync(string formattedRecoveryKey, CancellationToken cancellationToken = default);

    Task<bool> SetPassphraseProtectionAsync(string passphrase, CancellationToken cancellationToken = default);

    Task<bool> UnlockWithPassphraseAsync(string passphrase, CancellationToken cancellationToken = default);

    Task<EncryptionStatusInfo> GetEncryptionStatusAsync(CancellationToken cancellationToken = default);

    Task<bool> VerifyEncryptionIntegrityAsync(CancellationToken cancellationToken = default);
}

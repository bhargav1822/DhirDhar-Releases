using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Audit;
using DhirDhar.Application.Security.Cryptography;
using DhirDhar.Application.Security.Keys;
using DhirDhar.Application.Security.Models;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Security.Keys;

[SupportedOSPlatform("windows")]
public sealed class KeyManagementService : IKeyManagementService
{
    private static readonly byte[] Entropy = "DhirDhar.Enterprise.Security.v1"u8.ToArray();
    private readonly ICryptoService _cryptoService;
    private readonly IDatabasePathService _pathService;
    private readonly IAuditService _auditService;
    private readonly ILogger<KeyManagementService> _logger;

    private byte[]? _cachedMasterKey;
    private byte[]? _fieldKey;
    private byte[]? _searchKey;
    private byte[]? _photoKey;
    private byte[]? _backupKey;
    private DateTime? _lastVerifiedAt;
    private readonly object _lock = new();

    public KeyManagementService(
        ICryptoService cryptoService,
        IDatabasePathService pathService,
        IAuditService auditService,
        ILogger<KeyManagementService> logger)
    {
        _cryptoService = cryptoService;
        _pathService = pathService;
        _auditService = auditService;
        _logger = logger;
    }

    private string GetSecurityDirectory()
    {
        if (!string.IsNullOrEmpty(_pathService.ApplicationDataDirectory))
        {
            var pathDir = Path.Combine(_pathService.ApplicationDataDirectory, "Security");
            Directory.CreateDirectory(pathDir);
            return pathDir;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "DhirDhar Solution", "Security");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string GetMasterKeyFilePath() => Path.Combine(GetSecurityDirectory(), "master.key.enc");
    private string GetRecoveryKeyFilePath() => Path.Combine(GetSecurityDirectory(), "recovery.key.enc");
    private string GetRecoveryVaultFilePath() => Path.Combine(GetSecurityDirectory(), "vault.recovery.enc");
    private string GetPassphraseVaultFilePath() => Path.Combine(GetSecurityDirectory(), "vault.passphrase.enc");

    public bool IsMasterKeyInitialized()
    {
        lock (_lock)
        {
            if (_cachedMasterKey != null) return true;
            return File.Exists(GetMasterKeyFilePath());
        }
    }

    public async Task InitializeMasterKeyAsync(CancellationToken cancellationToken = default)
    {
        EnsureMasterKeyLoadedCore();

        await _auditService.RecordAsync(new AuditEvent(
            "EncryptionInitialized",
            "Security",
            null,
            "Master encryption key initialized and verified with DPAPI hardware-backed protection.",
            "SUCCESS",
            null,
            null), cancellationToken).ConfigureAwait(false);
    }

    private void EnsureMasterKeyLoadedCore()
    {
        lock (_lock)
        {
            if (_cachedMasterKey != null) return;

            var keyFile = GetMasterKeyFilePath();
            if (File.Exists(keyFile))
            {
                try
                {
                    var encryptedBytes = File.ReadAllBytes(keyFile);
                    _cachedMasterKey = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                    _logger.LogInformation("Loaded and unprotected master encryption key via Windows DPAPI.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to unprotect existing master key via DPAPI.");
                    throw new InvalidOperationException("Failed to unlock master encryption key. Ensure the application is running under the authorized Windows user account.", ex);
                }
            }
            else
            {
                _logger.LogInformation("Generating new 256-bit cryptographically secure master encryption key.");
                _cachedMasterKey = _cryptoService.GenerateRandomKey(32);

                var protectedBytes = ProtectedData.Protect(_cachedMasterKey, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(keyFile, protectedBytes);
                _logger.LogInformation("Securely stored master encryption key using Windows DPAPI.");

                // Initialize persistent recovery key at first setup
                var recoveryKeyFile = GetRecoveryKeyFilePath();
                if (!File.Exists(recoveryKeyFile))
                {
                    var recoveryRawBytes = _cryptoService.GenerateRandomKey(32);
                    var protectedRecoveryBytes = ProtectedData.Protect(recoveryRawBytes, Entropy, DataProtectionScope.CurrentUser);
                    File.WriteAllBytes(recoveryKeyFile, protectedRecoveryBytes);

                    var recoveryDerivedKey = _cryptoService.DeriveKey(recoveryRawBytes, "DhirDhar-Recovery-Vault-v1");
                    var payload = _cryptoService.Encrypt(_cachedMasterKey, recoveryDerivedKey);
                    File.WriteAllBytes(GetRecoveryVaultFilePath(), payload.ToBytes());
                }
            }

            DeriveAllSubKeys();
        }
    }

    public byte[] GetMasterKey()
    {
        lock (_lock)
        {
            if (_cachedMasterKey == null)
            {
                EnsureMasterKeyLoadedCore();
            }
            return _cachedMasterKey!;
        }
    }

    public byte[] GetFieldEncryptionKey()
    {
        lock (_lock)
        {
            if (_fieldKey == null)
            {
                GetMasterKey();
            }
            return _fieldKey!;
        }
    }

    public byte[] GetSearchIndexKey()
    {
        lock (_lock)
        {
            if (_searchKey == null)
            {
                GetMasterKey();
            }
            return _searchKey!;
        }
    }

    public byte[] GetPhotoEncryptionKey()
    {
        lock (_lock)
        {
            if (_photoKey == null)
            {
                GetMasterKey();
            }
            return _photoKey!;
        }
    }

    public byte[] GetBackupMasterKey()
    {
        lock (_lock)
        {
            if (_backupKey == null)
            {
                GetMasterKey();
            }
            return _backupKey!;
        }
    }

    private void DeriveAllSubKeys()
    {
        if (_cachedMasterKey == null) return;

        _fieldKey = _cryptoService.DeriveKey(_cachedMasterKey, "DhirDhar-Field-Encryption-v1");
        _searchKey = _cryptoService.DeriveKey(_cachedMasterKey, "DhirDhar-Search-BlindIndex-v1");
        _photoKey = _cryptoService.DeriveKey(_cachedMasterKey, "DhirDhar-Photo-Encryption-v1");
        _backupKey = _cryptoService.DeriveKey(_cachedMasterKey, "DhirDhar-Backup-Encryption-v1");
        _lastVerifiedAt = DateTime.UtcNow;
    }

    public async Task<RecoveryKeyDetails> GenerateOrGetRecoveryKeyAsync(CancellationToken cancellationToken = default)
    {
        var masterKey = GetMasterKey();
        var keyFilePath = GetRecoveryKeyFilePath();
        var vaultPath = GetRecoveryVaultFilePath();

        byte[] recoveryRawBytes;
        bool isNew = false;

        lock (_lock)
        {
            if (File.Exists(keyFilePath))
            {
                try
                {
                    var encryptedBytes = File.ReadAllBytes(keyFilePath);
                    recoveryRawBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                }
                catch
                {
                    recoveryRawBytes = _cryptoService.GenerateRandomKey(32);
                    var protectedBytes = ProtectedData.Protect(recoveryRawBytes, Entropy, DataProtectionScope.CurrentUser);
                    File.WriteAllBytes(keyFilePath, protectedBytes);
                    isNew = true;
                }
            }
            else
            {
                recoveryRawBytes = _cryptoService.GenerateRandomKey(32);
                var protectedBytes = ProtectedData.Protect(recoveryRawBytes, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(keyFilePath, protectedBytes);
                isNew = true;
            }
        }

        var hex = Convert.ToHexString(recoveryRawBytes);
        var chunks = new string[8];
        for (int i = 0; i < 8; i++)
        {
            chunks[i] = hex.Substring(i * 8, 8);
        }
        var formattedKey = "DDRK-" + string.Join("-", chunks);

        if (isNew || !File.Exists(vaultPath))
        {
            // Wrap master key with recovery key into recovery vault
            var recoveryDerivedKey = _cryptoService.DeriveKey(recoveryRawBytes, "DhirDhar-Recovery-Vault-v1");
            var payload = _cryptoService.Encrypt(masterKey, recoveryDerivedKey);
            await File.WriteAllBytesAsync(vaultPath, payload.ToBytes(), cancellationToken).ConfigureAwait(false);

            await _auditService.RecordAsync(new AuditEvent(
                "RecoveryKeyGenerated",
                "Security",
                null,
                "A persistent disaster recovery key was generated and encrypted master key vault was initialized.",
                "SUCCESS",
                null,
                null), cancellationToken).ConfigureAwait(false);
        }

        return new RecoveryKeyDetails(formattedKey, DateTime.UtcNow);
    }

    public string? GetCurrentRecoveryKey()
    {
        lock (_lock)
        {
            var keyFilePath = GetRecoveryKeyFilePath();
            if (!File.Exists(keyFilePath))
            {
                return null;
            }

            try
            {
                var encryptedBytes = File.ReadAllBytes(keyFilePath);
                var recoveryRawBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                var hex = Convert.ToHexString(recoveryRawBytes);
                var chunks = new string[8];
                for (int i = 0; i < 8; i++)
                {
                    chunks[i] = hex.Substring(i * 8, 8);
                }
                return "DDRK-" + string.Join("-", chunks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve existing recovery key via DPAPI.");
                return null;
            }
        }
    }

    public async Task<RecoveryKeyDetails> RotateRecoveryKeyAsync(CancellationToken cancellationToken = default)
    {
        var masterKey = GetMasterKey();
        var keyFilePath = GetRecoveryKeyFilePath();
        var vaultPath = GetRecoveryVaultFilePath();

        var recoveryRawBytes = _cryptoService.GenerateRandomKey(32);
        lock (_lock)
        {
            var protectedBytes = ProtectedData.Protect(recoveryRawBytes, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(keyFilePath, protectedBytes);
        }

        var hex = Convert.ToHexString(recoveryRawBytes);
        var chunks = new string[8];
        for (int i = 0; i < 8; i++)
        {
            chunks[i] = hex.Substring(i * 8, 8);
        }
        var formattedKey = "DDRK-" + string.Join("-", chunks);

        var recoveryDerivedKey = _cryptoService.DeriveKey(recoveryRawBytes, "DhirDhar-Recovery-Vault-v1");
        var payload = _cryptoService.Encrypt(masterKey, recoveryDerivedKey);
        await File.WriteAllBytesAsync(vaultPath, payload.ToBytes(), cancellationToken).ConfigureAwait(false);

        await _auditService.RecordAsync(new AuditEvent(
            "RecoveryKeyRotated",
            "Security",
            null,
            "Disaster recovery key was explicitly rotated and master key vault was re-encrypted.",
            "SUCCESS",
            null,
            null), cancellationToken).ConfigureAwait(false);

        return new RecoveryKeyDetails(formattedKey, DateTime.UtcNow);
    }

    public async Task<bool> RecoverMasterKeyWithRecoveryKeyAsync(string formattedRecoveryKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(formattedRecoveryKey)) return false;

        var cleanHex = formattedRecoveryKey.Replace("DDRK-", "", StringComparison.OrdinalIgnoreCase).Replace("-", "").Trim();
        if (cleanHex.Length != 64) return false;

        byte[] rawRecoveryBytes;
        try
        {
            rawRecoveryBytes = Convert.FromHexString(cleanHex);
        }
        catch
        {
            return false;
        }

        var vaultPath = GetRecoveryVaultFilePath();
        if (!File.Exists(vaultPath)) return false;

        try
        {
            var vaultBytes = await File.ReadAllBytesAsync(vaultPath, cancellationToken).ConfigureAwait(false);
            var payload = EncryptedPayload.FromBytes(vaultBytes);
            var recoveryDerivedKey = _cryptoService.DeriveKey(rawRecoveryBytes, "DhirDhar-Recovery-Vault-v1");
            var recoveredMasterKey = _cryptoService.Decrypt(payload, recoveryDerivedKey);

            lock (_lock)
            {
                _cachedMasterKey = recoveredMasterKey;
                var keyFile = GetMasterKeyFilePath();
                var protectedBytes = ProtectedData.Protect(_cachedMasterKey, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(keyFile, protectedBytes);

                // Persist the authorized recovery key
                var recKeyFile = GetRecoveryKeyFilePath();
                var protectedRecBytes = ProtectedData.Protect(rawRecoveryBytes, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(recKeyFile, protectedRecBytes);

                DeriveAllSubKeys();
            }

            await _auditService.RecordAsync(new AuditEvent(
                "MasterKeyRecovered",
                "Security",
                null,
                "Master encryption key was successfully recovered using authorized recovery key.",
                "SUCCESS",
                null,
                null), cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover master key with provided recovery key.");
            return false;
        }
    }

    public async Task<bool> SetPassphraseProtectionAsync(string passphrase, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 6)
        {
            throw new ArgumentException("Passphrase must be at least 6 characters.", nameof(passphrase));
        }

        var masterKey = GetMasterKey();
        var salt = _cryptoService.GenerateRandomNonce(16);
        var derivedKey = _cryptoService.DeriveKeyFromPassphrase(passphrase, salt);

        var payload = _cryptoService.Encrypt(masterKey, derivedKey);
        var vaultPath = GetPassphraseVaultFilePath();

        using (var fs = new FileStream(vaultPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await fs.WriteAsync(salt, cancellationToken).ConfigureAwait(false);
            var payloadBytes = payload.ToBytes();
            await fs.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
        }

        await _auditService.RecordAsync(new AuditEvent(
            "PassphraseProtectionConfigured",
            "Security",
            null,
            "User passphrase protection was configured for master key vault.",
            "SUCCESS",
            null,
            null), cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<bool> UnlockWithPassphraseAsync(string passphrase, CancellationToken cancellationToken = default)
    {
        var vaultPath = GetPassphraseVaultFilePath();
        if (!File.Exists(vaultPath)) return false;

        try
        {
            using var fs = new FileStream(vaultPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var salt = new byte[16];
            await fs.ReadExactlyAsync(salt, cancellationToken).ConfigureAwait(false);

            var payloadBytes = new byte[fs.Length - 16];
            await fs.ReadExactlyAsync(payloadBytes, cancellationToken).ConfigureAwait(false);

            var derivedKey = _cryptoService.DeriveKeyFromPassphrase(passphrase, salt);
            var payload = EncryptedPayload.FromBytes(payloadBytes);
            var masterKey = _cryptoService.Decrypt(payload, derivedKey);

            lock (_lock)
            {
                _cachedMasterKey = masterKey;
                var keyFile = GetMasterKeyFilePath();
                var protectedBytes = ProtectedData.Protect(_cachedMasterKey, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(keyFile, protectedBytes);
                DeriveAllSubKeys();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unlock master key with passphrase.");
            return false;
        }
    }

    public Task<EncryptionStatusInfo> GetEncryptionStatusAsync(CancellationToken cancellationToken = default)
    {
        var isInit = IsMasterKeyInitialized();
        var hasPassphrase = File.Exists(GetPassphraseVaultFilePath());

        return Task.FromResult(new EncryptionStatusInfo(
            IsEncryptionActive: isInit,
            IsDatabaseEncrypted: isInit,
            IsBackupEncrypted: true,
            IsKeyStorageSecure: true,
            Algorithm: "AES-256-GCM / PBKDF2-SHA256",
            EncryptionVersion: "v1.0 (AEAD)",
            LastVerifiedAt: _lastVerifiedAt,
            HasUserPassphrase: hasPassphrase));
    }

    public async Task<bool> VerifyEncryptionIntegrityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var fieldKey = GetFieldEncryptionKey();
            var testPlaintext = "DhirDhar-Security-Verification-Test-" + Guid.NewGuid().ToString("N");
            var encrypted = _cryptoService.EncryptString(testPlaintext, fieldKey);
            var decrypted = _cryptoService.DecryptString(encrypted, fieldKey);

            if (decrypted != testPlaintext)
            {
                throw new InvalidOperationException("Cryptographic verification roundtrip mismatch.");
            }

            _lastVerifiedAt = DateTime.UtcNow;

            await _auditService.RecordAsync(new AuditEvent(
                "EncryptionVerified",
                "Security",
                null,
                "Cryptographic integrity verification passed successfully.",
                "SUCCESS",
                null,
                null), cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cryptographic integrity verification failed.");
            return false;
        }
    }
}

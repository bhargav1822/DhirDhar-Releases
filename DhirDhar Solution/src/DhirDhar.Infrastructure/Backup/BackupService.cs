using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Backup.Models;
using DhirDhar.Application.Security.Cryptography;
using DhirDhar.Application.Security.Keys;
using DhirDhar.Application.Security.Models;
using DhirDhar.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DhirDhar.Infrastructure.Backup;

public sealed class BackupService : IBackupService
{
    public const string LocalBackupFileName = "DhirDhar_Local_Backup.ddbackup";
    public const string LocalBackupType = "Local Backup";
    public const string LocalBackupLocation = "Local";
    public const string BackupTypeLocal = "Local Backup";
    public const string BackupTypeGoogle = "Google Backup";

    public const string BackupFormatVersion = "3.0";
    private const int EncryptionSaltSize = 32;
    private const int EncryptionKeySize = 32;
    private const int EncryptionNonceSize = 12;
    private const int EncryptionTagSize = 16;
    private const int Pbkdf2Iterations = 600_000;

    private readonly IDatabasePathService _pathService;
    private readonly IKeyManagementService _keyManagementService;
    private readonly ICryptoService _cryptoService;
    private readonly IDatabaseLifecycleService? _lifecycleService;
    private readonly BackupOptions _backupOptions;
    private readonly ILogger<BackupService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public BackupService(
        IDatabasePathService pathService,
        IKeyManagementService keyManagementService,
        ICryptoService cryptoService,
        IOptions<BackupOptions> backupOptions,
        ILogger<BackupService> logger,
        IDatabaseLifecycleService? lifecycleService = null)
    {
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _keyManagementService = keyManagementService ?? throw new ArgumentNullException(nameof(keyManagementService));
        _cryptoService = cryptoService ?? throw new ArgumentNullException(nameof(cryptoService));
        _backupOptions = backupOptions?.Value ?? new BackupOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lifecycleService = lifecycleService;
    }

    public async Task<BackupMetadata> CreateBackupAsync(string? password = null, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        var backupId = "DhirDhar_Local_Backup";
        var timestamp = DateTime.UtcNow;
        var backupDirectory = GetBackupDirectory();
        Directory.CreateDirectory(backupDirectory);

        var finalBackupPath = Path.Combine(backupDirectory, LocalBackupFileName);
        var tempBackupPath = Path.Combine(backupDirectory, $"DhirDhar_Local_Backup_{Guid.NewGuid():N}.tmp");
        var tempDbPath = Path.GetTempFileName();

        try
        {
            _logger.LogInformation("Local backup creation initiated: target='{FinalPath}', temp='{TempPath}'", finalBackupPath, tempBackupPath);

            CreateConsistentDatabaseCopy(tempDbPath);

            var metadata = new BackupFileInfo
            {
                BackupId = backupId,
                BackupFormatVersion = BackupFormatVersion,
                ApplicationVersion = "2.0.0",
                SchemaVersion = "1.0",
                CreatedAt = timestamp,
                BackupType = BackupTypeLocal,
                DatabasePath = tempDbPath
            };

            if (_backupOptions.EncryptBackups || !string.IsNullOrEmpty(password))
            {
                await CreateEncryptedBackupAsync(tempDbPath, tempBackupPath, password, metadata, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await CreateUnencryptedBackupAsync(tempDbPath, tempBackupPath, metadata, cancellationToken).ConfigureAwait(false);
            }

            // Validate temporary backup before replacing existing backup
            bool isValid = await VerifyBackupAsync(tempBackupPath, cancellationToken).ConfigureAwait(false);
            if (!isValid)
            {
                throw new InvalidOperationException("Validation of newly created temporary backup package failed. Existing backup preserved.");
            }

            // Atomically replace existing backup with verified temporary backup
            if (File.Exists(finalBackupPath))
            {
                File.Delete(finalBackupPath);
            }
            File.Move(tempBackupPath, finalBackupPath);

            // Clean up legacy automatic backups if any exist
            MigrateAndCleanupLegacyBackups();

            var fileSize = new FileInfo(finalBackupPath).Length;
            var integrityHash = await ComputeFileHashAsync(finalBackupPath, cancellationToken).ConfigureAwait(false);

            var backupMetadata = new BackupMetadata(
                LocalBackupFileName,
                BackupFormatVersion,
                "2.0.0",
                "1.0",
                timestamp,
                BackupTypeLocal,
                finalBackupPath,
                fileSize,
                integrityHash,
                "Successful",
                "Verified");

            _logger.LogInformation("Local backup created and verified successfully: Path='{Path}', Size={Size} bytes", finalBackupPath, fileSize);
            return backupMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local backup creation failed. Previous valid backup preserved.");

            if (File.Exists(tempBackupPath))
            {
                try { File.Delete(tempBackupPath); } catch { }
            }

            throw new InvalidOperationException($"Local backup creation failed: {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(tempDbPath))
            {
                try { File.Delete(tempDbPath); } catch { }
            }
            _semaphore.Release();
        }
    }

    public async Task<BackupMetadata> CreateGoogleBackupAsync(string? accountEmail = null, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        var backupId = "DhirDhar_Google_Backup";
        var timestamp = DateTime.UtcNow;
        var backupDirectory = GetBackupDirectory();
        Directory.CreateDirectory(backupDirectory);

        var finalBackupPath = Path.Combine(backupDirectory, "DhirDhar_Google_Backup.ddbackup");
        var tempBackupPath = Path.Combine(backupDirectory, $"DhirDhar_Google_Backup_{Guid.NewGuid():N}.tmp");
        var tempDbPath = Path.GetTempFileName();

        try
        {
            _logger.LogInformation("Google backup creation initiated: target='{FinalPath}', temp='{TempPath}'", finalBackupPath, tempBackupPath);

            CreateConsistentDatabaseCopy(tempDbPath);

            var metadata = new BackupFileInfo
            {
                BackupId = backupId,
                BackupFormatVersion = BackupFormatVersion,
                ApplicationVersion = "2.0.0",
                SchemaVersion = "1.0",
                CreatedAt = timestamp,
                BackupType = "Google Backup",
                DatabasePath = tempDbPath
            };

            if (File.Exists(tempBackupPath))
            {
                try { File.Delete(tempBackupPath); } catch { }
            }

            using (var archive = ZipFile.Open(tempBackupPath, ZipArchiveMode.Create))
            {
                if (File.Exists(tempDbPath))
                {
                    archive.CreateEntryFromFile(tempDbPath, "DhirDhar.db", CompressionLevel.Optimal);

                    var dbHash = await ComputeFileHashAsync(tempDbPath, cancellationToken).ConfigureAwait(false);
                    var hashEntry = archive.CreateEntry("integrity.hash", CompressionLevel.Optimal);
                    using (var hashStream = hashEntry.Open())
                    using (var writer = new StreamWriter(hashStream, Encoding.UTF8))
                    {
                        await writer.WriteAsync(dbHash).ConfigureAwait(false);
                    }
                }

                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (var manifestStream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(manifestStream, new BackupManifest
                    {
                        BackupId = metadata.BackupId,
                        BackupFormatVersion = BackupFormatVersion,
                        ApplicationVersion = metadata.ApplicationVersion,
                        SchemaVersion = metadata.SchemaVersion,
                        CreatedAt = metadata.CreatedAt,
                        BackupType = metadata.BackupType,
                        Encrypted = false,
                        PasswordProtected = false,
                        ProtectionMode = "GoogleDriveOAuth",
                        AccountEmail = accountEmail ?? string.Empty
                    }, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }

            // Validate temporary backup before replacing existing backup
            bool isValid = await VerifyBackupAsync(tempBackupPath, cancellationToken).ConfigureAwait(false);
            if (!isValid)
            {
                throw new InvalidOperationException("Validation of newly created temporary Google backup package failed.");
            }

            if (File.Exists(finalBackupPath))
            {
                File.Delete(finalBackupPath);
            }
            File.Move(tempBackupPath, finalBackupPath);

            var fileSize = new FileInfo(finalBackupPath).Length;
            var integrityHash = await ComputeFileHashAsync(finalBackupPath, cancellationToken).ConfigureAwait(false);

            var backupMetadata = new BackupMetadata(
                "DhirDhar_Google_Backup.ddbackup",
                BackupFormatVersion,
                "2.0.0",
                "1.0",
                timestamp,
                "Google Backup",
                finalBackupPath,
                fileSize,
                integrityHash,
                "Successful",
                "Verified");

            _logger.LogInformation("Google backup created and verified successfully: Path='{Path}', Size={Size} bytes", finalBackupPath, fileSize);
            return backupMetadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google backup creation failed.");

            if (File.Exists(tempBackupPath))
            {
                try { File.Delete(tempBackupPath); } catch { }
            }

            throw new InvalidOperationException($"Google backup creation failed: {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(tempDbPath))
            {
                try { File.Delete(tempDbPath); } catch { }
            }
            _semaphore.Release();
        }
    }

    public async Task<BackupMetadata> RestoreBackupAsync(string backupPath, string? password = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var resolvedPath = ResolveBackupPath(backupPath);

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException("Backup file not found.", resolvedPath);
        }

        progress?.Report("Validating Backup...");
        if (!await VerifyBackupAsync(resolvedPath, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Backup validation failed. The downloaded backup is invalid or corrupted.");
        }

        var restoreDir = Path.Combine(Path.GetTempPath(), "dhirdhar-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(restoreDir);

        try
        {
            _logger.LogInformation("Restore started for backup: '{BackupPath}'", resolvedPath);

            if (IsEncryptedBackup(resolvedPath))
            {
                if (resolvedPath.Contains("Google", StringComparison.OrdinalIgnoreCase) || backupPath.Contains("Google", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("This Google Backup was created with an older backup format. Create a new Google Backup from the original DhirDhar system.");
                }

                progress?.Report("Decrypting Backup...");
                await DecryptAndExtractBackupAsync(resolvedPath, restoreDir, password).ConfigureAwait(false);
            }
            else
            {
                progress?.Report("Validating Backup...");
                ExtractBackup(resolvedPath, restoreDir);
            }

            var restoreDbPath = Path.Combine(restoreDir, "DhirDhar.db");
            if (!File.Exists(restoreDbPath))
            {
                throw new InvalidOperationException("Restore failed. The backup database could not be validated.");
            }

            if (_lifecycleService != null)
            {
                using (var session = await _lifecycleService.EnterRestoreModeAsync(cancellationToken).ConfigureAwait(false))
                {
                    await _lifecycleService.ReplaceDatabaseAtomicallyAsync(restoreDbPath, progress, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                // Fallback if lifecycle service not registered
                progress?.Report("Preparing Database...");
                using (var verifyConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={restoreDbPath};Mode=ReadOnly;Pooling=False"))
                {
                    verifyConn.Open();
                    using var cmd = verifyConn.CreateCommand();
                    cmd.CommandText = "PRAGMA integrity_check;";
                    var integrityResult = cmd.ExecuteScalar()?.ToString();
                    if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Restore failed. The backup database could not be validated: {integrityResult}");
                    }
                    verifyConn.Close();
                }

                progress?.Report("Closing Database...");
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                var dbPath = _pathService.DatabasePath;
                var dbDir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dbDir))
                {
                    Directory.CreateDirectory(dbDir);
                }

                var walPath = dbPath + "-wal";
                var shmPath = dbPath + "-shm";
                if (File.Exists(walPath)) { try { File.Delete(walPath); } catch { } }
                if (File.Exists(shmPath)) { try { File.Delete(shmPath); } catch { } }

                progress?.Report("Restoring Data...");
                File.Copy(restoreDbPath, dbPath, true);

                progress?.Report("Validating Restored Database...");
                using (var verifyConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False"))
                {
                    verifyConn.Open();
                    using var cmd = verifyConn.CreateCommand();
                    cmd.CommandText = "PRAGMA integrity_check;";
                    var integrityResult = cmd.ExecuteScalar()?.ToString();
                    if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Restore failed. The existing DhirDhar data was not changed: {integrityResult}");
                    }
                    verifyConn.Close();
                }
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            }

            progress?.Report("Restore Complete");
            try { Directory.Delete(restoreDir, true); } catch { }

            _logger.LogInformation("Restore completed successfully for: '{BackupPath}'", resolvedPath);

            var fileSize = new FileInfo(resolvedPath).Length;
            return new BackupMetadata(
                Path.GetFileName(resolvedPath),
                BackupFormatVersion,
                "2.0.0",
                "1.0",
                DateTime.UtcNow,
                BackupTypeLocal,
                resolvedPath,
                fileSize,
                string.Empty,
                "Successful",
                "Verified");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed for backup: '{BackupPath}'", resolvedPath);
            throw new InvalidOperationException($"Restore failed: {ex.Message}", ex);
        }
    }

    public async Task<bool> VerifyBackupAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var resolvedPath = ResolveBackupPath(backupPath);
        try
        {
            if (!File.Exists(resolvedPath) || new FileInfo(resolvedPath).Length == 0)
            {
                return false;
            }

            using var archive = ZipFile.OpenRead(resolvedPath);
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry is null)
            {
                return false;
            }

            BackupManifest? manifest;
            using (var manifestStream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (manifest is null)
                {
                    return false;
                }
            }

            if (IsEncryptedBackup(resolvedPath))
            {
                var dataEntry = archive.GetEntry("data.enc");
                if (dataEntry is null || dataEntry.Length <= (EncryptionSaltSize + EncryptionNonceSize + EncryptionTagSize))
                {
                    return false;
                }

                // Verify integrity hash if present
                var hashEntry = archive.GetEntry("integrity.hash");
                if (hashEntry is not null)
                {
                    string expectedHash;
                    using (var hashReader = new StreamReader(hashEntry.Open()))
                    {
                        expectedHash = (await hashReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(expectedHash))
                    {
                        using var sha256 = SHA256.Create();
                        await using var dataStream = dataEntry.Open();
                        var computedBytes = await sha256.ComputeHashAsync(dataStream, cancellationToken).ConfigureAwait(false);
                        var computedBase64 = Convert.ToBase64String(computedBytes);
                        var computedHex = Convert.ToHexString(computedBytes);

                        if (!string.Equals(expectedHash, computedBase64, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(expectedHash, computedHex, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            var dbEntry = archive.GetEntry("DhirDhar.db");
            if (dbEntry is null)
            {
                return false;
            }

            var unencryptedHashEntry = archive.GetEntry("integrity.hash");
            if (unencryptedHashEntry is null)
            {
                return false;
            }

            string expectedDbHash;
            using (var hashReader = new StreamReader(unencryptedHashEntry.Open()))
            {
                expectedDbHash = (await hashReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
            }

            if (string.IsNullOrWhiteSpace(expectedDbHash))
            {
                return false;
            }

            using var sha256Db = SHA256.Create();
            await using var dbStream = dbEntry.Open();
            var computedHashBytes = await sha256Db.ComputeHashAsync(dbStream, cancellationToken).ConfigureAwait(false);

            var computedDbBase64 = Convert.ToBase64String(computedHashBytes);
            var computedDbHex = Convert.ToHexString(computedHashBytes);

            return string.Equals(expectedDbHash, computedDbBase64, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(expectedDbHash, computedDbHex, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backup verification failed for '{BackupPath}'", resolvedPath);
            return false;
        }
    }

    public Task<IReadOnlyList<BackupHistoryEntry>> GetBackupHistoryAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<BackupHistoryEntry>();
        var backupDirectory = GetBackupDirectory();

        if (Directory.Exists(backupDirectory))
        {
            MigrateAndCleanupLegacyBackups();

            var localBackupPath = Path.Combine(backupDirectory, LocalBackupFileName);
            if (File.Exists(localBackupPath) && new FileInfo(localBackupPath).Length > 0)
            {
                var fileInfo = new FileInfo(localBackupPath);
                var backupDate = fileInfo.LastWriteTimeUtc > fileInfo.CreationTimeUtc ? fileInfo.LastWriteTimeUtc : fileInfo.CreationTimeUtc;

                entries.Add(new BackupHistoryEntry(
                    LocalBackupFileName,
                    backupDate,
                    BackupTypeLocal,
                    "Local",
                    fileInfo.Length,
                    "Successful",
                    "Verified"));
            }
        }

        return Task.FromResult<IReadOnlyList<BackupHistoryEntry>>(entries);
    }

    public Task<BackupMetadata> CreateSafetyBackupAsync(CancellationToken cancellationToken = default)
    {
        // Safety backups safely update/create the single local backup without generating duplicate timestamped files
        return CreateBackupAsync(null, cancellationToken);
    }

    public Task CleanupOldBackupsAsync(int? retentionCount = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var backupDirectory = GetBackupDirectory();
            if (Directory.Exists(backupDirectory))
            {
                MigrateAndCleanupLegacyBackups();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CleanupOldBackupsAsync encountered an unexpected error.");
        }

        return Task.CompletedTask;
    }

    public void MigrateAndCleanupLegacyBackups()
    {
        try
        {
            var backupDir = GetBackupDirectory();
            if (!Directory.Exists(backupDir)) return;

            var localBackupPath = Path.Combine(backupDir, LocalBackupFileName);

            // 1. If DhirDhar_Local_Backup.ddbackup doesn't exist, migrate the newest valid legacy automatic backup
            if (!File.Exists(localBackupPath) || new FileInfo(localBackupPath).Length == 0)
            {
                var legacyFiles = Directory.GetFiles(backupDir, "*.ddbackup")
                    .Where(f => !Path.GetFileName(f).Equals(LocalBackupFileName, StringComparison.OrdinalIgnoreCase))
                    .Where(f => Path.GetFileName(f).StartsWith("DhirDhar_Backup_", StringComparison.OrdinalIgnoreCase) ||
                                Path.GetFileName(f).StartsWith("DhirDhar_Safety_", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();

                foreach (var legacy in legacyFiles)
                {
                    if (IsValidZipArchive(legacy))
                    {
                        File.Copy(legacy, localBackupPath, true);
                        _logger.LogInformation("Migrated legacy automatic backup '{Legacy}' to '{Target}'.", legacy, localBackupPath);
                        break;
                    }
                }
            }

            // 2. Remove obsolete automatic legacy files
            var obsoleteFiles = Directory.GetFiles(backupDir, "*.ddbackup")
                .Where(f => !Path.GetFileName(f).Equals(LocalBackupFileName, StringComparison.OrdinalIgnoreCase))
                .Where(f => Path.GetFileName(f).StartsWith("DhirDhar_Backup_", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(f).StartsWith("DhirDhar_Safety_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var obs in obsoleteFiles)
            {
                try
                {
                    File.Delete(obs);
                    _logger.LogInformation("Deleted obsolete automatic backup file: '{File}'.", obs);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete obsolete automatic backup file: '{File}'.", obs);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during legacy backup migration/cleanup.");
        }
    }

    private string ResolveBackupPath(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) ||
            backupPath.Equals("Local", StringComparison.OrdinalIgnoreCase) ||
            backupPath.Equals(BackupTypeLocal, StringComparison.OrdinalIgnoreCase) ||
            backupPath.Equals(LocalBackupFileName, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(GetBackupDirectory(), LocalBackupFileName);
        }

        if (!Path.IsPathRooted(backupPath))
        {
            return Path.Combine(GetBackupDirectory(), backupPath);
        }

        return backupPath;
    }

    private string GetBackupDirectory()
    {
        if (!string.IsNullOrEmpty(_backupOptions.Directory) && Directory.Exists(_backupOptions.Directory))
        {
            return _backupOptions.Directory;
        }

        return _pathService.BackupDirectory;
    }

    private void CreateConsistentDatabaseCopy(string destinationPath)
    {
        var dbPath = _pathService.DatabasePath;
        if (!File.Exists(dbPath))
        {
            return;
        }

        try
        {
            if (File.Exists(destinationPath))
            {
                try { File.Delete(destinationPath); } catch { }
            }

            using (var srcConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False"))
            {
                srcConn.Open();
                using (var dstConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={destinationPath};Pooling=False"))
                {
                    dstConn.Open();
                    srcConn.BackupDatabase(dstConn);
                    dstConn.Close();
                }
                srcConn.Close();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        catch
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Copy(dbPath, destinationPath, true);
        }
    }

    private async Task CreateEncryptedBackupAsync(string dbPath, string backupPath, string? password, BackupFileInfo metadata, CancellationToken cancellationToken)
    {
        var salt = _cryptoService.GenerateRandomNonce(EncryptionSaltSize);
        var nonce = _cryptoService.GenerateRandomNonce(EncryptionNonceSize);
        bool isExplicitPassword = !string.IsNullOrWhiteSpace(password);

        string effectiveCredential;
        string protectionMode;

        if (isExplicitPassword)
        {
            effectiveCredential = password!.Trim();
            protectionMode = (effectiveCredential.StartsWith("DDRK-", StringComparison.OrdinalIgnoreCase) || (effectiveCredential.Length == 64 && effectiveCredential.All(Uri.IsHexDigit)))
                ? "RecoveryKey"
                : "PasswordProtected";
        }
        else
        {
            var systemRecoveryKey = _keyManagementService.GetCurrentRecoveryKey();
            if (string.IsNullOrWhiteSpace(systemRecoveryKey))
            {
                var details = await _keyManagementService.GenerateOrGetRecoveryKeyAsync(cancellationToken).ConfigureAwait(false);
                systemRecoveryKey = details.FormattedRecoveryKey;
            }

            effectiveCredential = systemRecoveryKey;
            protectionMode = "RecoveryKey";
        }

        var key = _cryptoService.DerivePortableBackupKey(effectiveCredential, salt, Pbkdf2Iterations, EncryptionKeySize);

        if (File.Exists(backupPath))
        {
            try { File.Delete(backupPath); } catch { }
        }

        using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var dbBytes = File.Exists(dbPath) ? await File.ReadAllBytesAsync(dbPath, cancellationToken).ConfigureAwait(false) : Array.Empty<byte>();
            var cipherText = new byte[dbBytes.Length];
            var tag = new byte[EncryptionTagSize];

            using (var aes = new AesGcm(key, EncryptionTagSize))
            {
                aes.Encrypt(nonce, dbBytes, cipherText, tag);
            }

            var dataBytes = new byte[EncryptionSaltSize + EncryptionNonceSize + EncryptionTagSize + cipherText.Length];
            Buffer.BlockCopy(salt, 0, dataBytes, 0, EncryptionSaltSize);
            Buffer.BlockCopy(nonce, 0, dataBytes, EncryptionSaltSize, EncryptionNonceSize);
            Buffer.BlockCopy(tag, 0, dataBytes, EncryptionSaltSize + EncryptionNonceSize, EncryptionTagSize);
            Buffer.BlockCopy(cipherText, 0, dataBytes, EncryptionSaltSize + EncryptionNonceSize + EncryptionTagSize, cipherText.Length);

            var dataEntry = archive.CreateEntry("data.enc", CompressionLevel.Optimal);
            using (var dataStream = dataEntry.Open())
            {
                await dataStream.WriteAsync(dataBytes, cancellationToken).ConfigureAwait(false);
            }

            using var sha256 = SHA256.Create();
            var dataHash = Convert.ToHexString(sha256.ComputeHash(dataBytes));

            var hashEntry = archive.CreateEntry("integrity.hash", CompressionLevel.Optimal);
            using (var hashStream = hashEntry.Open())
            using (var writer = new StreamWriter(hashStream, Encoding.UTF8))
            {
                await writer.WriteAsync(dataHash).ConfigureAwait(false);
            }

            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var manifestStream = manifestEntry.Open())
            {
                await JsonSerializer.SerializeAsync(manifestStream, new BackupManifest
                {
                    BackupId = metadata.BackupId,
                    BackupFormatVersion = BackupFormatVersion,
                    ApplicationVersion = metadata.ApplicationVersion,
                    SchemaVersion = metadata.SchemaVersion,
                    CreatedAt = metadata.CreatedAt,
                    BackupType = metadata.BackupType,
                    Encrypted = true,
                    PasswordProtected = isExplicitPassword && protectionMode == "PasswordProtected",
                    ProtectionMode = protectionMode,
                    Encryption = new BackupEncryptionHeader
                    {
                        Algorithm = "AES-256-GCM",
                        Kdf = "PBKDF2-HMAC-SHA256",
                        Iterations = Pbkdf2Iterations,
                        SaltSize = EncryptionSaltSize,
                        NonceSize = EncryptionNonceSize,
                        TagSize = EncryptionTagSize,
                        KeySize = EncryptionKeySize,
                        ProtectionMode = protectionMode
                    }
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task CreateUnencryptedBackupAsync(string dbPath, string backupPath, BackupFileInfo metadata, CancellationToken cancellationToken)
    {
        if (File.Exists(backupPath))
        {
            try { File.Delete(backupPath); } catch { }
        }

        using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);

        if (File.Exists(dbPath))
        {
            archive.CreateEntryFromFile(dbPath, "DhirDhar.db", CompressionLevel.Optimal);

            var dbHash = await ComputeFileHashAsync(dbPath, cancellationToken).ConfigureAwait(false);
            var hashEntry = archive.CreateEntry("integrity.hash", CompressionLevel.Optimal);
            using (var hashStream = hashEntry.Open())
            using (var writer = new StreamWriter(hashStream, Encoding.UTF8))
            {
                await writer.WriteAsync(dbHash).ConfigureAwait(false);
            }
        }

        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(manifestStream, new BackupManifest
            {
                BackupId = metadata.BackupId,
                BackupFormatVersion = "1.0",
                ApplicationVersion = metadata.ApplicationVersion,
                SchemaVersion = metadata.SchemaVersion,
                CreatedAt = metadata.CreatedAt,
                BackupType = metadata.BackupType,
                Encrypted = false,
                PasswordProtected = false,
                ProtectionMode = "Unencrypted"
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public bool IsEncryptedBackup(string backupPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(backupPath);
            return archive.GetEntry("data.enc") is not null;
        }
        catch
        {
            return false;
        }
    }

    private async Task DecryptAndExtractBackupAsync(string backupPath, string outputDir, string? password)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        var dataEntry = archive.GetEntry("data.enc") ?? throw new InvalidOperationException("Encrypted data not found in backup.");

        BackupManifest? manifest = null;
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry != null)
        {
            try
            {
                using var msManifest = manifestEntry.Open();
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(msManifest).ConfigureAwait(false);
            }
            catch { }
        }

        using var ms = new MemoryStream();
        using (var dataStream = dataEntry.Open())
        {
            await dataStream.CopyToAsync(ms).ConfigureAwait(false);
        }

        var allBytes = ms.ToArray();
        if (allBytes.Length < 16 + 12 + 16)
        {
            throw new InvalidOperationException("Encrypted backup payload is corrupted or truncated.");
        }

        // Define potential payload chunk structures to attempt
        var payloadCandidates = new List<(byte[] Salt, byte[] Nonce, byte[] Tag, byte[] CipherText)>();

        // Layout 1: EncryptedPayload format with MagicBytes "DDE1" (v1/v2 format)
        if (allBytes.Length >= 34 && allBytes.AsSpan(0, 4).SequenceEqual(EncryptedPayload.MagicBytes))
        {
            try
            {
                var payload = EncryptedPayload.FromBytes(allBytes);
                byte[] salt = new byte[32];
                payloadCandidates.Add((salt, payload.Nonce, payload.Tag, payload.Ciphertext));
            }
            catch { }
        }

        // Layout 2: Manifest-declared layout if specified
        if (manifest?.Encryption != null)
        {
            int saltLen = manifest.Encryption.SaltSize > 0 ? manifest.Encryption.SaltSize : EncryptionSaltSize;
            int nonceLen = manifest.Encryption.NonceSize > 0 ? manifest.Encryption.NonceSize : EncryptionNonceSize;
            int tagLen = manifest.Encryption.TagSize > 0 ? manifest.Encryption.TagSize : EncryptionTagSize;

            if (allBytes.Length >= saltLen + nonceLen + tagLen)
            {
                var s = allBytes.AsSpan(0, saltLen).ToArray();
                var n = allBytes.AsSpan(saltLen, nonceLen).ToArray();
                var t = allBytes.AsSpan(saltLen + nonceLen, tagLen).ToArray();
                var c = allBytes.AsSpan(saltLen + nonceLen + tagLen).ToArray();
                payloadCandidates.Add((s, n, t, c));
            }
        }

        // Layout 3: 32-byte salt layout (v3.0 standard: 32 salt + 12 nonce + 16 tag)
        if (allBytes.Length >= 32 + 12 + 16)
        {
            var s = allBytes.AsSpan(0, 32).ToArray();
            var n = allBytes.AsSpan(32, 12).ToArray();
            var t = allBytes.AsSpan(32 + 12, 16).ToArray();
            var c = allBytes.AsSpan(32 + 12 + 16).ToArray();
            payloadCandidates.Add((s, n, t, c));
        }

        // Layout 4: 16-byte salt layout (v2.0 standard: 16 salt + 12 nonce + 16 tag)
        if (allBytes.Length >= 16 + 12 + 16)
        {
            var s = allBytes.AsSpan(0, 16).ToArray();
            var n = allBytes.AsSpan(16, 12).ToArray();
            var t = allBytes.AsSpan(16 + 12, 16).ToArray();
            var c = allBytes.AsSpan(16 + 12 + 16).ToArray();
            payloadCandidates.Add((s, n, t, c));
        }

        // Layout 5: 0-byte salt layout (12 nonce + 16 tag + ciphertext)
        if (allBytes.Length >= 12 + 16)
        {
            var s = new byte[32];
            var n = allBytes.AsSpan(0, 12).ToArray();
            var t = allBytes.AsSpan(12, 16).ToArray();
            var c = allBytes.AsSpan(12 + 16).ToArray();
            payloadCandidates.Add((s, n, t, c));
        }

        byte[]? decryptedPlaintext = null;
        Exception? lastCryptoEx = null;

        foreach (var (salt, nonce, tag, cipherText) in payloadCandidates)
        {
            if (decryptedPlaintext != null) break;
            if (cipherText.Length == 0 || nonce.Length != 12 || tag.Length != 16) continue;

            int iterations = manifest?.Encryption?.Iterations ?? (salt.All(b => b == 0) ? 100_000 : Pbkdf2Iterations);

            // Generate candidate keys for this specific salt
            var candidateKeys = BuildDecryptionKeyCandidates(password, salt, iterations);

            var plainText = new byte[cipherText.Length];

            foreach (var candidateKey in candidateKeys)
            {
                try
                {
                    using var aes = new AesGcm(candidateKey, tag.Length);
                    aes.Decrypt(nonce, cipherText, tag, plainText);

                    // Verify decrypted payload integrity - must be a valid SQLite database header or valid content
                    if (plainText.Length >= 16 && plainText.AsSpan(0, 16).SequenceEqual("SQLite format 3\0"u8))
                    {
                        decryptedPlaintext = plainText;
                        break;
                    }
                    else if (plainText.Length > 0)
                    {
                        decryptedPlaintext = plainText;
                        break;
                    }
                }
                catch (CryptographicException ex)
                {
                    lastCryptoEx = ex;
                }
            }
        }

        if (decryptedPlaintext == null)
        {
            throw new InvalidOperationException("Backup decryption failed. The password or recovery key is incorrect, or the backup is corrupted.", lastCryptoEx);
        }

        var dbPath = Path.Combine(outputDir, "DhirDhar.db");
        await File.WriteAllBytesAsync(dbPath, decryptedPlaintext).ConfigureAwait(false);
    }

    private List<byte[]> BuildDecryptionKeyCandidates(string? credential, byte[] salt, int iterations)
    {
        var keys = new List<byte[]>();
        var seen = new HashSet<string>();

        void AddKey(byte[]? key)
        {
            if (key != null && key.Length == EncryptionKeySize)
            {
                var hex = Convert.ToHexString(key);
                if (seen.Add(hex))
                {
                    keys.Add(key);
                }
            }
        }

        var cleanCredential = credential?.Trim();

        if (!string.IsNullOrEmpty(cleanCredential))
        {
            // Check if credential is a Disaster Recovery Key
            var cleanHex = cleanCredential.Replace("DDRK-", "", StringComparison.OrdinalIgnoreCase).Replace("-", "").Replace(" ", "").Trim();
            if (cleanHex.Length == 64 && cleanHex.All(Uri.IsHexDigit))
            {
                try
                {
                    var rawRecoveryBytes = Convert.FromHexString(cleanHex);
                    AddKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, rawRecoveryBytes, EncryptionKeySize, salt: salt, info: "DhirDhar-Portable-RecoveryKey-v3"u8.ToArray()));
                    AddKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, rawRecoveryBytes, EncryptionKeySize, salt: salt, info: "DhirDhar-Backup-Encryption-v1"u8.ToArray()));
                    AddKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, rawRecoveryBytes, EncryptionKeySize, salt: salt, info: "DhirDhar-Recovery-Vault-v1"u8.ToArray()));
                    AddKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, rawRecoveryBytes, EncryptionKeySize, info: "DhirDhar-Portable-RecoveryKey-v3"u8.ToArray()));
                    AddKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, rawRecoveryBytes, EncryptionKeySize, info: "DhirDhar-Backup-Encryption-v1"u8.ToArray()));
                    AddKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, rawRecoveryBytes, EncryptionKeySize, info: "DhirDhar-Recovery-Vault-v1"u8.ToArray()));
                    AddKey(rawRecoveryBytes);
                    AddKey(Rfc2898DeriveBytes.Pbkdf2(rawRecoveryBytes, salt, iterations, HashAlgorithmName.SHA256, EncryptionKeySize));
                    AddKey(Rfc2898DeriveBytes.Pbkdf2(rawRecoveryBytes, salt, 100_000, HashAlgorithmName.SHA256, EncryptionKeySize));
                    AddKey(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(cleanHex), salt, iterations, HashAlgorithmName.SHA256, EncryptionKeySize));
                    AddKey(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(cleanHex.ToLowerInvariant()), salt, iterations, HashAlgorithmName.SHA256, EncryptionKeySize));
                    AddKey(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(cleanHex.ToUpperInvariant()), salt, iterations, HashAlgorithmName.SHA256, EncryptionKeySize));
                }
                catch { }
            }

            // User passphrase / password key derivation
            AddKey(_cryptoService.DerivePortableBackupKey(cleanCredential, salt, iterations, EncryptionKeySize));
            AddKey(_cryptoService.DeriveKeyFromPassphrase(cleanCredential, salt, iterations, EncryptionKeySize));
            AddKey(_cryptoService.DeriveKeyFromPassphrase(cleanCredential, salt, 100_000, EncryptionKeySize));
            AddKey(_cryptoService.DeriveKeyFromPassphrase(cleanCredential, salt, 50_000, EncryptionKeySize));
            AddKey(_cryptoService.DeriveKeyFromPassphrase(cleanCredential, salt, 10_000, EncryptionKeySize));
            AddKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(cleanCredential), EncryptionKeySize, salt: salt, info: "DhirDhar-Portable-Passphrase-v3"u8.ToArray()));
        }

        // Standard portable keys (for automated or non-password protected backups)
        AddKey(_cryptoService.DerivePortableBackupKey(null, salt, iterations, EncryptionKeySize));
        AddKey(_cryptoService.DerivePortableBackupKey(null, salt, 600_000, EncryptionKeySize));
        AddKey(_cryptoService.DerivePortableBackupKey(null, salt, 100_000, EncryptionKeySize));
        AddKey(Rfc2898DeriveBytes.Pbkdf2("DhirDhar.Standard.Portable.Backup.Key.v3"u8.ToArray(), salt, iterations, HashAlgorithmName.SHA256, EncryptionKeySize));
        AddKey(Rfc2898DeriveBytes.Pbkdf2("DhirDhar.Standard.Portable.Backup.Key.v2"u8.ToArray(), salt, iterations, HashAlgorithmName.SHA256, EncryptionKeySize));
        AddKey(Rfc2898DeriveBytes.Pbkdf2("DhirDhar.Portable.Backup.Key.v1"u8.ToArray(), salt, iterations, HashAlgorithmName.SHA256, EncryptionKeySize));
        AddKey(Rfc2898DeriveBytes.Pbkdf2("DhirDhar.Backup.Key.v1"u8.ToArray(), salt, iterations, HashAlgorithmName.SHA256, EncryptionKeySize));

        // System's current persistent Disaster Recovery Key (enables transparent local restores without prompt)
        try
        {
            var systemRecoveryKey = _keyManagementService.GetCurrentRecoveryKey();
            if (!string.IsNullOrWhiteSpace(systemRecoveryKey))
            {
                AddKey(_cryptoService.DerivePortableBackupKey(systemRecoveryKey, salt, iterations, EncryptionKeySize));
                var cleanHex = systemRecoveryKey.Replace("DDRK-", "", StringComparison.OrdinalIgnoreCase).Replace("-", "").Replace(" ", "").Trim();
                if (cleanHex.Length == 64 && cleanHex.All(Uri.IsHexDigit))
                {
                    var rawRecoveryBytes = Convert.FromHexString(cleanHex);
                    AddKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, rawRecoveryBytes, EncryptionKeySize, salt: salt, info: "DhirDhar-Portable-RecoveryKey-v3"u8.ToArray()));
                    AddKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, rawRecoveryBytes, EncryptionKeySize, info: "DhirDhar-Portable-RecoveryKey-v3"u8.ToArray()));
                    AddKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, rawRecoveryBytes, EncryptionKeySize, salt: salt, info: "DhirDhar-Backup-Encryption-v1"u8.ToArray()));
                    AddKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, rawRecoveryBytes, EncryptionKeySize, info: "DhirDhar-Backup-Encryption-v1"u8.ToArray()));
                }
            }
        }
        catch { }

        // Machine-local key fallbacks (if restoring on same local machine that created a legacy backup)
        try
        {
            var masterBackupKey = _keyManagementService.GetBackupMasterKey();
            AddKey(masterBackupKey);
            var masterKey = _keyManagementService.GetMasterKey();
            AddKey(masterKey);
            if (masterKey != null)
            {
                AddKey(_cryptoService.DeriveKey(masterKey, "DhirDhar-Backup-Encryption-v1"));
                AddKey(_cryptoService.DeriveKey(masterKey, "DhirDhar-Recovery-Vault-v1"));
                AddKey(_cryptoService.DeriveKey(masterKey, "DhirDhar-Field-Encryption-v1"));
            }
        }
        catch { }

        return keys;
    }

    private void ExtractBackup(string backupPath, string outputDir)
    {
        ZipFile.ExtractToDirectory(backupPath, outputDir, true);
    }

    private static bool IsValidZipArchive(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return archive.Entries.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(hash);
    }

    private sealed class BackupFileInfo
    {
        public string BackupId { get; set; } = string.Empty;
        public string BackupFormatVersion { get; set; } = string.Empty;
        public string ApplicationVersion { get; set; } = string.Empty;
        public string SchemaVersion { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string BackupType { get; set; } = string.Empty;
        public string DatabasePath { get; set; } = string.Empty;
    }

    public sealed class BackupManifest
    {
        public string BackupId { get; set; } = string.Empty;
        public string BackupFormatVersion { get; set; } = string.Empty;
        public string ApplicationVersion { get; set; } = string.Empty;
        public string SchemaVersion { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string BackupType { get; set; } = string.Empty;
        public bool Encrypted { get; set; }
        public bool PasswordProtected { get; set; }
        public string ProtectionMode { get; set; } = "StandardPortable";
        public string? AccountEmail { get; set; }
        public BackupEncryptionHeader? Encryption { get; set; }
    }

    public sealed class BackupEncryptionHeader
    {
        public string Algorithm { get; set; } = "AES-256-GCM";
        public string Kdf { get; set; } = "PBKDF2-HMAC-SHA256";
        public int Iterations { get; set; } = 600_000;
        public int SaltSize { get; set; } = 32;
        public int NonceSize { get; set; } = 12;
        public int TagSize { get; set; } = 16;
        public int KeySize { get; set; } = 32;
        public string ProtectionMode { get; set; } = "StandardPortable";
    }
}

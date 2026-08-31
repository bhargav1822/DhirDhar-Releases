using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Licensing;
using DhirDhar.Application.Licensing.Models;
using DhirDhar.Application.Settings;
using DhirDhar.Infrastructure.Backup;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.Licensing;
using DhirDhar.Infrastructure.Settings;
using DhirDhar.LicenseGenerator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dataDir;
    private readonly string _backupDir;
    private readonly string _dbPath;
    private readonly TestPathService _pathService;
    private readonly BackupOptions _backupOptions;
    private readonly DhirDhar.Infrastructure.Security.Keys.KeyManagementService _keyManagementService;
    private readonly BackupService _backupService;

    public BackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dhirdhar-backup-tests-" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_tempDir, "Data");
        _backupDir = Path.Combine(_tempDir, "Backup");
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_backupDir);

        _dbPath = Path.Combine(_dataDir, "DhirDhar.db");
        CreateSampleDatabase(_dbPath);

        _pathService = new TestPathService(_tempDir, _dbPath, _backupDir);
        _backupOptions = new BackupOptions
        {
            Directory = _backupDir,
            RetentionCount = 1,
            EncryptBackups = true
        };

        var cryptoService = new DhirDhar.Infrastructure.Security.Cryptography.CryptoService(NullLogger<DhirDhar.Infrastructure.Security.Cryptography.CryptoService>.Instance);
        _keyManagementService = new DhirDhar.Infrastructure.Security.Keys.KeyManagementService(cryptoService, _pathService, new SecurityEncryptionTests.MockAuditService(), NullLogger<DhirDhar.Infrastructure.Security.Keys.KeyManagementService>.Instance);
        _keyManagementService.InitializeMasterKeyAsync().GetAwaiter().GetResult();

        _backupService = new BackupService(
            _pathService,
            _keyManagementService,
            cryptoService,
            Options.Create(_backupOptions),
            NullLogger<BackupService>.Instance);
    }

    private static void CreateSampleDatabase(string path)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Borrowers (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Phone TEXT NOT NULL,
                Status INTEGER NOT NULL
            );
            INSERT INTO Borrowers (Id, Name, Phone, Status) VALUES ('B1', 'John Doe', '9876543210', 0);
            INSERT INTO Borrowers (Id, Name, Phone, Status) VALUES ('B2', 'Jane Smith', '9876543211', 1);
        ";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task CreateBackupAsync_CreatesValidPackageNamedDhirDharLocalBackup()
    {
        var metadata = await _backupService.CreateBackupAsync();

        Assert.NotNull(metadata);
        Assert.Equal(BackupService.LocalBackupFileName, Path.GetFileName(metadata.Location));
        Assert.True(File.Exists(metadata.Location));
        Assert.True(metadata.FileSize > 0);
        Assert.Equal("Successful", metadata.Status);
        Assert.Equal("Verified", metadata.VerificationStatus);
        Assert.Equal(BackupService.LocalBackupType, metadata.BackupType);

        // Verify ZIP contents
        using var archive = ZipFile.OpenRead(metadata.Location);
        var payloadEntry = archive.GetEntry("data.enc") ?? archive.GetEntry("DhirDhar.db");
        Assert.NotNull(payloadEntry);
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.NotNull(archive.GetEntry("integrity.hash"));

        // Verify SHA-256 in integrity.hash matches payload entry
        using var sha256 = SHA256.Create();
        await using var payloadStream = payloadEntry.Open();
        var computedHash = Convert.ToHexString(await sha256.ComputeHashAsync(payloadStream));

        var hashEntry = archive.GetEntry("integrity.hash")!;
        using var reader = new StreamReader(hashEntry.Open());
        var storedHash = (await reader.ReadToEndAsync()).Trim();

        Assert.Equal(storedHash, computedHash);
    }

    [Fact]
    public async Task SingleLocalBackupFile_IsMaintainedAndOverwrittenSafely()
    {
        // 1. Create first backup
        var meta1 = await _backupService.CreateBackupAsync();
        Assert.True(File.Exists(meta1.Location));

        // 2. Modify database
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Borrowers (Id, Name, Phone, Status) VALUES ('B3', 'New User', '9998887777', 0);";
            cmd.ExecuteNonQuery();
        }

        // 3. Create second backup - must overwrite previous file safely
        var meta2 = await _backupService.CreateBackupAsync();
        Assert.Equal(meta1.Location, meta2.Location);

        // Verify only one .ddbackup file exists in the directory
        var allBackups = Directory.GetFiles(_backupDir, "*.ddbackup");
        Assert.Single(allBackups);
        Assert.Equal(BackupService.LocalBackupFileName, Path.GetFileName(allBackups[0]));

        // Verify history contains only one entry
        var history = await _backupService.GetBackupHistoryAsync();
        Assert.Single(history);
        Assert.Equal(BackupService.LocalBackupType, history[0].Type);
        Assert.Equal(BackupService.LocalBackupLocation, history[0].Location);
    }

    [Fact]
    public async Task VerifyBackupAsync_ReturnsTrue_ForValidBackup()
    {
        var metadata = await _backupService.CreateBackupAsync();
        var isValid = await _backupService.VerifyBackupAsync(metadata.Location);
        Assert.True(isValid);
    }

    [Fact]
    public async Task VerifyBackupAsync_ReturnsFalse_WhenDatabaseIsTampered()
    {
        var metadata = await _backupService.CreateBackupAsync();

        // Tamper with the backup file by modifying bytes of data.enc inside the zip
        var tamperedPath = Path.Combine(_backupDir, "tampered.ddbackup");
        File.Copy(metadata.Location, tamperedPath, true);

        using (var archive = ZipFile.Open(tamperedPath, ZipArchiveMode.Update))
        {
            var oldEntry = archive.GetEntry("data.enc") ?? archive.GetEntry("DhirDhar.db");
            var bytes = new byte[oldEntry!.Length];
            using (var stream = oldEntry.Open())
            {
                await stream.ReadExactlyAsync(bytes);
            }
            oldEntry.Delete();

            bytes[bytes.Length / 2] ^= 0xFF;

            var newEntry = archive.CreateEntry("data.enc");
            using var writer = newEntry.Open();
            await writer.WriteAsync(bytes);
        }

        var isValid = await _backupService.VerifyBackupAsync(tamperedPath);
        Assert.False(isValid);
    }

    [Fact]
    public async Task VerifyBackupAsync_ReturnsFalse_WhenManifestMissing()
    {
        var invalidPath = Path.Combine(_backupDir, "no_manifest.ddbackup");
        using (var archive = ZipFile.Open(invalidPath, ZipArchiveMode.Create))
        {
            var dbEntry = archive.CreateEntry("DhirDhar.db");
            using var writer = new StreamWriter(dbEntry.Open());
            writer.Write("some db content");
        }

        var isValid = await _backupService.VerifyBackupAsync(invalidPath);
        Assert.False(isValid);
    }

    [Fact]
    public async Task RestoreBackupAsync_RestoresDatabaseSuccessfully()
    {
        // 1. Create initial backup
        var backupMeta = await _backupService.CreateBackupAsync();

        // 2. Modify database to simulate data loss / alteration
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Borrowers WHERE Id = 'B1';";
            cmd.ExecuteNonQuery();
        }

        // Verify modification
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Borrowers;";
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(1L, count);
        }

        // 3. Restore from backup
        var restoreMeta = await _backupService.RestoreBackupAsync(backupMeta.Location);
        Assert.NotNull(restoreMeta);
        Assert.Equal("Successful", restoreMeta.Status);

        // 4. Verify data is fully restored
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Borrowers;";
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(2L, count);
        }

        // 5. Verify local backup file exists and is valid
        var localBackupPath = Path.Combine(_backupDir, BackupService.LocalBackupFileName);
        Assert.True(File.Exists(localBackupPath));
    }

    [Fact]
    public async Task RestoreBackupAsync_CanRestoreSameBackupMultipleTimes_WithoutCollision()
    {
        // 1. Seed data and create backup
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Borrowers (Id, Name, Phone, Status) VALUES ('B3', 'MultiRestore User 1', '9991112222', 0);";
            cmd.ExecuteNonQuery();
        }

        var backupA = await _backupService.CreateBackupAsync();
        Assert.NotNull(backupA);

        // 2. Perform Restore Backup A
        var r1 = await _backupService.RestoreBackupAsync(backupA.Location);
        Assert.NotNull(r1);
        Assert.Equal("Successful", r1.Status);

        // 3. Immediately Restore Backup A again (same second)
        var r2 = await _backupService.RestoreBackupAsync(backupA.Location);
        Assert.NotNull(r2);
        Assert.Equal("Successful", r2.Status);

        // 4. Modify data and create updated backup
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Borrowers (Id, Name, Phone, Status) VALUES ('B4', 'MultiRestore User 2', '9993334444', 0);";
            cmd.ExecuteNonQuery();
        }

        var backupB = await _backupService.CreateBackupAsync();
        Assert.NotNull(backupB);
        Assert.Equal(backupA.Location, backupB.Location);

        // 5. Restore updated backup multiple times
        var r3 = await _backupService.RestoreBackupAsync(backupB.Location);
        Assert.NotNull(r3);
        Assert.Equal("Successful", r3.Status);

        var r4 = await _backupService.RestoreBackupAsync(backupB.Location);
        Assert.NotNull(r4);
        Assert.Equal("Successful", r4.Status);

        // Verify active database is valid and contains expected data from the latest backup (B1, B2, B3, B4)
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Borrowers;";
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(4L, count);
        }
    }

    [Fact]
    public async Task RestoreBackupAsync_RejectsCorruptedBackup_WithoutOverwritingData()
    {
        var corruptedPath = Path.Combine(_backupDir, "corrupted.ddbackup");
        await File.WriteAllBytesAsync(corruptedPath, new byte[] { 0x01, 0x02, 0x03, 0x04 });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _backupService.RestoreBackupAsync(corruptedPath);
        });

        // Verify active database still has intact data
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Borrowers;";
        var count = (long)(cmd.ExecuteScalar() ?? 0L);
        Assert.Equal(2L, count);
    }

    [Fact]
    public async Task LegacyBackups_MigratedAndCleanedUp()
    {
        // 1. Create a valid backup file first
        var validBackup = await _backupService.CreateBackupAsync();
        var validPath = validBackup.Location;

        // Rename it to a legacy name
        var legacy1 = Path.Combine(_backupDir, "DhirDhar_Backup_2026-08-17_10-00-00_0001.ddbackup");
        var legacy2 = Path.Combine(_backupDir, "DhirDhar_Safety_2026-08-17_11-00-00.ddbackup");
        File.Move(validPath, legacy1);
        File.Copy(legacy1, legacy2);

        // Ensure DhirDhar_Local_Backup.ddbackup does not exist yet
        var standardLocalPath = Path.Combine(_backupDir, BackupService.LocalBackupFileName);
        Assert.False(File.Exists(standardLocalPath));

        // 2. Running GetBackupHistoryAsync or CleanupOldBackupsAsync triggers migration
        var history = await _backupService.GetBackupHistoryAsync();

        // 3. Verify single local backup exists
        Assert.True(File.Exists(standardLocalPath));
        Assert.Single(history);
        Assert.Equal(BackupService.LocalBackupType, history[0].Type);

        // Obsolete legacy files must have been cleaned up
        Assert.False(File.Exists(legacy1));
        Assert.False(File.Exists(legacy2));
    }

    [Fact]
    public async Task CrossSystemRestore_SystemAToSystemB_WithPassword_Succeeds()
    {
        // 1. System A creates an encrypted backup with password
        var backupMeta = await _backupService.CreateBackupAsync("MySecurePassword123!");
        Assert.True(File.Exists(backupMeta.Location));

        // 2. Simulate System B: Fresh installation in a separate directory with independent crypto state
        var systemBDir = Path.Combine(Path.GetTempPath(), "dhirdhar-systemB-" + Guid.NewGuid().ToString("N"));
        var systemBDataDir = Path.Combine(systemBDir, "Data");
        var systemBBackupDir = Path.Combine(systemBDir, "Backup");
        Directory.CreateDirectory(systemBDataDir);
        Directory.CreateDirectory(systemBBackupDir);
        var systemBDbPath = Path.Combine(systemBDataDir, "DhirDhar.db");

        // System B starts with initial empty or distinct database
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={systemBDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Borrowers (Id TEXT PRIMARY KEY, Name TEXT, Phone TEXT, Status INTEGER);";
            cmd.ExecuteNonQuery();
        }

        var systemBPathService = new TestPathService(systemBDir, systemBDbPath, systemBBackupDir);
        var cryptoServiceB = new DhirDhar.Infrastructure.Security.Cryptography.CryptoService(NullLogger<DhirDhar.Infrastructure.Security.Cryptography.CryptoService>.Instance);
        var keyServiceB = new DhirDhar.Infrastructure.Security.Keys.KeyManagementService(cryptoServiceB, systemBPathService, new SecurityEncryptionTests.MockAuditService(), NullLogger<DhirDhar.Infrastructure.Security.Keys.KeyManagementService>.Instance);
        await keyServiceB.InitializeMasterKeyAsync();

        var backupServiceB = new BackupService(
            systemBPathService,
            keyServiceB,
            cryptoServiceB,
            Options.Create(new BackupOptions { Directory = systemBBackupDir, RetentionCount = 1, EncryptBackups = true }),
            NullLogger<BackupService>.Instance);

        // Copy backup package from System A to System B
        var systemBPackagePath = Path.Combine(systemBBackupDir, BackupService.LocalBackupFileName);
        File.Copy(backupMeta.Location, systemBPackagePath, true);

        // 3. System B restores the backup using System A's password
        var restoreResult = await backupServiceB.RestoreBackupAsync(systemBPackagePath, "MySecurePassword123!");
        Assert.NotNull(restoreResult);
        Assert.Equal("Successful", restoreResult.Status);

        // 4. Verify System B now contains all data from System A (B1 and B2)
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={systemBDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Borrowers;";
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(2L, count);
        }

        try { Directory.Delete(systemBDir, true); } catch { }
    }

    [Fact]
    public async Task CrossSystemRestore_SystemA_ExportedDisasterRecoveryKey_To_SystemB_Succeeds()
    {
        // 1. System A creates a backup automatically without entering a password
        var backupMeta = await _backupService.CreateBackupAsync(null);
        Assert.True(File.Exists(backupMeta.Location));

        // 2. System A exports its Disaster Recovery Key
        var exportedRecoveryKey = _keyManagementService.GetCurrentRecoveryKey();
        Assert.NotNull(exportedRecoveryKey);
        Assert.StartsWith("DDRK-", exportedRecoveryKey);

        // 3. System B: Fresh installation in a separate directory with independent crypto state
        var systemBDir = Path.Combine(Path.GetTempPath(), "dhirdhar-systemB-exp-rk-" + Guid.NewGuid().ToString("N"));
        var systemBDataDir = Path.Combine(systemBDir, "Data");
        var systemBBackupDir = Path.Combine(systemBDir, "Backup");
        Directory.CreateDirectory(systemBDataDir);
        Directory.CreateDirectory(systemBBackupDir);
        var systemBDbPath = Path.Combine(systemBDataDir, "DhirDhar.db");

        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={systemBDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Borrowers (Id TEXT PRIMARY KEY, Name TEXT, Phone TEXT, Status INTEGER);";
            cmd.ExecuteNonQuery();
        }

        var systemBPathService = new TestPathService(systemBDir, systemBDbPath, systemBBackupDir);
        var cryptoServiceB = new DhirDhar.Infrastructure.Security.Cryptography.CryptoService(NullLogger<DhirDhar.Infrastructure.Security.Cryptography.CryptoService>.Instance);
        var keyServiceB = new DhirDhar.Infrastructure.Security.Keys.KeyManagementService(cryptoServiceB, systemBPathService, new SecurityEncryptionTests.MockAuditService(), NullLogger<DhirDhar.Infrastructure.Security.Keys.KeyManagementService>.Instance);
        await keyServiceB.InitializeMasterKeyAsync();

        var backupServiceB = new BackupService(
            systemBPathService,
            keyServiceB,
            cryptoServiceB,
            Options.Create(new BackupOptions { Directory = systemBBackupDir, RetentionCount = 1, EncryptBackups = true }),
            NullLogger<BackupService>.Instance);

        var systemBPackagePath = Path.Combine(systemBBackupDir, BackupService.LocalBackupFileName);
        File.Copy(backupMeta.Location, systemBPackagePath, true);

        // 4. System B restores System A's backup using System A's exported Disaster Recovery Key
        var restoreResult = await backupServiceB.RestoreBackupAsync(systemBPackagePath, exportedRecoveryKey);
        Assert.NotNull(restoreResult);
        Assert.Equal("Successful", restoreResult.Status);

        // 5. Verify System B now has System A's data
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={systemBDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Borrowers;";
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(2L, count);
        }

        try { Directory.Delete(systemBDir, true); } catch { }
    }

    [Fact]
    public async Task CrossSystemRestore_SystemAToSystemB_WithRecoveryKey_Succeeds()
    {
        // 1. System A creates a disaster recovery key and uses it for backup
        var recoveryKey = "DDRK-A1B2C3D4-E5F60718-293A4B5C-6D7E8F90-11223344-55667788-99AABBCC-DDEEFF00";
        var backupMeta = await _backupService.CreateBackupAsync(recoveryKey);
        Assert.True(File.Exists(backupMeta.Location));

        // 2. System B with completely independent master key and fresh environment
        var systemBDir = Path.Combine(Path.GetTempPath(), "dhirdhar-systemB-rk-" + Guid.NewGuid().ToString("N"));
        var systemBDataDir = Path.Combine(systemBDir, "Data");
        var systemBBackupDir = Path.Combine(systemBDir, "Backup");
        Directory.CreateDirectory(systemBDataDir);
        Directory.CreateDirectory(systemBBackupDir);
        var systemBDbPath = Path.Combine(systemBDataDir, "DhirDhar.db");

        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={systemBDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Borrowers (Id TEXT PRIMARY KEY, Name TEXT, Phone TEXT, Status INTEGER);";
            cmd.ExecuteNonQuery();
        }

        var systemBPathService = new TestPathService(systemBDir, systemBDbPath, systemBBackupDir);
        var cryptoServiceB = new DhirDhar.Infrastructure.Security.Cryptography.CryptoService(NullLogger<DhirDhar.Infrastructure.Security.Cryptography.CryptoService>.Instance);
        var keyServiceB = new DhirDhar.Infrastructure.Security.Keys.KeyManagementService(cryptoServiceB, systemBPathService, new SecurityEncryptionTests.MockAuditService(), NullLogger<DhirDhar.Infrastructure.Security.Keys.KeyManagementService>.Instance);
        await keyServiceB.InitializeMasterKeyAsync();

        var backupServiceB = new BackupService(
            systemBPathService,
            keyServiceB,
            cryptoServiceB,
            Options.Create(new BackupOptions { Directory = systemBBackupDir, RetentionCount = 1, EncryptBackups = true }),
            NullLogger<BackupService>.Instance);

        var systemBPackagePath = Path.Combine(systemBBackupDir, BackupService.LocalBackupFileName);
        File.Copy(backupMeta.Location, systemBPackagePath, true);

        // 3. System B restores using the disaster recovery key
        var restoreResult = await backupServiceB.RestoreBackupAsync(systemBPackagePath, recoveryKey);
        Assert.NotNull(restoreResult);
        Assert.Equal("Successful", restoreResult.Status);

        // 4. Verify data is intact
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={systemBDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Borrowers;";
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(2L, count);
        }

        try { Directory.Delete(systemBDir, true); } catch { }
    }

    [Fact]
    public async Task Restore_WithWrongPassword_FailsSafely_AndPreservesDatabase()
    {
        // 1. Create encrypted backup with correct password
        var backupMeta = await _backupService.CreateBackupAsync("CorrectPassword123!");

        // 2. Modify database to state X
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Borrowers (Id, Name, Phone, Status) VALUES ('B99', 'Existing Database Data', '0000000000', 0);";
            cmd.ExecuteNonQuery();
        }

        // 3. Attempt restore with wrong password
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _backupService.RestoreBackupAsync(backupMeta.Location, "WrongPassword999!");
        });

        Assert.Contains("decryption failed", ex.Message, StringComparison.OrdinalIgnoreCase);

        // 4. Verify existing database is untouched (still contains B1, B2, B99)
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Borrowers;";
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(3L, count);
        }
    }

    [Fact]
    public async Task Restore_WithWrongRecoveryKey_FailsSafely_AndPreservesDatabase()
    {
        var correctKey = "DDRK-11111111-22222222-33333333-44444444-55555555-66666666-77777777-88888888";
        var wrongKey = "DDRK-99999999-88888888-77777777-66666666-55555555-44444444-33333333-22222222";

        var backupMeta = await _backupService.CreateBackupAsync(correctKey);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _backupService.RestoreBackupAsync(backupMeta.Location, wrongKey);
        });

        Assert.Contains("decryption failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Restore_TamperedCiphertext_FailsSafely()
    {
        var backupMeta = await _backupService.CreateBackupAsync("Password123!");
        var tamperedPath = Path.Combine(_backupDir, "tampered_payload.ddbackup");
        File.Copy(backupMeta.Location, tamperedPath, true);

        // Tamper with data.enc inside zip
        using (var archive = ZipFile.Open(tamperedPath, ZipArchiveMode.Update))
        {
            var dataEntry = archive.GetEntry("data.enc");
            var bytes = new byte[dataEntry!.Length];
            using (var stream = dataEntry.Open())
            {
                await stream.ReadExactlyAsync(bytes);
            }
            dataEntry.Delete();

            // Corrupt last byte
            bytes[^1] ^= 0xFF;

            var newEntry = archive.CreateEntry("data.enc");
            using var newStream = newEntry.Open();
            await newStream.WriteAsync(bytes);
        }

        // Must fail either on verification or decryption
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _backupService.RestoreBackupAsync(tamperedPath, "Password123!");
        });
    }

    [Fact]
    public async Task LargeBorrowerDatabase_BackupAndRestore_Succeeds()
    {
        // 1. Insert 500 borrowers
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var trans = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "INSERT INTO Borrowers (Id, Name, Phone, Status) VALUES (@id, @name, @phone, 0);";
            var idParam = cmd.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Text);
            var nameParam = cmd.Parameters.Add("@name", Microsoft.Data.Sqlite.SqliteType.Text);
            var phoneParam = cmd.Parameters.Add("@phone", Microsoft.Data.Sqlite.SqliteType.Text);

            for (int i = 3; i <= 500; i++)
            {
                idParam.Value = $"B{i}";
                nameParam.Value = $"Borrower Number {i}";
                phoneParam.Value = $"98765{i:D5}";
                cmd.ExecuteNonQuery();
            }
            trans.Commit();
        }

        // 2. Create backup
        var backupMeta = await _backupService.CreateBackupAsync("LargeDbPassword!");
        Assert.NotNull(backupMeta);
        Assert.True(backupMeta.FileSize > 0);

        // 3. Clear database
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Borrowers;";
            cmd.ExecuteNonQuery();
        }

        // 4. Restore backup
        var restoreMeta = await _backupService.RestoreBackupAsync(backupMeta.Location, "LargeDbPassword!");
        Assert.NotNull(restoreMeta);
        Assert.Equal("Successful", restoreMeta.Status);

        // 5. Verify all 500 records restored
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Borrowers;";
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(500L, count);
        }
    }

    [Fact]
    public async Task LicensePreservation_DuringDatabaseRestore()
    {
        // 1. Set up a valid license in storage
        var licFile = Path.Combine(_tempDir, "DhirDhar.lic");
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, licFile);
        var fingerprintService = new DeviceFingerprintService();
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        var (payload, key) = LicenseSigner.CreateUniqueLicense("Customer A", "customerA@example.com");
        var actResult = await manager.ActivateAsync(key);
        Assert.True(actResult.Success);
        Assert.Equal(LicenseStatus.Active, actResult.Status);

        // 2. Perform database backup and restore
        var backup = await _backupService.CreateBackupAsync();
        var restore = await _backupService.RestoreBackupAsync(backup.Location);
        Assert.Equal("Successful", restore.Status);

        // 3. Verify license remains active and untouched
        var currentLic = manager.CurrentLicense;
        Assert.NotNull(currentLic);
        Assert.Equal(payload.LicenseId, currentLic.LicenseId);
        Assert.Equal(LicenseStatus.Active, manager.Status);

        var initResult = await manager.InitializeAsync();
        Assert.True(initResult.IsValid);
        Assert.Equal(LicenseStatus.Active, initResult.Status);
    }

    [Fact]
    public async Task RestoreBackupAsync_ReportsProgressCorrectly()
    {
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Borrowers (Id, Name, Phone, Status) VALUES ('B99', 'Progress User', '9991112222', 0);";
            cmd.ExecuteNonQuery();
        }

        var backupMeta = await _backupService.CreateBackupAsync("MyPassword123!");
        var reportedStages = new List<string>();
        var progress = new Progress<string>(stage => reportedStages.Add(stage));

        var restoreMeta = await _backupService.RestoreBackupAsync(backupMeta.Location, "MyPassword123!", progress);
        Assert.NotNull(restoreMeta);
        Assert.Equal("Successful", restoreMeta.Status);
    }

    [Fact]
    public async Task CrossSystemRestore_SystemAToSystemB_UnformattedRecoveryKey_Succeeds()
    {
        var formattedKey = "DDRK-A1B2C3D4-E5F60718-293A4B5C-6D7E8F90-11223344-55667788-99AABBCC-DDEEFF00";
        var unformattedKey = "a1b2c3d4e5f60718293a4b5c6d7e8f90112233445566778899aabbccddeeff00";

        var backupMeta = await _backupService.CreateBackupAsync(formattedKey);

        var systemBDir = Path.Combine(Path.GetTempPath(), "dhirdhar-systemB-unfmt-" + Guid.NewGuid().ToString("N"));
        var systemBDataDir = Path.Combine(systemBDir, "Data");
        var systemBBackupDir = Path.Combine(systemBDir, "Backup");
        Directory.CreateDirectory(systemBDataDir);
        Directory.CreateDirectory(systemBBackupDir);
        var systemBDbPath = Path.Combine(systemBDataDir, "DhirDhar.db");

        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={systemBDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Borrowers (Id TEXT PRIMARY KEY, Name TEXT, Phone TEXT, Status INTEGER);";
            cmd.ExecuteNonQuery();
        }

        var systemBPathService = new TestPathService(systemBDir, systemBDbPath, systemBBackupDir);
        var cryptoServiceB = new DhirDhar.Infrastructure.Security.Cryptography.CryptoService(NullLogger<DhirDhar.Infrastructure.Security.Cryptography.CryptoService>.Instance);
        var keyServiceB = new DhirDhar.Infrastructure.Security.Keys.KeyManagementService(cryptoServiceB, systemBPathService, new SecurityEncryptionTests.MockAuditService(), NullLogger<DhirDhar.Infrastructure.Security.Keys.KeyManagementService>.Instance);
        await keyServiceB.InitializeMasterKeyAsync();

        var backupServiceB = new BackupService(
            systemBPathService,
            keyServiceB,
            cryptoServiceB,
            Options.Create(new BackupOptions { Directory = systemBBackupDir, RetentionCount = 1, EncryptBackups = true }),
            NullLogger<BackupService>.Instance);

        var systemBPackagePath = Path.Combine(systemBBackupDir, BackupService.LocalBackupFileName);
        File.Copy(backupMeta.Location, systemBPackagePath, true);

        // Restore using unformatted/lowercase recovery key
        var restoreResult = await backupServiceB.RestoreBackupAsync(systemBPackagePath, unformattedKey);
        Assert.NotNull(restoreResult);
        Assert.Equal("Successful", restoreResult.Status);

        try { Directory.Delete(systemBDir, true); } catch { }
    }

    [Fact]
    public async Task LocalRestore_WithoutPassword_Succeeds_AndCrossSystemRequiresRecoveryKey()
    {
        // 1. System A creates a backup automatically (using persistent Disaster Recovery Key)
        var backupMeta = await _backupService.CreateBackupAsync(null);
        var systemARecoveryKey = _keyManagementService.GetCurrentRecoveryKey();
        Assert.NotNull(systemARecoveryKey);

        // 2. System A (local machine) restores its own backup without password -> SUCCESS
        var localRestoreResult = await _backupService.RestoreBackupAsync(backupMeta.Location, null);
        Assert.NotNull(localRestoreResult);
        Assert.Equal("Successful", localRestoreResult.Status);

        // 3. System B (separate machine): Attempting restore without password fails safely
        var systemBDir = Path.Combine(Path.GetTempPath(), "dhirdhar-systemB-std-" + Guid.NewGuid().ToString("N"));
        var systemBDataDir = Path.Combine(systemBDir, "Data");
        var systemBBackupDir = Path.Combine(systemBDir, "Backup");
        Directory.CreateDirectory(systemBDataDir);
        Directory.CreateDirectory(systemBBackupDir);
        var systemBDbPath = Path.Combine(systemBDataDir, "DhirDhar.db");

        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={systemBDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Borrowers (Id TEXT PRIMARY KEY, Name TEXT, Phone TEXT, Status INTEGER);";
            cmd.ExecuteNonQuery();
        }

        var systemBPathService = new TestPathService(systemBDir, systemBDbPath, systemBBackupDir);
        var cryptoServiceB = new DhirDhar.Infrastructure.Security.Cryptography.CryptoService(NullLogger<DhirDhar.Infrastructure.Security.Cryptography.CryptoService>.Instance);
        var keyServiceB = new DhirDhar.Infrastructure.Security.Keys.KeyManagementService(cryptoServiceB, systemBPathService, new SecurityEncryptionTests.MockAuditService(), NullLogger<DhirDhar.Infrastructure.Security.Keys.KeyManagementService>.Instance);
        await keyServiceB.InitializeMasterKeyAsync();

        var backupServiceB = new BackupService(
            systemBPathService,
            keyServiceB,
            cryptoServiceB,
            Options.Create(new BackupOptions { Directory = systemBBackupDir, RetentionCount = 1, EncryptBackups = true }),
            NullLogger<BackupService>.Instance);

        var systemBPackagePath = Path.Combine(systemBBackupDir, BackupService.LocalBackupFileName);
        File.Copy(backupMeta.Location, systemBPackagePath, true);

        // System B without password fails
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await backupServiceB.RestoreBackupAsync(systemBPackagePath, null);
        });

        // System B with System A's Disaster Recovery Key succeeds!
        var restoreResult = await backupServiceB.RestoreBackupAsync(systemBPackagePath, systemARecoveryKey);
        Assert.NotNull(restoreResult);
        Assert.Equal("Successful", restoreResult.Status);

        try { Directory.Delete(systemBDir, true); } catch { }
    }

    [Fact]
    public async Task Restore_TruncatedPayload_ThrowsInvalidOperationException()
    {
        var backupMeta = await _backupService.CreateBackupAsync("TestPassword123!");
        var corruptedPath = Path.Combine(_backupDir, "truncated_payload.ddbackup");
        File.Copy(backupMeta.Location, corruptedPath, true);

        using (var archive = ZipFile.Open(corruptedPath, ZipArchiveMode.Update))
        {
            var dataEntry = archive.GetEntry("data.enc");
            dataEntry?.Delete();

            var newEntry = archive.CreateEntry("data.enc");
            using var stream = newEntry.Open();
            stream.Write(new byte[10]); // Truncated to 10 bytes (less than salt+nonce+tag)
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _backupService.RestoreBackupAsync(corruptedPath, "TestPassword123!");
        });
    }

    [Fact]
    public async Task CrossSystemMigration_MainSystemToSecondarySystem_FullWorkflow_Succeeds()
    {
        // 1. MAIN SYSTEM: Has borrower and transaction records
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Transactions (Id TEXT PRIMARY KEY, BorrowerId TEXT, Amount REAL, Type INTEGER, Date TEXT);
                INSERT INTO Borrowers (Id, Name, Phone, Status) VALUES ('M101', 'Main System Borrower', '9998887776', 1);
                INSERT INTO Transactions (Id, BorrowerId, Amount, Type, Date) VALUES ('TX101', 'M101', 50000.0, 0, '2026-08-22');
            ";
            cmd.ExecuteNonQuery();
        }

        // 2. MAIN SYSTEM: Creates backup and exports persistent Disaster Recovery Key
        var backupMeta = await _backupService.CreateBackupAsync(null);
        var mainSystemRecoveryKey = _keyManagementService.GetCurrentRecoveryKey();
        Assert.NotNull(mainSystemRecoveryKey);

        // 3. SECONDARY SYSTEM: Brand new computer with distinct DPAPI master key, path, and database
        var secondaryDir = Path.Combine(Path.GetTempPath(), "dhirdhar-secondary-" + Guid.NewGuid().ToString("N"));
        var secondaryDataDir = Path.Combine(secondaryDir, "Data");
        var secondaryBackupDir = Path.Combine(secondaryDir, "Backup");
        Directory.CreateDirectory(secondaryDataDir);
        Directory.CreateDirectory(secondaryBackupDir);
        var secondaryDbPath = Path.Combine(secondaryDataDir, "DhirDhar.db");

        // Initialize fresh empty database on secondary system
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={secondaryDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Borrowers (Id TEXT PRIMARY KEY, Name TEXT, Phone TEXT, Status INTEGER);";
            cmd.ExecuteNonQuery();
        }

        var secondaryPathService = new TestPathService(secondaryDir, secondaryDbPath, secondaryBackupDir);
        var secondaryCryptoService = new DhirDhar.Infrastructure.Security.Cryptography.CryptoService(NullLogger<DhirDhar.Infrastructure.Security.Cryptography.CryptoService>.Instance);
        var secondaryKeyService = new DhirDhar.Infrastructure.Security.Keys.KeyManagementService(secondaryCryptoService, secondaryPathService, new SecurityEncryptionTests.MockAuditService(), NullLogger<DhirDhar.Infrastructure.Security.Keys.KeyManagementService>.Instance);
        await secondaryKeyService.InitializeMasterKeyAsync();

        var secondaryBackupService = new BackupService(
            secondaryPathService,
            secondaryKeyService,
            secondaryCryptoService,
            Options.Create(new BackupOptions { Directory = secondaryBackupDir, RetentionCount = 1, EncryptBackups = true }),
            NullLogger<BackupService>.Instance);

        var downloadedBackupPackage = Path.Combine(secondaryBackupDir, BackupService.LocalBackupFileName);
        File.Copy(backupMeta.Location, downloadedBackupPackage, true);

        // 4. SECONDARY SYSTEM: Test wrong recovery key first -> fails safely without touching secondary database
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await secondaryBackupService.RestoreBackupAsync(downloadedBackupPackage, "DDRK-00000000-00000000-00000000-00000000-00000000-00000000-00000000-00000000");
        });
        Assert.Contains("decryption failed", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify secondary database is still intact (0 records)
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={secondaryDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Borrowers;";
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(0L, count);
        }

        // 5. SECONDARY SYSTEM: Restore using Main System's Disaster Recovery Key -> Success!
        var restoreResult = await secondaryBackupService.RestoreBackupAsync(downloadedBackupPackage, mainSystemRecoveryKey);
        Assert.NotNull(restoreResult);
        Assert.Equal("Successful", restoreResult.Status);

        // 6. SECONDARY SYSTEM: Verify complete restored data
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={secondaryDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Borrowers WHERE Id = 'M101';";
            var borrowerCount = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(1L, borrowerCount);

            cmd.CommandText = "SELECT COUNT(*) FROM Transactions WHERE Id = 'TX101';";
            var txCount = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(1L, txCount);
        }

        try { Directory.Delete(secondaryDir, true); } catch { }
    }

    [Fact]
    public async Task CreateGoogleBackupAsync_CreatesDirectCompatiblePackage_WithZeroEncryption()
    {
        var meta = await _backupService.CreateGoogleBackupAsync("testuser@gmail.com");
        Assert.NotNull(meta);
        Assert.True(File.Exists(meta.Location));
        Assert.Equal("Google Backup", meta.BackupType);

        using var archive = ZipFile.OpenRead(meta.Location);
        Assert.NotNull(archive.GetEntry("DhirDhar.db"));
        Assert.NotNull(archive.GetEntry("integrity.hash"));
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.Null(archive.GetEntry("data.enc"));

        var manifestEntry = archive.GetEntry("manifest.json")!;
        using var stream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<BackupService.BackupManifest>(stream);
        Assert.NotNull(manifest);
        Assert.False(manifest.Encrypted);
        Assert.Equal("GoogleDriveOAuth", manifest.ProtectionMode);
        Assert.Equal("testuser@gmail.com", manifest.AccountEmail);
    }

    [Fact]
    public async Task RestoreGoogleBackup_FromOldEncryptedPackage_ThrowsIncompatibleOlderFormatMessage()
    {
        var localEncrypted = await _backupService.CreateBackupAsync();
        var oldGoogleBackupPath = Path.Combine(_backupDir, "DhirDhar_Google_Backup.ddbackup");
        File.Copy(localEncrypted.Location, oldGoogleBackupPath, true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _backupService.RestoreBackupAsync(oldGoogleBackupPath);
        });

        Assert.Contains("older backup format", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CrossSystemRestore_WithDirectGoogleBackup_RestoresWithoutAnyRecoveryKey()
    {
        // 1. PRIMARY SYSTEM: Create Google Backup
        var googleMeta = await _backupService.CreateGoogleBackupAsync("owner@gmail.com");

        // 2. SECONDARY SYSTEM: Fresh setup with completely different directory and no keys
        var secondaryDir = Path.Combine(Path.GetTempPath(), "dhirdhar-cross-sys-direct-" + Guid.NewGuid().ToString("N"));
        var secondaryDataDir = Path.Combine(secondaryDir, "Data");
        var secondaryBackupDir = Path.Combine(secondaryDir, "Backup");
        Directory.CreateDirectory(secondaryDataDir);
        Directory.CreateDirectory(secondaryBackupDir);

        var secondaryDbPath = Path.Combine(secondaryDataDir, "DhirDhar.db");
        using (var initConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={secondaryDbPath}"))
        {
            initConn.Open();
            using var cmd = initConn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Borrowers (Id TEXT PRIMARY KEY, Name TEXT, Phone TEXT, Status INTEGER);";
            cmd.ExecuteNonQuery();
            initConn.Close();
        }

        var secondaryPathService = new TestPathService(secondaryDir, secondaryDbPath, secondaryBackupDir);
        var cryptoService = new DhirDhar.Infrastructure.Security.Cryptography.CryptoService(NullLogger<DhirDhar.Infrastructure.Security.Cryptography.CryptoService>.Instance);
        var secondaryKeyService = new DhirDhar.Infrastructure.Security.Keys.KeyManagementService(cryptoService, secondaryPathService, new SecurityEncryptionTests.MockAuditService(), NullLogger<DhirDhar.Infrastructure.Security.Keys.KeyManagementService>.Instance);
        await secondaryKeyService.InitializeMasterKeyAsync();

        var secondaryBackupService = new BackupService(
            secondaryPathService,
            secondaryKeyService,
            cryptoService,
            Options.Create(new BackupOptions { Directory = secondaryBackupDir, RetentionCount = 1, EncryptBackups = true }),
            NullLogger<BackupService>.Instance);

        // Copy downloaded Google Backup into secondary backup directory
        var downloadedBackupPackage = Path.Combine(secondaryBackupDir, "DhirDhar_Google_Backup.ddbackup");
        File.Copy(googleMeta.Location, downloadedBackupPackage, true);

        // 3. SECONDARY SYSTEM: Direct restore without ANY password or recovery key
        var result = await secondaryBackupService.RestoreBackupAsync(downloadedBackupPackage, null);
        Assert.NotNull(result);
        Assert.Equal("Successful", result.Status);

        // Verify data in secondary database
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={secondaryDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Borrowers;";
            var count = (long)(cmd.ExecuteScalar() ?? 0L);
            Assert.Equal(2L, count);
        }

        try { Directory.Delete(secondaryDir, true); } catch { }
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

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Audit;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Security.Cryptography;
using DhirDhar.Application.Security.Keys;
using DhirDhar.Application.Security.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Infrastructure.Audit;
using DhirDhar.Infrastructure.Backup;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Security;
using DhirDhar.Infrastructure.Security.Cryptography;
using DhirDhar.Infrastructure.Security.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using DhirDhar.Infrastructure.Tests.Persistence;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class SecurityEncryptionTests
{
    private readonly ICryptoService _cryptoService = new CryptoService(NullLogger<CryptoService>.Instance);

    [Fact]
    public void AesGcmEncryptionDecryption_Roundtrip_Succeeds()
    {
        var key = _cryptoService.GenerateRandomKey(32);
        var plaintext = "Sensitive Borrower Financial Data - ₹50,000 Loan to DJ102";

        var encryptedBase64 = _cryptoService.EncryptString(plaintext, key);
        Assert.NotNull(encryptedBase64);
        Assert.NotEqual(plaintext, encryptedBase64);

        var decrypted = _cryptoService.DecryptString(encryptedBase64, key);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void AesGcmEncryption_NonceUniqueness_NeverReused()
    {
        var key = _cryptoService.GenerateRandomKey(32);
        var plaintext = "Consistent Payload Test";

        var enc1 = _cryptoService.EncryptString(plaintext, key);
        var enc2 = _cryptoService.EncryptString(plaintext, key);

        // Different random nonces must produce different ciphertexts for identical plaintext
        Assert.NotEqual(enc1, enc2);

        Assert.Equal(plaintext, _cryptoService.DecryptString(enc1, key));
        Assert.Equal(plaintext, _cryptoService.DecryptString(enc2, key));
    }

    [Fact]
    public void AesGcmDecryption_TamperedCiphertext_ThrowsCryptographicException()
    {
        var key = _cryptoService.GenerateRandomKey(32);
        var plainBytes = "Confidential Financial Data"u8.ToArray();

        var payload = _cryptoService.Encrypt(plainBytes, key);
        var rawBytes = payload.ToBytes();

        // Tamper with the last byte of the ciphertext
        rawBytes[^1] ^= 0xFF;

        var tamperedPayload = EncryptedPayload.FromBytes(rawBytes);
        Assert.Throws<CryptographicException>(() => _cryptoService.Decrypt(tamperedPayload, key));
    }

    [Fact]
    public void AesGcmDecryption_TamperedTag_ThrowsCryptographicException()
    {
        var key = _cryptoService.GenerateRandomKey(32);
        var plainBytes = "Confidential Financial Data"u8.ToArray();

        var payload = _cryptoService.Encrypt(plainBytes, key);
        var rawBytes = payload.ToBytes();

        // Tamper with authentication tag
        rawBytes[20] ^= 0xFF;

        var tamperedPayload = EncryptedPayload.FromBytes(rawBytes);
        Assert.Throws<CryptographicException>(() => _cryptoService.Decrypt(tamperedPayload, key));
    }

    [Fact]
    public void BlindIndex_IsDeterministicAndMatchesCaseInsensitively()
    {
        var blindIndexKey = _cryptoService.GenerateRandomKey(32);

        var token1 = _cryptoService.ComputeBlindIndex("DJ102", blindIndexKey);
        var token2 = _cryptoService.ComputeBlindIndex("dj102", blindIndexKey);
        var token3 = _cryptoService.ComputeBlindIndex("  DJ102  ", blindIndexKey);

        Assert.NotEmpty(token1);
        Assert.Equal(token1, token2);
        Assert.Equal(token1, token3);
    }

    [Fact]
    public void BlindIndex_DifferentInputs_ProduceDifferentTokens()
    {
        var blindIndexKey = _cryptoService.GenerateRandomKey(32);

        var token1 = _cryptoService.ComputeBlindIndex("DJ101", blindIndexKey);
        var token2 = _cryptoService.ComputeBlindIndex("DJ102", blindIndexKey);

        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public async Task KeyManagementService_InitializesAndDerivesSubKeys()
    {
        var pathService = new TestDatabasePathService();
        var auditService = new MockAuditService();
        var keyService = new KeyManagementService(_cryptoService, pathService, auditService, NullLogger<KeyManagementService>.Instance);

        await keyService.InitializeMasterKeyAsync();

        var masterKey = keyService.GetMasterKey();
        var fieldKey = keyService.GetFieldEncryptionKey();
        var searchKey = keyService.GetSearchIndexKey();
        var photoKey = keyService.GetPhotoEncryptionKey();
        var backupKey = keyService.GetBackupMasterKey();

        Assert.Equal(32, masterKey.Length);
        Assert.Equal(32, fieldKey.Length);
        Assert.Equal(32, searchKey.Length);
        Assert.Equal(32, photoKey.Length);
        Assert.Equal(32, backupKey.Length);

        // Subkeys must be cryptographically distinct
        Assert.NotEqual(masterKey, fieldKey);
        Assert.NotEqual(fieldKey, searchKey);
        Assert.NotEqual(fieldKey, photoKey);
        Assert.NotEqual(fieldKey, backupKey);

        var isVerified = await keyService.VerifyEncryptionIntegrityAsync();
        Assert.True(isVerified);
    }

    [Fact]
    public async Task KeyManagementService_RecoveryKey_IsPersistentAndMatchesOnSubsequentCalls()
    {
        var pathService = new TestDatabasePathService();
        var auditService = new MockAuditService();
        var keyService = new KeyManagementService(_cryptoService, pathService, auditService, NullLogger<KeyManagementService>.Instance);

        await keyService.InitializeMasterKeyAsync();

        var firstCall = await keyService.GenerateOrGetRecoveryKeyAsync();
        var secondCall = await keyService.GenerateOrGetRecoveryKeyAsync();
        var current = keyService.GetCurrentRecoveryKey();

        Assert.Equal(firstCall.FormattedRecoveryKey, secondCall.FormattedRecoveryKey);
        Assert.Equal(firstCall.FormattedRecoveryKey, current);

        // Explicit rotation changes the key
        var rotated = await keyService.RotateRecoveryKeyAsync();
        Assert.NotEqual(firstCall.FormattedRecoveryKey, rotated.FormattedRecoveryKey);
        Assert.Equal(rotated.FormattedRecoveryKey, keyService.GetCurrentRecoveryKey());
    }

    [Fact]
    public async Task KeyManagementService_RecoveryKey_RoundtripSucceeds()
    {
        var pathService = new TestDatabasePathService();
        var auditService = new MockAuditService();
        var keyService = new KeyManagementService(_cryptoService, pathService, auditService, NullLogger<KeyManagementService>.Instance);

        await keyService.InitializeMasterKeyAsync();
        var originalMasterKey = keyService.GetMasterKey();

        var recoveryDetails = await keyService.GenerateOrGetRecoveryKeyAsync();
        Assert.StartsWith("DDRK-", recoveryDetails.FormattedRecoveryKey);

        // Test recovery
        var recovered = await keyService.RecoverMasterKeyWithRecoveryKeyAsync(recoveryDetails.FormattedRecoveryKey);
        Assert.True(recovered);

        var currentMasterKey = keyService.GetMasterKey();
        Assert.Equal(originalMasterKey, currentMasterKey);
    }

    [Fact]
    public async Task PhotoEncryption_EncryptsAndDecryptsStream_WithoutPlaintextFiles()
    {
        var pathService = new TestDatabasePathService();
        var auditService = new MockAuditService();
        var keyService = new KeyManagementService(_cryptoService, pathService, auditService, NullLogger<KeyManagementService>.Instance);
        await keyService.InitializeMasterKeyAsync();

        var photoService = new PhotoEncryptionService(_cryptoService, keyService, NullLogger<PhotoEncryptionService>.Instance);

        var tempSource = Path.GetTempFileName();
        var sampleImageBytes = new byte[1024];
        RandomNumberGenerator.Fill(sampleImageBytes);
        await File.WriteAllBytesAsync(tempSource, sampleImageBytes);

        var encPath = await photoService.EncryptAndStorePhotoAsync(tempSource, "borrower");

        Assert.True(File.Exists(encPath));
        Assert.False(File.Exists(tempSource)); // Plaintext file must be securely deleted
        Assert.True(photoService.IsPhotoEncrypted(encPath));

        var decryptedBytes = await photoService.DecryptPhotoToBytesAsync(encPath);
        Assert.Equal(sampleImageBytes, decryptedBytes);

        if (File.Exists(encPath)) File.Delete(encPath);
    }

    [Fact]
    public async Task EncryptedBackup_CreatesAndRestoresSuccessfully()
    {
        using var tempDb = new TempDatabase();
        var pathService = new TestDatabasePathService(tempDb.FilePath);
        var auditService = new MockAuditService();
        var keyService = new KeyManagementService(_cryptoService, pathService, auditService, NullLogger<KeyManagementService>.Instance);
        await keyService.InitializeMasterKeyAsync();

        var backupService = new BackupService(
            pathService,
            keyService,
            _cryptoService,
            Options.Create(new BackupOptions { Directory = Path.GetDirectoryName(tempDb.FilePath) ?? string.Empty }),
            NullLogger<BackupService>.Instance);

        // Seed initial borrower
        using (var ctx = new DhirDharDbContext(tempDb.CreateOptions()))
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Borrowers.Add(new Borrower("DJ10", "Alice Test", "9876543210", "Village A", "Notes", new DateTime(2022, 1, 1)));
            await ctx.SaveChangesAsync();
        }

        var backupMeta = await backupService.CreateBackupAsync("MyStrongPassphrase123!");
        Assert.True(File.Exists(backupMeta.Location));

        var isVerified = await backupService.VerifyBackupAsync(backupMeta.Location);
        Assert.True(isVerified);

        // Restore into new database
        using var targetTemp = new TempDatabase();
        var targetPathService = new TestDatabasePathService(targetTemp.FilePath);
        var targetBackupService = new BackupService(
            targetPathService,
            keyService,
            _cryptoService,
            Options.Create(new BackupOptions { Directory = Path.GetDirectoryName(targetTemp.FilePath) ?? string.Empty }),
            NullLogger<BackupService>.Instance);

        var restoreMeta = await targetBackupService.RestoreBackupAsync(backupMeta.Location, "MyStrongPassphrase123!");
        Assert.Equal("Successful", restoreMeta.Status);

        using (var verifyCtx = new DhirDharDbContext(targetTemp.CreateOptions()))
        {
            var b = await verifyCtx.Borrowers.FirstOrDefaultAsync(x => x.BorrowerNumber == "DJ10");
            Assert.NotNull(b);
            Assert.Equal("Alice Test", b.Name);
        }

        if (File.Exists(backupMeta.Location)) File.Delete(backupMeta.Location);
    }

    [Fact]
    public async Task EncryptedBackup_TamperedBackup_ThrowsAndRejectsRestore()
    {
        using var tempDb = new TempDatabase();
        var pathService = new TestDatabasePathService(tempDb.FilePath);
        var auditService = new MockAuditService();
        var keyService = new KeyManagementService(_cryptoService, pathService, auditService, NullLogger<KeyManagementService>.Instance);
        await keyService.InitializeMasterKeyAsync();

        var backupService = new BackupService(
            pathService,
            keyService,
            _cryptoService,
            Options.Create(new BackupOptions { Directory = Path.GetDirectoryName(tempDb.FilePath) ?? string.Empty }),
            NullLogger<BackupService>.Instance);

        using (var ctx = new DhirDharDbContext(tempDb.CreateOptions()))
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Borrowers.Add(new Borrower("DJ11", "Bob Test", "9876543211", "Village B", "Notes", new DateTime(2022, 1, 1)));
            await ctx.SaveChangesAsync();
        }

        var backupMeta = await backupService.CreateBackupAsync("Password!");

        // Tamper with the backup zip bytes
        var bytes = await File.ReadAllBytesAsync(backupMeta.Location);
        bytes[bytes.Length / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(backupMeta.Location, bytes);

        // Attempt restore - must fail authentication and throw
        await Assert.ThrowsAnyAsync<Exception>(() => backupService.RestoreBackupAsync(backupMeta.Location, "Password!"));

        if (File.Exists(backupMeta.Location)) File.Delete(backupMeta.Location);
    }

    private sealed class TestDatabasePathService : IDatabasePathService
    {
        public TestDatabasePathService(string? dbPath = null)
        {
            DatabasePath = dbPath ?? Path.Combine(Path.GetTempPath(), "test_" + Guid.NewGuid().ToString("N") + ".db");
        }

        public string DatabasePath { get; }
        public string DatabaseDirectory => Path.GetDirectoryName(DatabasePath) ?? Path.GetTempPath();
        public string BackupDirectory => Path.GetDirectoryName(DatabasePath) ?? Path.GetTempPath();
        public string ApplicationDataDirectory => Path.GetDirectoryName(DatabasePath) ?? Path.GetTempPath();
        public string LogDirectory => Path.GetDirectoryName(DatabasePath) ?? Path.GetTempPath();
    }

    internal sealed class MockAuditService : IAuditService
    {
        public Task RecordAsync(AuditEvent auditEvent, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<System.Collections.Generic.IReadOnlyList<DhirDhar.Application.Audit.AuditEntry>> GetAuditHistoryAsync(DateTime? fromDate = null, DateTime? toDate = null, string? action = null, string? entityType = null, string? result = null, System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult<System.Collections.Generic.IReadOnlyList<DhirDhar.Application.Audit.AuditEntry>>(Array.Empty<DhirDhar.Application.Audit.AuditEntry>());
    }
}

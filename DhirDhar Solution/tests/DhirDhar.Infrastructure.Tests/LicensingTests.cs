using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DhirDhar.Application.Licensing;
using DhirDhar.Application.Licensing.Models;
using DhirDhar.Infrastructure.Licensing;
using DhirDhar.LicenseGenerator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class LicensingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _storageFile;
    private readonly string _testPrivateKeyPem;
    private readonly string _testPublicKeyPem;

    // Official developer private key matching LicenseVerificationKey.PublicKeyPem
    private const string OfficialDevPrivateKeyPem = @"-----BEGIN EC PRIVATE KEY-----
MHcCAQEEIGdwp3oX8tjTumMbdGQBEucR6oa4Gtbtixy2Sh91v5MvoAoGCCqGSM49
AwEHoUQDQgAEx5L8QLG6AScIeADmTZbxUZhmVn5tgsS6ALUdFVjrC3KnQMU70oaA
IpEEa90Pt0F1apDusYVwT6TI9Hh4DTVMxg==
-----END EC PRIVATE KEY-----";

    public LicensingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DhirDharLicTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _storageFile = Path.Combine(_tempDir, "activation.dat");

        var (priv, pub) = LicenseSigner.GenerateKeyPair();
        _testPrivateKeyPem = priv;
        _testPublicKeyPem = pub;
    }

    [Fact]
    public void LicenseSigner_GeneratesValidSerialKey_And_VerifiesSuccessfully()
    {
        var issueDate = DateTime.UtcNow.Date;
        var expiryDate = issueDate.AddDays(365);
        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-2026-TEST01",
            CustomerName: "Ramesh Patel",
            CustomerEmail: "ramesh@example.com",
            Edition: "Annual",
            IssuedAt: issueDate,
            ExpiresAt: expiryDate,
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "TEST-ISSUANCE-01");

        var serialKey = LicenseSigner.CreateSerialKey(payload, _testPrivateKeyPem);

        Assert.NotNull(serialKey);
        Assert.Equal(29, serialKey.Length); // 25 chars + 4 hyphens
        Assert.Equal(5, serialKey.Split('-').Length);

        var (isValid, verifiedPayload, errorMessage) = LicenseDecoder.VerifySerialKey(serialKey, _testPublicKeyPem);
        Assert.True(isValid, errorMessage);
        Assert.NotNull(verifiedPayload);
        Assert.Equal("DhirDhar", verifiedPayload.Product);
        Assert.StartsWith("DD-", verifiedPayload.LicenseId);
        Assert.Equal(issueDate, verifiedPayload.IssuedAt);
        Assert.Equal(expiryDate, verifiedPayload.ExpiresAt);
    }

    [Fact]
    public void LicenseDecoder_RejectsTamperedPayloadData()
    {
        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-2026-000001",
            CustomerName: "Original Customer",
            CustomerEmail: "original@dhirdhar.com",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(365),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "TEST-ISSUANCE-02");

        var serialKey = LicenseSigner.CreateSerialKey(payload, _testPrivateKeyPem);

        // Modifying any character in the 25-character key must fail verification
        var chars = serialKey.ToCharArray();
        chars[0] = chars[0] == '9' ? '8' : '9';
        var tamperedKey = new string(chars);

        var (isValid, verifiedPayload, error) = LicenseDecoder.VerifySerialKey(tamperedKey, _testPublicKeyPem);
        Assert.False(isValid);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void LicenseDecoder_RejectsTamperedExpiryDate()
    {
        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-2026-000002",
            CustomerName: "Customer",
            CustomerEmail: "customer@dhirdhar.com",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(30),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "TEST-ISSUANCE-03");

        var serialKey = LicenseSigner.CreateSerialKey(payload, _testPrivateKeyPem);

        // Modifying any character in middle/end of key must fail verification
        var chars = serialKey.ToCharArray();
        chars[12] = chars[12] == 'A' ? 'B' : 'A';
        var tamperedKey = new string(chars);

        var (isValid, verifiedPayload, error) = LicenseDecoder.VerifySerialKey(tamperedKey, _testPublicKeyPem);
        Assert.False(isValid);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void LicenseDecoder_RejectsInvalidSignature()
    {
        var (otherPriv, _) = LicenseSigner.GenerateKeyPair();

        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-2026-000003",
            CustomerName: "Customer",
            CustomerEmail: "customer@dhirdhar.com",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(365),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "TEST-ISSUANCE-04");

        var rogueSerialKey = LicenseSigner.CreateSerialKey(payload, otherPriv);
        var (isValid, _, error) = LicenseDecoder.VerifySerialKey(rogueSerialKey, _testPublicKeyPem);
        Assert.False(isValid);
        Assert.Contains("signature", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LicenseStorageService_SavesAndLoadsEncryptedActivationRecord()
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var record = new StoredActivation(
            SerialKey: "DD-TEST-KEY-123456",
            BoundDeviceId: "DD-PC-1122-3344-5566-7788",
            ActivatedAt: DateTime.UtcNow,
            LastVerifiedAt: DateTime.UtcNow,
            LastKnownSystemDate: DateTime.UtcNow,
            Checksum: string.Empty);

        await storage.SaveActivationAsync(record);
        var loaded = await storage.LoadActivationAsync();

        Assert.NotNull(loaded);
        Assert.Equal("DD-TEST-KEY-123456", loaded.SerialKey);
        Assert.Equal("DD-PC-1122-3344-5566-7788", loaded.BoundDeviceId);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Checksum));
    }

    [Fact]
    public async Task LicenseManager_FullActivation_Persistence_And_Renewal_Workflow()
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new DeviceFingerprintService();
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        // 1. Initial State before activation -> NotActivated
        var initResult = await manager.InitializeAsync();
        Assert.False(initResult.IsValid);
        Assert.Equal(LicenseStatus.NotActivated, initResult.Status);
        Assert.True(manager.RequiresActivation);
        Assert.False(manager.IsLicensed);
        Assert.False(manager.IsReadOnly);

        // 2. Generate signed serial key using official developer private key
        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-2026-OFFICIAL-01",
            CustomerName: "Kanti Patel",
            CustomerEmail: "kanti@patel.in",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(365),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "TEST-ISSUANCE-05");

        var serialKey = LicenseSigner.CreateSerialKey(payload, OfficialDevPrivateKeyPem);

        // 3. Activate license in LicenseManager
        var actResult = await manager.ActivateAsync(serialKey);
        Assert.True(actResult.Success, actResult.Message);
        Assert.Equal(LicenseStatus.Active, actResult.Status);
        Assert.True(manager.IsLicensed);
        Assert.False(manager.IsReadOnly);
        Assert.False(manager.RequiresActivation);
        Assert.NotNull(manager.CurrentLicense);
        Assert.StartsWith("DD-", manager.CurrentLicense.LicenseId);
        Assert.Equal(365, manager.CurrentLicense.DaysRemaining);

        // 4. Simulate App Restart (new LicenseManager instance loading from stored encrypted file)
        var restartManager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);
        var restartInit = await restartManager.InitializeAsync();
        Assert.True(restartInit.IsValid, restartInit.Message);
        Assert.Equal(LicenseStatus.Active, restartInit.Status);
        Assert.True(restartManager.IsLicensed);
        Assert.False(restartManager.IsReadOnly);

        // 5. Test Renewal with a new key
        var renewalPayload = payload with
        {
            LicenseId = "DD-2027-RENEW-01",
            IssuedAt = DateTime.UtcNow.Date,
            ExpiresAt = DateTime.UtcNow.Date.AddDays(365 * 2),
            Renewal = true
        };
        var renewalKey = LicenseSigner.CreateSerialKey(renewalPayload, OfficialDevPrivateKeyPem);

        var renewResult = await restartManager.RenewAsync(renewalKey);
        Assert.True(renewResult.Success, renewResult.Message);
        Assert.NotNull(restartManager.CurrentLicense);
        Assert.StartsWith("DD-", restartManager.CurrentLicense.LicenseId);
    }

    [Fact]
    public async Task LicenseManager_RejectsDeviceMismatch()
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new DeviceFingerprintService();
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-2026-DEV-01",
            CustomerName: "Test User",
            CustomerEmail: "test@user.com",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(365),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "TEST-ISSUANCE-06");

        var serialKey = LicenseSigner.CreateSerialKey(payload, OfficialDevPrivateKeyPem);

        // Store with a fake/different bound device ID
        var foreignRecord = new StoredActivation(
            SerialKey: serialKey,
            BoundDeviceId: "DD-PC-OTHER-MACHINE-9999",
            ActivatedAt: DateTime.UtcNow,
            LastVerifiedAt: DateTime.UtcNow,
            LastKnownSystemDate: DateTime.UtcNow,
            Checksum: string.Empty);

        await storage.SaveActivationAsync(foreignRecord);

        // Act
        var result = await manager.InitializeAsync();

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(LicenseStatus.Invalid, result.Status);
        Assert.True(manager.RequiresActivation);
        Assert.Contains("different Windows PC", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeviceFingerprintService_GeneratesDeterministicNonEmptyId()
    {
        var service = new DeviceFingerprintService();
        var id1 = service.GetDeviceFingerprint();
        var id2 = service.GetDeviceFingerprint();

        Assert.NotNull(id1);
        Assert.StartsWith("DD-PC-", id1);
        Assert.Equal(id1, id2);
        Assert.True(service.ValidateDeviceFingerprint(id1));
        Assert.False(service.ValidateDeviceFingerprint("DD-PC-WRONG-DEVICE-0000"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task LicenseManager_RejectsEmptyOrWhitespaceKeys(string emptyOrWhitespaceKey)
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new DeviceFingerprintService();
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        var result = await manager.ActivateAsync(emptyOrWhitespaceKey);
        Assert.False(result.Success);
        Assert.Equal(LicenseStatus.NotActivated, result.Status);
        Assert.True(manager.RequiresActivation);
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("INVALID-SERIAL-KEY-12345")]
    [InlineData("DD-123456-789012-345678")]
    public async Task LicenseManager_RejectsArbitraryInvalidKeys(string invalidKey)
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new DeviceFingerprintService();
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        var result = await manager.ActivateAsync(invalidKey);
        Assert.False(result.Success);
        Assert.Equal(LicenseStatus.Invalid, result.Status);
        Assert.True(manager.RequiresActivation);
    }

    [Fact]
    public async Task LicenseManager_SuccessfullyActivatesWithWhitespacePaddedValidKey()
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new DeviceFingerprintService();
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-2026-PAD-01",
            CustomerName: "Padded Test Customer",
            CustomerEmail: "pad@test.com",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(365),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "TEST-ISSUANCE-07");

        var serialKey = LicenseSigner.CreateSerialKey(payload, OfficialDevPrivateKeyPem);
        var paddedKey = $"  \t {serialKey} \r\n  ";

        var result = await manager.ActivateAsync(paddedKey.Trim());
        Assert.True(result.Success, result.Message);
        Assert.Equal(LicenseStatus.Active, result.Status);
        Assert.True(manager.IsLicensed);
    }

    [Fact]
    public async Task HardwareId_DD_PC_0867_7F05_6809_46EA_FullEndToEndActivationWorkflow()
    {
        const string targetHwId = "DD-PC-0867-7F05-6809-46EA";
        var fakeFingerprint = new SpecificDeviceFingerprintService(targetHwId);
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var manager = new LicenseManager(storage, fakeFingerprint, NullLogger<LicenseManager>.Instance);

        // 1. Verify invalid/corrupted test key 629HS-NQ222-226A-6T8UX-SCD8Z is properly rejected
        const string invalidCorruptedKey = "629HS-NQ222-226A-6T8UX-SCD8Z";
        var invalidResult = await manager.ActivateAsync(invalidCorruptedKey);
        Assert.False(invalidResult.Success);
        Assert.Equal(LicenseStatus.Invalid, invalidResult.Status);

        // 2. Generate a legitimate signed license for DD-PC-0867-7F05-6809-46EA
        var historyService = new LicenseHistoryService(Path.Combine(_tempDir, "history.json"));
        var (payload, legitimateKey) = LicenseSigner.CreateUniqueLicense(
            customerName: "DhirDhar Customer",
            customerEmail: "customer@dhirdhar.com",
            privateKeyPem: OfficialDevPrivateKeyPem,
            publicKeyPem: LicenseVerificationKey.PublicKeyPem,
            historyService: historyService,
            deviceBinding: targetHwId);

        Assert.Equal(29, legitimateKey.Length);
        Assert.Equal(25, LicenseDecoder.NormalizeSerialKey(legitimateKey).Length);

        // 3. Activate on target hardware ID -> Activation must succeed
        var activationResult = await manager.ActivateAsync(legitimateKey);
        Assert.True(activationResult.Success, activationResult.Message);
        Assert.Equal(LicenseStatus.Active, activationResult.Status);
        Assert.True(manager.IsLicensed);
        Assert.Equal(targetHwId, manager.CurrentLicense?.BoundDeviceId);
        Assert.Equal(payload.LicenseId, manager.CurrentLicense?.LicenseId);

        // 4. Test persistence across app restart (simulate reload from encrypted storage)
        var restartManager = new LicenseManager(storage, fakeFingerprint, NullLogger<LicenseManager>.Instance);
        var restartResult = await restartManager.InitializeAsync();
        Assert.True(restartResult.IsValid, restartResult.Message);
        Assert.Equal(LicenseStatus.Active, restartResult.Status);
        Assert.True(restartManager.IsLicensed);
        Assert.Equal(payload.LicenseId, restartManager.CurrentLicense?.LicenseId);
        Assert.Equal(targetHwId, restartManager.CurrentLicense?.BoundDeviceId);

        // 5. Test wrong Hardware ID rejection
        var wrongFingerprint = new SpecificDeviceFingerprintService("DD-PC-9999-8888-7777-6666");
        var wrongStorage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, Path.Combine(_tempDir, "wrong_act.dat"));
        var wrongManager = new LicenseManager(wrongStorage, wrongFingerprint, NullLogger<LicenseManager>.Instance);
        var wrongHwResult = await wrongManager.ActivateAsync(legitimateKey);
        Assert.False(wrongHwResult.Success);
        Assert.Equal(LicenseStatus.Invalid, wrongHwResult.Status);
        Assert.Contains("PC", wrongHwResult.Message, StringComparison.OrdinalIgnoreCase);

        // 6. Test modified payload / character tampering rejection
        var keyChars = legitimateKey.ToCharArray();
        keyChars[2] = keyChars[2] == 'X' ? 'Y' : 'X';
        var tamperedKey = new string(keyChars);
        var tamperedResult = await manager.ActivateAsync(tamperedKey);
        Assert.False(tamperedResult.Success);
        Assert.Equal(LicenseStatus.Invalid, tamperedResult.Status);

        // 7. Test expired license rejection
        var expiredPayload = payload with
        {
            LicenseId = "DD-20240101-00001",
            IssuedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var expiredKey = LicenseSigner.CreateSerialKey(expiredPayload, OfficialDevPrivateKeyPem);
        var expiredResult = await manager.ActivateAsync(expiredKey);
        Assert.False(expiredResult.Success);
        Assert.Equal(LicenseStatus.Expired, expiredResult.Status);

        // 8. Test rogue / wrong signing key rejection
        var (roguePriv, _) = LicenseSigner.GenerateKeyPair();
        var rogueKey = LicenseSigner.CreateSerialKey(payload, roguePriv);
        var rogueResult = await manager.ActivateAsync(rogueKey);
        Assert.False(rogueResult.Success);
        Assert.Equal(LicenseStatus.Invalid, rogueResult.Status);
    }

    [Fact]
    public async Task IsolatedCustomerPC_WithoutHistoryOrCache_VerifiesAndActivatesSuccessfully()
    {
        const string targetHwId = "DD-PC-0867-7F05-6809-46EA";
        var fakeFingerprint = new SpecificDeviceFingerprintService(targetHwId);
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var manager = new LicenseManager(storage, fakeFingerprint, NullLogger<LicenseManager>.Instance);

        // Admin creates license for "Shreeji Jewellers" on developer machine
        var (payload, key) = LicenseSigner.CreateUniqueLicense(
            customerName: "Shreeji Jewellers",
            customerEmail: "shreeji@jewellers.com",
            deviceBinding: targetHwId);

        // Simulate customer PC by clearing in-memory candidates and ensuring no history file on disk
        var field = typeof(LicenseDecoder).GetField("_inMemoryCandidates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (field != null)
        {
            dynamic dict = field.GetValue(null)!;
            dict.Clear();
        }

        // Customer activates on offline PC
        var (isValid, verifiedPayload, err) = LicenseDecoder.VerifySerialKey(key);
        Assert.True(isValid, err);
        Assert.NotNull(verifiedPayload);
        Assert.Equal(payload.LicenseId, verifiedPayload.LicenseId);

        var actResult = await manager.ActivateAsync(key);
        Assert.True(actResult.Success, actResult.Message);
        Assert.Equal(LicenseStatus.Active, actResult.Status);
    }

    private sealed class SpecificDeviceFingerprintService : IDeviceFingerprintService
    {
        private readonly string _fingerprint;

        public SpecificDeviceFingerprintService(string fingerprint)
        {
            _fingerprint = fingerprint;
        }

        public string GetDeviceFingerprint() => _fingerprint;

        public bool ValidateDeviceFingerprint(string? expectedFingerprint) =>
            !string.IsNullOrWhiteSpace(expectedFingerprint) &&
            string.Equals(_fingerprint.Trim(), expectedFingerprint.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
        }
    }
}

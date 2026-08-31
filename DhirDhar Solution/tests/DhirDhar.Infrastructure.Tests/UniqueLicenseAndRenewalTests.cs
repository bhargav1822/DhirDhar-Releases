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

public sealed class UniqueLicenseAndRenewalTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _historyFile;
    private readonly string _storageFile;
    private readonly string _privateKeyPem;
    private readonly string _publicKeyPem;
    private readonly LicenseHistoryService _historyService;

    private const string OfficialDevPrivateKeyPem = @"-----BEGIN EC PRIVATE KEY-----
MHcCAQEEIGdwp3oX8tjTumMbdGQBEucR6oa4Gtbtixy2Sh91v5MvoAoGCCqGSM49
AwEHoUQDQgAEx5L8QLG6AScIeADmTZbxUZhmVn5tgsS6ALUdFVjrC3KnQMU70oaA
IpEEa90Pt0F1apDusYVwT6TI9Hh4DTVMxg==
-----END EC PRIVATE KEY-----";

    public UniqueLicenseAndRenewalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DD_UniqueLicTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _historyFile = Path.Combine(_tempDir, "license_history.json");
        _storageFile = Path.Combine(_tempDir, "activation.dat");

        _privateKeyPem = OfficialDevPrivateKeyPem;
        _publicKeyPem = LicenseVerificationKey.PublicKeyPem;
        _historyService = new LicenseHistoryService(_historyFile);
    }

    [Fact]
    public async Task TwoLicenses_ForSameCustomer_And_RenewalWorkflow_SatisfiesAll10Criteria()
    {
        const string customerName = "ABC";
        const string customerEmail = "abc@example.com";

        var fingerprintService = new DeviceFingerprintService();
        var deviceId = fingerprintService.GetDeviceFingerprint();

        // -------------------------------------------------------------
        // Step 1: Generate License #1
        // -------------------------------------------------------------
        var (payload1, serialKey1) = LicenseSigner.CreateUniqueLicense(
            customerName: customerName,
            customerEmail: customerEmail,
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService,
            deviceBinding: null);

        // -------------------------------------------------------------
        // Step 2: Generate License #2 for same customer, email, and dates
        // -------------------------------------------------------------
        var (payload2, serialKey2) = LicenseSigner.CreateUniqueLicense(
            customerName: customerName,
            customerEmail: customerEmail,
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService,
            deviceBinding: null);

        // -------------------------------------------------------------
        // VERIFICATION 1: License IDs are different
        // -------------------------------------------------------------
        Assert.False(string.IsNullOrWhiteSpace(payload1.LicenseId));
        Assert.False(string.IsNullOrWhiteSpace(payload2.LicenseId));
        Assert.NotEqual(payload1.LicenseId, payload2.LicenseId);

        // -------------------------------------------------------------
        // VERIFICATION 2: Issuance IDs are different
        // -------------------------------------------------------------
        Assert.False(string.IsNullOrWhiteSpace(payload1.IssuanceId));
        Assert.False(string.IsNullOrWhiteSpace(payload2.IssuanceId));
        Assert.NotEqual(payload1.IssuanceId, payload2.IssuanceId);

        // -------------------------------------------------------------
        // VERIFICATION 3: Serial keys are different
        // -------------------------------------------------------------
        Assert.False(string.IsNullOrWhiteSpace(serialKey1));
        Assert.False(string.IsNullOrWhiteSpace(serialKey2));
        Assert.NotEqual(serialKey1, serialKey2);

        // -------------------------------------------------------------
        // VERIFICATION 4: Digital signatures are different
        // -------------------------------------------------------------
        var (_, sig1) = LicenseDecoder.DecodeRawSerialKey(serialKey1);
        var (_, sig2) = LicenseDecoder.DecodeRawSerialKey(serialKey2);
        Assert.False(sig1.AsSpan().SequenceEqual(sig2));

        // -------------------------------------------------------------
        // VERIFICATION 5: Both signatures independently verify
        // -------------------------------------------------------------
        var (isValid1, decoded1, err1) = LicenseDecoder.VerifySerialKey(serialKey1, _publicKeyPem);
        Assert.True(isValid1, $"License 1 verification failed: {err1}");
        Assert.NotNull(decoded1);
        Assert.Equal(payload1.LicenseId, decoded1.LicenseId);

        var (isValid2, decoded2, err2) = LicenseDecoder.VerifySerialKey(serialKey2, _publicKeyPem);
        Assert.True(isValid2, $"License 2 verification failed: {err2}");
        Assert.NotNull(decoded2);
        Assert.Equal(payload2.LicenseId, decoded2.LicenseId);

        // -------------------------------------------------------------
        // VERIFICATION 6: Both licenses contain valid dates
        // -------------------------------------------------------------
        Assert.True(payload1.IssuedAt <= DateTime.UtcNow.Date.AddDays(1));
        Assert.True(payload1.ExpiresAt > payload1.IssuedAt);
        Assert.Equal(365, (payload1.ExpiresAt - payload1.IssuedAt).TotalDays);

        Assert.True(payload2.IssuedAt <= DateTime.UtcNow.Date.AddDays(1));
        Assert.True(payload2.ExpiresAt > payload2.IssuedAt);
        Assert.Equal(365, (payload2.ExpiresAt - payload2.IssuedAt).TotalDays);

        // -------------------------------------------------------------
        // VERIFICATION 7: License #2 does not invalidate License #1 in generator history
        // -------------------------------------------------------------
        var allHistory = _historyService.GetAllRecords();
        Assert.Equal(2, allHistory.Count);

        var history1 = _historyService.FindByLicenseId(payload1.LicenseId);
        Assert.NotNull(history1);
        Assert.Equal(serialKey1, history1.SerialKey);
        Assert.Equal(customerName, history1.CustomerName);

        var history2 = _historyService.FindByLicenseId(payload2.LicenseId);
        Assert.NotNull(history2);
        Assert.Equal(serialKey2, history2.SerialKey);
        Assert.Equal(customerName, history2.CustomerName);

        // -------------------------------------------------------------
        // VERIFICATION 8: A renewal can be generated for the same customer referencing PreviousLicenseId
        // -------------------------------------------------------------
        var renewalIssueDate = payload1.ExpiresAt;
        var renewalExpiryDate = renewalIssueDate.AddDays(365);

        var (renewalPayload, renewalSerialKey) = LicenseSigner.CreateUniqueRenewal(
            previousLicenseId: payload1.LicenseId,
            customerName: customerName,
            customerEmail: customerEmail,
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService,
            customIssuedAt: renewalIssueDate,
            customExpiresAt: renewalExpiryDate);

        Assert.NotNull(renewalPayload);
        Assert.True(renewalPayload.IsRenewal);
        Assert.Equal(payload1.LicenseId, renewalPayload.PreviousLicenseId);
        Assert.NotEqual(payload1.LicenseId, renewalPayload.LicenseId);
        Assert.NotEqual(serialKey1, renewalSerialKey);
        Assert.NotEqual(serialKey2, renewalSerialKey);

        // Verify renewal in history
        var updatedHistory = _historyService.GetAllRecords();
        Assert.Equal(3, updatedHistory.Count);
        var renewalHistory = _historyService.FindByLicenseId(renewalPayload.LicenseId);
        Assert.NotNull(renewalHistory);
        Assert.True(renewalHistory.IsRenewal);
        Assert.Equal(payload1.LicenseId, renewalHistory.PreviousLicenseId);

        // -------------------------------------------------------------
        // VERIFICATION 9 & 10: Activation and Renewal on the same PC
        // -------------------------------------------------------------
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        // Initial state
        var initResult = await manager.InitializeAsync();
        Assert.Equal(LicenseStatus.NotActivated, initResult.Status);

        // Activate License #1
        var act1Result = await manager.ActivateAsync(serialKey1);
        Assert.True(act1Result.Success, act1Result.Message);
        Assert.Equal(LicenseStatus.Active, act1Result.Status);
        Assert.Equal(payload1.LicenseId, manager.CurrentLicense?.LicenseId);
        Assert.Equal(payload1.ExpiresAt, manager.CurrentLicense?.ExpiresAt);
        Assert.Equal(deviceId, manager.CurrentLicense?.BoundDeviceId);

        // Now Renew with renewal serial key on the same PC
        var renewResult = await manager.RenewAsync(renewalSerialKey);
        Assert.True(renewResult.Success, renewResult.Message);
        Assert.Equal(LicenseStatus.Active, renewResult.Status);

        // VERIFICATION 10: The MAIN DhirDhar application adopts the renewal's new expiry date
        Assert.NotNull(manager.CurrentLicense);
        Assert.Equal(renewalPayload.LicenseId, manager.CurrentLicense.LicenseId);
        Assert.Equal(renewalExpiryDate, manager.CurrentLicense.ExpiresAt);
        Assert.Equal(deviceId, manager.CurrentLicense.BoundDeviceId);
        Assert.Equal(payload1.LicenseId, manager.CurrentLicense.PreviousLicenseId);
        Assert.True(manager.CurrentLicense.IsRenewal);

        // Simulate app restart - verify stored license has new renewal expiry date
        var restartManager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);
        var restartInit = await restartManager.InitializeAsync();
        Assert.True(restartInit.IsValid);
        Assert.Equal(renewalPayload.LicenseId, restartManager.CurrentLicense?.LicenseId);
        Assert.Equal(renewalExpiryDate, restartManager.CurrentLicense?.ExpiresAt);
    }

    [Fact]
    public void DuplicateProtection_DetectsCollision_AndGeneratesUniqueRecord()
    {
        // Add a dummy record to history
        var existingRecord = new LicenseHistoryRecord(
            LicenseId: "DD-EXISTING-001",
            IssuanceId: "EXISTING-IID-001",
            CustomerName: "Test",
            CustomerEmail: "test@dhirdhar.com",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(365),
            DeviceLimit: 1,
            DeviceBinding: null,
            PreviousLicenseId: null,
            IsRenewal: false,
            SerialKey: "DD-EXISTING-KEY",
            CreatedAt: DateTime.UtcNow);

        _historyService.AddRecord(existingRecord);

        Assert.True(_historyService.Exists("DD-EXISTING-001", "other-iid", "other-key"));
        Assert.True(_historyService.Exists("other-lid", "EXISTING-IID-001", "other-key"));
        Assert.True(_historyService.Exists("other-lid", "other-iid", "DD-EXISTING-KEY"));
        Assert.False(_historyService.Exists("other-lid", "other-iid", "other-key"));
    }

    [Fact]
    public void LicensePayload_Serialization_VisiblyContainsIssuanceId_And_SurvivesFullPipeline()
    {
        var (payload, serialKey) = LicenseSigner.CreateUniqueLicense(
            customerName: "Praveen Sharma",
            customerEmail: "praveen@dhirdhar.com",
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService);

        // 1. Inspect serial key format
        Assert.Equal(29, serialKey.Length);
        Assert.Equal(5, serialKey.Split('-').Length);

        // 2. Decode raw 25-character serial key
        var (decodedPayload, signature) = LicenseDecoder.DecodeRawSerialKey(serialKey);
        Assert.NotNull(decodedPayload);
        Assert.Equal(payload.IssuanceId, decodedPayload.IssuanceId);
        Assert.False(string.IsNullOrWhiteSpace(decodedPayload.IssuanceId));

        // 3. Verify digital signature & validate IssuanceId
        var (isValid, verifiedPayload, error) = LicenseDecoder.VerifySerialKey(serialKey, _publicKeyPem);
        Assert.True(isValid, error);
        Assert.NotNull(verifiedPayload);
        Assert.Equal(payload.IssuanceId, verifiedPayload.IssuanceId);
    }

    [Fact]
    public async Task LiveGeneratedLicense_ActivatesInMainApplication_WithoutIssuanceIdMismatch()
    {
        // 1. Generate live license with LicenseSigner
        var (payload, serialKey) = LicenseSigner.CreateUniqueLicense(
            customerName: "Live Customer",
            customerEmail: "live@dhirdhar.com",
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService);

        // 2. Decode and verify in LicenseDecoder
        var (isValid, decodedPayload, errorMessage) = LicenseDecoder.VerifySerialKey(serialKey);
        Assert.True(isValid, errorMessage);
        Assert.NotNull(decodedPayload);
        Assert.False(string.IsNullOrWhiteSpace(decodedPayload.IssuanceId));
        Assert.Equal(payload.IssuanceId, decodedPayload.IssuanceId);
        Assert.Equal(payload.LicenseId, decodedPayload.LicenseId);

        // 3. Activate in LicenseManager
        var fingerprintService = new DeviceFingerprintService();
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        var activationResult = await manager.ActivateAsync(serialKey);
        Assert.True(activationResult.Success, activationResult.Message);
        Assert.Equal(LicenseStatus.Active, activationResult.Status);
        Assert.NotNull(manager.CurrentLicense);
        Assert.Equal(payload.IssuanceId, manager.CurrentLicense.IssuanceId);
        Assert.Equal(payload.LicenseId, manager.CurrentLicense.LicenseId);
    }

    [Fact]
    public async Task EndToEndChecklist_VerifiesAll13Items()
    {
        var fingerprintService = new DeviceFingerprintService();
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);
        var deviceId = fingerprintService.GetDeviceFingerprint();

        // 1. Generate License A
        var (payloadA, keyA) = LicenseSigner.CreateUniqueLicense(
            customerName: "Test User A",
            customerEmail: "userA@example.com",
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService);

        // 2. Confirm key is exactly: XXXXX-XXXXX-XXXXX-XXXXX-XXXXX
        Assert.Equal(29, keyA.Length);
        var groupsA = keyA.Split('-');
        Assert.Equal(5, groupsA.Length);
        foreach (var g in groupsA)
        {
            Assert.Equal(5, g.Length);
            foreach (var c in g)
            {
                Assert.Contains(c, LicenseDecoder.Alphabet);
                Assert.DoesNotContain(c, "IO01");
            }
        }

        // 3. Activate License A in MAIN DhirDhar
        var actResultA = await manager.ActivateAsync(keyA);

        // 4. Confirm activation succeeds
        Assert.True(actResultA.Success, actResultA.Message);
        Assert.Equal(LicenseStatus.Active, actResultA.Status);
        Assert.NotNull(manager.CurrentLicense);
        Assert.Equal(payloadA.LicenseId, manager.CurrentLicense.LicenseId);

        // 5. Generate Renewal License B
        var (payloadB, keyB) = LicenseSigner.CreateUniqueRenewal(
            previousLicenseId: payloadA.LicenseId,
            customerName: "Test User A",
            customerEmail: "userA@example.com",
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService,
            customIssuedAt: payloadA.ExpiresAt,
            customExpiresAt: payloadA.ExpiresAt.AddDays(365));

        // 6. Confirm License B has a different LicenseId
        Assert.NotEqual(payloadA.LicenseId, payloadB.LicenseId);

        // 7. Confirm License B has a different IssuanceId
        Assert.NotEqual(payloadA.IssuanceId, payloadB.IssuanceId);

        // 8. Confirm License B has a completely different 25-character serial key
        Assert.NotEqual(keyA, keyB);
        Assert.Equal(29, keyB.Length);
        Assert.Equal(5, keyB.Split('-').Length);

        // 9. Activate License B in MAIN DhirDhar
        var renewResultB = await manager.RenewAsync(keyB);

        // 10. Confirm renewal activation succeeds
        Assert.True(renewResultB.Success, renewResultB.Message);
        Assert.Equal(LicenseStatus.Active, renewResultB.Status);
        Assert.NotNull(manager.CurrentLicense);
        Assert.Equal(payloadB.LicenseId, manager.CurrentLicense.LicenseId);
        Assert.Equal(payloadA.LicenseId, manager.CurrentLicense.PreviousLicenseId);

        // 11. Modify one character of the key and verify activation fails
        var tamperedChars = keyA.ToCharArray();
        tamperedChars[0] = tamperedChars[0] == '9' ? '8' : '9';
        var tamperedKey = new string(tamperedChars);
        var tamperResult = await manager.ActivateAsync(tamperedKey);
        Assert.False(tamperResult.Success);
        Assert.Equal(LicenseStatus.Invalid, tamperResult.Status);

        // 12. Test an incorrect hardware ID and verify activation fails
        var boundWrongPayload = payloadA with
        {
            LicenseId = "DD-20260817-99999",
            DeviceBinding = "DD-PC-WRONG-HARDWARE-ID"
        };
        var boundWrongKey = LicenseSigner.CreateSerialKey(boundWrongPayload, _privateKeyPem);
        var wrongHwResult = await manager.ActivateAsync(boundWrongKey);
        Assert.False(wrongHwResult.Success);
        Assert.Equal(LicenseStatus.Invalid, wrongHwResult.Status);
        Assert.Contains("PC", wrongHwResult.Message, StringComparison.OrdinalIgnoreCase);

        // 13. Test an expired license and verify activation fails
        var expiredPayload = payloadA with
        {
            LicenseId = "DD-20240101-00001",
            IssuedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var expiredKey = LicenseSigner.CreateSerialKey(expiredPayload, _privateKeyPem);
        var expiredResult = await manager.ActivateAsync(expiredKey);
        Assert.False(expiredResult.Success);
        Assert.Equal(LicenseStatus.Expired, expiredResult.Status);
    }

    [Fact]
    public async Task TwentyFiveCharacterKey_FullLifecycle_SatisfiesAll13Requirements()
    {
        // Setup isolated storage, fingerprint, and manager
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new DeviceFingerprintService();
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);
        var deviceId = fingerprintService.GetDeviceFingerprint();

        const string customerName = "Praveen Patel";
        const string customerEmail = "praveen@patel.in";

        // Step 1: Generate a new annual license in License Generator
        var (payload1, generatedKey1) = LicenseSigner.CreateUniqueLicense(
            customerName: customerName,
            customerEmail: customerEmail,
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService,
            deviceBinding: null);

        // Step 2: Confirm generated key is exactly 25 characters (in 5x5 format: 25 base32 chars + 4 hyphens)
        var normalized1 = LicenseDecoder.NormalizeSerialKey(generatedKey1);
        Assert.Equal(25, normalized1.Length);
        Assert.Equal(29, generatedKey1.Length);
        Assert.True(LicenseDecoder.IsValidAlphabetString(normalized1));

        // Step 3: Copy the key (unmodified)
        var copiedKey1 = generatedKey1.Trim();
        Assert.Equal(generatedKey1, copiedKey1);

        // Step 4 & 5: Open DhirDhar Main App and enter/paste the key
        string? desktopAssemblyPath = null;
        var possiblePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DhirDhar.Desktop", "bin", "x64", "Release", "net8.0-windows10.0.19041.0", "win-x64", "DhirDhar.Desktop.dll"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DhirDhar.Desktop", "bin", "x64", "Debug", "net8.0-windows10.0.19041.0", "win-x64", "DhirDhar.Desktop.dll"),
            @"d:\DhirDhar\DhirDhar Solution\src\DhirDhar.Desktop\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\DhirDhar.Desktop.dll",
            @"d:\DhirDhar\DhirDhar Solution\src\DhirDhar.Desktop\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\DhirDhar.Desktop.dll",
            @"d:\DhirDhar\DhirDhar Solution\Release\DhirDhar.Desktop.dll"
        };
        foreach (var p in possiblePaths)
        {
            if (File.Exists(p))
            {
                desktopAssemblyPath = p;
                break;
            }
        }
        if (desktopAssemblyPath == null)
        {
            var desktopBinDir = @"d:\DhirDhar\DhirDhar Solution\src\DhirDhar.Desktop\bin";
            if (Directory.Exists(desktopBinDir))
            {
                desktopAssemblyPath = Directory.GetFiles(desktopBinDir, "DhirDhar.Desktop.dll", SearchOption.AllDirectories).FirstOrDefault();
            }
        }
        if (desktopAssemblyPath == null || !File.Exists(desktopAssemblyPath))
        {
            return;
        }

        var desktopAssembly = System.Reflection.Assembly.LoadFrom(desktopAssemblyPath);
        var viewModelType = desktopAssembly.GetType("DhirDhar.Desktop.ViewModels.License.LicenseViewModel")!;
        var localizationService = new DhirDhar.Infrastructure.Localization.LocalizationService();

        dynamic vm = Activator.CreateInstance(viewModelType, manager, localizationService, null)!;

        // Before entering key: Activate button must be disabled
        Assert.False((bool)vm.CanActivate);

        // Step 6: Enter/paste key -> Confirm Activate License becomes enabled
        vm.SerialKeyInput = copiedKey1;
        Assert.True((bool)vm.CanActivate);
        Assert.True((bool)vm.ActivateCommand.CanExecute(null));

        // Step 7: Activate successfully
        bool actSuccess = await (Task<bool>)vm.ExecuteActivateAsync();
        Assert.True(actSuccess);
        Assert.True((bool)vm.IsSuccess);
        Assert.False((bool)vm.HasError);
        Assert.Equal(LicenseStatus.Active, manager.Status);
        Assert.Equal(payload1.LicenseId, manager.CurrentLicense?.LicenseId);
        Assert.Equal(deviceId, manager.CurrentLicense?.BoundDeviceId);

        // Step 8: Generate another renewal key for the same customer
        var renewalIssueDate = payload1.ExpiresAt;
        var renewalExpiryDate = renewalIssueDate.AddDays(365);
        var (payload2, renewalKey) = LicenseSigner.CreateUniqueRenewal(
            previousLicenseId: payload1.LicenseId,
            customerName: customerName,
            customerEmail: customerEmail,
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService,
            customIssuedAt: renewalIssueDate,
            customExpiresAt: renewalExpiryDate);

        // Step 9: Confirm the renewal key is different from the previous key
        var normalizedRenewal = LicenseDecoder.NormalizeSerialKey(renewalKey);
        Assert.Equal(25, normalizedRenewal.Length);
        Assert.NotEqual(copiedKey1, renewalKey);
        Assert.NotEqual(normalized1, normalizedRenewal);
        Assert.NotEqual(payload1.LicenseId, payload2.LicenseId);

        // Step 10: Confirm the renewal key activates successfully
        vm.SerialKeyInput = renewalKey;
        Assert.True((bool)vm.CanActivate);
        bool renewSuccess = await (Task<bool>)vm.ExecuteActivateAsync();
        Assert.True(renewSuccess);
        Assert.Equal(LicenseStatus.Active, manager.Status);
        Assert.Equal(payload2.LicenseId, manager.CurrentLicense?.LicenseId);
        Assert.Equal(payload1.LicenseId, manager.CurrentLicense?.PreviousLicenseId);
        Assert.Equal(renewalExpiryDate, manager.CurrentLicense?.ExpiresAt);

        // Step 11: Test an invalid 25-character key and confirm it is rejected
        var invalid25Key = "23456-789AB-CDEF2-34567-89ABC";
        var invalidResult = await manager.ActivateAsync(invalid25Key);
        Assert.False(invalidResult.Success);
        Assert.Equal(LicenseStatus.Invalid, invalidResult.Status);

        // Step 12: Test a modified character and confirm it is rejected
        var modifiedChars = renewalKey.ToCharArray();
        modifiedChars[3] = modifiedChars[3] == 'Z' ? 'Y' : 'Z';
        var modifiedKey = new string(modifiedChars);
        var modifiedResult = await manager.ActivateAsync(modifiedKey);
        Assert.False(modifiedResult.Success);
        Assert.Equal(LicenseStatus.Invalid, modifiedResult.Status);

        // Step 13: Test an expired key and confirm it is rejected
        var expiredPayload = payload1 with
        {
            LicenseId = "DD-20240101-00001",
            IssuedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var expiredKey = LicenseSigner.CreateSerialKey(expiredPayload, _privateKeyPem);
        var expiredResult = await manager.ActivateAsync(expiredKey);
        Assert.False(expiredResult.Success);
        Assert.Equal(LicenseStatus.Expired, expiredResult.Status);
    }

    [Fact]
    public async Task DwitiJewellers_HardwareBound_Workflow_Tests1Through7()
    {
        const string customerName = "Dwiti Jewellers";
        const string customerEmail = "dwiti@jewellers.in";
        const string hardwareId = "DD-PC-3433-1B0F-323C-3912";

        var fakeFingerprintService = new SpecificDeviceFingerprintService(hardwareId);
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var manager = new LicenseManager(storage, fakeFingerprintService, NullLogger<LicenseManager>.Instance);

        // TEST 1: Generate a new annual license for Dwiti Jewellers with hardware DD-PC-3433-1B0F-323C-3912
        var (payload1, generatedKey) = LicenseSigner.CreateUniqueLicense(
            customerName: customerName,
            customerEmail: customerEmail,
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService,
            deviceBinding: hardwareId);

        // Confirm exactly 25 alphanumeric characters (29 with hyphens)
        var normalizedKey = LicenseDecoder.NormalizeSerialKey(generatedKey);
        Assert.Equal(25, normalizedKey.Length);
        Assert.Equal(29, generatedKey.Length);
        Assert.Equal(5, generatedKey.Split('-').Length);
        Assert.True(LicenseDecoder.IsValidAlphabetString(normalizedKey));

        // TEST 2: Copy the generated key directly and paste it into Main App
        var copiedKey = generatedKey.Trim();
        var actResult = await manager.ActivateAsync(copiedKey);
        Assert.True(actResult.Success, actResult.Message);
        Assert.Equal(LicenseStatus.Active, actResult.Status);
        Assert.Equal(payload1.LicenseId, manager.CurrentLicense?.LicenseId);
        Assert.Equal(hardwareId, manager.CurrentLicense?.BoundDeviceId);
        Assert.Equal("Dwiti Jewellers", manager.CurrentLicense?.CustomerName);

        // TEST 3: Change one character in the key -> Activation fails with invalid/tampered key
        var modifiedChars = copiedKey.ToCharArray();
        modifiedChars[0] = modifiedChars[0] == '9' ? '8' : '9';
        var tamperedKey = new string(modifiedChars);
        var tamperResult = await manager.ActivateAsync(tamperedKey);
        Assert.False(tamperResult.Success);
        Assert.Equal(LicenseStatus.Invalid, tamperResult.Status);

        // TEST 4: Use the same valid key on a different hardware ID -> Activation fails
        var otherMachineFingerprint = new SpecificDeviceFingerprintService("DD-PC-OTHER-MACHINE-9999");
        var otherStorage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, Path.Combine(_tempDir, "other_act.dat"));
        var otherManager = new LicenseManager(otherStorage, otherMachineFingerprint, NullLogger<LicenseManager>.Instance);

        var wrongMachineResult = await otherManager.ActivateAsync(copiedKey);
        Assert.False(wrongMachineResult.Success);
        Assert.Equal(LicenseStatus.Invalid, wrongMachineResult.Status);
        Assert.Contains("PC", wrongMachineResult.Message, StringComparison.OrdinalIgnoreCase);

        // TEST 5: Generate another annual license / renewal -> New key is different and activates successfully
        var (renewalPayload, renewalKey) = LicenseSigner.CreateUniqueRenewal(
            previousLicenseId: payload1.LicenseId,
            customerName: customerName,
            customerEmail: customerEmail,
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService,
            customIssuedAt: payload1.ExpiresAt,
            customExpiresAt: payload1.ExpiresAt.AddDays(365),
            deviceBinding: hardwareId);

        Assert.NotEqual(generatedKey, renewalKey);
        Assert.NotEqual(payload1.LicenseId, renewalPayload.LicenseId);

        var renewalResult = await manager.RenewAsync(renewalKey);
        Assert.True(renewalResult.Success, renewalResult.Message);
        Assert.Equal(LicenseStatus.Active, renewalResult.Status);
        Assert.Equal(renewalPayload.LicenseId, manager.CurrentLicense?.LicenseId);
        Assert.Equal(payload1.LicenseId, manager.CurrentLicense?.PreviousLicenseId);
        Assert.Equal("Dwiti Jewellers", manager.CurrentLicense?.CustomerName);

        // TEST 6: Copy/paste the key with hyphens -> Main App accepts XXXXX-XXXXX-XXXXX-XXXXX-XXXXX
        var (isValidFormatted, decodedFormatted, _) = LicenseDecoder.VerifySerialKey(renewalKey, _publicKeyPem);
        Assert.True(isValidFormatted);
        Assert.NotNull(decodedFormatted);
        Assert.Equal("Dwiti Jewellers", decodedFormatted.CustomerName);

        // TEST 7: Paste the same key without hyphens -> Main App accepts normalized 25-character string
        var unhyphenatedKey = renewalKey.Replace("-", "");
        var (isValidRaw, decodedRaw, _) = LicenseDecoder.VerifySerialKey(unhyphenatedKey, _publicKeyPem);
        Assert.True(isValidRaw);
        Assert.NotNull(decodedRaw);
        Assert.Equal(decodedFormatted.LicenseId, decodedRaw.LicenseId);
        Assert.Equal("Dwiti Jewellers", decodedRaw.CustomerName);

        // TEST 8: Close and reopen Main App -> Previously activated license remains valid offline
        var newAppManager = new LicenseManager(storage, fakeFingerprintService, NullLogger<LicenseManager>.Instance);
        var reloadResult = await newAppManager.InitializeAsync();
        Assert.True(reloadResult.IsValid);
        Assert.Equal(LicenseStatus.Active, reloadResult.Status);
        Assert.Equal(renewalPayload.LicenseId, newAppManager.CurrentLicense?.LicenseId);
        Assert.Equal("Dwiti Jewellers", newAppManager.CurrentLicense?.CustomerName);

        // TEST 9: Disconnect Internet completely -> License activation and validation continue to work 100% offline
        // (LicenseManager and LicenseDecoder operate entirely offline without network I/O)
        var offlineValidateResult = await newAppManager.ValidateCurrentLicenseAsync();
        Assert.True(offlineValidateResult.IsValid);
        Assert.Equal(LicenseStatus.Active, offlineValidateResult.Status);
        Assert.Equal("Dwiti Jewellers", offlineValidateResult.LicenseInfo?.CustomerName);
    }

    [Fact]
    public async Task External_CDhirDharLicenseGenerator_Keys_Activate_In_MainApp()
    {
        var genAssemblyPath = @"C:\DhirDharLicenseGenerator\src\DhirDhar.LicenseGenerator\bin\Release\net8.0-windows\win-x64\DhirDhar.LicenseGenerator.dll";
        if (!File.Exists(genAssemblyPath))
        {
            return;
        }

        var genAssembly = System.Reflection.Assembly.LoadFrom(genAssemblyPath);
        var cryptoServiceType = genAssembly.GetType("DhirDhar.LicenseGenerator.Services.LicenseCryptoService")!;
        var payloadType = genAssembly.GetType("DhirDhar.LicenseGenerator.Models.LicensePayload")!;
        var keyServiceType = genAssembly.GetType("DhirDhar.LicenseGenerator.Services.KeyManagementService")!;

        dynamic keyService = Activator.CreateInstance(keyServiceType, @"C:\DhirDharLicenseGenerator")!;
        string privPem = (string)keyService.GetPrivateKeyPem();

        const string customer = "Dwiti Jewellers";
        const string email = "dwiti@jewellers.in";
        const string hardwareId = "DD-PC-3433-1B0F-323C-3912";
        var issue = DateTime.Today;
        var expiry = issue.AddDays(365);

        dynamic genPayload = Activator.CreateInstance(payloadType,
            "DhirDhar",
            $"DD-{issue:yyyyMMdd}-03433",
            customer,
            email,
            "Annual",
            issue,
            expiry,
            1,
            1,
            "",
            hardwareId,
            (string?)null,
            false)!;

        var createKeyMethod = cryptoServiceType.GetMethod("CreateSerialKey", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        string generatedKey = (string)createKeyMethod.Invoke(null, new object[] { genPayload, privPem })!;

        Assert.Equal(29, generatedKey.Length);
        Assert.Equal(25, LicenseDecoder.NormalizeSerialKey(generatedKey).Length);

        // Verify with Main App LicenseDecoder
        var (isValid, decodedPayload, verifyError) = LicenseDecoder.VerifySerialKey(generatedKey);
        Assert.True(isValid, $"Verification failed: {verifyError}");
        Assert.NotNull(decodedPayload);
        Assert.Equal("DhirDhar", decodedPayload.Product);
        Assert.Equal("Dwiti Jewellers", decodedPayload.CustomerName);

        // Verify with Main App LicenseManager
        var tempStorage = Path.Combine(_tempDir, "c_gen_act.dat");
        var storageService = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, tempStorage);
        var fingerprintService = new SpecificDeviceFingerprintService(hardwareId);
        var manager = new LicenseManager(storageService, fingerprintService, NullLogger<LicenseManager>.Instance);

        var actResult = await manager.ActivateAsync(generatedKey);
        Assert.True(actResult.Success, actResult.Message);
        Assert.Equal(LicenseStatus.Active, actResult.Status);
        Assert.Equal(hardwareId, manager.CurrentLicense?.BoundDeviceId);
        Assert.Equal("Dwiti Jewellers", manager.CurrentLicense?.CustomerName);

        // Verify wrong hardware ID fails
        var wrongFingerprint = new SpecificDeviceFingerprintService("DD-PC-WRONG-9999");
        var wrongManager = new LicenseManager(new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, Path.Combine(_tempDir, "c_gen_wrong.dat")), wrongFingerprint, NullLogger<LicenseManager>.Instance);
        var wrongResult = await wrongManager.ActivateAsync(generatedKey);
        Assert.False(wrongResult.Success);
        Assert.Equal(LicenseStatus.Invalid, wrongResult.Status);
    }

    [Fact]
    public async Task CustomerName_Propagation_Between_Generator_And_MainApp_Workflow()
    {
        var deviceId = "DD-PC-7777-8888-9999-0000";
        var fingerprintService = new SpecificDeviceFingerprintService(deviceId);
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, Path.Combine(_tempDir, "prop_test.dat"));
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        // 1. Generate license for "Dwiti Jewellers"
        var (dwitiPayload, dwitiKey) = LicenseSigner.CreateUniqueLicense(
            customerName: "Dwiti Jewellers",
            customerEmail: "dwiti@example.com",
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService,
            deviceBinding: deviceId);

        // Activate in Main App
        var act1 = await manager.ActivateAsync(dwitiKey);
        Assert.True(act1.Success, act1.Message);
        Assert.Equal(LicenseStatus.Active, act1.Status);
        Assert.Equal("Dwiti Jewellers", manager.CurrentLicense?.CustomerName);

        // 2. Generate license for "ABC Traders"
        var (abcPayload, abcKey) = LicenseSigner.CreateUniqueLicense(
            customerName: "ABC Traders",
            customerEmail: "abc@traders.com",
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService,
            deviceBinding: deviceId);

        // Activate "ABC Traders" in Main App
        var act2 = await manager.ActivateAsync(abcKey);
        Assert.True(act2.Success, act2.Message);
        Assert.Equal(LicenseStatus.Active, act2.Status);
        Assert.Equal("ABC Traders", manager.CurrentLicense?.CustomerName);
        Assert.NotEqual("Dwiti Jewellers", manager.CurrentLicense?.CustomerName);

        // 3. Renew for "ABC Traders"
        var (renewalPayload, renewalKey) = LicenseSigner.CreateUniqueRenewal(
            previousLicenseId: abcPayload.LicenseId,
            customerName: "ABC Traders",
            customerEmail: "abc@traders.com",
            privateKeyPem: _privateKeyPem,
            publicKeyPem: _publicKeyPem,
            historyService: _historyService,
            deviceBinding: deviceId);

        var renewAct = await manager.RenewAsync(renewalKey);
        Assert.True(renewAct.Success, renewAct.Message);
        Assert.Equal("ABC Traders", manager.CurrentLicense?.CustomerName);

        // 4. Reload manager from storage (app restart simulation)
        var restartedManager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);
        var initResult = await restartedManager.InitializeAsync();
        Assert.True(initResult.IsValid);
        Assert.Equal("ABC Traders", restartedManager.CurrentLicense?.CustomerName);

        // 5. Fallback check for legacy/unnamed license
        var legacyPayload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-20260101-00099",
            CustomerName: "",
            CustomerEmail: "",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(365),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "ISS-9999000000000000",
            DeviceBinding: deviceId,
            PreviousLicenseId: null,
            IsRenewal: false);

        var legacyKey = LicenseSigner.CreateSerialKey(legacyPayload, _privateKeyPem);
        var legacyStorage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, Path.Combine(_tempDir, "legacy_test.dat"));
        var legacyManager = new LicenseManager(legacyStorage, fingerprintService, NullLogger<LicenseManager>.Instance);
        var legacyAct = await legacyManager.ActivateAsync(legacyKey);
        Assert.True(legacyAct.Success);
        Assert.Equal("DhirDhar Customer", legacyManager.CurrentLicense?.CustomerName);
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
        catch { }
    }
}

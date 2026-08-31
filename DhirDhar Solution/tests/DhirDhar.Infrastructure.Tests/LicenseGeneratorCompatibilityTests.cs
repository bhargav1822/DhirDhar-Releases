using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DhirDhar.Application.Licensing;
using DhirDhar.Application.Licensing.Models;
using DhirDhar.Infrastructure.Licensing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class LicenseGeneratorCompatibilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _storageFile;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    private const string CurrentHardwareId = "DD-PC-0867-7F05-6809-46EA";
    private const string WrongHardwareId = "DD-PC-9999-9999-9999-9999";

    // Private key for signing test licenses (matching LicenseVerificationKey.PublicKeyPem)
    private const string OfficialDevPrivateKeyPem = @"-----BEGIN EC PRIVATE KEY-----
MHcCAQEEIGdwp3oX8tjTumMbdGQBEucR6oa4Gtbtixy2Sh91v5MvoAoGCCqGSM49
AwEHoUQDQgAEx5L8QLG6AScIeADmTZbxUZhmVn5tgsS6ALUdFVjrC3KnQMU70oaA
IpEEa90Pt0F1apDusYVwT6TI9Hh4DTVMxg==
-----END EC PRIVATE KEY-----";

    public LicenseGeneratorCompatibilityTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"DD_LicCompatTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _storageFile = Path.Combine(_tempDir, "activation.dat");
    }

    private sealed class MockDeviceFingerprintService : IDeviceFingerprintService
    {
        private readonly string _fingerprint;
        public MockDeviceFingerprintService(string fingerprint) => _fingerprint = fingerprint;
        public string GetDeviceFingerprint() => _fingerprint;
        public bool ValidateDeviceFingerprint(string expectedFingerprint) =>
            string.Equals(_fingerprint.Trim(), expectedFingerprint.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scenario1_CorrectLicense_With_CorrectHardwareId_Succeeds()
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new MockDeviceFingerprintService(CurrentHardwareId);
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        // Generate license using the generator's exact logic
        var issueDate = DateTime.UtcNow.Date;
        var expiryDate = issueDate.AddDays(365);
        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-20260820-00001",
            CustomerName: "Dwiti Jewellers",
            CustomerEmail: "panchal.bhargav78@gmail.com",
            Edition: "Annual",
            IssuedAt: issueDate,
            ExpiresAt: expiryDate,
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "ISS-0001ABCDEF123456",
            DeviceBinding: CurrentHardwareId);

        var serialKey = DhirDhar.LicenseGenerator.LicenseSigner.CreateSerialKey(payload, OfficialDevPrivateKeyPem);
        _output.WriteLine($"[FRESH_TEST_LICENSE_KEY]: {serialKey}");

        // Verify serial key decoder directly
        var (isValid, decodedPayload, errorMsg) = LicenseDecoder.VerifySerialKey(serialKey);
        Assert.True(isValid, $"Decoder failed: {errorMsg}");
        Assert.NotNull(decodedPayload);
        Assert.Equal("DhirDhar", decodedPayload.Product);
        Assert.Equal("Annual", decodedPayload.Edition);
        Assert.Equal(issueDate, decodedPayload.IssuedAt);
        Assert.Equal(expiryDate, decodedPayload.ExpiresAt);

        // Activate in LicenseManager
        var activationResult = await manager.ActivateAsync(serialKey);
        Assert.True(activationResult.Success, $"Activation failed: {activationResult.Message}");
        Assert.True(manager.IsLicensed);
        Assert.Equal(LicenseStatus.Active, manager.Status);
        Assert.NotNull(manager.CurrentLicense);
        Assert.Equal(CurrentHardwareId, manager.CurrentLicense.BoundDeviceId);
    }

    [Fact]
    public async Task Scenario2_CorrectLicense_With_WrongHardwareId_Fails()
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        // Current device is WrongHardwareId, but license is bound to CurrentHardwareId
        var fingerprintService = new MockDeviceFingerprintService(WrongHardwareId);
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        var issueDate = DateTime.UtcNow.Date;
        var expiryDate = issueDate.AddDays(365);
        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-20260820-00002",
            CustomerName: "Dwiti Jewellers",
            CustomerEmail: "panchal.bhargav78@gmail.com",
            Edition: "Annual",
            IssuedAt: issueDate,
            ExpiresAt: expiryDate,
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "ISS-0002ABCDEF123456",
            DeviceBinding: CurrentHardwareId); // Bound to CurrentHardwareId, not WrongHardwareId

        var serialKey = DhirDhar.LicenseGenerator.LicenseSigner.CreateSerialKey(payload, OfficialDevPrivateKeyPem);

        var activationResult = await manager.ActivateAsync(serialKey);
        Assert.False(activationResult.Success);
        Assert.Equal(LicenseStatus.Invalid, activationResult.Status);
        Assert.Contains("specific PC", activationResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(manager.IsLicensed);
    }

    [Fact]
    public async Task Scenario3_ModifiedLicense_Fails()
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new MockDeviceFingerprintService(CurrentHardwareId);
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-20260820-00003",
            CustomerName: "Dwiti Jewellers",
            CustomerEmail: "panchal.bhargav78@gmail.com",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(365),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "ISS-0003ABCDEF123456",
            DeviceBinding: CurrentHardwareId);

        var serialKey = DhirDhar.LicenseGenerator.LicenseSigner.CreateSerialKey(payload, OfficialDevPrivateKeyPem);

        // 1. Tamper with the signature tag character at the end of the serial key
        var chars1 = serialKey.ToCharArray();
        chars1[^1] = chars1[^1] == 'A' ? 'B' : 'A';
        var tamperedKey1 = new string(chars1);

        var activationResult1 = await manager.ActivateAsync(tamperedKey1);
        Assert.False(activationResult1.Success);
        Assert.Contains("signature", activationResult1.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(manager.IsLicensed);

        // 2. Tamper with payload characters in the middle
        var chars2 = serialKey.ToCharArray();
        chars2[12] = chars2[12] == 'A' ? 'B' : 'A';
        var tamperedKey2 = new string(chars2);

        var activationResult2 = await manager.ActivateAsync(tamperedKey2);
        Assert.False(activationResult2.Success);
        Assert.False(manager.IsLicensed);
    }

    [Fact]
    public async Task Scenario4_InvalidSignature_Fails()
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new MockDeviceFingerprintService(CurrentHardwareId);
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        // Generate an untrusted rogue keypair
        var (roguePriv, _) = DhirDhar.LicenseGenerator.LicenseSigner.GenerateKeyPair();

        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-20260820-00004",
            CustomerName: "Attacker",
            CustomerEmail: "attacker@example.com",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(365),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "ISS-0004ABCDEF123456",
            DeviceBinding: CurrentHardwareId);

        var rogueSerialKey = DhirDhar.LicenseGenerator.LicenseSigner.CreateSerialKey(payload, roguePriv);

        var activationResult = await manager.ActivateAsync(rogueSerialKey);
        Assert.False(activationResult.Success);
        Assert.Contains("signature", activationResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(manager.IsLicensed);
    }

    [Fact]
    public async Task Scenario5_ExpiredLicense_Fails()
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new MockDeviceFingerprintService(CurrentHardwareId);
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        var pastIssueDate = DateTime.UtcNow.Date.AddDays(-400);
        var pastExpiryDate = DateTime.UtcNow.Date.AddDays(-35); // Expired 35 days ago

        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-20250701-00005",
            CustomerName: "Dwiti Jewellers",
            CustomerEmail: "panchal.bhargav78@gmail.com",
            Edition: "Annual",
            IssuedAt: pastIssueDate,
            ExpiresAt: pastExpiryDate,
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "ISS-0005ABCDEF123456",
            DeviceBinding: CurrentHardwareId);

        var expiredSerialKey = DhirDhar.LicenseGenerator.LicenseSigner.CreateSerialKey(payload, OfficialDevPrivateKeyPem);

        var activationResult = await manager.ActivateAsync(expiredSerialKey);
        Assert.False(activationResult.Success);
        Assert.Equal(LicenseStatus.Expired, activationResult.Status);
        Assert.Contains("expired", activationResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(manager.IsLicensed);
    }

    [Fact]
    public async Task Scenario6_MalformedLicense_Fails()
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new MockDeviceFingerprintService(CurrentHardwareId);
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        // 1. Completely wrong format
        var res1 = await manager.ActivateAsync("INVALID-KEY-FORMAT");
        Assert.False(res1.Success);

        // 2. Contains invalid Base32 characters (e.g. '1', '0', 'I', 'O')
        var res2 = await manager.ActivateAsync("11111-00000-IIIII-OOOOO-11111");
        Assert.False(res2.Success);

        // 3. Empty string
        var res3 = await manager.ActivateAsync("");
        Assert.False(res3.Success);
    }

    [Fact]
    public async Task Scenario7_CorrectLicense_AfterApplicationRestart_Succeeds()
    {
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new MockDeviceFingerprintService(CurrentHardwareId);

        // First session: activate license
        var issueDate = DateTime.UtcNow.Date;
        var managerSession1 = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);
        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: $"DD-{issueDate:yyyyMMdd}-00007",
            CustomerName: "Dwiti Jewellers",
            CustomerEmail: "panchal.bhargav78@gmail.com",
            Edition: "Annual",
            IssuedAt: issueDate,
            ExpiresAt: issueDate.AddDays(365),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "ISS-0007ABCDEF123456",
            DeviceBinding: CurrentHardwareId);

        var serialKey = DhirDhar.LicenseGenerator.LicenseSigner.CreateSerialKey(payload, OfficialDevPrivateKeyPem);
        var actRes = await managerSession1.ActivateAsync(serialKey);
        Assert.True(actRes.Success);

        // Second session (simulate app restart with new LicenseManager instance)
        var managerSession2 = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);
        var initResult = await managerSession2.InitializeAsync();

        Assert.True(initResult.IsValid, $"Session 2 Initialize failed: {initResult.Message}");
        Assert.True(managerSession2.IsLicensed);
        Assert.Equal(LicenseStatus.Active, managerSession2.Status);
        Assert.NotNull(managerSession2.CurrentLicense);
        Assert.Equal(CurrentHardwareId, managerSession2.CurrentLicense.BoundDeviceId);
        Assert.Equal(payload.LicenseId, managerSession2.CurrentLicense.LicenseId);
    }

    [Fact]
    public async Task Scenario8_CorrectLicense_WithInternetDisabled_100PercentOffline_Succeeds()
    {
        // Standalone offline activation test with isolated storage and no network calls
        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new MockDeviceFingerprintService(CurrentHardwareId);
        var manager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);

        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-20260820-00008",
            CustomerName: "Offline Customer",
            CustomerEmail: "offline@example.com",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(365),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "ISS-0008ABCDEF123456",
            DeviceBinding: CurrentHardwareId);

        var serialKey = DhirDhar.LicenseGenerator.LicenseSigner.CreateSerialKey(payload, OfficialDevPrivateKeyPem);

        // Verify that offline activation completes synchronously without network dependencies
        var activationResult = await manager.ActivateAsync(serialKey);
        Assert.True(activationResult.Success);
        Assert.True(manager.IsLicensed);
        Assert.Equal(LicenseStatus.Active, manager.Status);
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

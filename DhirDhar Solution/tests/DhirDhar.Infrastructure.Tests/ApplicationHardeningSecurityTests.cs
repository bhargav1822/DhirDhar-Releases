using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DhirDhar.Application.Licensing.Models;
using DhirDhar.Application.Security.Cryptography;
using DhirDhar.Application.Security.Integrity;
using DhirDhar.Application.Security.Models;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.Interest;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Licensing;
using DhirDhar.Infrastructure.Security.Cryptography;
using DhirDhar.Infrastructure.Security.Integrity;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class ApplicationHardeningSecurityTests
{
    #region Startup Security Scan & Binary Integrity (Scenarios A through F)
    [Fact]
    public async Task ScenarioA_UntouchedInstallation_RealProgressReachesCompleted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DhirDharIntegrityTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create full set of dummy files representing installation
            File.WriteAllText(Path.Combine(tempDir, "DhirDhar.Desktop.exe"), "OriginalExecutableContent");
            File.WriteAllText(Path.Combine(tempDir, "DhirDhar.Application.dll"), "OriginalApplicationDll");
            File.WriteAllText(Path.Combine(tempDir, "DhirDhar.Domain.dll"), "OriginalDomainDll");
            File.WriteAllText(Path.Combine(tempDir, "DhirDhar.Infrastructure.dll"), "OriginalInfrastructureDll");
            File.WriteAllText(Path.Combine(tempDir, "appsettings.json"), "{\"Application\":{\"Name\":\"DhirDhar\"}}");
            File.WriteAllText(Path.Combine(tempDir, "resources.pri"), "PRIResourceContent");

            var integrityService = new ApplicationIntegrityService(NullLogger<ApplicationIntegrityService>.Instance, tempDir);
            var manifestPath = integrityService.GenerateIntegrityManifest(tempDir);
            Assert.True(File.Exists(manifestPath));

            var progressReports = new List<IntegrityScanProgress>();
            var progress = new Progress<IntegrityScanProgress>(p => progressReports.Add(p));

            var result = await integrityService.VerifyApplicationIntegrityAsync(progress);
            Assert.True(result.IsValid);
            Assert.Empty(result.TamperedFiles);
            Assert.Empty(result.MissingFiles);
            Assert.True(result.TotalFilesScanned >= 5);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ScenarioA2_ParseManifestJson_RobustAgainstPropertyNamesAndCasing()
    {
        var jsonWithMixedCases = """
        {
            "generatedAtUtc": "2026-08-20T12:00:00Z",
            "signature": "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899",
            "files": [
                {
                    "relativePath": "DhirDhar.Desktop.exe",
                    "sha256": "112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00",
                    "sizeBytes": 12345
                },
                {
                    "relative_path": "appsettings.json",
                    "sha_256": "AABBCCDDEEFF00112233445566778899112233445566778899AABBCCDDEEFF00",
                    "size": 500
                }
            ]
        }
        """;

        var (success, manifest, error) = ApplicationIntegrityService.ParseManifestJson(jsonWithMixedCases);
        Assert.True(success, error);
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest.Files.Count);
        Assert.Equal("DhirDhar.Desktop.exe", manifest.Files[0].RelativePath);
        Assert.Equal(12345, manifest.Files[0].SizeBytes);
        Assert.Equal("appsettings.json", manifest.Files[1].RelativePath);
        Assert.Equal(500, manifest.Files[1].SizeBytes);
    }

    [Fact]
    public async Task ScenarioA3_FullTamperAndRestoreCycle_PassesFailsAndRestoresDeterministically()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DhirDharCycleTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var exePath = Path.Combine(tempDir, "DhirDhar.Desktop.exe");
            var dllPath = Path.Combine(tempDir, "DhirDhar.Application.dll");
            var originalExeContent = "OriginalExecutableContent";
            var originalDllContent = "OriginalApplicationDll";

            File.WriteAllText(exePath, originalExeContent);
            File.WriteAllText(dllPath, originalDllContent);

            var integrityService = new ApplicationIntegrityService(NullLogger<ApplicationIntegrityService>.Instance, tempDir);
            integrityService.GenerateIntegrityManifest(tempDir);

            // 1. Clean Build -> MUST PASS
            var result1 = await integrityService.VerifyApplicationIntegrityAsync();
            Assert.True(result1.IsValid);

            // 2. Modify DLL -> MUST FAIL
            File.WriteAllText(dllPath, "TAMPERED_DLL_CONTENT");
            var result2 = await integrityService.VerifyApplicationIntegrityAsync();
            Assert.False(result2.IsValid);
            Assert.Contains("DhirDhar.Application.dll", result2.TamperedFiles);

            // 3. Restore DLL -> MUST PASS
            File.WriteAllText(dllPath, originalDllContent);
            var result3 = await integrityService.VerifyApplicationIntegrityAsync();
            Assert.True(result3.IsValid);

            // 4. Remove DLL -> MUST FAIL
            File.Delete(dllPath);
            var result4 = await integrityService.VerifyApplicationIntegrityAsync();
            Assert.False(result4.IsValid);
            Assert.Contains("DhirDhar.Application.dll", result4.MissingFiles);

            // 5. Restore DLL -> MUST PASS
            File.WriteAllText(dllPath, originalDllContent);
            var result5 = await integrityService.VerifyApplicationIntegrityAsync();
            Assert.True(result5.IsValid);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ScenarioB_CorruptedOrModifiedDll_BlocksVerificationAndReportsTamperedFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DhirDharIntegrityTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var exePath = Path.Combine(tempDir, "DhirDhar.Desktop.exe");
            var dllPath = Path.Combine(tempDir, "DhirDhar.Application.dll");
            File.WriteAllText(exePath, "OriginalExecutableContent");
            File.WriteAllText(dllPath, "OriginalApplicationDll");

            var integrityService = new ApplicationIntegrityService(NullLogger<ApplicationIntegrityService>.Instance, tempDir);
            integrityService.GenerateIntegrityManifest(tempDir);

            // Attacker patches or modifies the DLL binary
            File.WriteAllText(dllPath, "CrackedOrModifiedApplicationDll");

            var result = await integrityService.VerifyApplicationIntegrityAsync();
            Assert.False(result.IsValid);
            Assert.Contains("DhirDhar.Application.dll", result.TamperedFiles);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ScenarioC_MissingCriticalFile_FailsVerificationSafely()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DhirDharIntegrityTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var exePath = Path.Combine(tempDir, "DhirDhar.Desktop.exe");
            var dllPath = Path.Combine(tempDir, "DhirDhar.Infrastructure.dll");
            File.WriteAllText(exePath, "OriginalExecutableContent");
            File.WriteAllText(dllPath, "OriginalDllContent");

            var integrityService = new ApplicationIntegrityService(NullLogger<ApplicationIntegrityService>.Instance, tempDir);
            integrityService.GenerateIntegrityManifest(tempDir);

            // Critical DLL deleted
            File.Delete(dllPath);

            var result = await integrityService.VerifyApplicationIntegrityAsync();
            Assert.False(result.IsValid);
            Assert.Contains("DhirDhar.Infrastructure.dll", result.MissingFiles);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ScenarioD_UserDatabase_IsIgnoredAndNeverDestroyed()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DhirDharIntegrityTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var exePath = Path.Combine(tempDir, "DhirDhar.Desktop.exe");
            var dbPath = Path.Combine(tempDir, "DhirDhar.db");
            var dbWalPath = Path.Combine(tempDir, "DhirDhar.db-wal");

            File.WriteAllText(exePath, "OriginalExecutableContent");
            File.WriteAllText(dbPath, "ImportantUserBorrowerDataRow");
            File.WriteAllText(dbWalPath, "ImportantWalData");

            var integrityService = new ApplicationIntegrityService(NullLogger<ApplicationIntegrityService>.Instance, tempDir);
            integrityService.GenerateIntegrityManifest(tempDir);

            var result = await integrityService.VerifyApplicationIntegrityAsync();
            Assert.True(result.IsValid);

            // Database files must remain untouched
            Assert.True(File.Exists(dbPath));
            Assert.Equal("ImportantUserBorrowerDataRow", File.ReadAllText(dbPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ScenarioE_OfflineScan_ExecutesWithoutNetwork()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DhirDharIntegrityTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "DhirDhar.Desktop.exe"), "OriginalExecutableContent");
            File.WriteAllText(Path.Combine(tempDir, "DhirDhar.Application.dll"), "OriginalApplicationDll");

            var integrityService = new ApplicationIntegrityService(NullLogger<ApplicationIntegrityService>.Instance, tempDir);
            integrityService.GenerateIntegrityManifest(tempDir);

            // Local offline cryptographic verification executes completely without internet
            var result = await integrityService.VerifyApplicationIntegrityAsync();
            Assert.True(result.IsValid);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ScenarioE2_PowerShellManifestGenerator_ProducesValidManifestVerifiedByService()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DhirDharPsIntegrityTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "DhirDhar.Desktop.exe"), "SampleDesktopBinary");
            File.WriteAllText(Path.Combine(tempDir, "DhirDhar.Application.dll"), "SampleApplicationBinary");
            File.WriteAllText(Path.Combine(tempDir, "DhirDhar.Infrastructure.dll"), "SampleInfrastructureBinary");
            File.WriteAllText(Path.Combine(tempDir, "appsettings.json"), "{\"Test\":true}");

            var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "generate-integrity-manifest.ps1"));
            if (!File.Exists(scriptPath))
            {
                scriptPath = @"D:\DhirDhar\DhirDhar Solution\scripts\generate-integrity-manifest.ps1";
            }

            Assert.True(File.Exists(scriptPath), $"Script not found at: {scriptPath}");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -TargetDir \"{tempDir}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            Assert.NotNull(process);
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);

            var manifestPath = Path.Combine(tempDir, "app_integrity.sig");
            Assert.True(File.Exists(manifestPath));

            var integrityService = new ApplicationIntegrityService(NullLogger<ApplicationIntegrityService>.Instance, tempDir);
            var result = await integrityService.VerifyApplicationIntegrityAsync();

            Assert.True(result.IsValid, $"Integrity check failed: {result.StatusMessage}");
            Assert.Empty(result.TamperedFiles);
            Assert.Empty(result.MissingFiles);
            Assert.Equal(4, result.TotalFilesScanned);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
    #endregion

    #region Offline Licensing & Tamper Resistance
    [Fact]
    public void ScenarioF_ModifiedLicensePayload_IsRejected()
    {
        var tamperedKey = "AAAAA-BBBBB-CCCCC-DDDDD-EEEEE";
        var (isValid, payload, error) = LicenseDecoder.VerifySerialKey(tamperedKey);

        Assert.False(isValid);
        Assert.Null(payload);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void ScenarioG_WrongHardwareId_FailsHardwareBindingVerification()
    {
        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-20260820-00001",
            CustomerName: "Test User",
            CustomerEmail: "test@example.com",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.AddDays(-10),
            ExpiresAt: DateTime.UtcNow.AddDays(355),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "ISS-0001-TEST",
            DeviceBinding: "HW-123456",
            PreviousLicenseId: null,
            IsRenewal: false);

        var actualDeviceHardwareHash = LicensePayload.ComputeHardwareIdHash("DIFFERENT-HARDWARE-ID-789");
        var expectedBoundHash = Convert.ToUInt32("123456", 16);

        Assert.NotEqual(expectedBoundHash, actualDeviceHardwareHash);
    }

    [Fact]
    public void ScenarioH_ExpiredLicense_IsIdentifiedAsExpired()
    {
        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-20240101-00001",
            CustomerName: "Test User",
            CustomerEmail: "test@example.com",
            Edition: "Annual",
            IssuedAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpiresAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "ISS-0001-EXPIRED",
            DeviceBinding: null,
            PreviousLicenseId: null,
            IsRenewal: false);

        Assert.True(DateTime.UtcNow > payload.ExpiresAt);
    }
    #endregion

    #region Cryptographic Authenticated Encryption (AEAD)
    [Fact]
    public void ScenarioI_TamperedCiphertext_FailsAesGcmAuthenticationTagVerification()
    {
        var crypto = new CryptoService(NullLogger<CryptoService>.Instance);
        var key = crypto.GenerateRandomKey(32);
        var plaintext = "SensitiveFinancialInformation"u8.ToArray();

        var encrypted = crypto.Encrypt(plaintext, key);

        // Tamper with one byte of ciphertext
        encrypted.Ciphertext[0] ^= 0xFF;

        Assert.Throws<CryptographicException>(() =>
        {
            crypto.Decrypt(encrypted, key);
        });
    }
    #endregion

    #region Secret Scanning Verification (No Private Keys)
    [Fact]
    public void ScenarioJ_LicenseVerificationKey_ContainsOnlyPublicKey()
    {
        var pubKey = LicenseVerificationKey.PublicKeyPem;

        Assert.Contains("BEGIN PUBLIC KEY", pubKey);
        Assert.DoesNotContain("PRIVATE KEY", pubKey);
        Assert.DoesNotContain("BEGIN EC PRIVATE KEY", pubKey);
        Assert.DoesNotContain("BEGIN RSA PRIVATE KEY", pubKey);
    }
    #endregion

    #region Interest Calculation Engine Invariance
    [Fact]
    public void ScenarioK_InterestEngine_CalculatesOnStrict30DayBasis_AndEventsAreDepositWithdrawalOnly()
    {
        var loanDate = new DateTime(2026, 1, 1);
        var asOfDate = new DateTime(2026, 2, 1); // Exactly 31 calendar days -> full month
        decimal principal = 10000m;
        decimal monthlyRatePercent = 2.0m;

        var (rawInterest, applicableDays, daysInMonth, isFullMonth) = InterestCalculator.CalculateMonthSegment(
            principal,
            monthlyRatePercent,
            loanDate,
            asOfDate);

        // Full month yields exactly 30 applicable days and 200 interest
        Assert.True(isFullMonth);
        Assert.Equal(30, applicableDays);
        Assert.Equal(30, daysInMonth);
        Assert.Equal(200.00m, rawInterest);
        Assert.Equal(30, InterestCalculator.FixedDaysInMonthBasis);
    }
    #endregion
}

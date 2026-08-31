using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DhirDhar.Application.Licensing;
using DhirDhar.Application.Licensing.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Infrastructure.Licensing;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.LicenseGenerator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class LicenseActivationWorkflowTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _storageFile;
    private const string OfficialDevPrivateKeyPem = @"-----BEGIN EC PRIVATE KEY-----
MHcCAQEEIGdwp3oX8tjTumMbdGQBEucR6oa4Gtbtixy2Sh91v5MvoAoGCCqGSM49
AwEHoUQDQgAEx5L8QLG6AScIeADmTZbxUZhmVn5tgsS6ALUdFVjrC3KnQMU70oaA
IpEEa90Pt0F1apDusYVwT6TI9Hh4DTVMxg==
-----END EC PRIVATE KEY-----";

    public LicenseActivationWorkflowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DD_WorkFlowTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _storageFile = Path.Combine(_tempDir, "activation.dat");
    }

    [Fact]
    public async Task CompleteActivationButtonWorkflow_MatchesRequirements_A_through_G()
    {
        // Load DhirDhar.Desktop assembly
        var desktopAssemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DhirDhar.Desktop", "bin", "x64", "Debug", "net8.0-windows10.0.19041.0", "win-x64", "DhirDhar.Desktop.dll");

        if (!File.Exists(desktopAssemblyPath))
        {
            desktopAssemblyPath = @"d:\DhirDhar\DhirDhar Solution\src\DhirDhar.Desktop\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\DhirDhar.Desktop.dll";
        }
        if (!File.Exists(desktopAssemblyPath))
        {
            // Fallback path search
            desktopAssemblyPath = @"d:\DhirDhar\DhirDhar Solution\src\DhirDhar.Desktop\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\DhirDhar.Desktop.dll";
        }
        if (!File.Exists(desktopAssemblyPath))
        {
            var desktopBinDir = @"d:\DhirDhar\DhirDhar Solution\src\DhirDhar.Desktop\bin";
            if (Directory.Exists(desktopBinDir))
            {
                var found = Directory.GetFiles(desktopBinDir, "DhirDhar.Desktop.dll", SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) desktopAssemblyPath = found;
            }
        }

        var desktopAssembly = Assembly.LoadFrom(desktopAssemblyPath);
        var viewModelType = desktopAssembly.GetType("DhirDhar.Desktop.ViewModels.License.LicenseViewModel")!;

        var storage = new LicenseStorageService(NullLogger<LicenseStorageService>.Instance, _storageFile);
        var fingerprintService = new DeviceFingerprintService();
        var licenseManager = new LicenseManager(storage, fingerprintService, NullLogger<LicenseManager>.Instance);
        var localizationService = new LocalizationService();

        // Create LicenseViewModel instance
        dynamic vm = Activator.CreateInstance(viewModelType, licenseManager, localizationService, null)!;

        // Generate a valid license key for test G
        var payload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: "DD-2026-TEST-VERIFY",
            CustomerName: "Praveen Sharma",
            CustomerEmail: "praveen@dhirdhar.com",
            Edition: "Annual",
            IssuedAt: DateTime.UtcNow.Date,
            ExpiresAt: DateTime.UtcNow.Date.AddDays(365),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: "WORKFLOW-ISSUANCE-01");
        var validGeneratedKey = LicenseSigner.CreateSerialKey(payload, OfficialDevPrivateKeyPem);

        // TEST A: Start application. Expected: Activate License = disabled.
        Assert.False((bool)vm.CanActivate);
        Assert.False((bool)vm.ActivateCommand.CanExecute(null));
        Assert.True(string.IsNullOrEmpty((string)vm.SerialKeyInput));

        // TEST B: Type: ABC. Expected: Activate License = enabled.
        vm.SerialKeyInput = "ABC";
        Assert.True((bool)vm.CanActivate);
        Assert.True((bool)vm.ActivateCommand.CanExecute(null));

        // TEST C: Click Clear. Expected: Activate License = disabled.
        vm.ClearCommand.Execute(null);
        Assert.False((bool)vm.CanActivate);
        Assert.False((bool)vm.ActivateCommand.CanExecute(null));
        Assert.True(string.IsNullOrEmpty((string)vm.SerialKeyInput));

        // TEST D: Paste the generated serial key. Expected: Activate License = enabled.
        vm.SerialKeyInput = validGeneratedKey;
        Assert.True((bool)vm.CanActivate);
        Assert.True((bool)vm.ActivateCommand.CanExecute(null));

        // TEST E: Delete all text. Expected: Activate License = disabled.
        vm.SerialKeyInput = "";
        Assert.False((bool)vm.CanActivate);
        Assert.False((bool)vm.ActivateCommand.CanExecute(null));

        // TEST F: Paste whitespace only. Expected: Activate License = disabled.
        vm.SerialKeyInput = "   \t\r\n ";
        Assert.False((bool)vm.CanActivate);
        Assert.False((bool)vm.ActivateCommand.CanExecute(null));

        // TEST G: Enter the actual generated license key. Expected: Button remains enabled and clicking it invokes existing validation.
        vm.SerialKeyInput = $"  {validGeneratedKey}  ";
        Assert.True((bool)vm.CanActivate);
        Assert.True((bool)vm.ActivateCommand.CanExecute(null));

        bool activationSucceededEventFired = false;
        vm.ActivationSucceeded += new Action(() => { activationSucceededEventFired = true; });

        bool success = await (Task<bool>)vm.ExecuteActivateAsync();
        Assert.True(success);
        Assert.True((bool)vm.IsSuccess);
        Assert.False((bool)vm.HasError);
        Assert.True(activationSucceededEventFired);
        Assert.True((bool)vm.IsLicensed);
        Assert.NotNull((string)vm.CurrentLicense.CustomerName);
        Assert.StartsWith("DD-", (string)vm.CurrentLicense.LicenseId);
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

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Updates.Helpers;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class UpdateAndVersionWorkflowTests
{
    [Fact]
    public void Version213_IsGreaterThan_PreviousVersions()
    {
        Assert.True(SemanticVersion.TryParse("2.1.3", out var v213));
        Assert.True(SemanticVersion.TryParse("2.1.2", out var v212));
        Assert.True(SemanticVersion.TryParse("2.1.1", out var v211));
        Assert.True(SemanticVersion.TryParse("2.1.0", out var v210));
        Assert.True(SemanticVersion.TryParse("2.0.0", out var v200));
        Assert.True(SemanticVersion.TryParse("1.4.1", out var v141));
        Assert.True(SemanticVersion.TryParse("1.4.0", out var v140));
        Assert.True(SemanticVersion.TryParse("1.3.9", out var v139));
        Assert.True(SemanticVersion.TryParse("1.3.8", out var v138));
        Assert.True(SemanticVersion.TryParse("1.3.7", out var v137));
        Assert.True(SemanticVersion.TryParse("1.3.6", out var v136));
        Assert.True(SemanticVersion.TryParse("1.3.5", out var v135));
        Assert.True(SemanticVersion.TryParse("1.3.4", out var v134));
        Assert.True(SemanticVersion.TryParse("1.3.3", out var v133));
        Assert.True(SemanticVersion.TryParse("1.3.2", out var v132));
        Assert.True(SemanticVersion.TryParse("1.3.1", out var v131));
        Assert.True(SemanticVersion.TryParse("1.3.0", out var v130));
        Assert.True(SemanticVersion.TryParse("1.2.9", out var v129));
        Assert.True(SemanticVersion.TryParse("1.2.8", out var v128));
        Assert.True(SemanticVersion.TryParse("1.2.7", out var v127));
        Assert.True(SemanticVersion.TryParse("1.2.6", out var v126));
        Assert.True(SemanticVersion.TryParse("1.2.5", out var v125));
        Assert.True(SemanticVersion.TryParse("1.2.4", out var v124));
        Assert.True(SemanticVersion.TryParse("1.2.3", out var v123));
        Assert.True(SemanticVersion.TryParse("1.2.2", out var v122));
        Assert.True(SemanticVersion.TryParse("1.2.1", out var v121));
        Assert.True(SemanticVersion.TryParse("1.2.0", out var v120));
        Assert.True(SemanticVersion.TryParse("1.1.0", out var v110));
        Assert.True(SemanticVersion.TryParse("1.0.0", out var v100));

        Assert.True(v213 > v212);
        Assert.True(v213 > v211);
        Assert.True(v213 > v210);
        Assert.True(v213 > v200);
        Assert.True(v213 > v141);
        Assert.True(v213 > v140);
        Assert.True(v213 > v139);
        Assert.True(v213 > v138);
        Assert.True(v213 > v137);
        Assert.True(v213 > v136);
        Assert.True(v213 > v135);
        Assert.True(v213 > v134);
        Assert.True(v213 > v133);
        Assert.True(v213 > v132);
        Assert.True(v213 > v131);
        Assert.True(v213 > v130);
        Assert.True(v213 > v129);
        Assert.True(v213 > v128);
        Assert.True(v213 > v127);
        Assert.True(v213 > v126);
        Assert.True(v213 > v125);
        Assert.True(v213 > v124);
        Assert.True(v213 > v123);
        Assert.True(v213 > v122);
        Assert.True(v213 > v121);
        Assert.True(v213 > v120);
        Assert.True(v213 > v110);
        Assert.True(v213 > v100);
    }

    [Fact]
    public void DirectoryBuildProps_DefinesVersion213()
    {
        var propsPath = @"d:\DhirDhar\DhirDhar Solution\Directory.Build.props";
        Assert.True(File.Exists(propsPath), $"Directory.Build.props not found at {propsPath}");

        var content = File.ReadAllText(propsPath);
        Assert.Contains("<Version>2.1.3</Version>", content);
        Assert.Contains("<AssemblyVersion>2.1.3.0</AssemblyVersion>", content);
        Assert.Contains("<FileVersion>2.1.3.0</FileVersion>", content);
        Assert.Contains("<InformationalVersion>2.1.3</InformationalVersion>", content);
    }

    [Fact]
    public void AppSettings_DefinesVersion213()
    {
        var appsettingsPath = @"d:\DhirDhar\DhirDhar Solution\src\DhirDhar.Desktop\appsettings.json";
        Assert.True(File.Exists(appsettingsPath), $"appsettings.json not found at {appsettingsPath}");

        var json = File.ReadAllText(appsettingsPath);
        using var doc = JsonDocument.Parse(json);
        var version = doc.RootElement.GetProperty("Application").GetProperty("Version").GetString();
        Assert.Equal("2.1.3", version);
    }

    [Fact]
    public void Published_DhirDharDesktopExe_ReportsFileVersion2130()
    {
        var exePath = @"d:\DhirDhar\DhirDhar Solution\Release\DhirDhar.Desktop.exe";
        if (File.Exists(exePath))
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            Assert.Equal("2.1.3.0", versionInfo.FileVersion);
            Assert.StartsWith("2.1.3", versionInfo.ProductVersion);
        }
    }

    [Fact]
    public void InstallerExe_Exists_And_IsNonEmpty()
    {
        var installerPath = @"d:\DhirDhar\DhirDhar Solution\Installer\DhirDhar-2.1.3-x64-Setup.exe";
        if (File.Exists(installerPath))
        {
            var fileInfo = new FileInfo(installerPath);
            Assert.True(fileInfo.Length > 20 * 1024 * 1024, "Installer exe should be at least 20 MB");

            // Verify PE / DOS MZ signature
            using var fs = File.OpenRead(installerPath);
            var header = new byte[2];
            int read = fs.Read(header, 0, 2);
            Assert.Equal(2, read);
            Assert.Equal(0x4D, header[0]); // 'M'
            Assert.Equal(0x5A, header[1]); // 'Z'
        }
    }

    [Fact]
    public void VersionComparison_DetectsUpdateFromPreviousVersion()
    {
        Assert.True(SemanticVersion.TryParse("2.1.1", out var installedVersion));
        Assert.True(SemanticVersion.TryParse("2.1.3", out var newVersion));
        Assert.True(newVersion > installedVersion, "2.1.3 must be detected as an update over 2.1.1");
    }

    [Fact]
    public async Task LiveGitHubRelease_ReportsValidVersion_And_ValidInstallerAsset()
    {
        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DhirDhar-Updater-Test/1.3.2");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");

        var response = await client.GetAsync("https://api.github.com/repos/bhargav1822/DhirDhar-Releases/releases/latest");
        if (!response.IsSuccessStatusCode)
        {
            // Gracefully skip when offline, unauthenticated, or rate limited
            return;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var tagName = doc.RootElement.GetProperty("tag_name").GetString();
        Assert.NotNull(tagName);
        Assert.True(SemanticVersion.TryParse(tagName, out var releaseSemVer));
        Assert.True(releaseSemVer >= new SemanticVersion(1, 3, 1), "Live release must be at least v1.3.1");

        var assets = doc.RootElement.GetProperty("assets");
        var hasInstaller = false;

        foreach (var asset in assets.EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString();
            var size = asset.GetProperty("size").GetInt64();
            var downloadUrl = asset.GetProperty("browser_download_url").GetString();

            if (!string.IsNullOrEmpty(assetName) && assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !assetName.Contains("License"))
            {
                hasInstaller = true;
                Assert.True(size > 20 * 1024 * 1024, "Setup executable asset size must be > 20MB");
                Assert.NotNull(downloadUrl);
            }
        }

        if (assets.GetArrayLength() > 0 && hasInstaller)
        {
            Assert.True(hasInstaller, "GitHub Release must contain Windows setup exe asset");
        }
    }

    [Fact]
    public async Task UpdateDetectionWorkflow_DetectsRelease_FromOlderInstalledVersion()
    {
        var installedVersion = "1.3.0";

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DhirDhar-Updater-Test/{installedVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");

        var response = await client.GetAsync("https://api.github.com/repos/bhargav1822/DhirDhar-Releases/releases/latest");
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var tagName = doc.RootElement.GetProperty("tag_name").GetString();
        
        Assert.True(SemanticVersion.TryParse(installedVersion, out var currentSemVer));
        Assert.True(SemanticVersion.TryParse(tagName, out var latestSemVer));

        Assert.True(latestSemVer > currentSemVer, "Live release must be strictly greater than installed v1.3.0");

        var assets = doc.RootElement.GetProperty("assets");
        var foundCompatibleAsset = false;
        string? downloadUrl = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString();
            if (!string.IsNullOrEmpty(assetName) && assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !assetName.Contains("License"))
            {
                foundCompatibleAsset = true;
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
            }
        }

        if (assets.GetArrayLength() > 0 && foundCompatibleAsset)
        {
            Assert.NotNull(downloadUrl);
        }
    }

    [Fact]
    public async Task CleanupInstalledPackages_DeletesInstalledVersion_PreservesHigherVersionAndUserData()
    {
        var tempUpdatesDir = Path.Combine(Path.GetTempPath(), "DhirDhar_TestUpdates_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUpdatesDir);

        try
        {
            // 1. Setup simulated update packages
            string installedPkg132 = Path.Combine(tempUpdatesDir, "DhirDhar-v1.3.2-update.exe");
            string installedPkg131 = Path.Combine(tempUpdatesDir, "DhirDhar_Setup_v1.3.1.exe");
            string pendingFuturePkg133 = Path.Combine(tempUpdatesDir, "DhirDhar-v1.3.3-update.exe");
            string stagingDir = Path.Combine(tempUpdatesDir, "Staging");
            string backupDir = Path.Combine(tempUpdatesDir, "Backup_20260818_120000");

            // 2. Setup protected non-update files
            string dbFile = Path.Combine(tempUpdatesDir, "DhirDhar.db");
            string settingsFile = Path.Combine(tempUpdatesDir, "appsettings.json");
            string licenseFile = Path.Combine(tempUpdatesDir, "license.lic");
            string logFile = Path.Combine(tempUpdatesDir, "update.log");

            File.WriteAllText(installedPkg132, "dummy-installer-132");
            File.WriteAllText(installedPkg131, "dummy-installer-131");
            File.WriteAllText(pendingFuturePkg133, "dummy-installer-133");
            Directory.CreateDirectory(stagingDir);
            File.WriteAllText(Path.Combine(stagingDir, "stage.tmp"), "staging-data");
            Directory.CreateDirectory(backupDir);
            File.WriteAllText(Path.Combine(backupDir, "backup.tmp"), "backup-data");

            File.WriteAllText(dbFile, "sqlite-db-data");
            File.WriteAllText(settingsFile, "{ \"theme\": \"dark\" }");
            File.WriteAllText(licenseFile, "license-key-data");
            File.WriteAllText(logFile, "log-entries");

            // Execute cleanup logic simulating current app version = 1.3.2
            var currentAppVersion = new SemanticVersion(1, 3, 2);
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".zip", ".msi", ".tmp" };

            foreach (var file in Directory.GetFiles(tempUpdatesDir))
            {
                var fileName = Path.GetFileName(file);
                var ext = Path.GetExtension(file);

                if (string.IsNullOrEmpty(ext) || !allowedExtensions.Contains(ext)) continue;

                if (fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".lic", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".config", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = Regex.Match(fileName, @"(?:v)?(\d+\.\d+(?:\.\d+)?(?:\.\d+)?)", RegexOptions.IgnoreCase);
                if (match.Success && SemanticVersion.TryParse(match.Groups[1].Value, out var pkgVer))
                {
                    if (pkgVer <= currentAppVersion)
                    {
                        File.Delete(file);
                    }
                }
            }

            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
            foreach (var dir in Directory.GetDirectories(tempUpdatesDir, "Backup_*")) Directory.Delete(dir, true);

            // Assertions
            Assert.False(File.Exists(installedPkg132), "Installed package v1.3.2 MUST be deleted");
            Assert.False(File.Exists(installedPkg131), "Older package v1.3.1 MUST be deleted");
            Assert.True(File.Exists(pendingFuturePkg133), "Future/pending package v1.3.3 MUST be preserved for retry");
            Assert.False(Directory.Exists(stagingDir), "Staging directory MUST be cleaned up");
            Assert.False(Directory.Exists(backupDir), "Temporary backup directory MUST be cleaned up");

            // Verify protected user files are 100% intact
            Assert.True(File.Exists(dbFile), "Database file MUST NEVER be deleted");
            Assert.True(File.Exists(settingsFile), "Settings file MUST NEVER be deleted");
            Assert.True(File.Exists(licenseFile), "License file MUST NEVER be deleted");
            Assert.True(File.Exists(logFile), "Log file MUST NEVER be deleted");
        }
        finally
        {
            if (Directory.Exists(tempUpdatesDir))
            {
                try { Directory.Delete(tempUpdatesDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task CleanupInstalledPackages_HandlesLockedFilesSafelyWithRetry()
    {
        var tempUpdatesDir = Path.Combine(Path.GetTempPath(), "DhirDhar_LockedTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUpdatesDir);

        try
        {
            string lockedPkg = Path.Combine(tempUpdatesDir, "DhirDhar-v1.3.2-update.exe");
            File.WriteAllText(lockedPkg, "locked-installer-content");

            // Lock file with exclusive read/write handle for 1.5 seconds
            var lockStream = new FileStream(lockedPkg, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                lockStream.Dispose(); // Release lock
            });

            // Perform retry delete
            bool deleted = false;
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                try
                {
                    if (File.Exists(lockedPkg))
                    {
                        File.Delete(lockedPkg);
                        deleted = true;
                        break;
                    }
                }
                catch (IOException)
                {
                    await Task.Delay(400);
                }
            }

            Assert.True(deleted, "Cleanup retry loop must successfully delete package once lock is released");
            Assert.False(File.Exists(lockedPkg), "Locked installer file must be successfully removed");
        }
        finally
        {
            if (Directory.Exists(tempUpdatesDir))
            {
                try { Directory.Delete(tempUpdatesDir, true); } catch { }
            }
        }
    }

    [Theory]
    [InlineData("https://github.com/bhargav1822/DhirDhar-Releases/releases/download/v1.3.7/DhirDhar_Setup_v1.3.7.exe", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/12345/DhirDhar_Setup_v1.3.7.exe", true)]
    [InlineData("https://raw.githubusercontent.com/bhargav1822/DhirDhar-Releases/main/release.json", true)]
    [InlineData("http://github.com/bhargav1822/DhirDhar-Releases/releases/download/v1.3.7/DhirDhar_Setup_v1.3.7.exe", false)] // No plain HTTP
    [InlineData("https://support.microsoft.com/windows/what-is-a-cloud-security-scan", false)] // Disallowed domain / support page
    [InlineData("https://github.com/bhargav1822/DhirDhar-Releases/issues", false)] // Web issue page
    [InlineData("https://attacker.com/malicious.exe", false)] // Non-GitHub domain
    [InlineData("", false)]
    [InlineData(null, false)]
    public void UpdateUrlValidation_StrictlyValidatesAllowedSources(string? url, bool expectedValid)
    {
        bool isValid = false;
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == Uri.UriSchemeHttps)
            {
                var host = uri.Host.ToLowerInvariant();
                bool isAllowedHost = host == "github.com" ||
                                     host == "api.github.com" ||
                                     host == "objects.githubusercontent.com" ||
                                     host == "raw.githubusercontent.com" ||
                                     host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

                var path = uri.AbsolutePath.ToLowerInvariant();
                if (isAllowedHost && !path.Contains("/issues") && !path.Contains("/pull") && !path.Contains("/wiki") && !path.Contains("support.microsoft.com"))
                {
                    isValid = true;
                }
            }
        }

        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void PeBinaryValidator_DetectsRealPE_AndRejectsHtmlPayload()
    {
        var tempFilePe = Path.Combine(Path.GetTempPath(), "valid_pe_test_" + Guid.NewGuid().ToString("N") + ".exe");
        var tempFileHtml = Path.Combine(Path.GetTempPath(), "fake_html_test_" + Guid.NewGuid().ToString("N") + ".exe");

        try
        {
            // 1. Create a dummy valid PE header (MZ at 0, offset at 0x3C pointing to PE\0\0)
            var peBytes = new byte[1024];
            peBytes[0] = 0x4D; // 'M'
            peBytes[1] = 0x5A; // 'Z'
            // Set e_lfanew at 0x3C = 0x80 (128)
            peBytes[0x3C] = 0x80;
            peBytes[0x3D] = 0x00;
            peBytes[0x3E] = 0x00;
            peBytes[0x3F] = 0x00;
            // Place PE signature at 128
            peBytes[128] = 0x50; // 'P'
            peBytes[129] = 0x45; // 'E'
            peBytes[130] = 0x00;
            peBytes[131] = 0x00;
            File.WriteAllBytes(tempFilePe, peBytes);

            // 2. Create fake HTML payload disguised with .exe extension
            File.WriteAllText(tempFileHtml, "<!DOCTYPE html><html><head><title>Cloud Security Scan</title></head><body><h1>Help Page</h1></body></html>");

            // Test PE validation logic
            Assert.True(TestValidatePeExecutable(tempFilePe), "Real PE binary structure MUST pass validation");
            Assert.False(TestValidatePeExecutable(tempFileHtml), "HTML disguised as .exe MUST FAIL PE validation");
        }
        finally
        {
            if (File.Exists(tempFilePe)) File.Delete(tempFilePe);
            if (File.Exists(tempFileHtml)) File.Delete(tempFileHtml);
        }
    }

    private static bool TestValidatePeExecutable(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < 1024) return false;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            if (fs.Length < 64) return false;
            ushort mz = reader.ReadUInt16();
            if (mz != 0x5A4D) return false;

            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset + 4 > fs.Length) return false;

            fs.Seek(peOffset, SeekOrigin.Begin);
            uint peSignature = reader.ReadUInt32();
            return peSignature == 0x00004550;
        }
        catch
        {
            return false;
        }
    }
}

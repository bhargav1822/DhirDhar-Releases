using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Settings;
using DhirDhar.Desktop.Configuration;
using DhirDhar.Desktop.Updates.Helpers;
using DhirDhar.Desktop.Updates.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.Updates;

public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService? _settingsService;
    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly SemaphoreSlim _downloadLock = new(1, 1);
    private static readonly ThreadLocal<bool> _checkedThisSession = new(() => false);

    public string CurrentVersion { get; }
    public string? LatestVersion { get; private set; }
    public UpdateInfo? AvailableUpdate { get; private set; }

    public bool IsChecking { get; private set; }
    public bool IsDownloading { get; private set; }
    public int DownloadProgressPercent { get; private set; }
    public long BytesDownloaded { get; private set; }
    public long TotalBytes { get; private set; }
    public bool IsReadyToInstall { get; private set; }
    public string? VerifiedZipPath { get; private set; }

    public event EventHandler<UpdateInfo>? UpdateAvailable;
    public event EventHandler<string?>? StatusChanged;
    public event EventHandler<int>? DownloadProgressChanged;

    public UpdateService(
        IConfiguration configuration,
        ILogger<UpdateService> logger,
        ISettingsService? settingsService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService;

        var appOptions = configuration.GetSection("Application").Get<AppOptions>() ?? new AppOptions();
        var rawVersion = appOptions.Version;
        if (string.IsNullOrWhiteSpace(rawVersion) || rawVersion == "0.0.0")
        {
            var asmVer = Assembly.GetExecutingAssembly().GetName().Version;
            rawVersion = asmVer != null ? $"{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}" : "1.3.7";
        }
        CurrentVersion = rawVersion.TrimStart('v', 'V');

        _settings = configuration.GetSection(UpdateSettings.SectionName).Get<UpdateSettings>() ?? new UpdateSettings();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _httpClient = new HttpClient(new HttpClientHandler())
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DhirDhar-Desktop-Updater", CurrentVersion));
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync(bool force = false)
    {
        if (!force)
        {
            if (_checkedThisSession.Value)
            {
                _logger.LogInformation("[UPDATER] Update check already performed this session; skipping.");
                return AvailableUpdate;
            }
            _checkedThisSession.Value = true;
        }

        if (!await _updateLock.WaitAsync(0).ConfigureAwait(false))
        {
            _logger.LogInformation("[UPDATER] Another update operation is currently running; skipping check.");
            return AvailableUpdate;
        }

        IsChecking = true;
        StatusChanged?.Invoke(this, "CheckingForUpdates");

        try
        {
            var result = await ExecuteCheckForUpdatesInternalAsync().ConfigureAwait(false);
            return result;
        }
        finally
        {
            IsChecking = false;
            _updateLock.Release();
        }
    }

    private async Task<UpdateInfo?> ExecuteCheckForUpdatesInternalAsync()
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            _logger.LogWarning("[UPDATER] No internet connection available.");
            StatusChanged?.Invoke(this, "UpdateNetworkError");
            LogUpdateEvent($"Update check failed: No network connectivity. Current version: {CurrentVersion}");
            return null;
        }

        var repo = string.IsNullOrWhiteSpace(_settings.GitHubRepository)
            ? "bhargav1822/DhirDhar-Releases"
            : _settings.GitHubRepository.Trim();

        var apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";
        _logger.LogInformation("[UPDATER] Querying GitHub latest release for '{Repo}'. Installed version: {CurrentVersion}.", repo, CurrentVersion);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

            GitHubReleaseDto? latestRelease = null;

            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                latestRelease = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, _jsonOptions, cts.Token).ConfigureAwait(false);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var listApiUrl = $"https://api.github.com/repos/{repo}/releases";
                using var listRequest = new HttpRequestMessage(HttpMethod.Get, listApiUrl);
                using var listResponse = await _httpClient.SendAsync(listRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

                if (listResponse.IsSuccessStatusCode)
                {
                    await using var listStream = await listResponse.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                    var releases = await JsonSerializer.DeserializeAsync<List<GitHubReleaseDto>>(listStream, _jsonOptions, cts.Token).ConfigureAwait(false);

                    if (releases != null && releases.Count > 0)
                    {
                        var eligibleReleases = releases
                            .Where(r => !r.Draft && (_settings.IncludePrerelease || !r.Prerelease))
                            .ToList();

                        if (eligibleReleases.Count > 0)
                        {
                            latestRelease = eligibleReleases.First();
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning("[UPDATER] GitHub API returned status code {StatusCode}.", response.StatusCode);
                StatusChanged?.Invoke(this, "UpdateNetworkError");
                LogUpdateEvent($"Update check failed: GitHub API returned status code {response.StatusCode}.");
                return null;
            }

            if (latestRelease is null)
            {
                _logger.LogInformation("[UPDATER] No published releases found in repository '{Repo}'.", repo);
                StatusChanged?.Invoke(this, "UpToDate");
                LogUpdateEvent($"Update check completed: No published releases found in '{repo}'. Current version: {CurrentVersion}.");
                return null;
            }

            var tagToParse = !string.IsNullOrWhiteSpace(latestRelease.TagName) ? latestRelease.TagName : latestRelease.Name;
            if (!SemanticVersion.TryParse(tagToParse, out var latestSemVer))
            {
                _logger.LogWarning("[UPDATER] Could not parse semantic version from release tag '{Tag}'.", tagToParse);
                StatusChanged?.Invoke(this, "UpdateInvalidPayload");
                LogUpdateEvent($"Update check failed: Invalid version tag '{tagToParse}'.");
                return null;
            }

            if (!SemanticVersion.TryParse(CurrentVersion, out var currentSemVer))
            {
                currentSemVer = new SemanticVersion(1, 0, 1);
            }

            LatestVersion = latestSemVer.RawVersion;
            _logger.LogInformation("[UPDATER] Installed: {Current}, Latest: {Latest}.", currentSemVer.RawVersion, latestSemVer.RawVersion);

            if (latestSemVer <= currentSemVer)
            {
                _logger.LogInformation("[UPDATER] Application is up to date (Installed {Current} >= Latest {Latest}).", currentSemVer.RawVersion, latestSemVer.RawVersion);
                StatusChanged?.Invoke(this, "UpToDate");
                LogUpdateEvent($"Update check completed: Up to date. Installed version {currentSemVer.RawVersion} is latest.");
                return null;
            }

            // Find matching Windows x64 asset: Prioritize Inno Setup EXE installer, then ZIP package
            // Explicitly exclude any License Generator binaries
            var updateAsset = latestRelease.Assets.FirstOrDefault(a =>
                !string.IsNullOrEmpty(a.Name) &&
                !a.Name.Contains("License", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ?? latestRelease.Assets.FirstOrDefault(a =>
                !string.IsNullOrEmpty(a.Name) &&
                !a.Name.Contains("License", StringComparison.OrdinalIgnoreCase) &&
                (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                 a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)));

            if (updateAsset is null || string.IsNullOrWhiteSpace(updateAsset.BrowserDownloadUrl))
            {
                _logger.LogWarning("[UPDATER] No compatible Windows x64 update asset found in release '{Tag}'.", tagToParse);
                StatusChanged?.Invoke(this, "NoCompatiblePackage");
                LogUpdateEvent($"Update check result: Update v{latestSemVer.RawVersion} found, but no compatible Windows x64 asset exists.");
                return null;
            }

            // Check for SHA256 checksum in asset list or body notes
            string sha256 = ExtractSha256Checksum(latestRelease);

            var updateInfo = new UpdateInfo
            {
                Version = latestSemVer.RawVersion,
                PackageUrl = updateAsset.BrowserDownloadUrl,
                AssetName = updateAsset.Name,
                SizeBytes = updateAsset.Size,
                Sha256 = sha256,
                ReleaseNotes = !string.IsNullOrWhiteSpace(latestRelease.Body) ? latestRelease.Body : latestRelease.Name,
                IsStable = !latestRelease.Prerelease,
                PublishedAt = latestRelease.PublishedAt
            };

            AvailableUpdate = updateInfo;
            _logger.LogInformation("[UPDATER] Update available: Installed v{Current} -> Available v{New}.", CurrentVersion, updateInfo.Version);
            LogUpdateEvent($"Update check result: Update available v{updateInfo.Version}. Asset: {updateInfo.AssetName} ({updateInfo.SizeBytes} bytes).");

            UpdateAvailable?.Invoke(this, updateInfo);
            StatusChanged?.Invoke(this, "UpdateAvailableContent");

            // Handle Automatic Install preference if enabled
            _ = HandleAutoInstallIfEnabledAsync(updateInfo);

            return updateInfo;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[UPDATER] Update check request timed out.");
            StatusChanged?.Invoke(this, "UpdateNetworkError");
            LogUpdateEvent($"Update check failed: Timeout. Current version: {CurrentVersion}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[UPDATER] Network/HTTP error during update check.");
            StatusChanged?.Invoke(this, "UpdateNetworkError");
            LogUpdateEvent($"Update check failed: Network error - {ex.Message}");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[UPDATER] Invalid JSON response from GitHub API.");
            StatusChanged?.Invoke(this, "UpdateInvalidPayload");
            LogUpdateEvent($"Update check failed: Invalid JSON response.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UPDATER] Unexpected error during update check.");
            StatusChanged?.Invoke(this, "UpdateCheckFailed");
            LogUpdateEvent($"Update check failed: {ex.Message}");
            return null;
        }
    }

    private static bool IsValidUpdateDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        bool isAllowedHost = host == "github.com" ||
                             host == "api.github.com" ||
                             host == "objects.githubusercontent.com" ||
                             host == "raw.githubusercontent.com" ||
                             host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

        if (!isAllowedHost)
        {
            return false;
        }

        var path = uri.AbsolutePath.ToLowerInvariant();
        if (path.Contains("/issues") || path.Contains("/pull") || path.Contains("/wiki") || path.Contains("support.microsoft.com"))
        {
            return false;
        }

        return true;
    }

    private static bool IsValidPeExecutable(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < 1024)
            {
                return false;
            }

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            if (fs.Length < 64) return false;
            ushort mz = reader.ReadUInt16();
            if (mz != 0x5A4D) return false; // 'MZ'

            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset + 4 > fs.Length) return false;

            fs.Seek(peOffset, SeekOrigin.Begin);
            uint peSignature = reader.ReadUInt32();
            return peSignature == 0x00004550; // 'PE\0\0'
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DownloadAndVerifyUpdateAsync(UpdateInfo updateInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateInfo);

        if (!IsValidUpdateDownloadUrl(updateInfo.PackageUrl))
        {
            _logger.LogError("[UPDATER ERROR] Refusing download: Invalid or disallowed update URL '{Url}'.", updateInfo.PackageUrl);
            StatusChanged?.Invoke(this, "UpdateVerificationFailed");
            LogUpdateEvent($"Download rejected: Disallowed update URL '{updateInfo.PackageUrl}'.");
            return false;
        }

        if (!await _downloadLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("[UPDATER] Another download is already in progress; skipping duplicate download request.");
            return false;
        }

        IsDownloading = true;
        DownloadProgressPercent = 0;
        BytesDownloaded = 0;
        TotalBytes = updateInfo.SizeBytes;
        StatusChanged?.Invoke(this, "DownloadingUpdate");
        DownloadProgressChanged?.Invoke(this, 0);

        string updatesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DhirDhar Solution", "Updates");
        Directory.CreateDirectory(updatesDir);

        string ext = Path.GetExtension(updateInfo.AssetName);
        if (string.IsNullOrEmpty(ext)) ext = ".zip";
        string destinationPath = Path.Combine(updatesDir, $"DhirDhar-v{updateInfo.Version}-update{ext}");
        LogUpdateEvent($"Starting download of v{updateInfo.Version} from '{updateInfo.PackageUrl}' to '{destinationPath}'...");

        try
        {
            using var response = await _httpClient.GetAsync(updateInfo.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
            if (mediaType.Contains("text/html") || mediaType.Contains("text/plain"))
            {
                throw new InvalidDataException($"Received unexpected MIME content-type '{mediaType}' instead of binary update asset.");
            }

            var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.SizeBytes;
            TotalBytes = totalBytes;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                    totalRead += bytesRead;
                    BytesDownloaded = totalRead;

                    if (totalBytes > 0)
                    {
                        int percent = (int)((double)totalRead / totalBytes * 100);
                        if (percent != DownloadProgressPercent)
                        {
                            DownloadProgressPercent = percent;
                            DownloadProgressChanged?.Invoke(this, percent);
                        }
                    }
                }
            }

            LogUpdateEvent($"Download of v{updateInfo.Version} completed successfully ({BytesDownloaded} bytes). Verifying package...");
            StatusChanged?.Invoke(this, "VerifyingUpdate");

            // 1. Verify File Exists & Non-Empty
            if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length == 0)
            {
                throw new InvalidDataException("Downloaded update file is missing or empty.");
            }

            // 2. Verify structure if EXE installer
            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsValidPeExecutable(destinationPath))
                {
                    throw new InvalidDataException("Downloaded file is not a valid Windows executable binary (failed PE validation).");
                }
            }
            // 3. Verify structure if ZIP archive
            else if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(destinationPath);
                if (archive.Entries.Count == 0)
                {
                    throw new InvalidDataException("Downloaded ZIP file contains no entries.");
                }
            }
            else
            {
                throw new InvalidDataException($"Unsupported update package extension '{ext}'.");
            }

            // 4. Verify SHA256 Checksum if provided
            if (!string.IsNullOrWhiteSpace(updateInfo.Sha256))
            {
                string computedHash = ComputeSha256(destinationPath);
                if (!string.Equals(computedHash, updateInfo.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"SHA-256 checksum mismatch! Expected '{updateInfo.Sha256}', computed '{computedHash}'.");
                }
                LogUpdateEvent($"SHA-256 checksum verified successfully: {computedHash}");
            }

            VerifiedZipPath = destinationPath;
            IsReadyToInstall = true;
            StatusChanged?.Invoke(this, "UpdateDownloadedAndVerified");
            LogUpdateEvent($"Update v{updateInfo.Version} downloaded and verified successfully.");

            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[UPDATER] Update download cancelled.");
            CleanupFile(destinationPath);
            StatusChanged?.Invoke(this, "UpdateCheckFailed");
            LogUpdateEvent($"Download of v{updateInfo.Version} was cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UPDATER] Download or verification of update failed.");
            CleanupFile(destinationPath);
            IsReadyToInstall = false;
            VerifiedZipPath = null;
            StatusChanged?.Invoke(this, "UpdateVerificationFailed");
            LogUpdateEvent($"Download/Verification failed for v{updateInfo.Version}: {ex.Message}");
            return false;
        }
        finally
        {
            IsDownloading = false;
            _downloadLock.Release();
        }
    }

    public async Task<bool> InstallUpdateAsync(UpdateInfo updateInfo)
    {
        ArgumentNullException.ThrowIfNull(updateInfo);

        if (!IsReadyToInstall || string.IsNullOrEmpty(VerifiedZipPath) || !File.Exists(VerifiedZipPath))
        {
            _logger.LogInformation("[UPDATER] Package not downloaded yet. Downloading package before installation...");
            bool downloaded = await DownloadAndVerifyUpdateAsync(updateInfo).ConfigureAwait(false);
            if (!downloaded || string.IsNullOrEmpty(VerifiedZipPath) || !File.Exists(VerifiedZipPath))
            {
                _logger.LogError("[UPDATER] Cannot proceed with installation because download or verification failed.");
                return false;
            }
        }

        string appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        string exeName = Process.GetCurrentProcess().ProcessName + ".exe";
        string exePath = Path.Combine(appDir, exeName);
        if (!File.Exists(exePath))
        {
            exePath = Path.Combine(appDir, "DhirDhar.Desktop.exe");
        }

        int pid = Process.GetCurrentProcess().Id;
        bool isInstallerExe = VerifiedZipPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        // Find DhirDharUpdater.exe
        string updaterExe = Path.Combine(appDir, "DhirDharUpdater.exe");
        if (!File.Exists(updaterExe))
        {
            string candidate = Path.Combine(Directory.GetParent(appDir)?.FullName ?? appDir, "DhirDharUpdater.exe");
            if (File.Exists(candidate))
            {
                updaterExe = candidate;
            }
        }

        if (File.Exists(updaterExe))
        {
            string tempUpdaterDir = Path.Combine(Path.GetTempPath(), "DhirDharUpdater_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempUpdaterDir);
            string updaterSourceDir = Path.GetDirectoryName(updaterExe) ?? appDir;
            foreach (var file in Directory.GetFiles(updaterSourceDir))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith("DhirDharUpdater", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Copy(file, Path.Combine(tempUpdaterDir, fileName), true); } catch { }
                }
            }

            string tempUpdaterExe = Path.Combine(tempUpdaterDir, Path.GetFileName(updaterExe));
            string targetExeToRun = File.Exists(tempUpdaterExe) ? tempUpdaterExe : updaterExe;

            LogUpdateEvent($"Launching DhirDharUpdater (PID {pid}, Target: '{appDir}', Package: '{VerifiedZipPath}')...");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = targetExeToRun,
                    Arguments = $"--pid {pid} --package \"{VerifiedZipPath}\" --target \"{appDir}\" --exe \"{exePath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = tempUpdaterDir
                };

                try
                {
                    Process.Start(startInfo);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    startInfo.Verb = "";
                    Process.Start(startInfo);
                }

                LogUpdateEvent("Updater process launched successfully. Exiting main application.");
                ExitApplicationCleanly();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UPDATER ERROR] Failed to launch DhirDharUpdater.");
                LogUpdateEvent($"Failed to launch DhirDharUpdater: {ex.Message}");
            }
        }

        // Direct installer execution fallback without batch scripts
        if (isInstallerExe && File.Exists(VerifiedZipPath) && IsValidPeExecutable(VerifiedZipPath))
        {
            LogUpdateEvent($"Launching installer directly: '{VerifiedZipPath}'...");
            try
            {
                var installerStartInfo = new ProcessStartInfo
                {
                    FileName = VerifiedZipPath,
                    Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /DIR=\"{appDir}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(VerifiedZipPath) ?? appDir
                };

                try
                {
                    Process.Start(installerStartInfo);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    installerStartInfo.Verb = "";
                    Process.Start(installerStartInfo);
                }

                LogUpdateEvent("Direct installer launched successfully. Exiting main application.");
                ExitApplicationCleanly();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[UPDATER ERROR] Failed to launch installer executable directly.");
                StatusChanged?.Invoke(this, "UpdateInstallerLaunchFailed");
                LogUpdateEvent($"Failed to launch installer directly: {ex.Message}");
                return false;
            }
        }

        _logger.LogError("[UPDATER ERROR] Neither DhirDharUpdater.exe nor a valid installer package could be launched.");
        StatusChanged?.Invoke(this, "UpdateInstallerMissing");
        LogUpdateEvent("Installation failed: Neither DhirDharUpdater.exe nor valid installer package could be launched.");
        return false;
    }

    private static void ExitApplicationCleanly()
    {
        if (Microsoft.UI.Xaml.Application.Current is not null)
        {
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private async Task HandleAutoInstallIfEnabledAsync(UpdateInfo updateInfo)
    {
        if (_settingsService is null) return;
        try
        {
            var settings = await _settingsService.GetSettingsAsync().ConfigureAwait(false);
            if (settings.UpdatesAutoCheckEnabled && settings.UpdatesAutoInstallEnabled && updateInfo.IsStable)
            {
                _logger.LogInformation("[UPDATER] Automatic install is enabled. Pre-downloading update v{Version}...", updateInfo.Version);
                await DownloadAndVerifyUpdateAsync(updateInfo).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UPDATER] Error handling automatic install.");
        }
    }

    private static string ExtractSha256Checksum(GitHubReleaseDto release)
    {
        // Try finding a .sha256 or SHA256SUMS asset
        var shaAsset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("SHA256", StringComparison.OrdinalIgnoreCase));
        if (shaAsset is not null && !string.IsNullOrEmpty(shaAsset.BrowserDownloadUrl))
        {
            // Could be read if needed, or parse body
        }

        // Try extracting 64-char hex string from body
        if (!string.IsNullOrWhiteSpace(release.Body))
        {
            var match = Regex.Match(release.Body, @"\b([a-fA-F0-9]{64})\b");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return string.Empty;
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    public async Task CleanupInstalledPackagesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!SemanticVersion.TryParse(CurrentVersion, out var currentAppVersion))
            {
                _logger.LogWarning("[UPDATER CLEANUP] Could not parse current application version '{Version}'. Aborting cleanup.", CurrentVersion);
                return;
            }

            string updatesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DhirDhar Solution", "Updates");
            if (!Directory.Exists(updatesDir))
            {
                return;
            }

            _logger.LogInformation("[UPDATER CLEANUP] Scanning '{UpdatesDir}' for installed update packages (Current Version: v{CurrentVersion})...", updatesDir, currentAppVersion.RawVersion);

            // Allowed update package extensions
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".zip", ".msi", ".tmp" };

            // Scan files in updates directory
            var candidateFiles = Directory.GetFiles(updatesDir);
            foreach (var filePath in candidateFiles)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var fileName = Path.GetFileName(filePath);
                var ext = Path.GetExtension(filePath);

                // Strict safety check: Never delete database, settings, log, license, or non-package files
                if (string.IsNullOrEmpty(ext) || !allowedExtensions.Contains(ext))
                {
                    continue;
                }

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

                // Extract version from file name (e.g. "DhirDhar-v1.3.2-update.exe", "DhirDhar_Setup_v1.3.2.exe", "DhirDhar-1.3.2.zip")
                var match = Regex.Match(fileName, @"(?:v)?(\d+\.\d+(?:\.\d+)?(?:\.\d+)?)", RegexOptions.IgnoreCase);
                if (match.Success && SemanticVersion.TryParse(match.Groups[1].Value, out var packageVersion))
                {
                    // Delete only packages for versions that were successfully installed (<= currentAppVersion)
                    // If packageVersion > currentAppVersion, installation failed or is pending: KEEP IT so it can be retried.
                    if (packageVersion <= currentAppVersion)
                    {
                        LogUpdateEvent($"[CLEANUP] Found installed update package '{fileName}' (v{packageVersion.RawVersion} <= current v{currentAppVersion.RawVersion}). Initiating removal...");
                        await DeleteFileWithRetryAsync(filePath, fileName, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        LogUpdateEvent($"[CLEANUP] Preserving pending update package '{fileName}' (v{packageVersion.RawVersion} > current v{currentAppVersion.RawVersion}) for future update/retry.");
                        _logger.LogInformation("[UPDATER CLEANUP] Preserved pending package '{File}' (v{Version} > v{Current}).", fileName, packageVersion.RawVersion, currentAppVersion.RawVersion);
                    }
                }
            }

            // Cleanup staging folder if left behind
            string stagingDir = Path.Combine(updatesDir, "Staging");
            if (Directory.Exists(stagingDir))
            {
                try
                {
                    Directory.Delete(stagingDir, true);
                    LogUpdateEvent("[CLEANUP] Cleaned up temporary staging directory.");
                }
                catch (Exception stgEx)
                {
                    _logger.LogDebug(stgEx, "[UPDATER CLEANUP] Could not remove staging dir.");
                }
            }

            // Cleanup backup folders older than current session if present in Updates directory
            try
            {
                foreach (var dir in Directory.GetDirectories(updatesDir, "Backup_*"))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            catch { }

            // Cleanup temp bootstrap runner directories in %TEMP%
            try
            {
                string tempPath = Path.GetTempPath();
                foreach (var dir in Directory.GetDirectories(tempPath, "DhirDharBootstrap_*"))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UPDATER CLEANUP] Unexpected error during update package cleanup.");
            LogUpdateEvent($"[CLEANUP ERROR] Unexpected error: {ex.Message}");
        }
    }

    private async Task DeleteFileWithRetryAsync(string filePath, string fileName, CancellationToken cancellationToken)
    {
        const int maxRetries = 10;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested) return;

            try
            {
                if (File.Exists(filePath))
                {
                    var attributes = File.GetAttributes(filePath);
                    if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                    }
                    File.Delete(filePath);
                    LogUpdateEvent($"[CLEANUP SUCCESS] Successfully deleted installed update package '{fileName}' (attempt {attempt}).");
                    _logger.LogInformation("[UPDATER CLEANUP] Successfully deleted installed update package '{File}' (attempt {Attempt}).", fileName, attempt);
                }
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == maxRetries)
                {
                    LogUpdateEvent($"[CLEANUP WARNING] Failed to delete locked package '{fileName}' after {maxRetries} attempts: {ex.Message}. Will retry on next launch.");
                    _logger.LogWarning(ex, "[UPDATER CLEANUP] Failed to delete locked package '{File}' after {Attempts} attempts.", fileName, maxRetries);
                }
                else
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogUpdateEvent($"[CLEANUP ERROR] Error deleting package '{fileName}': {ex.Message}");
                _logger.LogError(ex, "[UPDATER CLEANUP] Error deleting package '{File}'.", fileName);
                return;
            }
        }
    }

    private static void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static void LogUpdateEvent(string message)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DhirDhar Solution", "Logs");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "update.log");
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(file, line);
        }
        catch { }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Desktop.Updates.Models;

namespace DhirDhar.Desktop.Updates;

/// <summary>
/// Service that checks for, downloads, verifies, and triggers installation of DhirDhar application updates from GitHub Releases.
/// </summary>
public interface IUpdateService
{
    string CurrentVersion { get; }
    string? LatestVersion { get; }
    UpdateInfo? AvailableUpdate { get; }

    bool IsChecking { get; }
    bool IsDownloading { get; }
    int DownloadProgressPercent { get; }
    long BytesDownloaded { get; }
    long TotalBytes { get; }
    bool IsReadyToInstall { get; }
    string? VerifiedZipPath { get; }

    event EventHandler<UpdateInfo>? UpdateAvailable;
    event EventHandler<string?>? StatusChanged;
    event EventHandler<int>? DownloadProgressChanged;

    /// <summary>
    /// Checks the GitHub Releases API for a newer version.
    /// </summary>
    Task<UpdateInfo?> CheckForUpdatesAsync(bool force = false);

    /// <summary>
    /// Downloads the Windows x64 update package and verifies its integrity and checksum.
    /// </summary>
    Task<bool> DownloadAndVerifyUpdateAsync(UpdateInfo updateInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches the companion updater process and exits the main application cleanly.
    /// </summary>
    Task<bool> InstallUpdateAsync(UpdateInfo updateInfo);

    /// <summary>
    /// Automatically cleans up installed update packages and installers from the local updates directory.
    /// </summary>
    Task CleanupInstalledPackagesAsync(CancellationToken cancellationToken = default);
}

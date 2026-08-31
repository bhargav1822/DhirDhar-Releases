using System;

namespace DhirDhar.Desktop.Updates.Models;

/// <summary>
/// Immutable description of an available application update published via GitHub Releases.
/// </summary>
public sealed class UpdateInfo
{
    /// <summary>
    /// Semantic version of the update release, e.g. "1.0.1" or "v1.0.1".
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// HTTPS URL to the x64 ZIP package release asset. Must be HTTPS.
    /// </summary>
    public string PackageUrl { get; init; } = string.Empty;

    /// <summary>
    /// Name of the release asset file, e.g. "DhirDhar-v1.0.1-win-x64.zip".
    /// </summary>
    public string AssetName { get; init; } = string.Empty;

    /// <summary>
    /// Size of the release asset in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// SHA-256 hash of the package bytes, hex-encoded (optional).
    /// </summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>
    /// HTML or plain-text release notes shown to the user.
    /// </summary>
    public string ReleaseNotes { get; init; } = string.Empty;

    /// <summary>
    /// Optional minimum installed version required to apply this update.
    /// </summary>
    public string? MinimumSupportedVersion { get; init; }

    /// <summary>
    /// Whether this version should be treated as a stable release (vs. a pre-release).
    /// </summary>
    public bool IsStable { get; init; } = true;

    /// <summary>
    /// Release publication timestamp.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; init; }
}

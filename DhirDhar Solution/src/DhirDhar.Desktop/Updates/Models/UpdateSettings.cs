namespace DhirDhar.Desktop.Updates.Models;

/// <summary>
/// Update configuration bound from appsettings.json ("Update" section).
/// </summary>
public sealed class UpdateSettings
{
    public const string SectionName = "Update";

    /// <summary>
    /// The public GitHub repository path in "owner/repo" format (e.g. "bhargav1822/DhirDhar-Releases").
    /// </summary>
    public string GitHubRepository { get; init; } = "bhargav1822/DhirDhar-Releases";

    /// <summary>
    /// Whether to consider pre-release updates. Defaults to false (stable releases only).
    /// </summary>
    public bool IncludePrerelease { get; init; } = false;

    /// <summary>
    /// Whether to check for updates automatically at startup.
    /// </summary>
    public bool AutoCheckEnabled { get; init; } = true;

    /// <summary>
    /// Whether to download and install eligible updates automatically.
    /// </summary>
    public bool AutoInstallEnabled { get; init; } = true;
}

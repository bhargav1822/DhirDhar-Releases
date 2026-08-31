namespace DhirDhar.Application.Localization;

/// <summary>
/// Authoritative model representing the application language configuration state.
/// </summary>
public sealed class ApplicationLanguageSettings
{
    /// <summary>
    /// The single authoritative runtime application language (e.g., "gu-IN", "hi-IN", "en-US").
    /// </summary>
    public string CurrentLanguage { get; set; } = "gu-IN";

    /// <summary>
    /// The initial language selected during installation (from language.json), if present.
    /// </summary>
    public string? InstallerLanguage { get; set; }

    /// <summary>
    /// The user language explicitly saved in DhirDhar database settings, if present.
    /// </summary>
    public string? SavedApplicationLanguage { get; set; }

    /// <summary>
    /// Indicates whether localization and settings have been initialized.
    /// </summary>
    public bool IsLanguageInitialized { get; set; }
}

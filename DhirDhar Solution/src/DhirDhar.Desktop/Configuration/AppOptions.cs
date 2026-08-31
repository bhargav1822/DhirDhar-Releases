namespace DhirDhar.Desktop.Configuration;

/// <summary>
/// Centralized application metadata. The version is defined in a single place here and
/// referenced by the UI, so updates do not require searching through the project.
/// </summary>
public sealed class AppOptions
{
    public const string SectionName = "Application";

    public string Name { get; set; } = "DhirDhar Solution";

    public string Version { get; set; } = "1.4.0";

    public string Environment { get; set; } = "Development";
}

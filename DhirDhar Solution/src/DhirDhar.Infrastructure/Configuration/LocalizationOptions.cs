namespace DhirDhar.Infrastructure.Configuration;

/// <summary>
/// Configuration options for localization.
/// </summary>
public sealed class LocalizationOptions
{
    public const string SectionName = "Localization";

    public string DefaultCulture { get; set; } = "en-US";
}

namespace DhirDhar.Infrastructure.Configuration;

/// <summary>
/// Configuration options for application security.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public bool DataProtectionEnabled { get; set; }
}

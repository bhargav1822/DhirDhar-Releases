namespace DhirDhar.Infrastructure.Configuration;

/// <summary>
/// Configuration options for application logging.
/// </summary>
public sealed class LoggingOptions
{
    public const string SectionName = "Logging";

    public string MinimumLevel { get; set; } = "Information";
}

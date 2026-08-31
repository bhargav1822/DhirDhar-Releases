namespace DhirDhar.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the persistence (database) layer. Defaults are safe for
/// production: sensitive data logging is disabled and a sensible command timeout is applied.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = "Sqlite";

    public string DatabasePath { get; set; } = "DhirDhar.db";

    public int? CommandTimeout { get; set; } = 30;

    public bool EnableSensitiveDataLogging { get; set; }
}

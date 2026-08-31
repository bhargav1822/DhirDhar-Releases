namespace DhirDhar.Infrastructure.Configuration;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public bool Enabled { get; set; }

    public string Provider { get; set; } = "Local";

    public string Directory { get; set; } = string.Empty;

    public bool AutomaticBackupEnabled { get; set; } = true;

    public string BackupFrequency { get; set; } = "Daily";

    public int RetentionCount { get; set; } = 7;

    public bool GoogleDriveEnabled { get; set; }

    public string GoogleDriveFolder { get; set; } = "DhirDhar/Backups";

    public string GoogleDriveClientId { get; set; } = string.Empty;

    public string GoogleDriveClientSecret { get; set; } = string.Empty;

    public bool EncryptBackups { get; set; } = true;
}

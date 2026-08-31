namespace DhirDhar.Application.Backup.Models;

public sealed record BackupMetadata(
    string BackupId,
    string BackupFormatVersion,
    string ApplicationVersion,
    string SchemaVersion,
    DateTime CreatedAt,
    string BackupType,
    string Location,
    long FileSize,
    string IntegrityHash,
    string Status,
    string? VerificationStatus);

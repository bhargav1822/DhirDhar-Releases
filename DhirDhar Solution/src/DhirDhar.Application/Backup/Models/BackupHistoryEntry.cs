namespace DhirDhar.Application.Backup.Models;

public sealed record BackupHistoryEntry(
    string BackupId,
    DateTime BackupDate,
    string Type,
    string Location,
    long Size,
    string Status,
    string VerificationStatus);

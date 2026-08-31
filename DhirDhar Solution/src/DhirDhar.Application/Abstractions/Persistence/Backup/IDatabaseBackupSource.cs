namespace DhirDhar.Application.Abstractions.Persistence.Backup;

/// <summary>
/// Architectural preparation for the backup subsystem (planned for a later phase).
/// A future backup service uses this abstraction to obtain a consistent snapshot of the
/// local database. Cloud integration (Google Drive, OAuth, upload/download and
/// synchronization) is intentionally not implemented in this phase.
/// </summary>
public interface IDatabaseBackupSource
{
    Task<IDatabaseBackupSnapshot> AcquireSnapshotAsync(CancellationToken cancellationToken = default);
}

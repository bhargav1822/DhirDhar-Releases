namespace DhirDhar.Application.Abstractions.Persistence.Backup;

/// <summary>
/// A consistent snapshot of the local database obtained from an
/// <see cref="IDatabaseBackupSource"/>. Disposal must release any resources held for
/// consistency (for example an open database connection).
/// </summary>
public interface IDatabaseBackupSnapshot : IAsyncDisposable
{
    string DatabasePath { get; }

    Task CopyToAsync(string destinationPath, CancellationToken cancellationToken = default);
}

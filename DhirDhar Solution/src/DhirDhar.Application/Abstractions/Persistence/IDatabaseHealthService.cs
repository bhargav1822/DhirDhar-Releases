namespace DhirDhar.Application.Abstractions.Persistence;

/// <summary>
/// Checks the availability and integrity of the local database and returns structured
/// health information without leaking raw database exceptions to the UI layer.
/// </summary>
public interface IDatabaseHealthService
{
    Task<DatabaseHealthResult> CheckAsync(CancellationToken cancellationToken = default);
}

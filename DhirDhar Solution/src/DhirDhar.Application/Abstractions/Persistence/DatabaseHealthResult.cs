namespace DhirDhar.Application.Abstractions.Persistence;

/// <summary>
/// Structured health information about the local database. Never exposes raw database
/// exceptions to the UI; technical details are logged by the implementation instead.
/// </summary>
public sealed record DatabaseHealthResult(
    bool IsHealthy,
    string DatabasePath,
    bool FileExists,
    bool CanConnect,
    bool MigrationsAreApplied,
    bool CanRead,
    string? Error);

namespace DhirDhar.Application.Abstractions.Persistence;

/// <summary>
/// Abstraction over database initialization. Implemented by the infrastructure layer,
/// allowing the application and desktop layers to remain independent of the concrete provider.
/// </summary>
public interface IDatabaseInitializer
{
    Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default);
}

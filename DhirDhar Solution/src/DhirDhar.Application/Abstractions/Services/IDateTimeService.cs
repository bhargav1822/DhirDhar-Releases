namespace DhirDhar.Application.Abstractions.Services;

/// <summary>
/// Abstraction over time sources so domain and application logic can be
/// tested with deterministic time.
/// </summary>
public interface IDateTimeService
{
    DateTimeOffset Now { get; }

    DateTime UtcNow { get; }
}

using DhirDhar.Application.Abstractions.Services;

namespace DhirDhar.Infrastructure.Services;

/// <summary>
/// Provides real system time. The abstraction exists so tests can substitute deterministic time.
/// </summary>
public sealed class DateTimeService : IDateTimeService
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public DateTime UtcNow => DateTime.UtcNow;
}

namespace DhirDhar.Application.Audit;

public interface IAuditService
{
    Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntry>> GetAuditHistoryAsync(DateTime? fromDate, DateTime? toDate, string? action, string? entityType, string? result, CancellationToken cancellationToken = default);
}

public sealed record AuditEvent(
    string Action,
    string EntityType,
    string? EntityId,
    string Description,
    string Result,
    string? BeforeValue = null,
    string? AfterValue = null);

public sealed record AuditEntry(
    Guid AuditId,
    DateTime Timestamp,
    string Action,
    string EntityType,
    string? EntityId,
    string Description,
    string Result,
    string? BeforeValue,
    string? AfterValue);

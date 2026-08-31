namespace DhirDhar.Domain.Entities;

public sealed class AuditEntry : Common.AuditableEntity
{
    private AuditEntry()
    {
    }

    public AuditEntry(Guid id, DateTime timestamp, string action, string entityType, string? entityId, string description, string result, string? beforeValue, string? afterValue)
        : base(id)
    {
        Timestamp = timestamp;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Description = description;
        Result = result;
        BeforeValue = beforeValue;
        AfterValue = afterValue;
    }

    public DateTime Timestamp { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string? EntityId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string Result { get; private set; } = string.Empty;
    public string? BeforeValue { get; private set; }
    public string? AfterValue { get; private set; }
}

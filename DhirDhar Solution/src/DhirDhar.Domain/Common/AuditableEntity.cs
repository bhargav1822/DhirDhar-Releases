namespace DhirDhar.Domain.Common;

/// <summary>
/// Base class for entities that carry application timestamps.
/// CreatedAt is set at construction; UpdatedAt is refreshed by <see cref="Touch"/>.
/// </summary>
public abstract class AuditableEntity : Entity
{
    protected AuditableEntity()
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    protected AuditableEntity(Guid id)
        : base(id)
    {
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public DateTime CreatedAt { get; protected set; }

    public DateTime UpdatedAt { get; protected set; }

    protected void Touch()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}

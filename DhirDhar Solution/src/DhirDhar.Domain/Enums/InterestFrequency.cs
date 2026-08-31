namespace DhirDhar.Domain.Enums;

/// <summary>
/// How often interest is compounded or accrued. Designed as an open enum so future
/// frequencies can be added without changing the interest configuration structure.
/// The calculation formula itself is intentionally not part of this type.
/// </summary>
public enum InterestFrequency
{
    Monthly = 1
}

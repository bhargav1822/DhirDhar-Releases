namespace DhirDhar.Domain.Enums;

/// <summary>
/// Strongly typed borrower lifecycle status. Using a closed enum prevents invalid status values.
/// </summary>
public enum BorrowerStatus
{
    Active = 1,
    Inactive = 2,
    Closed = 3,
    Archived = 4
}

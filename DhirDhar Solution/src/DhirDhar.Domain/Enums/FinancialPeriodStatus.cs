namespace DhirDhar.Domain.Enums;

/// <summary>
/// Lifecycle status of a financial period. A closed period is immutable for reporting purposes.
/// </summary>
public enum FinancialPeriodStatus
{
    Open = 1,
    Closed = 2
}

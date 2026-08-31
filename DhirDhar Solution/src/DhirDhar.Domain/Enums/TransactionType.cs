namespace DhirDhar.Domain.Enums;

/// <summary>
/// The kind of a financial transaction. Designed as an open enum so additional
/// transaction types can be added in later phases.
/// </summary>
public enum TransactionType
{
    Deposit = 1,
    Withdrawal = 2
}

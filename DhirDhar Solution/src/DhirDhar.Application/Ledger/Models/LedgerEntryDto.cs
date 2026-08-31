using System;

namespace DhirDhar.Application.Ledger.Models;

public sealed record LedgerEntryDto(
    DateTime Date,
    string EventType,
    string Description,
    decimal? TransactionAmount,
    decimal? InterestAmount,
    decimal? ApplicableRate,
    decimal OpeningPrincipal,
    decimal ClosingPrincipal,
    string Reference,
    string Status)
{
    public string FormattedDate => Date.ToString("dd-MMM-yyyy");
    public string FormattedTransactionAmount => TransactionAmount.HasValue ? $"₹ {TransactionAmount.Value:N2}" : "-";
    public string FormattedDebitAmount => string.Equals(EventType, "Withdrawal", StringComparison.OrdinalIgnoreCase) && TransactionAmount.HasValue ? $"₹ {TransactionAmount.Value:N2}" : "-";
    public string FormattedCreditAmount => string.Equals(EventType, "Deposit", StringComparison.OrdinalIgnoreCase) && TransactionAmount.HasValue ? $"₹ {TransactionAmount.Value:N2}" : "-";
    public string FormattedInterestAmount => InterestAmount.HasValue && InterestAmount.Value > 0 ? $"₹ {InterestAmount.Value:N2}" : "-";
    public string FormattedOpeningPrincipal => $"₹ {OpeningPrincipal:N2}";
    public string FormattedClosingPrincipal => $"₹ {ClosingPrincipal:N2}";
    public string FormattedRate => ApplicableRate.HasValue ? $"{ApplicableRate.Value:N2}%" : "-";
    public bool IsWithdrawal => string.Equals(EventType, "Withdrawal", StringComparison.OrdinalIgnoreCase);
    public bool IsDeposit => string.Equals(EventType, "Deposit", StringComparison.OrdinalIgnoreCase);
    public bool IsInterest => string.Equals(EventType, "Interest", StringComparison.OrdinalIgnoreCase);
}

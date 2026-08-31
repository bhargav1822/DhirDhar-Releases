using System;
using System.Collections.Generic;
using DhirDhar.Application.Ledger.Models;

namespace DhirDhar.Application.Reports.Models;

public sealed record BorrowerStatementReport(
    string BorrowerNumber,
    string BorrowerName,
    string Contact,
    string AccountStatus,
    DateTime EntryDate,
    DateTime? ClosedDate,
    decimal InterestRate,
    DateTime FromDate,
    DateTime ToDate,
    decimal OpeningPrincipal,
    decimal TotalDeposits,
    decimal TotalWithdrawals,
    decimal TotalInterest,
    decimal FinalOutstanding,
    IReadOnlyList<LedgerEntryDto> FinancialHistory)
{
    public string FormattedTotalDeposits => $"₹ {TotalDeposits:N2}";
    public string FormattedTotalWithdrawals => $"₹ {TotalWithdrawals:N2}";
    public string FormattedTotalInterest => $"₹ {TotalInterest:N2}";
    public string FormattedFinalOutstanding => $"₹ {FinalOutstanding:N2}";
    public string FormattedOpeningPrincipal => $"₹ {OpeningPrincipal:N2}";
    public string FormattedInterestRate => $"{InterestRate:N2}% / mo";
    public string FormattedDateRange => $"{FromDate:dd-MMM-yyyy} to {ToDate:dd-MMM-yyyy}";
    public string FormattedEntryDate => EntryDate.ToString("dd-MMM-yyyy");
    public string FormattedClosedDate => ClosedDate.HasValue ? ClosedDate.Value.ToString("dd-MMM-yyyy") : "-";
    public bool IsClosed => string.Equals(AccountStatus, "Closed", StringComparison.OrdinalIgnoreCase) || string.Equals(AccountStatus, "Archived", StringComparison.OrdinalIgnoreCase) || ClosedDate.HasValue;
    public bool HasFinancialHistory => FinancialHistory != null && FinancialHistory.Count > 0;
}

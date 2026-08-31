using System;
using System.Collections.Generic;

namespace DhirDhar.Application.Reports.Models;

public sealed record TransactionReportItem(
    DateTime Date,
    string BorrowerNumber,
    string BorrowerName,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    string Description)
{
    public string FormattedDate => Date.ToString("dd-MMM-yyyy");
    public string FormattedAmount => $"₹ {Amount:N2}";
    public string FormattedBalanceAfter => $"₹ {BalanceAfter:N2}";
}

public sealed record TransactionReport(
    DateTime FromDate,
    DateTime ToDate,
    string TransactionTypeFilter,
    string BorrowerName,
    IReadOnlyList<TransactionReportItem> Items,
    decimal TotalDeposits,
    decimal TotalWithdrawals,
    decimal NetAmount)
{
    public string FormattedTotalDeposits => $"₹ {TotalDeposits:N2}";
    public string FormattedTotalWithdrawals => $"₹ {TotalWithdrawals:N2}";
    public string FormattedNetAmount => $"₹ {NetAmount:N2}";
}

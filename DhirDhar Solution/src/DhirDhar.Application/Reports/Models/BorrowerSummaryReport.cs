using System;
using System.Collections.Generic;

namespace DhirDhar.Application.Reports.Models;

public sealed record BorrowerSummaryItem(
    string BorrowerNumber,
    string BorrowerName,
    string Contact,
    decimal TotalWithdrawn,
    decimal TotalDeposited,
    decimal TotalInterest,
    decimal CurrentBalance,
    decimal TotalOutstanding,
    string Status,
    DateTime? LastActivityDate)
{
    public string FormattedTotalWithdrawn => $"₹ {TotalWithdrawn:N2}";
    public string FormattedTotalDeposited => $"₹ {TotalDeposited:N2}";
    public string FormattedTotalInterest => $"₹ {TotalInterest:N2}";
    public string FormattedCurrentBalance => $"₹ {CurrentBalance:N2}";
    public string FormattedTotalOutstanding => $"₹ {TotalOutstanding:N2}";
}

public sealed record BorrowerSummaryReport(
    DateTime GeneratedDate,
    int TotalBorrowers,
    int ActiveBorrowers,
    int InactiveBorrowers,
    int ClosedBorrowers,
    IReadOnlyList<BorrowerSummaryItem> Items,
    decimal TotalDeposits,
    decimal TotalWithdrawals,
    decimal TotalInterest,
    decimal TotalOutstanding)
{
    public string FormattedTotalDeposits => $"₹ {TotalDeposits:N2}";
    public string FormattedTotalWithdrawals => $"₹ {TotalWithdrawals:N2}";
    public string FormattedTotalInterest => $"₹ {TotalInterest:N2}";
    public string FormattedTotalOutstanding => $"₹ {TotalOutstanding:N2}";
}

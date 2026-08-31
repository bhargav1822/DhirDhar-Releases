using System;
using System.Collections.Generic;

namespace DhirDhar.Application.Reports.Models;

public sealed record OutstandingReportItem(
    string BorrowerNumber,
    string BorrowerName,
    string Contact,
    decimal Principal,
    decimal AccumulatedInterest,
    decimal Outstanding,
    string Status,
    DateTime? LastActivityDate)
{
    public string FormattedPrincipal => $"₹ {Principal:N2}";
    public string FormattedAccumulatedInterest => $"₹ {AccumulatedInterest:N2}";
    public string FormattedOutstanding => $"₹ {Outstanding:N2}";
    public string FormattedLastActivityDate => LastActivityDate.HasValue ? LastActivityDate.Value.ToString("dd-MMM-yyyy") : "-";
}

public sealed record OutstandingReport(
    DateTime GeneratedDate,
    IReadOnlyList<OutstandingReportItem> Items,
    decimal TotalPrincipal,
    decimal TotalInterest,
    decimal TotalOutstanding)
{
    public string FormattedTotalPrincipal => $"₹ {TotalPrincipal:N2}";
    public string FormattedTotalInterest => $"₹ {TotalInterest:N2}";
    public string FormattedTotalOutstanding => $"₹ {TotalOutstanding:N2}";
}

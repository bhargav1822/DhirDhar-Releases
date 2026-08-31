using System;
using System.Collections.Generic;

namespace DhirDhar.Application.Reports.Models;

public sealed record InterestReportSegment(
    DateTime StartDate,
    DateTime EndDate,
    string BorrowerName,
    decimal OpeningPrincipal,
    decimal Rate,
    int Days,
    int DaysInMonth,
    decimal Interest,
    string? TransactionType,
    decimal? ClosingPrincipal)
{
    public string FormattedStartDate => StartDate.ToString("dd-MMM-yyyy");
    public string FormattedEndDate => EndDate.ToString("dd-MMM-yyyy");
    public string FormattedOpeningPrincipal => $"₹ {OpeningPrincipal:N2}";
    public string FormattedRate => $"{Rate:N2}%";
    public string FormattedDays => Days.ToString();
    public string FormattedInterest => $"₹ {Interest:N2}";
    public string FormattedClosingPrincipal => ClosingPrincipal.HasValue ? $"₹ {ClosingPrincipal.Value:N2}" : "-";
}

public sealed record InterestReport(
    Guid? BorrowerId,
    string BorrowerName,
    DateTime CalculationStart,
    DateTime CalculationEnd,
    decimal OpeningPrincipal,
    decimal ClosingPrincipal,
    decimal TotalInterest,
    string AccountStatus,
    DateTime? ClosedDate,
    IReadOnlyList<InterestReportSegment> Segments)
{
    public string FormattedTotalInterest => $"₹ {TotalInterest:N2}";
}

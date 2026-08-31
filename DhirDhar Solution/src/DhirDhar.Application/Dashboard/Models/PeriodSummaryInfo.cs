namespace DhirDhar.Application.Dashboard.Models;

public sealed record PeriodSummaryInfo(
    decimal OpeningBalance,
    decimal NewLoans,
    decimal Payments,
    decimal ClosingBalance);

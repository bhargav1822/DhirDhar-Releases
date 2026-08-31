namespace DhirDhar.Application.Dashboard.Models;

public sealed record DashboardSummary(
    int TotalBorrowers,
    int ActiveBorrowers,
    int InactiveBorrowers,
    int ClosedBorrowers,
    decimal TotalDeposits,
    decimal TotalWithdrawals,
    decimal OutstandingAmount,
    decimal TotalInterest,
    IReadOnlyList<RecentTransactionSummary> RecentTransactions,
    PeriodSummaryInfo PeriodSummary,
    IReadOnlyList<HistoricalOutstandingPoint> HistoricalOutstanding)
{
    public int ArchivedBorrowers => ClosedBorrowers;

    public static DashboardSummary Empty => new(
        0, 0, 0, 0, 0m, 0m, 0m, 0m,
        Array.Empty<RecentTransactionSummary>(),
        new PeriodSummaryInfo(0m, 0m, 0m, 0m),
        Array.Empty<HistoricalOutstandingPoint>());
}

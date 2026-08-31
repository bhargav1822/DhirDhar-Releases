using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Dashboard.Models;

namespace DhirDhar.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<PeriodSummaryInfo> GetMonthlyPeriodSummaryAsync(int year, int month, CancellationToken cancellationToken = default);

    Task<PeriodSummaryInfo> GetYearlyPeriodSummaryAsync(int year, CancellationToken cancellationToken = default);

    Task<YearlyOutstandingChartData> GetYearlyChartDataAsync(int year, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetAvailableYearsAsync(CancellationToken cancellationToken = default);
}

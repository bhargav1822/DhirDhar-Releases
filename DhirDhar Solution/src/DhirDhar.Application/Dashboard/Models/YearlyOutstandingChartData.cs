using System;
using System.Collections.Generic;

namespace DhirDhar.Application.Dashboard.Models;

public sealed class YearlyOutstandingChartData
{
    public int Year { get; }
    public decimal MaxAmount { get; }
    public decimal MaxYAxisValue { get; }
    public IReadOnlyList<ChartYAxisTick> YAxisTicks { get; }
    public IReadOnlyList<MonthlyChartGroup> MonthlyGroups { get; }

    public YearlyOutstandingChartData(
        int year,
        decimal maxAmount,
        decimal maxYAxisValue,
        IReadOnlyList<ChartYAxisTick> yAxisTicks,
        IReadOnlyList<MonthlyChartGroup> monthlyGroups)
    {
        Year = year;
        MaxAmount = maxAmount;
        MaxYAxisValue = maxYAxisValue;
        YAxisTicks = yAxisTicks ?? Array.Empty<ChartYAxisTick>();
        MonthlyGroups = monthlyGroups ?? Array.Empty<MonthlyChartGroup>();
    }
}

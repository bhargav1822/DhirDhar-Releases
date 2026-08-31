using System.Collections.Generic;

namespace DhirDhar.Application.Dashboard.Models;

public sealed class MonthlyChartGroup
{
    public int Month { get; }
    public int Year { get; }
    public string MonthLabel { get; }
    public string FullMonthName { get; }
    public MonthlyChartBar NewLoansBar { get; }
    public MonthlyChartBar WithdrawalsBar { get; }
    public MonthlyChartBar DepositsBar { get; }
    public MonthlyChartBar InterestEarnedBar { get; }

    public IReadOnlyList<MonthlyChartBar> Bars { get; }

    public MonthlyChartGroup(
        int month,
        int year,
        string monthLabel,
        string fullMonthName,
        MonthlyChartBar newLoansBar,
        MonthlyChartBar withdrawalsBar,
        MonthlyChartBar depositsBar,
        MonthlyChartBar interestEarnedBar)
    {
        Month = month;
        Year = year;
        MonthLabel = monthLabel;
        FullMonthName = fullMonthName;
        NewLoansBar = newLoansBar;
        WithdrawalsBar = withdrawalsBar;
        DepositsBar = depositsBar;
        InterestEarnedBar = interestEarnedBar;
        Bars = new[] { newLoansBar, withdrawalsBar, depositsBar, interestEarnedBar };
    }
}

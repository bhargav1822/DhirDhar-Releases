using System;
using System.Collections.Generic;
using System.Linq;

namespace DhirDhar.Domain.Interest;

public sealed record InterestCalculationResult(
    Guid BorrowerId,
    DateTime CalculationStartDate,
    DateTime CalculationEndDate,
    decimal OpeningPrincipal,
    decimal ClosingPrincipal,
    decimal TotalInterest,
    string Status,
    bool IsClosed,
    DateTime? ClosedDate,
    IReadOnlyList<InterestCalculationSegment> Segments,
    decimal UncapitalizedInterest = 0m)
{
    public int CompletedMonths
    {
        get
        {
            if (CalculationStartDate >= CalculationEndDate) return 0;
            int totalMonths = (CalculationEndDate.Year - CalculationStartDate.Year) * 12 + (CalculationEndDate.Month - CalculationStartDate.Month);
            if (CalculationEndDate.Day < CalculationStartDate.Day)
            {
                totalMonths--;
                int daysInPrevMonth = DateTime.DaysInMonth(CalculationEndDate.AddMonths(-1).Year, CalculationEndDate.AddMonths(-1).Month);
                int extraDays = (daysInPrevMonth - CalculationStartDate.Day) + CalculationEndDate.Day;
                if ((decimal)extraDays / daysInPrevMonth >= 0.5m)
                {
                    totalMonths++;
                }
            }
            return Math.Max(0, totalMonths);
        }
    }

    public decimal MonthlyInterestRate => Segments.Count > 0 && Segments[0].ApplicableMonthlyRate > 0m ? Segments[0].ApplicableMonthlyRate : 3.0m;

    public decimal MonthlyInterest => Common.FinancialRounding.RoundInterest(ClosingPrincipal * (MonthlyInterestRate / 100m));

    public decimal TotalOutstanding => Common.FinancialRounding.RoundMonetary(ClosingPrincipal + UncapitalizedInterest);
}

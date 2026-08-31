namespace DhirDhar.Application.Dashboard.Models;

public sealed class HistoricalOutstandingPoint
{
    public string MonthLabel { get; }
    public decimal OutstandingAmount { get; }
    public double BarHeight { get; }

    public HistoricalOutstandingPoint(string monthLabel, decimal outstandingAmount, double barHeight)
    {
        MonthLabel = monthLabel;
        OutstandingAmount = outstandingAmount;
        BarHeight = barHeight;
    }
}
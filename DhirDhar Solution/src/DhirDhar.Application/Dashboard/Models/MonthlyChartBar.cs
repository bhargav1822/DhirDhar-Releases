namespace DhirDhar.Application.Dashboard.Models;

public sealed class MonthlyChartBar
{
    public string CategoryKey { get; }
    public string CategoryLabel { get; }
    public decimal Amount { get; }
    public string FormattedAmount { get; }
    public double BarHeight { get; }
    public string HexColor { get; }
    public string TooltipText { get; }

    public MonthlyChartBar(
        string categoryKey,
        string categoryLabel,
        decimal amount,
        string formattedAmount,
        double barHeight,
        string hexColor,
        string tooltipText)
    {
        CategoryKey = categoryKey;
        CategoryLabel = categoryLabel;
        Amount = amount;
        FormattedAmount = formattedAmount;
        BarHeight = barHeight;
        HexColor = hexColor;
        TooltipText = tooltipText;
    }
}

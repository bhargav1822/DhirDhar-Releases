namespace DhirDhar.Application.Dashboard.Models;

public sealed class ChartYAxisTick
{
    public decimal Value { get; }
    public string FormattedLabel { get; }
    public double NormalizedPosition { get; }

    public ChartYAxisTick(decimal value, string formattedLabel, double normalizedPosition)
    {
        Value = value;
        FormattedLabel = formattedLabel;
        NormalizedPosition = normalizedPosition;
    }
}

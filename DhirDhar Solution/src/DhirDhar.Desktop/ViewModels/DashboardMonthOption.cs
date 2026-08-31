namespace DhirDhar.Desktop.ViewModels;

public sealed class DashboardMonthOption
{
    public DashboardMonthOption(string label, int year, int month, bool isCurrentMonth)
    {
        Label = label;
        Year = year;
        Month = month;
        IsCurrentMonth = isCurrentMonth;
    }

    public string Label { get; }
    public int Year { get; }
    public int Month { get; }
    public bool IsCurrentMonth { get; }
}

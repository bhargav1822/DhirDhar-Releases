namespace DhirDhar.Application.Dashboard.Models;

public sealed record DashboardYearOption(
    string Label,
    int Year,
    bool IsCurrentYear);

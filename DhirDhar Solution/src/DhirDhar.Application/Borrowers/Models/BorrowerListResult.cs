namespace DhirDhar.Application.Borrowers.Models;

public sealed record BorrowerListResult(
    IReadOnlyList<BorrowerSummary> Items,
    int TotalCount,
    int Page,
    int PageSize);

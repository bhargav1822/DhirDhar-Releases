namespace DhirDhar.Application.Search.Models;

public sealed record SearchFilter(
    string? SearchTerm = null,
    string? BorrowerFilter = null,
    string? TransactionTypeFilter = null,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    string SortBy = "Date",
    bool SortDescending = true,
    int Page = 1,
    int PageSize = 50);

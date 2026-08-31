namespace DhirDhar.Application.Search.Models;

public sealed record SearchResultPage(
    IReadOnlyList<SearchResult> Items,
    int TotalCount,
    int Page,
    int PageSize);

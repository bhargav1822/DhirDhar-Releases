using DhirDhar.Application.Search.Models;

namespace DhirDhar.Application.Search;

public interface ISearchService
{
    Task<SearchResultPage> SearchAsync(SearchFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BorrowerSearchResult>> SearchBorrowersAsync(
        string? searchTerm,
        string? statusFilter,
        DateTime? entryDateFrom,
        DateTime? entryDateTo,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransactionSearchResult>> SearchTransactionsAsync(
        string? searchTerm,
        string? typeFilter,
        DateTime? fromDate,
        DateTime? toDate,
        decimal? minAmount,
        decimal? maxAmount,
        CancellationToken cancellationToken = default);
}

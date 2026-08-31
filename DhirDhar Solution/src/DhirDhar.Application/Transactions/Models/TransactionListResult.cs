namespace DhirDhar.Application.Transactions.Models;

public sealed record TransactionListResult(
    IReadOnlyList<TransactionSummary> Items,
    int TotalCount,
    int Page,
    int PageSize);

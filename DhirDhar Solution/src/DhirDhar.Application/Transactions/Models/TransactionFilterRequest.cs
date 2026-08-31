namespace DhirDhar.Application.Transactions.Models;

public sealed record TransactionFilterRequest(
    Guid? BorrowerId,
    TransactionTypeFilter TypeFilter,
    DateTime? StartDate,
    DateTime? EndDate,
    string? SearchTerm,
    int Page,
    int PageSize);

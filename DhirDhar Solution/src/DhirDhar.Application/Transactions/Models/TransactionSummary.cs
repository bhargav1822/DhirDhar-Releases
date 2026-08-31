namespace DhirDhar.Application.Transactions.Models;

public sealed record TransactionSummary(
    Guid Id,
    string BorrowerName,
    string TransactionType,
    decimal Amount,
    DateTime TransactionDate,
    string? Description,
    DateTime CreatedAt,
    string? BorrowerNumber = null,
    Guid? BorrowerId = null);

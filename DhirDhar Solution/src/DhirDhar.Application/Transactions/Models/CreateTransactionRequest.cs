namespace DhirDhar.Application.Transactions.Models;

public sealed record CreateTransactionRequest(
    Guid BorrowerId,
    Domain.Enums.TransactionType Type,
    decimal Amount,
    DateTime TransactionDate,
    string? Description);

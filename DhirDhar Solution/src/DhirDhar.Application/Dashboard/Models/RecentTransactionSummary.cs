namespace DhirDhar.Application.Dashboard.Models;

public sealed record RecentTransactionSummary(
    Guid Id,
    string Reference,
    string TransactionType,
    string TransactionTypeKey,
    decimal Amount,
    DateTime TransactionDate,
    string? Description,
    string BorrowerName);

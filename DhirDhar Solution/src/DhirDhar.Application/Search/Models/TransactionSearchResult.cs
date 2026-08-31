namespace DhirDhar.Application.Search.Models;

public sealed record TransactionSearchResult(
    Guid Id,
    DateTime TransactionDate,
    string BorrowerName,
    string Type,
    decimal Amount,
    string? Reference);

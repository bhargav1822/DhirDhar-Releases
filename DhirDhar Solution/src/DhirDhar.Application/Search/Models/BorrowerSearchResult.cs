namespace DhirDhar.Application.Search.Models;

public sealed record BorrowerSearchResult(
    Guid Id,
    string BorrowerNumber,
    string Name,
    string? Contact,
    string Status,
    decimal Outstanding,
    DateTime EntryDate,
    DateTime? LastTransactionDate);

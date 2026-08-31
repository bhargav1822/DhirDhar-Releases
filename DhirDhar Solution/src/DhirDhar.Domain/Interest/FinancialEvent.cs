namespace DhirDhar.Domain.Interest;

public sealed record FinancialEvent(
    DateTime Date,
    string Type,
    decimal Amount,
    string? Description,
    int SequenceOrder);

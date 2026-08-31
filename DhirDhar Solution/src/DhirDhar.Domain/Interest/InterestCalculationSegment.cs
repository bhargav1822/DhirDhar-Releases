namespace DhirDhar.Domain.Interest;

public sealed record InterestCalculationSegment(
    DateTime SegmentStartDate,
    DateTime SegmentEndDate,
    decimal OpeningPrincipal,
    decimal ApplicableMonthlyRate,
    int ElapsedDays,
    int DaysInMonth,
    decimal CalculatedInterest,
    string? TransactionType,
    decimal? TransactionAmount,
    decimal ClosingPrincipal);

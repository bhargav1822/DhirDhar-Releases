namespace DhirDhar.Domain.Interest;

public sealed record InterestRatePeriod(
    decimal MonthlyRatePercent,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo);

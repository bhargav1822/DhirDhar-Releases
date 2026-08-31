namespace DhirDhar.Domain.Common;

public static class FinancialRounding
{
    public const int MonetaryPrecision = 2;
    public const int InterestPrecision = 4;
    public const MidpointRounding RoundingMode = MidpointRounding.AwayFromZero;

    public static decimal RoundMonetary(decimal value)
    {
        return decimal.Round(value, MonetaryPrecision, RoundingMode);
    }

    public static decimal RoundInterest(decimal value)
    {
        return decimal.Round(value, InterestPrecision, RoundingMode);
    }
}

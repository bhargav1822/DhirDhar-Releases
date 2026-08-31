using DhirDhar.Domain.Common;

namespace DhirDhar.Domain.ValueObjects;

/// <summary>
/// A reusable money value. The amount is stored as a <see cref="decimal"/> (never float or
/// double), is rounded to a consistent scale, and rejects invalid/out-of-range amounts.
/// </summary>
public sealed class Money : ValueObject
{
    public const int Scale = 2;

    private const decimal MaxAbsAmount = 999_999_999_999.99m;

    private Money()
    {
    }

    private Money(decimal amount)
    {
        Amount = amount;
    }

    public decimal Amount { get; private set; }

    public bool IsPositive => Amount > 0m;

    public bool IsNegative => Amount < 0m;

    public bool IsZero => Amount == 0m;

    public static Money Zero => Create(0m);

    public static Money Create(decimal amount)
    {
        if (!decimal.TryParse(amount.ToString(System.Globalization.CultureInfo.InvariantCulture), out var parsed))
        {
            throw new DomainValidationException(new[] { "Money: amount is not a valid number." });
        }

        amount = parsed;

        var rounded = decimal.Round(amount, Scale, MidpointRounding.AwayFromZero);

        if (rounded < -MaxAbsAmount || rounded > MaxAbsAmount)
        {
            throw new DomainValidationException(new[] { $"Money: amount '{amount}' is outside the supported range." });
        }

        return new Money(rounded);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
    }
}

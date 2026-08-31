namespace DhirDhar.Domain.Common;

/// <summary>
/// Guards against invalid enum values (for example values produced by casting
/// an out-of-range integer), keeping strongly typed domain values valid.
/// </summary>
public static class EnumValidator
{
    public static void EnsureDefined<TEnum>(TEnum value, string memberName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(typeof(TEnum), value))
        {
            throw new DomainValidationException(new[]
            {
                $"{memberName}: value '{value}' is not a valid {typeof(TEnum).Name}."
            });
        }
    }
}

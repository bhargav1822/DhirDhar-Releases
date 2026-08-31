namespace DhirDhar.Domain.Common;

/// <summary>
/// Collects validation errors and throws a <see cref="DomainValidationException"/>
/// with structured errors when validation fails.
/// </summary>
public sealed class ValidationErrorCollector
{
    private readonly List<string> _errors = new();

    public IReadOnlyList<string> Errors => _errors;

    public bool HasErrors => _errors.Count > 0;

    public void Add(string member, string message)
    {
        _errors.Add($"{member}: {message}");
    }

    public void ThrowIfInvalid()
    {
        if (HasErrors)
        {
            throw new DomainValidationException(_errors);
        }
    }
}

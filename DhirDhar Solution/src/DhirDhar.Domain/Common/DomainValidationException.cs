namespace DhirDhar.Domain.Common;

/// <summary>
/// Raised when domain validation fails. Carries a structured list of
/// "member: message" errors so callers can react to the actual problems.
/// </summary>
public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(IReadOnlyList<string> errors)
        : base(string.Join(" ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

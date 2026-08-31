namespace DhirDhar.Application.Common.Exceptions;

/// <summary>
/// Represents an error caused by invalid input that violates application rules.
/// </summary>
public class ValidationException : Exception
{
    public ValidationException()
    {
    }

    public ValidationException(string message)
        : base(message)
    {
    }

    public ValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

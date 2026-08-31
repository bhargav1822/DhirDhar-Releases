namespace DhirDhar.Application.Common.Exceptions;

/// <summary>
/// Represents an error raised when a requested resource could not be found.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException()
    {
    }

    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

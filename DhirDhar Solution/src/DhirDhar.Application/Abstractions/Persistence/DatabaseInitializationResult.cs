namespace DhirDhar.Application.Abstractions.Persistence;

/// <summary>
/// Describes the outcome of a database initialization attempt. Success carries the resolved
/// database file path; failure carries a safe, user-presentable error message.
/// </summary>
public sealed record DatabaseInitializationResult(bool IsSuccess, string DatabasePath, string? Error)
{
    public bool IsFailure => !IsSuccess;

    public static DatabaseInitializationResult Success(string databasePath)
    {
        return new DatabaseInitializationResult(true, databasePath, null);
    }

    public static DatabaseInitializationResult Failure(string databasePath, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        return new DatabaseInitializationResult(false, databasePath, error);
    }
}

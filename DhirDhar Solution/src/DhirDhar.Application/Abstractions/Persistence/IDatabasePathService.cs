namespace DhirDhar.Application.Abstractions.Persistence;

/// <summary>
/// Resolves the locations used by the persistence and logging subsystems. Centralizing path
/// resolution avoids duplicating path logic across classes and keeps the application free of
/// hardcoded user paths.
/// </summary>
public interface IDatabasePathService
{
    /// <summary>The application-wide data directory (under the user's local application data folder).</summary>
    string ApplicationDataDirectory { get; }

    /// <summary>The directory that contains the database file.</summary>
    string DatabaseDirectory { get; }

    /// <summary>The full path to the database file.</summary>
    string DatabasePath { get; }

    /// <summary>The directory reserved for database backups.</summary>
    string BackupDirectory { get; }

    /// <summary>The directory used for application log files.</summary>
    string LogDirectory { get; }
}

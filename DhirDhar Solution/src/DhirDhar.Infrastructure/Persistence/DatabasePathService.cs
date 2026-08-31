using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DhirDhar.Infrastructure.Persistence;

/// <summary>
/// Resolves the locations used by the persistence and logging subsystems using the Windows
/// local application data folder. No username, drive letter or absolute user path is hardcoded.
/// </summary>
public sealed class DatabasePathService : IDatabasePathService
{
    public const string ApplicationFolderName = "DhirDhar Solution";
    public const string DataFolderName = "Data";
    public const string BackupFolderName = "Backup";
    public const string LogFolderName = "Logs";

    private readonly DatabaseOptions _databaseOptions;
    private readonly BackupOptions _backupOptions;

    public DatabasePathService(IOptions<DatabaseOptions> databaseOptions, IOptions<BackupOptions> backupOptions)
    {
        _databaseOptions = databaseOptions.Value;
        _backupOptions = backupOptions.Value;
    }

    public string ApplicationDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationFolderName);

    public string DatabaseDirectory =>
        !string.IsNullOrWhiteSpace(_databaseOptions.DatabasePath) && Path.IsPathRooted(_databaseOptions.DatabasePath)
            ? (Path.GetDirectoryName(Path.GetFullPath(_databaseOptions.DatabasePath)) ?? Path.Combine(ApplicationDataDirectory, DataFolderName))
            : Path.Combine(ApplicationDataDirectory, DataFolderName);

    public string DatabasePath
    {
        get
        {
            // An unconfigured path resolves to the default file name so diagnostics and the
            // database initializer can report a meaningful location. The initializer is the
            // gatekeeper that validates the configured value and fails safely.
            var configuredPath = string.IsNullOrWhiteSpace(_databaseOptions.DatabasePath)
                ? "DhirDhar.db"
                : _databaseOptions.DatabasePath;

            return Path.IsPathRooted(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.Combine(DatabaseDirectory, configuredPath);
        }
    }

    public string BackupDirectory =>
        Path.Combine(
            ApplicationDataDirectory,
            string.IsNullOrWhiteSpace(_backupOptions.Directory) ? BackupFolderName : _backupOptions.Directory);

    public string LogDirectory =>
        Path.Combine(ApplicationDataDirectory, LogFolderName);
}

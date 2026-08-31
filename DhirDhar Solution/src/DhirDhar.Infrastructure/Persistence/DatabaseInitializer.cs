using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DhirDhar.Infrastructure.Persistence;

/// <summary>
/// Initializes the local SQLite database at startup: resolves the location, ensures the data
/// directory exists, opens the database, checks the migration state, applies pending migrations
/// and confirms readiness. An existing database is never deleted or recreated.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDatabasePathService _pathService;
    private readonly DatabaseOptions _databaseOptions;
    private readonly IDbContextFactory<DhirDharDbContext> _dbContextFactory;
    private readonly DhirDhar.Application.Security.Keys.IKeyManagementService? _keyManagementService;
    private readonly DhirDhar.Application.Security.IEncryptionMigrationService? _migrationService;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IDatabasePathService pathService,
        IOptions<DatabaseOptions> databaseOptions,
        IDbContextFactory<DhirDharDbContext> dbContextFactory,
        ILogger<DatabaseInitializer> logger,
        DhirDhar.Application.Security.Keys.IKeyManagementService? keyManagementService = null,
        DhirDhar.Application.Security.IEncryptionMigrationService? migrationService = null)
    {
        _pathService = pathService;
        _databaseOptions = databaseOptions.Value;
        _dbContextFactory = dbContextFactory;
        _keyManagementService = keyManagementService;
        _migrationService = migrationService;
        _logger = logger;
    }

    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (string.IsNullOrWhiteSpace(_databaseOptions.Provider))
            {
                return DatabaseInitializationResult.Failure(_pathService.DatabasePath, "Database provider is not configured.");
            }

            if (!string.Equals(_databaseOptions.Provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseInitializationResult.Failure(
                    _pathService.DatabasePath,
                    $"Database provider '{_databaseOptions.Provider}' is not supported.");
            }

            if (string.IsNullOrWhiteSpace(_databaseOptions.DatabasePath))
            {
                return DatabaseInitializationResult.Failure(_pathService.DatabasePath, "Database path is not configured.");
            }

            _logger.LogInformation(
                "Database initialization started. Provider '{Provider}', file '{DatabaseFile}'.",
                _databaseOptions.Provider,
                _pathService.DatabasePath);

            var targetDir = Path.GetDirectoryName(_pathService.DatabasePath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            Directory.CreateDirectory(_pathService.DatabaseDirectory);
            _logger.LogInformation("Ensured database directory '{Directory}'.", _pathService.DatabaseDirectory);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            _logger.LogInformation("Migration state checked. {PendingCount} pending migration(s).", pending.Count);

            // Inspect existing table columns to synchronize migration history if schema was partially updated
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                await using var conn = dbContext.Database.GetDbConnection();
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(Borrowers);";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!reader.IsDBNull(1))
                    {
                        existingColumns.Add(reader.GetString(1));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query Borrowers PRAGMA table_info.");
            }

            if (pending.Count > 0)
            {
                if (pending.Contains("20260812074736_AddBorrowerLoanFields"))
                {
                    var newCols = new (string Name, string Sql)[]
                    {
                        ("BorrowerPhotoPath", "ALTER TABLE Borrowers ADD COLUMN BorrowerPhotoPath TEXT NULL;"),
                        ("OrnamentPhotoPath", "ALTER TABLE Borrowers ADD COLUMN OrnamentPhotoPath TEXT NULL;"),
                        ("LoanType", "ALTER TABLE Borrowers ADD COLUMN LoanType TEXT NULL;"),
                        ("OrnamentType", "ALTER TABLE Borrowers ADD COLUMN OrnamentType TEXT NULL;"),
                        ("OrnamentWeight", "ALTER TABLE Borrowers ADD COLUMN OrnamentWeight REAL NULL;"),
                        ("LoanAmount", "ALTER TABLE Borrowers ADD COLUMN LoanAmount TEXT NULL;"),
                        ("LoanDate", "ALTER TABLE Borrowers ADD COLUMN LoanDate TEXT NULL;")
                    };

                    bool hasAnyCol = newCols.Any(c => existingColumns.Contains(c.Name));
                    if (hasAnyCol)
                    {
                        foreach (var (colName, sql) in newCols)
                        {
                            if (!existingColumns.Contains(colName))
                            {
                                try
                                {
                                    await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
                                    existingColumns.Add(colName);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to add column '{ColumnName}' to Borrowers.", colName);
                                }
                            }
                        }

                        _logger.LogInformation("Recording migration '20260812074736_AddBorrowerLoanFields' in __EFMigrationsHistory.");
                        try
                        {
                            await dbContext.Database.ExecuteSqlRawAsync(
                                "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20260812074736_AddBorrowerLoanFields', '8.0.4');",
                                cancellationToken).ConfigureAwait(false);
                            pending.Remove("20260812074736_AddBorrowerLoanFields");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not insert migration 20260812074736_AddBorrowerLoanFields into __EFMigrationsHistory.");
                        }
                    }
                }

                if (pending.Contains("20260811101601_AddBorrowerProfileFields") && existingColumns.Contains("FatherName"))
                {
                    _logger.LogInformation("FatherName column already exists in Borrowers table. Recording migration '20260811101601_AddBorrowerProfileFields' in __EFMigrationsHistory.");
                    try
                    {
                        await dbContext.Database.ExecuteSqlRawAsync(
                            "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('20260811101601_AddBorrowerProfileFields', '8.0.4');",
                            cancellationToken).ConfigureAwait(false);
                        pending.Remove("20260811101601_AddBorrowerProfileFields");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not insert migration 20260811101601_AddBorrowerProfileFields into __EFMigrationsHistory.");
                    }
                }

                if (pending.Count > 0)
                {
                    _logger.LogInformation("Applying {PendingCount} migration(s): {MigrationNames}...", pending.Count, string.Join(", ", pending));
                    await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Migrations applied successfully.");
                }
                else
                {
                    _logger.LogInformation("All pending migration schemas were already present; marked as applied.");
                }
            }
            else
            {
                await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("No pending migrations; database schema is up to date.");
            }

            if (!existingColumns.Contains("InterestRate"))
            {
                try
                {
                    await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Borrowers ADD COLUMN InterestRate REAL NULL;", cancellationToken).ConfigureAwait(false);
                    existingColumns.Add("InterestRate");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add column 'InterestRate' to Borrowers.");
                }
            }

            if (!existingColumns.Contains("ClosedDate"))
            {
                try
                {
                    await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Borrowers ADD COLUMN ClosedDate TEXT NULL;", cancellationToken).ConfigureAwait(false);
                    existingColumns.Add("ClosedDate");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add column 'ClosedDate' to Borrowers.");
                }
            }

            if (!existingColumns.Contains("ClosingAmount"))
            {
                try
                {
                    await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Borrowers ADD COLUMN ClosingAmount REAL NULL;", cancellationToken).ConfigureAwait(false);
                    existingColumns.Add("ClosingAmount");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add column 'ClosingAmount' to Borrowers.");
                }
            }

            if (!existingColumns.Contains("ClosedAccruedInterest"))
            {
                try
                {
                    await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Borrowers ADD COLUMN ClosedAccruedInterest REAL NULL;", cancellationToken).ConfigureAwait(false);
                    existingColumns.Add("ClosedAccruedInterest");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add column 'ClosedAccruedInterest' to Borrowers.");
                }
            }

            // Ensure UserTextTranslations table exists for persistent multilingual data caching
            try
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    @"CREATE TABLE IF NOT EXISTS UserTextTranslations (
                        Id TEXT PRIMARY KEY NOT NULL,
                        SourceText TEXT NOT NULL,
                        SourceLanguage TEXT NOT NULL,
                        TargetLanguage TEXT NOT NULL,
                        TranslatedText TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_UserTextTranslations_SourceText_TargetLanguage ON UserTextTranslations(SourceText, TargetLanguage);
                    CREATE INDEX IF NOT EXISTS IX_UserTextTranslations_TranslatedText ON UserTextTranslations(TranslatedText);",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to ensure UserTextTranslations table in SQLite.");
            }

            // Migrate legacy Archived (4) status to Closed (3)
            try
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    "UPDATE Borrowers SET Status = 3 WHERE Status = 4;",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to migrate legacy Archived status to Closed in SQLite.");
            }

            // Migrate Business Profile and Borrower Numbers safely to the DJ sequence
            try
            {
                await MigrateBorrowerNumbersAndSettingsAsync(dbContext, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to migrate Business Profile and Borrower Numbers.");
            }

            // Configure SQLite RAM-optimized pragmas for maximum responsiveness
            try
            {
                await dbContext.Database.ExecuteSqlRawAsync(
                    @"PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA cache_size = -64000;
                    PRAGMA mmap_size = 268435456;
                    PRAGMA temp_store = MEMORY;",
                    cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Applied SQLite RAM-optimized performance pragmas (64MB Cache, 256MB MMAP, In-Memory Temp Store, WAL).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply SQLite performance pragmas.");
            }

            // Ensure performance indexes exist in SQLite for verified entity tables
            try
            {
                await EnsurePerformanceIndexesAsync(dbContext, _logger, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create performance indexes in SQLite.");
            }

            // Ensure data consistency: Borrower EntryDate must be on or before LoanDate
            try
            {
                var inconsistentBorrowers = await dbContext.Borrowers
                    .Where(b => b.LoanDate != null && b.EntryDate > b.LoanDate)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (inconsistentBorrowers.Count > 0)
                {
                    foreach (var b in inconsistentBorrowers)
                    {
                        if (b.LoanDate.HasValue && b.EntryDate > b.LoanDate.Value)
                        {
                            b.SetEntryDate(b.LoanDate.Value.Date);
                        }
                    }
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to align borrower entry dates with loan dates in SQLite.");
            }

            // Initialize Master Key and run E2EE migration if required
            if (_keyManagementService != null)
            {
                try
                {
                    await _keyManagementService.InitializeMasterKeyAsync(cancellationToken).ConfigureAwait(false);
                    if (_migrationService != null && await _migrationService.IsMigrationRequiredAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var migrationResult = await _migrationService.MigrateExistingDataAsync(cancellationToken).ConfigureAwait(false);
                        _logger.LogInformation("Automatic encryption migration completed with status: {Status}.", migrationResult.IsSuccess);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize or migrate encryption during database initialization.");
                }
            }

            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            if (!canConnect)
            {
                _logger.LogError("Database is not accessible after initialization.");
                return DatabaseInitializationResult.Failure(_pathService.DatabasePath, "Database is not accessible after initialization.");
            }

            _logger.LogInformation("Database initialization completed successfully. File '{DatabaseFile}'.", _pathService.DatabasePath);
            return DatabaseInitializationResult.Success(_pathService.DatabasePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var innerMsg = exception.InnerException?.Message;
            var detailedError = string.IsNullOrWhiteSpace(innerMsg)
                ? $"{exception.GetType().Name}: {exception.Message}"
                : $"{exception.GetType().Name}: {exception.Message} -> {innerMsg}";

            _logger.LogError(
                exception,
                "Database initialization failed. Path: '{DatabasePath}', Exception: '{ExceptionType}', Details: '{ErrorDetails}'",
                _pathService.DatabasePath,
                exception.GetType().FullName,
                detailedError);

            return DatabaseInitializationResult.Failure(_pathService.DatabasePath, detailedError);
        }
    }

    private async Task MigrateBorrowerNumbersAndSettingsAsync(DhirDharDbContext dbContext, CancellationToken cancellationToken)
    {
        // 1. Ensure Business.Name setting is populated
        var businessNameSetting = await dbContext.ApplicationSettings
            .FirstOrDefaultAsync(s => s.Key == DhirDhar.Infrastructure.Settings.SettingsService.BusinessNameKey, cancellationToken)
            .ConfigureAwait(false);

        if (businessNameSetting == null)
        {
            businessNameSetting = new Domain.Entities.ApplicationSetting(
                DhirDhar.Infrastructure.Settings.SettingsService.BusinessNameKey,
                Domain.Common.BusinessProfileHelper.DefaultBusinessName);
            dbContext.ApplicationSettings.Add(businessNameSetting);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var businessName = businessNameSetting?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(businessName))
        {
            businessName = Domain.Common.BusinessProfileHelper.DefaultBusinessName;
        }

        var prefix = Domain.Common.BorrowerNumberHelper.GeneratePrefixFromBusinessName(businessName);

        // 2. Safely initialize persistent sequence tracking from highest existing borrower number without modifying existing borrower numbers
        var allBorrowers = await dbContext.Borrowers
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        long maxSeq = 0;
        foreach (var b in allBorrowers)
        {
            if (Domain.Common.BorrowerNumberHelper.TryParseSequence(b.BorrowerNumber, prefix, out var seq))
            {
                if (seq > maxSeq) maxSeq = seq;
            }
        }

        var sequenceSettingKey = $"BorrowerSequence_{prefix}";
        var sequenceSetting = await dbContext.ApplicationSettings
            .FirstOrDefaultAsync(s => s.Key == sequenceSettingKey, cancellationToken)
            .ConfigureAwait(false);

        if (sequenceSetting == null)
        {
            if (maxSeq > 0)
            {
                dbContext.ApplicationSettings.Add(new Domain.Entities.ApplicationSetting(
                    sequenceSettingKey,
                    maxSeq.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        else if (long.TryParse(sequenceSetting.Value, out var storedSeq) && maxSeq > storedSeq)
        {
            sequenceSetting.UpdateValue(maxSeq.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Borrower sequence watermark initialized for prefix '{Prefix}' with highest sequence {MaxSeq}.", prefix, maxSeq);
    }

    private static async Task EnsurePerformanceIndexesAsync(
        DhirDharDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var indexDefinitions = new (string Table, string IndexName, string Columns)[]
        {
            ("Borrowers", "IX_Borrowers_Status", "Status"),
            ("Borrowers", "IX_Borrowers_BorrowerNumber", "BorrowerNumber"),
            ("Borrowers", "IX_Borrowers_Name", "Name"),
            ("Borrowers", "IX_Borrowers_Phone", "Phone"),
            ("Borrowers", "IX_Borrowers_AadharNumber", "AadharNumber"),
            ("Transactions", "IX_Transactions_BorrowerId", "BorrowerId"),
            ("Transactions", "IX_Transactions_OccurredOn", "OccurredOn"),
            ("Transactions", "IX_Transactions_Type", "Type"),
            ("Transactions", "IX_Transactions_FinancialPeriodId", "FinancialPeriodId"),
            ("Transactions", "IX_Transactions_BorrowerId_OccurredOn", "BorrowerId, OccurredOn"),
            ("AuditEntries", "IX_AuditEntries_Timestamp", "Timestamp"),
            ("AuditEntries", "IX_AuditEntries_EntityType_EntityId", "EntityType, EntityId"),
            ("AuditEntries", "IX_AuditEntries_Action", "Action"),
            ("UserTextTranslations", "IX_UserTextTranslations_Source_Target", "SourceText, TargetLanguage"),
            ("UserTextTranslations", "IX_UserTextTranslations_TranslatedText", "TranslatedText"),
        };

        var connection = dbContext.Database.GetDbConnection();
        bool openedHere = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            openedHere = true;
        }

        try
        {
            var tableColumnsCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (table, indexName, columns) in indexDefinitions)
            {
                if (!tableColumnsCache.TryGetValue(table, out var existingColumns))
                {
                    existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $"PRAGMA table_info({table});";
                    using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        existingColumns.Add(reader.GetString(1));
                    }
                    tableColumnsCache[table] = existingColumns;
                }

                if (existingColumns.Count == 0)
                {
                    continue; // Table does not exist in SQLite schema
                }

                var targetColumns = columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                bool allColumnsExist = targetColumns.All(col => existingColumns.Contains(col));

                if (allColumnsExist)
                {
                    try
                    {
                        var uniqueClause = indexName == "IX_Borrowers_BorrowerNumber" ? "UNIQUE " : "";
#pragma warning disable EF1002 // Dynamic DDL cannot use parameters; identifiers are hardcoded constants from static table
                        await dbContext.Database.ExecuteSqlRawAsync(
                            $"CREATE {uniqueClause}INDEX IF NOT EXISTS {indexName} ON {table}({columns});",
                            cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Index {IndexName} on {Table}({Columns}) could not be created.", indexName, table, columns);
                    }
                }
            }
        }
        finally
        {
            if (openedHere && connection.State == System.Data.ConnectionState.Open)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }
}


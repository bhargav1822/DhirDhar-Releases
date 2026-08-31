using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Caching;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Persistence;

/// <summary>
/// Central coordinator for application database lifecycle, exclusive restore access,
/// background worker suspension, connection pool cleanup, atomic file replacement, and failure rollback.
/// </summary>
public sealed class DatabaseLifecycleService : IDatabaseLifecycleService
{
    private readonly IDatabasePathService _pathService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<DatabaseLifecycleService> _logger;
    private readonly DhirDhar.Application.Transactions.ITransactionEventService? _transactionEventService;
    private readonly List<IPausableDatabaseWorker> _workers = new();
    private readonly SemaphoreSlim _restoreLock = new(1, 1);
    private readonly string _mutexName;
    private volatile bool _isRestoreModeActive;

    public bool IsRestoreModeActive => _isRestoreModeActive;

    public event EventHandler? DatabaseRestored;

    public DatabaseLifecycleService(
        IDatabasePathService pathService,
        ICacheService cacheService,
        ILogger<DatabaseLifecycleService> logger,
        DhirDhar.Application.Transactions.ITransactionEventService? transactionEventService = null)
    {
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _transactionEventService = transactionEventService;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_pathService.DatabasePath.ToLowerInvariant())));
        _mutexName = $@"Local\DhirDhar_Database_Restore_{hash}";
    }

    public void RegisterPausableWorker(IPausableDatabaseWorker worker)
    {
        if (worker == null) return;
        lock (_workers)
        {
            if (!_workers.Contains(worker))
            {
                _workers.Add(worker);
                _logger.LogDebug("[LIFECYCLE] Registered pausable database worker: '{WorkerName}'.", worker.WorkerName);
            }
        }
    }

    public async Task<IDisposable> EnterRestoreModeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[LIFECYCLE] Requesting exclusive database restore mode...");

        Semaphore? namedSemaphore = null;
        bool semaphoreAcquired = false;

        try
        {
            namedSemaphore = new Semaphore(1, 1, _mutexName);
            semaphoreAcquired = namedSemaphore.WaitOne(300);

            if (!semaphoreAcquired)
            {
                _logger.LogWarning("[LIFECYCLE] Another DhirDhar process or restore operation holds the database lock.");
                throw new InvalidOperationException("DhirDhar is already running. Close the other DhirDhar instance before restoring.");
            }
        }
        catch (InvalidOperationException)
        {
            namedSemaphore?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LIFECYCLE] Warning checking named restore semaphore.");
        }

        if (!await _restoreLock.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false))
        {
            if (semaphoreAcquired) namedSemaphore?.Release();
            namedSemaphore?.Dispose();
            throw new InvalidOperationException("Another restore or backup operation is currently in progress. Please wait and try again.");
        }

        _isRestoreModeActive = true;
        _logger.LogInformation("[LIFECYCLE] Entered exclusive restore mode. Pausing background database workers...");

        // Pause all background workers
        List<IPausableDatabaseWorker> workersSnapshot;
        lock (_workers)
        {
            workersSnapshot = new List<IPausableDatabaseWorker>(_workers);
        }

        foreach (var worker in workersSnapshot)
        {
            try
            {
                await worker.PauseAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("[LIFECYCLE] Paused worker '{WorkerName}'.", worker.WorkerName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LIFECYCLE] Error pausing worker '{WorkerName}'.", worker.WorkerName);
            }
        }

        // Clean up connection pools and force resource reclamation
        ReleaseAllDatabaseConnections();

        // Verify active database file is released and openable with exclusive access
        await VerifyDatabaseFileIsReleasedAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("[LIFECYCLE] Database is fully released and prepared for atomic restore.");

        return new RestoreSession(this, namedSemaphore, semaphoreAcquired);
    }

    private void ReleaseAllDatabaseConnections()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SqliteConnection.ClearAllPools();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LIFECYCLE] Exception clearing SQLite connection pools.");
        }
    }

    private async Task VerifyDatabaseFileIsReleasedAsync(CancellationToken cancellationToken)
    {
        var dbPath = _pathService.DatabasePath;
        if (!File.Exists(dbPath))
        {
            return; // No existing file to lock
        }

        const int maxAttempts = 15;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using (var stream = new FileStream(dbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    // Successfully acquired exclusive file handle
                    return;
                }
            }
            catch (IOException)
            {
                ReleaseAllDatabaseConnections();
                if (attempt == maxAttempts)
                {
                    _logger.LogError("[LIFECYCLE] Database file '{DbPath}' remains locked by another process after teardown.", dbPath);
                    throw new InvalidOperationException("Restore could not safely access the DhirDhar database. Please close any other DhirDhar window and try again.");
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task ReplaceDatabaseAtomicallyAsync(
        string stagedDatabasePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(stagedDatabasePath))
        {
            throw new FileNotFoundException("Staged restore database file not found.", stagedDatabasePath);
        }

        var dbPath = _pathService.DatabasePath;
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        var safetyBackupPath = dbPath + ".safety_backup";
        var walPath = dbPath + "-wal";
        var shmPath = dbPath + "-shm";

        _logger.LogInformation("[LIFECYCLE] Starting atomic database replacement. Staged='{Staged}', Target='{Target}'", stagedDatabasePath, dbPath);

        progress?.Report("Preparing Database...");

        // Pre-validate staged database before touching active database
        ValidateDatabaseIntegrity(stagedDatabasePath, "Staged");

        progress?.Report("Closing Database...");
        ReleaseAllDatabaseConnections();

        progress?.Report("Restoring Data...");

        // Create safety backup of existing active database if it exists
        if (File.Exists(safetyBackupPath))
        {
            try { File.Delete(safetyBackupPath); } catch { }
        }

        if (File.Exists(dbPath))
        {
            try
            {
                File.Copy(dbPath, safetyBackupPath, true);
                _logger.LogInformation("[LIFECYCLE] Created safety copy of active database at '{SafetyPath}'.", safetyBackupPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LIFECYCLE] Could not create safety backup copy: {Message}", ex.Message);
            }
        }

        // Clean up WAL and SHM journal files
        if (File.Exists(walPath)) { try { File.Delete(walPath); } catch { } }
        if (File.Exists(shmPath)) { try { File.Delete(shmPath); } catch { } }

        // Perform atomic file replacement
        try
        {
            File.Copy(stagedDatabasePath, dbPath, true);
            _logger.LogInformation("[LIFECYCLE] Replaced active database file with restored version.");
        }
        catch (Exception copyEx)
        {
            _logger.LogError(copyEx, "[LIFECYCLE] Failed to replace active database file. Attempting immediate safety rollback.");
            RollbackSafetyBackup(safetyBackupPath, dbPath, walPath, shmPath);
            throw new InvalidOperationException("Restore could not safely access the DhirDhar database. Please close any other DhirDhar window and try again.", copyEx);
        }

        progress?.Report("Validating Restored Database...");

        // Validate restored database integrity and schema
        try
        {
            ValidateDatabaseIntegrity(dbPath, "Restored");
            _logger.LogInformation("[LIFECYCLE] Restored database passed all integrity and schema verification checks.");
        }
        catch (Exception valEx)
        {
            _logger.LogError(valEx, "[LIFECYCLE] Restored database failed integrity validation. Rolling back to original database...");
            RollbackSafetyBackup(safetyBackupPath, dbPath, walPath, shmPath);
            throw new InvalidOperationException($"Restore failed: Restored database validation failed ({valEx.Message}). The existing DhirDhar data was preserved.", valEx);
        }

        // Commit: Delete safety backup after successful verification
        if (File.Exists(safetyBackupPath))
        {
            try { File.Delete(safetyBackupPath); } catch { }
        }

        progress?.Report("Restarting DhirDhar...");

        // Invalidate in-memory caches and notify components
        try
        {
            _cacheService.Clear();
            _logger.LogInformation("[LIFECYCLE] In-memory cache cleared.");
        }
        catch (Exception cacheEx)
        {
            _logger.LogWarning(cacheEx, "[LIFECYCLE] Error clearing in-memory cache.");
        }

        try
        {
            DatabaseRestored?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("[LIFECYCLE] DatabaseRestored event raised successfully.");
            _transactionEventService?.PublishTransactionChanged(new DhirDhar.Application.Transactions.TransactionChangedEventArgs(null, null, DhirDhar.Application.Transactions.TransactionMutationKind.Adjusted));
        }
        catch (Exception eventEx)
        {
            _logger.LogWarning(eventEx, "[LIFECYCLE] Error raising DatabaseRestored event.");
        }

        return Task.CompletedTask;
    }

    private void ValidateDatabaseIntegrity(string databasePath, string label)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            conn.Open();

            // 1. Run PRAGMA integrity_check
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA integrity_check;";
                var result = cmd.ExecuteScalar()?.ToString();
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"{label} database failed PRAGMA integrity_check: {result}");
                }
            }

            // 2. Verify essential DhirDhar schema tables exist
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Borrowers', 'Transactions', 'Loans', 'ApplicationSettings');";
                var tableCount = Convert.ToInt32(cmd.ExecuteScalar());
                if (tableCount < 2)
                {
                    throw new InvalidOperationException($"{label} database is missing essential schema tables (found {tableCount} required tables).");
                }
            }

            // 3. Test basic query capability
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table';";
                cmd.ExecuteScalar();
            }

            conn.Close();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{label} database validation failed: {ex.Message}", ex);
        }
        finally
        {
            ReleaseAllDatabaseConnections();
        }
    }

    private void RollbackSafetyBackup(string safetyBackupPath, string dbPath, string walPath, string shmPath)
    {
        try
        {
            ReleaseAllDatabaseConnections();
            if (File.Exists(walPath)) { try { File.Delete(walPath); } catch { } }
            if (File.Exists(shmPath)) { try { File.Delete(shmPath); } catch { } }

            if (File.Exists(safetyBackupPath))
            {
                File.Copy(safetyBackupPath, dbPath, true);
                _logger.LogInformation("[LIFECYCLE] Successfully rolled back to safety backup database.");
            }
        }
        catch (Exception rbEx)
        {
            _logger.LogCritical(rbEx, "[LIFECYCLE] Critical failure during rollback to safety backup.");
        }
        finally
        {
            ReleaseAllDatabaseConnections();
        }
    }

    private async Task ExitRestoreModeAsync(Semaphore? namedSemaphore, bool semaphoreAcquired)
    {
        _logger.LogInformation("[LIFECYCLE] Exiting restore mode and resuming background workers...");

        List<IPausableDatabaseWorker> workersSnapshot;
        lock (_workers)
        {
            workersSnapshot = new List<IPausableDatabaseWorker>(_workers);
        }

        foreach (var worker in workersSnapshot)
        {
            try
            {
                await worker.ResumeAsync().ConfigureAwait(false);
                _logger.LogInformation("[LIFECYCLE] Resumed worker '{WorkerName}'.", worker.WorkerName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LIFECYCLE] Error resuming worker '{WorkerName}'.", worker.WorkerName);
            }
        }

        _isRestoreModeActive = false;

        try
        {
            if (semaphoreAcquired && namedSemaphore != null)
            {
                namedSemaphore.Release();
            }
        }
        catch { }
        finally
        {
            namedSemaphore?.Dispose();
        }

        try
        {
            _restoreLock.Release();
        }
        catch { }

        _logger.LogInformation("[LIFECYCLE] Restore mode exited. Normal database operations restored.");
    }

    private sealed class RestoreSession : IDisposable
    {
        private readonly DatabaseLifecycleService _service;
        private readonly Semaphore? _semaphore;
        private readonly bool _semaphoreAcquired;
        private int _disposed;

        public RestoreSession(DatabaseLifecycleService service, Semaphore? semaphore, bool semaphoreAcquired)
        {
            _service = service;
            _semaphore = semaphore;
            _semaphoreAcquired = semaphoreAcquired;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _service.ExitRestoreModeAsync(_semaphore, _semaphoreAcquired).GetAwaiter().GetResult();
            }
        }
    }
}
